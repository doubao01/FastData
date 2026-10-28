using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Data.Common;
using System.Diagnostics;
using FastUntility.Page;
using FastUntility.Base;
using FastData.Base;
using FastData.Model;
using FastData.DbTypes;
using FastData.Config;
using System.Linq.Expressions;
using System.Data;
using FastData.Property;
using FastData.Aop;
using FastData.Core.Base;
using System.Threading;
using System.Threading.Tasks;

namespace FastData.Context
{
    public partial class DataContext : IDisposable
    {
        private static string BuildLambdaWhereClause<T>(Expression<Func<T, bool>> predicate, VisitModel visitModel) where T : class, new()
        {
            if (string.IsNullOrEmpty(visitModel.Where))
                return string.Empty;

            var parameterPrefix = string.Format("{0}.", predicate.Parameters[0].Name);
            return string.Format("where {0}", visitModel.Where.Replace(parameterPrefix, string.Empty));
        }

        private void SetCommandParameters(IEnumerable<DbParameter> parameters)
        {
            cmd.Parameters.Clear();

            if (parameters != null)
            {
                var list = parameters.ToArray();
                if (list.Length != 0)
                    cmd.Parameters.AddRange(list);
            }
        }

        private void FinishWriteTransaction(bool isTrans, bool isSuccess)
        {
            if (!isTrans)
                return;

            if (isSuccess)
                SubmitTrans();
            else
                RollbackTrans();
        }

        private void LogDeleteException<T>(Exception ex, string operation, string sql) where T : class, new()
        {
            if (string.Equals(config?.SqlErrorType, SqlErrorType.Db, StringComparison.OrdinalIgnoreCase))
                DbLogTable.LogException<T>(config, ex, operation, string.Empty);
            else
                DbLog.LogException<T>(config.IsOutError, config.DbType, ex, operation, sql);
        }

        private void LogUpdateException<T>(Exception ex, string operation, string sql) where T : class, new()
        {
            if (string.Equals(config?.SqlErrorType, SqlErrorType.Db, StringComparison.OrdinalIgnoreCase))
                DbLogTable.LogException<T>(config, ex, operation, string.Empty);
            else
                DbLog.LogException<T>(config.IsOutError, config.DbType, ex, operation, sql);
        }

        #region 删除(Lambda表达式)
        /// <summary>
        /// 删除(Lambda表达式)
        /// </summary>
        /// <typeparam name="T">泛型</typeparam>
        /// <param name="predicate">查询条件表达式</param>
        /// <param name="isTrans">是否使用事务</param>
        /// <returns>数据操作结果</returns>
        public DataReturn<T> Delete<T>(Expression<Func<T, bool>> predicate, bool isTrans = false) where T : class, new()
        {
            var result = new DataReturn<T>();
            var sql = new StringBuilder();
            var visitModel = new VisitModel();
            var tableName = new List<string>();

            try
            {
                if (isTrans)
                    BeginTrans();

                visitModel = VisitExpression.LambdaWhere<T>(predicate, config);

                if (predicate == null || string.IsNullOrEmpty(visitModel.Where))
                {
                    result.WriteReturn.IsSuccess = false;
                    result.WriteReturn.Message = "删除条件不能为空，已阻止无条件删除";
                    FinishWriteTransaction(isTrans, false);
                    return result;
                }

                var entityTableName = TableNameHelper.GetTableName<T>(config);
                var whereClause = BuildLambdaWhereClause(predicate, visitModel);
                sql.AppendFormat("delete from {0} {1}", entityTableName, whereClause);
                var commandText = sql.ToString();

                result.Sql = ParameterToSql.ObjectParamToSql(visitModel.Param, commandText, config);

                SetCommandParameters(visitModel.Param);

                tableName.Add(entityTableName);
                    AopBefore(tableName, commandText, visitModel.Param, config, false,AopType.Delete_Lambda);

                if (visitModel.IsSuccess)
                {
                    if (conn.State == ConnectionState.Closed)
                        conn.Open();
                    result.WriteReturn.IsSuccess = BaseExecute.ToBool(cmd, commandText);
                }
                else
                    result.WriteReturn.IsSuccess = false;

                FinishWriteTransaction(isTrans, result.WriteReturn.IsSuccess);

                AopAfter(tableName, commandText, visitModel.Param, config, false, AopType.Delete_Lambda, result.WriteReturn.IsSuccess);
            }
            catch (Exception ex)
            {
                AopException(ex, "Delete by Lambda tableName"+typeof(T).Name,config, AopType.Delete_Lambda);

                LogDeleteException<T>(ex, "Delete<T>", result.Sql);

                if (isTrans)
                    RollbackTrans();

                result.WriteReturn.IsSuccess = false;
                result.WriteReturn.Message = ex.Message;
            }

            return result;
        }
        #endregion

        #region 删除
        /// <summary>
        /// 删除
        /// </summary>
        /// <typeparam name="T">泛型</typeparam>
        /// <param name="model">实体对象</param>
        /// <param name="isTrans">是否使用事务</param>
        /// <returns>数据操作结果</returns>
        public DataReturn<T> Delete<T>(T model, bool isTrans = false) where T : class, new()
        {
            var result = new DataReturn<T>();
            var optionModel = new OptionModel();
            var tableName = new List<string>();

            try
            {
                if (isTrans)
                    BeginTrans();

                optionModel = BaseModel.DeleteToSql<T>(cmd, model, config);

                result.Sql = ParameterToSql.ObjectParamToSql(optionModel.Param, optionModel.Sql, config);

                SetCommandParameters(optionModel.Param);

                tableName.Add(TableNameHelper.GetTableName<T>(config));
                AopBefore(tableName, optionModel.Sql, optionModel.Param, config, false,AopType.Delete_PrimaryKey);

                if (optionModel.IsSuccess)
                {
                    if (conn.State == ConnectionState.Closed)
                        conn.Open();
                    result.WriteReturn.IsSuccess = BaseExecute.ToBool(cmd, optionModel.Sql);
                }
                else
                {
                    result.WriteReturn.IsSuccess = false;
                    result.WriteReturn.Message = optionModel.Message;
                }

                FinishWriteTransaction(isTrans, result.WriteReturn.IsSuccess);

                AopAfter(tableName, optionModel.Sql, optionModel.Param, config, false, AopType.Delete_PrimaryKey, result.WriteReturn.IsSuccess);
            }
            catch (Exception ex)
            {
                AopException(ex, "Delete by Primary Key tableName" + typeof(T).Name,config, AopType.Delete_PrimaryKey);

                if (isTrans)
                    RollbackTrans();

                LogDeleteException<T>(ex, "Delete<T>", result.Sql);

                result.WriteReturn.IsSuccess = false;
                result.WriteReturn.Message = ex.Message;
            }

            return result;
        }
        #endregion

        #region 修改(Lambda表达式)
        /// <summary>
        /// 修改(Lambda表达式)
        /// </summary>
        /// <typeparam name="T">泛型</typeparam>
        /// <param name="model">实体对象</param>
        /// <param name="predicate">查询条件表达式</param>
        /// <param name="field">更新字段表达式</param>
        /// <param name="isTrans">是否使用事务</param>
        /// <returns>数据操作结果</returns>
        public DataReturn<T> Update<T>(T model, Expression<Func<T, bool>> predicate, Expression<Func<T, object>> field = null, bool isTrans = false) where T : class, new()
        {
            string sql = "";
            var result = new DataReturn<T>();
            var visitModel = new VisitModel();
            var update = new OptionModel();
            var tableName = new List<string>();

            try
            {
                if (isTrans)
                    BeginTrans();

                // UpdateToSql 要读主键元数据，必须已有打开的连接。
                // 原实现在这里没开连接，而把 conn.Open() 放在了真正执行 SQL 之前，
                // 于是「新建 DataContext 后第一次调用 Update」必然抛
                // "ExecuteReader can only be called when the connection is open."
                EnsureConnectionOpen();

                update = BaseModel.UpdateToSql<T>(model, config, field, cmd);

                if (update.IsSuccess)
                {
                    visitModel = VisitExpression.LambdaWhere<T>(predicate, config);

                    if (predicate == null || string.IsNullOrEmpty(visitModel.Where))
                    {
                        result.WriteReturn.IsSuccess = false;
                        result.WriteReturn.Message = "更新条件不能为空，已阻止无条件更新";
                        FinishWriteTransaction(isTrans, false);
                        return result;
                    }

                    sql = string.Format("{0} {1}", update.Sql, string.IsNullOrEmpty(visitModel.Where) ? "" : string.Format("where {0}", visitModel.Where.Replace(string.Format("{0}.", predicate.Parameters[0].Name), "")));

                    SetCommandParameters(System.Linq.Enumerable.Concat(update.Param, visitModel.Param));

                    var mergedParams = Parameter.ParamMerge(update.Param, visitModel.Param);
                    result.Sql = ParameterToSql.ObjectParamToSql(mergedParams, sql, config);

                    tableName.Add(TableNameHelper.GetTableName<T>(config));
                    AopBefore(tableName, sql, mergedParams, config, false, AopType.Update_Lambda);

                    if (visitModel.IsSuccess)
                    {
                        if (conn.State == ConnectionState.Closed)
                            conn.Open();
                        result.WriteReturn.IsSuccess = BaseExecute.ToBool(cmd, sql);
                    }
                    else
                    {
                        // 原来这里只置失败、不给原因：调用方拿到 IsSuccess=false 且 Message 为空，
                        // 无法判断是条件表达式解析失败还是数据库拒绝。
                        result.WriteReturn.IsSuccess = false;
                        result.WriteReturn.Message = string.Format(
                            "更新条件表达式解析失败，已阻止执行（解析出的条件：{0}）",
                            string.IsNullOrEmpty(visitModel.Where) ? "空" : visitModel.Where);
                    }
                }
                else
                {
                    result.WriteReturn.Message = update.Message;
                    result.WriteReturn.IsSuccess = false;
                }

                FinishWriteTransaction(isTrans, result.WriteReturn.IsSuccess);

                AopAfter(tableName, sql, Parameter.ParamMerge(update.Param, visitModel.Param), config, false, AopType.Update_Lambda, result.WriteReturn.IsSuccess);
            }
            catch (Exception ex)
            {
                AopException(ex, "Update by Lambda tableName:" + typeof(T).Name, config, AopType.Update_Lambda);

                LogUpdateException<T>(ex, "Update<T>", result.Sql);
                result.WriteReturn.IsSuccess = false;
                result.WriteReturn.Message = ex.Message;

                if (isTrans)
                    RollbackTrans();
            }

            return result;
        }
        #endregion

