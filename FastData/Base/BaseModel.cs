using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using FastData.DbTypes;
using FastData.Infrastructure;
using FastData.Model;
using FastData.Property;

namespace FastData.Base
{
    /// <summary>
    /// 实体模型转 SQL 类
    /// 提供将实体对象转换为 INSERT/UPDATE/DELETE SQL 语句的功能
    /// </summary>
    internal static class BaseModel
    {
        /// <summary>
        /// 主键缓存（表名 → 主键字段列表）
        /// </summary>
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, List<string>> _primaryKeyCache =
            new System.Collections.Concurrent.ConcurrentDictionary<string, List<string>>();

        #region 实体转 Update SQL
        /// <summary>
        /// 将实体转换为 UPDATE SQL 语句
        /// </summary>
        public static OptionModel UpdateToSql<T>(T entity, ConfigModel config, Expression<Func<T, object>> fieldSelector = null, DbCommand cmd = null) where T : class
        {
            var result = new OptionModel();
            var entityGetter = new Property.DynamicGet<T>();
            result.IsCache = config.IsPropertyCache;
            var tableName = TableNameHelper.GetTableName<T>(config);
            var factory = DbProviderAutoRegistrar.GetFactory(config.ProviderName);

            try
            {
                var sqlBuilder = new StringBuilder();
                sqlBuilder.AppendFormat("update {0} set", tableName);

                BuildSetClause(sqlBuilder, entity, config, fieldSelector, entityGetter, factory, result);

                // 验证主键值不为空
                var primaryKeys = GetPrimaryKeys(config, cmd, tableName);
                foreach (var primaryKey in primaryKeys)
                {
                    if (result.Param.Exists(p => p.ParameterName == primaryKey))
                    {
                        var primaryKeyValue = entityGetter.GetValue(entity, primaryKey, config.IsPropertyCache);
                        if (primaryKeyValue == null)
                        {
                            result.IsSuccess = false;
                            result.Message = string.Format("主键{0}值为空", primaryKey);
                            return result;
                        }
                    }
                }

                result.Sql = sqlBuilder.ToString().Substring(0, sqlBuilder.Length - 1);
                result.IsSuccess = true;
                return result;
            }
            catch (Exception ex)
            {
                LogException(config, ex, "UpdateToSql<T>", result.Sql);
                result.IsSuccess = false;
                return result;
            }
        }
        #endregion

        #region 实体转 Update SQL (带 Command)
        /// <summary>
        /// 将实体转换为 UPDATE SQL 语句（包含 WHERE 主键条件）
        /// </summary>
        public static OptionModel UpdateToSql<T>(DbCommand cmd, T entity, ConfigModel config, Expression<Func<T, object>> fieldSelector = null) where T : class
        {
            var result = new OptionModel();
            var entityGetter = new Property.DynamicGet<T>();
            result.IsCache = config.IsPropertyCache;
            var tableName = TableNameHelper.GetTableName<T>(config);
            var primaryKeys = GetPrimaryKeys(config, cmd, tableName);
            var factory = DbProviderAutoRegistrar.GetFactory(config.ProviderName);

            if (primaryKeys.Count == 0)
            {
                result.Message = string.Format("{0}没有主键", typeof(T).Name);
                result.IsSuccess = false;
                return result;
            }

            try
            {
                var sqlBuilder = new StringBuilder();
                sqlBuilder.AppendFormat("update {0} set", tableName);

                BuildSetClause(sqlBuilder, entity, config, fieldSelector, entityGetter, factory, result);

                // 移除末尾逗号
                result.Sql = sqlBuilder.ToString().Substring(0, sqlBuilder.Length - 1);

                // 添加 WHERE 主键条件
                AppendPrimaryKeyWhere(result, entity, config, entityGetter, factory, primaryKeys);

                result.IsSuccess = true;
                return result;
            }
            catch (Exception ex)
            {
                LogException(config, ex, "UpdateToSql<T>", result.Sql);
                result.IsSuccess = false;
                return result;
            }
        }
        #endregion

