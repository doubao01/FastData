using System;
using System.Collections.Generic;

namespace FastData.Tooling.Sync
{
    /// <summary>
    /// 同步任务配置
    /// </summary>
    public class SyncTaskConfig
    {
        /// <summary>
        /// 任务 ID
        /// </summary>
        public string TaskId { get; set; }

        /// <summary>
        /// 任务名称
        /// </summary>
        public string TaskName { get; set; }

        /// <summary>
        /// 源表名
        /// </summary>
        public string SourceTable { get; set; }

        /// <summary>
        /// 目标表名
        /// </summary>
        public string TargetTable { get; set; }

        /// <summary>
        /// 主键字段（逗号分隔）
        /// </summary>
        public string PrimaryKeyColumns { get; set; }

        /// <summary>
        /// 是否自增主键
        /// </summary>
        public bool IsAutoIncrementKey { get; set; }

        /// <summary>
        /// 时间字段（可选，用于动态数据）
        /// </summary>
        public string TimeColumn { get; set; }

        /// <summary>
        /// 是否启用时间范围
        /// </summary>
        public bool EnableTimeRange { get; set; }

        /// <summary>
        /// 同步范围天数
        /// </summary>
        public int RangeDays { get; set; } = 3;

        /// <summary>
        /// 同步字段列表（逗号分隔，空表示所有字段）
        /// </summary>
        public string SyncColumns { get; set; }

        /// <summary>
        /// 上次同步时间
        /// </summary>
        public DateTime? LastSyncTime { get; set; }

        /// <summary>
        /// 上次同步的主键最大值（主键增量模式，以字符串保存以避免溢出）
        /// </summary>
        public string LastPrimaryKeyValue { get; set; }

        /// <summary>
        /// 是否首次同步
        /// </summary>
        public bool IsFirstSync { get; set; } = true;

        /// <summary>
        /// 数据类型（静态/动态）
        /// </summary>
        public SyncDataType DataType { get; set; } = SyncDataType.Static;

        /// <summary>
        /// 是否启用
        /// </summary>
        public bool IsEnabled { get; set; } = true;

        /// <summary>
        /// 创建时间
        /// </summary>
        public DateTime CreatedTime { get; set; }

        /// <summary>
        /// 最后修改时间
        /// </summary>
        public DateTime ModifiedTime { get; set; }

        /// <summary>
        /// 最后同步状态
        /// </summary>
        public string LastSyncStatus { get; set; }

        /// <summary>
        /// 最后同步消息
        /// </summary>
        public string LastSyncMessage { get; set; }

        /// <summary>
        /// 源库连接字符串
        /// </summary>
        public string SourceConnection { get; set; }

        /// <summary>
        /// 目标库连接字符串
        /// </summary>
        public string TargetConnection { get; set; }

        /// <summary>
        /// 中间库连接字符串
        /// </summary>
        public string IntermediateConnection { get; set; }

        /// <summary>
        /// 表配置列表
        /// </summary>
        public IList<TableSyncConfig> TableConfigs { get; set; } = new List<TableSyncConfig>();

        // ===== 高级配置 =====

        /// <summary>
        /// 是否启用全局配置模式
        /// </summary>
        public bool EnableGlobalConfig { get; set; }

        /// <summary>
        /// 全局同步范围天数（0 = 使用任务级别配置）
        /// </summary>
        public int GlobalRangeDays { get; set; }

        /// <summary>
        /// 是否始终去重
        /// </summary>
        public bool AlwaysDeduplicate { get; set; } = true;

        /// <summary>
        /// 批次大小
        /// </summary>
        public int BatchSize { get; set; } = 1000;

        /// <summary>
        /// 重试次数
        /// </summary>
        public int RetryCount { get; set; } = 3;

        /// <summary>
        /// 自动创建中间库表结构
        /// </summary>
        public bool AutoCreateIntermediateSchema { get; set; } = true;

        /// <summary>
        /// 失败记录续传
        /// </summary>
        public bool ResumeFailedRecords { get; set; }

        /// <summary>
        /// 同步后清理中间库
        /// </summary>
        public bool CleanIntermediateData { get; set; }

        /// <summary>
        /// 启用定时同步
        /// </summary>
        public bool EnableTimer { get; set; }

        /// <summary>
        /// 定时同步间隔（秒）
        /// </summary>
        public int TimerInterval { get; set; } = 60;
    }
}
