using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using FastData.Base;
using FastData.Config;
using FastData.Context;
using FastData.Core.Base;
using FastData.Infrastructure;
using FastData.Model;
using FastData.Property;
using FastData.Sharding;
using FastUntility.Page;

namespace FastData
{
    /// <summary>
    /// FastData 分片读取操作助手（静态方法）
    /// 
    /// 职责：
    /// 1. 分片表查询（根据分片策略自动路由到对应的表）
    /// 2. 分片分页查询
    /// 3. 合并多个分片表的查询结果
    /// 
    /// 使用示例：
    /// <code>
    /// // 分片查询（自动路由到对应的分片表）
    /// var results = ShardingReadHelper.Query&lt;Order&gt;(
    ///     predicate: o =&gt; o.CreateTime &gt; DateTime.Now.AddMonths(-3),
    ///     queryParams: new Dictionary&lt;string, object&gt; { { "CreateTime", DateTime.Now.AddMonths(-3) } },
    ///     key: "db1"
    /// );
    /// 
    /// // 分片分页查询
    /// var page = ShardingReadHelper.QueryPage&lt;Order&gt;(
    ///     pModel: new PageModel { PageIndex = 1, PageSize = 10 },
    ///     predicate: o =&gt; o.CreateTime &gt; DateTime.Now.AddMonths(-3),
    ///     queryParams: new Dictionary&lt;string, object&gt; { { "CreateTime", DateTime.Now.AddMonths(-3) } },
    ///     key: "db1"
    /// );
    /// 
    /// // 推荐使用 FastDataClient 代替
    /// var client = new FastDataClient("db1");
    /// var results = client.ShardQuery&lt;Order&gt;(o =&gt; o.CreateTime &gt; DateTime.Now.AddMonths(-3));
    /// </code>
    /// 
    /// 相关类：
    /// - ShardingWriteHelper: 分片写入操作
    /// - FastDataClient: 统一门面（推荐，整合所有功能）
    /// - ShardingManager: 分片管理器
    /// - IShardingStrategy: 分片策略接口
    /// </summary>
    public static class ShardingReadHelper
    {
        private static DataQuery<T> BuildShardedQuery<T>(Expression<Func<T, bool>> predicate, string tableName, string key) where T : class, new()
        {
            var query = FastRead.Query<T>(predicate, key: key);
            var alias = predicate != null && predicate.Parameters.Count != 0 ? predicate.Parameters[0].Name : "t";
            query.Table.Clear();
            query.Table.Add(string.Format("{0} {1}", ShardingSqlHelper.FormatTableName(tableName, query.Config), alias));
            return query;
        }

        /// <summary>
        /// 从分表读取数据
        /// </summary>
        /// <typeparam name="T">实体类型</typeparam>
        /// <param name="predicate">查询条件</param>
        /// <param name="queryParams">分表查询参数</param>
        /// <param name="key">数据库 Key</param>
        /// <returns>合并后的数据列表</returns>
        public static List<T> Query<T>(
            Expression<Func<T, bool>> predicate,
            Dictionary<string, object> queryParams,
            string key = null) where T : class, new()
        {
            return Query<T>(predicate, queryParams, key, null);
        }

        internal static List<T> Query<T>(
            Expression<Func<T, bool>> predicate,
            Dictionary<string, object> queryParams,
            string key,
            ShardingConfig configOverride) where T : class, new()
        {
            if (configOverride == null && !ShardingManager.IsShardingEnabled<T>())
            {
                throw new InvalidOperationException(string.Format("Entity type {0} is not configured for sharding.", typeof(T).Name));
            }

            var tableNames = ShardingManager.GetTableNames<T>(queryParams, configOverride);
            var allResults = new List<T>();

            foreach (var tableName in tableNames)
            {
                key = key ?? FastDb.CurrentKey;
                using (var db = new DataContext(key))
                {
                    var query = BuildShardedQuery(predicate, tableName, key);
                    var results = db.GetList<T>(query);
                    allResults.AddRange(results.List);
                }
            }

            return allResults;
        }

