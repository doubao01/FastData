using System;
using System.IO;
using System.Linq;
using FastData.Tooling.Sync;
using Xunit;

namespace FastData.Tests.Sync
{
    /// <summary>
    /// 同步日志记录器测试
    /// 
    /// 验证 Logger 的任务级文件写入、级别过滤与全局默认文件写入行为。
    /// Logger 为静态类，测试统一使用独立的临时日志目录，避免污染工程目录。
    /// </summary>
    public class LoggerTests
    {
        private static string CreateTempLogDir()
        {
            var dir = Path.Combine(Path.GetTempPath(), "fastdata-sync-logs", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            return dir;
        }

        [Fact]
        public void Write_WithTaskId_CreatesTaskScopedFile()
        {
            var dir = CreateTempLogDir();
            Logger.Initialize(dir, LogLevel.Debug);

            Logger.Write(LogLevel.Info, "hello-task", "task-123");

            var files = Directory.GetFiles(dir);
            var taskFile = Assert.Single(files, f => f.Contains("task-123"));
            Assert.Contains("hello-task", File.ReadAllText(taskFile));
        }

        [Fact]
        public void Write_SeparatesLogsByTaskId()
        {
            var dir = CreateTempLogDir();
            Logger.Initialize(dir, LogLevel.Debug);

            Logger.Write(LogLevel.Info, "message-A", "task-A");
            Logger.Write(LogLevel.Info, "message-B", "task-B");

            var fileA = Directory.GetFiles(dir).Single(f => f.Contains("task-A"));
            var fileB = Directory.GetFiles(dir).Single(f => f.Contains("task-B"));

            Assert.Contains("message-A", File.ReadAllText(fileA));
            Assert.DoesNotContain("message-B", File.ReadAllText(fileA));
            Assert.Contains("message-B", File.ReadAllText(fileB));
        }

        [Fact]
        public void Write_WithoutTaskId_UsesDefaultFile()
        {
            var dir = CreateTempLogDir();
            Logger.Initialize(dir, LogLevel.Debug);

            Logger.Write(LogLevel.Error, "default-file-message", null);

            var files = Directory.GetFiles(dir);
            Assert.Contains(files, f => File.ReadAllText(f).Contains("default-file-message"));
        }

        [Fact]
        public void Write_BelowMinLevel_IsFiltered()
        {
            var dir = CreateTempLogDir();
            Logger.Initialize(dir, LogLevel.Warn);

            Logger.Write(LogLevel.Debug, "should-not-appear", "filter-task");
            Logger.Write(LogLevel.Info, "also-filtered", "filter-task");
            Logger.Write(LogLevel.Error, "should-appear", "filter-task");

            var taskFile = Directory.GetFiles(dir).Single(f => f.Contains("filter-task"));
            var content = File.ReadAllText(taskFile);
            Assert.DoesNotContain("should-not-appear", content);
            Assert.DoesNotContain("also-filtered", content);
            Assert.Contains("should-appear", content);
        }

        [Fact]
        public void Write_IncludesLevelAndTimestamp()
        {
            var dir = CreateTempLogDir();
            Logger.Initialize(dir, LogLevel.Debug);

            Logger.Write(LogLevel.Warn, "level-check", "level-task");

            var taskFile = Directory.GetFiles(dir).Single(f => f.Contains("level-task"));
            var content = File.ReadAllText(taskFile);
            Assert.Contains("[WARN", content);
            Assert.Contains("level-check", content);
        }
    }
}