        #region 修改
        /// <summary>
        /// 修改
        /// </summary>
        /// <typeparam name="T">泛型</typeparam>
        /// <param name="model">实体对象</param>
        /// <param name="field">更新字段表达式</param>
        /// <param name="isTrans">是否使用事务</param>
        /// <returns>数据操作结果</returns>
        public DataReturn<T> Update<T>(T model, Expression<Func<T, object>> field = null, bool isTrans = false) where T : class, new()
        {
            var result = new DataReturn<T>();
            var update = new OptionModel();
            var tableName = new List<string>();
            try
            {

                // 同 Update(model, predicate, field)：先保证连接可用，再读主键元数据。
                if (isTrans)
                    BeginTrans();
                EnsureConnectionOpen();

                update = BaseModel.UpdateToSql<T>(cmd, model, config, field);
                if (update.IsSuccess)
                {
                    SetCommandParameters(update.Param);

                    result.Sql = ParameterToSql.ObjectParamToSql(update.Param, update.Sql, config);

                    tableName.Add(TableNameHelper.GetTableName<T>(config));
                    AopBefore(tableName, update.Sql, update.Param, config, false, AopType.Update_PrimaryKey);

                    if (conn.State == ConnectionState.Closed)
                        conn.Open();
                    result.WriteReturn.IsSuccess = BaseExecute.ToBool(cmd, update.Sql);
                }
                else
                {
                    result.WriteReturn.Message = update.Message;
                    result.WriteReturn.IsSuccess = false;
                }

                FinishWriteTransaction(isTrans, result.WriteReturn.IsSuccess);

                AopAfter(tableName, update.Sql, update.Param, config, false, AopType.Update_PrimaryKey, result.WriteReturn.IsSuccess);
            }
            catch (Exception ex)
            {
                AopException(ex, "Update by Primary Key tableName:" + typeof(T).Name, config, AopType.Update_PrimaryKey);

                if (isTrans)
                    RollbackTrans();

                LogUpdateException<T>(ex, "UpdateModel<T>", result.Sql);
                result.WriteReturn.IsSuccess = false;
                result.WriteReturn.Message = ex.Message;
            }

            return result;
        }
        #endregion


        #region 修改list
        /// <summary>
        /// 修改list
        /// </summary>
        /// <typeparam name="T">泛型</typeparam>
        /// <param name="list">实体集合</param>
        /// <param name="field">更新字段表达式</param>
        /// <returns>数据操作结果</returns>
        public DataReturn<T> UpdateList<T>(List<T> list, Expression<Func<T, object>> field = null) where T : class, new()
        {
            var result = new DataReturn<T>();
            var update = new OptionModel();
            var tableName = new List<string>();
            try
            {
                if (list.Count == 0)
                {
                    result.WriteReturn.IsSuccess = false;
                    result.WriteReturn.Message = "更新数据不能为空";
                    return result;
                }

                update = BaseModel.UpdateListToSql<T>(cmd, list, config, field);

                if (update.IsSuccess)
                {
                    using (var adapter = DbProviderFactories.GetFactory(config.ProviderName).CreateDataAdapter())
                    {
                        BeginTrans();
                        // 清理参数而非 dispose 命令对象（PostgreSQL/Npgsql 不支持对已 dispose 的命令重新使用）
                        cmd.Parameters.Clear();
                        adapter.InsertCommand = cmd;
                        adapter.InsertCommand.CommandText = update.Sql;
                        adapter.InsertCommand.UpdatedRowSource = UpdateRowSource.None;
                        adapter.UpdateBatchSize = 0;

                        if (update.Param.Count != 0)
                            adapter.InsertCommand.Parameters.AddRange(update.Param.ToArray());

                        result.Sql = ParameterToSql.ObjectParamToSql(update.Param, update.Sql, config);
                        
                tableName.Add(TableNameHelper.GetTableName<T>(config));
                        AopBefore(tableName, update.Sql, update.Param, config, false,AopType.UpdateList);

                        result.WriteReturn.IsSuccess = adapter.Update(update.table) > 0;
                        if (result.WriteReturn.IsSuccess)
                            SubmitTrans();
                        else
                            RollbackTrans();

                        AopAfter(tableName, update.Sql, update.Param, config, false, AopType.UpdateList, result.WriteReturn.IsSuccess);
                    }
                }
                else
                {
                    result.WriteReturn.Message = update.Message;
                    result.WriteReturn.IsSuccess = false;
                }
            }
            catch (Exception ex)
            {
                AopException(ex, "Update List tableName:" + typeof(T).Name,config, AopType.UpdateList);

                if (string.Equals(config?.SqlErrorType, SqlErrorType.Db, StringComparison.OrdinalIgnoreCase))
                    DbLogTable.LogException<T>(config, ex, "UpdateList<T>", "");
                else
                    DbLog.LogException<T>(config.IsOutError, config.DbType, ex, "UpdateList<T>", result.Sql);
                result.WriteReturn.IsSuccess = false;
                result.WriteReturn.Message = ex.Message;
            }

            return result;
        }
        #endregion

        #region 增加
        /// <summary>
        /// 增加
        /// </summary>
        /// <typeparam name="T">泛型</typeparam>
        /// <param name="model">实体对象</param>
        /// <param name="isTrans">是否使用事务</param>
        /// <param name="notAddField">不添加的字段</param>
        /// <returns>数据操作结果</returns>
        public DataReturn<T> Add<T>(T model, bool isTrans = false, Expression<Func<T, object>> notAddField = null) where T : class, new()
        {
            var result = new DataReturn<T>();
            var insert = new OptionModel();
            var tableName = new List<string>();

            try
            {
                if (isTrans)
                    BeginTrans();

                insert = BaseModel.InsertToSql<T>(model, config);

                // InsertToSql 失败（属性缺少 [Column]、表名解析失败、主键缺失等）必须显式失败。
                // 这里原本是个空 if 块，随后走 else 直接返回默认 DataReturn：
                // 调用方会拿到「没有异常、没有 Message、Sql 为空」的对象，误以为数据已写入。
                if (!insert.IsSuccess)
                {
                    result.WriteReturn.IsSuccess = false;
                    result.WriteReturn.Message = string.Format("生成插入语句失败（{0}）：{1}",
                        typeof(T).Name,
                        string.IsNullOrEmpty(insert.Message) ? "未提供原因" : insert.Message);

                    if (isTrans)
                        RollbackTrans();

                    AopAfter(tableName, insert.Sql, insert.Param, config, false, AopType.Add, false);
                    return result;
                }

                if (insert.IsSuccess)
                {
                    result.Sql = ParameterToSql.ObjectParamToSql(insert.Param, insert.Sql, config);

                    // 清理参数而非 dispose 命令对象（PostgreSQL/Npgsql 不支持对已 dispose 的命令重新使用）
                    cmd.Parameters.Clear();

                    if (insert.Param.Count != 0)
                        cmd.Parameters.AddRange(insert.Param.ToArray());

                    tableName.Add(TableNameHelper.GetTableName<T>(config));
                    AopBefore(tableName, insert.Sql, insert.Param, config, false,AopType.Add);

                    if (conn.State == ConnectionState.Closed)
                        conn.Open();
                    result.WriteReturn.IsSuccess = BaseExecute.ToBool(cmd, insert.Sql);

                    // 插入成功后获取自增主键值
                    if (result.WriteReturn.IsSuccess)
                        result.WriteReturn.IdentityValue = BaseExecute.GetIdentityValue(config.DbType, cmd);

                    if (isTrans)
                        SubmitTrans();

                    AopAfter(tableName, insert.Sql, insert.Param, config, false, AopType.Add, result.WriteReturn.IsSuccess);

                    return result;
                }
                else
                    return result;
            }
            catch (Exception ex)
            {
                AopException(ex, "Add tableName: " + typeof(T).Name,config, AopType.Add);

                if (string.Equals(config?.SqlErrorType, SqlErrorType.Db, StringComparison.OrdinalIgnoreCase))
                    DbLogTable.LogException<T>(config, ex, "Add<T>", "");
                else
                    DbLog.LogException<T>(config?.IsOutError ?? false, config?.DbType ?? DataDbType.SqlServer, ex, "Add<T>", result.Sql);

                if (isTrans)
                    RollbackTrans();

                result.WriteReturn.Message = string.Format("{0}: {1}", ex.GetType().Name, ex.Message);
                result.WriteReturn.IsSuccess = false;
                return result;
            }
        }
        #endregion

