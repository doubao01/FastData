using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Text;
using FastData.Base;
using FastData.Config;
using FastData.Context;
using FastData.Model;
using FastData.Sharding.Strategies;

namespace FastData.Sharding
{
    /// <summary>
    /// FastData 分片管理器
    /// 
    /// 职责：
    /// 1. 分片策略注册与管理
    /// 2. 分片配置注册与查询
    /// 3. 分片表名生成
    /// 4. 分片类型枚举定义
    /// 
    /// 使用示例：
    /// <code>
    /// // 注册分片配置
    /// ShardingManager.Register&lt;Order&gt;(new ShardingConfig
    /// {
    ///     ShardingType = ShardingType.Time,
    ///     TableNameFormat = "Orders_{0:yyyyMM}",
    ///     TimeField = "CreateTime",
    ///     TimeRange = TimeRange.Month
    /// });
    /// 
    /// // 检查是否启用分片
    /// bool isEnabled = ShardingManager.IsShardingEnabled&lt;Order&gt;();
    /// 
    /// // 获取分片表名
    /// var tableNames = ShardingManager.GetTableNames&lt;Order&gt;(queryParams);
    /// </code>
    /// 
    /// 相关类：
    /// - IShardingStrategy: 分片策略接口
    /// - ShardingConfig: 分片配置
    /// - TimeShardingStrategy: 时间分片策略
    /// - HashShardingStrategy: 哈希分片策略
    /// - ListShardingStrategy: 列表分片策略
    /// - CompositeShardingStrategy: 组合分片策略
    /// - ShardingReadHelper: 分片读取操作
    /// - ShardingWriteHelper: 分片写入操作
    /// </summary>
    public class ShardingManager
    {
        private static readonly ConcurrentDictionary<ShardingType, IShardingStrategy> _strategies = new ConcurrentDictionary<ShardingType, IShardingStrategy>();
        private static readonly ConcurrentDictionary<System.Type, ShardingConfig> _configs = new ConcurrentDictionary<System.Type, ShardingConfig>();

        static ShardingManager()
        {
            // 注册默认策略
            RegisterStrategy(new TimeShardingStrategy());
            RegisterStrategy(new HashShardingStrategy());
            RegisterStrategy(new ListShardingStrategy());
            RegisterStrategy(new CompositeShardingStrategy());
            RegisterStrategy(new QueryFrequencyShardingStrategy());
        }

        /// <summary>
        /// 注册分表策略
        /// </summary>
        public static void RegisterStrategy(IShardingStrategy strategy)
        {
            if (strategy == null)
                throw new ArgumentNullException(nameof(strategy));

            _strategies[strategy.Type] = strategy;
        }

        /// <summary>
        /// 配置实体分表
        /// </summary>
        public static void Configure<T>(ShardingConfig config) where T : class
        {
            if (config == null)
                throw new ArgumentNullException(nameof(config));

            config.EntityType = typeof(T);

            if (string.IsNullOrEmpty(config.BaseTableName))
                config.BaseTableName = Base.TableNameHelper.GetTableName<T>();

            // 缓存策略引用，避免后续每次查询时重复查找
            if (_strategies.TryGetValue(config.ShardingType, out var strategy))
                config.CachedStrategy = strategy;

            _configs[typeof(T)] = config;
        }

        /// <summary>
        /// 获取实体分表配置
        /// </summary>
        public static ShardingConfig GetConfig<T>() where T : class
        {
            return GetConfig(typeof(T));
        }

        /// <summary>
        /// 获取实体分表配置
        /// </summary>
        public static ShardingConfig GetConfig(System.Type entityType)
        {
            _configs.TryGetValue(entityType, out var config);
            return config;
        }

        /// <summary>
        /// 检查实体是否配置了分表
        /// </summary>
        public static bool IsShardingEnabled<T>() where T : class
        {
            return GetConfig<T>() != null;
        }

        /// <summary>
        /// 检查实体是否配置了分表
        /// </summary>
        public static bool IsShardingEnabled(System.Type entityType)
        {
            return GetConfig(entityType) != null;
        }

        /// <summary>
        /// 根据实体获取分表名称
        /// </summary>
        public static string GetTableName<T>(T entity) where T : class
        {
            var config = GetConfig<T>();
            if (config == null)
                return Base.TableNameHelper.GetTableName<T>();

            var strategy = config.CachedStrategy ?? GetStrategy(config.ShardingType);
            return strategy.GetTableName(config, entity);
        }

        /// <summary>
        /// 根据查询条件获取所有可能的分表名称
        /// </summary>
        public static List<string> GetTableNames<T>(Dictionary<string, object> queryParams) where T : class
        {
            var config = GetConfig<T>();
            if (config == null)
                return new List<string> { Base.TableNameHelper.GetTableName<T>() };

            var strategy = config.CachedStrategy ?? GetStrategy(config.ShardingType);
            return strategy.GetTableNames(config, queryParams);
        }

