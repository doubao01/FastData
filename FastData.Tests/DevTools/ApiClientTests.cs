using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FastData.DevTools;
using Xunit;

namespace FastData.Tests.DevTools
{
    /// <summary>
    /// ApiClient 的端到端契约。
    /// <para>
    /// 背景：这个类 609 行、此前零测试。它真的会对外发 HTTP 请求，
    /// 出错比定时器更容易被忽略 —— 定时器不跑至少能被发现，HTTP 请求悄悄失败只会
    /// 让业务逻辑拿到一个「看起来正常但没数据」的响应。
    /// </para>
    /// <para>
    /// 用 <see cref="StubHttpServer"/> 起本地服务端，端到端验证真实收发，
    /// 不打桩、不 mock HttpClient。
    /// </para>
    /// </summary>
    public class ApiClientTests
    {
        private sealed class Payload
        {
            public int Id { get; set; }
            public string Name { get; set; }
        }

        /// <summary>
        /// 正常 200 + 合法 JSON：应成功且能反序列化出对象。
        /// </summary>
        [Fact]
        public async Task GetAsync_Success_ShouldDeserialize()
        {
            using (var server = new StubHttpServer(_ => StubHttpServer.Json("{\"Id\":7,\"Name\":\"abc\"}")))
            {
                var result = await ApiClient.GetAsync<Payload>(server.BaseUrl + "/api/x");

                Assert.True(result.IsSuccess, result.Error);
                Assert.Equal(200, result.StatusCode);
                Assert.NotNull(result.Data);
                Assert.Equal(7, result.Data.Id);
                Assert.Equal("abc", result.Data.Name);
            }
        }

        /// <summary>
        /// 关键回归：HTTP 200 但 JSON 格式不对时必须判失败。
        /// 原实现保持 IsSuccess=true、Data=null，调用方会把「响应格式不符」
        /// 误当成「服务端返回了空数据」。
        /// </summary>
        [Fact]
        public async Task GetAsync_MalformedJson_ShouldFailNotSilentlySucceed()
        {
            using (var server = new StubHttpServer(_ => StubHttpServer.Text("this-is-not-json")))
            {
                var result = await ApiClient.GetAsync<Payload>(server.BaseUrl + "/api/x");

                Assert.False(result.IsSuccess, "HTTP 200 + 非法 JSON 不应判成功");
                Assert.NotNull(result.Error);
                Assert.Contains("反序列化失败", result.Error);
                Assert.Null(result.Data);
                // 原始响应必须保留，便于排查
                Assert.Contains("this-is-not-json", result.Content);
            }
        }

        /// <summary>
        /// 5xx 应判失败并带上状态码。
        /// </summary>
        [Fact]
        public async Task GetAsync_ServerError_ShouldFail()
        {
            using (var server = new StubHttpServer(_ => StubHttpServer.Json("{}", 500, "Internal Server Error")))
            {
                var result = await ApiClient.GetAsync<Payload>(server.BaseUrl + "/api/x");

                Assert.False(result.IsSuccess);
                Assert.Equal(500, result.StatusCode);
            }
        }

        /// <summary>
        /// 404 同样应判失败。
        /// </summary>
        [Fact]
        public async Task GetAsync_NotFound_ShouldFail()
        {
            using (var server = new StubHttpServer(_ => StubHttpServer.Json("{}", 404, "Not Found")))
            {
                var result = await ApiClient.GetAsync<Payload>(server.BaseUrl + "/missing");

                Assert.False(result.IsSuccess);
                Assert.Equal(404, result.StatusCode);
            }
        }

        /// <summary>
        /// 连接不上时应返回失败并带错误信息，而不是抛异常。
        /// </summary>
        [Fact]
        public async Task GetAsync_UnreachableHost_ShouldReturnFailureNotThrow()
        {
            // 端口 1 基本不会有人监听
            var result = await ApiClient.GetAsync<Payload>("http://127.0.0.1:1/api/x");

            Assert.False(result.IsSuccess);
            Assert.False(string.IsNullOrEmpty(result.Error));
        }

        /// <summary>
        /// 耗时字段应被填充（StartTime / EndTime / Duration）。
        /// </summary>
        [Fact]
        public async Task GetAsync_ShouldRecordTiming()
        {
            using (var server = new StubHttpServer(_ => StubHttpServer.Json("{}")))
            {
                var result = await ApiClient.GetAsync<Payload>(server.BaseUrl + "/t");

                Assert.True(result.Duration >= TimeSpan.Zero);
                Assert.True(result.EndTime >= result.StartTime);
                Assert.False(string.IsNullOrEmpty(result.Url));
                Assert.False(string.IsNullOrEmpty(result.Method));
            }
        }

