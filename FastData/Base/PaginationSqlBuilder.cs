using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FastData.DbTypes;
using FastData.Model;
using FastUntility.Page;

namespace FastData.Base
{
    /// <summary>
    /// 分页 SQL 生成器
    /// <para>
    /// 统一同步与异步分页的 SQL 构造，避免两套实现产生偏移量、WHERE 条件不一致的问题。
    /// 页码从 1 开始，StarId 为当前页第一行（1 基），EndId 为当前页最后一行。
    /// </para>
    /// </summary>
    internal static class PaginationSqlBuilder
    {
        /// <summary>
        /// 生成分页查询 SQL，并填充查询参数
        /// </summary>
        public static string Build(DataQuery item, PageModel pModel, List<DbParameter> param)
        {
            if (item == null) throw new ArgumentNullException(nameof(item));
            if (pModel == null) throw new ArgumentNullException(nameof(pModel));
            if (param == null) throw new ArgumentNullException(nameof(param));

            var table = BuildTable(item, param);
            var where = WhereBuilder.HasWhereClause(item)
                ? WhereBuilder.BuildWhereClause(item, ref param)
                : string.Empty;
            var whereFragment = string.IsNullOrEmpty(where) ? string.Empty : " where " + where;

            var orderBy = item.OrderBy != null && item.OrderBy.Count > 0
                ? string.Join(",", item.OrderBy)
                : string.Empty;
            var orderFragment = string.IsNullOrEmpty(orderBy) ? string.Empty : " order by " + orderBy;

            var fields = string.Join(",", item.Field);
            var pageSize = pModel.PageSize;
            var startId = pModel.StarId;
            var endId = pModel.EndId;

            switch (item.Config.DbType)
            {
                case DataDbType.SqlServer:
                {
                    var rowNumberOrder = string.IsNullOrEmpty(orderBy) ? "order by tempcolumn" : "order by " + orderBy;
                    return string.Format(
                        "select top {0} * from (select row_number() over({1}) temprownumber,* from (select tempcolumn=0,{2} from {3}{4})t)tt where temprownumber>={5}",
                        pageSize, rowNumberOrder, fields, table, whereFragment, startId);
                }

                case DataDbType.Oracle:
                {
                    return string.Format(
                        "select * from (select field.*,ROWNUM RN from (select {0} from {1}{2}{3}) field where rownum<={4}) where rn>={5}",
                        fields, table, whereFragment, orderFragment, endId, startId);
                }

                case DataDbType.MySql:
                {
                    return string.Format(
                        "select {0} from {1}{2}{3} limit {4}, {5}",
                        fields, table, whereFragment, orderFragment, startId - 1, pageSize);
                }

                case DataDbType.DB2:
                {
                    var rowNumberOrder = string.IsNullOrEmpty(orderBy) ? "order by 1" : "order by " + orderBy;
                    return string.Format(
                        "select * from (select row_number() over ({0}) as row_number,{1} from {2}{3}) a where a.row_number>={4} and a.row_number<={5}",
                        rowNumberOrder, fields, table, whereFragment, startId, endId);
                }

                case DataDbType.SQLite:
                case DataDbType.PostgreSql:
                {
                    return string.Format(
                        "select {0} from {1}{2}{3} limit {4} offset {5}",
                        fields, table, whereFragment, orderFragment, pageSize, startId - 1);
                }

                default:
                    throw new NotSupportedException(string.Format("不支持的分页数据库类型: {0}", item.Config.DbType));
            }
        }

        /// <summary>
        /// 执行分页总数查询
        /// </summary>
        public static int Count(DataQuery item, DbCommand cmd, ref string sql)
        {
            var param = new List<DbParameter>();
            var countSql = BuildCountSql(item, param);

            cmd.Parameters.Clear();
            if (param.Count > 0)
                cmd.Parameters.AddRange(param.ToArray());

            sql = ParameterToSql.ObjectParamToSql(param, countSql, item.Config);
            var dt = BaseExecute.ToDataTable(cmd, countSql);
            return Convert.ToInt32(dt.Rows[0][0]);
        }

        /// <summary>
        /// 异步执行分页总数查询
        /// </summary>
        public static async Task<(int count, string sql)> CountAsync(DataQuery item, DbCommand cmd, CancellationToken cancellationToken = default)
        {
            var param = new List<DbParameter>();
            var countSql = BuildCountSql(item, param);

            cmd.Parameters.Clear();
            if (param.Count > 0)
                cmd.Parameters.AddRange(param.ToArray());

            var displaySql = ParameterToSql.ObjectParamToSql(param, countSql, item.Config);
            var dt = await BaseExecute.ToDataTableAsync(cmd, countSql).ConfigureAwait(false);
            return (Convert.ToInt32(dt.Rows[0][0]), displaySql);
        }

        /// <summary>
        /// 执行分页数据查询
        /// </summary>
        public static DbDataReader Read(DataQuery item, DbCommand cmd, PageModel pModel, ref string sql)
        {
            var param = new List<DbParameter>();
            var pageSql = Build(item, pModel, param);

            cmd.Parameters.Clear();
            if (param.Count > 0)
                cmd.Parameters.AddRange(param.ToArray());

            cmd.CommandText = pageSql;
            sql = string.Format("count:{0},page:{1}", sql, ParameterToSql.ObjectParamToSql(param, pageSql, item.Config));
            return cmd.ExecuteReader();
        }

        /// <summary>
        /// 异步执行分页数据查询
        /// </summary>
        public static async Task<(DbDataReader reader, string sql)> ReadAsync(
            DataQuery item, DbCommand cmd, PageModel pModel, CancellationToken cancellationToken = default)
        {
            var param = new List<DbParameter>();
            var pageSql = Build(item, pModel, param);

            cmd.Parameters.Clear();
            if (param.Count > 0)
                cmd.Parameters.AddRange(param.ToArray());

            cmd.CommandText = pageSql;
            var displaySql = ParameterToSql.ObjectParamToSql(param, pageSql, item.Config);
            var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            return (reader, displaySql);
        }

        private static string BuildCountSql(DataQuery item, List<DbParameter> param)
        {
            var table = BuildTable(item, param);
            var where = WhereBuilder.HasWhereClause(item)
                ? WhereBuilder.BuildWhereClause(item, ref param)
                : string.Empty;
            var whereFragment = string.IsNullOrEmpty(where) ? string.Empty : " where " + where;

            if (item.GroupBy != null && item.GroupBy.Count > 0)
            {
                var groupSql = string.Format("select {0} from {1}{2} group by {3}",
                    string.Join(",", item.GroupBy), table, whereFragment, string.Join(",", item.GroupBy));
                return string.Format("select count(0) from ({0})t", groupSql);
            }

            return string.Format("select count(0) from {0}{1}", table, whereFragment);
        }

        private static string BuildTable(DataQuery item, List<DbParameter> param)
        {
            var table = new StringBuilder();
            table.Append(item.Table[0]);

            for (var i = 1; i < item.Table.Count; i++)
            {
                if (i < item.Predicate.Count)
                {
                    table.AppendFormat(" {0} on {1}", item.Table[i], item.Predicate[i].Where);

                    if (item.Predicate[i].Param != null && item.Predicate[i].Param.Count > 0)
                        param.AddRange(item.Predicate[i].Param);
                }
                else
                {
                    table.Append(' ').Append(item.Table[i]);
                }
            }

            return table.ToString();
        }
    }
}
