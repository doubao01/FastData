using System;
using System.IO;
using System.Linq;
using System.Threading;

namespace FastUntility.Base
{
    /// <summary>
    /// 日志管理器 - 支持按大小轮转、按日期轮转、自动清理过期日志
    /// 
    /// 功能说明：
    /// 1. 按日期轮转：每天自动创建新的日志目录（格式：yyyy-MM-dd）
    /// 2. 按大小轮转：当日志文件超过指定大小时，自动创建新文件（后缀 _1, _2, ...）
    /// 3. 自动清理：保留最近 N 天的日志，自动删除过期日志
    /// 
    /// 目录结构示例：
    ///   App_Data/log/
    ///     2026-05-30/
    ///       app.txt          (当前日志文件)
    ///       app_1.txt        (轮转后的文件)
    ///       app_2.txt
    ///     2026-05-29/
    ///       app.txt
    /// </summary>
    public static class LogManager
    {
        private static readonly object _cleanLock = new object();
        private static DateTime _lastCleanTime = DateTime.MinValue;

        /// <summary>
        /// 单个日志文件最大大小（字节），默认 10MB
        /// </summary>
        public static long MaxFileSizeBytes { get; set; } = 10 * 1024 * 1024;

        /// <summary>
        /// 日志保留天数，默认 7 天
        /// </summary>
        public static int RetentionDays { get; set; } = 7;

        /// <summary>
        /// 日志根目录，默认为 App_Data/log
        /// </summary>
        public static string LogRootDirectory { get; set; } = null;

        /// <summary>
        /// 获取日志根目录
        /// </summary>
        private static string GetLogRootPath()
        {
            if (!string.IsNullOrEmpty(LogRootDirectory))
                return LogRootDirectory;

            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "App_Data", "log");
        }

        /// <summary>
        /// 写入日志
        /// </summary>
        /// <param name="logContent">日志内容</param>
        /// <param name="fileName">日志文件名（不含扩展名）</param>
        public static void SaveLog(string logContent, string fileName)
        {
            try
            {
                var now = DateTime.Now;
                var logDir = GetLogDateDirectory(now);

                if (!Directory.Exists(logDir))
                    Directory.CreateDirectory(logDir);

                var filePath = GetLogFilePath(logDir, fileName, now);
                var logLine = FormatLogLine(logContent, now);

                WriteToFile(filePath, logLine);

                // 每小时检查一次是否需要清理旧日志
                TryCleanOldLogs(now);
            }
            catch (Exception ex)
            {
                // 降级合理：日志写入失败绝不能影响主程序，否则日志会拖垮业务。
                // 但这是日志设施自身的失败，不能再次调 SaveLog（会递归），用 Trace 留痕。
                System.Diagnostics.Trace.TraceWarning("LogManager.SaveLog 写日志失败，已忽略：" + ex);
            }
        }

        /// <summary>
        /// 异步写入日志
        /// </summary>
        /// <param name="logContent">日志内容</param>
        /// <param name="fileName">日志文件名</param>
        public static void SaveLogAsync(string logContent, string fileName)
        {
            System.Threading.Tasks.Task.Run(() =>
            {
                SaveLog(logContent, fileName);
            });
        }

        /// <summary>
        /// 获取日志日期目录
        /// </summary>
        private static string GetLogDateDirectory(DateTime now)
        {
            var rootPath = GetLogRootPath();
            return Path.Combine(rootPath, now.ToString("yyyy-MM-dd"));
        }

        /// <summary>
        /// 获取日志文件路径（支持大小轮转）
        /// </summary>
        private static string GetLogFilePath(string logDir, string fileName, DateTime now)
        {
            if (string.IsNullOrEmpty(fileName))
                fileName = "app";

            var baseFileName = string.Format("{0}.txt", fileName);
            var filePath = Path.Combine(logDir, baseFileName);

            // 检查文件大小，超过限制则创建新文件
            if (File.Exists(filePath))
            {
                var fileInfo = new FileInfo(filePath);
                if (fileInfo.Length >= MaxFileSizeBytes)
                {
                    filePath = GetNextRotationFilePath(logDir, fileName);
                }
            }

            return filePath;
        }

        /// <summary>
        /// 获取下一个轮转文件路径
        /// </summary>
        private static string GetNextRotationFilePath(string logDir, string fileName)
        {
            // 查找当前最大的轮转序号
            var existingFiles = Directory.GetFiles(logDir, string.Format("{0}_*.txt", fileName));
            var maxIndex = 0;

            foreach (var file in existingFiles)
            {
                var name = Path.GetFileNameWithoutExtension(file);
                var parts = name.Split('_');
                if (parts.Length >= 2 && int.TryParse(parts[parts.Length - 1], out var index))
                {
                    if (index > maxIndex)
                        maxIndex = index;
                }
            }

            return Path.Combine(logDir, string.Format("{0}_{1}.txt", fileName, maxIndex + 1));
        }