        #region 批量增加 
        /// <summary>
        /// 批量增加
        /// </summary>
        /// <typeparam name="T">泛型</typeparam>
        /// <param name="list">实体集合</param>
        /// <param name="IsTrans">是否使用事务</param>
        /// <param name="isLog">是否记录日志</param>
        /// <returns>数据操作结果</returns>
        public DataReturn<T> AddList<T>(List<T> list, bool IsTrans = false, bool isLog = true) where T : class, new()
        {
            var result = new DataReturn<T>();
            var sql = new StringBuilder();
            var dyn = new Property.DynamicGet<T>();
            var tableName = new List<string>();

            try
            {
                if (IsTrans)
                    BeginTrans();

                if (config.DbType == DataDbType.Oracle)
                {
                    #region oracle
                    // 清理参数而非 dispose 命令对象（PostgreSQL/Npgsql 不支持对已 dispose 的命令重新使用）
                    cmd.Parameters.Clear();
                    if (!isLog)
                    {
                        cmd.CommandText = string.Format("alter table {0} nologging", TableNameHelper.GetTableName<T>());
                        cmd.ExecuteNonQuery();
                    }

                    var oracleCmdType = cmd.GetType();
                    var setArrayBindCount = PropertyCache.GetSetterMethodCached(oracleCmdType, "set_ArrayBindCount");
                    var setBindByName = PropertyCache.GetSetterMethodCached(oracleCmdType, "set_BindByName");

                    if (setArrayBindCount != null)
                    {
                        var param = new object[1] { list.Count };
                        setArrayBindCount.Invoke(cmd, param);
                    }

                    if (setBindByName != null)
                    {
                        var param = new object[1] { true };
                        setBindByName.Invoke(cmd, param);
                    }

                    sql.AppendFormat("insert into {0} values(", TableNameHelper.GetTableName<T>());

                    // 使用缓存的非 Identity 属性数组
                    var oracleProperties = PropertyCache.GetNonIdentityProperties<T>();
                    foreach (var a in oracleProperties)
                    {
                        var pValue = new List<object>();
                        var param = DbProviderFactories.GetFactory(config.ProviderName).CreateParameter();

                        if (a.PropertyType.Name.ToLower() == "nullable`1")
                            param.DbType = CommandParam.GetOracleDbType(a.PropertyType.GetGenericArguments()[0].Name);
                        else
                            param.DbType = CommandParam.GetOracleDbType(a.PropertyType.Name);

                        param.Direction = ParameterDirection.Input;
                        param.ParameterName = a.Name;
                        sql.AppendFormat("{0}{1},", config.Flag, a.Name);

                        foreach (var l in list)
                        {
                            var value = dyn.GetValue(l, a.Name, true);
                            if (value == null)
                                value = DBNull.Value;
                            pValue.Add(value);
                        }

                        param.Value = pValue.ToArray();
                        cmd.Parameters.Add(param);
                    }

                    sql.Append(")");
                    cmd.CommandText = sql.ToString().Replace(",)", ")");

                    tableName.Add(TableNameHelper.GetTableName<T>());
                    AopBefore(tableName, cmd.CommandText, null, config, false, AopType.AddList);

                    result.WriteReturn.IsSuccess = cmd.ExecuteNonQuery() > 0;

                    if (!isLog)
                    {
                        cmd.CommandText = string.Format("alter table {0} logging", TableNameHelper.GetTableName<T>());
                        cmd.ExecuteNonQuery();
                    }
                    #endregion
                }

                if (config.DbType == DataDbType.SqlServer)
                {
                    #region sqlserver
                    // 清理参数而非 dispose 命令对象
                    cmd.Parameters.Clear();
                    
                    // 打开连接以支持 InitTvps 和 GetTable 中的 ExecuteReader
                    if (conn.State == ConnectionState.Closed)
                        conn.Open();
                    
                    CommandParam.InitTvps<T>(cmd);
                    
                    var parametersType = cmd.Parameters.GetType();
                    var addWithValueMethod = PropertyCache.GetSetterMethodCached(parametersType, "AddWithValue");
                    
                    if (addWithValueMethod != null)
                    {
                        var param = new object[2];
                        param[0] = string.Format("@{0}_TVP", typeof(T).Name);
                        param[1] = CommandParam.GetTable<T>(cmd, list);
                        var sqlParam = addWithValueMethod.Invoke(cmd.Parameters, param);

                        var sqlParamType = sqlParam.GetType();
                        var setSqlDbType = PropertyCache.GetSetterMethodCached(sqlParamType, "set_SqlDbType");
                        var setSelectedTypeName = PropertyCache.GetSetterMethodCached(sqlParamType, "set_TypeName");

                        if (setSqlDbType != null)
                        {
                            param = new object[1];
                            param[0] = SqlDbType.Structured;
                            setSqlDbType.Invoke(sqlParam, param);
                        }
                        if (setSelectedTypeName != null)
                        {
                            param = new object[1];
                            param[0] = string.Format("{0}_TVP", typeof(T).Name);
                            setSelectedTypeName.Invoke(sqlParam, param);
                        }
                    }

                    cmd.CommandText = CommandParam.GetTvps<T>();

                    tableName.Add(TableNameHelper.GetTableName<T>());
                    AopBefore(tableName, cmd.CommandText, null, config, false, AopType.AddList);

                    result.WriteReturn.IsSuccess = cmd.ExecuteNonQuery() > 0;
                    #endregion
                }

                if (config.DbType == DataDbType.MySql)
                {
                    #region mysql
                    // 清理参数而非 dispose 命令对象
                    cmd.Parameters.Clear();
                    
                    // 使用缓存的非 Identity 属性数组
                    var mysqlProperties = PropertyCache.GetNonIdentityProperties<T>();
                    
                    var mysqlTableName = TableNameHelper.GetTableName<T>(config);
                    var mysqlColumns = string.Join(", ", mysqlProperties.Select(p => p.Name));
                    
                    // 使用参数化查询防止 SQL 注入
                    var paramIndex = 0;
                    var valuePlaceholders = new List<string>();
                    
                    foreach (var item in list)
                    {
                        var itemParams = new List<string>();
                        foreach (var prop in mysqlProperties)
                        {
                            var paramName = string.Format("@mysql_p{0}", paramIndex++);
                            itemParams.Add(paramName);
                            
                            var param = cmd.CreateParameter();
                            param.ParameterName = paramName;
                            var value = dyn.GetValue(item, prop.Name, true);
                            param.Value = value ?? DBNull.Value;
                            cmd.Parameters.Add(param);
                        }
                        valuePlaceholders.Add(string.Format("({0})", string.Join(", ", itemParams)));
                    }
                    
                    var mysqlValues = string.Join(", ", valuePlaceholders);
                    cmd.CommandText = string.Format("INSERT INTO {0} ({1}) VALUES {2}", mysqlTableName, mysqlColumns, mysqlValues);

                    tableName.Add(TableNameHelper.GetTableName<T>());
                    AopBefore(tableName, cmd.CommandText, null, config, false, AopType.AddList);

                    if (conn.State == ConnectionState.Closed)
                        conn.Open();
                    result.WriteReturn.IsSuccess = cmd.ExecuteNonQuery() > 0;
                    #endregion
                }

                if (config.DbType == DataDbType.PostgreSql)
                {
                    #region postgresql
                    // 清理参数而非 dispose 命令对象
                    cmd.Parameters.Clear();
                    
                    var pgTableName = TableNameHelper.GetTableName<T>(config);
                    
                    // 使用缓存的非 Identity 属性数组
                    var pgNonIdentityProperties = PropertyCache.GetNonIdentityProperties<T>();
                    
                    var pgColumns = string.Join(", ", pgNonIdentityProperties.Select(p => p.Name));
                    var pgPlaceholders = string.Join(", ", pgNonIdentityProperties.Select((p, i) => string.Format("{0}{1}", config.Flag, p.Name)));
                    var insertSql = string.Format("INSERT INTO {0} ({1}) VALUES ({2})", pgTableName, pgColumns, pgPlaceholders);
                    
                    AopBefore(new List<string> { pgTableName }, insertSql, null, config, false, AopType.AddList);
                    
                    // 使用事务包裹批量插入
                    if (!IsTrans)
                        BeginTrans();
                    
                    cmd.CommandText = insertSql;
                    
                    var affectedRows = 0;
                    var rowCountUnknown = false;

                    foreach (var item in list)
                    {
                        cmd.Parameters.Clear();
                        foreach (var prop in pgNonIdentityProperties)
                        {
                            var param = cmd.CreateParameter();
                            param.ParameterName = string.Format("{0}{1}", config.Flag, prop.Name);
                            var value = dyn.GetValue(item, prop.Name, true);
                            // 处理布尔值，PostgreSql 使用 true/false
                            if (value is bool pgBoolVal)
                                param.Value = pgBoolVal;
                            else
                                param.Value = value ?? DBNull.Value;
                            cmd.Parameters.Add(param);
                        }
                        
                        if (conn.State == ConnectionState.Closed)
                            conn.Open();
                        var rowCount = cmd.ExecuteNonQuery();
                        if (rowCount < 0) rowCountUnknown = true; else affectedRows += rowCount;
                    }
                    
                    if (!IsTrans)
                        SubmitTrans();
                    
                    // 原来这里无条件置成功：逐条执行的返回值被丢弃，
                    // 中间某行没写进去也照样报告成功。现在按实际受影响行数比对。
                    // -1 表示驱动不提供行数（如 SET NOCOUNT ON），无法判定时按成功处理。
                    result.WriteReturn.IsSuccess = rowCountUnknown || affectedRows >= list.Count;
                    if (!result.WriteReturn.IsSuccess)
                    {
                        result.WriteReturn.Message = string.Format(
                            "批量插入受影响行数不足：期望 {0} 行，实际 {1} 行", list.Count, affectedRows);
                    }
                    result.Sql = string.Format("{0} (x{1})", insertSql, list.Count);
                    
                    AopAfter(new List<string> { pgTableName }, result.Sql, null, config, false, AopType.AddList, result.WriteReturn.IsSuccess);
                    #endregion
                }

                if (config.DbType == DataDbType.SQLite)
                {
                    #region sqlite
                    // 清理参数而非 dispose 命令对象
                    cmd.Parameters.Clear();
                    
                    var sqliteTableName = TableNameHelper.GetTableName<T>();
                    // 使用缓存的非 Identity 属性数组
                    var sqliteProperties = PropertyCache.GetNonIdentityProperties<T>();
                    var columns = string.Join(", ", sqliteProperties.Select(p => p.Name));
                    var placeholders = string.Join(", ", sqliteProperties.Select(p => string.Format("{0}{1}", config.Flag, p.Name)));
                    var insertSql = string.Format("INSERT INTO {0} ({1}) VALUES ({2})", sqliteTableName, columns, placeholders);
                    
                    AopBefore(new List<string> { sqliteTableName }, insertSql, null, config, false, AopType.AddList);
                    
                    // 使用事务包裹批量插入
                    if (!IsTrans)
                        BeginTrans();
                    
                    cmd.CommandText = insertSql;
                    
                    var affectedRows = 0;
                    var rowCountUnknown = false;

                    foreach (var item in list)
                    {
                        cmd.Parameters.Clear();
                        foreach (var prop in sqliteProperties)
                        {
                            var param = cmd.CreateParameter();
                            param.ParameterName = string.Format("{0}{1}", config.Flag, prop.Name);
                            var value = dyn.GetValue(item, prop.Name, true);
                            param.Value = value ?? DBNull.Value;
                            cmd.Parameters.Add(param);
                        }
                        var rowCount = cmd.ExecuteNonQuery();
                        if (rowCount < 0) rowCountUnknown = true; else affectedRows += rowCount;
                    }
                    
                    if (!IsTrans)
                        SubmitTrans();
                    
                    // 原来这里无条件置成功：逐条执行的返回值被丢弃，
                    // 中间某行没写进去也照样报告成功。现在按实际受影响行数比对。
                    // -1 表示驱动不提供行数（如 SET NOCOUNT ON），无法判定时按成功处理。
                    result.WriteReturn.IsSuccess = rowCountUnknown || affectedRows >= list.Count;
                    if (!result.WriteReturn.IsSuccess)
                    {
                        result.WriteReturn.Message = string.Format(
                            "批量插入受影响行数不足：期望 {0} 行，实际 {1} 行", list.Count, affectedRows);
                    }
                    result.Sql = string.Format("{0} (x{1})", insertSql, list.Count);
                    
                    AopAfter(new List<string> { sqliteTableName }, result.Sql, null, config, false, AopType.AddList, result.WriteReturn.IsSuccess);
                    #endregion
                }

                if (result.WriteReturn.IsSuccess && IsTrans)
                    SubmitTrans();
                else if (result.WriteReturn.IsSuccess == false && IsTrans)
                    RollbackTrans();

                AopAfter(tableName, cmd.CommandText, null, config, false, AopType.AddList, result.WriteReturn.IsSuccess);
            }
            catch (Exception ex)
            {
                AopException(ex, "Add List tableName:" + typeof(T).Name,config,AopType.AddList);

                if (IsTrans)
                    RollbackTrans();

                if (string.Equals(config?.SqlErrorType, SqlErrorType.Db, StringComparison.OrdinalIgnoreCase))
                    DbLogTable.LogException<T>(config, ex, "AddList<T>", "");
                else
                    DbLog.LogException<T>(config.IsOutError, config.DbType, ex, "AddList<T>", result.Sql);

                result.WriteReturn.IsSuccess = false;
                result.WriteReturn.Message = ex.Message;
            }

            return result;
        }
        #endregion