        #region 实体转 Insert SQL
        /// <summary>
        /// 将实体转换为 INSERT SQL 语句
        /// </summary>
        /// <typeparam name="T">实体类型</typeparam>
        /// <param name="entity">实体对象</param>
        /// <param name="config">数据库配置模型</param>
        /// <returns>操作结果模型</returns>
        public static OptionModel InsertToSql<T>(T entity, ConfigModel config) where T : class, new()
        {
            var columnBuilder = new StringBuilder();
            var valueBuilder = new StringBuilder();
            var entityGetter = new Property.DynamicGet<T>();
            var processedColumns = new List<MemberInfo>();
            var result = new OptionModel();

            try
            {
                if (config == null)
                    throw new ArgumentNullException("config", "数据库配置不能为空");
                if (string.IsNullOrEmpty(config.Flag))
                    throw new InvalidOperationException("config.Flag 为空 - 数据库配置未加载");
                if (string.IsNullOrEmpty(config.ProviderName))
                    throw new InvalidOperationException("config.ProviderName 为空 - 数据库配置未加载");

                columnBuilder.AppendFormat("insert into {0} (", TableNameHelper.GetTableName<T>(config));
                valueBuilder.Append(" values (");
                var factory = DbProviderAutoRegistrar.GetFactory(config.ProviderName);

                var properties = PropertyCache.GetNonIdentityProperties<T>();

                foreach (var property in properties)
                {
                    if (processedColumns.Exists(p => p.Name == property.Name))
                        continue;

                    columnBuilder.AppendFormat("{0},", property.Name);
                    valueBuilder.AppendFormat("{1}{0},", property.Name, config.Flag);

                    var propertyValue = entityGetter.GetValue(entity, property.Name, config.IsPropertyCache);
                    var parameter = factory.CreateParameter();
                    parameter.ParameterName = property.Name;
                    parameter.Value = propertyValue ?? (object)DBNull.Value;
                    result.Param.Add(parameter);
                }

                result.Sql = string.Format("{0}) {1})",
                    columnBuilder.ToString().Substring(0, columnBuilder.Length - 1),
                    valueBuilder.ToString().Substring(0, valueBuilder.Length - 1));
                result.IsSuccess = true;
                return result;
            }
            catch (Exception ex)
            {
                if (config != null && config.SqlErrorType != null && config.SqlErrorType.ToLowerInvariant() == SqlErrorType.Db)
                    DbLogTable.LogException(config, ex, "InsertToSql<T>", result.Sql);
                else
                    DbLog.LogException(config != null && config.IsOutError, config != null ? config.DbType : DataDbType.SqlServer, ex, "InsertToSql<T>", result.Sql);

                result.IsSuccess = false;
                result.Sql = ex.Message;
                return result;
            }
        }
        #endregion

