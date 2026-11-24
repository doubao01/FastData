using System;
using System.Collections.Generic;

namespace FastData.Sharding
{
    /// <summary>
    /// 分表配置
    /// </summary>
    public class ShardingConfig
    {
        /// <summary>
        /// 实体类型
        /// </summary>
        public System.Type EntityType { get; set; }

        /// <summary>
        /// 基础表名（如 "UserLog"）
        /// </summary>
        public string BaseTableName { get; set; }

        /// <summary>
        /// 分表策略类型
        /// </summary>
        public ShardingType ShardingType { get; set; }

        /// <summary>
        /// 时间、哈希、列表、组合键分表的后缀格式（如 "{0:yyyyMMdd}", "{0:D4}"）；为空时保留默认格式
        /// </summary>
        public string SuffixFormat { get; set; }

        /// <summary>
        /// 时间分表配置
        /// </summary>
        public TimeShardingConfig TimeConfig { get; set; }

        /// <summary>
        /// 哈希分表配置
        /// </summary>
        public HashShardingConfig HashConfig { get; set; }

        /// <summary>
        /// 列表分表配置
        /// </summary>
        public ListShardingConfig ListConfig { get; set; }

        /// <summary>
        /// 组合键分表配置
        /// </summary>
        public CompositeShardingConfig CompositeConfig { get; set; }

        /// <summary>
        /// 地理位置分表配置
        /// </summary>
        public GeoShardingConfig GeoConfig { get; set; }

        /// <summary>
        /// 查询频率分表配置
        /// </summary>
        public QueryFrequencyShardingConfig FrequencyConfig { get; set; }

        /// <summary>
        /// 分表数量限制（防止查询太多表）
        /// </summary>
        public int MaxTableCount { get; set; } = 100;

        /// <summary>
        /// 是否自动创建分表；默认关闭，启用后调用建表入口会因尚未实现而抛出异常
        /// </summary>
        public bool AutoCreateTable { get; set; } = false;

        /// <summary>
        /// 数据库连接 Key
        /// </summary>
        public string DatabaseKey { get; set; }

        /// <summary>
        /// 缓存的分片策略实例（由 ShardingManager.Configure 设置，避免每次查询重复查找策略字典）
        /// </summary>
        internal IShardingStrategy CachedStrategy { get; set; }
    }

    /// <summary>
    /// 时间分表配置
    /// </summary>
    public class TimeShardingConfig
    {
        /// <summary>
        /// 时间字段名称
        /// </summary>
        public string TimeField { get; set; }

        /// <summary>
        /// 时间粒度
        /// </summary>
        public TimeGranularity Granularity { get; set; } = TimeGranularity.Month;

        /// <summary>
        /// 起始时间（用于计算分表范围）
        /// </summary>
        public DateTime? StartTime { get; set; }

        /// <summary>
        /// 结束时间（为空则不限制）
        /// </summary>
        public DateTime? EndTime { get; set; }
    }

    /// <summary>
    /// 哈希分表配置
    /// </summary>
    public class HashShardingConfig
    {
        /// <summary>
        /// 哈希字段名称
        /// </summary>
        public string HashField { get; set; }

        /// <summary>
        /// 分表数量
        /// </summary>
        public int ShardCount { get; set; } = 8;

        /// <summary>
        /// 哈希算法
        /// </summary>
        public HashAlgorithm Algorithm { get; set; } = HashAlgorithm.Modulo;
    }

    /// <summary>
    /// 列表分表配置
    /// </summary>
    public class ListShardingConfig
    {
        /// <summary>
        /// 列表字段名称
        /// </summary>
        public string ListField { get; set; }

        /// <summary>
        /// 枚举值到分表后缀的映射
        /// </summary>
        public Dictionary<string, string> ValueMapping { get; set; } = new Dictionary<string, string>();
    }

    /// <summary>
    /// 组合键分表配置
    /// </summary>
    public class CompositeShardingConfig
    {
        /// <summary>
        /// 组合字段列表
        /// </summary>
        public List<string> CompositeFields { get; set; } = new List<string>();

        /// <summary>
        /// 分隔符
        /// </summary>
        public string Separator { get; set; } = "_";

        /// <summary>
        /// 是否使用哈希（否则使用拼接）
        /// </summary>
        public bool UseHash { get; set; } = true;

        /// <summary>
        /// 分表数量（UseHash=true 时生效）
        /// </summary>
        public int ShardCount { get; set; } = 16;
    }

    /// <summary>
    /// 地理位置分表配置
    /// </summary>
    public class GeoShardingConfig
    {
        /// <summary>
        /// 地区字段名称
        /// </summary>
        public string RegionField { get; set; }

        /// <summary>
        /// 地区到分表后缀的映射
        /// </summary>
        public Dictionary<string, string> RegionMapping { get; set; } = new Dictionary<string, string>();

        /// <summary>
        /// 默认地区（未知地区使用）
        /// </summary>
        public string DefaultRegion { get; set; } = "default";
    }

    /// <summary>
    /// 查询频率分表配置
    /// </summary>
    public class QueryFrequencyShardingConfig
    {
        /// <summary>
        /// 频率统计字段（如 "UserId", "ProductId"）
        /// </summary>
        public string Field { get; set; }

        /// <summary>
        /// 热数据阈值（查询次数超过此值视为热数据）
        /// </summary>
        public long HotThreshold { get; set; } = 100;

        /// <summary>
        /// 热数据表后缀
        /// </summary>
        public string HotSuffix { get; set; } = "_hot";

        /// <summary>
        /// 冷数据表后缀
        /// </summary>
        public string ColdSuffix { get; set; } = "_cold";

        /// <summary>
        /// 冷数据分表类型
        /// </summary>
        public ColdShardingType ColdShardingType { get; set; } = ColdShardingType.ByHash;

        /// <summary>
        /// 冷数据哈希分表数量
        /// </summary>
        public int? ColdShardCount { get; set; } = 4;

        /// <summary>
        /// 时间字段（ColdShardingType.ByTime 时使用）
        /// </summary>
        public string TimeField { get; set; }

        /// <summary>
        /// 时间分表粒度（ColdShardingType.ByTime 时使用）
        /// </summary>
        public TimeGranularity? TimeGranularity { get; set; }
    }

    /// <summary>
    /// 冷数据分表类型
    /// </summary>
    public enum ColdShardingType
    {
        /// <summary>
        /// 按哈希分表
        /// </summary>
        ByHash = 0,

        /// <summary>
        /// 按时间分表
        /// </summary>
        ByTime = 1,

        /// <summary>
        /// 单表存储
        /// </summary>
        Single = 2
    }
}