        #region 执行sql
        /// <summary>
        /// 执行sql
        /// </summary>
        /// <param name="sql">SQL语句</param>
        /// <param name="param">数据库参数数组</param>
        /// <param name="isTrans">是否事务</param>
        /// <param name="isLog">是否记录日志</param>
        /// <param name="IsProcedure">是否存储过程</param>
        /// <param name="isAop">是否启用AOP</param>
        /// <returns>数据返回对象</returns>
        public DataReturn ExecuteSql(string sql, DbParameter[] param = null, bool isTrans = false, bool isLog = false, bool IsProcedure = false,bool isAop=true)
        {
            var result = new DataReturn();
            try
            {
                if (isTrans)
                    BeginTrans();

                if (param != null)
                    result.Sql = ParameterToSql.ObjectParamToSql(param.ToList(), sql, config);
                else
                    result.Sql = sql;

                DbLog.LogSql(isLog, result.Sql, config.DbType, 0);

                cmd.Parameters.Clear();

                if (param != null)
                    cmd.Parameters.AddRange(param);

                if (isAop)
                    AopBefore(null, sql, param?.ToList(), config, false,AopType.Execute_Sql_Bool);

                if (conn.State == ConnectionState.Closed)
                    conn.Open();
                result.WriteReturn.IsSuccess = BaseExecute.ToBool(cmd, sql, IsProcedure);

                if (isTrans && result.WriteReturn.IsSuccess)
                    SubmitTrans();
                else if (isTrans && result.WriteReturn.IsSuccess == false)
                    RollbackTrans();

                if (isAop)
                    AopAfter(null, sql, param?.ToList(), config, false, AopType.Execute_Sql_Bool, result.WriteReturn.IsSuccess);
            }
            catch (Exception ex)
            {
                AopException(ex, "Excute Sql",config, AopType.Execute_Sql_Bool);

                if (isTrans)
                    RollbackTrans();

                if (string.Equals(config?.SqlErrorType, SqlErrorType.Db, StringComparison.OrdinalIgnoreCase))
                    DbLogTable.LogException(config, ex, "ExecuteSql", result.Sql);
                else
                    DbLog.LogException(config.IsOutError, config.DbType, ex, "ExecuteSql", result.Sql);
                result.WriteReturn.IsSuccess = false;
                result.WriteReturn.Message = ex.Message;
            }

              return result;
        }
        #endregion

        #region 批量插入
        /// <summary>
        /// 批量插入（使用事务优化性能）
        /// </summary>
        /// <typeparam name="T">实体类型</typeparam>
        /// <param name="list">实体列表</param>
        /// <param name="isLog">是否记录SQL</param>
        /// <returns>插入结果</returns>
        public DataReturn BulkInsert<T>(List<T> list, bool isLog = false) where T : class, new()
        {
            var result = new DataReturn();
            var stopwatch = Stopwatch.StartNew();

            try
            {
                BeginTrans();
                var tableName = TableNameHelper.GetTableName<T>();
                var allProperties = PropertyCache.GetPropertiesCached<T>().Where(p => p.CanRead && p.CanWrite).ToList();
                // 排除自增标识列（如 Id），避免 INSERT 时出现 "IDENTITY_INSERT is set to OFF" 错误
                var properties = allProperties.Where(p =>
                {
                    var propInfo = typeof(T).GetProperty(p.Name);
                    if (propInfo == null) return true;
                    var colAttr = propInfo.GetCustomAttributes(typeof(Property.ColumnAttribute), false)
                        .FirstOrDefault() as Property.ColumnAttribute;
                    return colAttr == null || !colAttr.IsIdentity;
                }).ToList();
                var columnNames = string.Join(", ", properties.Select(p => p.Name));

                // 重置命令对象，避免引用已释放的旧命令
                if (_command != null)
                {
                    _command.Parameters.Clear();
                    _command.Dispose();
                }
                _command = CreateCommand();
                if (_transaction != null)
                    _command.Transaction = _transaction;

                var paramNames = string.Join(", ", properties.Select((_, i) => string.Format("@p{0}", i)));
                var insertSql = string.Format("INSERT INTO {0} ({1}) VALUES ({2})", tableName, columnNames, paramNames);
                _command.CommandText = insertSql;

                foreach (var item in list)
                {
                    _command.Parameters.Clear();
                    for (int i = 0; i < properties.Count; i++)
                    {
                        var value = properties[i].GetValue(item);
                        var param = _command.CreateParameter();
                        param.ParameterName = string.Format("@p{0}", i);
                        param.Value = value ?? DBNull.Value;
                        _command.Parameters.Add(param);
                    }

                    if (conn.State == ConnectionState.Closed)
                        conn.Open();
                    _command.ExecuteNonQuery();
                }

                result.Sql = string.Format("{0} (x{1})", insertSql, list.Count);
                if (isLog)
                    DbLog.LogSql(true, result.Sql, config.DbType, 0);

                AopBefore(new List<string> { tableName }, insertSql, new List<DbParameter>(), config, false, AopType.AddList);
                result.WriteReturn.IsSuccess = true;
                SubmitTrans();
                AopAfter(new List<string> { tableName }, insertSql, new List<DbParameter>(), config, false, AopType.AddList, list.Count);
            }
            catch (Exception ex)
            {
                RollbackTrans();
                AopException(ex, "BulkInsert", config, AopType.AddList);

                if (string.Equals(config?.SqlErrorType, SqlErrorType.Db, StringComparison.OrdinalIgnoreCase))
                    DbLogTable.LogException(config, ex, "BulkInsert", result.Sql);
                else
                    DbLog.LogException(config.IsOutError, config.DbType, ex, "BulkInsert", result.Sql);

                result.WriteReturn.IsSuccess = false;
                result.WriteReturn.Message = ex.Message;
            }

            stopwatch.Stop();
            return result;
        }
        #endregion

        #region 批量更新/删除

