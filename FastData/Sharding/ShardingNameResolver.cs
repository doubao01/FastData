using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using FastData.Sharding.Strategies;

namespace FastData.Sharding
{
    /// <summary>
    /// 分表名称解析器
    /// 
    /// 为所有分表策略提供统一的分表名称解析逻辑，确保通过实体对象、查询参数字典
    /// 或原始字段值计算得到的分表名称完全一致。
    /// 
    /// 使用示例：
    /// <code>
    /// var tableName = ShardingNameResolver.Resolve(config, field =&gt; entity[field]);
    /// </code>
    /// </summary>
    public static class ShardingNameResolver
    {
        /// <summary>
        /// 根据字段取值委托解析目标分表名称
        /// </summary>
        /// <param name="config">分表配置</param>
        /// <param name="valueAccessor">根据字段名获取字段值的委托</param>
        /// <returns>目标分表名称</returns>
        public static string Resolve(ShardingConfig config, Func<string, object> valueAccessor)
        {
            if (config == null)
                throw new ArgumentNullException(nameof(config));
            if (valueAccessor == null)
                throw new ArgumentNullException(nameof(valueAccessor));

            switch (config.ShardingType)
            {
                case ShardingType.Time:
                    {
                        var field = config.TimeConfig?.TimeField;
                        var timeValue = GetRequiredValue(config, valueAccessor, field);
                        return ResolveTime(config, Convert.ToDateTime(timeValue, CultureInfo.InvariantCulture));
                    }

                case ShardingType.Hash:
                    {
                        var field = config.HashConfig?.HashField;
                        var hashValue = GetRequiredValue(config, valueAccessor, field)?.ToString();
                        return ResolveHash(config, hashValue);
                    }

                case ShardingType.List:
                    {
                        var field = config.ListConfig?.ListField;
                        var listValue = GetRequiredValue(config, valueAccessor, field)?.ToString();
                        return ResolveList(config, listValue);
                    }

                case ShardingType.Composite:
                    {
                        if (config.CompositeConfig == null)
                            throw new InvalidOperationException("组合键分表配置不能为空");

                        var values = new List<string>();
                        foreach (var field in config.CompositeConfig.CompositeFields ?? new List<string>())
                        {
                            values.Add(GetRequiredValue(config, valueAccessor, field)?.ToString());
                        }

                        return ResolveComposite(config, values);
                    }

                default:
                    throw new NotSupportedException(string.Format("暂不支持的分表类型: {0}", config.ShardingType));
            }
        }

        /// <summary>
        /// 解析时间分表名称
        /// </summary>
        public static string ResolveTime(ShardingConfig config, DateTime time)
        {
            if (config == null)
                throw new ArgumentNullException(nameof(config));

            switch (config.TimeConfig?.Granularity ?? TimeGranularity.Month)
            {
                case TimeGranularity.Day:
                    return FormatTableName(config, "{0:yyyyMMdd}", time);
                case TimeGranularity.Week:
                    return FormatTableName(config, "{0:yyyy}W{1:D2}", time, GetWeekOfYear(time));
                case TimeGranularity.Month:
                    return FormatTableName(config, "{0:yyyyMM}", time);
                case TimeGranularity.Quarter:
                    return FormatTableName(config, "{0:yyyy}Q{1}", time, (time.Month - 1) / 3 + 1);
                case TimeGranularity.Year:
                    return FormatTableName(config, "{0:yyyy}", time);
                default:
                    return FormatTableName(config, "{0:yyyyMMdd}", time);
            }
        }

        /// <summary>
        /// 解析哈希分表名称
        /// </summary>
        public static string ResolveHash(ShardingConfig config, string hashValue)
        {
            if (config == null)
                throw new ArgumentNullException(nameof(config));
            if (config.HashConfig == null)
                throw new InvalidOperationException("哈希分表配置不能为空");
            if (string.IsNullOrEmpty(hashValue))
                throw new InvalidOperationException(string.Format("字段 {0} 的值不能为空", config.HashConfig.HashField));

            return FormatTableName(config, "{0:D4}", ComputeShardIndex(config, hashValue));
        }

