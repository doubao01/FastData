using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace FastData.Sharding.Strategies
{
    /// <summary>
    /// 组合键分表策略
    /// 适用场景：多个字段组合唯一标识记录
    /// </summary>
    public class CompositeShardingStrategy : IShardingStrategy
    {
        /// <summary>
        /// 获取分片策略的名称
        /// </summary>
        public string Name => "CompositeSharding";

        /// <summary>
        /// 获取分片策略的类型
        /// </summary>
        public ShardingType Type => ShardingType.Composite;

        /// <summary>
        /// 根据实体获取分表名称
        /// </summary>
        /// <param name="config">分片配置</param>
        /// <param name="entity">实体对象</param>
        /// <returns>表名</returns>
        public string GetTableName(ShardingConfig config, object entity)
        {
            if (config.CompositeConfig == null)
                throw new InvalidOperationException("组合键分表配置不能为空");

            var compositeFields = config.CompositeConfig.CompositeFields;
            if (compositeFields == null || compositeFields.Count == 0)
                throw new InvalidOperationException("组合字段列表不能为空");

            var entityType = entity.GetType();
            var values = new List<string>();

            foreach (var field in compositeFields)
            {
                var property = entityType.GetProperty(field);
                if (property == null)
                    throw new InvalidOperationException(string.Format("实体类型 {0} 中找不到字段 {1}", entityType.Name, field));

                var value = property.GetValue(entity)?.ToString();
                if (string.IsNullOrEmpty(value))
                    throw new InvalidOperationException(string.Format("字段 {0} 的值不能为空", field));

                values.Add(value);
            }

            return ShardingNameResolver.ResolveComposite(config, values);
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

            if (config.CompositeConfig == null)
                return result;

            var compositeFields = config.CompositeConfig.CompositeFields;
            var values = new List<string>();
            var allFieldsPresent = true;

            // 检查查询条件是否包含所有组合字段
            foreach (var field in compositeFields)
            {
                if (queryParams.ContainsKey(field) && queryParams[field] != null)
                {
                    values.Add(queryParams[field].ToString());
                }
                else
                {
                    allFieldsPresent = false;
                    break;
                }
            }

            // 如果包含所有组合字段，直接定位到分表
            if (allFieldsPresent && values.Count > 0)
            {
                result.Add(ShardingNameResolver.ResolveComposite(config, values));
                return result;
            }

            // 没有所有组合字段条件，返回所有分表
            return GetAllTableNames(config);
        }

        /// <summary>
        /// 获取所有分表名称
        /// </summary>
        public List<string> GetAllTableNames(ShardingConfig config)
        {
            var result = new List<string>();

            if (config.CompositeConfig == null)
                return result;

            if (config.CompositeConfig.UseHash)
            {
                // 使用哈希方式，返回所有分表
                for (int i = 0; i < config.CompositeConfig.ShardCount; i++)
                {
                    result.Add(string.Format("{0}_{1:D4}", config.BaseTableName, i));
                }
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
            return true;
        }

        #region 私有方法

        #endregion
    }
}