        /// <summary>
        /// 批量更新实体（使用 SQL UPDATE ... WHERE IN）
        /// </summary>
        /// <typeparam name="T">实体类型</typeparam>
        /// <param name="list">实体列表</param>
        /// <param name="predicate">更新条件</param>
        /// <param name="isLog">是否记录SQL</param>
        /// <returns>更新结果</returns>
        public DataReturn BulkUpdate<T>(List<T> list, Expression<Func<T, bool>> predicate, bool isLog = false) where T : class, new()
        {
            var result = new DataReturn();
            var stopwatch = Stopwatch.StartNew();
            var updatedRows = 0;

            try
            {
                BeginTrans();

                var tableName = TableNameHelper.GetTableName<T>();
                var properties = PropertyCache.GetPropertiesCached<T>()
                    .Where(p => p.CanRead && p.CanWrite && p.Name != "Id")
                    .ToList();

                var setClause = string.Join(", ", properties.Select(p => string.Format("{0} = @{0}", p.Name)));
                var paramNames = string.Join(", ", properties.Select(p => string.Format("@{0}", p.Name)));

                var visitModel = VisitExpression.LambdaWhere<T>(predicate, config);
                if (!visitModel.IsSuccess || predicate == null || string.IsNullOrEmpty(visitModel.Where))
                {
                    throw new InvalidOperationException("更新条件解析失败或为空，已阻止无条件批量更新");
                }

                var whereClause = visitModel.Where.Replace(string.Format("{0}.", predicate.Parameters[0].Name), string.Empty);
                var paramList = visitModel.Param;

                foreach (var item in list)
                {
                    cmd.Parameters.Clear();
                    foreach (var prop in properties)
                    {
                        var value = prop.GetValue(item);
                        var param = cmd.CreateParameter();
                        param.ParameterName = string.Format("@{0}", prop.Name);
                        param.Value = value ?? DBNull.Value;
                        cmd.Parameters.Add(param);
                    }

                    foreach (var param in paramList)
                        cmd.Parameters.Add(param);

                    if (conn.State == ConnectionState.Closed)
                        conn.Open();

                    var updateSql = string.Format("UPDATE {0} SET {1} WHERE {2}", tableName, setClause, whereClause);
                    cmd.CommandText = updateSql;

                    // 逐行更新时必须累加受影响行数：原来直接丢弃返回值，
                    // 最后无条件 IsSuccess=true，调用方无法知道到底更新了几行。
                    // 注意 UPDATE 命中 0 行是合法结果（记录已存在且值相同），
                    // 因此不据此判定失败，只把实际行数交给调用方判断。
                    var rowCount = cmd.ExecuteNonQuery();
                    if (rowCount < 0) updatedRows = -1; else if (updatedRows >= 0) updatedRows += rowCount;
                }

                result.Sql = string.Format("UPDATE {0} SET {1} WHERE {2} (x{3}, affected:{4})",
                    tableName, setClause, whereClause, list.Count,
                    updatedRows < 0 ? "unknown" : updatedRows.ToString());
                if (isLog)
                    DbLog.LogSql(true, result.Sql, config.DbType, 0);

                AopBefore(new List<string> { tableName }, result.Sql, paramList, config, false, AopType.UpdateList);
                result.WriteReturn.IsSuccess = true;
                SubmitTrans();
                AopAfter(new List<string> { tableName }, result.Sql, paramList, config, false, AopType.UpdateList, list.Count);
            }
            catch (Exception ex)
            {
                RollbackTrans();
                AopException(ex, "BulkUpdate", config, AopType.UpdateList);

                if (string.Equals(config?.SqlErrorType, SqlErrorType.Db, StringComparison.OrdinalIgnoreCase))
                    DbLogTable.LogException(config, ex, "BulkUpdate", result.Sql);
                else
                    DbLog.LogException(config.IsOutError, config.DbType, ex, "BulkUpdate", result.Sql);

                result.WriteReturn.IsSuccess = false;
                result.WriteReturn.Message = ex.Message;
            }

            stopwatch.Stop();
            return result;
        }

        /// <summary>
        /// 批量删除实体（使用 SQL DELETE FROM ... WHERE IN）
        /// </summary>
        /// <typeparam name="T">实体类型</typeparam>
        /// <param name="predicate">删除条件</param>
        /// <param name="isLog">是否记录SQL</param>
        /// <returns>删除结果</returns>
        public DataReturn BulkDelete<T>(Expression<Func<T, bool>> predicate, bool isLog = false) where T : class, new()
        {
            var result = new DataReturn();
            var stopwatch = Stopwatch.StartNew();

            try
            {
                BeginTrans();

                var tableName = TableNameHelper.GetTableName<T>();
                var visitModel = VisitExpression.LambdaWhere<T>(predicate, config);
                if (!visitModel.IsSuccess || predicate == null || string.IsNullOrEmpty(visitModel.Where))
                {
                    throw new InvalidOperationException("删除条件解析失败或为空，已阻止无条件批量删除");
                }

                var whereClause = visitModel.Where.Replace(string.Format("{0}.", predicate.Parameters[0].Name), string.Empty);
                var paramList = visitModel.Param;

                if (conn.State == ConnectionState.Closed)
                    conn.Open();

                var deleteSql = string.Format("DELETE FROM {0} WHERE {1}", tableName, whereClause);
                cmd.CommandText = deleteSql;

                cmd.Parameters.Clear();
                foreach (var param in paramList)
                {
                    cmd.Parameters.Add(param);
                }

                int affectedRows = cmd.ExecuteNonQuery();
                result.Sql = string.Format("DELETE FROM {0} WHERE {1} (x{2})", tableName, whereClause, affectedRows);
                if (isLog)
                    DbLog.LogSql(true, result.Sql, config.DbType, 0);

                AopBefore(new List<string> { tableName }, result.Sql, paramList, config, false, AopType.Delete_Lambda);
                result.WriteReturn.IsSuccess = true;
                result.WriteReturn.Message = string.Format("删除 {0} 条记录", affectedRows);
                SubmitTrans();
                AopAfter(new List<string> { tableName }, result.Sql, paramList, config, false, AopType.Delete_Lambda, affectedRows);
            }
            catch (Exception ex)
            {
                RollbackTrans();
                AopException(ex, "BulkDelete", config, AopType.Delete_Lambda);

                if (string.Equals(config?.SqlErrorType, SqlErrorType.Db, StringComparison.OrdinalIgnoreCase))
                    DbLogTable.LogException(config, ex, "BulkDelete", result.Sql);
                else
                    DbLog.LogException(config.IsOutError, config.DbType, ex, "BulkDelete", result.Sql);

                result.WriteReturn.IsSuccess = false;
                result.WriteReturn.Message = ex.Message;
            }

            stopwatch.Stop();
            return result;
        }

        #endregion

        #region 异步写入操作

        /// <summary>
        /// 异步确保连接已打开（真正的I/O异步）
        /// </summary>
        private async Task EnsureConnectionOpenAsync(CancellationToken cancellationToken = default)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(DataContext));

            if (_connection == null)
                throw new InvalidOperationException("Connection is null. DataContext was not initialized properly.");

            if (_connection.State != ConnectionState.Open)
                await _connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// 异步删除(Lambda表达式)
        /// </summary>
        public async Task<DataReturn<T>> DeleteAsync<T>(Expression<Func<T, bool>> predicate, bool isTrans = false, CancellationToken cancellationToken = default) where T : class, new()
        {
            var result = new DataReturn<T>();
            var sql = new StringBuilder();
            var visitModel = new VisitModel();
            var tableName = new List<string>();

            try
            {
                if (isTrans)
                    BeginTrans();

                visitModel = VisitExpression.LambdaWhere<T>(predicate, config);

                if (predicate == null || string.IsNullOrEmpty(visitModel.Where))
                {
                    result.WriteReturn.IsSuccess = false;
                    result.WriteReturn.Message = "删除条件不能为空，已阻止无条件删除";
                    FinishWriteTransaction(isTrans, false);
                    return result;
                }

                var entityTableName = TableNameHelper.GetTableName<T>(config);
                var whereClause = BuildLambdaWhereClause(predicate, visitModel);
                sql.AppendFormat("delete from {0} {1}", entityTableName, whereClause);
                var commandText = sql.ToString();

                result.Sql = ParameterToSql.ObjectParamToSql(visitModel.Param, commandText, config);

                SetCommandParameters(visitModel.Param);

                tableName.Add(entityTableName);
                AopBefore(tableName, commandText, visitModel.Param, config, false, AopType.Delete_Lambda);

                if (visitModel.IsSuccess)
                {
                    await EnsureConnectionOpenAsync(cancellationToken).ConfigureAwait(false);
                    result.WriteReturn.IsSuccess = await BaseExecute.ToBoolAsync(cmd, commandText).ConfigureAwait(false);
                }
                else
                    result.WriteReturn.IsSuccess = false;

                FinishWriteTransaction(isTrans, result.WriteReturn.IsSuccess);

                AopAfter(tableName, commandText, visitModel.Param, config, false, AopType.Delete_Lambda, result.WriteReturn.IsSuccess);
            }
            catch (Exception ex)
            {
                AopException(ex, "DeleteAsync by Lambda tableName" + typeof(T).Name, config, AopType.Delete_Lambda);

                LogDeleteException<T>(ex, "DeleteAsync<T>", result.Sql);

                if (isTrans)
                    RollbackTrans();

                result.WriteReturn.IsSuccess = false;
                result.WriteReturn.Message = ex.Message;
            }

            return result;
        }

        /// <summary>
        /// 异步删除(根据实体主键)
        /// </summary>
        public async Task<DataReturn<T>> DeleteAsync<T>(T model, bool isTrans = false, CancellationToken cancellationToken = default) where T : class, new()
        {
            var result = new DataReturn<T>();
            var optionModel = new OptionModel();
            var tableName = new List<string>();

            try
            {
                if (isTrans)
                    BeginTrans();

                optionModel = BaseModel.DeleteToSql<T>(cmd, model, config);

                result.Sql = ParameterToSql.ObjectParamToSql(optionModel.Param, optionModel.Sql, config);

                SetCommandParameters(optionModel.Param);

                tableName.Add(TableNameHelper.GetTableName<T>(config));
                AopBefore(tableName, optionModel.Sql, optionModel.Param, config, false, AopType.Delete_PrimaryKey);

                if (optionModel.IsSuccess)
                {
                    await EnsureConnectionOpenAsync(cancellationToken).ConfigureAwait(false);
                    result.WriteReturn.IsSuccess = await BaseExecute.ToBoolAsync(cmd, optionModel.Sql).ConfigureAwait(false);
                }
                else
                {
                    result.WriteReturn.IsSuccess = false;
                    result.WriteReturn.Message = optionModel.Message;
                }

                FinishWriteTransaction(isTrans, result.WriteReturn.IsSuccess);

                AopAfter(tableName, optionModel.Sql, optionModel.Param, config, false, AopType.Delete_PrimaryKey, result.WriteReturn.IsSuccess);
            }
            catch (Exception ex)
            {
                AopException(ex, "DeleteAsync by Primary Key tableName" + typeof(T).Name, config, AopType.Delete_PrimaryKey);

                if (isTrans)
                    RollbackTrans();

                LogDeleteException<T>(ex, "DeleteAsync<T>", result.Sql);

                result.WriteReturn.IsSuccess = false;
                result.WriteReturn.Message = ex.Message;
            }

            return result;
        }