        /// <summary>
        /// 解析列表分表名称
        /// </summary>
        public static string ResolveList(ShardingConfig config, string value)
        {
            if (config == null)
                throw new ArgumentNullException(nameof(config));
            if (config.ListConfig == null)
                throw new InvalidOperationException("列表分表配置不能为空");
            if (string.IsNullOrEmpty(value))
                throw new InvalidOperationException(string.Format("字段 {0} 的值不能为空", config.ListConfig.ListField));

            string suffix;
            if (config.ListConfig.ValueMapping != null && config.ListConfig.ValueMapping.TryGetValue(value, out suffix))
            {
                return FormatTableName(config, "{0}", suffix);
            }

            return FormatTableName(config, "{0}", value);
        }

        /// <summary>
        /// 解析组合键分表名称
        /// </summary>
        public static string ResolveComposite(ShardingConfig config, IList<string> values)
        {
            if (config == null)
                throw new ArgumentNullException(nameof(config));
            if (config.CompositeConfig == null)
                throw new InvalidOperationException("组合键分表配置不能为空");
            if (values == null || values.Count == 0)
                throw new InvalidOperationException("组合字段列表不能为空");

            var separator = config.CompositeConfig.Separator ?? "_";
            var combinedValue = string.Join(separator, values);

            if (config.CompositeConfig.UseHash)
            {
                var shardCount = config.CompositeConfig.ShardCount <= 0 ? 1 : config.CompositeConfig.ShardCount;
                return FormatTableName(config, "{0:D4}", NonNegativeHash(combinedValue) % shardCount);
            }

            return FormatTableName(config, "{0}", combinedValue);
        }

        internal static string FormatTableName(ShardingConfig config, string defaultSuffixFormat, params object[] values)
        {
            var suffixFormat = string.IsNullOrEmpty(config.SuffixFormat) ? defaultSuffixFormat : config.SuffixFormat;
            return string.Format("{0}_{1}", config.BaseTableName, string.Format(suffixFormat, values));
        }

        /// <summary>
        /// 计算哈希分片索引（非负）
        /// </summary>
        public static int ComputeShardIndex(ShardingConfig config, string hashValue)
        {
            if (config?.HashConfig == null)
                throw new InvalidOperationException("哈希分表配置不能为空");

            var shardCount = config.HashConfig.ShardCount <= 0 ? 1 : config.HashConfig.ShardCount;
            return ComputeHash(config, hashValue) % shardCount;
        }

        /// <summary>
        /// 获取 ISO 周序号
        /// </summary>
        public static int GetWeekOfYear(DateTime time)
        {
            return CultureInfo.InvariantCulture.Calendar.GetWeekOfYear(
                time,
                CalendarWeekRule.FirstFourDayWeek,
                DayOfWeek.Monday);
        }

        private static object GetRequiredValue(ShardingConfig config, Func<string, object> valueAccessor, string field)
        {
            if (string.IsNullOrEmpty(field))
                throw new InvalidOperationException("分表字段未配置");

            var value = valueAccessor(field);
            if (value == null || value == DBNull.Value)
                throw new InvalidOperationException(string.Format("字段 {0} 的值不能为空", field));

            return value;
        }

        private static int ComputeHash(ShardingConfig config, string value)
        {
            switch (config.HashConfig.Algorithm)
            {
                case HashAlgorithm.Consistent:
                    return NonNegativeHash(value);
                case HashAlgorithm.Crc32:
                    return HashCrc32(value);
                default:
                    return HashModulo(value);
            }
        }

        private static int HashModulo(string value)
        {
            int hash = 0;
            foreach (var c in value)
            {
                hash = (hash * 31 + c) & 0x7FFFFFFF;
            }
            return hash;
        }

        private static int NonNegativeHash(string value)
        {
            using (var md5 = MD5.Create())
            {
                var hashBytes = md5.ComputeHash(Encoding.UTF8.GetBytes(value));
                var hash = BitConverter.ToInt32(hashBytes, 0);
                return hash == int.MinValue ? 0 : Math.Abs(hash);
            }
        }

        private static int HashCrc32(string value)
        {
            var crc32 = new Crc32();
            var hashBytes = crc32.ComputeHash(Encoding.UTF8.GetBytes(value));
            var hash = BitConverter.ToInt32(hashBytes, 0);
            return hash == int.MinValue ? 0 : Math.Abs(hash);
        }
    }
}