        /// <summary>
        /// 格式化日志行
        /// </summary>
        private static string FormatLogLine(string logContent, DateTime now)
        {
            return string.Format("[{0:yyyy-MM-dd HH:mm:ss.fff}] {1}", now, logContent);
        }

        /// <summary>
        /// 写入文件（线程安全）
        /// </summary>
        private static void WriteToFile(string filePath, string logLine)
        {
            // 使用文件锁确保线程安全
            using (var mutex = new Mutex(false, string.Format("Global\\LogManager_{0}", filePath.GetHashCode())))
            {
                try
                {
                    mutex.WaitOne(TimeSpan.FromSeconds(5));
                }
                catch (AbandonedMutexException)
                {
                    // 合理：上一个持有者崩死导致锁被遗弃，本进程已获得锁，继续写入即可。
                    // 属预期情形，无需告警。
                }

                try
                {
                    using (var fs = new FileStream(filePath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
                    using (var writer = new StreamWriter(fs))
                    {
                        writer.WriteLine(logLine);
                        writer.Flush();
                    }
                }
                finally
                {
                    mutex.ReleaseMutex();
                }
            }
        }

        /// <summary>
        /// 尝试清理旧日志（每小时最多执行一次）
        /// </summary>
        private static void TryCleanOldLogs(DateTime now)
        {
            // 每小时最多清理一次
            if ((now - _lastCleanTime).TotalHours < 1)
                return;

            // 使用双重检查锁定
            if ((now - _lastCleanTime).TotalHours < 1)
                return;

            lock (_cleanLock)
            {
                if ((now - _lastCleanTime).TotalHours < 1)
                    return;

                _lastCleanTime = now;
                CleanOldLogs(now);
            }
        }

        /// <summary>
        /// 清理过期日志
        /// </summary>
        private static void CleanOldLogs(DateTime now)
        {
            try
            {
                var rootPath = GetLogRootPath();
                if (!Directory.Exists(rootPath))
                    return;

                var cutoffDate = now.AddDays(-RetentionDays);
                var directories = Directory.GetDirectories(rootPath);

                foreach (var dir in directories)
                {
                    var dirName = Path.GetFileName(dir);

                    // 尝试解析目录名为日期
                    if (DateTime.TryParseExact(dirName, "yyyy-MM-dd",
                        System.Globalization.CultureInfo.InvariantCulture,
                        System.Globalization.DateTimeStyles.None, out var dirDate))
                    {
                        // 删除超过保留天数的目录
                        if (dirDate < cutoffDate.Date)
                        {
                            try
                            {
                                Directory.Delete(dir, true);
                            }
                            catch (Exception ex)
                            {
                                // 降级合理：旧日志目录删不掉不影响写日志，用 Trace 留痕。
                                System.Diagnostics.Trace.TraceWarning("LogManager 删除过期日志目录失败：" + dir + " " + ex);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                // 降级合理：清理失败不影响主程序，但需留痕。
                System.Diagnostics.Trace.TraceWarning("LogManager 清理过期日志失败：" + ex);
            }
        }

        /// <summary>
        /// 手动触发日志清理
        /// </summary>
        public static void CleanOldLogs()
        {
            CleanOldLogs(DateTime.Now);
        }

        /// <summary>
        /// 获取日志统计信息
        /// </summary>
        /// <returns>(总文件数, 总大小MB, 最早日志日期)</returns>
        public static (int FileCount, double TotalSizeMB, DateTime? EarliestDate) GetLogStatistics()
        {
            try
            {
                var rootPath = GetLogRootPath();
                if (!Directory.Exists(rootPath))
                    return (0, 0, null);

                var files = Directory.GetFiles(rootPath, "*.txt", SearchOption.AllDirectories);
                var totalSize = files.Sum(f => new FileInfo(f).Length);
                var earliestDate = files
                    .Select(f => Path.GetDirectoryName(f))
                    .Where(d => !string.IsNullOrEmpty(d))
                    .Select(d => Path.GetFileName(d))
                    .Where(d => DateTime.TryParseExact(d, "yyyy-MM-dd",
                        System.Globalization.CultureInfo.InvariantCulture,
                        System.Globalization.DateTimeStyles.None, out _))
                    .Select(d => DateTime.ParseExact(d, "yyyy-MM-dd",
                        System.Globalization.CultureInfo.InvariantCulture))
                    .OrderBy(d => d)
                    .FirstOrDefault();

                return (files.Length, totalSize / (1024.0 * 1024.0), earliestDate);
            }
            catch (Exception ex)
            {
                // 降级合理：统计信息取不到时返回零值（调用方已有可判定结果），
                // 但需留痕以便排查权限/路径问题。
                System.Diagnostics.Trace.TraceWarning("LogManager.GetLogStatistics 统计日志失败：" + ex);
                return (0, 0, null);
            }
        }
    }
}
