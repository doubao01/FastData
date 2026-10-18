using System;

namespace FastData.Tooling.Sync
{
    /// <summary>
    /// 数据同步结果
    /// </summary>
    public class DataSyncResult
    {
        /// <summary>
        /// 读取记录数
        /// </summary>
        public int ReadCount { get; set; }

        /// <summary>
        /// 写入记录数
        /// </summary>
        public int WriteCount { get; set; }

        /// <summary>
        /// 失败记录数
        /// </summary>
        public int FailedCount { get; set; }

        /// <summary>
        /// 重试次数
        /// </summary>
        public int RetryCount { get; set; }

        /// <summary>
        /// 恢复记录数
        /// </summary>
        public int RecoveredCount { get; set; }

        /// <summary>
        /// 去重跳过的记录数（目标库已存在，未实际写入）
        /// </summary>
        public int SkippedCount { get; set; }

        /// <summary>
        /// 结果消息
        /// </summary>
        public string Message { get; set; }

        /// <summary>
        /// 最后同步时间
        /// </summary>
        public DateTime? LastSyncTime { get; set; }

        /// <summary>
        /// 最大主键值（增量同步使用）
        /// </summary>
        public long? MaxPkValue { get; set; }
    }
}
