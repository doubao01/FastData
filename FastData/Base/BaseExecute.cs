using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Data;
using System.Data.Common;
using FastUntility.Page;
using FastData.DbTypes;
using FastData.Model;
using System.Linq.Expressions;
using FastData.Property;

namespace FastData.Base
{
    /// <summary>
    /// 执行数据库操作
    /// </summary>
    internal static class BaseExecute
    {
        #region 返回DataTable
        /// <summary>
        ///  返回DataTable
        /// </summary>
        /// <param name="cmd">数据库命令对象</param>
        /// <param name="sql">SQL语句</param>
        /// <param name="IsProcedure">是否存储过程</param>
        /// <returns>数据表</returns>
        public static DataTable ToDataTable(DbCommand cmd, string sql, bool IsProcedure = false)
        {
            var dt = new DataTable();

            using (var dr = ToDataReader(cmd, sql, IsProcedure))
            {
                dt.Load(dr);
            }

            return dt;
        }

        /// <summary>
        ///  异步返回DataTable（真正的I/O异步）
        /// </summary>
        /// <param name="cmd">数据库命令对象</param>
        /// <param name="sql">SQL语句</param>
        /// <param name="IsProcedure">是否存储过程</param>
        /// <returns>数据表任务</returns>
        public static async Task<DataTable> ToDataTableAsync(DbCommand cmd, string sql, bool IsProcedure = false)
        {
            var dt = new DataTable();

            using (var dr = await ToDataReaderAsync(cmd, sql, IsProcedure).ConfigureAwait(false))
            {
                dt.Load(dr);
            }

            return dt;
        }
        #endregion

        #region 返回DataReader
        /// <summary>
        ///  返回DataReader
        /// </summary>
        /// <param name="cmd">数据库命令对象</param>
        /// <param name="sql">SQL语句</param>
        /// <param name="IsProcedure">是否存储过程</param>
        /// <returns>数据读取器</returns>
        public static DbDataReader ToDataReader(DbCommand cmd, string sql, bool IsProcedure = false)
        {
            if (IsProcedure)
                cmd.CommandType = CommandType.StoredProcedure;
            cmd.CommandText = sql;
            return cmd.ExecuteReader();
        }

        /// <summary>
        ///  异步返回DataReader（真正的I/O异步）
        /// </summary>
        /// <param name="cmd">数据库命令对象</param>
        /// <param name="sql">SQL语句</param>
        /// <param name="IsProcedure">是否存储过程</param>
        /// <returns>数据读取器任务</returns>
        public static async Task<DbDataReader> ToDataReaderAsync(DbCommand cmd, string sql, bool IsProcedure = false)
        {
            if (IsProcedure)
                cmd.CommandType = CommandType.StoredProcedure;
            cmd.CommandText = sql;
            return await cmd.ExecuteReaderAsync().ConfigureAwait(false);
        }
        #endregion

        #region 返回bool
        /// <summary>
        ///  返回bool
        /// </summary>
        /// <param name="cmd">数据库命令对象</param>
        /// <param name="sql">SQL语句</param>
        /// <param name="IsProcedure">是否存储过程</param>
        /// <returns>布尔值</returns>
        public static bool ToBool(DbCommand cmd, string sql, bool IsProcedure = false)
        {
            cmd.CommandText = sql;

            if (IsProcedure)
                cmd.CommandType = CommandType.StoredProcedure;

            return cmd.ExecuteNonQuery() > 0;
        }

        /// <summary>
        /// 获取 INSERT 操作后生成的自增主键值（跨数据库兼容）
        /// </summary>
        /// <param name="dbType">数据库类型</param>
        /// <param name="cmd">数据库命令对象（需与 INSERT 在同一连接上执行）</param>
        /// <returns>自增主键值，失败返回 0</returns>
        public static long GetIdentityValue(DataDbType dbType, DbCommand cmd)
        {
            try
            {
                cmd.Parameters.Clear();
                cmd.CommandType = CommandType.Text;
                // 根据数据库类型生成对应的身份查询 SQL
                cmd.CommandText = dbType switch
                {
                    DataDbType.SqlServer => "SELECT CAST(SCOPE_IDENTITY() AS BIGINT)",
                    DataDbType.MySql => "SELECT LAST_INSERT_ID()",
                    DataDbType.SQLite => "SELECT last_insert_rowid()",
                    DataDbType.PostgreSql => "SELECT lastval()",
                    DataDbType.Oracle => "SELECT last_number FROM user_sequences WHERE sequence_name = 'SEQ_ID'",
                    _ => "SELECT CAST(SCOPE_IDENTITY() AS BIGINT)"
                };
                var result = cmd.ExecuteScalar();
                if (result != null && result != DBNull.Value)
                    return Convert.ToInt64(result);
            }
            catch (Exception ex)
            {
                // 记录到调试日志，但不影响主流程
                DbLog.LogException(false, DataDbType.SqlServer, ex, "GetIdentityValue", "");
            }
            return 0L;
        }

