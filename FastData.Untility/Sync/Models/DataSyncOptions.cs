using System;
using System.Collections.Generic;

namespace FastData.Tooling.Sync
{
    public class DataSyncOptions
    {
        public string SourceProvider { get; set; }

        public string SourceConnectionString { get; set; }

        public string TargetProvider { get; set; }

        public string TargetConnectionString { get; set; }

        public string IntermediateProvider { get; set; }

        public string IntermediateConnectionString { get; set; }

        public string TaskId { get; set; }

        public string SourceTable { get; set; }

        public string TargetTable { get; set; }

        public string PrimaryKeyColumns { get; set; }

        public bool IsAutoIncrementKey { get; set; }

        public string TimeColumn { get; set; }

        public bool EnableTimeRange { get; set; }

        public bool IsFullSyncForFirstTime { get; set; } = true;

        /// <summary>
        /// 是否始终去重（首次同步也根据业务主键去重，而不是全量插入）
        /// 默认：true（首次全量，后续增量去重）
        /// 特殊场景：false（首次也根据主键去重，只插入不存在的记录）
        /// </summary>
        public bool AlwaysDeduplicate { get; set; } = true;

        /// <summary>
        /// 全局同步范围天数（优先于任务级别的 RangeDays）
        /// 0 = 使用任务级别配置
        /// >0 = 使用全局配置
        /// </summary>
        public int GlobalRangeDays { get; set; } = 0;

        /// <summary>
        /// 是否启用全局配置模式
        /// true = 所有任务使用统一的全局配置（GlobalRangeDays, GlobalAlwaysDeduplicate）
        /// false = 每个任务使用自己的配置
        /// </summary>
        public bool EnableGlobalConfig { get; set; } = false;

        /// <summary>
        /// 全局去重模式（EnableGlobalConfig=true 时覆盖任务级别的 AlwaysDeduplicate）
        /// </summary>
        public bool GlobalAlwaysDeduplicate { get; set; } = true;

        public DateTime? EndTime { get; set; }

        public int BatchSize { get; set; }

        public int RetryCount { get; set; }

        public bool CleanIntermediateData { get; set; }

        public bool AutoCreateIntermediateSchema { get; set; }

        public bool ResumeFailedRecords { get; set; }

        public bool EnableTimer { get; set; }

        public int SyncIntervalSeconds { get; set; }

        public PrimaryKeyConfigService PrimaryKeyConfigService { get; set; }

        public SyncConfigManager ConfigManager { get; set; }

        public int RangeDays { get; set; } = 3;

        public IList<string> SyncColumns { get; set; }
    }
}
