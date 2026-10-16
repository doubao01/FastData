using System;
using System.Text.RegularExpressions;

namespace FastData.Adapter
{
    /// <summary>
    /// SQL Server 方言实现
    /// 
    /// 处理 SQL Server 特有的 SQL 语法。
    /// </summary>
    internal class SqlServerDialect : ISqlDialect
    {
        private static readonly Regex DistinctPattern =
            new Regex(@"^\s+distinct\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private static readonly Regex TopPattern =
            new Regex(@"^\s+top\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        /// <summary>
        /// 应用 TOP 子句
        /// </summary>
        /// <param name="sql">原始 SQL 语句</param>
        /// <param name="take">获取的记录数</param>
        /// <returns>添加了 TOP 的 SQL 语句</returns>
        public string ApplyTake(string sql, int take)
        {
            if (take <= 0 || string.IsNullOrEmpty(sql))
                return sql;

            var trimmed = sql.TrimStart();
            if (!trimmed.StartsWith("select", StringComparison.OrdinalIgnoreCase))
                return sql;

            var afterSelect = trimmed.Substring(6);
            if (afterSelect.Length == 0 || !char.IsWhiteSpace(afterSelect[0]))
                return sql;

            var prefix = sql.Substring(0, sql.Length - trimmed.Length) + "select";

            var body = afterSelect;
            var distinctPart = string.Empty;
            var distinctMatch = DistinctPattern.Match(body);
            if (distinctMatch.Success)
            {
                // SELECT DISTINCT 时 TOP 必须位于 DISTINCT 之后
                distinctPart = distinctMatch.Value;
                body = body.Substring(distinctMatch.Length);
            }

            if (TopPattern.IsMatch(body))
                return sql;

            return prefix + " top " + take + distinctPart + body;
        }

        /// <summary>
        /// 构建参数名称（使用 @ 前缀）
        /// </summary>
        /// <param name="name">参数名（不含前缀）</param>
        /// <returns>带 @ 前缀的参数名</returns>
        public string BuildParameterName(string name)
        {
            return "@" + name.TrimStart('@', ':', '?');
        }
    }
}
