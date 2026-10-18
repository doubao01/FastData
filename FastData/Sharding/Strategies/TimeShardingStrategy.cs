using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Text;
using FastData.Base;
using FastData.Config;
using FastData.Context;

namespace FastData.Sharding.Strategies
{
    /// <summary>
    /// 时间分表策略
    /// 适用场景：日志表、流水表、操作记录等有时间字段的表
    /// </summary>
    public class TimeShardingStrategy : IShardingStrategy
    {
        /// <summary>
        /// 获取分片策略的名称
        /// </summary>
        public string Name => "TimeSharding";

        /// <summary>
        /// 获取分片策略的类型
        /// </summary>
        public ShardingType Type => ShardingType.Time;

        /// <summary>
        /// 根据实体获取分表名称
        /// </summary>
        /// <param name="config">分片配置</param>
        /// <param name="entity">实体对象</param>
        /// <returns>表名</returns>
        public string GetTableName(ShardingConfig config, object entity)
        {
            if (config.TimeConfig == null)
                throw new InvalidOperationException("时间分表配置不能为空");

            var timeField = config.TimeConfig.TimeField;
            var entityType = entity.GetType();
            var property = entityType.GetProperty(timeField);

            if (property == null)
                throw new InvalidOperationException(string.Format("实体类型 {0} 中找不到字段 {1}", entityType.Name, timeField));

            var timeValue = (DateTime)property.GetValue(entity);
            return ShardingNameResolver.ResolveTime(config, timeValue);
        }

        /// <summary>
        /// 根据查询条件获取所有可能的分表名称
        /// </summary>
        /// <param name="config">分片配置</param>
        /// <param name="queryParams">查询参数</param>
        /// <returns>表名列表</returns>
        public List<string> GetTableNames(ShardingConfig config, Dictionary<string, object> queryParams)
        {
            var result = new List<string>();

            if (config.TimeConfig == null)
                return result;

            var timeField = config.TimeConfig.TimeField;
            DateTime? startTime = null;
            DateTime? endTime = null;

            // 从查询参数中提取时间范围
            if (queryParams.ContainsKey(timeField + "_Start"))
                startTime = Convert.ToDateTime(queryParams[timeField + "_Start"]);
            else if (queryParams.ContainsKey(timeField))
                startTime = Convert.ToDateTime(queryParams[timeField]);

            if (queryParams.ContainsKey(timeField + "_End"))
                endTime = Convert.ToDateTime(queryParams[timeField + "_End"]);

            // 如果没有时间条件，返回所有表
            if (!startTime.HasValue && !endTime.HasValue)
            {
                return GetAllTableNames(config);
            }

            // 计算时间范围内的所有分表
            var effectiveStart = startTime ?? config.TimeConfig.StartTime ?? DateTime.MinValue;
            var effectiveEnd = endTime ?? config.TimeConfig.EndTime ?? DateTime.MaxValue;

            var current = GetTimeBoundary(config.TimeConfig.Granularity, effectiveStart);
            while (current <= effectiveEnd)
            {
                result.Add(GetTableNameByTime(config, current));
                current = AddTimeGranularity(config.TimeConfig.Granularity, current);
            }

            return result;
        }

        /// <summary>
        /// 获取所有分表名称
        /// </summary>
        public List<string> GetAllTableNames(ShardingConfig config)
        {
            var result = new List<string>();

            if (config.TimeConfig?.StartTime == null)
                return result;

            var current = GetTimeBoundary(config.TimeConfig.Granularity, config.TimeConfig.StartTime.Value);
            var endTime = config.TimeConfig.EndTime ?? DateTime.Now;

            while (current <= endTime)
            {
                result.Add(GetTableNameByTime(config, current));
                current = AddTimeGranularity(config.TimeConfig.Granularity, current);

                if (result.Count >= config.MaxTableCount)
                    break;
            }

            return result;
        }

        /// <summary>
        /// 创建分表
        /// </summary>
        /// <param name="config">分片配置</param>
        /// <param name="tableName">表名</param>
        /// <returns>是否成功</returns>
        public bool CreateTable(ShardingConfig config, string tableName)
        {
            // 分表创建逻辑由 ShardingManager 统一处理
            return true;
        }

        #region 私有方法

        private string GetTableNameByTime(ShardingConfig config, DateTime time)
        {
            return ShardingNameResolver.ResolveTime(config, time);
        }

        private DateTime GetTimeBoundary(TimeGranularity granularity, DateTime time)
        {
            return granularity switch
            {
                TimeGranularity.Day => time.Date,
                TimeGranularity.Week => time.Date.AddDays(-(int)time.DayOfWeek + 1),
                TimeGranularity.Month => new DateTime(time.Year, time.Month, 1),
                TimeGranularity.Quarter => new DateTime(time.Year, ((time.Month - 1) / 3) * 3 + 1, 1),
                TimeGranularity.Year => new DateTime(time.Year, 1, 1),
                _ => time.Date
            };
        }

        private DateTime AddTimeGranularity(TimeGranularity granularity, DateTime time)
        {
            return granularity switch
            {
                TimeGranularity.Day => time.AddDays(1),
                TimeGranularity.Week => time.AddDays(7),
                TimeGranularity.Month => time.AddMonths(1),
                TimeGranularity.Quarter => time.AddMonths(3),
                TimeGranularity.Year => time.AddYears(1),
                _ => time.AddDays(1)
            };
        }

        #endregion
    }
}
