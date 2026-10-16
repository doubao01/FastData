using System;
using FastData.Context;
using System.Collections.Generic;
using System.Linq;

namespace FastData.DevTools
{
    /// <summary>
    /// SQL查询构建器
    /// </summary>
    public class SqlQueryBuilder
    {
        private string _select = "*";
        private string _from;
        private readonly List<string> _joins = new List<string>();
        private readonly List<string> _wheres = new List<string>();
        private readonly List<string> _groupBys = new List<string>();
        private readonly List<string> _orderBys = new List<string>();
        private int? _skip;
        private int? _take;
        private readonly List<object> _parameters = new List<object>();

        /// <summary>
        /// 设置查询表
        /// </summary>
        /// <param name="tableName">表名</param>
        /// <returns>构建器本身</returns>
        public SqlQueryBuilder From(string tableName)
        {
            _from = tableName;
            return this;
        }

        /// <summary>
        /// 设置查询列
        /// </summary>
        /// <param name="columns">列表达式</param>
        /// <returns>构建器本身</returns>
        public SqlQueryBuilder Select(string columns)
        {
            _select = columns;
            return this;
        }

        /// <summary>
        /// 添加连接
        /// </summary>
        /// <param name="joinTable">连接的表</param>
        /// <param name="onCondition">连接条件</param>
        /// <param name="joinType">连接类型</param>
        /// <returns>构建器本身</returns>
        public SqlQueryBuilder Join(string joinTable, string onCondition, string joinType = "INNER")
        {
            _joins.Add(string.Format("{0} JOIN {1} ON {2}", joinType, joinTable, onCondition));
            return this;
        }

        /// <summary>
        /// 添加左连接
        /// </summary>
        /// <param name="joinTable">连接的表</param>
        /// <param name="onCondition">连接条件</param>
        /// <returns>构建器本身</returns>
        public SqlQueryBuilder LeftJoin(string joinTable, string onCondition)
        {
            return Join(joinTable, onCondition, "LEFT");
        }

        /// <summary>
        /// 添加右连接
        /// </summary>
        /// <param name="joinTable">连接的表</param>
        /// <param name="onCondition">连接条件</param>
        /// <returns>构建器本身</returns>
        public SqlQueryBuilder RightJoin(string joinTable, string onCondition)
        {
            return Join(joinTable, onCondition, "RIGHT");
        }

        /// <summary>
        /// 添加 WHERE 条件
        /// </summary>
        /// <param name="condition">条件表达式</param>
        /// <param name="parameter">参数值</param>
        /// <returns>构建器本身</returns>
        public SqlQueryBuilder Where(string condition, object parameter = null)
        {
            _wheres.Add(condition);
            if (parameter != null)
            {
                _parameters.Add(parameter);
            }
            return this;
        }

        /// <summary>
        /// 添加 IN 条件
        /// </summary>
        /// <param name="column">列名</param>
        /// <param name="values">值集合</param>
        /// <returns>构建器本身</returns>
        public SqlQueryBuilder WhereIn(string column, IEnumerable<object> values)
        {
            var placeholders = string.Join(", ", values.Select((_, i) => string.Format("@param{0}", _parameters.Count + i)));
            _wheres.Add(string.Format("{0} IN ({1})", column, placeholders));
            _parameters.AddRange(values);
            return this;
        }

        /// <summary>
        /// 添加 BETWEEN 条件
        /// </summary>
        /// <param name="column">列名</param>
        /// <param name="start">起始值</param>
        /// <param name="end">结束值</param>
        /// <returns>构建器本身</returns>
        public SqlQueryBuilder WhereBetween(string column, object start, object end)
        {
            _wheres.Add(string.Format("{0} BETWEEN @param{1} AND @param{2}", column, _parameters.Count, _parameters.Count + 1));
            _parameters.Add(start);
            _parameters.Add(end);
            return this;
        }

        /// <summary>
        /// 添加 LIKE 条件
        /// </summary>
        /// <param name="column">列名</param>
        /// <param name="pattern">匹配模式</param>
        /// <returns>构建器本身</returns>
        public SqlQueryBuilder WhereLike(string column, string pattern)
        {
            _wheres.Add(string.Format("{0} LIKE @param{1}", column, _parameters.Count));
            _parameters.Add(pattern);
            return this;
        }