        /// <summary>
        /// 从分表读取数据（分页）
        /// </summary>
        /// <typeparam name="T">实体类型</typeparam>
        /// <param name="predicate">查询条件</param>
        /// <param name="queryParams">分表查询参数</param>
        /// <param name="pageIndex">页码</param>
        /// <param name="pageSize">每页大小</param>
        /// <param name="key">数据库 Key</param>
        /// <returns>分页数据</returns>
        public static PageResult<T> QueryPage<T>(
            Expression<Func<T, bool>> predicate,
            Dictionary<string, object> queryParams,
            int pageIndex,
            int pageSize,
            string key = null) where T : class, new()
        {
            return QueryPage<T>(predicate, queryParams, pageIndex, pageSize, key, null);
        }

        internal static PageResult<T> QueryPage<T>(
            Expression<Func<T, bool>> predicate,
            Dictionary<string, object> queryParams,
            int pageIndex,
            int pageSize,
            string key,
            ShardingConfig configOverride) where T : class, new()
        {
            if (configOverride == null && !ShardingManager.IsShardingEnabled<T>())
            {
                throw new InvalidOperationException(string.Format("Entity type {0} is not configured for sharding.", typeof(T).Name));
            }

            var tableNames = ShardingManager.GetTableNames<T>(queryParams, configOverride);
            var allResults = new List<T>();

            foreach (var tableName in tableNames)
            {
                key = key ?? FastDb.CurrentKey;
                using (var db = new DataContext(key))
                {
                    var query = BuildShardedQuery(predicate, tableName, key);
                    var results = db.GetList<T>(query);
                    allResults.AddRange(results.List);
                }
            }

            // 手动分页
            var totalCount = allResults.Count;
            var pagedResults = allResults
                .Skip((pageIndex - 1) * pageSize)
                .Take(pageSize)
                .ToList();

            var pageResult = new PageResult<T>();
            pageResult.pModel.PageId = pageIndex;
            pageResult.pModel.PageSize = pageSize;
            pageResult.pModel.TotalRecord = totalCount;
            pageResult.pModel.TotalPage = (int)Math.Ceiling((double)totalCount / pageSize);
            pageResult.list = pagedResults;

            return pageResult;
        }
    }

    /// <summary>
    /// FastData 分片写入操作助手（静态方法）
    /// 
    /// 职责：
    /// 1. 分片表数据添加（根据分片策略自动路由到对应的表）
    /// 2. 分片表批量添加
    /// 3. 分片表数据删除
    /// 4. 分片表数据更新
    /// 
    /// 使用示例：
    /// <code>
    /// // 分片添加
    /// var result = ShardingWriteHelper.Add&lt;Order&gt;(order, key: "db1");
    /// 
    /// // 分片批量添加
    /// var result = ShardingWriteHelper.AddList&lt;Order&gt;(orders, key: "db1");
    /// 
    /// // 分片删除
    /// var result = ShardingWriteHelper.Delete&lt;Order&gt;(
    ///     predicate: o =&gt; o.CreateTime &lt; DateTime.Now.AddYears(-1),
    ///     key: "db1"
    /// );
    /// 
    /// // 分片更新
    /// var result = ShardingWriteHelper.Update&lt;Order&gt;(
    ///     entity: new Order { Status = "Completed" },
    ///     predicate: o =&gt; o.Id == orderId,
    ///     key: "db1"
    /// );
    /// 
    /// // 推荐使用 FastDataClient 代替
    /// var client = new FastDataClient("db1");
    /// var result = client.ShardAdd(order);
    /// </code>
    /// 
    /// 相关类：
    /// - ShardingReadHelper: 分片读取操作
    /// - FastDataClient: 统一门面（推荐，整合所有功能）
    /// - ShardingManager: 分片管理器
    /// - IShardingStrategy: 分片策略接口
    /// </summary>
    public static class ShardingWriteHelper
    {
        /// <summary>
        /// 向分表写入数据
        /// </summary>
        /// <typeparam name="T">实体类型</typeparam>
        /// <param name="entity">实体对象</param>
        /// <param name="key">数据库 Key</param>
        /// <returns>写入结果</returns>
        public static WriteReturn Add<T>(T entity, string key = null) where T : class, new()
        {
            if (!ShardingManager.IsShardingEnabled<T>())
            {
                throw new InvalidOperationException(string.Format("Entity type {0} is not configured for sharding.", typeof(T).Name));
            }

            key = key ?? FastDb.CurrentKey;
            var tableName = ShardingManager.GetTableName(entity);
            using (var db = new DataContext(key))
            {
                var insert = BaseModel.InsertToSql(entity, db.config);
                return ShardingSqlHelper.ExecuteWrite<T>(db, insert, tableName).WriteReturn;
            }
        }

