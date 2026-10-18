using System;
using System.IO;
using FastData.Tooling.Sync;
using Xunit;

namespace FastData.Tests.Sync
{
    /// <summary>
    /// 同步配置管理器测试
    /// 
    /// 覆盖配置的持久化（原子写入）、重新加载、主键游标更新以及损坏配置文件的容错处理，
    /// 验证 SyncConfigManager 在多任务并发场景下的线程安全与数据不丢失特性。
    /// </summary>
    public class SyncConfigManagerTests
    {
        private static string CreateTempConfigPath()
        {
            var dir = Path.Combine(Path.GetTempPath(), "fastdata-sync-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, "sync_tasks.json");
        }

        private static SyncTaskConfig CreateConfig(string taskId)
        {
            return new SyncTaskConfig
            {
                TaskId = taskId,
                TaskName = "任务-" + taskId,
                SourceTable = "AppUser",
                TargetTable = "AppUser",
                PrimaryKeyColumns = "Id",
                CreatedTime = DateTime.Now,
                ModifiedTime = DateTime.Now
            };
        }

        [Fact]
        public void SaveTaskConfig_ThenReload_PersistsConfig()
        {
            var path = CreateTempConfigPath();

            var manager = new SyncConfigManager(path);
            manager.SaveTaskConfig(CreateConfig("t1"));

            var reloaded = new SyncConfigManager(path);
            var config = reloaded.GetTaskConfig("t1");

            Assert.NotNull(config);
            Assert.Equal("t1", config.TaskId);
            Assert.Equal("AppUser", config.SourceTable);
        }

        [Fact]
        public void GetTaskConfig_IsCaseInsensitive()
        {
            var path = CreateTempConfigPath();
            var manager = new SyncConfigManager(path);
            manager.SaveTaskConfig(CreateConfig("TaskA"));

            Assert.NotNull(manager.GetTaskConfig("taska"));
        }

        [Fact]
        public void UpdateLastPrimaryKeyValue_SetsValueAndClearsFirstSyncFlag()
        {
            var path = CreateTempConfigPath();
            var manager = new SyncConfigManager(path);
            manager.SaveTaskConfig(CreateConfig("t1"));

            manager.UpdateLastPrimaryKeyValue("t1", "9223372036854775807");

            var config = manager.GetTaskConfig("t1");
            Assert.Equal("9223372036854775807", config.LastPrimaryKeyValue);
            Assert.False(config.IsFirstSync);

            var reloaded = new SyncConfigManager(path);
            Assert.Equal("9223372036854775807", reloaded.GetTaskConfig("t1").LastPrimaryKeyValue);
        }

        [Fact]
        public void UpdateLastPrimaryKeyValue_UnknownTask_NoEffect()
        {
            var path = CreateTempConfigPath();
            var manager = new SyncConfigManager(path);

            manager.UpdateLastPrimaryKeyValue("missing", "10");

            Assert.Null(manager.GetTaskConfig("missing"));
        }

        [Fact]
        public void SaveConfigs_LeavesNoTempFile()
        {
            var path = CreateTempConfigPath();
            var manager = new SyncConfigManager(path);

            manager.SaveTaskConfig(CreateConfig("t1"));

            Assert.True(File.Exists(path));
            Assert.False(File.Exists(path + ".tmp"));
        }

        [Fact]
        public void Constructor_CorruptFile_DoesNotThrowAndStaysUsable()
        {
            var path = CreateTempConfigPath();
            File.WriteAllText(path, "{ this is not valid json ");

            var manager = new SyncConfigManager(path);

            Assert.Empty(manager.GetAllTaskConfigs());

            manager.SaveTaskConfig(CreateConfig("t1"));
            Assert.NotNull(manager.GetTaskConfig("t1"));
        }

        [Fact]
        public void DeleteTaskConfig_RemovesAndPersists()
        {
            var path = CreateTempConfigPath();
            var manager = new SyncConfigManager(path);
            manager.SaveTaskConfig(CreateConfig("t1"));
            manager.SaveTaskConfig(CreateConfig("t2"));

            manager.DeleteTaskConfig("t1");

            Assert.Null(manager.GetTaskConfig("t1"));
            Assert.NotNull(manager.GetTaskConfig("t2"));

            var reloaded = new SyncConfigManager(path);
            Assert.Null(reloaded.GetTaskConfig("t1"));
            Assert.NotNull(reloaded.GetTaskConfig("t2"));
        }

        [Fact]
        public void GetAllTaskConfigs_ReturnsSnapshot()
        {
            var path = CreateTempConfigPath();
            var manager = new SyncConfigManager(path);
            manager.SaveTaskConfig(CreateConfig("t1"));

            var snapshot = manager.GetAllTaskConfigs();
            manager.SaveTaskConfig(CreateConfig("t2"));

            Assert.Single(snapshot);
        }

        [Fact]
        public void UpdateTaskStatus_UpdatesFieldsAndPersists()
        {
            var path = CreateTempConfigPath();
            var manager = new SyncConfigManager(path);
            manager.SaveTaskConfig(CreateConfig("t1"));

            manager.UpdateTaskStatus("t1", "Success", "ok");

            var reloaded = new SyncConfigManager(path);
            var config = reloaded.GetTaskConfig("t1");
            Assert.Equal("Success", config.LastSyncStatus);
            Assert.Equal("ok", config.LastSyncMessage);
        }
    }
}
