using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Data.Common;
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
        #region 获取列表
        /// <summary>
        /// 获取列表
        /// </summary>
        /// <param name="item">数据查询对象</param>
        /// <returns>查询结果列表或单条记录</returns>
        public DataReturn<T> GetList<T>(DataQuery item) where T : class,new()
        {
            var param = new List<DbParameter>();
            var result = new DataReturn<T>();
            var sql = new StringBuilder();
            object data;

            try
            {
                BuildBaseSelectQuery(item, param, sql);

                result.Sql = ParameterToSql.ObjectParamToSql(param, sql.ToString(), item.Config);

                if (param.Count != 0)
                    cmd.Parameters.AddRange(param.ToArray());

                AopBefore(item.Table, sql.ToString(), param, config, true,AopType.Query_List_Lambda);

                if (conn.State == ConnectionState.Closed)
                    conn.Open();

                using (var dr = BaseExecute.ToDataReader(cmd, sql.ToString()))
                {
                    // Take(1) 只是「只查一条」的优化提示，不代表调用方要的是 Item。
                    // 原来该分支只填 Item、不填 List，导致 Take(1).ToList() / Take(1).ToJson()
                    // 拿到空集合（测试 ToJson_ShouldReturnJsonString / ToListAsync_ShouldReturnResults 复现）。
                    var items = BaseDataReader.ToList<T>(dr, item.Config, item.Field);
                    result.List = items;
                    result.Item = items.FirstOrDefault<T>();
                    data = item.Take == 1 ? (object)result.Item : (object)items;
                }

                AopAfter(item.Table, sql.ToString(), param, config, true, AopType.Query_List_Lambda, data);

                return result;
            }
            catch (Exception ex)
            {
                AopException(ex, "to List tableName:" + typeof(T).Name,config, AopType.Query_List_Lambda);

                if (string.Equals(config?.SqlErrorType, SqlErrorType.Db, StringComparison.OrdinalIgnoreCase))
                    DbLogTable.LogException<T>(config, ex, "GetList<T>", "");
                else
                    DbLog.LogException<T>(item.Config.IsOutError, item.Config.DbType, ex, "GetList<T>", result.Sql);
                return result;
            }
        }
        #endregion

        #region 获取分页
        /// <summary>
        /// 获取分页
        /// </summary>
        /// <param name="item">数据查询对象</param>
        /// <param name="pModel">分页模型</param>
        /// <returns>分页查询结果</returns>
        public DataReturn<T> GetPage<T>(DataQuery item, PageModel pModel) where T : class,new()
        {
            var param = new List<DbParameter>();
            var result = new DataReturn<T>();
            var sql = "";

            try
            {
                pModel.StarId = (pModel.PageId - 1) * pModel.PageSize + 1;
                pModel.EndId = pModel.PageId * pModel.PageSize;
                // 清理参数而非 dispose 命令对象（PostgreSQL/Npgsql 不支持对已 dispose 的命令重新使用）
                cmd.Parameters.Clear();
                if (conn.State == ConnectionState.Closed)
                    conn.Open();
                pModel.TotalRecord = PaginationSqlBuilder.Count(item, cmd, ref sql);

                if (pModel.TotalRecord > 0)
                {
                    if ((pModel.TotalRecord % pModel.PageSize) == 0)
                        pModel.TotalPage = pModel.TotalRecord / pModel.PageSize;
                    else
                        pModel.TotalPage = (pModel.TotalRecord / pModel.PageSize) + 1;

                    if (pModel.PageId > pModel.TotalPage)
                        pModel.PageId = pModel.TotalPage;

                    // 页码被夹取后必须重算边界，否则仍会按越界页码取数
                    pModel.StarId = (pModel.PageId - 1) * pModel.PageSize + 1;
                    pModel.EndId = pModel.PageId * pModel.PageSize;

                    AopBefore(item.Table, sql.ToString(), param, config, true,AopType.Query_Page_Lambda_Model);

                    // 清理参数而非 dispose 命令对象
                    cmd.Parameters.Clear();
                    using (var dr = PaginationSqlBuilder.Read(item, cmd, pModel, ref sql))
                    {
                        if (dr != null)
                        {
                            result.PageResult.list = BaseDataReader.ToList<T>(dr, item.Config, item.AsName);
                        }
                    }
                    result.Sql = sql;

                    AopAfter(item.Table, sql.ToString(), param, config, true, AopType.Query_Page_Lambda_Model, result.PageResult.list);
                }
                else
                    result.PageResult.list = new List<T>();

                // 把实际执行的 SQL 带回去：分页返回空列表时，仅看 list 无法区分
                // 「条件本来就无匹配」与「SQL 生成错误」，两者都表现为 0 行。
                result.PageResult.Sql = string.IsNullOrEmpty(result.Sql) ? sql : result.Sql;

                result.PageResult.pModel = pModel;
            }
            catch (Exception ex)
            {
                AopException(ex, "to Page tableName:" + typeof(T).Name,config, AopType.Query_Page_Lambda_Model);

                if (string.Equals(config?.SqlErrorType, SqlErrorType.Db, StringComparison.OrdinalIgnoreCase))
                    DbLogTable.LogException<T>(config, ex, "GetPage<T>", "");
                else
                    DbLog.LogException<T>(item.Config.IsOutError, item.Config.DbType, ex, "GetPage<T>", result.Sql);

                // 异常不能只记日志就返回空壳：调用方看到的 TotalRecord=0 + 空列表，
                // 与「查询确实没有匹配行」完全无法区分（分页查询长期表现为「静默 0 行」的原因）。
                // 把失败信号与原因带回结果对象。
                result.WriteReturn.IsSuccess = false;
                result.WriteReturn.Message = string.Format("{0}: {1}", ex.GetType().Name, ex.Message);
                result.Sql = string.IsNullOrEmpty(result.Sql) ? sql : result.Sql;
                result.PageResult.Sql = result.Sql;
                result.PageResult.pModel = pModel;
            }

            return result;
        }
        #endregion

        #region 获取分页
        /// <summary>
        /// 获取分页
        /// </summary>
        /// <param name="item">数据查询对象</param>
        /// <param name="pModel">分页模型</param>
        /// <returns>数据返回对象</returns>
        public DataReturn GetPage(DataQuery item, PageModel pModel)
        {
            var param = new List<DbParameter>();
            var result = new DataReturn();
            var sql = "";

            try
            {
                pModel.StarId = (pModel.PageId - 1) * pModel.PageSize + 1;
                pModel.EndId = pModel.PageId * pModel.PageSize;
                // 清理参数而非 dispose 命令对象（PostgreSQL/Npgsql 不支持对已 dispose 的命令重新使用）
                cmd.Parameters.Clear();
                if (conn.State == ConnectionState.Closed)
                    conn.Open();
                pModel.TotalRecord = PaginationSqlBuilder.Count(item, cmd, ref sql);

                if (pModel.TotalRecord > 0)
                {
                    if ((pModel.TotalRecord % pModel.PageSize) == 0)
                        pModel.TotalPage = pModel.TotalRecord / pModel.PageSize;
                    else
                        pModel.TotalPage = (pModel.TotalRecord / pModel.PageSize) + 1;

                    if (pModel.PageId > pModel.TotalPage)
                        pModel.PageId = pModel.TotalPage;

                    // 页码被夹取后必须重算边界，否则仍会按越界页码取数
                    pModel.StarId = (pModel.PageId - 1) * pModel.PageSize + 1;
                    pModel.EndId = pModel.PageId * pModel.PageSize;

                    AopBefore(item.Table, sql.ToString(), param, config, true,AopType.Query_Page_Lambda_Dic);

                    // 清理参数而非 dispose 命令对象
                    cmd.Parameters.Clear();
                    using (var dr = PaginationSqlBuilder.Read(item, cmd, pModel, ref sql))
                    {
                        if (dr != null)
                        {
                            result.PageResult.list = BaseJson.DataReaderToDic(dr, config.DbType == DataDbType.Oracle);
                        }
                    }
                    result.Sql = sql;

                    AopAfter(item.Table, sql.ToString(), param, config, true, AopType.Query_Page_Lambda_Dic, result.PageResult.list);
                }
                else
                    result.PageResult.list = new List<Dictionary<string, object>>();

                // 把实际执行的 SQL 带回去：分页返回空列表时，仅看 list 无法区分
                // 「条件本来就无匹配」与「SQL 生成错误」，两者都表现为 0 行。
                result.PageResult.Sql = string.IsNullOrEmpty(result.Sql) ? sql : result.Sql;

                result.PageResult.pModel = pModel;
            }
            catch (Exception ex)
            {
                AopException(ex, "to Page",config, AopType.Query_Page_Lambda_Dic);

                if (string.Equals(item.Config?.SqlErrorType, SqlErrorType.Db, StringComparison.OrdinalIgnoreCase))
                    DbLogTable.LogException(item.Config, ex, "GetPage", result.Sql);
                else
                    DbLog.LogException(item.Config?.IsOutError ?? false, item.Config?.DbType ?? DataDbType.SqlServer, ex, "GetPage", result.Sql);
            }

            return result;
        }
        #endregion

        #region 获取分页sql
        /// <summary>
        /// 获取分页
        /// </summary>
        /// <param name="pModel">分页模型</param>
        /// <param name="sql">SQL语句</param>
        /// <param name="param">数据库参数数组</param>
        /// <param name="isAop">是否启用AOP</param>
        /// <returns>数据返回对象</returns>
        public DataReturn GetPageSql(PageModel pModel, string sql, DbParameter[] param,bool isAop=true)
        {
            var result = new DataReturn();
            var countSql = "";
            var pageSql = "";

            try
            {
                pModel.StarId = (pModel.PageId - 1) * pModel.PageSize + 1;
                pModel.EndId = pModel.PageId * pModel.PageSize;
                // 清理参数而非 dispose 命令对象
                cmd.Parameters.Clear();
                if (conn.State == ConnectionState.Closed)
                    conn.Open();
                pModel.TotalRecord = BaseExecute.ToPageCountSql(param, cmd, sql, config, ref countSql);

                if (pModel.TotalRecord > 0)
                {
                    if ((pModel.TotalRecord % pModel.PageSize) == 0)
                        pModel.TotalPage = pModel.TotalRecord / pModel.PageSize;
                    else
                        pModel.TotalPage = (pModel.TotalRecord / pModel.PageSize) + 1;

                    if (pModel.PageId > pModel.TotalPage)
                        pModel.PageId = pModel.TotalPage;

                    // 页码被夹取后必须重算边界，否则仍会按越界页码取数
                    pModel.StarId = (pModel.PageId - 1) * pModel.PageSize + 1;
                    pModel.EndId = pModel.PageId * pModel.PageSize;

                    if (isAop)
                        AopBefore(null, sql.ToString(), param?.ToList(), config, true,AopType.Query_Page_Lambda_Dic);

                    // 清理参数而非 dispose 命令对象
                    cmd.Parameters.Clear();
                    using (var dr = BaseExecute.ToPageDataReaderSql(param, cmd, pModel, sql, config, ref pageSql))
                    {
                        if (dr != null)
                        {
                            result.PageResult.list = BaseJson.DataReaderToDic(dr, config.DbType == DataDbType.Oracle);
                        }
                    }
                    result.Sql = string.Format("count:{0},page:{1}", countSql, pageSql);

                    if(isAop)
                        AopAfter(null, sql.ToString(), param?.ToList(), config, true, AopType.Query_Page_Lambda_Dic, result.PageResult.list);
                }
                else
                    result.PageResult.list = new List<Dictionary<string, object>>();

                // 把实际执行的 SQL 带回去：分页返回空列表时，仅看 list 无法区分
                // 「条件本来就无匹配」与「SQL 生成错误」，两者都表现为 0 行。
                result.PageResult.Sql = string.IsNullOrEmpty(result.Sql) ? sql : result.Sql;

                result.PageResult.pModel = pModel;
            }
            catch (Exception ex)
            {
                AopException(ex, "to Page sql",config, AopType.Query_Page_Lambda_Dic);

                if (string.Equals(config?.SqlErrorType, SqlErrorType.Db, StringComparison.OrdinalIgnoreCase))
                    DbLogTable.LogException(config, ex, "GetPageSql", result.Sql);
                else
                    DbLog.LogException(config.IsOutError, config.DbType, ex, "GetPageSql", result.Sql);
            }

            return result;
        }
        #endregion
        
        #region 获取分页sql
        /// <summary>
        /// 获取分页
        /// </summary>
        /// <param name="pModel">分页模型</param>
        /// <param name="sql">SQL语句</param>
        /// <param name="param">SQL参数数组</param>
        /// <param name="isAop">是否启用AOP</param>
        /// <returns>分页查询结果</returns>
        public DataReturn<T> GetPageSql<T>(PageModel pModel, string sql, DbParameter[] param,bool isAop=true) where T : class, new()
        {
            var result = new DataReturn<T>();
            var countSql = "";
            var pageSql = "";

            try
            {
                pModel.StarId = (pModel.PageId - 1) * pModel.PageSize + 1;
                pModel.EndId = pModel.PageId * pModel.PageSize;
                // 清理参数而非 dispose 命令对象
                cmd.Parameters.Clear();
                if (conn.State == ConnectionState.Closed)
                    conn.Open();
                pModel.TotalRecord = BaseExecute.ToPageCountSql(param, cmd, sql, config, ref countSql);

                if (pModel.TotalRecord > 0)
                {
                    if ((pModel.TotalRecord % pModel.PageSize) == 0)
                        pModel.TotalPage = pModel.TotalRecord / pModel.PageSize;
                    else
                        pModel.TotalPage = (pModel.TotalRecord / pModel.PageSize) + 1;

                    if (pModel.PageId > pModel.TotalPage)
                        pModel.PageId = pModel.TotalPage;

                    // 页码被夹取后必须重算边界，否则仍会按越界页码取数
                    pModel.StarId = (pModel.PageId - 1) * pModel.PageSize + 1;
                    pModel.EndId = pModel.PageId * pModel.PageSize;

                    if (isAop)
                        AopBefore(null, sql.ToString(), param?.ToList(), config, true,AopType.Query_Page_Lambda_Model);

                    // 清理参数而非 dispose 命令对象
                    cmd.Parameters.Clear();
                    using (var dr = BaseExecute.ToPageDataReaderSql(param, cmd, pModel, sql, config, ref pageSql))
                    {
                        if (dr != null)
                        {
                            result.PageResult.list = BaseDataReader.ToList<T>(dr, config, null);
                        }
                    }
                    result.Sql = string.Format("count:{0},page:{1}", countSql, pageSql);

                    if (isAop)
                        AopAfter(null, sql.ToString(), param?.ToList(), config, true, AopType.Query_Page_Lambda_Model, result.PageResult.list);
                }

                // 把实际执行的 SQL 带回去：分页返回空列表时，仅看 list 无法区分
                // 「条件本来就无匹配」与「SQL 生成错误」，两者都表现为 0 行。
                result.PageResult.Sql = string.IsNullOrEmpty(result.Sql) ? sql : result.Sql;

                result.PageResult.pModel = pModel;
            }
            catch (Exception ex)
            {
                AopException(ex, "to Page tableName:" + typeof(T).Name,config, AopType.Query_Page_Lambda_Model);

                if (string.Equals(config?.SqlErrorType, SqlErrorType.Db, StringComparison.OrdinalIgnoreCase))
                    DbLogTable.LogException(config, ex, "GetPageSql", result.Sql);
                else
                    DbLog.LogException(config.IsOutError, config.DbType, ex, "GetPageSql", result.Sql);
            }

            return result;
        }
        #endregion

        #region 获取json
        /// <summary>
        /// 获取json多表
        /// </summary>
        /// <param name="item">数据查询对象</param>
        /// <returns>JSON格式的查询结果</returns>
        public DataReturn GetJson(DataQuery item)
        {
            var param = new List<DbParameter>();
            var result = new DataReturn();
            var sql = new StringBuilder();

            try
            {
                // SQL Server TOP 特殊处理
                BuildBaseSelectQuery(item, param, sql);

                if (item.Predicate[0].Param.Count != 0)
                    param.AddRange(item.Predicate[0].Param);

                result.Sql = ParameterToSql.ObjectParamToSql(param, sql.ToString(), item.Config);

                cmd.Parameters.Clear();

                if (param.Count != 0)
                    cmd.Parameters.AddRange(param.ToArray());

                AopBefore(null, sql.ToString(), param?.ToList(), config, true,AopType.Query_Json_Lambda);

                if (conn.State == ConnectionState.Closed)
                    conn.Open();
                using (var dr = BaseExecute.ToDataReader(cmd, sql.ToString()))
                {
                    result.Json = BaseJson.DataReaderToJson(dr, config.DbType == DataDbType.Oracle);
                }

                AopAfter(null, sql.ToString(), param?.ToList(), config, true, AopType.Query_Json_Lambda, result.Json);

                return result;
            }
            catch (Exception ex)
            {
                AopException(ex, "to Json",config, AopType.Query_Json_Lambda);

                if (string.Equals(config?.SqlErrorType, SqlErrorType.Db, StringComparison.OrdinalIgnoreCase))
                    DbLogTable.LogException(config, ex, "GetJson", result.Sql);
                else
                    DbLog.LogException(item.Config.IsOutError, item.Config.DbType, ex, "GetJson", result.Sql);
                return result;
            }
        }
        #endregion

        #region 获取条数
        /// <summary>
        /// 获取条数
        /// </summary>
        /// <param name="item">数据查询对象</param>
        /// <returns>记录总数</returns>
        public DataReturn GetCount(DataQuery item)
        {
            var sql = new StringBuilder();
            var result = new DataReturn();
            var param = new List<DbParameter>();

            try
            {
                sql.AppendFormat("select count(0) from {0}", item.Table[0]);

                for (var i = 1; i < item.Predicate.Count; i++)
                {
                    sql.AppendFormat(" {0} on {1}", item.Table[i], item.Predicate[i].Where);

                    if (item.Predicate[i].Param.Count != 0)
                        param.AddRange(item.Predicate[i].Param);
                }

                // 使用 WhereBuilder 构建完整的 WHERE 子句（支持链式条件）
                if (WhereBuilder.HasWhereClause(item))
                {
                    var whereClause = WhereBuilder.BuildWhereClause(item, ref param);
                    sql.AppendFormat(" where {0}", whereClause);
                }

                if (item.GroupBy.Count > 0)
                {
                    sql.Append(" group by ");
                    sql.Append(string.Join(",", item.GroupBy));
                }

                if (item.OrderBy.Count > 0)
                {
                    sql.Append(" order by ");
                    sql.Append(string.Join(",", item.OrderBy));
                }

                result.Sql = ParameterToSql.ObjectParamToSql(param, sql.ToString(), item.Config);

                cmd.Parameters.Clear();

                if (param.Count != 0)
                    cmd.Parameters.AddRange(param.ToArray());

                AopBefore(null, sql.ToString(), param?.ToList(), config, true,AopType.Query_Json_Lambda);

                if (conn.State == ConnectionState.Closed)
                    conn.Open();
                var dt = BaseExecute.ToDataTable(cmd, sql.ToString());

                if (dt.Rows.Count > 0)
                    result.Count = dt.Rows[0][0].ToString().ToInt(0);
                else
                    result.Count = 0;

                AopAfter(null, sql.ToString(), param?.ToList(), config, true, AopType.Query_Json_Lambda, result.Count);

                return result;
            }
            catch (Exception ex)
            {
                AopException(ex, "to Count",config, AopType.Query_Json_Lambda);

                if (string.Equals(config?.SqlErrorType, SqlErrorType.Db, StringComparison.OrdinalIgnoreCase))
                    DbLogTable.LogException(config, ex, "GetCount", result.Sql);
                else
                    DbLog.LogException(item.Config.IsOutError, item.Config.DbType, ex, "GetCount", result.Sql);
                return result;
            }
        }
        #endregion

        #region 执行sql
        /// <summary>
        /// 执行sql
        /// </summary>
        /// <param name="sql">SQL语句</param>
        /// <param name="param">SQL参数数组</param>
        /// <returns>查询结果列表</returns>
        public DataReturn<T> ExecuteSql<T>(string sql, DbParameter[] param=null) where T : class,new()
        {
            var result = new DataReturn<T>();
            try
            {
                if (param != null)
                    result.Sql = ParameterToSql.ObjectParamToSql(param.ToList(), sql, config);
                else
                    result.Sql = sql;

                cmd.Parameters.Clear();

                if (param != null)
                    cmd.Parameters.AddRange(param.ToArray());

                AopBefore(null, sql.ToString(), param?.ToList(), config, true,AopType.Execute_Sql_Model);

                if (conn.State == ConnectionState.Closed)
                    conn.Open();
                using (var dr = BaseExecute.ToDataReader(cmd, sql))
                {
                    result.List = BaseDataReader.ToList<T>(dr, config);
                }

                AopAfter(null, sql.ToString(), param?.ToList(), config, true, AopType.Execute_Sql_Model, result.List);
            }
            catch (Exception ex)
            {
                AopException(ex, "ExecuteSql tableName:" + typeof(T).Name,config, AopType.Execute_Sql_Model);

                if (string.Equals(config?.SqlErrorType, SqlErrorType.Db, StringComparison.OrdinalIgnoreCase))
                    DbLogTable.LogException<T>(config, ex, "ExecuteSql<T>", "");
                else
                    DbLog.LogException<T>(config.IsOutError, config.DbType, ex, "ExecuteSql<T>", result.Sql);
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
        /// <param name="isLog">是否记录日志</param>
        /// <param name="isAop">是否启用AOP</param>
        /// <returns>数据返回对象</returns>
        public DataReturn ExecuteSqlList(string sql, DbParameter[] param=null,bool isLog=false,bool isAop=true)
        {
            var result = new DataReturn();
            try
            {
                if (param != null)
                    result.Sql = ParameterToSql.ObjectParamToSql(param.ToList(), sql, config);
                else
                    result.Sql = sql;

                DbLog.LogSql(isLog, result.Sql, config.DbType, 0);

                cmd.Parameters.Clear();

                if (param != null)
                    cmd.Parameters.AddRange(param.ToArray());

                if(isAop)
                    AopBefore(null, sql.ToString(), param?.ToList(), config, true,AopType.Execute_Sql_Dic);

                if (conn.State == ConnectionState.Closed)
                    conn.Open();
                using (var dr = BaseExecute.ToDataReader(cmd, sql))
                {
                    result.DicList = BaseJson.DataReaderToDic(dr, config.DbType == DataDbType.Oracle);
                }
                result.WriteReturn.IsSuccess = true;

                if (isAop)
                    AopAfter(null, sql.ToString(), param?.ToList(), config, true, AopType.Execute_Sql_Dic, result.WriteReturn.IsSuccess);
            }
            catch (Exception ex)
            {
                AopException(ex, "Execute Sql",config, AopType.Execute_Sql_Dic);

                result.WriteReturn.IsSuccess = false;
                result.WriteReturn.Message = ex.Message;

                if (string.Equals(config?.SqlErrorType, SqlErrorType.Db, StringComparison.OrdinalIgnoreCase))
                    DbLogTable.LogException(config, ex, "ExecuteSqlList", result.Sql);
                else
                    DbLog.LogException(config.IsOutError, config.DbType, ex, "ExecuteSqlList", result.Sql);
            }

            return result;
        }
        #endregion

        #region 获取dic
        /// <summary>
        /// 获取dic
        /// </summary>
        /// <param name="item">数据查询对象</param>
        /// <returns>字典格式的查询结果</returns>
        public DataReturn GetDic(DataQuery item)
        {
            var param = new List<DbParameter>();
            var result = new DataReturn();
            var sql = new StringBuilder();
            object data;

            try
            {
                // SQL Server TOP 特殊处理
                if (item.Config.DbType == DataDbType.SqlServer && (item.Take != 0 || item.HasTake))
                    sql.AppendFormat("select top {2} {0} from {1}", string.Join(",", item.Field), item.Table[0], item.Take);
                
                BuildBaseSelectQuery(item, param, sql);

                result.Sql = ParameterToSql.ObjectParamToSql(param, sql.ToString(), item.Config);

                cmd.Parameters.Clear();

                if (param.Count != 0)
                    cmd.Parameters.AddRange(param.ToArray());

                AopBefore(item.Table, sql.ToString(), param, config, true,AopType.Query_Dic_Lambda);

                if (conn.State == ConnectionState.Closed)
                    conn.Open();
                using (var dr = BaseExecute.ToDataReader(cmd, sql.ToString()))
                {
                    // 同实体版：Take(1) 时也要填 DicList，否则 Take(1).ToList() 拿到空集合。
                    var dicList = BaseJson.DataReaderToDic(dr, config.DbType == DataDbType.Oracle);
                    result.DicList = dicList;
                    result.Dic = dicList.FirstOrDefault() ?? new Dictionary<string, object>();
                    data = item.Take == 1 ? (object)result.Dic : (object)dicList;
                }

                AopAfter(item.Table, sql.ToString(), param, config, true, AopType.Query_Dic_Lambda, data);

                return result;
            }
            catch (Exception ex)
            {
                AopException(ex, "to Dic",config, AopType.Query_Dic_Lambda);

                if (string.Equals(config?.SqlErrorType, SqlErrorType.Db, StringComparison.OrdinalIgnoreCase))
                    DbLogTable.LogException(config, ex, "GetDic", result.Sql);
                else
                    DbLog.LogException(item.Config.IsOutError, item.Config.DbType, ex, "GetDic", result.Sql);
                return result;
            }
        }
        #endregion

        #region 获取DataTable
        /// <summary>
        /// 获取DataTable
        /// </summary>
        /// <param name="item">数据查询对象</param>
        /// <returns>DataTable对象</returns>
        public DataReturn GetDataTable(DataQuery item)
        {
            var param = new List<DbParameter>();
            var result = new DataReturn();
            var sql = new StringBuilder();

            try
            {
                // SQL Server TOP 特殊处理
                if (item.Config.DbType == DataDbType.SqlServer && (item.Take != 0 || item.HasTake))
                    sql.AppendFormat("select top {2} {0} from {1}", string.Join(",", item.Field), item.Table[0], item.Take);
                
                BuildBaseSelectQuery(item, param, sql);

                result.Sql = ParameterToSql.ObjectParamToSql(param, sql.ToString(), item.Config);

                cmd.Parameters.Clear();

                if (param.Count != 0)
                    cmd.Parameters.AddRange(param.ToArray());

                AopBefore(item.Table, sql.ToString(), param, config, true,AopType.Query_DataTable_Lambda);

                if (conn.State == ConnectionState.Closed)
                    conn.Open();
                using (var dr = BaseExecute.ToDataReader(cmd, sql.ToString()))
                {
                    result.Table.Load(dr);
                }

                AopAfter(item.Table, sql.ToString(), param, config, true, AopType.Query_DataTable_Lambda, result.Table);

                return result;
            }
            catch (Exception ex)
            {
                AopException(ex, "to DataTable",config, AopType.Query_DataTable_Lambda);

                if (string.Equals(config?.SqlErrorType, SqlErrorType.Db, StringComparison.OrdinalIgnoreCase))
                    DbLogTable.LogException(config, ex, "GetDataTable", result.Sql);
                else
                    DbLog.LogException(item.Config.IsOutError, item.Config.DbType, ex, "GetDataTable", result.Sql);
                return result;
            }
        }
        #endregion

        #region 私有辅助方法

        /// <summary>
        /// 构建基础 SELECT + JOIN + WHERE + GROUP BY + ORDER BY 查询
        /// </summary>
        /// <param name="item">数据查询对象</param>
        /// <param name="param">数据库参数列表</param>
        /// <param name="sql">SQL构建器</param>
        private void BuildBaseSelectQuery(DataQuery item, List<DbParameter> param, StringBuilder sql)
        {
            sql.Append("select ");
            if (item.Config.DbType == DataDbType.SqlServer && (item.Take != 0 || item.HasTake))
            {
                sql.Append("top ");
                sql.Append(item.Take);
                sql.Append(" ");
            }
            // 字段列表为空时（调用方没有显式 Select）必须用 *，否则会拼出
            // `select top 1  from perf_users ...` 这种空列清单的语法错误，
            // 异常被上层 catch 吞掉后表现为「ToJson 返回空」。
            if (item.Field.Count == 0)
            {
                sql.Append("* ");
            }
            else
            {
                for (int i = 0; i < item.Field.Count; i++)
                {
                    if (i > 0) sql.Append(",");
                    sql.Append(item.Field[i]);
                }
            }
            sql.Append(" from ");
            sql.Append(item.Table[0]);

            for (var i = 1; i < item.Predicate.Count; i++)
            {
                sql.Append(" ");
                sql.Append(item.Table[i]);
                sql.Append(" on ");
                sql.Append(item.Predicate[i].Where);
                if (item.Predicate[i].Param.Count != 0)
                    param.AddRange(item.Predicate[i].Param);
            }

            var whereClause = WhereBuilder.BuildWhereClause(item, ref param);
            if (!string.IsNullOrEmpty(whereClause))
            {
                sql.Append(" where ");
                sql.Append(whereClause);
            }

            // Oracle 的 rownum 只能出现在 WHERE 中；其余方言的 LIMIT / FETCH FIRST
            // 必须跟在 ORDER BY 之后，否则会拼出 `... where x limit 2 order by id`
            // 这种语法错误（issue：分页查询返回 0 行）。
            if (item.Config.DbType == DataDbType.Oracle)
                AppendTakeClause(item, sql);

            if (item.GroupBy.Count > 0)
            {
                sql.Append(" group by ");
                sql.Append(string.Join(",", item.GroupBy));
            }

            if (item.OrderBy.Count > 0)
            {
                sql.Append(" order by ");
                sql.Append(string.Join(",", item.OrderBy));
            }

            if (item.Config.DbType != DataDbType.Oracle)
                AppendTakeClause(item, sql);
        }

        /// <summary>
        /// 追加 TAKE/LIMIT 子句（数据库特定语法）
        /// </summary>
        /// <param name="item">数据查询对象</param>
        /// <param name="sql">SQL构建器</param>
        private void AppendTakeClause(DataQuery item, StringBuilder sql)
        {
            // Take 的默认值是 0，若仅凭 0 就 return，会把「显式 Take(0)」当成「不限制」，
            // 于是 Take(0) 返回全表。用 HasTake 区分两种语义。
            if (item.Take == 0 && !item.HasTake)
                return;

            if (item.Config.DbType == DataDbType.SqlServer)
            {
                // SQL Server 的 TOP 已在主查询中处理
            }
            else if (item.Config.DbType == DataDbType.Oracle)
            {
                sql.AppendFormat(" and rownum <={0}", item.Take);
            }
            else if (item.Config.DbType == DataDbType.DB2)
            {
                sql.AppendFormat(" and fetch first {0} rows only", item.Take);
            }
            else if (item.Config.DbType == DataDbType.MySql || 
                     item.Config.DbType == DataDbType.PostgreSql || 
                     item.Config.DbType == DataDbType.SQLite)
            {
                sql.AppendFormat(" limit {0}", item.Take);
            }
        }

        #endregion

        #region 异步读取操作

        /// <summary>
        /// 异步确保连接已打开
        /// </summary>
        private async Task EnsureConnectionOpenReadAsync(CancellationToken cancellationToken = default)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(DataContext));

            if (_connection == null)
                throw new InvalidOperationException("Connection is null. DataContext was not initialized properly.");

            if (_connection.State != ConnectionState.Open)
                await _connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// 异步获取列表
        /// </summary>
        public async Task<DataReturn<T>> GetListAsync<T>(DataQuery item, CancellationToken cancellationToken = default) where T : class, new()
        {
            var param = new List<DbParameter>();
            var result = new DataReturn<T>();
            var sql = new StringBuilder();
            object data;

            try
            {
                BuildBaseSelectQuery(item, param, sql);

                result.Sql = ParameterToSql.ObjectParamToSql(param, sql.ToString(), item.Config);

                if (param.Count != 0)
                    cmd.Parameters.AddRange(param.ToArray());

                AopBefore(item.Table, sql.ToString(), param, config, true, AopType.Query_List_Lambda);

                await EnsureConnectionOpenReadAsync(cancellationToken).ConfigureAwait(false);

                using (var dr = await BaseExecute.ToDataReaderAsync(cmd, sql.ToString()).ConfigureAwait(false))
                {
                    // Take(1) 只是「只查一条」的优化提示，不代表调用方要的是 Item。
                    // 原来该分支只填 Item、不填 List，导致 Take(1).ToList() / Take(1).ToJson()
                    // 拿到空集合（测试 ToJson_ShouldReturnJsonString / ToListAsync_ShouldReturnResults 复现）。
                    var items = BaseDataReader.ToList<T>(dr, item.Config, item.Field);
                    result.List = items;
                    result.Item = items.FirstOrDefault<T>();
                    data = item.Take == 1 ? (object)result.Item : (object)items;
                }

                AopAfter(item.Table, sql.ToString(), param, config, true, AopType.Query_List_Lambda, data);

                return result;
            }
            catch (Exception ex)
            {
                AopException(ex, "GetListAsync tableName:" + typeof(T).Name, config, AopType.Query_List_Lambda);

                if (config?.SqlErrorType?.ToLower() == SqlErrorType.Db)
                    DbLogTable.LogException<T>(config, ex, "GetListAsync<T>", "");
                else
                    DbLog.LogException<T>(item.Config.IsOutError, item.Config.DbType, ex, "GetListAsync<T>", result.Sql);
                return result;
            }
        }

        /// <summary>
        /// 异步执行SQL查询(实体)
        /// </summary>
        public async Task<DataReturn<T>> ExecuteSqlAsync<T>(string sql, DbParameter[] param = null, CancellationToken cancellationToken = default) where T : class, new()
        {
            var result = new DataReturn<T>();
            try
            {
                if (param != null)
                    result.Sql = ParameterToSql.ObjectParamToSql(param.ToList(), sql, config);
                else
                    result.Sql = sql;

                cmd.Parameters.Clear();

                if (param != null)
                    cmd.Parameters.AddRange(param.ToArray());

                AopBefore(null, sql.ToString(), param?.ToList(), config, true, AopType.Execute_Sql_Model);

                await EnsureConnectionOpenReadAsync(cancellationToken).ConfigureAwait(false);
                using (var dr = await BaseExecute.ToDataReaderAsync(cmd, sql).ConfigureAwait(false))
                {
                    result.List = BaseDataReader.ToList<T>(dr, config);
                }

                AopAfter(null, sql.ToString(), param?.ToList(), config, true, AopType.Execute_Sql_Model, result.List);
            }
            catch (Exception ex)
            {
                AopException(ex, "ExecuteSqlAsync tableName:" + typeof(T).Name, config, AopType.Execute_Sql_Model);

                if (config?.SqlErrorType?.ToLower() == SqlErrorType.Db)
                    DbLogTable.LogException<T>(config, ex, "ExecuteSqlAsync<T>", "");
                else
                    DbLog.LogException<T>(config.IsOutError, config.DbType, ex, "ExecuteSqlAsync<T>", result.Sql);
            }

            return result;
        }

        /// <summary>
        /// 异步执行SQL查询(字典)
        /// </summary>
        public async Task<DataReturn> ExecuteSqlListAsync(string sql, DbParameter[] param = null, bool isLog = false, bool isAop = true, CancellationToken cancellationToken = default)
        {
            var result = new DataReturn();
            try
            {
                if (param != null)
                    result.Sql = ParameterToSql.ObjectParamToSql(param.ToList(), sql, config);
                else
                    result.Sql = sql;

                DbLog.LogSql(isLog, result.Sql, config.DbType, 0);

                cmd.Parameters.Clear();

                if (param != null)
                    cmd.Parameters.AddRange(param.ToArray());

                if (isAop)
                    AopBefore(null, sql.ToString(), param?.ToList(), config, true, AopType.Execute_Sql_Dic);

                await EnsureConnectionOpenReadAsync(cancellationToken).ConfigureAwait(false);
                using (var dr = await BaseExecute.ToDataReaderAsync(cmd, sql).ConfigureAwait(false))
                {
                    result.DicList = BaseJson.DataReaderToDic(dr, config.DbType == DataDbType.Oracle);
                }
                result.WriteReturn.IsSuccess = true;

                if (isAop)
                    AopAfter(null, sql.ToString(), param?.ToList(), config, true, AopType.Execute_Sql_Dic, result.WriteReturn.IsSuccess);
            }
            catch (Exception ex)
            {
                AopException(ex, "ExecuteSqlListAsync", config, AopType.Execute_Sql_Dic);

                result.WriteReturn.IsSuccess = false;
                result.WriteReturn.Message = ex.Message;

                if (config?.SqlErrorType?.ToLower() == SqlErrorType.Db)
                    DbLogTable.LogException(config, ex, "ExecuteSqlListAsync", result.Sql);
                else
                    DbLog.LogException(config.IsOutError, config.DbType, ex, "ExecuteSqlListAsync", result.Sql);
            }

            return result;
        }

        /// <summary>
        /// 异步获取字典列表
        /// </summary>
        public async Task<DataReturn> GetDicAsync(DataQuery item, CancellationToken cancellationToken = default)
        {
            var param = new List<DbParameter>();
            var result = new DataReturn();
            var sql = new StringBuilder();
            object data;

            try
            {
                BuildBaseSelectQuery(item, param, sql);

                result.Sql = ParameterToSql.ObjectParamToSql(param, sql.ToString(), item.Config);

                cmd.Parameters.Clear();

                if (param.Count != 0)
                    cmd.Parameters.AddRange(param.ToArray());

                AopBefore(item.Table, sql.ToString(), param, config, true, AopType.Query_Dic_Lambda);

                await EnsureConnectionOpenReadAsync(cancellationToken).ConfigureAwait(false);
                using (var dr = await BaseExecute.ToDataReaderAsync(cmd, sql.ToString()).ConfigureAwait(false))
                {
                    // 同实体版：Take(1) 时也要填 DicList，否则 Take(1).ToList() 拿到空集合。
                    var dicList = BaseJson.DataReaderToDic(dr, config.DbType == DataDbType.Oracle);
                    result.DicList = dicList;
                    result.Dic = dicList.FirstOrDefault() ?? new Dictionary<string, object>();
                    data = item.Take == 1 ? (object)result.Dic : (object)dicList;
                }

                AopAfter(item.Table, sql.ToString(), param, config, true, AopType.Query_Dic_Lambda, data);

                return result;
            }
            catch (Exception ex)
            {
                AopException(ex, "GetDicAsync", config, AopType.Query_Dic_Lambda);

                if (config?.SqlErrorType?.ToLower() == SqlErrorType.Db)
                    DbLogTable.LogException(config, ex, "GetDicAsync", result.Sql);
                else
                    DbLog.LogException(item.Config.IsOutError, item.Config.DbType, ex, "GetDicAsync", result.Sql);
                return result;
            }
        }

        /// <summary>
        /// 异步获取DataTable
        /// </summary>
        public async Task<DataReturn> GetDataTableAsync(DataQuery item, CancellationToken cancellationToken = default)
        {
            var param = new List<DbParameter>();
            var result = new DataReturn();
            var sql = new StringBuilder();

            try
            {
                BuildBaseSelectQuery(item, param, sql);

                result.Sql = ParameterToSql.ObjectParamToSql(param, sql.ToString(), item.Config);

                cmd.Parameters.Clear();

                if (param.Count != 0)
                    cmd.Parameters.AddRange(param.ToArray());

                AopBefore(item.Table, sql.ToString(), param, config, true, AopType.Query_DataTable_Lambda);

                await EnsureConnectionOpenReadAsync(cancellationToken).ConfigureAwait(false);
                using (var dr = await BaseExecute.ToDataReaderAsync(cmd, sql.ToString()).ConfigureAwait(false))
                {
                    result.Table.Load(dr);
                }

                AopAfter(item.Table, sql.ToString(), param, config, true, AopType.Query_DataTable_Lambda, result.Table);

                return result;
            }
            catch (Exception ex)
            {
                AopException(ex, "GetDataTableAsync", config, AopType.Query_DataTable_Lambda);

                if (config?.SqlErrorType?.ToLower() == SqlErrorType.Db)
                    DbLogTable.LogException(config, ex, "GetDataTableAsync", result.Sql);
                else
                    DbLog.LogException(item.Config.IsOutError, item.Config.DbType, ex, "GetDataTableAsync", result.Sql);
                return result;
            }
        }

        /// <summary>
        /// 异步获取记录总数
        /// </summary>
        public async Task<DataReturn> GetCountAsync(DataQuery item, CancellationToken cancellationToken = default)
        {
            var sql = new StringBuilder();
            var result = new DataReturn();
            var param = new List<DbParameter>();

            try
            {
                sql.AppendFormat("select count(0) from {0}", item.Table[0]);

                for (var i = 1; i < item.Predicate.Count; i++)
                {
                    sql.AppendFormat(" {0} on {1}", item.Table[i], item.Predicate[i].Where);

                    if (item.Predicate[i].Param.Count != 0)
                        param.AddRange(item.Predicate[i].Param);
                }

                if (WhereBuilder.HasWhereClause(item))
                {
                    var whereClause = WhereBuilder.BuildWhereClause(item, ref param);
                    sql.AppendFormat(" where {0}", whereClause);
                }

                if (item.GroupBy.Count > 0)
                {
                    sql.Append(" group by ");
                    sql.Append(string.Join(",", item.GroupBy));
                }

                if (item.OrderBy.Count > 0)
                {
                    sql.Append(" order by ");
                    sql.Append(string.Join(",", item.OrderBy));
                }

                if (item.Predicate.Count > 0 && item.Predicate[0].Param.Count != 0)
                    param.AddRange(item.Predicate[0].Param);

                result.Sql = ParameterToSql.ObjectParamToSql(param, sql.ToString(), item.Config);

                cmd.Parameters.Clear();

                if (param.Count != 0)
                    cmd.Parameters.AddRange(param.ToArray());

                AopBefore(null, sql.ToString(), param?.ToList(), config, true, AopType.Query_Json_Lambda);

                await EnsureConnectionOpenReadAsync(cancellationToken).ConfigureAwait(false);
                var dt = await BaseExecute.ToDataTableAsync(cmd, sql.ToString()).ConfigureAwait(false);

                if (dt.Rows.Count > 0)
                    result.Count = dt.Rows[0][0].ToString().ToInt(0);
                else
                    result.Count = 0;

                AopAfter(null, sql.ToString(), param?.ToList(), config, true, AopType.Query_Json_Lambda, result.Count);

                return result;
            }
            catch (Exception ex)
            {
                AopException(ex, "GetCountAsync", config, AopType.Query_Json_Lambda);

                if (config?.SqlErrorType?.ToLower() == SqlErrorType.Db)
                    DbLogTable.LogException(config, ex, "GetCountAsync", result.Sql);
                else
                    DbLog.LogException(item.Config.IsOutError, item.Config.DbType, ex, "GetCountAsync", result.Sql);
                return result;
            }
        }

        /// <summary>
        /// 异步获取JSON
        /// </summary>
        public async Task<DataReturn> GetJsonAsync(DataQuery item, CancellationToken cancellationToken = default)
        {
            var param = new List<DbParameter>();
            var result = new DataReturn();
            var sql = new StringBuilder();

            try
            {
                BuildBaseSelectQuery(item, param, sql);

                result.Sql = ParameterToSql.ObjectParamToSql(param, sql.ToString(), item.Config);

                cmd.Parameters.Clear();

                if (param.Count != 0)
                    cmd.Parameters.AddRange(param.ToArray());

                AopBefore(null, sql.ToString(), param?.ToList(), config, true, AopType.Query_Json_Lambda);

                await EnsureConnectionOpenReadAsync(cancellationToken).ConfigureAwait(false);
                using (var dr = await BaseExecute.ToDataReaderAsync(cmd, sql.ToString()).ConfigureAwait(false))
                {
                    result.Json = BaseJson.DataReaderToJson(dr, config.DbType == DataDbType.Oracle);
                }

                AopAfter(null, sql.ToString(), param?.ToList(), config, true, AopType.Query_Json_Lambda, result.Json);

                return result;
            }
            catch (Exception ex)
            {
                AopException(ex, "GetJsonAsync", config, AopType.Query_Json_Lambda);

                if (config?.SqlErrorType?.ToLower() == SqlErrorType.Db)
                    DbLogTable.LogException(config, ex, "GetJsonAsync", result.Sql);
                else
                    DbLog.LogException(item.Config.IsOutError, item.Config.DbType, ex, "GetJsonAsync", result.Sql);
                return result;
            }
        }

        /// <summary>
        /// 异步获取分页(实体)
        /// </summary>
        public async Task<DataReturn<T>> GetPageAsync<T>(DataQuery item, PageModel pModel, CancellationToken cancellationToken = default) where T : class, new()
        {
            var param = new List<DbParameter>();
            var result = new DataReturn<T>();
            var sql = "";

            try
            {
                pModel.StarId = (pModel.PageId - 1) * pModel.PageSize + 1;
                pModel.EndId = pModel.PageId * pModel.PageSize;
                cmd.Parameters.Clear();
                await EnsureConnectionOpenReadAsync(cancellationToken).ConfigureAwait(false);
                var (count, countSql) = await PaginationSqlBuilder.CountAsync(item, cmd, cancellationToken).ConfigureAwait(false);
                pModel.TotalRecord = count;
                sql = countSql;

                if (pModel.TotalRecord > 0)
                {
                    if ((pModel.TotalRecord % pModel.PageSize) == 0)
                        pModel.TotalPage = pModel.TotalRecord / pModel.PageSize;
                    else
                        pModel.TotalPage = (pModel.TotalRecord / pModel.PageSize) + 1;

                    if (pModel.PageId > pModel.TotalPage)
                        pModel.PageId = pModel.TotalPage;

                    // 页码被夹取后必须重算边界，否则仍会按越界页码取数
                    pModel.StarId = (pModel.PageId - 1) * pModel.PageSize + 1;
                    pModel.EndId = pModel.PageId * pModel.PageSize;

                    AopBefore(item.Table, sql.ToString(), param, config, true, AopType.Query_Page_Lambda_Model);

                    cmd.Parameters.Clear();
                    var (dr, pageSql) = await PaginationSqlBuilder.ReadAsync(item, cmd, pModel, cancellationToken).ConfigureAwait(false);
                    using (dr)
                    {
                        if (dr != null)
                        {
                            result.PageResult.list = BaseDataReader.ToList<T>(dr, item.Config, item.AsName);
                        }
                    }
                    result.Sql = pageSql;
                    sql = pageSql;

                    AopAfter(item.Table, sql.ToString(), param, config, true, AopType.Query_Page_Lambda_Model, result.PageResult.list);
                }
                else
                    result.PageResult.list = new List<T>();

                // 把实际执行的 SQL 带回去：分页返回空列表时，仅看 list 无法区分
                // 「条件本来就无匹配」与「SQL 生成错误」，两者都表现为 0 行。
                result.PageResult.Sql = string.IsNullOrEmpty(result.Sql) ? sql : result.Sql;

                result.PageResult.pModel = pModel;
            }
            catch (Exception ex)
            {
                AopException(ex, "GetPageAsync tableName:" + typeof(T).Name, config, AopType.Query_Page_Lambda_Model);

                if (config?.SqlErrorType?.ToLower() == SqlErrorType.Db)
                    DbLogTable.LogException<T>(config, ex, "GetPageAsync<T>", "");
                else
                    DbLog.LogException<T>(item.Config.IsOutError, item.Config.DbType, ex, "GetPageAsync<T>", result.Sql);
            }

            return result;
        }

        /// <summary>
        /// 异步获取分页(字典)
        /// </summary>
        public async Task<DataReturn> GetPageAsync(DataQuery item, PageModel pModel, CancellationToken cancellationToken = default)
        {
            var param = new List<DbParameter>();
            var result = new DataReturn();
            var sql = "";

            try
            {
                pModel.StarId = (pModel.PageId - 1) * pModel.PageSize + 1;
                pModel.EndId = pModel.PageId * pModel.PageSize;
                cmd.Parameters.Clear();
                await EnsureConnectionOpenReadAsync(cancellationToken).ConfigureAwait(false);
                var (count, countSql) = await PaginationSqlBuilder.CountAsync(item, cmd, cancellationToken).ConfigureAwait(false);
                pModel.TotalRecord = count;
                sql = countSql;

                if (pModel.TotalRecord > 0)
                {
                    if ((pModel.TotalRecord % pModel.PageSize) == 0)
                        pModel.TotalPage = pModel.TotalRecord / pModel.PageSize;
                    else
                        pModel.TotalPage = (pModel.TotalRecord / pModel.PageSize) + 1;

                    if (pModel.PageId > pModel.TotalPage)
                        pModel.PageId = pModel.TotalPage;

                    // 页码被夹取后必须重算边界，否则仍会按越界页码取数
                    pModel.StarId = (pModel.PageId - 1) * pModel.PageSize + 1;
                    pModel.EndId = pModel.PageId * pModel.PageSize;

                    AopBefore(item.Table, sql.ToString(), param, config, true, AopType.Query_Page_Lambda_Dic);

                    cmd.Parameters.Clear();
                    var (dr, pageSql) = await PaginationSqlBuilder.ReadAsync(item, cmd, pModel, cancellationToken).ConfigureAwait(false);
                    using (dr)
                    {
                        if (dr != null)
                        {
                            result.PageResult.list = BaseJson.DataReaderToDic(dr, config.DbType == DataDbType.Oracle);
                        }
                    }
                    result.Sql = pageSql;

                    AopAfter(item.Table, sql.ToString(), param, config, true, AopType.Query_Page_Lambda_Dic, result.PageResult.list);
                }
                else
                    result.PageResult.list = new List<Dictionary<string, object>>();

                // 把实际执行的 SQL 带回去：分页返回空列表时，仅看 list 无法区分
                // 「条件本来就无匹配」与「SQL 生成错误」，两者都表现为 0 行。
                result.PageResult.Sql = string.IsNullOrEmpty(result.Sql) ? sql : result.Sql;

                result.PageResult.pModel = pModel;
            }
            catch (Exception ex)
            {
                AopException(ex, "GetPageAsync", config, AopType.Query_Page_Lambda_Dic);

                if (string.Equals(item.Config?.SqlErrorType, SqlErrorType.Db, StringComparison.OrdinalIgnoreCase))
                    DbLogTable.LogException(item.Config, ex, "GetPageAsync", result.Sql);
                else
                    DbLog.LogException(item.Config?.IsOutError ?? false, item.Config?.DbType ?? DataDbType.SqlServer, ex, "GetPageAsync", result.Sql);
            }

            return result;
        }

        /// <summary>
        /// 异步获取分页总记录数
        /// </summary>
        /// <param name="item">数据查询对象</param>
        /// <param name="cmd">数据库命令对象</param>
        /// <param name="sql">原始 SQL 语句</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>总记录数和生成的 COUNT SQL</returns>
        private Task<(int count, string sql)> ToPageCountAsync(DataQuery item, DbCommand cmd, string sql, CancellationToken cancellationToken = default)
        {
            // 统一委托给 PaginationSqlBuilder，避免同步/异步两套分页逻辑产生偏差
            return PaginationSqlBuilder.CountAsync(item, cmd, cancellationToken);
        }

        /// <summary>
        /// 异步获取分页DataReader
        /// </summary>
        private async Task<(DbDataReader reader, string sql)> ToPageDataReaderAsync(DataQuery item, DbCommand cmd, PageModel pModel, string sql, CancellationToken cancellationToken = default)
        {
            try
            {
                var table = new StringBuilder();
                var sb = new StringBuilder();
                var param = new List<DbParameter>();

                table.Append(item.Table[0]);
                for (var i = 1; i < item.Predicate.Count; i++)
                {
                    table.AppendFormat(" {0} on {1}", item.Table[i], item.Predicate[i].Where);

                    if (item.Predicate[i].Param.Count != 0)
                        param.AddRange(item.Predicate[i].Param);
                }

                if (item.Predicate[0].Param.Count != 0)
                    param.AddRange(item.Predicate[0].Param);

                if (item.Config.DbType == DataDbType.SqlServer)
                {
                    var orderByLenght = item.Predicate[0].Where.IndexOf("order by");
                    sb.AppendFormat(@"select top {0} * from (select row_number()over({5})temprownumber,* 
                                        from (select tempcolumn=0,{3} from {1} where {4})t)tt where temprownumber>={2}"
                                            , pModel.PageSize
                                            , table
                                            , pModel.StarId - 1
                                            , string.Join(",", item.Field)
                                            , orderByLenght == -1 ? item.Predicate[0].Where : item.Predicate[0].Where.Substring(0, orderByLenght)
                                            , orderByLenght == -1 ? "order by tempcolumn" : item.Predicate[0].Where.Substring(orderByLenght, item.Predicate[0].Where.Length - orderByLenght));
                }
                else if (item.Config.DbType == DataDbType.Oracle)
                {
                    sb = new StringBuilder();
                    if (item.Predicate.Count > 0)
                    {
                        sb.AppendFormat("select * from(select field.*,ROWNUM RN from(select {0} from {1} where {2}) field where rownum<={3}) where rn>={4}"
                                        , string.Join(",", item.Field)
                                        , table
                                        , item.Predicate[0].Where
                                        , pModel.EndId
                                        , pModel.StarId);
                    }
                    else
                    {
                        sb.AppendFormat(@"select * from {3} 
                                    where rowid in(select rid from 
                                    (select rownum rn,rid from 
                                    (select rowid rid from {3}) 
                                    where rownum<={0}) where rn>{1}) and {4}"
                                        , pModel.EndId
                                        , (pModel.StarId - 1)
                                        , string.Join(",", item.Field)
                                        , table
                                        , item.Predicate[0].Where);
                    }
                }
                else if (item.Config.DbType == DataDbType.MySql)
                {
                    sb.AppendFormat("select {2} from {3} where {4} limit {0}, {1}"
                                       , pModel.StarId
                                       , pModel.PageSize
                                       , string.Join(",", item.Field)
                                       , table
                                       , item.Predicate[0].Where);
                }
                else if (item.Config.DbType == DataDbType.DB2)
                {
                    var orderByLenght = item.Predicate[0].Where.IndexOf("order by");
                    sb.AppendFormat("select * from (select row_number() over ({5}) as row_number,{2} from {3} where {4}) a where a.row_number>{0} and row_number<{1} "
                                       , pModel.StarId
                                       , pModel.EndId
                                       , string.Join(",", item.Field)
                                       , table
                                       , item.Predicate[0].Where == "" ? "" : item.Predicate[0].Where
                                       , orderByLenght == -1 ? "" : item.Predicate[0].Where.Substring(orderByLenght, item.Predicate[0].Where.Length - orderByLenght));
                }
                else if (item.Config.DbType == DataDbType.SQLite)
                {
                    sb.AppendFormat("select {2} from {3} where {4} limit {1} offset {0}"
                                       , pModel.StarId
                                       , pModel.PageSize
                                       , string.Join(",", item.Field)
                                       , table
                                       , item.Predicate[0].Where);
                }
                else if (item.Config.DbType == DataDbType.PostgreSql)
                {
                    sb.AppendFormat("select {2} from {3} where {4} limit {1} offset {0}"
                                       , pModel.StarId
                                       , pModel.PageSize
                                       , string.Join(",", item.Field)
                                       , table
                                       , item.Predicate[0].Where);
                }

                if (param.Count != 0)
                    cmd.Parameters.AddRange(param.ToArray());
                cmd.CommandText = sb.ToString();
                sql = string.Format("count:{0},page:{1}", sql, ParameterToSql.ObjectParamToSql(param, sb.ToString(), item.Config));
                var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                return (reader, sql);
            }
            catch (Exception ex)
            {
                if (string.Equals(item.Config?.SqlErrorType, SqlErrorType.Db, StringComparison.OrdinalIgnoreCase))
                    DbLogTable.LogException(item.Config, ex, "ToPageDataReaderAsync", "");
                else
                    DbLog.LogException(true, item.Config?.DbType ?? DataDbType.SqlServer, ex, "ToPageDataReaderAsync", "");
                throw;
            }
        }

        #endregion
    }
}
