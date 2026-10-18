using System;
using System.Collections.Generic;
using System.IO;

#if NETFRAMEWORK
using System.Web.Script.Serialization;
#else
using System.Text.Json;
#endif

namespace FastData.Tooling.Sync
{
    /// <summary>
    /// 同步任务配置管理器
    /// </summary>
    public class SyncConfigManager
    {
        private readonly string configPath;
        private readonly object syncRoot = new object();
        private IDictionary<string, SyncTaskConfig> configs;

        public SyncConfigManager(string configFilePath = null)
        {
            configPath = configFilePath ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "sync_tasks.json");
            configs = new Dictionary<string, SyncTaskConfig>(StringComparer.OrdinalIgnoreCase);
            LoadConfigs();
        }

        /// <summary>
        /// 加载所有配置
        /// 
        /// 加载失败时保留已有配置，避免因读取异常导致配置被清空。
        /// </summary>
        private void LoadConfigs()
        {
            if (!File.Exists(configPath))
                return;

            List<SyncTaskConfig> items = null;
            try
            {
                var json = File.ReadAllText(configPath);

#if NETFRAMEWORK
                var serializer = new JavaScriptSerializer();
                items = serializer.Deserialize<List<SyncTaskConfig>>(json);
#else
                items = JsonSerializer.Deserialize<List<SyncTaskConfig>>(json);
#endif
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine("SyncConfigManager: Failed to load configs - " + ex.Message);
                return;
            }

            if (items == null)
                return;

            var loaded = new Dictionary<string, SyncTaskConfig>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in items)
            {
                if (!string.IsNullOrEmpty(item.TaskId))
                {
                    loaded[item.TaskId] = item;
                }
            }

            lock (syncRoot)
            {
                configs = loaded;
            }
        }

        /// <summary>
        /// 保存所有配置（先写临时文件再替换，避免写入中断导致配置损坏）
        /// </summary>
        private void SaveConfigs()
        {
            List<SyncTaskConfig> snapshot;
            lock (syncRoot)
            {
                snapshot = new List<SyncTaskConfig>(configs.Values);
            }

            var dir = Path.GetDirectoryName(configPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

#if NETFRAMEWORK
            var serializer = new JavaScriptSerializer();
            var json = serializer.Serialize(snapshot);
#else
            var json = JsonSerializer.Serialize(snapshot);
#endif

            var tempPath = configPath + ".tmp";
            File.WriteAllText(tempPath, json);
            if (File.Exists(configPath))
                File.Replace(tempPath, configPath, null);
            else
                File.Move(tempPath, configPath);
        }

        /// <summary>
        /// 获取任务配置
        /// </summary>
        public SyncTaskConfig GetTaskConfig(string taskId)
        {
            if (string.IsNullOrEmpty(taskId))
                return null;

            lock (syncRoot)
            {
                SyncTaskConfig config;
                return configs.TryGetValue(taskId, out config) ? config : null;
            }
        }

        /// <summary>
        /// 保存任务配置
        /// </summary>
        public void SaveTaskConfig(SyncTaskConfig config)
        {
            if (config == null || string.IsNullOrEmpty(config.TaskId))
                return;

            lock (syncRoot)
            {
                configs[config.TaskId] = config;
            }
            SaveConfigs();
        }

        /// <summary>
        /// 更新最后同步时间
        /// </summary>
        public void UpdateLastSyncTime(string taskId, DateTime syncTime)
        {
            var config = GetTaskConfig(taskId);
            if (config != null)
            {
                config.LastSyncTime = syncTime;
                config.IsFirstSync = false;
                SaveTaskConfig(config);
            }
        }

        /// <summary>
        /// 更新最后同步的主键最大值（主键增量模式）
        /// </summary>
        public void UpdateLastPrimaryKeyValue(string taskId, string primaryKeyValue)
        {
            var config = GetTaskConfig(taskId);
            if (config != null)
            {
                config.LastPrimaryKeyValue = primaryKeyValue;
                config.IsFirstSync = false;
                SaveTaskConfig(config);
            }
        }

        /// <summary>
        /// 获取所有任务配置
        /// </summary>
        public IList<SyncTaskConfig> GetAllTaskConfigs()
        {
            lock (syncRoot)
            {
                return new List<SyncTaskConfig>(configs.Values);
            }
        }

        /// <summary>
        /// 获取所有任务配置（用于任务列表）
        /// </summary>
        public IList<SyncTaskConfig> GetAllConfigs()
        {
            lock (syncRoot)
            {
                return new List<SyncTaskConfig>(configs.Values);
            }
        }

        /// <summary>
        /// 删除任务配置
        /// </summary>
        public void DeleteTaskConfig(string taskId)
        {
            if (string.IsNullOrEmpty(taskId))
                return;

            var removed = false;
            lock (syncRoot)
            {
                removed = configs.Remove(taskId);
            }

            if (removed)
                SaveConfigs();
        }

        /// <summary>
        /// 更新任务状态
        /// </summary>
        public void UpdateTaskStatus(string taskId, string status, string message)
        {
            var config = GetTaskConfig(taskId);
            if (config != null)
            {
                config.LastSyncStatus = status;
                config.LastSyncMessage = message;
                config.ModifiedTime = DateTime.Now;
                SaveTaskConfig(config);
            }
        }
    }
}