        /// <summary>
        /// 向分表批量写入数据
        /// </summary>
        /// <typeparam name="T">实体类型</typeparam>
        /// <param name="entities">实体列表</param>
        /// <param name="key">数据库 Key</param>
        /// <returns>写入结果</returns>
        public static WriteReturn AddList<T>(List<T> entities, string key = null) where T : class, new()
        {
            if (!ShardingManager.IsShardingEnabled<T>())
            {
                throw new InvalidOperationException(string.Format("Entity type {0} is not configured for sharding.", typeof(T).Name));
            }

            key = key ?? FastDb.CurrentKey;
            var lastResult = new WriteReturn { IsSuccess = true };

            foreach (var group in entities.GroupBy(ShardingManager.GetTableName))
            {
                foreach (var entity in group)
                {
                    using (var db = new DataContext(key))
                    {
                        var insert = BaseModel.InsertToSql(entity, db.config);
                        lastResult = ShardingSqlHelper.ExecuteWrite<T>(db, insert, group.Key).WriteReturn;
                        if (!lastResult.IsSuccess)
                            return lastResult;
                    }
                }
            }

            return lastResult;
        }

        /// <summary>
        /// 从分表删除数据
        /// </summary>
        /// <typeparam name="T">实体类型</typeparam>
        /// <param name="predicate">删除条件</param>
        /// <param name="queryParams">分表查询参数</param>
        /// <param name="key">数据库 Key</param>
        /// <returns>删除结果</returns>
        public static WriteReturn Delete<T>(
            Expression<Func<T, bool>> predicate,
            Dictionary<string, object> queryParams,
            string key = null) where T : class, new()
        {
            if (!ShardingManager.IsShardingEnabled<T>())
            {
                throw new InvalidOperationException(string.Format("Entity type {0} is not configured for sharding.", typeof(T).Name));
            }

            var tableNames = ShardingManager.GetTableNames<T>(queryParams);
            var lastResult = new WriteReturn();

            foreach (var tableName in tableNames)
            {
                key = key ?? FastDb.CurrentKey;
                using (var db = new DataContext(key))
                {
                    var visitModel = VisitExpression.LambdaWhere<T>(predicate, db.config);
                    var whereClause = string.IsNullOrEmpty(visitModel.Where)
                        ? string.Empty
                        : string.Format(" where {0}", visitModel.Where.Replace(string.Format("{0}.", predicate.Parameters[0].Name), string.Empty));
                    var delete = new OptionModel
                    {
                        IsSuccess = visitModel.IsSuccess,
                        Param = visitModel.Param,
                        Sql = string.Format("delete from {0}{1}", ShardingSqlHelper.FormatTableName(tableName, db.config), whereClause)
                    };
                    lastResult = ShardingSqlHelper.ExecuteWrite<T>(db, delete, tableName).WriteReturn;
                }
            }

            return lastResult;
        }

        /// <summary>
        /// 更新分表数据
        /// </summary>
        /// <typeparam name="T">实体类型</typeparam>
        /// <param name="entity">实体对象</param>
        /// <param name="predicate">更新条件</param>
        /// <param name="field">要更新的字段</param>
        /// <param name="key">数据库 Key</param>
        /// <returns>更新结果</returns>
        public static WriteReturn Update<T>(T entity, Expression<Func<T, bool>> predicate, Expression<Func<T, object>> field = null, string key = null) where T : class, new()
        {
            if (!ShardingManager.IsShardingEnabled<T>())
            {
                throw new InvalidOperationException(string.Format("Entity type {0} is not configured for sharding.", typeof(T).Name));
            }

            key = key ?? FastDb.CurrentKey;
            var tableName = ShardingManager.GetTableName(entity);
            using (var db = new DataContext(key))
            {
                var update = ShardingSqlHelper.BuildUpdate(entity, field, tableName, db.config);
                if (update.IsSuccess)
                {
                    var visitModel = VisitExpression.LambdaWhere<T>(predicate, db.config);
                    update.Sql = string.Format("{0} {1}", update.Sql, string.IsNullOrEmpty(visitModel.Where) ? string.Empty : string.Format("where {0}", visitModel.Where.Replace(string.Format("{0}.", predicate.Parameters[0].Name), string.Empty)));
                    update.Param = Parameter.ParamMerge(update.Param, visitModel.Param);
                    update.IsSuccess = visitModel.IsSuccess;
                }

                return ShardingSqlHelper.ExecuteWrite<T>(db, update, tableName).WriteReturn;
            }
        }
    }