        /// <summary>
        /// 添加原始 SQL 条件
        /// </summary>
        /// <param name="rawSql">原始 SQL</param>
        /// <returns>构建器本身</returns>
        public SqlQueryBuilder WhereRaw(string rawSql)
        {
            _wheres.Add(string.Format("({0})", rawSql));
            return this;
        }

        /// <summary>
        /// 添加分组
        /// </summary>
        /// <param name="columns">分组列</param>
        /// <returns>构建器本身</returns>
        public SqlQueryBuilder GroupBy(string columns)
        {
            _groupBys.Add(columns);
            return this;
        }

        /// <summary>
        /// 添加排序
        /// </summary>
        /// <param name="column">排序列</param>
        /// <param name="descending">是否降序</param>
        /// <returns>构建器本身</returns>
        public SqlQueryBuilder OrderBy(string column, bool descending = false)
        {
            _orderBys.Add(string.Format("{0} {1}", column, descending ? "DESC" : "ASC"));
            return this;
        }

        /// <summary>
        /// 添加降序排序
        /// </summary>
        /// <param name="column">排序列</param>
        /// <returns>构建器本身</returns>
        public SqlQueryBuilder OrderByDescending(string column)
        {
            return OrderBy(column, true);
        }

        /// <summary>
        /// 设置跳过的记录数
        /// </summary>
        /// <param name="count">跳过数量</param>
        /// <returns>构建器本身</returns>
        public SqlQueryBuilder Skip(int count)
        {
            _skip = count;
            return this;
        }

        /// <summary>
        /// 设置获取的记录数
        /// </summary>
        /// <param name="count">获取数量</param>
        /// <returns>构建器本身</returns>
        public SqlQueryBuilder Take(int count)
        {
            _take = count;
            return this;
        }

        /// <summary>
        /// 构建 SQL 语句
        /// </summary>
        /// <returns>SQL 语句</returns>
        public string BuildSql()
        {
            if (string.IsNullOrEmpty(_from))
            {
                throw new InvalidOperationException("必须指定 FROM 子句");
            }

            var sql = new System.Text.StringBuilder();

            // SELECT
            sql.Append(string.Format("SELECT {0}", _select));

            // FROM
            sql.Append(string.Format(" FROM {0}", _from));

            // JOIN
            if (_joins.Any())
            {
                sql.Append(" ");
                sql.Append(string.Join(" ", _joins));
            }

            // WHERE
            if (_wheres.Any())
            {
                sql.Append(" WHERE ");
                sql.Append(string.Join(" AND ", _wheres));
            }

            // GROUP BY
            if (_groupBys.Any())
            {
                sql.Append(" GROUP BY ");
                sql.Append(string.Join(", ", _groupBys));
            }

            // ORDER BY
            if (_orderBys.Any())
            {
                sql.Append(" ORDER BY ");
                sql.Append(string.Join(", ", _orderBys));
            }

            // LIMIT
            if (_take.HasValue)
            {
                if (_skip.HasValue)
                {
                    sql.Append(string.Format(" LIMIT {0}, {1}", _skip.Value, _take.Value));
                }
                else
                {
                    sql.Append(string.Format(" LIMIT {0}", _take.Value));
                }
            }
            else if (_skip.HasValue)
            {
                sql.Append(string.Format(" LIMIT {0}, 18446744073709551615", _skip.Value));
            }

            return sql.ToString();
        }

        /// <summary>
        /// 获取参数列表
        /// </summary>
        /// <returns>参数列表</returns>
        public List<object> GetParameters()
        {
            return new List<object>(_parameters);
        }

        /// <summary>
        /// 构建 SQL 与参数
        /// </summary>
        /// <returns>SQL 语句与参数元组</returns>
        public (string sql, List<object> parameters) Build()
        {
            return (BuildSql(), GetParameters());
        }
    }

    /// <summary>
    /// 插入语句构建器
    /// </summary>
    public class InsertQueryBuilder
    {
        private string _tableName;
        private readonly Dictionary<string, object> _values = new Dictionary<string, object>();
        private readonly List<string> _columns = new List<string>();

