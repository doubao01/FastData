using System;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using FastUntility.Base;
using Xunit;

namespace FastData.Tests
{
    /// <summary>
    /// issue #13：BaseUrl 共享 HttpClient 的可变状态回归测试。
    ///
    /// 修复前，BaseUrl.http 的 getter 每次访问都会执行
    /// http.DefaultRequestHeaders.Connection.Add("keep-alive")，
    /// 在同一次进程里反复调用 GetUrl/PostUrl 会让请求头不断膨胀
    /// （Connection: keep-alive, keep-alive, ...）。
    ///
    /// 这里直接通过 InternalsVisibleTo("FastData.Tests") 访问 internal 的
    /// BaseUrl.http，不需要反射。
    /// </summary>
    public class BaseUrlSingletonTests
    {
        private const string KeepAlive = "keep-alive";

        /// <summary>
        /// 连续读取 http 属性 N 次，"keep-alive" 只能出现一次。
        /// 这正是修复前会失败的用例：修复前读 N 次就会 Add N 次。
        /// </summary>
        [Fact]
        public void Http_ReadPropertyManyTimes_KeepAliveIsAddedOnlyOnce()
        {
            // 第一次访问触发 lazy 工厂（keep-alive 在这里设置一次）
            BaseUrl.http.ShouldNotBeNull();

            const int repeat = 10;
            for (var i = 0; i < repeat; i++)
            {
                // getter 现在应当只返回实例，不产生任何副作用
                var client = BaseUrl.http;
                Assert.NotNull(client);
            }

            var keepAliveCount = BaseUrl.http.DefaultRequestHeaders.Connection
                .Count(value => string.Equals(value, KeepAlive, StringComparison.OrdinalIgnoreCase));

            Assert.Equal(1, keepAliveCount);
        }

        /// <summary>
        /// 强版本：Connection 头集合里除了唯一的 keep-alive 之外不应有任何残留项，
        /// 防止将来改成"先清空再 Add"之类的写法把其它 Connection 指令抹掉/重复。
        /// </summary>
        [Fact]
        public void Http_ConnectionHeader_ContainsExactlyOneKeepAliveEntry()
        {
            var client = BaseUrl.http;

            for (var i = 0; i < 20; i++)
            {
                BaseUrl.http.ShouldNotBeNull();
            }

            var connectionValues = client.DefaultRequestHeaders.Connection.ToArray();
            Assert.Single(connectionValues);
            Assert.True(
                string.Equals(KeepAlive, connectionValues[0], StringComparison.OrdinalIgnoreCase),
                "Connection 头应只包含 keep-alive，实际为：" + connectionValues[0]);
        }

        /// <summary>
        /// 单例语义未被破坏：多次读取必须返回同一个 HttpClient 实例。
        /// 注意 HttpClient 是懒加载的，这里断言的是"同一个引用"，而非"非 null"。
        /// </summary>
        [Fact]
        public void Http_ReadTwice_ReturnsSameInstance()
        {
            var first = BaseUrl.http;
            var second = BaseUrl.http;

            Assert.NotNull(first);
            Assert.Same(first, second);
            Assert.Equal(first.GetHashCode(), second.GetHashCode());
        }

        /// <summary>
        /// 并发读取不应破坏单例，也不应重复追加 keep-alive
        /// （修复前多线程同时请求会放大头部污染）。
        /// </summary>
        [Fact]
        public void Http_ParallelReads_KeepAliveStillSingleAndInstanceIsShared()
        {
            var expected = BaseUrl.http;
            var results = new HttpClient[32];

            Parallel.For(0, results.Length, index =>
            {
                results[index] = BaseUrl.http;
            });

            foreach (var client in results)
            {
                Assert.Same(expected, client);
            }

            var keepAliveCount = expected.DefaultRequestHeaders.Connection
                .Count(value => string.Equals(value, KeepAlive, StringComparison.OrdinalIgnoreCase));

            Assert.Equal(1, keepAliveCount);
        }

        /// <summary>
        /// getter 无副作用的最直接断言：读取前后 Connection 头内容完全一致。
        /// </summary>
        [Fact]
        public void Http_ReadingProperty_DoesNotMutateConnectionHeader()
        {
            var client = BaseUrl.http;
            var before = client.DefaultRequestHeaders.Connection.ToArray();

            for (var i = 0; i < 5; i++)
            {
                BaseUrl.http.ShouldNotBeNull();
            }

            var after = client.DefaultRequestHeaders.Connection.ToArray();

            Assert.Equal(before, after);
        }
    }

    /// <summary>
    /// 本地断言辅助方法（避免为一条 null 检查只写 Assert.NotNull 分散注意力）。
    /// </summary>
    internal static class BaseUrlTestAssertions
    {
        internal static void ShouldNotBeNull(this HttpClient client)
        {
            Assert.NotNull(client);
        }
    }
}
