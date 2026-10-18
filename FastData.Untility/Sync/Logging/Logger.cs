using System;
using System.IO;

namespace FastData.Tooling.Sync
{
    /// <summary>
    /// 日志级别
    /// </summary>
    public enum LogLevel
    {
        Debug,
        Info,
        Warn,
        Error
    }

    /// <summary>
    /// 同步工具日志记录器
    /// 
    /// 支持两种写入方式：
    /// 1. 全局默认文件（<see cref="Initialize"/> + <see cref="Info"/> 等）
    /// 2. 任务级文件（<see cref="Write"/> 传入 taskId，各任务互不干扰，适合并发同步场景）
    /// </summary>
    public static class Logger
    {
        private static string _logDirectory;
        private static string _logFile;
        private static LogLevel _minLevel = LogLevel.Info;
        private static readonly object LockObj = new object();

        /// <summary>
        /// 初始化日志记录器
        /// </summary>
        /// <param name="logDirectory">日志目录</param>
        /// <param name="minLevel">最低日志级别</param>
        public static void Initialize(string logDirectory, LogLevel minLevel = LogLevel.Info)
        {
            lock (LockObj)
            {
                _logDirectory = logDirectory;
                _minLevel = minLevel;

                if (!Directory.Exists(_logDirectory))
                {
                    Directory.CreateDirectory(_logDirectory);
                }

                var fileName = string.Format("sync_{0}.log", DateTime.Now.ToString("yyyyMMdd"));
                _logFile = Path.Combine(_logDirectory, fileName);
            }
        }

        /// <summary>
        /// 设置日志文件（按任务 ID）
        /// </summary>
        /// <param name="taskId">任务 ID</param>
        public static void SetLogFile(string taskId)
        {
            EnsureDirectory();

            lock (LockObj)
            {
                _logFile = Path.Combine(_logDirectory, BuildTaskLogFileName(taskId));
            }
        }

        /// <summary>
        /// 记录 Debug 级别日志
        /// </summary>
        public static void Debug(string message)
        {
            Write(LogLevel.Debug, message, null);
        }

        /// <summary>
        /// 记录 Info 级别日志
        /// </summary>
        public static void Info(string message)
        {
            Write(LogLevel.Info, message, null);
        }

        /// <summary>
        /// 记录 Warn 级别日志
        /// </summary>
        public static void Warn(string message)
        {
            Write(LogLevel.Warn, message, null);
        }

        /// <summary>
        /// 记录 Error 级别日志
        /// </summary>
        public static void Error(string message)
        {
            Write(LogLevel.Error, message, null);
        }

        /// <summary>
        /// 记录 Error 级别日志（带异常）
        /// </summary>
        public static void Error(string message, Exception ex)
        {
            Write(LogLevel.Error, string.Format("{0} {1}", message, ex.ToString()), null);
        }

        /// <summary>
        /// 按任务写入日志（并发安全，不切换全局日志文件）
        /// </summary>
        /// <param name="level">日志级别</param>
        /// <param name="message">日志内容</param>
        /// <param name="taskId">任务 ID（为空则写入默认文件）</param>
        public static void Write(LogLevel level, string message, string taskId)
        {
            EnsureDirectory();

            var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
            var levelStr = level.ToString().ToUpper().PadRight(5);
            var logLine = string.Format("[{0}] [{1}] {2}", timestamp, levelStr, message);

            lock (LockObj)
            {
                if (level < _minLevel)
                    return;

                var targetFile = string.IsNullOrWhiteSpace(taskId)
                    ? _logFile
                    : Path.Combine(_logDirectory, BuildTaskLogFileName(taskId));

                if (string.IsNullOrWhiteSpace(targetFile))
                {
                    targetFile = Path.Combine(_logDirectory, string.Format("sync_{0}.log", DateTime.Now.ToString("yyyyMMdd")));
                }

                try
                {
                    File.AppendAllText(targetFile, logLine + Environment.NewLine);
                }
                catch (Exception ex)
                {
                    Console.WriteLine("[LOGGER ERROR] Failed to write log: " + ex.Message);
                }

                if (_minLevel <= LogLevel.Debug)
                {
                    Console.WriteLine(logLine);
                }
            }
        }

        private static void EnsureDirectory()
        {
            lock (LockObj)
            {
                if (string.IsNullOrWhiteSpace(_logDirectory))
                {
                    _logDirectory = "./logs";
                }

                if (!Directory.Exists(_logDirectory))
                {
                    Directory.CreateDirectory(_logDirectory);
                }
            }
        }

        private static string BuildTaskLogFileName(string taskId)
        {
            return string.Format("sync_{0}_{1}.log", DateTime.Now.ToString("yyyyMMdd"), taskId);
        }
    }
}