        /// <summary>
        /// 设置插入表
        /// </summary>
        /// <param name="tableName">表名</param>
        /// <returns>构建器本身</returns>
        public InsertQueryBuilder Into(string tableName)
        {
            _tableName = tableName;
            return this;
        }

        /// <summary>
        /// 设置单列值
        /// </summary>
        /// <param name="column">列名</param>
        /// <param name="value">列值</param>
        /// <returns>构建器本身</returns>
        public InsertQueryBuilder Value(string column, object value)
        {
            _columns.Add(column);
            _values.Add(column, value);
            return this;
        }

        /// <summary>
        /// 设置多列值
        /// </summary>
        /// <param name="values">列值字典</param>
        /// <returns>构建器本身</returns>
        public InsertQueryBuilder Values(Dictionary<string, object> values)
        {
            foreach (var kvp in values)
            {
                Value(kvp.Key, kvp.Value);
            }
            return this;
        }

        /// <summary>
        /// 构建 SQL 语句
        /// </summary>
        /// <returns>SQL 语句</returns>
        public string BuildSql()
        {
            if (string.IsNullOrEmpty(_tableName))
            {
                throw new InvalidOperationException("必须指定表名");
            }

            if (!_columns.Any())
            {
                throw new InvalidOperationException("必须指定至少一列");
            }

            var columns = string.Join(", ", _columns);
            var placeholders = string.Join(", ", _columns.Select((c, i) => string.Format("@param{0}", i)));

            return string.Format("INSERT INTO {0} ({1}) VALUES ({2})", _tableName, columns, placeholders);
        }

        /// <summary>
        /// 获取参数列表
        /// </summary>
        /// <returns>参数列表</returns>
        public List<object> GetParameters()
        {
            return _columns.Select(c => _values[c]).ToList();
        }

        /// <summary>
        /// 构建 SQL 与参数
        /// </summary>
        /// <returns>SQL 语句与参数元组</returns>
        public (string sql, List<object> parameters) Build()
        {
            return (BuildSql(), GetParameters());
        }
    }

    /// <summary>
    /// 更新语句构建器
    /// </summary>
    public class UpdateQueryBuilder
    {
        private string _tableName;
        private readonly Dictionary<string, object> _setValues = new Dictionary<string, object>();
        private readonly List<string> _wheres = new List<string>();
        private readonly List<object> _parameters = new List<object>();

        /// <summary>
        /// 设置更新表
        /// </summary>
        /// <param name="tableName">表名</param>
        /// <returns>构建器本身</returns>
        public UpdateQueryBuilder Table(string tableName)
        {
            _tableName = tableName;
            return this;
        }

        /// <summary>
        /// 设置单列更新值
        /// </summary>
        /// <param name="column">列名</param>
        /// <param name="value">列值</param>
        /// <returns>构建器本身</returns>
        public UpdateQueryBuilder Set(string column, object value)
        {
            _setValues.Add(column, value);
            return this;
        }

        /// <summary>
        /// 设置多列更新值
        /// </summary>
        /// <param name="values">列值字典</param>
        /// <returns>构建器本身</returns>
        public UpdateQueryBuilder SetValues(Dictionary<string, object> values)
        {
            foreach (var kvp in values)
            {
                Set(kvp.Key, kvp.Value);
            }
            return this;
        }

        /// <summary>
        /// 添加 WHERE 条件
        /// </summary>
        /// <param name="condition">条件表达式</param>
        /// <param name="parameter">参数值</param>
        /// <returns>构建器本身</returns>
        public UpdateQueryBuilder Where(string condition, object parameter = null)
        {
            _wheres.Add(condition);
            if (parameter != null)
            {
                _parameters.Add(parameter);
            }
            return this;
        }

