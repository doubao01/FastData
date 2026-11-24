using System;
using System.Collections.Generic;
using System.Linq;

namespace FastData.Sharding.Strategies
{
    /// <summary>
    /// 查询频率分表策略
    /// 根据查询频率高的字段进行分表，将热数据（高频查询）和冷数据（低频查询）分离
    /// </summary>
    public class QueryFrequencyShardingStrategy : IShardingStrategy
    {
        /// <summary>
        /// 获取分片策略的名称
        /// </summary>
        public string Name => "QueryFrequency";

        /// <summary>
        /// 获取分片策略的类型
        /// </summary>
        public ShardingType Type => ShardingType.QueryFrequency;

        /// <summary>
        /// 查询频率统计
        /// Key: 字段值, Value: 查询次数
        /// </summary>
        private static readonly Dictionary<string, Dictionary<string, long>> _frequencyStats =
            new Dictionary<string, Dictionary<string, long>>();

        /// <summary>
        /// 热数据阈值（查询次数超过此值的数据视为热数据）
        /// </summary>
        private static readonly Dictionary<string, long> _hotThresholds =
            new Dictionary<string, long>();

        /// <summary>
        /// 频率统计锁
        /// </summary>
        private static readonly object _lock = new object();

        /// <summary>
        /// 获取表名
        /// </summary>
        /// <param name="config">分片配置</param>
        /// <param name="entity">实体对象</param>
        /// <returns>表名</returns>
        public string GetTableName(ShardingConfig config, object entity)
        {
            if (config.FrequencyConfig == null)
            {
                throw new InvalidOperationException("QueryFrequencyConfig is not configured.");
            }

            var freqConfig = config.FrequencyConfig;
            var fieldValue = GetFieldValue(entity, freqConfig.Field);
            var fieldValueStr = fieldValue?.ToString() ?? "null";

            // 判断是否为热数据
            var isHot = IsHotData(freqConfig.Field, fieldValueStr, freqConfig.HotThreshold);

            if (isHot)
            {
                return string.Format("{0}{1}", config.BaseTableName, freqConfig.HotSuffix);
            }
            else
            {
                // 冷数据按时间或哈希分表
                if (freqConfig.ColdShardingType == ColdShardingType.ByTime)
                {
                    var timeValue = GetFieldValue(entity, freqConfig.TimeField);
                    if (timeValue is DateTime time)
                    {
                        var timeGranularity = freqConfig.TimeGranularity ?? TimeGranularity.Month;
                        return string.Format("{0}{1}", config.BaseTableName, GetTimeSuffix(time, timeGranularity));
                    }
                }
                else if (freqConfig.ColdShardingType == ColdShardingType.ByHash)
                {
                    var hash = Math.Abs(fieldValueStr.GetHashCode()) % (freqConfig.ColdShardCount ?? 4);
                    return string.Format("{0}_cold_{1:D4}", config.BaseTableName, hash);
                }

                return string.Format("{0}{1}", config.BaseTableName, freqConfig.ColdSuffix);
            }
        }

        /// <summary>
        /// 获取表名列表
        /// </summary>
        /// <param name="config">分片配置</param>
        /// <param name="queryParams">查询参数</param>
        /// <returns>表名列表</returns>
        public List<string> GetTableNames(ShardingConfig config, Dictionary<string, object> queryParams)
        {
            if (config.FrequencyConfig == null)
            {
                throw new InvalidOperationException("QueryFrequencyConfig is not configured.");
            }

            var freqConfig = config.FrequencyConfig;
            var tableNames = new List<string>();

            // 检查查询参数中是否包含频率字段
            if (queryParams.ContainsKey(freqConfig.Field))
            {
                var fieldValue = queryParams[freqConfig.Field]?.ToString() ?? "null";
                var isHot = IsHotData(freqConfig.Field, fieldValue, freqConfig.HotThreshold);

                if (isHot)
                {
                    tableNames.Add(string.Format("{0}{1}", config.BaseTableName, freqConfig.HotSuffix));
                }
                else
                {
                    // 冷数据表
                    if (freqConfig.ColdShardingType == ColdShardingType.ByTime &&
                        queryParams.ContainsKey(freqConfig.TimeField))
                    {
                        var timeValue = queryParams[freqConfig.TimeField];
                        if (timeValue is DateTime time)
                        {
                            var timeGranularity = freqConfig.TimeGranularity ?? TimeGranularity.Month;
                            tableNames.Add(string.Format("{0}{1}", config.BaseTableName, GetTimeSuffix(time, timeGranularity)));
                        }
                    }
                    else if (freqConfig.ColdShardingType == ColdShardingType.ByHash)
                    {
                        var hash = Math.Abs(fieldValue.GetHashCode()) % (freqConfig.ColdShardCount ?? 4);
                        tableNames.Add(string.Format("{0}_cold_{1:D4}", config.BaseTableName, hash));
                    }
                    else
                    {
                        tableNames.Add(string.Format("{0}{1}", config.BaseTableName, freqConfig.ColdSuffix));
                    }
                }
            }
            else
            {
                // 没有指定频率字段，返回所有相关表
                tableNames.Add(string.Format("{0}{1}", config.BaseTableName, freqConfig.HotSuffix));

                if (freqConfig.ColdShardingType == ColdShardingType.ByHash)
                {
                    var shardCount = freqConfig.ColdShardCount ?? 4;
                    for (int i = 0; i < shardCount; i++)
                    {
                        tableNames.Add(string.Format("{0}_cold_{1:D4}", config.BaseTableName, i));
                    }
                }
                else
                {
                    tableNames.Add(string.Format("{0}{1}", config.BaseTableName, freqConfig.ColdSuffix));
                }
            }

            return tableNames;
        }