        #region 实体列表转 Update SQL
        /// <summary>
        /// 将实体列表转换为 UPDATE SQL 语句（使用 DataTable 批量更新）
        /// </summary>
        /// <typeparam name="T">实体类型</typeparam>
        /// <param name="cmd">数据库命令对象</param>
        /// <param name="entityList">实体列表</param>
        /// <param name="config">数据库配置模型</param>
        /// <param name="fieldSelector">更新的字段表达式，为 null 时更新所有字段</param>
        /// <returns>操作结果模型</returns>
        public static OptionModel UpdateListToSql<T>(DbCommand cmd, List<T> entityList, ConfigModel config, Expression<Func<T, object>> fieldSelector = null) where T : class
        {
            var entityGetter = new Property.DynamicGet<T>();
            var result = new OptionModel();
            result.IsCache = config.IsPropertyCache;
            var tableName = TableNameHelper.GetTableName<T>(config);
            var primaryKeys = GetPrimaryKeys(config, cmd, tableName);
            var factory = DbProviderAutoRegistrar.GetFactory(config.ProviderName);

            if (primaryKeys.Count == 0)
            {
                result.Message = string.Format("{0}没有主键", typeof(T).Name);
                result.IsSuccess = false;
                return result;
            }

            try
            {
                result.table = BaseExecute.ToDataTable<T>(cmd, config, primaryKeys, fieldSelector);

                var sqlBuilder = new StringBuilder();
                sqlBuilder.AppendFormat("update {0} set", tableName);

                // 使用缓存的属性数组替代 GetPropertyInfo
                var properties = PropertyCache.GetPropertiesCached<T>();

                if (fieldSelector == null)
                {
                    foreach (var property in properties)
                    {
                        if (primaryKeys.Exists(k => k == property.Name))
                            continue;

                        sqlBuilder.AppendFormat(" {0}={1}{0},", property.Name, config.Flag);
                        var parameter = factory.CreateParameter();
                        parameter.ParameterName = property.Name;
                        parameter.SourceColumn = property.Name;
                        result.Param.Add(parameter);
                    }
                }
                else
                {
                    // 更新指定字段
                    foreach (var memberName in GetSelectedMemberNames(fieldSelector))
                    {
                        if (primaryKeys.Exists(k => k == memberName))
                            continue;

                        sqlBuilder.AppendFormat(" {0}={1}{0},", memberName, config.Flag);
                        var parameter = factory.CreateParameter();
                        parameter.ParameterName = memberName;
                        parameter.SourceColumn = memberName;
                        result.Param.Add(parameter);
                    }
                }

                // 移除末尾逗号
                result.Sql = sqlBuilder.ToString().Substring(0, sqlBuilder.Length - 1);

                // 添加 WHERE 主键条件
                var keyIndex = 1;
                foreach (var primaryKey in primaryKeys)
                {
                    if (keyIndex == 1)
                        result.Sql = string.Format("{0} where {1}={2}{1} ", result.Sql, primaryKey, config.Flag);
                    else
                        result.Sql = string.Format("{0} and {1}={2}{1} ", result.Sql, primaryKey, config.Flag);

                    var parameter = factory.CreateParameter();
                    parameter.ParameterName = primaryKey;
                    parameter.SourceColumn = primaryKey;
                    result.Param.Add(parameter);
                    keyIndex++;
                }

                // 填充 DataTable
                foreach (var entity in entityList)
                {
                    var row = result.table.NewRow();
                    foreach (var primaryKey in primaryKeys)
                    {
                        row[primaryKey] = entityGetter.GetValue(entity, primaryKey, true);
                    }

                    if (fieldSelector == null)
                    {
                        foreach (var property in properties)
                        {
                            row[property.Name] = entityGetter.GetValue(entity, property.Name, true);
                        }
                    }
                    else
                    {
                        foreach (var memberName in GetSelectedMemberNames(fieldSelector))
                        {
                            row[memberName] = entityGetter.GetValue(entity, memberName, true);
                        }
                    }

                    result.table.Rows.Add(row);
                }

                result.IsSuccess = true;
                return result;
            }
            catch (Exception ex)
            {
                LogException(config, ex, "UpdateListToSql<T>", result.Sql);
                result.IsSuccess = false;
                return result;
            }
        }
        #endregion

        #region 实体转 Delete SQL
        /// <summary>
        /// 将实体转换为 DELETE SQL 语句
        /// </summary>
        /// <typeparam name="T">实体类型</typeparam>
        /// <param name="cmd">数据库命令对象</param>
        /// <param name="entity">实体对象</param>
        /// <param name="config">数据库配置模型</param>
        /// <returns>操作结果模型</returns>
        public static OptionModel DeleteToSql<T>(DbCommand cmd, T entity, ConfigModel config)
        {
            var result = new OptionModel();
            var entityGetter = new Property.DynamicGet<T>();
            result.IsCache = config.IsPropertyCache;
            var tableName = TableNameHelper.GetTableName<T>(config);
            var primaryKeys = GetPrimaryKeys(config, cmd, tableName);
            var factory = DbProviderAutoRegistrar.GetFactory(config.ProviderName);

            if (primaryKeys.Count == 0)
            {
                result.Message = string.Format("{0}没有主键", typeof(T).Name);
                result.IsSuccess = false;
                return result;
            }

            try
            {
                result.Sql = string.Format("delete {0} ", tableName);
                AppendPrimaryKeyWhere(result, entity, config, entityGetter, factory, primaryKeys, useKeyIndex: false);
                result.IsSuccess = true;
                return result;
            }
            catch (Exception ex)
            {
                LogException(config, ex, "DeleteToSql<T>", result.Sql);
                result.IsSuccess = false;
                return result;
            }
        }
        #endregion