        /// <summary>
        ///  异步返回bool（真正的I/O异步）
        /// </summary>
        /// <param name="cmd">数据库命令对象</param>
        /// <param name="sql">SQL语句</param>
        /// <param name="IsProcedure">是否存储过程</param>
        /// <returns>布尔值任务</returns>
        public static async Task<bool> ToBoolAsync(DbCommand cmd, string sql, bool IsProcedure = false)
        {
            cmd.CommandText = sql;

            if (IsProcedure)
                cmd.CommandType = CommandType.StoredProcedure;

            return await cmd.ExecuteNonQueryAsync().ConfigureAwait(false) > 0;
        }

        /// <summary>
        ///  异步执行标量查询（真正的I/O异步）
        /// </summary>
        /// <param name="cmd">数据库命令对象</param>
        /// <param name="sql">SQL语句</param>
        /// <param name="IsProcedure">是否存储过程</param>
        /// <returns>标量结果任务</returns>
        public static async Task<object> ExecuteScalarAsync(DbCommand cmd, string sql, bool IsProcedure = false)
        {
            cmd.CommandText = sql;

            if (IsProcedure)
                cmd.CommandType = CommandType.StoredProcedure;

            return await cmd.ExecuteScalarAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// 异步获取 INSERT 操作后生成的自增主键值（跨数据库兼容）
        /// </summary>
        /// <param name="dbType">数据库类型</param>
        /// <param name="cmd">数据库命令对象</param>
        /// <returns>自增主键值任务</returns>
        public static async Task<long> GetIdentityValueAsync(DataDbType dbType, DbCommand cmd)
        {
            try
            {
                cmd.Parameters.Clear();
                cmd.CommandType = CommandType.Text;
                cmd.CommandText = dbType switch
                {
                    DataDbType.SqlServer => "SELECT CAST(SCOPE_IDENTITY() AS BIGINT)",
                    DataDbType.MySql => "SELECT LAST_INSERT_ID()",
                    DataDbType.SQLite => "SELECT last_insert_rowid()",
                    DataDbType.PostgreSql => "SELECT lastval()",
                    DataDbType.Oracle => "SELECT last_number FROM user_sequences WHERE sequence_name = 'SEQ_ID'",
                    _ => "SELECT CAST(SCOPE_IDENTITY() AS BIGINT)"
                };
                var result = await ExecuteScalarAsync(cmd, cmd.CommandText).ConfigureAwait(false);
                if (result != null && result != DBNull.Value)
                    return Convert.ToInt64(result);
            }
            catch (Exception ex)
            {
                DbLog.LogException(false, DataDbType.SqlServer, ex, "GetIdentityValueAsync", "");
            }
            return 0L;
        }
        #endregion
                
        #region 返回分页DataReader
        /// <summary>
        /// 返回分页DataReader
        /// </summary>
        /// <param name="item">数据查询对象</param>
        /// <param name="cmd">数据库命令对象</param>
        /// <param name="pModel">分页模型</param>
        /// <param name="sql">SQL语句</param>
        /// <returns>数据读取器</returns>
        public static DbDataReader ToPageDataReader(DataQuery item, DbCommand cmd, PageModel pModel, ref string sql)
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
                    #region sqlserver
                    var orderByLenght = item.Predicate[0].Where.IndexOf("order by");
                    sb.AppendFormat(@"select top {0} * from (select row_number()over({5})temprownumber,* 
                                        from (select tempcolumn=0,{3} from {1} where {4})t)tt where temprownumber>={2}"
                                            , pModel.PageSize
                                            , table
                                            , pModel.StarId - 1
                                            , string.Join(",", item.Field)
                                            , orderByLenght == -1 ? item.Predicate[0].Where : item.Predicate[0].Where.Substring(0, orderByLenght)
                                            , orderByLenght == -1 ? "order by tempcolumn" : item.Predicate[0].Where.Substring(orderByLenght, item.Predicate[0].Where.Length - orderByLenght));
                    #endregion
                }
                else if (item.Config.DbType == DataDbType.Oracle)
                {
                    #region oracle
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
                    #endregion
                }
                else if (item.Config.DbType == DataDbType.MySql)
                {
                    #region MySql
                    sb.AppendFormat("select {2} from {3} where {4} limit {0}, {1}"
                                       , pModel.StarId - 1
                                       , pModel.PageSize
                                       , string.Join(",", item.Field)
                                       , table
                                       , item.Predicate[0].Where);
                    #endregion
                }
                else if (item.Config.DbType == DataDbType.DB2)
                {
                    #region DB2
                    var orderByLenght = item.Predicate[0].Where.IndexOf("order by");
                    sb.AppendFormat("select * from (select row_number() over ({5}) as row_number,{2} from {3} where {4}) a where a.row_number>{0} and row_number<{1} "
                                       , pModel.StarId
                                       , pModel.EndId
                                       , string.Join(",", item.Field)
                                       , table
                                       , item.Predicate[0].Where == "" ? "" : item.Predicate[0].Where
                                       , orderByLenght == -1 ? "" : item.Predicate[0].Where.Substring(orderByLenght, item.Predicate[0].Where.Length - orderByLenght));
                    #endregion
                }
                else if (item.Config.DbType == DataDbType.SQLite)
                {
                    #region sqlite
                    sb.AppendFormat("select {2} from {3} where {4} limit {1} offset {0}"
                                       , pModel.StarId - 1
                                       , pModel.PageSize
                                       , string.Join(",", item.Field)
                                       , table
                                       , item.Predicate[0].Where);
                    #endregion
                }
                else if (item.Config.DbType == DataDbType.PostgreSql)
                {
                    #region PostgreSql
                    sb.AppendFormat("select {2} from {3} where {4} limit {1} offset {0}"
                                       , pModel.StarId - 1
                                       , pModel.PageSize
                                       , string.Join(",", item.Field)
                                       , table
                                       , item.Predicate[0].Where);
                    #endregion
                }