        /// <summary>
        /// 构建 SQL 语句
        /// </summary>
        /// <returns>SQL 语句</returns>
        public string BuildSql()
        {
            if (string.IsNullOrEmpty(_tableName))
            {
                throw new InvalidOperationException("必须指定表名");
            }

            if (!_setValues.Any())
            {
                throw new InvalidOperationException("必须指定至少一个 SET 值");
            }

            var sql = new System.Text.StringBuilder();

            sql.Append(string.Format("UPDATE {0} SET ", _tableName));

            var setClauses = new List<string>();
            var paramIndex = 0;

            foreach (var kvp in _setValues)
            {
                setClauses.Add(string.Format("{0} = @param{1}", kvp.Key, paramIndex));
                _parameters.Add(kvp.Value);
                paramIndex++;
            }

            sql.Append(string.Join(", ", setClauses));

            if (_wheres.Any())
            {
                sql.Append(" WHERE ");
                var whereParams = _wheres.Select((w, i) =>
                {
                    var hasParam = _parameters.Count > paramIndex && _parameters[paramIndex] != null;
                    if (hasParam)
                    {
                        var updated = w.Replace("@" + _parameters[paramIndex], "@param" + paramIndex);
                        paramIndex++;
                        return updated;
                    }
                    return w;
                });
                sql.Append(string.Join(" AND ", _wheres));
            }

            return sql.ToString();
        }

        /// <summary>
        /// 获取参数列表
        /// </summary>
        /// <returns>参数列表</returns>
        public List<object> GetParameters()
        {
            return new List<object>(_parameters);
        }

        /// <summary>
        /// 构建 SQL 与参数
        /// </summary>
        /// <returns>SQL 语句与参数元组</returns>
        public (string sql, List<object> parameters) Build()
        {
            return (BuildSql(), GetParameters());
        }
    }

    /// <summary>
    /// 删除语句构建器
    /// </summary>
    public class DeleteQueryBuilder
    {
        private string _tableName;
        private readonly List<string> _wheres = new List<string>();
        private readonly List<object> _parameters = new List<object>();

        /// <summary>
        /// 设置删除表
        /// </summary>
        /// <param name="tableName">表名</param>
        /// <returns>构建器本身</returns>
        public DeleteQueryBuilder From(string tableName)
        {
            _tableName = tableName;
            return this;
        }

        /// <summary>
        /// 添加 WHERE 条件
        /// </summary>
        /// <param name="condition">条件表达式</param>
        /// <param name="parameter">参数值</param>
        /// <returns>构建器本身</returns>
        public DeleteQueryBuilder Where(string condition, object parameter = null)
        {
            _wheres.Add(condition);
            if (parameter != null)
            {
                _parameters.Add(parameter);
            }
            return this;
        }

        /// <summary>
        /// 构建 SQL 语句
        /// </summary>
        /// <returns>SQL 语句</returns>
        public string BuildSql()
        {
            if (string.IsNullOrEmpty(_tableName))
            {
                throw new InvalidOperationException("必须指定表名");
            }

            var sql = new System.Text.StringBuilder();
            sql.Append(string.Format("DELETE FROM {0}", _tableName));

            if (_wheres.Any())
            {
                sql.Append(" WHERE ");
                sql.Append(string.Join(" AND ", _wheres));
            }

            return sql.ToString();
        }

        /// <summary>
        /// 获取参数列表
        /// </summary>
        /// <returns>参数列表</returns>
        public List<object> GetParameters()
        {
            return new List<object>(_parameters);
        }

        /// <summary>
        /// 构建 SQL 与参数
        /// </summary>
        /// <returns>SQL 语句与参数元组</returns>
        public (string sql, List<object> parameters) Build()
        {
            return (BuildSql(), GetParameters());
        }
    }

    /// <summary>
    /// SQL构建器工厂
    /// </summary>
    public static class SqlBuilder
    {
        /// <summary>
        /// 创建查询构建器
        /// </summary>
        /// <returns>查询构建器</returns>
        public static SqlQueryBuilder Select()
        {
            return new SqlQueryBuilder();
        }

        /// <summary>
        /// 创建插入构建器
        /// </summary>
        /// <returns>插入构建器</returns>
        public static InsertQueryBuilder Insert()
        {
            return new InsertQueryBuilder();
        }

        /// <summary>
        /// 创建更新构建器
        /// </summary>
        /// <returns>更新构建器</returns>
        public static UpdateQueryBuilder Update()
        {
            return new UpdateQueryBuilder();
        }

        /// <summary>
        /// 创建删除构建器
        /// </summary>
        /// <returns>删除构建器</returns>
        public static DeleteQueryBuilder Delete()
        {
            return new DeleteQueryBuilder();
        }
    }
}