        /// <summary>
        /// 获取所有分表名称
        /// </summary>
        public static List<string> GetAllTableNames<T>() where T : class
        {
            var config = GetConfig<T>();
            if (config == null)
                return new List<string> { Base.TableNameHelper.GetTableName<T>() };

            var strategy = config.CachedStrategy ?? GetStrategy(config.ShardingType);
            return strategy.GetAllTableNames(config);
        }

        /// <summary>
        /// 获取分表策略
        /// </summary>
        public static IShardingStrategy GetStrategy(ShardingType type)
        {
            if (_strategies.TryGetValue(type, out var strategy))
                return strategy;

            throw new InvalidOperationException(string.Format("未注册的分表策略类型: {0}", type));
        }

        /// <summary>
        /// 创建分表
        /// </summary>
        public static bool CreateTable<T>(string tableName) where T : class
        {
            var config = GetConfig<T>();
            if (config == null)
                return false;

            return CreateTable(config, tableName);
        }

        /// <summary>
        /// 创建分表
        /// </summary>
        /// <param name="config">分片配置</param>
        /// <param name="tableName">表名</param>
        /// <returns>是否成功</returns>
        public static bool CreateTable(ShardingConfig config, string tableName)
        {
            try
            {
                var dbConfig = DataConfig.GetConfig(config.DatabaseKey);
                if (dbConfig == null)
                    return false;

                using (var db = new DataContext(config.DatabaseKey))
                {
                    var sql = GenerateCreateTableSql(config, tableName);
                    db.ExecuteSql(sql);
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 生成创建分表的 SQL
        /// </summary>
        /// <param name="config">分片配置</param>
        /// <param name="tableName">表名</param>
        /// <returns>SQL语句</returns>
        public static string GenerateCreateTableSql(ShardingConfig config, string tableName)
        {
            // 这里需要根据实体类型生成建表 SQL
            // 实际实现应该从实体的属性映射获取字段定义
            return string.Format("CREATE TABLE IF NOT EXISTS [{0}] (Id INT PRIMARY KEY)", tableName);
        }

        /// <summary>
        /// 自动创建分表（如果配置了自动创建）
        /// </summary>
        public static void AutoCreateTable<T>(T entity) where T : class
        {
            var config = GetConfig<T>();
            if (config == null || !config.AutoCreateTable)
                return;

            var tableName = GetTableName(entity);
            CreateTable<T>(tableName);
        }

        /// <summary>
        /// 获取分表的数据库上下文
        /// </summary>
        public static DataContext GetDbContext<T>(T entity) where T : class
        {
            var config = GetConfig<T>();
            if (config == null)
                return new DataContext();

            return new DataContext(config.DatabaseKey);
        }

        private const string ShardingNotImplementedHint =
            "此前版本会静默返回成功/空集合，导致调用方误认为操作已完成。为避免数据静默丢失，现改为显式抛出异常。";

        /// <summary>
        /// 从分表查询数据
        /// </summary>
        /// <exception cref="NotSupportedException">该方法尚未实现</exception>
        public static List<T> QueryFromShard<T>(string tableName, DataQuery query) where T : class, new()
        {
            // 待实现：需要 DataContext 支持动态表名替换
            throw new NotSupportedException(
                "QueryFromShard 尚未实现：分表 " + tableName + " 的查询需要 DataContext 支持动态表名替换。" +
                ShardingNotImplementedHint);
        }

        /// <summary>
        /// 向分表插入数据
        /// </summary>
        /// <exception cref="NotSupportedException">该方法尚未实现</exception>
        public static bool InsertToShard<T>(T entity) where T : class
        {
            // 待实现：需要 DataContext 支持动态表名替换
            throw new NotSupportedException(
                "InsertToShard 尚未实现：分表插入需要 DataContext 支持动态表名替换。" +
                ShardingNotImplementedHint);
        }

        /// <summary>
        /// 从分表更新数据
        /// </summary>
        /// <exception cref="NotSupportedException">该方法尚未实现</exception>
        public static bool UpdateInShard<T>(T entity) where T : class
        {
            // 待实现：需要 DataContext 支持动态表名替换
            throw new NotSupportedException(
                "UpdateInShard 尚未实现：分表更新需要 DataContext 支持动态表名替换。" +
                ShardingNotImplementedHint);
        }

        /// <summary>
        /// 从分表删除数据
        /// </summary>
        /// <exception cref="NotSupportedException">该方法尚未实现</exception>
        public static bool DeleteFromShard<T>(T entity) where T : class
        {
            // 待实现：需要 DataContext 支持动态表名替换
            throw new NotSupportedException(
                "DeleteFromShard 尚未实现：分表删除需要 DataContext 支持动态表名替换。" +
                ShardingNotImplementedHint);
        }

        /// <summary>
        /// 清除所有配置
        /// </summary>
        public static void Clear()
        {
            _configs.Clear();
        }
    }
}
