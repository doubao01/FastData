using System;
using System.Collections.Generic;
using System.IO;
using FastData.Tooling.Sync;

namespace FastData.SyncTool.WinForms.Services
{
public enum LogEntryLevel
{
    Debug = 0,
    Info = 1,
    Warn = 2,
    Error = 3
}

    public class LogEntry
    {
        public DateTime Timestamp { get; set; }
        public LogEntryLevel Level { get; set; }
        public string Message { get; set; }
        public string TaskId { get; set; }

        public override string ToString()
        {
            return string.Format("[{0}] [{1}] {2}", Timestamp.ToString("HH:mm:ss.fff"), Level.ToString().ToUpper().PadRight(5), Message);
        }

        public string ToFullString()
        {
            return string.Format("[{0}] [{1}] [{2}] {3}", Timestamp.ToString("yyyy-MM-dd HH:mm:ss.fff"), Level.ToString().ToUpper().PadRight(5), TaskId ?? "-", Message);
        }
    }

    public class LogService : IDisposable
    {
        private readonly List<LogEntry> entries = new List<LogEntry>();
        private readonly int maxEntries;
        private readonly string logDirectory;
        private LogEntryLevel minLevel = LogEntryLevel.Debug;
        private string filterTaskId;

        public event EventHandler<LogEntry> EntryAdded;

        public LogService(string logDir = "./logs", int maxLogs = 5000)
        {
            logDirectory = logDir;
            maxEntries = maxLogs;
            if (!Directory.Exists(logDirectory)) Directory.CreateDirectory(logDirectory);
            Logger.Initialize(logDirectory);
        }

        public LogEntryLevel MinLevel { get { return minLevel; } set { minLevel = value; } }
        public string FilterTaskId { get { return filterTaskId; } set { filterTaskId = value; } }

        public void Debug(string message, string taskId = null) { Log(LogEntryLevel.Debug, message, taskId); }
        public void Info(string message, string taskId = null) { Log(LogEntryLevel.Info, message, taskId); }
        public void Warn(string message, string taskId = null) { Log(LogEntryLevel.Warn, message, taskId); }
        public void Error(string message, Exception ex = null, string taskId = null) { Log(LogEntryLevel.Error, message + (ex != null ? ": " + ex.Message : ""), taskId); }

        private void Log(LogEntryLevel level, string message, string taskId)
        {
            if (level < minLevel) return;

            var entry = new LogEntry
            {
                Timestamp = DateTime.Now,
                Level = level,
                Message = message,
                TaskId = taskId
            };

            Logger.Write(ToLogLevel(level), message, taskId ?? "app");

            lock (entries)
            {
                entries.Add(entry);
                if (entries.Count > maxEntries) entries.RemoveAt(0);
            }

            if (string.IsNullOrEmpty(filterTaskId) || string.IsNullOrEmpty(taskId) || taskId == filterTaskId)
            {
                EntryAdded?.Invoke(this, entry);
            }
        }

        public List<LogEntry> GetEntries(LogEntryLevel? minLevel = null, string taskId = null, int maxCount = 500)
        {
            var result = new List<LogEntry>();
            lock (entries)
            {
                for (int i = entries.Count - 1; i >= 0 && result.Count < maxCount; i--)
                {
                    var e = entries[i];
                    if (minLevel.HasValue && e.Level < minLevel.Value) continue;
                    if (!string.IsNullOrEmpty(taskId) && e.TaskId != taskId) continue;
                    result.Add(e);
                }
            }
            result.Reverse();
            return result;
        }

        public List<string> GetRecentStrings(int count = 100)
        {
            var result = new List<string>();
            lock (entries)
            {
                int start = Math.Max(0, entries.Count - count);
                for (int i = start; i < entries.Count; i++)
                    result.Add(entries[i].ToString());
            }
            return result;
        }

        public void Clear() { lock (entries) { entries.Clear(); } }

        public void ExportLogs(string filePath, LogEntryLevel? minLevel = null, string taskId = null)
        {
            var logs = GetEntries(minLevel, taskId, int.MaxValue);
            var lines = new List<string>();
            foreach (var e in logs) lines.Add(e.ToFullString());
            File.WriteAllLines(filePath, lines);
            Info("日志已导出到: " + filePath);
        }

        public void Dispose() { }

        private static LogLevel ToLogLevel(LogEntryLevel level)
        {
            switch (level)
            {
                case LogEntryLevel.Debug: return LogLevel.Debug;
                case LogEntryLevel.Warn: return LogLevel.Warn;
                case LogEntryLevel.Error: return LogLevel.Error;
                default: return LogLevel.Info;
            }
        }
    }
}
