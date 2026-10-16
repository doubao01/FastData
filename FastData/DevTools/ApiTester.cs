using System;
using FastData.Context;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using FastData.Model;

namespace FastData.DevTools
{
    /// <summary>
    /// API 测试工具
    /// </summary>
    public static class ApiTester
    {
        private static readonly HttpClient _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };

        /// <summary>
        /// 测试 API 端点
        /// </summary>
        public static async Task<ApiTestResult> TestEndpoint(string url, HttpMethod method = null, string body = null, Dictionary<string, string> headers = null)
        {
            var result = new ApiTestResult
            {
                Url = url,
                Method = method?.Method ?? "GET",
                StartTime = DateTime.Now
            };

            try
            {
                var request = new HttpRequestMessage(method ?? HttpMethod.Get, url);

                if (headers != null)
                {
                    foreach (var header in headers)
                    {
                        request.Headers.Add(header.Key, header.Value);
                    }
                }

                if (body != null && (method == HttpMethod.Post || method == HttpMethod.Put || method == HttpMethod.Patch))
                {
                    request.Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json");
                }

                var response = await _httpClient.SendAsync(request);
                result.EndTime = DateTime.Now;
                result.Duration = result.EndTime - result.StartTime;
                result.StatusCode = (int)response.StatusCode;
                result.IsSuccess = response.IsSuccessStatusCode;
                result.Content = await response.Content.ReadAsStringAsync();

                result.Headers = new Dictionary<string, string>();
                foreach (var header in response.Headers)
                {
                    result.Headers[header.Key] = string.Join(", ", header.Value);
                }
            }
            catch (Exception ex)
            {
                result.EndTime = DateTime.Now;
                result.Duration = result.EndTime - result.StartTime;
                result.Error = ex.Message;
                result.IsSuccess = false;
            }

            return result;
        }

        /// <summary>
        /// 批量测试 API 端点
        /// </summary>
        public static async Task<List<ApiTestResult>> TestEndpoints(List<ApiTestCase> testCases, bool parallel = true)
        {
            if (parallel)
            {
                var tasks = testCases.Select(tc => TestEndpoint(tc.Url, tc.Method, tc.Body, tc.Headers));
                return (await Task.WhenAll(tasks)).ToList();
            }
            else
            {
                var results = new List<ApiTestResult>();
                foreach (var testCase in testCases)
                {
                    var result = await TestEndpoint(testCase.Url, testCase.Method, testCase.Body, testCase.Headers);
                    results.Add(result);
                }
                return results;
            }
        }

        /// <summary>
        /// 压力测试
        /// </summary>
        public static async Task<LoadTestResult> LoadTest(string url, int requestCount, int concurrency = 1)
        {
            var result = new LoadTestResult
            {
                Url = url,
                RequestCount = requestCount,
                Concurrency = concurrency,
                StartTime = DateTime.Now
            };

            var results = new List<ApiTestResult>();
            var semaphore = new System.Threading.SemaphoreSlim(concurrency);

            var tasks = Enumerable.Range(0, requestCount).Select(async i =>
            {
                await semaphore.WaitAsync();
                try
                {
                    var testResult = await TestEndpoint(url);
                    lock (results)
                    {
                        results.Add(testResult);
                    }
                }
                finally
                {
                    semaphore.Release();
                }
            });

            await Task.WhenAll(tasks);

            result.EndTime = DateTime.Now;
            result.Duration = result.EndTime - result.StartTime;
            result.SuccessCount = results.Count(r => r.IsSuccess);
            result.FailureCount = results.Count(r => !r.IsSuccess);
            result.AverageResponseTime = results.Average(r => r.Duration.TotalMilliseconds);
            result.MinResponseTime = results.Min(r => r.Duration.TotalMilliseconds);
            result.MaxResponseTime = results.Max(r => r.Duration.TotalMilliseconds);
            result.RequestsPerSecond = result.SuccessCount / result.Duration.TotalSeconds;

            return result;
        }

        /// <summary>
        /// 生成测试报告
        /// </summary>
        public static ApiTestReport GenerateReport(List<ApiTestResult> results)
        {
            return new ApiTestReport
            {
                TotalTests = results.Count,
                SuccessCount = results.Count(r => r.IsSuccess),
                FailureCount = results.Count(r => !r.IsSuccess),
                AverageResponseTime = results.Where(r => r.IsSuccess).Average(r => r.Duration.TotalMilliseconds),
                MinResponseTime = results.Where(r => r.IsSuccess).Min(r => r.Duration.TotalMilliseconds),
                MaxResponseTime = results.Where(r => r.IsSuccess).Max(r => r.Duration.TotalMilliseconds),
                SuccessRate = (double)results.Count(r => r.IsSuccess) / results.Count * 100,
                StatusCodeDistribution = results.GroupBy(r => r.StatusCode)
                    .ToDictionary(g => g.Key, g => g.Count()),
                GeneratedAt = DateTime.Now
            };
        }

