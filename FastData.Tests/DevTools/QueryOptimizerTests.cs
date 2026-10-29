using System.Collections.Generic;
using System.Linq;
using FastData.DevTools;
using Xunit;

namespace FastData.Tests.DevTools
{
    /// <summary>
    /// QueryOptimizer 的改写安全性契约。
    /// <para>
    /// 背景：原实现对任何 <c>SELECT *</c> 都替换成写死的
    /// <c>"id, name, created_at"</c>，对列名不同的表会生成执行失败的 SQL。
    /// 这里锁住三条底线：
    /// 1. 默认不改写
    /// 2. 没有表结构绝不猜列名
    /// 3. 有表结构时按真实列名展开
    /// </para>
    /// </summary>
    public class QueryOptimizerTests
    {
        private const string Sql = "SELECT * FROM orders WHERE status = 1";

        /// <summary>
        /// 默认只给建议，SQL 原样返回。
        /// </summary>
        [Fact]
        public void Optimize_ShouldNotRewriteByDefault()
        {
            var result = QueryOptimizer.Optimize(Sql);

            Assert.Equal(Sql, result.OptimizedSql);
            Assert.NotEmpty(result.Optimizations);
        }

        /// <summary>
        /// 关键回归：绝不把 SELECT * 换成写死的列名。
        /// 原实现会产出 "SELECT id, name, created_at FROM orders ..."，
        /// 而 orders 表若没有这些列，SQL 直接执行失败。
        /// </summary>
        [Fact]
        public void Optimize_ShouldNeverGuessColumnNames()
        {
            var result = QueryOptimizer.Optimize(Sql);

            Assert.DoesNotContain("id, name, created_at", result.OptimizedSql);
            Assert.Contains("SELECT *", result.OptimizedSql);
        }

        /// <summary>
        /// 允许改写但没给表结构时，仍然不改写，只在建议里说明缺表结构。
        /// </summary>
        [Fact]
        public void Optimize_RewriteEnabledButNoSchema_ShouldNotRewrite()
        {
            var options = new OptimizationOptions { AllowSqlRewrite = true };
            var result = QueryOptimizer.Optimize(Sql, options);

            Assert.Equal(Sql, result.OptimizedSql);
            Assert.Contains("未提供表结构", string.Join("|", result.Optimizations.Select(o => o.Description)));
        }

        /// <summary>
        /// 给了真实表结构才展开，且用的是真实列名。
        /// </summary>
        [Fact]
        public void OptimizeWithSchema_ShouldExpandUsingRealColumns()
        {
            var schema = new Dictionary<string, IList<string>>
            {
                ["orders"] = new List<string> { "order_id", "amount", "status" }
            };

            var options = new OptimizationOptions { AllowSqlRewrite = true };
            var result = QueryOptimizer.OptimizeWithSchema(Sql, options, schema);

            Assert.Contains("order_id, amount, status", result.OptimizedSql);
            Assert.DoesNotContain("SELECT *", result.OptimizedSql);
            // WHERE 子句必须原样保留
            Assert.Contains("WHERE status = 1", result.OptimizedSql);
        }

        /// <summary>
        /// 表名大小写不同也应能匹配到（不同数据库对标识符大小写处理不同）。
        /// </summary>
        [Fact]
        public void OptimizeWithSchema_ShouldMatchTableNameCaseInsensitively()
        {
            var schema = new Dictionary<string, IList<string>>
            {
                ["Orders"] = new List<string> { "order_id", "amount" }
            };

            var options = new OptimizationOptions { AllowSqlRewrite = true };
            var result = QueryOptimizer.OptimizeWithSchema(Sql, options, schema);

            Assert.Contains("order_id, amount", result.OptimizedSql);
        }

        /// <summary>
        /// 表结构里没有这张表时，保持原样而不是猜。
        /// </summary>
        [Fact]
        public void OptimizeWithSchema_UnknownTable_ShouldKeepOriginal()
        {
            var schema = new Dictionary<string, IList<string>>
            {
                ["users"] = new List<string> { "id", "name" }
            };

            var options = new OptimizationOptions { AllowSqlRewrite = true };
            var result = QueryOptimizer.OptimizeWithSchema(Sql, options, schema);

            Assert.Equal(Sql, result.OptimizedSql);
        }

        /// <summary>
        /// 子查询不再被正则改写。
        /// 原实现会把 `IN (SELECT x FROM t)` 变成 `JOIN t ON x = t.id` —— 语法不成立且丢语义。
        /// </summary>
        [Fact]
        public void Optimize_ShouldNotRewriteSubquery()
        {
            const string subquerySql = "SELECT * FROM orders WHERE id IN (SELECT order_id FROM items)";

            var options = new OptimizationOptions { AllowSqlRewrite = true };
            var result = QueryOptimizer.Optimize(subquerySql, options);

            Assert.DoesNotContain("JOIN items ON", result.OptimizedSql);
            Assert.Contains("IN (SELECT order_id FROM items)", result.OptimizedSql);
        }

        /// <summary>
        /// 空参数应明确报错，而不是返回空结果让调用方误判。
        /// </summary>
        [Fact]
        public void Optimize_NullSql_ShouldThrow()
        {
            Assert.Throws<System.ArgumentNullException>(() => QueryOptimizer.Optimize(null));
        }
    }
}