        /// <summary>
        /// 异步修改(Lambda表达式)
        /// </summary>
        public async Task<DataReturn<T>> UpdateAsync<T>(T model, Expression<Func<T, bool>> predicate, Expression<Func<T, object>> field = null, bool isTrans = false, CancellationToken cancellationToken = default) where T : class, new()
        {
            string sql = "";
            var result = new DataReturn<T>();
            var visitModel = new VisitModel();
            var update = new OptionModel();
            var tableName = new List<string>();

            try
            {
                if (isTrans)
                    BeginTrans();

                // UpdateToSql 要读主键元数据，必须已有打开的连接（同同步版的修复）。
                EnsureConnectionOpen();

                update = BaseModel.UpdateToSql<T>(model, config, field, cmd);

                if (update.IsSuccess)
                {
                    visitModel = VisitExpression.LambdaWhere<T>(predicate, config);

                    if (predicate == null || string.IsNullOrEmpty(visitModel.Where))
                    {
                        result.WriteReturn.IsSuccess = false;
                        result.WriteReturn.Message = "更新条件不能为空，已阻止无条件更新";
                        FinishWriteTransaction(isTrans, false);
                        return result;
                    }

                    sql = string.Format("{0} {1}", update.Sql, string.IsNullOrEmpty(visitModel.Where) ? "" : string.Format("where {0}", visitModel.Where.Replace(string.Format("{0}.", predicate.Parameters[0].Name), "")));

                    SetCommandParameters(System.Linq.Enumerable.Concat(update.Param, visitModel.Param));

                    var mergedParams = Parameter.ParamMerge(update.Param, visitModel.Param);
                    result.Sql = ParameterToSql.ObjectParamToSql(mergedParams, sql, config);

                    tableName.Add(TableNameHelper.GetTableName<T>(config));
                    AopBefore(tableName, sql, mergedParams, config, false, AopType.Update_Lambda);

                    if (visitModel.IsSuccess)
                    {
                        await EnsureConnectionOpenAsync(cancellationToken).ConfigureAwait(false);
                        result.WriteReturn.IsSuccess = await BaseExecute.ToBoolAsync(cmd, sql).ConfigureAwait(false);
                    }
                    else
                        result.WriteReturn.IsSuccess = false;
                }
                else
                {
                    result.WriteReturn.Message = update.Message;
                    result.WriteReturn.IsSuccess = false;
                }

                FinishWriteTransaction(isTrans, result.WriteReturn.IsSuccess);

                AopAfter(tableName, sql, Parameter.ParamMerge(update.Param, visitModel.Param), config, false, AopType.Update_Lambda, result.WriteReturn.IsSuccess);
            }
            catch (Exception ex)
            {
                AopException(ex, "UpdateAsync by Lambda tableName:" + typeof(T).Name, config, AopType.Update_Lambda);

                if (isTrans)
                    RollbackTrans();

                LogUpdateException<T>(ex, "UpdateAsync<T>", result.Sql);
                result.WriteReturn.IsSuccess = false;
                result.WriteReturn.Message = ex.Message;
            }

            return result;
        }

        /// <summary>
        /// 异步修改(根据主键)
        /// </summary>
        public async Task<DataReturn<T>> UpdateAsync<T>(T model, Expression<Func<T, object>> field = null, bool isTrans = false, CancellationToken cancellationToken = default) where T : class, new()
        {
            var result = new DataReturn<T>();
            var update = new OptionModel();
            var tableName = new List<string>();
            try
            {
                if (isTrans)
                    BeginTrans();
                EnsureConnectionOpen();

                update = BaseModel.UpdateToSql<T>(cmd, model, config, field);
                if (update.IsSuccess)
                {
                    SetCommandParameters(update.Param);

                    result.Sql = ParameterToSql.ObjectParamToSql(update.Param, update.Sql, config);

                    tableName.Add(TableNameHelper.GetTableName<T>(config));
                    AopBefore(tableName, update.Sql, update.Param, config, false, AopType.Update_PrimaryKey);

                    await EnsureConnectionOpenAsync(cancellationToken).ConfigureAwait(false);
                    result.WriteReturn.IsSuccess = await BaseExecute.ToBoolAsync(cmd, update.Sql).ConfigureAwait(false);
                }
                else
                {
                    result.WriteReturn.Message = update.Message;
                    result.WriteReturn.IsSuccess = false;
                }

                FinishWriteTransaction(isTrans, result.WriteReturn.IsSuccess);

                AopAfter(tableName, update.Sql, update.Param, config, false, AopType.Update_PrimaryKey, result.WriteReturn.IsSuccess);
            }
            catch (Exception ex)
            {
                AopException(ex, "UpdateAsync by Primary Key tableName:" + typeof(T).Name, config, AopType.Update_PrimaryKey);

                if (isTrans)
                    RollbackTrans();

                LogUpdateException<T>(ex, "UpdateModelAsync<T>", result.Sql);
                result.WriteReturn.IsSuccess = false;
                result.WriteReturn.Message = ex.Message;
            }

            return result;
        }

        /// <summary>
        /// 异步增加
        /// </summary>
        public async Task<DataReturn<T>> AddAsync<T>(T model, bool isTrans = false, Expression<Func<T, object>> notAddField = null, CancellationToken cancellationToken = default) where T : class, new()
        {
            var result = new DataReturn<T>();
            var insert = new OptionModel();
            var tableName = new List<string>();

            try
            {
                if (isTrans)
                    BeginTrans();

                insert = BaseModel.InsertToSql<T>(model, config);

                if (insert.IsSuccess)
                {
                    result.Sql = ParameterToSql.ObjectParamToSql(insert.Param, insert.Sql, config);

                    cmd.Parameters.Clear();

                    if (insert.Param.Count != 0)
                        cmd.Parameters.AddRange(insert.Param.ToArray());

                    tableName.Add(TableNameHelper.GetTableName<T>());
                    AopBefore(tableName, insert.Sql, insert.Param, config, false, AopType.Add);

                    await EnsureConnectionOpenAsync(cancellationToken).ConfigureAwait(false);
                    result.WriteReturn.IsSuccess = await BaseExecute.ToBoolAsync(cmd, insert.Sql).ConfigureAwait(false);

                    if (result.WriteReturn.IsSuccess)
                        result.WriteReturn.IdentityValue = await BaseExecute.GetIdentityValueAsync(config.DbType, cmd).ConfigureAwait(false);

                    if (isTrans)
                        SubmitTrans();

                    AopAfter(tableName, insert.Sql, insert.Param, config, false, AopType.Add, result.WriteReturn.IsSuccess);

                    return result;
                }

                // 与同步版 Add 保持一致：InsertToSql 失败时必须显式失败，
                // 否则返回的是默认 DataReturn（无异常、无 Message），调用方无法判断。
                result.WriteReturn.IsSuccess = false;
                result.WriteReturn.Message = string.Format("生成插入语句失败（{0}）：{1}",
                    typeof(T).Name,
                    string.IsNullOrEmpty(insert.Message) ? "未提供原因" : insert.Message);

                if (isTrans)
                    RollbackTrans();

                AopAfter(tableName, insert.Sql, insert.Param, config, false, AopType.Add, false);
                return result;
            }
            catch (Exception ex)
            {
                AopException(ex, "AddAsync tableName: " + typeof(T).Name, config, AopType.Add);

                if (config?.SqlErrorType?.ToLower() == SqlErrorType.Db)
                    DbLogTable.LogException<T>(config, ex, "AddAsync<T>", "");
                else
                    DbLog.LogException<T>(config?.IsOutError ?? false, config?.DbType ?? DataDbType.SqlServer, ex, "AddAsync<T>", result.Sql);

                if (isTrans)
                    RollbackTrans();

                result.WriteReturn.Message = string.Format("{0}: {1}", ex.GetType().Name, ex.Message);
                result.WriteReturn.IsSuccess = false;
                return result;
            }
        }

        /// <summary>
        /// 异步批量增加
        /// </summary>
        public async Task<DataReturn<T>> AddListAsync<T>(List<T> list, bool isTrans = false, bool isLog = true, CancellationToken cancellationToken = default) where T : class, new()
        {
            var result = new DataReturn<T>();
            var sql = new StringBuilder();
            var dyn = new Property.DynamicGet<T>();
            var tableName = new List<string>();

            try
            {
                if (isTrans)
                    BeginTrans();

                if (config.DbType == DataDbType.Oracle)
                {
                    cmd.Parameters.Clear();
                    if (!isLog)
                    {
                        cmd.CommandText = string.Format("alter table {0} nologging", TableNameHelper.GetTableName<T>());
                        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                    }

                    var oracleCmdType = cmd.GetType();
                    var setArrayBindCount = PropertyCache.GetSetterMethodCached(oracleCmdType, "set_ArrayBindCount");
                    var setBindByName = PropertyCache.GetSetterMethodCached(oracleCmdType, "set_BindByName");

                    if (setArrayBindCount != null)
                    {
                        var param = new object[1] { list.Count };
                        setArrayBindCount.Invoke(cmd, param);
                    }

                    if (setBindByName != null)
                    {
                        var param = new object[1] { true };
                        setBindByName.Invoke(cmd, param);
                    }

                    sql.AppendFormat("insert into {0} values(", TableNameHelper.GetTableName<T>());

                    // 使用缓存的非 Identity 属性数组
                    var oracleProperties = PropertyCache.GetNonIdentityProperties<T>();
                    foreach (var a in oracleProperties)
                    {
                        var pValue = new List<object>();
                        var param = DbProviderFactories.GetFactory(config.ProviderName).CreateParameter();

                        if (a.PropertyType.Name.ToLower() == "nullable`1")
                            param.DbType = CommandParam.GetOracleDbType(a.PropertyType.GetGenericArguments()[0].Name);
                        else
                            param.DbType = CommandParam.GetOracleDbType(a.PropertyType.Name);

                        param.Direction = ParameterDirection.Input;
                        param.ParameterName = a.Name;
                        sql.AppendFormat("{0}{1},", config.Flag, a.Name);

                        foreach (var l in list)
                        {
                            var value = dyn.GetValue(l, a.Name, true);
                            if (value == null)
                                value = DBNull.Value;
                            pValue.Add(value);
                        }

                        param.Value = pValue.ToArray();
                        cmd.Parameters.Add(param);
                    }

                    sql.Append(")");
                    cmd.CommandText = sql.ToString().Replace(",)", ")");

                    tableName.Add(TableNameHelper.GetTableName<T>());
                    AopBefore(tableName, cmd.CommandText, null, config, false, AopType.AddList);

                    result.WriteReturn.IsSuccess = await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) > 0;

                    if (!isLog)
                    {
                        cmd.CommandText = string.Format("alter table {0} logging", TableNameHelper.GetTableName<T>());
                        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                    }
                }

                if (config.DbType == DataDbType.SqlServer)
                {
                    cmd.Parameters.Clear();

                    await EnsureConnectionOpenAsync(cancellationToken).ConfigureAwait(false);

                    CommandParam.InitTvps<T>(cmd);
                    
                    var parametersType = cmd.Parameters.GetType();
                    var addWithValueMethod = PropertyCache.GetSetterMethodCached(parametersType, "AddWithValue");
                    
                    if (addWithValueMethod != null)
                    {
                        var param = new object[2];
                        param[0] = string.Format("@{0}_TVP", typeof(T).Name);
                        param[1] = CommandParam.GetTable<T>(cmd, list);
                        var sqlParam = addWithValueMethod.Invoke(cmd.Parameters, param);

                        var sqlParamType = sqlParam.GetType();
                        var setSqlDbType = PropertyCache.GetSetterMethodCached(sqlParamType, "set_SqlDbType");
                        var setSelectedTypeName = PropertyCache.GetSetterMethodCached(sqlParamType, "set_TypeName");

                        if (setSqlDbType != null)
                        {
                            param = new object[1];
                            param[0] = SqlDbType.Structured;
                            setSqlDbType.Invoke(sqlParam, param);
                        }
                        if (setSelectedTypeName != null)
                        {
                            param = new object[1];
                            param[0] = string.Format("{0}_TVP", typeof(T).Name);
                            setSelectedTypeName.Invoke(sqlParam, param);
                        }
                    }

                    cmd.CommandText = CommandParam.GetTvps<T>();

                    tableName.Add(TableNameHelper.GetTableName<T>());
                    AopBefore(tableName, cmd.CommandText, null, config, false, AopType.AddList);

                    result.WriteReturn.IsSuccess = await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) > 0;
                }

