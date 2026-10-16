using System;
using FastData.Context;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FastData.DevTools
{
    /// <summary>
    /// 日志聚合器
    /// </summary>
    public static class LogAggregator
    {
        private static readonly ConcurrentQueue<LogEntry> _logQueue = new ConcurrentQueue<LogEntry>();
        private static readonly List<ILogProvider> _providers = new List<ILogProvider>();
        private static readonly object _lock = new object();
        private static bool _isRunning;
        private static Task _processingTask;

        /// <summary>
        /// 添加日志提供者
        /// </summary>
        public static void AddProvider(ILogProvider provider)
        {
            lock (_lock)
            {
                _providers.Add(provider);
            }
        }

        /// <summary>
        /// 移除日志提供者
        /// </summary>
        public static void RemoveProvider(ILogProvider provider)
        {
            lock (_lock)
            {
                _providers.Remove(provider);
            }
        }

        /// <summary>
        /// 记录日志
        /// </summary>
        public static void Log(LogLevel level, string message, string category = null, Dictionary<string, object> properties = null)
        {
            var entry = new LogEntry
            {
                Timestamp = DateTime.Now,
                Level = level,
                Message = message,
                Category = category,
                Properties = properties ?? new Dictionary<string, object>()
            };

            _logQueue.Enqueue(entry);

            StartProcessing();
        }

        /// <summary>
        /// 记录信息日志
        /// </summary>
        public static void Info(string message, string category = null, Dictionary<string, object> properties = null)
        {
            Log(LogLevel.Info, message, category, properties);
        }

        /// <summary>
        /// 记录警告日志
        /// </summary>
        public static void Warning(string message, string category = null, Dictionary<string, object> properties = null)
        {
            Log(LogLevel.Warning, message, category, properties);
        }

        /// <summary>
        /// 记录错误日志
        /// </summary>
        public static void Error(string message, string category = null, Dictionary<string, object> properties = null)
        {
            Log(LogLevel.Error, message, category, properties);
        }

        /// <summary>
        /// 记录异常日志
        /// </summary>
        public static void Exception(Exception ex, string message = null, string category = null, Dictionary<string, object> properties = null)
        {
            var errorMessage = message ?? ex.Message;
            properties = properties ?? new Dictionary<string, object>();
            properties["ExceptionType"] = ex.GetType().Name;
            properties["StackTrace"] = ex.StackTrace;

            Log(LogLevel.Error, errorMessage, category, properties);
        }

        /// <summary>
        /// 查询日志
        /// </summary>
        public static List<LogEntry> QueryLogs(LogQuery query = null)
        {
            query = query ?? new LogQuery();

            var allLogs = GetAllLogs();

            return allLogs.Where(log =>
                (query.StartTime == null || log.Timestamp >= query.StartTime) &&
                (query.EndTime == null || log.Timestamp <= query.EndTime) &&
                (query.Level == null || log.Level == query.Level) &&
                (string.IsNullOrEmpty(query.Category) || log.Category == query.Category) &&
                (string.IsNullOrEmpty(query.Message) || log.Message.Contains(query.Message))
            ).OrderByDescending(l => l.Timestamp).ToList();
        }

        /// <summary>
        /// 获取日志统计
        /// </summary>
        public static LogStatistics GetStatistics()
        {
            var allLogs = GetAllLogs();

            return new LogStatistics
            {
                TotalLogs = allLogs.Count,
                InfoCount = allLogs.Count(l => l.Level == LogLevel.Info),
                WarningCount = allLogs.Count(l => l.Level == LogLevel.Warning),
                ErrorCount = allLogs.Count(l => l.Level == LogLevel.Error),
                Categories = allLogs.GroupBy(l => l.Category).ToDictionary(g => g.Key, g => g.Count()),
                TimeRange = allLogs.Any() ? new DateTimeRange
                {
                    StartTime = allLogs.Min(l => l.Timestamp),
                    EndTime = allLogs.Max(l => l.Timestamp)
                } : null
            };
        }

        /// <summary>
        /// 清理日志
        /// </summary>
        public static int CleanupLogs(TimeSpan olderThan)
        {
            var cutoff = DateTime.Now - olderThan;
            int cleanedCount = 0;

            lock (_lock)
            {
                foreach (var provider in _providers.OfType<MemoryLogProvider>())
                {
                    cleanedCount += provider.Cleanup(olderThan);
                }
            }

            return cleanedCount;
        }

        private static void StartProcessing()
        {
            lock (_lock)
            {
                if (_isRunning) return;
                _isRunning = true;
                _processingTask = ProcessLogsAsync();
            }
        }

        private static async Task ProcessLogsAsync()
        {
            while (_logQueue.TryDequeue(out var logEntry))
            {
                try
                {
                    ILogProvider[] providers;
                    lock (_lock)
                    {
                        providers = _providers.ToArray();
                    }

                    foreach (var provider in providers)
                    {
                        try
                        {
                            await provider.WriteLogAsync(logEntry);
                        }
                        catch
                        {
                            // 忽略单个提供者的错误
                        }
                    }
                }
                catch
                {
                    // 忽略处理错误
                }
            }

            lock (_lock)
            {
                if (_logQueue.IsEmpty)
                {
                    _isRunning = false;
                }
                else
                {
                    _processingTask = ProcessLogsAsync();
                }
            }
        }

        private static List<LogEntry> GetAllLogs()
        {
            var allLogs = new List<LogEntry>();

            lock (_lock)
            {
                foreach (var provider in _providers.OfType<MemoryLogProvider>())
                {
                    allLogs.AddRange(provider.GetLogs());
                }
            }

            return allLogs;
        }

        /// <summary>
        /// 停止日志处理
        /// </summary>
        public static async Task StopAsync()
        {
            // 处理所有剩余日志
            while (!_logQueue.IsEmpty)
            {
                await Task.Delay(100);
            }
        }
    }

    /// <summary>
    /// 日志提供者接口
    /// </summary>
    public interface ILogProvider
    {
        /// <summary>
        /// 异步写入日志
        /// </summary>
        /// <param name="entry">日志条目</param>
        /// <returns>异步任务</returns>
        Task WriteLogAsync(LogEntry entry);
    }

    /// <summary>
    /// 控制台日志提供者
    /// </summary>
    public class ConsoleLogProvider : ILogProvider
    {
        /// <summary>
        /// 异步写入日志到控制台
        /// </summary>
        /// <param name="entry">日志条目</param>
        /// <returns>异步任务</returns>
        public Task WriteLogAsync(LogEntry entry)
        {
            var color = Console.ForegroundColor;
            Console.ForegroundColor = GetColor(entry.Level);
            Console.WriteLine(string.Format("[{0:yyyy-MM-dd HH:mm:ss}] [{1}] {2}: {3}", entry.Timestamp, entry.Level, entry.Category, entry.Message));
            Console.ForegroundColor = color;
            return Task.CompletedTask;
        }

        private ConsoleColor GetColor(LogLevel level)
        {
            return level switch
            {
                LogLevel.Info => ConsoleColor.White,
                LogLevel.Warning => ConsoleColor.Yellow,
                LogLevel.Error => ConsoleColor.Red,
                _ => ConsoleColor.Gray
            };
        }
    }

    /// <summary>
    /// 文件日志提供者
    /// </summary>
    public class FileLogProvider : ILogProvider
    {
        private readonly string _logDirectory;
        private readonly string _filePattern;

        /// <summary>
        /// 初始化文件日志提供者
        /// </summary>
        /// <param name="logDirectory">日志目录</param>
        /// <param name="filePattern">文件名日期格式</param>
        public FileLogProvider(string logDirectory = "./logs", string filePattern = "yyyy-MM-dd")
        {
            _logDirectory = logDirectory;
            _filePattern = filePattern;

            if (!Directory.Exists(_logDirectory))
            {
                Directory.CreateDirectory(_logDirectory);
            }
        }

        /// <summary>
        /// 异步写入日志到文件
        /// </summary>
        /// <param name="entry">日志条目</param>
        /// <returns>异步任务</returns>
        public async Task WriteLogAsync(LogEntry entry)
        {
            var fileName = DateTime.Now.ToString(_filePattern) + ".log";
            var filePath = Path.Combine(_logDirectory, fileName);

            var logLine = FormatLogLine(entry);

            await File.AppendAllTextAsync(filePath, logLine + Environment.NewLine);
        }

        private string FormatLogLine(LogEntry entry)
        {
            var sb = new StringBuilder();
            sb.Append(string.Format("[{0:yyyy-MM-dd HH:mm:ss.fff}]", entry.Timestamp));
            sb.Append(string.Format(" [{0}]", entry.Level));
            if (!string.IsNullOrEmpty(entry.Category))
            {
                sb.Append(string.Format(" [{0}]", entry.Category));
            }
            sb.Append(string.Format(" {0}", entry.Message));

            if (entry.Properties != null && entry.Properties.Any())
            {
                sb.Append(string.Format(" | Properties: {0}", string.Join(", ", entry.Properties.Select(p => string.Format("{0}={1}", p.Key, p.Value)))));
            }

            return sb.ToString();
        }
    }

    /// <summary>
    /// 内存日志提供者
    /// </summary>
    public class MemoryLogProvider : ILogProvider
    {
        private readonly ConcurrentQueue<LogEntry> _logs = new ConcurrentQueue<LogEntry>();
        private readonly int _maxLogs;

        /// <summary>
        /// 初始化内存日志提供者
        /// </summary>
        /// <param name="maxLogs">最大日志条数</param>
        public MemoryLogProvider(int maxLogs = 10000)
        {
            _maxLogs = maxLogs;
        }

        /// <summary>
        /// 异步写入日志到内存
        /// </summary>
        /// <param name="entry">日志条目</param>
        /// <returns>异步任务</returns>
        public Task WriteLogAsync(LogEntry entry)
        {
            _logs.Enqueue(entry);

            // 限制日志数量
            while (_logs.Count > _maxLogs && _logs.TryDequeue(out _)) { }

            return Task.CompletedTask;
        }

        /// <summary>
        /// 获取所有日志
        /// </summary>
        /// <returns>日志列表</returns>
        public List<LogEntry> GetLogs()
        {
            return _logs.ToList();
        }

        /// <summary>
        /// 清理超过指定时间范围的日志
        /// </summary>
        /// <param name="olderThan">保留的时间范围</param>
        /// <returns>清理的日志条数</returns>
        public int Cleanup(TimeSpan olderThan)
        {
            var cutoff = DateTime.Now - olderThan;
            var currentLogs = GetLogs();
            var filteredLogs = currentLogs.Where(l => l.Timestamp >= cutoff).ToList();

            _logs.Clear();
            foreach (var log in filteredLogs)
            {
                _logs.Enqueue(log);
            }

            return currentLogs.Count - filteredLogs.Count;
        }
    }

    /// <summary>
    /// 日志条目
    /// </summary>
    public class LogEntry
    {
        /// <summary>
        /// 时间戳
        /// </summary>
        public DateTime Timestamp { get; set; }
        /// <summary>
        /// 日志级别
        /// </summary>
        public LogLevel Level { get; set; }
        /// <summary>
        /// 日志消息
        /// </summary>
        public string Message { get; set; }
        /// <summary>
        /// 日志分类
        /// </summary>
        public string Category { get; set; }
        /// <summary>
        /// 附加属性
        /// </summary>
        public Dictionary<string, object> Properties { get; set; }
    }

    /// <summary>
    /// 日志级别
    /// </summary>
    public enum LogLevel
    {
        /// <summary>
        /// 信息
        /// </summary>
        Info,
        /// <summary>
        /// 警告
        /// </summary>
        Warning,
        /// <summary>
        /// 错误
        /// </summary>
        Error
    }

    /// <summary>
    /// 日志查询
    /// </summary>
    public class LogQuery
    {
        /// <summary>
        /// 起始时间
        /// </summary>
        public DateTime? StartTime { get; set; }
        /// <summary>
        /// 结束时间
        /// </summary>
        public DateTime? EndTime { get; set; }
        /// <summary>
        /// 日志级别
        /// </summary>
        public LogLevel? Level { get; set; }
        /// <summary>
        /// 日志分类
        /// </summary>
        public string Category { get; set; }
        /// <summary>
        /// 消息关键字
        /// </summary>
        public string Message { get; set; }
    }

    /// <summary>
    /// 日志统计
    /// </summary>
    public class LogStatistics
    {
        /// <summary>
        /// 日志总数
        /// </summary>
        public int TotalLogs { get; set; }
        /// <summary>
        /// 信息日志数
        /// </summary>
        public int InfoCount { get; set; }
        /// <summary>
        /// 警告日志数
        /// </summary>
        public int WarningCount { get; set; }
        /// <summary>
        /// 错误日志数
        /// </summary>
        public int ErrorCount { get; set; }
        /// <summary>
        /// 各分类日志数量
        /// </summary>
        public Dictionary<string, int> Categories { get; set; }
        /// <summary>
        /// 时间范围
        /// </summary>
        public DateTimeRange TimeRange { get; set; }
    }

    /// <summary>
    /// 日期时间范围
    /// </summary>
    public class DateTimeRange
    {
        /// <summary>
        /// 起始时间
        /// </summary>
        public DateTime StartTime { get; set; }
        /// <summary>
        /// 结束时间
        /// </summary>
        public DateTime EndTime { get; set; }
    }

    /// <summary>
    /// 日志作用域
    /// </summary>
    public class LogScope : IDisposable
    {
        private readonly string _category;
        private readonly Dictionary<string, object> _properties;

        /// <summary>
        /// 初始化日志作用域
        /// </summary>
        /// <param name="category">日志分类</param>
        /// <param name="properties">附加属性</param>
        public LogScope(string category, Dictionary<string, object> properties = null)
        {
            _category = category;
            _properties = properties;
        }

        /// <summary>
        /// 记录信息日志
        /// </summary>
        public void Info(string message, Dictionary<string, object> additionalProperties = null)
        {
            var mergedProperties = MergeProperties(_properties, additionalProperties);
            LogAggregator.Info(message, _category, mergedProperties);
        }

        /// <summary>
        /// 记录警告日志
        /// </summary>
        public void Warning(string message, Dictionary<string, object> additionalProperties = null)
        {
            var mergedProperties = MergeProperties(_properties, additionalProperties);
            LogAggregator.Warning(message, _category, mergedProperties);
        }

        /// <summary>
        /// 记录错误日志
        /// </summary>
        public void Error(string message, Dictionary<string, object> additionalProperties = null)
        {
            var mergedProperties = MergeProperties(_properties, additionalProperties);
            LogAggregator.Error(message, _category, mergedProperties);
        }

        /// <summary>
        /// 记录异常日志
        /// </summary>
        public void Exception(Exception ex, string message = null, Dictionary<string, object> additionalProperties = null)
        {
            var mergedProperties = MergeProperties(_properties, additionalProperties);
            LogAggregator.Exception(ex, message, _category, mergedProperties);
        }

        private Dictionary<string, object> MergeProperties(Dictionary<string, object> props1, Dictionary<string, object> props2)
        {
            var merged = new Dictionary<string, object>(props1 ?? new Dictionary<string, object>());
            if (props2 != null)
            {
                foreach (var prop in props2)
                {
                    merged[prop.Key] = prop.Value;
                }
            }
            return merged;
        }

        /// <summary>
        /// 释放日志作用域
        /// </summary>
        public void Dispose()
        {
            // 可以在这里添加清理逻辑
        }
    }

    /// <summary>
    /// 日志工厂
    /// </summary>
    public static class LoggerFactory
    {
        /// <summary>
        /// 创建日志作用域
        /// </summary>
        /// <param name="category">日志分类</param>
        /// <param name="properties">附加属性</param>
        /// <returns>日志作用域</returns>
        public static LogScope CreateScope(string category, Dictionary<string, object> properties = null)
        {
            return new LogScope(category, properties);
        }
    }
}