                if (param.Count != 0)
                    cmd.Parameters.AddRange(param.ToArray());
                cmd.CommandText = sb.ToString();
                sql = string.Format("count:{0},page:{1}", sql, ParameterToSql.ObjectParamToSql(param, sb.ToString(), item.Config));
                return cmd.ExecuteReader();
            }
            catch (Exception ex)
            {
                if (string.Equals(item.Config?.SqlErrorType, SqlErrorType.Db, StringComparison.OrdinalIgnoreCase))
                    DbLogTable.LogException(item.Config, ex, "ToPageDataReader", "");
                else
                    DbLog.LogException(true, item.Config?.DbType ?? DataDbType.SqlServer, ex, "ToPageDataReader", "");
                throw;
            }
        }
        #endregion
        
        #region 返回分页条数
        /// <summary>
        /// 返回分页条数
        /// </summary>
        /// <param name="item">数据查询对象</param>
        /// <param name="cmd">数据库命令对象</param>
        /// <param name="sql">SQL语句</param>
        /// <returns>记录总数</returns>
        public static int ToPageCount(DataQuery item, DbCommand cmd,  ref string sql)
        {
            try
            {
                var param = new List<DbParameter>();
                sql = string.Format("select count(0) from {0}", item.Table[0]);

                for (var i = 1; i < item.Predicate.Count; i++)
                {
                    sql = string.Format("{2} {0} on {1}", item.Table[i], item.Predicate[i].Where, sql);

                    if (item.Predicate[i].Param.Count != 0)
                        param.AddRange(item.Predicate[i].Param);
                }

                if (!string.IsNullOrEmpty(item.Predicate[0].Where))
                    sql = string.Format("{1} where {0}", item.Predicate[0].Where, sql);

                if (item.Predicate[0].Param.Count != 0)
                    param.AddRange(item.Predicate[0].Param);

                if (param.Count != 0)
                    cmd.Parameters.AddRange(param.ToArray());

                var dt = BaseExecute.ToDataTable(cmd, sql.ToString());

                return int.Parse(dt.Rows[0][0].ToString());
            }
            catch (Exception ex)
            {
                if (string.Equals(item.Config?.SqlErrorType, SqlErrorType.Db, StringComparison.OrdinalIgnoreCase))
                    DbLogTable.LogException(item.Config, ex, "ToPageCount", "");
                else
                    DbLog.LogException(true, item.Config?.DbType ?? DataDbType.SqlServer, ex, "ToPageCount", "");
                throw;
            }
        }
        #endregion

        #region 返回分页条数sql
        /// <summary>
        /// 返回分页条数SQL
        /// </summary>
        /// <param name="param">SQL 参数数组</param>
        /// <param name="cmd">数据库命令对象</param>
        /// <param name="sql">原始 SQL 语句</param>
        /// <param name="config">数据库配置模型</param>
        /// <param name="tempSql">输出参数：转换后的参数化 SQL（用于日志或调试）</param>
        /// <returns>分页总记录数</returns>
        public static int ToPageCountSql(DbParameter[] param, DbCommand cmd, string sql, ConfigModel config, ref string tempSql)
        {
            try
            {
                sql = string.Format("select count(0) from ({0})t", sql);

                tempSql = ParameterToSql.ObjectParamToSql(param.ToList(), sql, config);

                if (param != null)
                    cmd.Parameters.AddRange(param.ToArray());

                var dt = BaseExecute.ToDataTable(cmd, sql.ToString());

                return int.Parse(dt.Rows[0][0].ToString());
            }
            catch (Exception ex)
            {
                if (string.Equals(config?.SqlErrorType, SqlErrorType.Db, StringComparison.OrdinalIgnoreCase))
                    DbLogTable.LogException(config, ex, "ToPageCountSql", "");
                else
                    DbLog.LogException(config?.IsOutError ?? false, config?.DbType ?? DataDbType.SqlServer, ex, "ToPageCountSql", "");
                throw;
            }
        }
        #endregion

        #region 返回分页DataReader sql
        /// <summary>
        /// 返回分页DataReader sql
        /// </summary>
        /// <param name="param">SQL 参数数组</param>
        /// <param name="cmd">数据库命令对象</param>
        /// <param name="pModel">分页模型（页码、每页大小）</param>
        /// <param name="sql">原始 SQL 语句</param>
        /// <param name="config">数据库配置模型</param>
        /// <param name="tempSql">输出参数：转换后的参数化 SQL（用于日志或调试）</param>
        /// <returns>分页数据读取器；出错返回 null</returns>
        public static DbDataReader ToPageDataReaderSql(DbParameter[] param, DbCommand cmd, PageModel pModel, string sql, ConfigModel config, ref string tempSql)
        {
            try
            {
                if (config.DbType == DataDbType.Oracle)
                    sql = string.Format("select * from(select field.*,ROWNUM RN from({0}) field where rownum<={1}) where rn>={2}"
                                    , sql, pModel.EndId, pModel.StarId);

                if (config.DbType == DataDbType.SqlServer)
                    sql = string.Format(@"select top {1} * from (select row_number()over(order by tempcolumn)temprownumber,* 
                                         from(select tempcolumn = 0, * from ({0})t)tt)ttt where temprownumber >= {2}"
                                    , sql, pModel.PageSize, pModel.StarId - 1);

                if (config.DbType == DataDbType.MySql)
                    sql = string.Format("{0} limit {1},{2}", sql, pModel.StarId, pModel.PageSize);

                if (config.DbType == DataDbType.DB2)
                    sql = string.Format("select * from(select field.*,ROWNUM RN from({0}) field where rownum<={1}) where rn>={2}"
                                    , sql, pModel.EndId, pModel.StarId);

                if (config.DbType == DataDbType.PostgreSql)
                    sql = string.Format("{0} limit {1} offset {2}", sql, pModel.PageSize, pModel.StarId);

                if (config.DbType == DataDbType.SQLite)
                    sql = string.Format("{0} limit {1} offset {2}", sql, pModel.PageSize, pModel.StarId);

                tempSql = ParameterToSql.ObjectParamToSql(param.ToList(), sql, config);

                if (param != null)
                    cmd.Parameters.AddRange(param.ToArray());

                cmd.CommandText = sql;

                return cmd.ExecuteReader();
            }
            catch (Exception ex)
            {
                if (string.Equals(config?.SqlErrorType, SqlErrorType.Db, StringComparison.OrdinalIgnoreCase))
                    DbLogTable.LogException(config, ex, "ToPageDataReaderSql", "");
                else
                    DbLog.LogException(config?.IsOutError ?? false, config?.DbType ?? DataDbType.SqlServer, ex, "ToPageDataReaderSql", "");
                throw;
            }
        }
        #endregion

        #region 获取表结构
        /// <summary>
        /// 获取表结构
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="cmd"></param>
        /// <returns></returns>
        public static DataTable ToDataTable<T>(DbCommand cmd, ConfigModel config, List<string> where, Expression<Func<T, object>> field = null) where T : class
        {
            var dt = new DataTable();
            var sql = new List<string>();

            if (field == null)
            {
                var properties = PropertyCache.GetPropertiesCached<T>();
                foreach (var a in properties)
                    sql.Add(a.Name);
            }
            else
                foreach (var name in BaseModel.GetSelectedMemberNames(field))
                    sql.Add(name);

            where.ForEach(a => { sql.Add(a); });

            cmd.CommandText = string.Format("select {1} from {0} where 1=0", TableNameHelper.GetTableName<T>(), string.Join(",", sql.ToArray()));

            var dr = cmd.ExecuteReader();
            dt.Load(dr);
            dr.Close();
            dr.Dispose();
            return dt;
        }
        #endregion


    }
}
