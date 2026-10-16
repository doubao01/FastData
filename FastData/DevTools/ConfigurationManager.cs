using System;
using FastData.Context;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace FastData.DevTools
{
    /// <summary>
    /// 配置管理器
    /// </summary>
    public static class ConfigurationManager
    {
        private static readonly Dictionary<string, object> _configurations = new Dictionary<string, object>();
        private static readonly object _lock = new object();
        private static string _configFilePath;

        static ConfigurationManager()
        {
            InitializeConfigPath();
        }

        private static void InitializeConfigPath()
        {
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            
            var activeEnv = GetActiveEnvironment();
            if (!string.IsNullOrEmpty(activeEnv))
            {
                var envConfigPath = Path.Combine(baseDir, string.Format("db.{0}.config", activeEnv));
                if (File.Exists(envConfigPath))
                {
                    _configFilePath = envConfigPath;
                    return;
                }
            }

            var defaultConfigPath = Path.Combine(baseDir, "db.config");
            if (File.Exists(defaultConfigPath))
            {
                _configFilePath = defaultConfigPath;
            }
            else
            {
                _configFilePath = Path.Combine(baseDir, "config", "appsettings.json");
            }
        }

        private static string GetActiveEnvironment()
        {
            try
            {
                var envVar = Environment.GetEnvironmentVariable("FASTDATA_ACTIVE");
                if (!string.IsNullOrEmpty(envVar))
                    return envVar.ToLower();

                var baseConfigPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "db.config");
                if (File.Exists(baseConfigPath))
                {
                    var content = File.ReadAllText(baseConfigPath);
                    var match = System.Text.RegularExpressions.Regex.Match(content, @"Active\s*=\s*[""']?([^""'\s/>]+)");
                    if (match.Success)
                        return match.Groups[1].Value.ToLower();
                }
            }
            catch { }
            
            return "dev";
        }

        /// <summary>
        /// 设置配置文件路径
        /// </summary>
        public static void SetConfigFilePath(string path)
        {
            _configFilePath = path;
        }

        /// <summary>
        /// 从文件加载配置
        /// </summary>
        public static void LoadFromFile(string filePath = null)
        {
            filePath = filePath ?? _configFilePath;

            if (!File.Exists(filePath))
            {
                LogAggregator.Warning(string.Format("配置文件不存在: {0}", filePath), "ConfigurationManager");
                return;
            }

            try
            {
                var json = File.ReadAllText(filePath);
                var config = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json);

                lock (_lock)
                {
                    foreach (var kvp in config)
                    {
                        _configurations[kvp.Key] = kvp.Value.ToString();
                    }
                }

                LogAggregator.Info(string.Format("配置加载成功: {0}", filePath), "ConfigurationManager");
            }
            catch (Exception ex)
            {
                LogAggregator.Exception(ex, string.Format("配置加载失败: {0}", filePath), "ConfigurationManager");
                throw;
            }
        }

        /// <summary>
        /// 保存配置到文件
        /// </summary>
        public static void SaveToFile(string filePath = null)
        {
            filePath = filePath ?? _configFilePath;

            try
            {
                var directory = Path.GetDirectoryName(filePath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var options = new JsonSerializerOptions { WriteIndented = true };
                var json = JsonSerializer.Serialize(_configurations, options);

                File.WriteAllText(filePath, json);

                LogAggregator.Info(string.Format("配置保存成功: {0}", filePath), "ConfigurationManager");
            }
            catch (Exception ex)
            {
                LogAggregator.Exception(ex, string.Format("配置保存失败: {0}", filePath), "ConfigurationManager");
                throw;
            }
        }

        /// <summary>
        /// 获取配置值
        /// </summary>
        public static T GetValue<T>(string key, T defaultValue = default)
        {
            lock (_lock)
            {
                if (_configurations.TryGetValue(key, out var value))
                {
                    try
                    {
                        if (typeof(T) == typeof(string))
                        {
                            return (T)(object)value.ToString();
                        }
                        else if (typeof(T) == typeof(int))
                        {
                            if (int.TryParse(value.ToString(), out var intValue))
                            {
                                return (T)(object)intValue;
                            }
                        }
                        else if (typeof(T) == typeof(bool))
                        {
                            if (bool.TryParse(value.ToString(), out var boolValue))
                            {
                                return (T)(object)boolValue;
                            }
                        }
                        else if (typeof(T) == typeof(double))
                        {
                            if (double.TryParse(value.ToString(), out var doubleValue))
                            {
                                return (T)(object)doubleValue;
                            }
                        }
                        else if (typeof(T) == typeof(TimeSpan))
                        {
                            if (TimeSpan.TryParse(value.ToString(), out var timeSpanValue))
                            {
                                return (T)(object)timeSpanValue;
                            }
                        }
                    }
                    catch
                    {
                        // 忽略转换错误
                    }
                }

                return defaultValue;
            }
        }

        /// <summary>
        /// 设置配置值
        /// </summary>
        public static void SetValue<T>(string key, T value)
        {
            lock (_lock)
            {
                _configurations[key] = value;
            }
        }

        /// <summary>
        /// 获取所有配置
        /// </summary>
        public static Dictionary<string, object> GetAll()
        {
            lock (_lock)
            {
                return new Dictionary<string, object>(_configurations);
            }
        }

        /// <summary>
        /// 检查配置是否存在
        /// </summary>
        public static bool HasKey(string key)
        {
            lock (_lock)
            {
                return _configurations.ContainsKey(key);
            }
        }

        /// <summary>
        /// 移除配置
        /// </summary>
        public static void Remove(string key)
        {
            lock (_lock)
            {
                _configurations.Remove(key);
            }
        }

        /// <summary>
        /// 清空所有配置
        /// </summary>
        public static void Clear()
        {
            lock (_lock)
            {
                _configurations.Clear();
            }
        }

        /// <summary>
        /// 批量设置配置
        /// </summary>
        public static void SetRange(Dictionary<string, object> configurations)
        {
            lock (_lock)
            {
                foreach (var kvp in configurations)
                {
                    _configurations[kvp.Key] = kvp.Value;
                }
            }
        }

        /// <summary>
        /// 获取配置前缀列表
        /// </summary>
        public static List<string> GetKeysByPrefix(string prefix)
        {
            lock (_lock)
            {
                return _configurations.Keys
                    .Where(k => k.StartsWith(prefix))
                    .ToList();
            }
        }

        /// <summary>
        /// 监听配置变化
        /// </summary>
        public static IDisposable Watch(string key, Action<object> onChange)
        {
            var watcher = new ConfigWatcher(key, onChange);
            watcher.Start();
            return watcher;
        }

        /// <summary>
        /// 配置观察者
        /// </summary>
        private class ConfigWatcher : IDisposable
        {
            private readonly string _key;
            private readonly Action<object> _onChange;
            private object _lastValue;
            private bool _disposed;

            public ConfigWatcher(string key, Action<object> onChange)
            {
                _key = key;
                _onChange = onChange;
                _lastValue = GetValue<object>(key);
            }

            public void Start()
            {
                System.Threading.Timer timer = null;
                timer = new System.Threading.Timer(state =>
                {
                    if (_disposed) return;

                    var currentValue = GetValue<object>(_key);
                    if (!Equals(currentValue, _lastValue))
                    {
                        _lastValue = currentValue;
                        try
                        {
                            _onChange(currentValue);
                        }
                        catch (Exception ex)
                        {
                            LogAggregator.Exception(ex, string.Format("配置变化监听器执行失败: {0}", _key), "ConfigurationManager");
                        }
                    }
                }, null, 1000, 1000);

                _timer = timer;
            }

            private System.Threading.Timer _timer;

            public void Dispose()
            {
                _disposed = true;
                _timer?.Dispose();
            }
        }
    }

    /// <summary>
    /// 健康检查工具
    /// </summary>
    public static class HealthChecker
    {
        private static readonly Dictionary<string, IHealthCheck> _healthChecks = new Dictionary<string, IHealthCheck>();

        /// <summary>
        /// 注册健康检查
        /// </summary>
        public static void RegisterHealthCheck(string name, IHealthCheck healthCheck)
        {
            _healthChecks[name] = healthCheck;
        }

        /// <summary>
        /// 移除健康检查
        /// </summary>
        public static void UnregisterHealthCheck(string name)
        {
            _healthChecks.Remove(name);
        }

        /// <summary>
        /// 执行所有健康检查
        /// </summary>
        public static HealthCheckResult CheckAll()
        {
            var results = new Dictionary<string, HealthCheckStatus>();

            foreach (var kvp in _healthChecks)
            {
                try
                {
                    var status = kvp.Value.CheckHealth();
                    results[kvp.Key] = status;
                }
                catch (Exception ex)
                {
                    results[kvp.Key] = new HealthCheckStatus
                    {
                        IsHealthy = false,
                        Status = "Error",
                        Message = ex.Message
                    };
                }
            }

            var overallHealthy = results.All(r => r.Value.IsHealthy);

            return new HealthCheckResult
            {
                IsHealthy = overallHealthy,
                Status = overallHealthy ? "Healthy" : "Unhealthy",
                Timestamp = DateTime.Now,
                Checks = results
            };
        }

        /// <summary>
        /// 执行单个健康检查
        /// </summary>
        public static HealthCheckStatus Check(string name)
        {
            if (!_healthChecks.TryGetValue(name, out var healthCheck))
            {
                return new HealthCheckStatus
                {
                    IsHealthy = false,
                    Status = "NotFound",
                    Message = string.Format("健康检查 '{0}' 不存在", name)
                };
            }

            try
            {
                return healthCheck.CheckHealth();
            }
            catch (Exception ex)
            {
                return new HealthCheckStatus
                {
                    IsHealthy = false,
                    Status = "Error",
                    Message = ex.Message
                };
            }
        }

        /// <summary>
        /// 异步执行所有健康检查
        /// </summary>
        public static async Task<HealthCheckResult> CheckAllAsync()
        {
            var results = new Dictionary<string, HealthCheckStatus>();

            var tasks = _healthChecks.Select(async kvp =>
            {
                try
                {
                    var status = await kvp.Value.CheckHealthAsync();
                    return (name: kvp.Key, status: status);
                }
                catch (Exception ex)
                {
                    return (name: kvp.Key, status: new HealthCheckStatus
                    {
                        IsHealthy = false,
                        Status = "Error",
                        Message = ex.Message
                    });
                }
            });

            var taskResults = await Task.WhenAll(tasks);

            foreach (var result in taskResults)
            {
                results[result.name] = result.status;
            }

            var overallHealthy = results.All(r => r.Value.IsHealthy);

            return new HealthCheckResult
            {
                IsHealthy = overallHealthy,
                Status = overallHealthy ? "Healthy" : "Unhealthy",
                Timestamp = DateTime.Now,
                Checks = results
            };
        }
    }

    /// <summary>
    /// 健康检查接口
    /// </summary>
    public interface IHealthCheck
    {
        /// <summary>
        /// 同步执行健康检查
        /// </summary>
        /// <returns>健康检查状态</returns>
        HealthCheckStatus CheckHealth();
        /// <summary>
        /// 异步执行健康检查
        /// </summary>
        /// <returns>健康检查状态</returns>
        Task<HealthCheckStatus> CheckHealthAsync();
    }

    /// <summary>
    /// 健康检查状态
    /// </summary>
    public class HealthCheckStatus
    {
        /// <summary>
        /// 是否健康
        /// </summary>
        public bool IsHealthy { get; set; }
        /// <summary>
        /// 状态文本
        /// </summary>
        public string Status { get; set; }
        /// <summary>
        /// 状态说明信息
        /// </summary>
        public string Message { get; set; }
        /// <summary>
        /// 附加数据
        /// </summary>
        public Dictionary<string, object> Data { get; set; } = new Dictionary<string, object>();
    }

    /// <summary>
    /// 健康检查结果
    /// </summary>
    public class HealthCheckResult
    {
        /// <summary>
        /// 整体是否健康
        /// </summary>
        public bool IsHealthy { get; set; }
        /// <summary>
        /// 整体状态文本
        /// </summary>
        public string Status { get; set; }
        /// <summary>
        /// 检查时间
        /// </summary>
        public DateTime Timestamp { get; set; }
        /// <summary>
        /// 各检查项的状态
        /// </summary>
        public Dictionary<string, HealthCheckStatus> Checks { get; set; } = new Dictionary<string, HealthCheckStatus>();
    }

    /// <summary>
    /// 常见健康检查实现
    /// </summary>
    public static class CommonHealthChecks
    {
        /// <summary>
        /// 数据库健康检查
        /// </summary>
        public class DatabaseHealthCheck : IHealthCheck
        {
            private readonly Func<bool> _checkFunc;

            /// <summary>
            /// 创建数据库健康检查
            /// </summary>
            /// <param name="checkFunc">返回数据库是否可用的委托</param>
            public DatabaseHealthCheck(Func<bool> checkFunc)
            {
                _checkFunc = checkFunc;
            }

            /// <summary>
            /// 同步执行数据库健康检查
            /// </summary>
            /// <returns>健康检查状态</returns>
            public HealthCheckStatus CheckHealth()
            {
                try
                {
                    var isHealthy = _checkFunc();
                    return new HealthCheckStatus
                    {
                        IsHealthy = isHealthy,
                        Status = isHealthy ? "Healthy" : "Unhealthy",
                        Message = isHealthy ? "数据库连接正常" : "数据库连接失败"
                    };
                }
                catch (Exception ex)
                {
                    return new HealthCheckStatus
                    {
                        IsHealthy = false,
                        Status = "Error",
                        Message = ex.Message
                    };
                }
            }

            /// <summary>
            /// 异步执行数据库健康检查
            /// </summary>
            /// <returns>健康检查状态</returns>
            public Task<HealthCheckStatus> CheckHealthAsync()
            {
                return Task.FromResult(CheckHealth());
            }
        }

        /// <summary>
        /// HTTP 健康检查
        /// </summary>
        public class HttpHealthCheck : IHealthCheck
        {
            private readonly string _url;
            private readonly TimeSpan _timeout;

            /// <summary>
            /// 创建 HTTP 健康检查
            /// </summary>
            /// <param name="url">健康检查地址</param>
            /// <param name="timeout">请求超时时间</param>
            public HttpHealthCheck(string url, TimeSpan timeout)
            {
                _url = url;
                _timeout = timeout;
            }

            /// <summary>
            /// 同步执行 HTTP 健康检查
            /// </summary>
            /// <returns>健康检查状态</returns>
            public HealthCheckStatus CheckHealth()
            {
                try
                {
                    using var client = new System.Net.Http.HttpClient { Timeout = _timeout };
                    var response = client.GetAsync(_url).GetAwaiter().GetResult();

                    return new HealthCheckStatus
                    {
                        IsHealthy = response.IsSuccessStatusCode,
                        Status = response.IsSuccessStatusCode ? "Healthy" : "Unhealthy",
                        Message = string.Format("HTTP 状态码: {0}", (int)response.StatusCode),
                        Data = new Dictionary<string, object>
                        {
                            ["StatusCode"] = (int)response.StatusCode,
                            ["ResponseTime"] = _timeout.TotalMilliseconds
                        }
                    };
                }
                catch (Exception ex)
                {
                    return new HealthCheckStatus
                    {
                        IsHealthy = false,
                        Status = "Error",
                        Message = ex.Message
                    };
                }
            }

            /// <summary>
            /// 异步执行 HTTP 健康检查
            /// </summary>
            /// <returns>健康检查状态</returns>
            public async Task<HealthCheckStatus> CheckHealthAsync()
            {
                try
                {
                    using var client = new System.Net.Http.HttpClient { Timeout = _timeout };
                    var response = await client.GetAsync(_url);

                    return new HealthCheckStatus
                    {
                        IsHealthy = response.IsSuccessStatusCode,
                        Status = response.IsSuccessStatusCode ? "Healthy" : "Unhealthy",
                        Message = string.Format("HTTP 状态码: {0}", (int)response.StatusCode),
                        Data = new Dictionary<string, object>
                        {
                            ["StatusCode"] = (int)response.StatusCode
                        }
                    };
                }
                catch (Exception ex)
                {
                    return new HealthCheckStatus
                    {
                        IsHealthy = false,
                        Status = "Error",
                        Message = ex.Message
                    };
                }
            }
        }

        /// <summary>
        /// 自定义健康检查
        /// </summary>
        public class CustomHealthCheck : IHealthCheck
        {
            private readonly Func<HealthCheckStatus> _checkFunc;

            /// <summary>
            /// 创建自定义健康检查
            /// </summary>
            /// <param name="checkFunc">返回健康检查状态的委托</param>
            public CustomHealthCheck(Func<HealthCheckStatus> checkFunc)
            {
                _checkFunc = checkFunc;
            }

            /// <summary>
            /// 同步执行自定义健康检查
            /// </summary>
            /// <returns>健康检查状态</returns>
            public HealthCheckStatus CheckHealth()
            {
                try
                {
                    return _checkFunc();
                }
                catch (Exception ex)
                {
                    return new HealthCheckStatus
                    {
                        IsHealthy = false,
                        Status = "Error",
                        Message = ex.Message
                    };
                }
            }

            /// <summary>
            /// 异步执行自定义健康检查
            /// </summary>
            /// <returns>健康检查状态</returns>
            public Task<HealthCheckStatus> CheckHealthAsync()
            {
                return Task.FromResult(CheckHealth());
            }
        }
    }
}