    internal static class ShardingSqlHelper
    {
        internal static OptionModel BuildUpdate<T>(T entity, Expression<Func<T, object>> field, string tableName, ConfigModel config) where T : class, new()
        {
            var result = new OptionModel { IsCache = config.IsPropertyCache };
            var entityGetter = new Property.DynamicGet<T>();
            var factory = DbProviderAutoRegistrar.GetFactory(config.ProviderName);
            var sqlBuilder = new StringBuilder();
            sqlBuilder.AppendFormat("update {0} set", FormatTableName(tableName, config));

            try
            {
                if (field == null)
                {
                    foreach (var property in PropertyCache.GetPropertiesCached<T>())
                        AppendSet(sqlBuilder, result, property.Name, entityGetter.GetValue(entity, property.Name, config.IsPropertyCache), config, factory);
                }
                else if (field.Body is NewExpression newExpression)
                {
                    foreach (var member in newExpression.Members)
                        AppendSet(sqlBuilder, result, member.Name, entityGetter.GetValue(entity, member.Name, config.IsPropertyCache), config, factory);
                }
                else
                {
                    result.Message = "Shard update field selector must be an anonymous object expression.";
                    result.IsSuccess = false;
                    return result;
                }

                if (result.Param.Count == 0)
                {
                    result.Message = "Shard update requires at least one field.";
                    result.IsSuccess = false;
                    return result;
                }

                result.Sql = sqlBuilder.ToString().Substring(0, sqlBuilder.Length - 1);
                result.IsSuccess = true;
                return result;
            }
            catch (Exception ex)
            {
                result.Message = ex.Message;
                result.Sql = sqlBuilder.ToString();
                result.IsSuccess = false;
                return result;
            }
        }

        private static void AppendSet(StringBuilder sqlBuilder, OptionModel result, string fieldName, object value, ConfigModel config, DbProviderFactory factory)
        {
            sqlBuilder.AppendFormat(" {0}={1}{0},", fieldName, config.Flag);
            var parameter = factory.CreateParameter();
            parameter.ParameterName = fieldName;
            parameter.Value = value ?? (object)DBNull.Value;
            result.Param.Add(parameter);
        }

        internal static DataReturn ExecuteWrite<T>(DataContext db, OptionModel option, string tableName) where T : class, new()
        {
            if (option != null && option.IsSuccess)
                option.Sql = ReplaceBaseTableName<T>(option.Sql, tableName, db.config);

            return db.ExecuteSql(option?.Sql ?? string.Empty, option?.Param?.ToArray(), isAop: false);
        }

        internal static string ReplaceBaseTableName<T>(string sql, string tableName, ConfigModel config) where T : class, new()
        {
            if (string.IsNullOrEmpty(sql))
                return sql;

            var baseTableName = TableNameHelper.GetTableName<T>(config);
            var shardTableName = FormatTableName(tableName, config);
            var replacements = new[]
            {
                new { Prefix = "insert into ", Suffix = " (" },
                new { Prefix = "update ", Suffix = " set" },
                new { Prefix = "delete from ", Suffix = " " },
                new { Prefix = "delete ", Suffix = " " }
            };

            foreach (var replacement in replacements)
            {
                var token = string.Concat(replacement.Prefix, baseTableName, replacement.Suffix);
                var index = sql.IndexOf(token, StringComparison.OrdinalIgnoreCase);
                if (index >= 0)
                {
                    return string.Concat(
                        sql.Substring(0, index),
                        replacement.Prefix,
                        shardTableName,
                        replacement.Suffix,
                        sql.Substring(index + token.Length));
                }
            }

            return sql;
        }

        internal static string FormatTableName(string tableName, ConfigModel config)
        {
            if (string.IsNullOrEmpty(tableName) || config == null)
                return tableName;

            if (tableName.StartsWith("`", StringComparison.Ordinal) || tableName.StartsWith("[", StringComparison.Ordinal))
                return tableName;

            switch (config.DbType)
            {
                case DbTypes.DataDbType.MySql:
                    return string.Format("`{0}`", tableName.Trim('`'));
                case DbTypes.DataDbType.PostgreSql:
                    return tableName.Trim('`', '[', ']').ToLowerInvariant();
                default:
                    return tableName;
            }
        }
    }
}