        #region 获取主键
        /// <summary>
        /// 查询表的主键字段列表
        /// </summary>
        /// <param name="config">数据库配置模型</param>
        /// <param name="cmd">数据库命令对象</param>
        /// <param name="tableName">表名</param>
        /// <returns>主键字段名列表</returns>
        public static List<string> GetPrimaryKeys(ConfigModel config, DbCommand cmd, string tableName)
        {
            var cacheKey = string.Format("{0}|{1}|{2}|{3}", config.Key, config.DbType, config.ConnStr, tableName);
            if (_primaryKeyCache.TryGetValue(cacheKey, out var cached))
                return cached;

            var primaryKeys = new List<string>();
            var savedCommandText = cmd.CommandText;
            var savedParameters = new List<DbParameter>();
            foreach (DbParameter parameter in cmd.Parameters)
                savedParameters.Add(parameter);

            // 根据不同数据库类型生成查询主键的 SQL
            switch (config.DbType)
            {
                case DataDbType.Oracle:
                    cmd.CommandText = string.Format(
                        "select a.COLUMN_NAME from all_cons_columns a,all_constraints b where a.constraint_name = b.constraint_name and b.constraint_type = 'P' and b.table_name = '{0}'",
                        tableName.ToUpper());
                    break;

                case DataDbType.SqlServer:
                    cmd.CommandText = string.Format(
                        "select column_name from INFORMATION_SCHEMA.KEY_COLUMN_USAGE where TABLE_NAME='{0}'",
                        tableName);
                    break;

                case DataDbType.MySql:
                    cmd.CommandText = string.Format(
                        "select column_name from INFORMATION_SCHEMA.KEY_COLUMN_USAGE a where TABLE_NAME='{0}' and constraint_name='PRIMARY'",
                        tableName);
                    break;

                case DataDbType.PostgreSql:
                    cmd.CommandText = string.Format(
                        "SELECT INITCAP(column_name) FROM information_schema.key_column_usage WHERE table_name='{0}' AND constraint_name LIKE '%%%pkey'",
                        tableName.ToLower());
                    break;

                case DataDbType.DB2:
                    cmd.CommandText = string.Format(
                        "select a.colname from sysibm.syskeycoluse a, syscat.tabconst b where a.tabname = b.tabname and b.tabname = '{0}' and b.type = 'P'",
                        tableName.ToUpper());
                    break;

                case DataDbType.SQLite:
                    cmd.CommandText = string.Format(
                        "SELECT name FROM pragma_table_info('{0}') WHERE pk > 0",
                        tableName);
                    break;
            }

            if (string.IsNullOrEmpty(cmd.CommandText))
                return primaryKeys;

            cmd.Parameters.Clear();

            try
            {
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        primaryKeys.Add(reader[0].ToString());
                    }
                }
            }
            finally
            {
                cmd.CommandText = savedCommandText;
                foreach (var parameter in savedParameters)
                    cmd.Parameters.Add(parameter);
            }

