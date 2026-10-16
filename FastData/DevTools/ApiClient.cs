using System;
using FastData.Context;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using System.Text.Json;

namespace FastData.DevTools
{
    /// <summary>
    /// API 客户端工具
    /// </summary>
    public static class ApiClient
    {
        private static readonly HttpClient _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(30)
        };

        /// <summary>
        /// 发送 GET 请求
        /// </summary>
        public static async Task<ApiResponse<T>> GetAsync<T>(string url, Dictionary<string, string> headers = null)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, url);

            if (headers != null)
            {
                foreach (var header in headers)
                {
                    request.Headers.Add(header.Key, header.Value);
                }
            }

            return await SendAsync<T>(request);
        }

        /// <summary>
        /// 发送 POST 请求
        /// </summary>
        public static async Task<ApiResponse<T>> PostAsync<T>(string url, object data, Dictionary<string, string> headers = null)
        {
            var json = JsonSerializer.Serialize(data);
            var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

            var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = content };

            if (headers != null)
            {
                foreach (var header in headers)
                {
                    request.Headers.Add(header.Key, header.Value);
                }
            }

            return await SendAsync<T>(request);
        }

        /// <summary>
        /// 发送 PUT 请求
        /// </summary>
        public static async Task<ApiResponse<T>> PutAsync<T>(string url, object data, Dictionary<string, string> headers = null)
        {
            var json = JsonSerializer.Serialize(data);
            var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

            var request = new HttpRequestMessage(HttpMethod.Put, url) { Content = content };

            if (headers != null)
            {
                foreach (var header in headers)
                {
                    request.Headers.Add(header.Key, header.Value);
                }
            }

            return await SendAsync<T>(request);
        }

        /// <summary>
        /// 发送 DELETE 请求
        /// </summary>
        public static async Task<ApiResponse<T>> DeleteAsync<T>(string url, Dictionary<string, string> headers = null)
        {
            var request = new HttpRequestMessage(HttpMethod.Delete, url);

            if (headers != null)
            {
                foreach (var header in headers)
                {
                    request.Headers.Add(header.Key, header.Value);
                }
            }

            return await SendAsync<T>(request);
        }

        /// <summary>
        /// 发送 PATCH 请求
        /// </summary>
        public static async Task<ApiResponse<T>> PatchAsync<T>(string url, object data, Dictionary<string, string> headers = null)
        {
            var json = JsonSerializer.Serialize(data);
            var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

            var request = new HttpRequestMessage(new HttpMethod("PATCH"), url) { Content = content };

            if (headers != null)
            {
                foreach (var header in headers)
                {
                    request.Headers.Add(header.Key, header.Value);
                }
            }

            return await SendAsync<T>(request);
        }

        /// <summary>
        /// 发送自定义请求
        /// </summary>
        public static async Task<ApiResponse<T>> SendAsync<T>(HttpRequestMessage request)
        {
            var response = new ApiResponse<T>
            {
                Url = request.RequestUri.ToString(),
                Method = request.Method.Method,
                StartTime = DateTime.Now
            };

            try
            {
                var httpResponse = await _httpClient.SendAsync(request);
                response.EndTime = DateTime.Now;
                response.Duration = response.EndTime - response.StartTime;
                response.StatusCode = (int)httpResponse.StatusCode;
                response.IsSuccess = httpResponse.IsSuccessStatusCode;

                var content = await httpResponse.Content.ReadAsStringAsync();
                response.Content = content;

                response.Headers = new Dictionary<string, string>();
                foreach (var header in httpResponse.Headers)
                {
                    response.Headers[header.Key] = string.Join(", ", header.Value);
                }

                if (httpResponse.IsSuccessStatusCode && !string.IsNullOrEmpty(content))
                {
                    try
                    {
                        response.Data = JsonSerializer.Deserialize<T>(content);
                    }
                    catch
                    {
                        // 反序列化失败，返回原始内容
                    }
                }
            }
            catch (Exception ex)
            {
                response.EndTime = DateTime.Now;
                response.Duration = response.EndTime - response.StartTime;
                response.Error = ex.Message;
                response.IsSuccess = false;
            }

            return response;
        }

        /// <summary>
        /// 设置超时时间
        /// </summary>
        public static void SetTimeout(TimeSpan timeout)
        {
            _httpClient.Timeout = timeout;
        }

        /// <summary>
        /// 设置默认请求头
        /// </summary>
        public static void SetDefaultHeader(string name, string value)
        {
            _httpClient.DefaultRequestHeaders.Add(name, value);
        }

        /// <summary>
        /// 移除默认请求头
        /// </summary>
        public static void RemoveDefaultHeader(string name)
        {
            _httpClient.DefaultRequestHeaders.Remove(name);
        }

        /// <summary>
        /// 批量请求
        /// </summary>
        public static async Task<List<ApiResponse<T>>> BatchAsync<T>(List<ApiRequest> requests, bool parallel = true)
        {
            if (parallel)
            {
                var tasks = requests.Select(r =>
                {
                    var request = new HttpRequestMessage(new HttpMethod(r.Method), r.Url);
                    if (r.Data != null)
                    {
                        var json = JsonSerializer.Serialize(r.Data);
                        request.Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
                    }
                    if (r.Headers != null)
                    {
                        foreach (var header in r.Headers)
                        {
                            request.Headers.Add(header.Key, header.Value);
                        }
                    }
                    return SendAsync<T>(request);
                });

                return (await Task.WhenAll(tasks)).ToList();
            }
            else
            {
                var results = new List<ApiResponse<T>>();
                foreach (var r in requests)
                {
                    ApiResponse<T> result;
                    switch (r.Method.ToUpper())
                    {
                        case "GET":
                            result = await GetAsync<T>(r.Url, r.Headers);
                            break;
                        case "POST":
                            result = await PostAsync<T>(r.Url, r.Data, r.Headers);
                            break;
                        case "PUT":
                            result = await PutAsync<T>(r.Url, r.Data, r.Headers);
                            break;
                        case "DELETE":
                            result = await DeleteAsync<T>(r.Url, r.Headers);
                            break;
                        case "PATCH":
                            result = await PatchAsync<T>(r.Url, r.Data, r.Headers);
                            break;
                        default:
                            result = new ApiResponse<T> { IsSuccess = false, Error = string.Format("不支持的 HTTP 方法: {0}", r.Method) };
                            break;
                    }
                    results.Add(result);
                }
                return results;
            }
        }

        /// <summary>
        /// 重试请求
        /// </summary>
        public static async Task<ApiResponse<T>> RetryAsync<T>(Func<Task<ApiResponse<T>>> requestFunc, int maxRetries = 3, TimeSpan? delay = null)
        {
            delay = delay ?? TimeSpan.FromSeconds(1);

            for (int attempt = 1; attempt <= maxRetries; attempt++)
            {
                try
                {
                    var response = await requestFunc();
                    if (response.IsSuccess || attempt == maxRetries)
                    {
                        return response;
                    }

                    if (attempt < maxRetries)
                    {
                        await Task.Delay(delay.Value);
                    }
                }
                catch (Exception ex)
                {
                    if (attempt == maxRetries)
                    {
                        return new ApiResponse<T> { IsSuccess = false, Error = ex.Message };
                    }

                    await Task.Delay(delay.Value);
                }
            }

            return new ApiResponse<T> { IsSuccess = false, Error = "重试失败" };
        }

        /// <summary>
        /// 熔断器装饰器
        /// </summary>
        public static async Task<ApiResponse<T>> WithCircuitBreaker<T>(Func<Task<ApiResponse<T>>> requestFunc, CircuitBreakerOptions options = null)
        {
            options = options ?? CircuitBreakerOptions.Default;

            if (!CircuitBreakerState.CanExecute(options))
            {
                return new ApiResponse<T>
                {
                    IsSuccess = false,
                    Error = "熔断器已打开，请求被拒绝"
                };
            }

            try
            {
                var response = await requestFunc();

                if (response.IsSuccess)
                {
                    CircuitBreakerState.RecordSuccess(options);
                }
                else
                {
                    CircuitBreakerState.RecordFailure(options);
                }

                return response;
            }
            catch (Exception ex)
            {
                CircuitBreakerState.RecordFailure(options);
                return new ApiResponse<T> { IsSuccess = false, Error = ex.Message };
            }
        }
    }

    /// <summary>
    /// API 响应
    /// </summary>
    public class ApiResponse<T>
    {
        /// <summary>
        /// 请求地址
        /// </summary>
        public string Url { get; set; }
        /// <summary>
        /// HTTP 方法
        /// </summary>
        public string Method { get; set; }
        /// <summary>
        /// 请求开始时间
        /// </summary>
        public DateTime StartTime { get; set; }
        /// <summary>
        /// 请求结束时间
        /// </summary>
        public DateTime EndTime { get; set; }
        /// <summary>
        /// 请求耗时
        /// </summary>
        public TimeSpan Duration { get; set; }
        /// <summary>
        /// HTTP 状态码
        /// </summary>
        public int StatusCode { get; set; }
        /// <summary>
        /// 是否成功
        /// </summary>
        public bool IsSuccess { get; set; }
        /// <summary>
        /// 反序列化后的响应数据
        /// </summary>
        public T Data { get; set; }
        /// <summary>
        /// 原始响应内容
        /// </summary>
        public string Content { get; set; }
        /// <summary>
        /// 错误信息
        /// </summary>
        public string Error { get; set; }
        /// <summary>
        /// 响应头
        /// </summary>
        public Dictionary<string, string> Headers { get; set; }
    }

    /// <summary>
    /// API 请求
    /// </summary>
    public class ApiRequest
    {
        /// <summary>
        /// HTTP 方法
        /// </summary>
        public string Method { get; set; }
        /// <summary>
        /// 请求地址
        /// </summary>
        public string Url { get; set; }
        /// <summary>
        /// 请求数据
        /// </summary>
        public object Data { get; set; }
        /// <summary>
        /// 请求头
        /// </summary>
        public Dictionary<string, string> Headers { get; set; }
    }

    /// <summary>
    /// 熔断器状态
    /// </summary>
    internal static class CircuitBreakerState
    {
        private static readonly Dictionary<string, CircuitBreakerInfo> _circuitBreakers = new Dictionary<string, CircuitBreakerInfo>();
        private static readonly object _lock = new object();

        public static bool CanExecute(CircuitBreakerOptions options)
        {
            lock (_lock)
            {
                if (!_circuitBreakers.ContainsKey(options.Name))
                {
                    _circuitBreakers[options.Name] = new CircuitBreakerInfo();
                }

                var cb = _circuitBreakers[options.Name];

                // 如果熔断器已打开，检查是否可以半开
                if (cb.State == CircuitBreakerStateEnum.Open)
                {
                    if (DateTime.Now - cb.LastFailureTime > options.ResetTimeout)
                    {
                        cb.State = CircuitBreakerStateEnum.HalfOpen;
                        return true;
                    }
                    return false;
                }

                return true;
            }
        }

        public static void RecordSuccess(CircuitBreakerOptions options)
        {
            lock (_lock)
            {
                if (_circuitBreakers.TryGetValue(options.Name, out var cb))
                {
                    cb.FailureCount = 0;

                    if (cb.State == CircuitBreakerStateEnum.HalfOpen)
                    {
                        cb.State = CircuitBreakerStateEnum.Closed;
                    }
                }
            }
        }

        public static void RecordFailure(CircuitBreakerOptions options)
        {
            lock (_lock)
            {
                if (!_circuitBreakers.ContainsKey(options.Name))
                {
                    _circuitBreakers[options.Name] = new CircuitBreakerInfo();
                }

                var cb = _circuitBreakers[options.Name];
                cb.FailureCount++;
                cb.LastFailureTime = DateTime.Now;

                if (cb.FailureCount >= options.FailureThreshold)
                {
                    cb.State = CircuitBreakerStateEnum.Open;
                }
            }
        }
    }

    /// <summary>
    /// 熔断器信息
    /// </summary>
    internal class CircuitBreakerInfo
    {
        public CircuitBreakerStateEnum State { get; set; } = CircuitBreakerStateEnum.Closed;
        public int FailureCount { get; set; }
        public DateTime LastFailureTime { get; set; }
    }

    /// <summary>
    /// 熔断器状态枚举
    /// </summary>
    internal enum CircuitBreakerStateEnum
    {
        Closed,
        Open,
        HalfOpen
    }

    /// <summary>
    /// 熔断器选项
    /// </summary>
    public class CircuitBreakerOptions
    {
        /// <summary>
        /// 熔断器名称
        /// </summary>
        public string Name { get; set; } = "default";
        /// <summary>
        /// 触发熔断的失败阈值
        /// </summary>
        public int FailureThreshold { get; set; } = 5;
        /// <summary>
        /// 熔断后进入半开状态的等待时间
        /// </summary>
        public TimeSpan ResetTimeout { get; set; } = TimeSpan.FromMinutes(1);

        /// <summary>
        /// 默认选项
        /// </summary>
        public static CircuitBreakerOptions Default => new CircuitBreakerOptions();
    }

    /// <summary>
    /// RESTful API 客户端
    /// </summary>
    public class RestClient
    {
        private readonly string _baseUrl;
        private readonly Dictionary<string, string> _defaultHeaders;

        /// <summary>
        /// 初始化 RESTful 客户端
        /// </summary>
        /// <param name="baseUrl">基础地址</param>
        /// <param name="defaultHeaders">默认请求头</param>
        public RestClient(string baseUrl, Dictionary<string, string> defaultHeaders = null)
        {
            _baseUrl = baseUrl.TrimEnd('/');
            _defaultHeaders = defaultHeaders ?? new Dictionary<string, string>();
        }

        /// <summary>
        /// 发送 GET 请求
        /// </summary>
        public async Task<ApiResponse<T>> GetAsync<T>(string path, Dictionary<string, string> headers = null)
        {
            var url = string.Format("{0}/{1}", _baseUrl, path.TrimStart('/'));
            var mergedHeaders = MergeHeaders(_defaultHeaders, headers);
            return await ApiClient.GetAsync<T>(url, mergedHeaders);
        }

        /// <summary>
        /// 发送 POST 请求
        /// </summary>
        public async Task<ApiResponse<T>> PostAsync<T>(string path, object data, Dictionary<string, string> headers = null)
        {
            var url = string.Format("{0}/{1}", _baseUrl, path.TrimStart('/'));
            var mergedHeaders = MergeHeaders(_defaultHeaders, headers);
            return await ApiClient.PostAsync<T>(url, data, mergedHeaders);
        }

        /// <summary>
        /// 发送 PUT 请求
        /// </summary>
        public async Task<ApiResponse<T>> PutAsync<T>(string path, object data, Dictionary<string, string> headers = null)
        {
            var url = string.Format("{0}/{1}", _baseUrl, path.TrimStart('/'));
            var mergedHeaders = MergeHeaders(_defaultHeaders, headers);
            return await ApiClient.PutAsync<T>(url, data, mergedHeaders);
        }

        /// <summary>
        /// 发送 DELETE 请求
        /// </summary>
        public async Task<ApiResponse<T>> DeleteAsync<T>(string path, Dictionary<string, string> headers = null)
        {
            var url = string.Format("{0}/{1}", _baseUrl, path.TrimStart('/'));
            var mergedHeaders = MergeHeaders(_defaultHeaders, headers);
            return await ApiClient.DeleteAsync<T>(url, mergedHeaders);
        }

        /// <summary>
        /// 发送 PATCH 请求
        /// </summary>
        public async Task<ApiResponse<T>> PatchAsync<T>(string path, object data, Dictionary<string, string> headers = null)
        {
            var url = string.Format("{0}/{1}", _baseUrl, path.TrimStart('/'));
            var mergedHeaders = MergeHeaders(_defaultHeaders, headers);
            return await ApiClient.PatchAsync<T>(url, data, mergedHeaders);
        }

        private Dictionary<string, string> MergeHeaders(Dictionary<string, string> headers1, Dictionary<string, string> headers2)
        {
            var merged = new Dictionary<string, string>(headers1);
            if (headers2 != null)
            {
                foreach (var header in headers2)
                {
                    merged[header.Key] = header.Value;
                }
            }
            return merged;
        }
    }
}