                if (config.DbType == DataDbType.MySql)
                {
                    cmd.Parameters.Clear();

                    // 使用缓存的非 Identity 属性数组
                    var mysqlProperties = PropertyCache.GetNonIdentityProperties<T>();

                    var mysqlTableName = TableNameHelper.GetTableName<T>(config);
                    var mysqlColumns = string.Join(", ", mysqlProperties.Select(p => p.Name));

                    var paramIndex = 0;
                    var valuePlaceholders = new List<string>();

                    foreach (var item in list)
                    {
                        var itemParams = new List<string>();
                        foreach (var prop in mysqlProperties)
                        {
                            var paramName = string.Format("@mysql_p{0}", paramIndex++);
                            itemParams.Add(paramName);

                            var param = cmd.CreateParameter();
                            param.ParameterName = paramName;
                            var value = dyn.GetValue(item, prop.Name, true);
                            param.Value = value ?? DBNull.Value;
                            cmd.Parameters.Add(param);
                        }
                        valuePlaceholders.Add(string.Format("({0})", string.Join(", ", itemParams)));
                    }

                    var mysqlValues = string.Join(", ", valuePlaceholders);
                    cmd.CommandText = string.Format("INSERT INTO {0} ({1}) VALUES {2}", mysqlTableName, mysqlColumns, mysqlValues);

                    tableName.Add(TableNameHelper.GetTableName<T>());
                    AopBefore(tableName, cmd.CommandText, null, config, false, AopType.AddList);

                    await EnsureConnectionOpenAsync(cancellationToken).ConfigureAwait(false);
                    result.WriteReturn.IsSuccess = await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) > 0;
                }

                if (config.DbType == DataDbType.PostgreSql)
                {
                    cmd.Parameters.Clear();

                    var pgTableName = TableNameHelper.GetTableName<T>(config);

                    // 使用缓存的非 Identity 属性数组
                    var pgNonIdentityProperties = PropertyCache.GetNonIdentityProperties<T>();

                    var pgColumns = string.Join(", ", pgNonIdentityProperties.Select(p => p.Name));
                    var pgPlaceholders = string.Join(", ", pgNonIdentityProperties.Select((p, i) => string.Format("{0}{1}", config.Flag, p.Name)));
                    var insertSql = string.Format("INSERT INTO {0} ({1}) VALUES ({2})", pgTableName, pgColumns, pgPlaceholders);

                    AopBefore(new List<string> { pgTableName }, insertSql, null, config, false, AopType.AddList);

                    if (!isTrans)
                        BeginTrans();

                    cmd.CommandText = insertSql;

                    var affectedRows = 0;
                    var rowCountUnknown = false;

                    foreach (var item in list)
                    {
                        cmd.Parameters.Clear();
                        foreach (var prop in pgNonIdentityProperties)
                        {
                            var param = cmd.CreateParameter();
                            param.ParameterName = string.Format("{0}{1}", config.Flag, prop.Name);
                            var value = dyn.GetValue(item, prop.Name, true);
                            if (value is bool pgBoolVal)
                                param.Value = pgBoolVal;
                            else
                                param.Value = value ?? DBNull.Value;
                            cmd.Parameters.Add(param);
                        }

                        await EnsureConnectionOpenAsync(cancellationToken).ConfigureAwait(false);
                        var rowCount = await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                        if (rowCount < 0) rowCountUnknown = true; else affectedRows += rowCount;
                    }

                    if (!isTrans)
                        SubmitTrans();

                    // 原来这里无条件置成功：逐条执行的返回值被丢弃，
                    // 中间某行没写进去也照样报告成功。现在按实际受影响行数比对。
                    // -1 表示驱动不提供行数（如 SET NOCOUNT ON），无法判定时按成功处理。
                    result.WriteReturn.IsSuccess = rowCountUnknown || affectedRows >= list.Count;
                    if (!result.WriteReturn.IsSuccess)
                    {
                        result.WriteReturn.Message = string.Format(
                            "批量插入受影响行数不足：期望 {0} 行，实际 {1} 行", list.Count, affectedRows);
                    }
                    result.Sql = string.Format("{0} (x{1})", insertSql, list.Count);

                    AopAfter(new List<string> { pgTableName }, result.Sql, null, config, false, AopType.AddList, result.WriteReturn.IsSuccess);
                }

                if (config.DbType == DataDbType.SQLite)
                {
                    cmd.Parameters.Clear();

                    var sqliteTableName = TableNameHelper.GetTableName<T>();

                    // 使用缓存的非 Identity 属性数组
                    var sqliteProperties = PropertyCache.GetNonIdentityProperties<T>();

                    var columns = string.Join(", ", sqliteProperties.Select(p => p.Name));
                    var placeholders = string.Join(", ", sqliteProperties.Select(p => string.Format("{0}{1}", config.Flag, p.Name)));
                    var insertSql = string.Format("INSERT INTO {0} ({1}) VALUES ({2})", sqliteTableName, columns, placeholders);

                    AopBefore(new List<string> { sqliteTableName }, insertSql, null, config, false, AopType.AddList);

                    if (!isTrans)
                        BeginTrans();

                    cmd.CommandText = insertSql;

                    var affectedRows = 0;
                    var rowCountUnknown = false;

                    foreach (var item in list)
                    {
                        cmd.Parameters.Clear();
                        foreach (var prop in sqliteProperties)
                        {
                            var param = cmd.CreateParameter();
                            param.ParameterName = string.Format("{0}{1}", config.Flag, prop.Name);
                            var value = dyn.GetValue(item, prop.Name, true);
                            param.Value = value ?? DBNull.Value;
                            cmd.Parameters.Add(param);
                        }
                        var rowCount = await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                        if (rowCount < 0) rowCountUnknown = true; else affectedRows += rowCount;
                    }

                    if (!isTrans)
                        SubmitTrans();

                    // 原来这里无条件置成功：逐条执行的返回值被丢弃，
                    // 中间某行没写进去也照样报告成功。现在按实际受影响行数比对。
                    // -1 表示驱动不提供行数（如 SET NOCOUNT ON），无法判定时按成功处理。
                    result.WriteReturn.IsSuccess = rowCountUnknown || affectedRows >= list.Count;
                    if (!result.WriteReturn.IsSuccess)
                    {
                        result.WriteReturn.Message = string.Format(
                            "批量插入受影响行数不足：期望 {0} 行，实际 {1} 行", list.Count, affectedRows);
                    }
                    result.Sql = string.Format("{0} (x{1})", insertSql, list.Count);

                    AopAfter(new List<string> { sqliteTableName }, result.Sql, null, config, false, AopType.AddList, result.WriteReturn.IsSuccess);
                }

                if (result.WriteReturn.IsSuccess && isTrans)
                    SubmitTrans();
                else if (!result.WriteReturn.IsSuccess && isTrans)
                    RollbackTrans();

                AopAfter(tableName, cmd.CommandText, null, config, false, AopType.AddList, result.WriteReturn.IsSuccess);
            }
            catch (Exception ex)
            {
                AopException(ex, "AddListAsync tableName:" + typeof(T).Name, config, AopType.AddList);

                if (isTrans)
                    RollbackTrans();

                if (config?.SqlErrorType?.ToLower() == SqlErrorType.Db)
                    DbLogTable.LogException<T>(config, ex, "AddListAsync<T>", "");
                else
                    DbLog.LogException<T>(config.IsOutError, config.DbType, ex, "AddListAsync<T>", result.Sql);

                result.WriteReturn.IsSuccess = false;
                result.WriteReturn.Message = ex.Message;
            }

            return result;
        }

        /// <summary>
        /// 异步执行SQL
        /// </summary>
        public async Task<DataReturn> ExecuteSqlAsync(string sql, DbParameter[] param = null, bool isTrans = false, bool isLog = false, bool isProcedure = false, bool isAop = true, CancellationToken cancellationToken = default)
        {
            var result = new DataReturn();
            try
            {
                if (isTrans)
                    BeginTrans();

                if (param != null)
                    result.Sql = ParameterToSql.ObjectParamToSql(param.ToList(), sql, config);
                else
                    result.Sql = sql;

                DbLog.LogSql(isLog, result.Sql, config.DbType, 0);

                cmd.Parameters.Clear();

                if (param != null)
                    cmd.Parameters.AddRange(param);

                if (isAop)
                    AopBefore(null, sql, param?.ToList(), config, false, AopType.Execute_Sql_Bool);

                await EnsureConnectionOpenAsync(cancellationToken).ConfigureAwait(false);
                result.WriteReturn.IsSuccess = await BaseExecute.ToBoolAsync(cmd, sql, isProcedure).ConfigureAwait(false);

                if (isTrans && result.WriteReturn.IsSuccess)
                    SubmitTrans();
                else if (isTrans && !result.WriteReturn.IsSuccess)
                    RollbackTrans();

                if (isAop)
                    AopAfter(null, sql, param?.ToList(), config, false, AopType.Execute_Sql_Bool, result.WriteReturn.IsSuccess);
            }
            catch (Exception ex)
            {
                AopException(ex, "ExecuteSqlAsync", config, AopType.Execute_Sql_Bool);

                if (isTrans)
                    RollbackTrans();

                if (config?.SqlErrorType?.ToLower() == SqlErrorType.Db)
                    DbLogTable.LogException(config, ex, "ExecuteSqlAsync", result.Sql);
                else
                    DbLog.LogException(config.IsOutError, config.DbType, ex, "ExecuteSqlAsync", result.Sql);
                result.WriteReturn.IsSuccess = false;
                result.WriteReturn.Message = ex.Message;
            }

            return result;
        }

        /// <summary>
        /// 异步批量插入（使用 SqlBulkCopy 或逐条异步插入）
        /// </summary>
        public async Task<DataReturn> BulkInsertAsync<T>(List<T> list, bool isLog = false, CancellationToken cancellationToken = default) where T : class, new()
        {
            var result = new DataReturn();
            var stopwatch = Stopwatch.StartNew();

            try
            {
                BeginTrans();
                var tableName = TableNameHelper.GetTableName<T>();
                var allProperties = PropertyCache.GetPropertiesCached<T>().Where(p => p.CanRead && p.CanWrite).ToList();
                var properties = allProperties.Where(p =>
                {
                    var propInfo = typeof(T).GetProperty(p.Name);
                    if (propInfo == null) return true;
                    var colAttr = propInfo.GetCustomAttributes(typeof(Property.ColumnAttribute), false)
                        .FirstOrDefault() as Property.ColumnAttribute;
                    return colAttr == null || !colAttr.IsIdentity;
                }).ToList();
                var columnNames = string.Join(", ", properties.Select(p => p.Name));

                if (_command != null)
                {
                    _command.Parameters.Clear();
                    _command.Dispose();
                }
                _command = CreateCommand();
                if (_transaction != null)
                    _command.Transaction = _transaction;

                var paramNames = string.Join(", ", properties.Select((_, i) => string.Format("@p{0}", i)));
                var insertSql = string.Format("INSERT INTO {0} ({1}) VALUES ({2})", tableName, columnNames, paramNames);
                _command.CommandText = insertSql;

                foreach (var item in list)
                {
                    _command.Parameters.Clear();
                    for (int i = 0; i < properties.Count; i++)
                    {
                        var value = properties[i].GetValue(item);
                        var param = _command.CreateParameter();
                        param.ParameterName = string.Format("@p{0}", i);
                        param.Value = value ?? DBNull.Value;
                        _command.Parameters.Add(param);
                    }

                    await EnsureConnectionOpenAsync(cancellationToken).ConfigureAwait(false);
                    await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                }

                result.Sql = string.Format("{0} (x{1})", insertSql, list.Count);
                if (isLog)
                    DbLog.LogSql(true, result.Sql, config.DbType, 0);

                AopBefore(new List<string> { tableName }, insertSql, new List<DbParameter>(), config, false, AopType.AddList);
                result.WriteReturn.IsSuccess = true;
                SubmitTrans();
                AopAfter(new List<string> { tableName }, insertSql, new List<DbParameter>(), config, false, AopType.AddList, list.Count);
            }
            catch (Exception ex)
            {
                RollbackTrans();
                AopException(ex, "BulkInsertAsync", config, AopType.AddList);

                if (config?.SqlErrorType?.ToLower() == SqlErrorType.Db)
                    DbLogTable.LogException(config, ex, "BulkInsertAsync", result.Sql);
                else
                    DbLog.LogException(config.IsOutError, config.DbType, ex, "BulkInsertAsync", result.Sql);

                result.WriteReturn.IsSuccess = false;
                result.WriteReturn.Message = ex.Message;
            }

            stopwatch.Stop();
            return result;
        }

        /// <summary>
        /// 异步批量更新
        /// </summary>
        public async Task<DataReturn> BulkUpdateAsync<T>(List<T> list, Expression<Func<T, bool>> predicate, bool isLog = false, CancellationToken cancellationToken = default) where T : class, new()
        {
            var result = new DataReturn();
            var stopwatch = Stopwatch.StartNew();

            try
            {
                BeginTrans();

                var tableName = TableNameHelper.GetTableName<T>();
                var properties = PropertyCache.GetPropertiesCached<T>()
                    .Where(p => p.CanRead && p.CanWrite && p.Name != "Id")
                    .ToList();

                var setClause = string.Join(", ", properties.Select(p => string.Format("{0} = @{0}", p.Name)));

                var visitModel = VisitExpression.LambdaWhere<T>(predicate, config);
                if (!visitModel.IsSuccess)
                {
                    throw new InvalidOperationException("更新条件解析失败");
                }

                var whereClause = visitModel.Where;
                var paramList = visitModel.Param;

                foreach (var item in list)
                {
                    cmd.Parameters.Clear();
                    foreach (var prop in properties)
                    {
                        var value = prop.GetValue(item);
                        var param = cmd.CreateParameter();
                        param.ParameterName = string.Format("@{0}", prop.Name);
                        param.Value = value ?? DBNull.Value;
                        cmd.Parameters.Add(param);
                    }

                    await EnsureConnectionOpenAsync(cancellationToken).ConfigureAwait(false);

                    var updateSql = string.Format("UPDATE {0} SET {1} WHERE {2}", tableName, setClause, whereClause);
                    cmd.CommandText = updateSql;
                    await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                }

                result.Sql = string.Format("UPDATE {0} SET {1} WHERE {2} (x{3})", tableName, setClause, whereClause, list.Count);
                if (isLog)
                    DbLog.LogSql(true, result.Sql, config.DbType, 0);

                AopBefore(new List<string> { tableName }, result.Sql, paramList, config, false, AopType.UpdateList);
                result.WriteReturn.IsSuccess = true;
                SubmitTrans();
                AopAfter(new List<string> { tableName }, result.Sql, paramList, config, false, AopType.UpdateList, list.Count);
            }
            catch (Exception ex)
            {
                RollbackTrans();
                AopException(ex, "BulkUpdateAsync", config, AopType.UpdateList);

                if (config?.SqlErrorType?.ToLower() == SqlErrorType.Db)
                    DbLogTable.LogException(config, ex, "BulkUpdateAsync", result.Sql);
                else
                    DbLog.LogException(config.IsOutError, config.DbType, ex, "BulkUpdateAsync", result.Sql);

                result.WriteReturn.IsSuccess = false;
                result.WriteReturn.Message = ex.Message;
            }

            stopwatch.Stop();
            return result;
        }

        /// <summary>
        /// 异步批量删除
        /// </summary>
        public async Task<DataReturn> BulkDeleteAsync<T>(Expression<Func<T, bool>> predicate, bool isLog = false, CancellationToken cancellationToken = default) where T : class, new()
        {
            var result = new DataReturn();
            var stopwatch = Stopwatch.StartNew();

            try
            {
                BeginTrans();

                var tableName = TableNameHelper.GetTableName<T>();
                var visitModel = VisitExpression.LambdaWhere<T>(predicate, config);
                if (!visitModel.IsSuccess)
                {
                    throw new InvalidOperationException("删除条件解析失败");
                }

                var whereClause = visitModel.Where;
                var paramList = visitModel.Param;

                await EnsureConnectionOpenAsync(cancellationToken).ConfigureAwait(false);

                var deleteSql = string.Format("DELETE FROM {0} WHERE {1}", tableName, whereClause);
                cmd.CommandText = deleteSql;

                foreach (var param in paramList)
                {
                    cmd.Parameters.Add(param);
                }

                int affectedRows = await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                result.Sql = string.Format("DELETE FROM {0} WHERE {1} (x{2})", tableName, whereClause, affectedRows);
                if (isLog)
                    DbLog.LogSql(true, result.Sql, config.DbType, 0);

                AopBefore(new List<string> { tableName }, result.Sql, paramList, config, false, AopType.Delete_Lambda);
                result.WriteReturn.IsSuccess = true;
                result.WriteReturn.Message = string.Format("删除 {0} 条记录", affectedRows);
                SubmitTrans();
                AopAfter(new List<string> { tableName }, result.Sql, paramList, config, false, AopType.Delete_Lambda, affectedRows);
            }
            catch (Exception ex)
            {
                RollbackTrans();
                AopException(ex, "BulkDeleteAsync", config, AopType.Delete_Lambda);

                if (config?.SqlErrorType?.ToLower() == SqlErrorType.Db)
                    DbLogTable.LogException(config, ex, "BulkDeleteAsync", result.Sql);
                else
                    DbLog.LogException(config.IsOutError, config.DbType, ex, "BulkDeleteAsync", result.Sql);

                result.WriteReturn.IsSuccess = false;
                result.WriteReturn.Message = ex.Message;
            }

            stopwatch.Stop();
            return result;
        }

        #endregion
    }
}