        /// <summary>
        /// 获取所有表名
        /// </summary>
        public List<string> GetAllTableNames(ShardingConfig config)
        {
            if (config.FrequencyConfig == null)
            {
                throw new InvalidOperationException("QueryFrequencyConfig is not configured.");
            }

            var freqConfig = config.FrequencyConfig;
            var tableNames = new List<string>
            {
                string.Format("{0}{1}", config.BaseTableName, freqConfig.HotSuffix)
            };

            if (freqConfig.ColdShardingType == ColdShardingType.ByHash)
            {
                var shardCount = freqConfig.ColdShardCount ?? 4;
                for (int i = 0; i < shardCount; i++)
                {
                    tableNames.Add(string.Format("{0}_cold_{1:D4}", config.BaseTableName, i));
                }
            }
            else
            {
                tableNames.Add(string.Format("{0}{1}", config.BaseTableName, freqConfig.ColdSuffix));
            }

            return tableNames;
        }

        /// <summary>
        /// 创建表
        /// </summary>
        /// <param name="config">分片配置</param>
        /// <param name="tableName">表名</param>
        /// <returns>是否成功</returns>
        public bool CreateTable(ShardingConfig config, string tableName)
        {
            return ShardingManager.CreateTable(config, tableName);
        }

        /// <summary>
        /// 记录查询频率
        /// </summary>
        public static void RecordQuery(string field, string fieldValue)
        {
            lock (_lock)
            {
                if (!_frequencyStats.ContainsKey(field))
                {
                    _frequencyStats[field] = new Dictionary<string, long>();
                }

                if (!_frequencyStats[field].ContainsKey(fieldValue))
                {
                    _frequencyStats[field][fieldValue] = 0;
                }

                _frequencyStats[field][fieldValue]++;
            }
        }

        /// <summary>
        /// 获取查询频率
        /// </summary>
        public static long GetQueryFrequency(string field, string fieldValue)
        {
            lock (_lock)
            {
                if (_frequencyStats.ContainsKey(field) &&
                    _frequencyStats[field].ContainsKey(fieldValue))
                {
                    return _frequencyStats[field][fieldValue];
                }
                return 0;
            }
        }

        /// <summary>
        /// 获取热数据列表
        /// </summary>
        public static List<string> GetHotDataValues(string field, long threshold)
        {
            lock (_lock)
            {
                if (!_frequencyStats.ContainsKey(field))
                {
                    return new List<string>();
                }

                return _frequencyStats[field]
                    .Where(kv => kv.Value >= threshold)
                    .Select(kv => kv.Key)
                    .ToList();
            }
        }

        /// <summary>
        /// 重置频率统计
        /// </summary>
        public static void ResetFrequencyStats(string field = null)
        {
            lock (_lock)
            {
                if (field == null)
                {
                    _frequencyStats.Clear();
                }
                else if (_frequencyStats.ContainsKey(field))
                {
                    _frequencyStats[field].Clear();
                }
            }
        }

        /// <summary>
        /// 判断是否为热数据
        /// </summary>
        private bool IsHotData(string field, string fieldValue, long threshold)
        {
            var frequency = GetQueryFrequency(field, fieldValue);
            return frequency >= threshold;
        }

        /// <summary>
        /// 获取字段值
        /// </summary>
        private object GetFieldValue(object entity, string fieldName)
        {
            var property = entity.GetType().GetProperty(fieldName);
            return property?.GetValue(entity);
        }

        /// <summary>
        /// 获取时间后缀
        /// </summary>
        private string GetTimeSuffix(DateTime time, TimeGranularity granularity)
        {
            switch (granularity)
            {
                case TimeGranularity.Day:
                    return string.Format("_{0:yyyyMMdd}", time);
                case TimeGranularity.Week:
                    var weekStart = time.AddDays(-(int)time.DayOfWeek);
                    return string.Format("_{0:yyyyMMdd}", weekStart);
                case TimeGranularity.Month:
                    return string.Format("_{0:yyyyMM}", time);
                case TimeGranularity.Quarter:
                    var quarter = (time.Month - 1) / 3 + 1;
                    return string.Format("_{0:yyyy}Q{1}", time, quarter);
                case TimeGranularity.Year:
                    return string.Format("_{0:yyyy}", time);
                default:
                    return string.Format("_{0:yyyyMM}", time);
            }
        }
    }
}
