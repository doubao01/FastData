using System;
using FastData.Context;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace FastData.DevTools
{
    /// <summary>
    /// 查询优化器
    /// </summary>
    public static class QueryOptimizer
    {
        /// <summary>
        /// 优化 SQL 查询
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>默认只给建议，不改写 SQL。</b><see cref="QueryOptimizationResult.OptimizedSql"/> 与输入相同。
        /// </para>
        /// <para>
        /// 需要真正改写时，必须显式设置 <see cref="OptimizationOptions.AllowSqlRewrite"/> = true，
        /// 并且要为涉及 SELECT * 的查询提供真实表结构（见
        /// <see cref="OptimizeWithSchema"/>）。本方法拿不到表结构，
        /// 强行改写只能靠猜列名 —— 那样产出的 SQL 会直接执行失败。
        /// </para>
        /// </remarks>
        public static QueryOptimizationResult Optimize(string sql, OptimizationOptions options = null)
        {
            return OptimizeCore(sql, options, null);
        }

        /// <summary>
        /// 带表结构的优化：可以安全地把 <c>SELECT *</c> 展开成真实列名。
        /// </summary>
        /// <param name="sql">待分析的 SQL</param>
        /// <param name="options">选项；需要改写时置 <see cref="OptimizationOptions.AllowSqlRewrite"/> = true</param>
        /// <param name="tableColumns">表名 → 列名清单。缺失的表不会被展开。</param>
        public static QueryOptimizationResult OptimizeWithSchema(
            string sql,
            OptimizationOptions options,
            IDictionary<string, IList<string>> tableColumns)
        {
            if (tableColumns == null)
                throw new ArgumentNullException(nameof(tableColumns));

            return OptimizeCore(sql, options, tableColumns);
        }

        private static QueryOptimizationResult OptimizeCore(
            string sql,
            OptimizationOptions options,
            IDictionary<string, IList<string>> tableColumns)
        {
            if (sql == null)
                throw new ArgumentNullException(nameof(sql));

            options = options ?? OptimizationOptions.Default;
            var result = new QueryOptimizationResult
            {
                OriginalSql = sql,
                OptimizedSql = sql,
                Optimizations = new List<OptimizationSuggestion>(),
                EstimatedImprovement = 0
            };

            // 检查 SELECT *
            if (sql.ToUpper().Contains("SELECT *"))
            {
                // 默认不改写。原来这里无条件调用 ReplaceSelectStar，
                // 而那个实现是把 SELECT * 换成写死的 "id, name, created_at" ——
                // 对任何列名不同的表都会生成执行失败的 SQL。
                if (options.AllowSqlRewrite && options.OptimizeColumns)
                {
                    result.OptimizedSql = ReplaceSelectStar(result.OptimizedSql, tableColumns);
                }

                result.Optimizations.Add(new OptimizationSuggestion
                {
                    Type = OptimizationType.ColumnSelection,
                    Priority = OptimizationPriority.High,
                    Description = tableColumns == null
                        ? "SELECT * 建议替换为具体列名（未提供表结构，不做自动改写）"
                        : "SELECT * 替换为具体列名，减少数据传输",
                    EstimatedImprovement = "20-30%"
                });
            }

            // 检查 WHERE 子句
            if (!sql.ToUpper().Contains("WHERE"))
            {
                result.Optimizations.Add(new OptimizationSuggestion
                {
                    Type = OptimizationType.Filtering,
                    Priority = OptimizationPriority.Critical,
                    Description = "查询缺少 WHERE 子句，可能返回大量数据",
                    EstimatedImprovement = "50-90%"
                });
            }

            // 检查 LIKE 查询
            var likePattern = new Regex(@"LIKE\s+['""]%[\w%]+['""]", RegexOptions.IgnoreCase);
            if (likePattern.IsMatch(sql))
            {
                result.Optimizations.Add(new OptimizationSuggestion
                {
                    Type = OptimizationType.PartialMatch,
                    Priority = OptimizationPriority.Medium,
                    Description = "前缀通配符 LIKE 查询无法使用索引，考虑使用全文搜索",
                    EstimatedImprovement = "10-50%"
                });
            }

            // 检查子查询
            if (sql.Contains("(") && sql.Contains("SELECT"))
            {
                var subqueryPattern = new Regex(@"\(\s*SELECT", RegexOptions.IgnoreCase);
                if (subqueryPattern.IsMatch(sql))
                {
                    // 默认不改写：原来无条件调用 ReplaceSubqueries，
                    // 而它用正则把 `IN (SELECT x FROM t)` 替换成 `JOIN t ON x = t.id` ——
                    // 既不是合法 SQL，也丢掉了原查询的过滤语义。
                    // 子查询改 JOIN 需要完整的语法树与语义分析，正则做不到。
                    result.Optimizations.Add(new OptimizationSuggestion
                    {
                        Type = OptimizationType.Subquery,
                        Priority = OptimizationPriority.Medium,
                        Description = "存在子查询，可评估改为 JOIN（需人工确认语义等价）",
                        EstimatedImprovement = "15-35%"
                    });
                }
            }

            // 检查 DISTINCT
            if (sql.ToUpper().Contains("DISTINCT"))
            {
                result.Optimizations.Add(new OptimizationSuggestion
                {
                    Type = OptimizationType.Deduplication,
                    Priority = OptimizationPriority.Low,
                    Description = "DISTINCT 操作消耗资源，考虑使用 GROUP BY 替代",
                    EstimatedImprovement = "5-15%"
                });
            }

            // 检查 ORDER BY
            var orderByMatch = Regex.Match(sql, @"ORDER BY\s+.+?(\s+DESC|\s+ASC|\s*$)", RegexOptions.IgnoreCase);
            if (orderByMatch.Success)
            {
                var orderByClause = orderByMatch.Value;
                if (orderByClause.Split(',').Length > 3)
                {
                    result.Optimizations.Add(new OptimizationSuggestion
                    {
                        Type = OptimizationType.Sorting,
                        Priority = OptimizationPriority.Low,
                        Description = "多个 ORDER BY 字段，考虑使用索引覆盖",
                        EstimatedImprovement = "5-10%"
                    });
                }
            }

            // 检查 JOIN 数量
            var joinCount = Regex.Matches(sql, @"\b(JOIN|INNER JOIN|LEFT JOIN|RIGHT JOIN)\b", RegexOptions.IgnoreCase).Count;
            if (joinCount > 5)
            {
                result.Optimizations.Add(new OptimizationSuggestion
                {
                    Type = OptimizationType.Join,
                    Priority = OptimizationPriority.Medium,
                    Description = string.Format("包含 {0} 个 JOIN，考虑查询分解", joinCount),
                    EstimatedImprovement = "20-40%"
                });
            }

            // 计算总体预估改进
            result.EstimatedImprovement = CalculateEstimatedImprovement(result.Optimizations);

            return result;
        }

        /// <summary>
        /// 分析查询性能
        /// </summary>
        public static QueryPerformanceAnalysis AnalyzePerformance(string sql, double executionTimeMs, int rowsReturned = 0)
        {
            var analysis = new QueryPerformanceAnalysis
            {
                Sql = sql,
                ExecutionTimeMs = executionTimeMs,
                RowsReturned = rowsReturned
            };

            // 评估性能等级
            if (executionTimeMs < 100)
            {
                analysis.PerformanceLevel = PerformanceLevel.Excellent;
                analysis.Rating = "优秀";
            }
            else if (executionTimeMs < 500)
            {
                analysis.PerformanceLevel = PerformanceLevel.Good;
                analysis.Rating = "良好";
            }
            else if (executionTimeMs < 1000)
            {
                analysis.PerformanceLevel = PerformanceLevel.Fair;
                analysis.Rating = "一般";
            }
            else if (executionTimeMs < 5000)
            {
                analysis.PerformanceLevel = PerformanceLevel.Poor;
                analysis.Rating = "较差";
            }
            else
            {
                analysis.PerformanceLevel = PerformanceLevel.Critical;
                analysis.Rating = "严重";
            }

            // 生成建议
            analysis.Recommendations = GeneratePerformanceRecommendations(sql, executionTimeMs, rowsReturned);

            return analysis;
        }

        /// <summary>
        /// 生成索引建议
        /// </summary>
        public static List<IndexSuggestion> SuggestIndexes(string sql, string tableName = null)
        {
            var suggestions = new List<IndexSuggestion>();

            // 从 SQL 中提取表名
            if (string.IsNullOrEmpty(tableName))
            {
                tableName = ExtractTableName(sql);
            }

            // 分析 WHERE 子句
            var whereMatch = Regex.Match(sql, @"WHERE\s+(.+?)(?:\s+GROUP BY|\s+ORDER BY|\s+LIMIT|$)", RegexOptions.IgnoreCase);
            if (whereMatch.Success)
            {
                var conditions = whereMatch.Groups[1].Value.Split(new[] { "AND", "OR" }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var condition in conditions)
                {
                    var columnMatch = Regex.Match(condition, @"(\w+)\s*[=<>!]+");
                    if (columnMatch.Success)
                    {
                        var column = columnMatch.Groups[1].Value;
                        if (!IsFunction(column))
                        {
                            suggestions.Add(new IndexSuggestion
                            {
                                TableName = tableName,
                                ColumnName = column,
                                Type = IndexType.BTree,
                                Priority = IndexPriority.High,
                                Reason = "WHERE 子句中频繁使用"
                            });
                        }
                    }
                }
            }

            // 分析 JOIN 条件
            var joinMatches = Regex.Matches(sql, @"\b(JOIN|INNER JOIN|LEFT JOIN|RIGHT JOIN)\s+(\w+)\s+ON\s+(\w+)\.(\w+)\s*=\s*(\w+)\.(\w+)", RegexOptions.IgnoreCase);
            foreach (Match match in joinMatches)
            {
                var column1 = match.Groups[4].Value;
                var column2 = match.Groups[6].Value;

                suggestions.Add(new IndexSuggestion
                {
                    TableName = tableName,
                    ColumnName = column1,
                    Type = IndexType.BTree,
                    Priority = IndexPriority.High,
                    Reason = "JOIN 条件中使用"
                });
            }

            // 分析 ORDER BY
            var orderByMatch = Regex.Match(sql, @"ORDER BY\s+(\w+)", RegexOptions.IgnoreCase);
            if (orderByMatch.Success)
            {
                var column = orderByMatch.Groups[1].Value;
                if (!IsFunction(column))
                {
                    suggestions.Add(new IndexSuggestion
                    {
                        TableName = tableName,
                        ColumnName = column,
                        Type = IndexType.BTree,
                        Priority = IndexPriority.Medium,
                        Reason = "ORDER BY 子句中使用"
                    });
                }
            }

            return suggestions.Distinct().ToList();
        }

        /// <summary>
        /// 检测查询中的常见问题
        /// </summary>
        public static List<QueryIssue> DetectIssues(string sql)
        {
            var issues = new List<QueryIssue>();

            // 检查 N+1 查询问题
            var inClauseCount = Regex.Matches(sql, @"IN\s*\(", RegexOptions.IgnoreCase).Count;
            if (inClauseCount > 3)
            {
                issues.Add(new QueryIssue
                {
                    Type = IssueType.NPlusOne,
                    Severity = IssueSeverity.High,
                    Description = "可能存在 N+1 查询问题，考虑使用 JOIN",
                    Recommendation = "使用 JOIN 替代多次 IN 查询"
                });
            }

            // 检查全表扫描
            if (!sql.ToUpper().Contains("WHERE") && !sql.ToUpper().Contains("LIMIT"))
            {
                issues.Add(new QueryIssue
                {
                    Type = IssueType.FullTableScan,
                    Severity = IssueSeverity.Critical,
                    Description = "查询可能导致全表扫描",
                    Recommendation = "添加 WHERE 子句或 LIMIT 限制"
                });
            }

            // 检查大结果集
            if (!sql.ToUpper().Contains("LIMIT") && !sql.ToUpper().Contains("TOP"))
            {
                issues.Add(new QueryIssue
                {
                    Type = IssueType.LargeResultset,
                    Severity = IssueSeverity.Medium,
                    Description = "查询可能返回大量数据",
                    Recommendation = "添加 LIMIT 或 TOP 限制结果集大小"
                });
            }

            // 检查未使用的子查询
            var subqueryPattern = new Regex(@"\(\s*SELECT\s+.+?\s+FROM\s+.+?\)", RegexOptions.IgnoreCase);
            var subqueries = subqueryPattern.Matches(sql);
            foreach (Match match in subqueries)
            {
                var subquery = match.Value;
                if (!subquery.Contains("WHERE") && !subquery.Contains("GROUP BY"))
                {
                    issues.Add(new QueryIssue
                    {
                        Type = IssueType.InefficientSubquery,
                        Severity = IssueSeverity.Medium,
                        Description = "子查询可能效率低下",
                        Recommendation = "考虑使用 JOIN 或 EXISTS 替代"
                    });
                }
            }

            return issues;
        }

        /// <summary>
        /// 生成查询执行计划建议
        /// </summary>
        public static ExecutionPlanSuggestions SuggestExecutionPlan(string sql)
        {
            var suggestions = new ExecutionPlanSuggestions();

            // 分析查询复杂度
            var complexity = AnalyzeQueryComplexity(sql);
            suggestions.Complexity = complexity;

            // 建议并行度
            if (complexity == QueryComplexity.High || complexity == QueryComplexity.VeryHigh)
            {
                suggestions.SuggestedParallelism = true;
                suggestions.RecommendedParallelismLevel = Math.Min(Environment.ProcessorCount, 4);
            }

            // 建议缓存策略
            if (sql.ToUpper().Contains("SELECT") && !sql.ToUpper().Contains("INSERT") && !sql.ToUpper().Contains("UPDATE") && !sql.ToUpper().Contains("DELETE"))
            {
                suggestions.SuggestedCacheStrategy = CacheStrategy.ReadCache;
            }

            // 建议分批处理
            if (sql.ToUpper().Contains("UPDATE") || sql.ToUpper().Contains("DELETE"))
            {
                var whereMatch = Regex.Match(sql, @"WHERE\s+(.+?)(?:\s+LIMIT|$)", RegexOptions.IgnoreCase);
                if (!whereMatch.Success)
                {
                    suggestions.SuggestedBatchProcessing = true;
                    suggestions.RecommendedBatchSize = 1000;
                }
            }

            return suggestions;
        }

        #region 私有辅助方法

        /// <summary>
        /// 从数据库读取指定表的列名，供 <see cref="OptimizeWithSchema"/> 使用。
        /// </summary>
        /// <param name="db">已配置好的数据上下文</param>
        /// <param name="tableNames">表名清单</param>
        /// <returns>表名 → 列名清单。读不到的表不会出现在结果里（调用方据此回退到「只给建议」）。</returns>
        /// <remarks>
        /// 依赖 INFORMATION_SCHEMA（SQL Server / MySQL / PostgreSQL / SQLite 均支持）。
        /// Oracle 等不支持的数据库会返回空字典，而不是抛异常 —— 优化建议本身不该因为
        /// 取表结构失败而中断。
        /// </remarks>
        public static IDictionary<string, IList<string>> LoadTableColumns(
            DataContext db, IEnumerable<string> tableNames)
        {
            if (db == null)
                throw new ArgumentNullException(nameof(db));
            if (tableNames == null)
                throw new ArgumentNullException(nameof(tableNames));

            var result = new Dictionary<string, IList<string>>(StringComparer.OrdinalIgnoreCase);

            try
            {
                db.EnsureConnectionOpen();

                foreach (var table in tableNames)
                {
                    if (string.IsNullOrWhiteSpace(table))
                        continue;

                    // 表名是标识符，无法参数化。只允许字母/数字/下划线，
                    // 挡掉拼接注入；不匹配的跳过，不报错。
                    if (!Regex.IsMatch(table, @"^[A-Za-z_][A-Za-z0-9_$]*$"))
                        continue;

                    var columns = new List<string>();

                    // 注意：不要 Dispose db.cmd —— 它是 DataContext 持有的共享命令对象。
                    db.cmd.Parameters.Clear();
                    db.cmd.CommandText =
                        "SELECT COLUMN_NAME FROM INFORMATION_SCHEMA.COLUMNS " +
                        "WHERE TABLE_NAME = @p_table ORDER BY ORDINAL_POSITION";

                    var param = db.cmd.CreateParameter();
                    param.ParameterName = "@p_table";
                    param.Value = table;
                    db.cmd.Parameters.Add(param);

                    using (var reader = db.cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            if (!reader.IsDBNull(0))
                                columns.Add(Convert.ToString(reader.GetValue(0)));
                        }
                    }

                    if (columns.Count > 0)
                        result[table] = columns;
                }
            }
            catch (Exception ex)
            {
                // 取表结构失败（如 Oracle 无 INFORMATION_SCHEMA）不应让优化建议功能不可用。
                System.Diagnostics.Trace.TraceWarning(string.Format(
                    "QueryOptimizer.LoadTableColumns 读取表结构失败，将回退为只给建议：{0}: {1}",
                    ex.GetType().Name, ex.Message));
            }

            return result;
        }

        /// <summary>
        /// 把 <c>SELECT *</c> 展开为真实列名。
        /// <para>
        /// 拿不到表结构时原样返回 —— 绝不猜列名。
        /// 原实现不管什么表都替换成写死的 "id, name, created_at"，
        /// 对列名不同的表会生成执行失败的 SQL。
        /// </para>
        /// </summary>
        private static string ReplaceSelectStar(string sql, IDictionary<string, IList<string>> tableColumns)
        {
            if (tableColumns == null || tableColumns.Count == 0)
                return sql;

            var tableMatch = Regex.Match(sql, @"FROM\s+(\w+)", RegexOptions.IgnoreCase);
            if (!tableMatch.Success)
                return sql;

            var tableName = tableMatch.Groups[1].Value;

            IList<string> columns = null;
            if (!tableColumns.TryGetValue(tableName, out columns))
            {
                // 大小写不敏感匹配一次（不同数据库对标识符大小写处理不同）
                foreach (var pair in tableColumns)
                {
                    if (string.Equals(pair.Key, tableName, StringComparison.OrdinalIgnoreCase))
                    {
                        columns = pair.Value;
                        break;
                    }
                }
            }

            if (columns == null || columns.Count == 0)
                return sql;

            var columnList = string.Join(", ", columns);
            return Regex.Replace(sql, @"SELECT\s+\*", "SELECT " + columnList, RegexOptions.IgnoreCase);
        }

        // 原 ReplaceSubqueries 已删除。
        // 它用正则把 `IN (SELECT x FROM t)` 替换成 `JOIN t ON x = t.id`：
        // 语法不成立（缺 ON 上下文与表别名），语义也被破坏（原 WHERE 过滤丢失）。
        // 子查询改 JOIN 需要完整语法树与语义等价性分析，正则做不到，
        // 因此只产出建议、由人工判断。

        private static string ExtractTableName(string sql)
        {
            var match = Regex.Match(sql, @"FROM\s+(\w+)", RegexOptions.IgnoreCase);
            return match.Success ? match.Groups[1].Value : "unknown";
        }

        private static bool IsFunction(string column)
        {
            var functions = new[] { "COUNT", "SUM", "AVG", "MAX", "MIN", "UPPER", "LOWER", "SUBSTRING", "DATE_FORMAT" };
            return functions.Any(f => column.ToUpper().StartsWith(f));
        }

        private static double CalculateEstimatedImprovement(List<OptimizationSuggestion> optimizations)
        {
            if (!optimizations.Any()) return 0;

            var totalImprovement = 0.0;
            foreach (var opt in optimizations)
            {
                var range = opt.EstimatedImprovement.Split('-');
                if (range.Length == 2)
                {
                    var min = double.Parse(range[0].TrimEnd('%'));
                    var max = double.Parse(range[1].TrimEnd('%'));
                    totalImprovement += (min + max) / 2;
                }
                else if (range.Length == 1)
                {
                    totalImprovement += double.Parse(range[0].TrimEnd('%'));
                }
            }

            return Math.Min(totalImprovement, 100);
        }

        private static List<PerformanceRecommendation> GeneratePerformanceRecommendations(string sql, double executionTimeMs, int rowsReturned)
        {
            var recommendations = new List<PerformanceRecommendation>();

            if (executionTimeMs > 1000)
            {
                recommendations.Add(new PerformanceRecommendation
                {
                    Type = RecommendationType.Optimization,
                    Priority = RecommendationPriority.High,
                    Description = "查询执行时间过长，建议优化",
                    Actions = new[] { "添加索引", "优化 WHERE 条件", "减少返回列数" }
                });
            }

            if (rowsReturned > 10000)
            {
                recommendations.Add(new PerformanceRecommendation
                {
                    Type = RecommendationType.Pagination,
                    Priority = RecommendationPriority.Medium,
                    Description = "返回数据量过大，建议分页",
                    Actions = new[] { "添加 LIMIT", "实现分页查询" }
                });
            }

            if (sql.ToUpper().Contains("SELECT *"))
            {
                recommendations.Add(new PerformanceRecommendation
                {
                    Type = RecommendationType.Optimization,
                    Priority = RecommendationPriority.Low,
                    Description = "避免使用 SELECT *",
                    Actions = new[] { "指定具体列名", "减少数据传输" }
                });
            }

            return recommendations;
        }

        private static QueryComplexity AnalyzeQueryComplexity(string sql)
        {
            var score = 0;

            // JOIN 数量
            score += Regex.Matches(sql, @"\bJOIN\b", RegexOptions.IgnoreCase).Count * 2;

            // 子查询数量
            score += Regex.Matches(sql, @"\(\s*SELECT", RegexOptions.IgnoreCase).Count * 3;

            // WHERE 条件复杂度
            var whereMatch = Regex.Match(sql, @"WHERE\s+(.+?)(?:\s+GROUP BY|\s+ORDER BY|$)", RegexOptions.IgnoreCase);
            if (whereMatch.Success)
            {
                var conditions = whereMatch.Groups[1].Value.Split(new[] { "AND", "OR" }, StringSplitOptions.RemoveEmptyEntries);
                score += conditions.Length;
            }

            // 聚合函数
            score += Regex.Matches(sql, @"\b(COUNT|SUM|AVG|MAX|MIN)\b", RegexOptions.IgnoreCase).Count * 2;

            // GROUP BY
            if (sql.ToUpper().Contains("GROUP BY"))
            {
                score += 3;
            }

            if (score < 5)
                return QueryComplexity.Low;
            else if (score < 10)
                return QueryComplexity.Medium;
            else if (score < 20)
                return QueryComplexity.High;
            else
                return QueryComplexity.VeryHigh;
        }

        #endregion
    }

    #region 结果类

    /// <summary>
    /// 查询优化结果
    /// </summary>
    public class QueryOptimizationResult
    {
        /// <summary>
        /// 原始 SQL
        /// </summary>
        public string OriginalSql { get; set; }
        /// <summary>
        /// 优化后的 SQL
        /// </summary>
        public string OptimizedSql { get; set; }
        /// <summary>
        /// 优化建议列表
        /// </summary>
        public List<OptimizationSuggestion> Optimizations { get; set; }
        /// <summary>
        /// 预估改进幅度
        /// </summary>
        public double EstimatedImprovement { get; set; }
    }

    /// <summary>
    /// 优化建议
    /// </summary>
    public class OptimizationSuggestion
    {
        /// <summary>
        /// 优化类型
        /// </summary>
        public OptimizationType Type { get; set; }
        /// <summary>
        /// 优化优先级
        /// </summary>
        public OptimizationPriority Priority { get; set; }
        /// <summary>
        /// 建议描述
        /// </summary>
        public string Description { get; set; }
        /// <summary>
        /// 预估改进幅度
        /// </summary>
        public string EstimatedImprovement { get; set; }
    }

    /// <summary>
    /// 优化类型
    /// </summary>
    public enum OptimizationType
    {
        /// <summary>
        /// 列选择
        /// </summary>
        ColumnSelection,
        /// <summary>
        /// 过滤条件
        /// </summary>
        Filtering,
        /// <summary>
        /// 连接
        /// </summary>
        Join,
        /// <summary>
        /// 子查询
        /// </summary>
        Subquery,
        /// <summary>
        /// 排序
        /// </summary>
        Sorting,
        /// <summary>
        /// 部分匹配
        /// </summary>
        PartialMatch,
        /// <summary>
        /// 去重
        /// </summary>
        Deduplication
    }

    /// <summary>
    /// 优化优先级
    /// </summary>
    public enum OptimizationPriority
    {
        /// <summary>
        /// 低
        /// </summary>
        Low,
        /// <summary>
        /// 中
        /// </summary>
        Medium,
        /// <summary>
        /// 高
        /// </summary>
        High,
        /// <summary>
        /// 严重
        /// </summary>
        Critical
    }

    /// <summary>
    /// 查询性能分析
    /// </summary>
    public class QueryPerformanceAnalysis
    {
        /// <summary>
        /// SQL 语句
        /// </summary>
        public string Sql { get; set; }
        /// <summary>
        /// 执行时间（毫秒）
        /// </summary>
        public double ExecutionTimeMs { get; set; }
        /// <summary>
        /// 返回行数
        /// </summary>
        public int RowsReturned { get; set; }
        /// <summary>
        /// 性能等级
        /// </summary>
        public PerformanceLevel PerformanceLevel { get; set; }
        /// <summary>
        /// 评级描述
        /// </summary>
        public string Rating { get; set; }
        /// <summary>
        /// 性能建议列表
        /// </summary>
        public List<PerformanceRecommendation> Recommendations { get; set; } = new List<PerformanceRecommendation>();
    }

    /// <summary>
    /// 性能级别
    /// </summary>
    public enum PerformanceLevel
    {
        /// <summary>
        /// 优秀
        /// </summary>
        Excellent,
        /// <summary>
        /// 良好
        /// </summary>
        Good,
        /// <summary>
        /// 一般
        /// </summary>
        Fair,
        /// <summary>
        /// 较差
        /// </summary>
        Poor,
        /// <summary>
        /// 严重
        /// </summary>
        Critical
    }

    /// <summary>
    /// 性能建议
    /// </summary>
    public class PerformanceRecommendation
    {
        /// <summary>
        /// 建议类型
        /// </summary>
        public RecommendationType Type { get; set; }
        /// <summary>
        /// 建议优先级
        /// </summary>
        public RecommendationPriority Priority { get; set; }
        /// <summary>
        /// 建议描述
        /// </summary>
        public string Description { get; set; }
        /// <summary>
        /// 建议操作
        /// </summary>
        public string[] Actions { get; set; }
    }

    /// <summary>
    /// 建议类型
    /// </summary>
    public enum RecommendationType
    {
        /// <summary>
        /// 优化
        /// </summary>
        Optimization,
        /// <summary>
        /// 分页
        /// </summary>
        Pagination,
        /// <summary>
        /// 索引
        /// </summary>
        Indexing,
        /// <summary>
        /// 缓存
        /// </summary>
        Caching
    }

    /// <summary>
    /// 建议优先级
    /// </summary>
    public enum RecommendationPriority
    {
        /// <summary>
        /// 低
        /// </summary>
        Low,
        /// <summary>
        /// 中
        /// </summary>
        Medium,
        /// <summary>
        /// 高
        /// </summary>
        High
    }

    /// <summary>
    /// 索引建议优先级
    /// </summary>
    public enum IndexPriority
    {
        /// <summary>
        /// 低
        /// </summary>
        Low,
        /// <summary>
        /// 中
        /// </summary>
        Medium,
        /// <summary>
        /// 高
        /// </summary>
        High,
        /// <summary>
        /// 严重
        /// </summary>
        Critical
    }

    /// <summary>
    /// 索引建议
    /// </summary>
    public class IndexSuggestion
    {
        /// <summary>
        /// 表名
        /// </summary>
        public string TableName { get; set; }
        /// <summary>
        /// 列名
        /// </summary>
        public string ColumnName { get; set; }
        /// <summary>
        /// 索引类型
        /// </summary>
        public IndexType Type { get; set; }
        /// <summary>
        /// 索引优先级
        /// </summary>
        public IndexPriority Priority { get; set; }
        /// <summary>
        /// 建议原因
        /// </summary>
        public string Reason { get; set; }

        /// <summary>
        /// 判断两个索引建议是否相等（按表名和列名）
        /// </summary>
        /// <param name="obj">比较对象</param>
        /// <returns>是否相等</returns>
        public override bool Equals(object obj)
        {
            return obj is IndexSuggestion other &&
                   TableName == other.TableName &&
                   ColumnName == other.ColumnName;
        }

        /// <summary>
        /// 获取哈希码
        /// </summary>
        /// <returns>哈希码</returns>
        public override int GetHashCode()
        {
            return HashCode.Combine(TableName, ColumnName);
        }
    }

    /// <summary>
    /// 查询问题
    /// </summary>
    public class QueryIssue
    {
        /// <summary>
        /// 问题类型
        /// </summary>
        public IssueType Type { get; set; }
        /// <summary>
        /// 严重程度
        /// </summary>
        public IssueSeverity Severity { get; set; }
        /// <summary>
        /// 问题描述
        /// </summary>
        public string Description { get; set; }
        /// <summary>
        /// 修复建议
        /// </summary>
        public string Recommendation { get; set; }
    }

    /// <summary>
    /// 问题类型
    /// </summary>
    public enum IssueType
    {
        /// <summary>
        /// N+1 查询
        /// </summary>
        NPlusOne,
        /// <summary>
        /// 全表扫描
        /// </summary>
        FullTableScan,
        /// <summary>
        /// 大结果集
        /// </summary>
        LargeResultset,
        /// <summary>
        /// 低效子查询
        /// </summary>
        InefficientSubquery
    }

    /// <summary>
    /// 问题严重程度
    /// </summary>
    public enum IssueSeverity
    {
        /// <summary>
        /// 低
        /// </summary>
        Low,
        /// <summary>
        /// 中
        /// </summary>
        Medium,
        /// <summary>
        /// 高
        /// </summary>
        High,
        /// <summary>
        /// 严重
        /// </summary>
        Critical
    }

    /// <summary>
    /// 执行计划建议
    /// </summary>
    public class ExecutionPlanSuggestions
    {
        /// <summary>
        /// 查询复杂度
        /// </summary>
        public QueryComplexity Complexity { get; set; }
        /// <summary>
        /// 是否建议并行执行
        /// </summary>
        public bool SuggestedParallelism { get; set; }
        /// <summary>
        /// 建议的并行度
        /// </summary>
        public int RecommendedParallelismLevel { get; set; }
        /// <summary>
        /// 建议的缓存策略
        /// </summary>
        public CacheStrategy SuggestedCacheStrategy { get; set; }
        /// <summary>
        /// 是否建议分批处理
        /// </summary>
        public bool SuggestedBatchProcessing { get; set; }
        /// <summary>
        /// 建议的批处理大小
        /// </summary>
        public int RecommendedBatchSize { get; set; }
    }

    /// <summary>
    /// 查询复杂度
    /// </summary>
    public enum QueryComplexity
    {
        /// <summary>
        /// 低
        /// </summary>
        Low,
        /// <summary>
        /// 中
        /// </summary>
        Medium,
        /// <summary>
        /// 高
        /// </summary>
        High,
        /// <summary>
        /// 极高
        /// </summary>
        VeryHigh
    }

    /// <summary>
    /// 缓存策略
    /// </summary>
    public enum CacheStrategy
    {
        /// <summary>
        /// 不使用缓存
        /// </summary>
        None,
        /// <summary>
        /// 读缓存
        /// </summary>
        ReadCache,
        /// <summary>
        /// 写缓存
        /// </summary>
        WriteCache,
        /// <summary>
        /// 读写缓存
        /// </summary>
        ReadWriteCache
    }

    /// <summary>
    /// 优化选项
    /// </summary>
    public class OptimizationOptions
    {
        /// <summary>
        /// 是否优化列选择
        /// </summary>
        public bool OptimizeColumns { get; set; } = true;
        /// <summary>
        /// 是否优化连接
        /// </summary>
        public bool OptimizeJoins { get; set; } = true;
        /// <summary>
        /// 是否优化子查询
        /// </summary>
        public bool OptimizeSubqueries { get; set; } = true;
        /// <summary>
        /// 是否优化排序
        /// </summary>
        public bool OptimizeSorting { get; set; } = true;

        /// <summary>
        /// 是否允许改写 SQL（默认 <c>false</c>）。
        /// <para>
        /// 关闭时 <see cref="QueryOptimizationResult.OptimizedSql"/> 始终等于输入 SQL，
        /// 只产出建议。这是安全默认值。
        /// </para>
        /// <para>
        /// 开启后仍需要调用方提供真实表结构（<see cref="QueryOptimizer.OptimizeWithSchema"/>），
        /// 否则缺少列信息时不会展开 <c>SELECT *</c>，只保留建议。
        /// </para>
        /// </summary>
        public bool AllowSqlRewrite { get; set; } = false;

        /// <summary>
        /// 默认选项
        /// </summary>
        public static OptimizationOptions Default => new OptimizationOptions();
    }

    #endregion
}