        /// <summary>
        /// 比较两个测试结果
        /// </summary>
        public static ApiTestComparison CompareResults(List<ApiTestResult> results1, List<ApiTestResult> results2, string label1, string label2)
        {
            var report1 = GenerateReport(results1);
            var report2 = GenerateReport(results2);

            return new ApiTestComparison
            {
                Label1 = label1,
                Label2 = label2,
                SuccessCount1 = report1.SuccessCount,
                SuccessCount2 = report2.SuccessCount,
                AverageResponseTime1 = report1.AverageResponseTime,
                AverageResponseTime2 = report2.AverageResponseTime,
                SuccessRate1 = report1.SuccessRate,
                SuccessRate2 = report2.SuccessRate,
                PerformanceImprovement = report1.AverageResponseTime - report2.AverageResponseTime,
                PerformanceImprovementPercentage = (report1.AverageResponseTime - report2.AverageResponseTime) / report1.AverageResponseTime * 100
            };
        }
    }

    /// <summary>
    /// 数据库监控工具
    /// </summary>
    public static class DatabaseMonitor
    {
        private static readonly Dictionary<string, MonitoringSession> _sessions = new Dictionary<string, MonitoringSession>();
        private static readonly object _lock = new object();

        /// <summary>
        /// 开始监控会话
        /// </summary>
        public static MonitoringSession StartMonitoring(string dbKey, MonitoringOptions options = null)
        {
            options = options ?? MonitoringOptions.Default;

            var session = new MonitoringSession
            {
                Id = Guid.NewGuid().ToString(),
                DbKey = dbKey,
                StartTime = DateTime.Now,
                Options = options,
                Queries = new List<QueryMetrics>()
            };

            lock (_lock)
            {
                _sessions[session.Id] = session;
            }

            return session;
        }

        /// <summary>
        /// 记录查询
        /// </summary>
        public static void RecordQuery(string sessionId, string sql, long durationMs, int rowsAffected = 0)
        {
            lock (_lock)
            {
                if (_sessions.TryGetValue(sessionId, out var session))
                {
                    session.Queries.Add(new QueryMetrics
                    {
                        Sql = sql,
                        DurationMs = durationMs,
                        RowsAffected = rowsAffected,
                        Timestamp = DateTime.Now
                    });

                    session.TotalQueries++;
                    session.TotalDuration += durationMs;

                    if (durationMs > session.MaxQueryDuration)
                    {
                        session.MaxQueryDuration = durationMs;
                        session.SlowestQuery = sql;
                    }

                    if (durationMs < session.MinQueryDuration || session.MinQueryDuration == 0)
                    {
                        session.MinQueryDuration = durationMs;
                    }
                }
            }
        }

        /// <summary>
        /// 停止监控会话
        /// </summary>
        public static MonitoringReport StopMonitoring(string sessionId)
        {
            lock (_lock)
            {
                if (_sessions.TryGetValue(sessionId, out var session))
                {
                    session.EndTime = DateTime.Now;
                    _sessions.Remove(sessionId);

                    return GenerateMonitoringReport(session);
                }
            }

            return null;
        }

        /// <summary>
        /// 获取监控会话
        /// </summary>
        public static MonitoringSession GetSession(string sessionId)
        {
            lock (_lock)
            {
                return _sessions.TryGetValue(sessionId, out var session) ? session : null;
            }
        }

        /// <summary>
        /// 获取所有活动会话
        /// </summary>
        public static List<MonitoringSession> GetActiveSessions()
        {
            lock (_lock)
            {
                return _sessions.Values.ToList();
            }
        }

        /// <summary>
        /// 生成监控报告
        /// </summary>
        public static MonitoringReport GenerateMonitoringReport(MonitoringSession session)
        {
            var queries = session.Queries;
            var duration = session.EndTime - session.StartTime;

            return new MonitoringReport
            {
                SessionId = session.Id,
                DbKey = session.DbKey,
                StartTime = session.StartTime,
                EndTime = session.EndTime,
                Duration = duration,
                TotalQueries = session.TotalQueries,
                TotalDuration = session.TotalDuration,
                AverageQueryDuration = session.TotalQueries > 0 ? session.TotalDuration / (double)session.TotalQueries : 0,
                MinQueryDuration = session.MinQueryDuration,
                MaxQueryDuration = session.MaxQueryDuration,
                SlowestQuery = session.SlowestQuery,
                SlowQueries = queries.Where(q => q.DurationMs > session.Options.SlowQueryThresholdMs).ToList(),
                QueriesPerSecond = duration.TotalSeconds > 0 ? session.TotalQueries / duration.TotalSeconds : 0,
                ErrorQueries = queries.Where(q => q.DurationMs < 0).ToList()
            };
        }

        /// <summary>
        /// 实时监控
        /// </summary>
        public static async Task<MonitoringReport> MonitorRealTime(string dbKey, TimeSpan duration, MonitoringOptions options = null)
        {
            var session = StartMonitoring(dbKey, options);

            await Task.Delay(duration);

            return StopMonitoring(session.Id);
        }

        /// <summary>
        /// 检测性能异常
        /// </summary>
        public static List<PerformanceAnomaly> DetectAnomalies(MonitoringReport report)
        {
            var anomalies = new List<PerformanceAnomaly>();

            // 检测慢查询
            if (report.SlowQueries.Any())
            {
                anomalies.Add(new PerformanceAnomaly
                {
                    Type = AnomalyType.SlowQuery,
                    Severity = AnomalySeverity.High,
                    Description = string.Format("检测到 {0} 个慢查询", report.SlowQueries.Count),
                    Details = report.SlowQueries.Select(q => q.Sql).ToList()
                });
            }

            // 检测平均查询时间异常
            if (report.AverageQueryDuration > 1000)
            {
                anomalies.Add(new PerformanceAnomaly
                {
                    Type = AnomalyType.HighLatency,
                    Severity = AnomalySeverity.Medium,
                    Description = string.Format("平均查询时间过长: {0:F2} ms", report.AverageQueryDuration),
                    Details = new List<string> { "阈值: 1000 ms" }
                });
            }

            // 检测查询频率异常
            if (report.QueriesPerSecond > 1000)
            {
                anomalies.Add(new PerformanceAnomaly
                {
                    Type = AnomalyType.HighFrequency,
                    Severity = AnomalySeverity.Medium,
                    Description = string.Format("查询频率过高: {0:F2} QPS", report.QueriesPerSecond),
                    Details = new List<string> { "阈值: 1000 QPS" }
                });
            }

            // 检测错误查询
            if (report.ErrorQueries.Any())
            {
                anomalies.Add(new PerformanceAnomaly
                {
                    Type = AnomalyType.QueryError,
                    Severity = AnomalySeverity.Critical,
                    Description = string.Format("检测到 {0} 个错误查询", report.ErrorQueries.Count),
                    Details = report.ErrorQueries.Select(q => q.Sql).ToList()
                });
            }

            return anomalies;
        }
    }

    #region 数据模型

    /// <summary>
    /// API 测试结果
    /// </summary>
    public class ApiTestResult
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
        /// 开始时间
        /// </summary>
        public DateTime StartTime { get; set; }
        /// <summary>
        /// 结束时间
        /// </summary>
        public DateTime EndTime { get; set; }
        /// <summary>
        /// 耗时
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
        /// 响应内容
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
    /// API 测试用例
    /// </summary>
    public class ApiTestCase
    {
        /// <summary>
        /// 请求地址
        /// </summary>
        public string Url { get; set; }
        /// <summary>
        /// HTTP 方法
        /// </summary>
        public HttpMethod Method { get; set; }
        /// <summary>
        /// 请求体
        /// </summary>
        public string Body { get; set; }
        /// <summary>
        /// 请求头
        /// </summary>
        public Dictionary<string, string> Headers { get; set; }
        /// <summary>
        /// 期望的状态码
        /// </summary>
        public string ExpectedStatusCode { get; set; }
    }

    /// <summary>
    /// 负载测试结果
    /// </summary>
    public class LoadTestResult
    {
        /// <summary>
        /// 请求地址
        /// </summary>
        public string Url { get; set; }
        /// <summary>
        /// 请求总数
        /// </summary>
        public int RequestCount { get; set; }
        /// <summary>
        /// 并发数
        /// </summary>
        public int Concurrency { get; set; }
        /// <summary>
        /// 开始时间
        /// </summary>
        public DateTime StartTime { get; set; }
        /// <summary>
        /// 结束时间
        /// </summary>
        public DateTime EndTime { get; set; }
        /// <summary>
        /// 总耗时
        /// </summary>
        public TimeSpan Duration { get; set; }
        /// <summary>
        /// 成功次数
        /// </summary>
        public int SuccessCount { get; set; }
        /// <summary>
        /// 失败次数
        /// </summary>
        public int FailureCount { get; set; }
        /// <summary>
        /// 平均响应时间（毫秒）
        /// </summary>
        public double AverageResponseTime { get; set; }
        /// <summary>
        /// 最小响应时间（毫秒）
        /// </summary>
        public double MinResponseTime { get; set; }
        /// <summary>
        /// 最大响应时间（毫秒）
        /// </summary>
        public double MaxResponseTime { get; set; }
        /// <summary>
        /// 每秒请求数
        /// </summary>
        public double RequestsPerSecond { get; set; }
    }

    /// <summary>
    /// API 测试报告
    /// </summary>
    public class ApiTestReport
    {
        /// <summary>
        /// 测试总数
        /// </summary>
        public int TotalTests { get; set; }
        /// <summary>
        /// 成功次数
        /// </summary>
        public int SuccessCount { get; set; }
        /// <summary>
        /// 失败次数
        /// </summary>
        public int FailureCount { get; set; }
        /// <summary>
        /// 平均响应时间（毫秒）
        /// </summary>
        public double AverageResponseTime { get; set; }
        /// <summary>
        /// 最小响应时间（毫秒）
        /// </summary>
        public double MinResponseTime { get; set; }
        /// <summary>
        /// 最大响应时间（毫秒）
        /// </summary>
        public double MaxResponseTime { get; set; }
        /// <summary>
        /// 成功率
        /// </summary>
        public double SuccessRate { get; set; }
        /// <summary>
        /// 状态码分布
        /// </summary>
        public Dictionary<int, int> StatusCodeDistribution { get; set; }
        /// <summary>
        /// 报告生成时间
        /// </summary>
        public DateTime GeneratedAt { get; set; }
    }

    /// <summary>
    /// API 测试比较
    /// </summary>
    public class ApiTestComparison
    {
        /// <summary>
        /// 第一组标签
        /// </summary>
        public string Label1 { get; set; }
        /// <summary>
        /// 第二组标签
        /// </summary>
        public string Label2 { get; set; }
        /// <summary>
        /// 第一组成功次数
        /// </summary>
        public int SuccessCount1 { get; set; }
        /// <summary>
        /// 第二组成功次数
        /// </summary>
        public int SuccessCount2 { get; set; }
        /// <summary>
        /// 第一组平均响应时间
        /// </summary>
        public double AverageResponseTime1 { get; set; }
        /// <summary>
        /// 第二组平均响应时间
        /// </summary>
        public double AverageResponseTime2 { get; set; }
        /// <summary>
        /// 第一组成功率
        /// </summary>
        public double SuccessRate1 { get; set; }
        /// <summary>
        /// 第二组成功率
        /// </summary>
        public double SuccessRate2 { get; set; }
        /// <summary>
        /// 性能提升值
        /// </summary>
        public double PerformanceImprovement { get; set; }
        /// <summary>
        /// 性能提升百分比
        /// </summary>
        public double PerformanceImprovementPercentage { get; set; }
    }

    /// <summary>
    /// 监控会话
    /// </summary>
    public class MonitoringSession
    {
        /// <summary>
        /// 会话 ID
        /// </summary>
        public string Id { get; set; }
        /// <summary>
        /// 数据库标识
        /// </summary>
        public string DbKey { get; set; }
        /// <summary>
        /// 开始时间
        /// </summary>
        public DateTime StartTime { get; set; }
        /// <summary>
        /// 结束时间
        /// </summary>
        public DateTime EndTime { get; set; }
        /// <summary>
        /// 监控选项
        /// </summary>
        public MonitoringOptions Options { get; set; }
        /// <summary>
        /// 已记录的查询列表
        /// </summary>
        public List<QueryMetrics> Queries { get; set; }
        /// <summary>
        /// 查询总数
        /// </summary>
        public int TotalQueries { get; set; }
        /// <summary>
        /// 累计耗时（毫秒）
        /// </summary>
        public long TotalDuration { get; set; }
        /// <summary>
        /// 最小查询耗时
        /// </summary>
        public double MinQueryDuration { get; set; }
        /// <summary>
        /// 最大查询耗时
        /// </summary>
        public long MaxQueryDuration { get; set; }
        /// <summary>
        /// 最慢的查询语句
        /// </summary>
        public string SlowestQuery { get; set; }
    }

    /// <summary>
    /// 监控选项
    /// </summary>
    public class MonitoringOptions
    {
        /// <summary>
        /// 慢查询阈值（毫秒）
        /// </summary>
        public int SlowQueryThresholdMs { get; set; } = 1000;
        /// <summary>
        /// 是否记录失败的查询
        /// </summary>
        public bool RecordFailedQueries { get; set; } = true;
        /// <summary>
        /// 最多记录的查询数量
        /// </summary>
        public int MaxQueriesToRecord { get; set; } = 10000;

        /// <summary>
        /// 默认选项
        /// </summary>
        public static MonitoringOptions Default => new MonitoringOptions();
    }

    /// <summary>
    /// 查询指标
    /// </summary>
    public class QueryMetrics
    {
        /// <summary>
        /// SQL 语句
        /// </summary>
        public string Sql { get; set; }
        /// <summary>
        /// 耗时（毫秒）
        /// </summary>
        public long DurationMs { get; set; }
        /// <summary>
        /// 受影响的行数
        /// </summary>
        public int RowsAffected { get; set; }
        /// <summary>
        /// 记录时间
        /// </summary>
        public DateTime Timestamp { get; set; }
    }

    /// <summary>
    /// 监控报告
    /// </summary>
    public class MonitoringReport
    {
        /// <summary>
        /// 会话 ID
        /// </summary>
        public string SessionId { get; set; }
        /// <summary>
        /// 数据库标识
        /// </summary>
        public string DbKey { get; set; }
        /// <summary>
        /// 开始时间
        /// </summary>
        public DateTime StartTime { get; set; }
        /// <summary>
        /// 结束时间
        /// </summary>
        public DateTime EndTime { get; set; }
        /// <summary>
        /// 总耗时
        /// </summary>
        public TimeSpan Duration { get; set; }
        /// <summary>
        /// 查询总数
        /// </summary>
        public int TotalQueries { get; set; }
        /// <summary>
        /// 累计耗时（毫秒）
        /// </summary>
        public long TotalDuration { get; set; }
        /// <summary>
        /// 平均查询耗时
        /// </summary>
        public double AverageQueryDuration { get; set; }
        /// <summary>
        /// 最小查询耗时
        /// </summary>
        public double MinQueryDuration { get; set; }
        /// <summary>
        /// 最大查询耗时
        /// </summary>
        public long MaxQueryDuration { get; set; }
        /// <summary>
        /// 最慢的查询语句
        /// </summary>
        public string SlowestQuery { get; set; }
        /// <summary>
        /// 慢查询列表
        /// </summary>
        public List<QueryMetrics> SlowQueries { get; set; }
        /// <summary>
        /// 每秒查询数
        /// </summary>
        public double QueriesPerSecond { get; set; }
        /// <summary>
        /// 错误查询列表
        /// </summary>
        public List<QueryMetrics> ErrorQueries { get; set; }
    }

    /// <summary>
    /// 性能异常
    /// </summary>
    public class PerformanceAnomaly
    {
        /// <summary>
        /// 异常类型
        /// </summary>
        public AnomalyType Type { get; set; }
        /// <summary>
        /// 严重程度
        /// </summary>
        public AnomalySeverity Severity { get; set; }
        /// <summary>
        /// 描述
        /// </summary>
        public string Description { get; set; }
        /// <summary>
        /// 详细信息
        /// </summary>
        public List<string> Details { get; set; }
    }

    /// <summary>
    /// 异常类型
    /// </summary>
    public enum AnomalyType
    {
        /// <summary>
        /// 慢查询
        /// </summary>
        SlowQuery,
        /// <summary>
        /// 高延迟
        /// </summary>
        HighLatency,
        /// <summary>
        /// 高频率
        /// </summary>
        HighFrequency,
        /// <summary>
        /// 查询错误
        /// </summary>
        QueryError,
        /// <summary>
        /// 连接泄漏
        /// </summary>
        ConnectionLeak
    }

    /// <summary>
    /// 异常严重程度
    /// </summary>
    public enum AnomalySeverity
    {
        /// <summary>
        /// 低
        /// </summary>
        Low,
        /// <summary>
        /// 中
        /// </summary>
        Medium,
        /// <summary>
        /// 高
        /// </summary>
        High,
        /// <summary>
        /// 严重
        /// </summary>
        Critical
    }

    #endregion
}