            _primaryKeyCache[cacheKey] = primaryKeys;
            return primaryKeys;
        }

        /// <summary>
        /// 预热主键缓存
        /// </summary>
        public static void WarmupPrimaryKeyCache(ConfigModel config, DbCommand cmd, params string[] tableNames)
        {
            foreach (var tableName in tableNames)
            {
                GetPrimaryKeys(config, cmd, tableName);
            }
        }
        #endregion

        #region 私有辅助方法
        /// <summary>
        /// 构建 UPDATE SET 子句（字段名=参数名）
        /// </summary>
        private static void BuildSetClause<T>(StringBuilder sqlBuilder, T entity, ConfigModel config,
            Expression<Func<T, object>> fieldSelector, Property.DynamicGet<T> entityGetter,
            DbProviderFactory factory, OptionModel result) where T : class
        {
            if (fieldSelector == null)
            {
                var properties = PropertyCache.GetPropertiesCached<T>();
                foreach (var property in properties)
                {
                    sqlBuilder.AppendFormat(" {0}={1}{0},", property.Name, config.Flag);
                    var parameter = factory.CreateParameter();
                    parameter.ParameterName = property.Name;
                    parameter.Value = entityGetter.GetValue(entity, property.Name, config.IsPropertyCache) ?? (object)DBNull.Value;
                    result.Param.Add(parameter);
                }
            }
            else
            {
                foreach (var memberName in GetSelectedMemberNames(fieldSelector))
                {
                    sqlBuilder.AppendFormat(" {0}={1}{0},", memberName, config.Flag);
                    var parameter = factory.CreateParameter();
                    parameter.ParameterName = memberName;
                    parameter.Value = entityGetter.GetValue(entity, memberName, config.IsPropertyCache) ?? (object)DBNull.Value;
                    result.Param.Add(parameter);
                }
            }
        }

        /// <summary>
        /// 提取字段选择器中的成员名，支持匿名对象、单字段和强制转换表达式
        /// </summary>
        internal static List<string> GetSelectedMemberNames<T>(Expression<Func<T, object>> fieldSelector)
        {
            var result = new List<string>();

            if (fieldSelector == null)
                return result;

            var body = fieldSelector.Body;

            if (body is UnaryExpression unary)
                body = unary.Operand;

            if (body is NewExpression newExpression)
            {
                foreach (var member in newExpression.Members)
                    result.Add(member.Name);
            }
            else if (body is MemberExpression memberExpression)
            {
                result.Add(memberExpression.Member.Name);
            }
            else
            {
                throw new ArgumentException("字段选择器必须是属性访问或匿名对象表达式", nameof(fieldSelector));
            }

            return result;
        }

        /// <summary>
        /// 追加 WHERE 主键条件到 SQL
        /// </summary>
        /// <param name="useKeyIndex">是否在参数名后追加序号（UPDATE 场景需要，DELETE 场景不需要）</param>
        private static void AppendPrimaryKeyWhere<T>(OptionModel result, T entity, ConfigModel config,
            Property.DynamicGet<T> entityGetter, DbProviderFactory factory, List<string> primaryKeys,
            bool useKeyIndex = true)
        {
            var keyIndex = 1;
            var sqlBuilder = new StringBuilder(result.Sql);
            foreach (var primaryKey in primaryKeys)
            {
                var primaryKeyValue = entityGetter.GetValue(entity, primaryKey, config.IsPropertyCache);

                if (primaryKeyValue == null)
                {
                    result.IsSuccess = false;
                    result.Message = string.Format("主键{0}值为空", primaryKey);
                    return;
                }

                var prefix = keyIndex == 1 ? "where" : "and";
                sqlBuilder.AppendFormat(" {0} {1}={2}{1}{3} ", prefix, primaryKey, config.Flag,
                    useKeyIndex ? keyIndex.ToString() : "");

                var parameter = factory.CreateParameter();
                parameter.ParameterName = useKeyIndex ? string.Format("{0}{1}", primaryKey, keyIndex) : primaryKey;
                parameter.Value = primaryKeyValue ?? (object)DBNull.Value;
                result.Param.Add(parameter);

                keyIndex++;
            }

            result.Sql = sqlBuilder.ToString();
        }
        #endregion

        #region 异常日志记录
        /// <summary>
        /// 统一异常日志记录
        /// </summary>
        private static void LogException<T>(ConfigModel config, Exception exception, string methodName, string sql)
        {
            try
            {
                if (config.SqlErrorType == SqlErrorType.Db)
                    DbLogTable.LogException<T>(config, exception, methodName, sql);
                else
                    DbLog.LogException<T>(config.IsOutError, config.DbType, exception, methodName, sql);
            }
            catch
            {
                // 避免日志记录失败影响主流程
            }
        }

        /// <summary>
        /// 统一异常日志记录（无泛型版本）
        /// </summary>
        private static void LogException(ConfigModel config, Exception exception, string methodName, string sql)
        {
            try
            {
                if (config.SqlErrorType == SqlErrorType.Db)
                    DbLogTable.LogException(config, exception, methodName, sql);
                else
                    DbLog.LogException(config.IsOutError, config.DbType, exception, methodName, sql);
            }
            catch
            {
                // 避免日志记录失败影响主流程
            }
        }
        #endregion
    }
}