        /// <summary>
        /// POST 应真的把请求体发出去。
        /// </summary>
        [Fact]
        public async Task PostAsync_ShouldSendBody()
        {
            using (var server = new StubHttpServer(_ => StubHttpServer.Json("{\"Id\":1,\"Name\":\"n\"}")))
            {
                var result = await ApiClient.PostAsync<Payload>(
                    server.BaseUrl + "/api/create",
                    new { Title = "hello-body-marker" });

                Assert.True(result.IsSuccess, result.Error);

                lock (server.ReceivedRequests)
                {
                    Assert.NotEmpty(server.ReceivedRequests);
                    var raw = server.ReceivedRequests[0];
                    Assert.Contains("POST", raw);
                    Assert.Contains("hello-body-marker", raw);
                }
            }
        }

        /// <summary>
        /// 自定义头应被带上。
        /// </summary>
        [Fact]
        public async Task GetAsync_ShouldSendCustomHeaders()
        {
            using (var server = new StubHttpServer(_ => StubHttpServer.Json("{}")))
            {
                var headers = new Dictionary<string, string> { ["X-Trace-Id"] = "trace-marker-42" };
                await ApiClient.GetAsync<Payload>(server.BaseUrl + "/h", headers);

                lock (server.ReceivedRequests)
                {
                    Assert.Contains("trace-marker-42", server.ReceivedRequests[0]);
                }
            }
        }

        /// <summary>
        /// 响应头应被回传。
        /// </summary>
        [Fact]
        public async Task GetAsync_ShouldExposeResponseHeaders()
        {
            using (var server = new StubHttpServer(_ => StubHttpServer.Json("{}")))
            {
                var result = await ApiClient.GetAsync<Payload>(server.BaseUrl + "/r");

                Assert.NotNull(result.Headers);
                Assert.True(result.Headers.ContainsKey("Content-Type"));
            }
        }

        /// <summary>
        /// RetryAsync：前两次失败、第三次成功，应最终成功且只调用 3 次。
        /// </summary>
        [Fact]
        public async Task RetryAsync_ShouldRetryUntilSuccess()
        {
            var calls = 0;

            var result = await ApiClient.RetryAsync<Payload>(() =>
            {
                calls++;
                var ok = calls >= 3;
                return Task.FromResult(new ApiResponse<Payload>
                {
                    IsSuccess = ok,
                    Error = ok ? null : "boom"
                });
            }, maxRetries: 3, delay: TimeSpan.FromMilliseconds(10));

            Assert.True(result.IsSuccess);
            Assert.Equal(3, calls);
        }

        /// <summary>
        /// RetryAsync：一直失败时应在达到上限后返回失败，而不是无限重试。
        /// </summary>
        [Fact]
        public async Task RetryAsync_AlwaysFails_ShouldStopAtLimit()
        {
            var calls = 0;

            var result = await ApiClient.RetryAsync<Payload>(() =>
            {
                calls++;
                return Task.FromResult(new ApiResponse<Payload> { IsSuccess = false, Error = "always" });
            }, maxRetries: 3, delay: TimeSpan.FromMilliseconds(10));

            Assert.False(result.IsSuccess);
            Assert.True(calls <= 3 + 1, "重试次数超过上限，实际调用 " + calls + " 次");
        }

        /// <summary>
        /// RetryAsync：抛异常也应被当作失败并重试，而不是让异常冒出去。
        /// </summary>
        [Fact]
        public async Task RetryAsync_Exception_ShouldBeTreatedAsFailure()
        {
            var calls = 0;

            var result = await ApiClient.RetryAsync<Payload>(() =>
            {
                calls++;
                throw new InvalidOperationException("network gone");
            }, maxRetries: 2, delay: TimeSpan.FromMilliseconds(10));

            Assert.False(result.IsSuccess);
            Assert.True(calls >= 1, "应至少尝试一次");
        }

        /// <summary>
        /// BatchAsync：多个请求并行发出，顺序应与输入一致。
        /// </summary>
        [Fact]
        public async Task BatchAsync_ShouldReturnInInputOrder()
        {
            using (var server = new StubHttpServer(req =>
            {
                var firstLine = req.Split(new[] { "\r\n" }, StringSplitOptions.None)[0];
                if (firstLine.Contains("/a"))
                    return StubHttpServer.Json("{\"Id\":1,\"Name\":\"a\"}");
                if (firstLine.Contains("/b"))
                    return StubHttpServer.Json("{\"Id\":2,\"Name\":\"b\"}");
                return StubHttpServer.Json("{\"Id\":3,\"Name\":\"c\"}");
            }))
            {
                var requests = new List<ApiRequest>
                {
                    new ApiRequest { Url = server.BaseUrl + "/a", Method = "GET" },
                    new ApiRequest { Url = server.BaseUrl + "/b", Method = "GET" },
                    new ApiRequest { Url = server.BaseUrl + "/c", Method = "GET" }
                };

                var results = await ApiClient.BatchAsync<Payload>(requests, parallel: true);

                Assert.Equal(3, results.Count);
                Assert.Equal("a", results[0].Data.Name);
                Assert.Equal("b", results[1].Data.Name);
                Assert.Equal("c", results[2].Data.Name);
            }
        }
    }
}