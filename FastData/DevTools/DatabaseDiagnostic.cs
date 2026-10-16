using System;
using FastData.Context;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Linq;
using FastData.Config;
using FastData.DbTypes;
using FastData.Model;

namespace FastData.DevTools
{
    /// <summary>
    /// 数据库诊断工具 - 检查和诊断数据库问题
    /// </summary>
    public static class DatabaseDiagnostic
    {
        /// <summary>
        /// 诊断结果
        /// </summary>
        public class DiagnosticResult
        {
            /// <summary>
            /// 数据库是否健康
            /// </summary>
            public bool IsHealthy { get; set; }
            /// <summary>
            /// 诊断发现的问题列表
            /// </summary>
            public List<DiagnosticIssue> Issues { get; set; } = new List<DiagnosticIssue>();
            /// <summary>
            /// 诊断指标集合
            /// </summary>
            public Dictionary<string, object> Metrics { get; set; } = new Dictionary<string, object>();

            /// <summary>
            /// 返回诊断结果的可读描述
            /// </summary>
            /// <returns>健康状态或问题列表文本</returns>
            public override string ToString()
            {
                if (IsHealthy)
                    return "✅ 数据库健康";

                var issues = string.Join("\n", Issues.Select(i => string.Format("⚠️ {0}: {1}", i.Severity, i.Message)));
                return string.Format("❌ 数据库有问题:\n{0}", issues);
            }
        }

        /// <summary>
        /// 诊断问题
        /// </summary>
        public class DiagnosticIssue
        {
            /// <summary>
            /// 严重程度
            /// </summary>
            public DiagnosticSeverity Severity { get; set; }
            /// <summary>
            /// 问题描述
            /// </summary>
            public string Message { get; set; }
            /// <summary>
            /// 问题分类
            /// </summary>
            public string Category { get; set; }
            /// <summary>
            /// 问题详细信息
            /// </summary>
            public object Details { get; set; }
        }

        /// <summary>
        /// 诊断严重程度
        /// </summary>
        public enum DiagnosticSeverity
        {
            /// <summary>提示信息</summary>
            Info,
            /// <summary>警告</summary>
            Warning,
            /// <summary>错误</summary>
            Error,
            /// <summary>严重错误</summary>
            Critical
        }

        /// <summary>
        /// 执行完整的数据库诊断
        /// </summary>
        public static DiagnosticResult Diagnose(string key = null)
        {
            var result = new DiagnosticResult();

            try
            {
                // 1. 检查连接
                CheckConnection(result, key);

                // 2. 检查配置
                CheckConfiguration(result, key);

                // 3. 检查性能
                CheckPerformance(result, key);

                // 4. 检查索引
                CheckIndexes(result, key);

                // 5. 检查表结构
                CheckTables(result, key);

                // 6. 检查数据一致性
                CheckDataConsistency(result, key);

                // 判断整体健康状态
                result.IsHealthy = !result.Issues.Any(i => i.Severity == DiagnosticSeverity.Error || i.Severity == DiagnosticSeverity.Critical);
            }
            catch (Exception ex)
            {
                result.Issues.Add(new DiagnosticIssue
                {
                    Severity = DiagnosticSeverity.Critical,
                    Message = string.Format("诊断过程中发生异常: {0}", ex.Message),
                    Category = "Diagnostics",
                    Details = ex.StackTrace
                });
                result.IsHealthy = false;
            }

            return result;
        }

        /// <summary>
        /// 检查连接状态
        /// </summary>
        private static void CheckConnection(DiagnosticResult result, string key)
        {
            try
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                using var db = new DataContext(key ?? FastDb.CurrentKey);
                db.conn.Open();
                sw.Stop();

                if (sw.ElapsedMilliseconds > 1000)
                {
                    result.Issues.Add(new DiagnosticIssue
                    {
                        Severity = DiagnosticSeverity.Warning,
                        Message = string.Format("数据库连接较慢: {0}ms", sw.ElapsedMilliseconds),
                        Category = "Connection",
                        Details = sw.ElapsedMilliseconds
                    });
                }

                result.Metrics["ConnectionTime"] = sw.ElapsedMilliseconds;
                result.Metrics["ConnectionState"] = "Open";
            }
            catch (Exception ex)
            {
                result.Issues.Add(new DiagnosticIssue
                {
                    Severity = DiagnosticSeverity.Critical,
                    Message = string.Format("数据库连接失败: {0}", ex.Message),
                    Category = "Connection",
                    Details = ex
                });
                result.Metrics["ConnectionState"] = "Failed";
            }
        }

        /// <summary>
        /// 检查配置
        /// </summary>
        private static void CheckConfiguration(DiagnosticResult result, string key)
        {
            var config = DataConfig.GetConfig(key ?? FastDb.CurrentKey);
            if (config == null)
            {
                result.Issues.Add(new DiagnosticIssue
                {
                    Severity = DiagnosticSeverity.Critical,
                    Message = "数据库配置未找到",
                    Category = "Configuration"
                });
                return;
            }

            // 检查连接字符串
            if (string.IsNullOrWhiteSpace(config.ConnStr))
            {
                result.Issues.Add(new DiagnosticIssue
                {
                    Severity = DiagnosticSeverity.Error,
                    Message = "连接字符串为空",
                    Category = "Configuration"
                });
            }

            result.Metrics["DbType"] = config.DbType.ToString();
            result.Metrics["CacheType"] = config.CacheType;
        }

        /// <summary>
        /// 检查性能
        /// </summary>
        private static void CheckPerformance(DiagnosticResult result, string key)
        {
            try
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();

                // 执行简单查询测试性能
                var testQuery = "SELECT 1";
                using var db = new DataContext(key ?? FastDb.CurrentKey);
                db.cmd.CommandText = testQuery;
                db.cmd.ExecuteScalar();

                sw.Stop();

                if (sw.ElapsedMilliseconds > 100)
                {
                    result.Issues.Add(new DiagnosticIssue
                    {
                        Severity = DiagnosticSeverity.Warning,
                        Message = string.Format("数据库响应较慢: {0}ms", sw.ElapsedMilliseconds),
                        Category = "Performance",
                        Details = sw.ElapsedMilliseconds
                    });
                }

                result.Metrics["QueryPerformance"] = sw.ElapsedMilliseconds;

                // 检查连接池
                result.Metrics["ConnectionPoolSize"] = "Unknown"; // 需要实际的连接池统计
            }
            catch (Exception ex)
            {
                result.Issues.Add(new DiagnosticIssue
                {
                    Severity = DiagnosticSeverity.Error,
                    Message = string.Format("性能检查失败: {0}", ex.Message),
                    Category = "Performance"
                });
            }
        }

        /// <summary>
        /// 检查索引
        /// </summary>
        private static void CheckIndexes(DiagnosticResult result, string key)
        {
            try
            {
                var config = DataConfig.GetConfig(key ?? FastDb.CurrentKey);
                if (config == null) return;

                var sql = config.DbType switch
                {
                    DataDbType.SqlServer => @"
                        SELECT 
                            t.name as TableName,
                            i.name as IndexName,
                            i.type_desc as IndexType
                        FROM sys.tables t
                        INNER JOIN sys.indexes i ON t.object_id = i.object_id
                        WHERE i.is_primary_key = 0
                        ORDER BY t.name, i.name",
                    DataDbType.MySql => @"
                        SELECT 
                            TABLE_NAME as TableName,
                            INDEX_NAME as IndexName,
                            INDEX_TYPE as IndexType
                        FROM information_schema.statistics
                        WHERE INDEX_NAME != 'PRIMARY'",
                    DataDbType.PostgreSql => @"
                        SELECT 
                            tablename as TableName,
                            indexname as IndexName,
                            indexdef as IndexType
                        FROM pg_indexes
                        WHERE indexname != '{table}_pkey'",
                    _ => null
                };

                if (sql == null) return;

                using var db = new DataContext(key);
                db.cmd.CommandText = sql;
                using var reader = db.cmd.ExecuteReader();

                var indexCount = 0;
                var tableIndexes = new Dictionary<string, int>();

                while (reader.Read())
                {
                    indexCount++;
                    var tableName = reader["TableName"].ToString();
                    tableIndexes[tableName] = tableIndexes.GetValueOrDefault(tableName, 0) + 1;
                }

                result.Metrics["TotalIndexes"] = indexCount;
                result.Metrics["TablesWithIndexes"] = tableIndexes.Count;

                // 检查缺少索引的表
                var tablesWithoutIndexes = tableIndexes.Where(kvp => kvp.Value == 0).Select(kvp => kvp.Key).ToList();
                if (tablesWithoutIndexes.Any())
                {
                    result.Issues.Add(new DiagnosticIssue
                    {
                        Severity = DiagnosticSeverity.Info,
                        Message = string.Format("{0} 个表可能缺少索引: {1}", tablesWithoutIndexes.Count, string.Join(", ", tablesWithoutIndexes)),
                        Category = "Index",
                        Details = tablesWithoutIndexes
                    });
                }
            }
            catch (Exception ex)
            {
                result.Issues.Add(new DiagnosticIssue
                {
                    Severity = DiagnosticSeverity.Warning,
                    Message = string.Format("索引检查失败: {0}", ex.Message),
                    Category = "Index"
                });
            }
        }

        /// <summary>
        /// 检查表结构
        /// </summary>
        private static void CheckTables(DiagnosticResult result, string key)
        {
            try
            {
                var config = DataConfig.GetConfig(key ?? FastDb.CurrentKey);
                if (config == null) return;

                var sql = config.DbType switch
                {
                    DataDbType.SqlServer => "SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES",
                    DataDbType.MySql => "SELECT COUNT(*) FROM information_schema.tables",
                    DataDbType.PostgreSql => "SELECT COUNT(*) FROM information_schema.tables",
                    DataDbType.Oracle => "SELECT COUNT(*) FROM user_tables",
                    DataDbType.SQLite => "SELECT COUNT(*) FROM sqlite_master WHERE type='table'",
                    _ => null
                };

                if (sql == null) return;

                using var db = new DataContext(key);
                db.cmd.CommandText = sql;
                var result_count = Convert.ToInt32(db.cmd.ExecuteScalar());

                result.Metrics["TableCount"] = result_count;

                if (result_count == 0)
                {
                    result.Issues.Add(new DiagnosticIssue
                    {
                        Severity = DiagnosticSeverity.Warning,
                        Message = "数据库中没有任何表",
                        Category = "Schema"
                    });
                }
            }
            catch (Exception ex)
            {
                result.Issues.Add(new DiagnosticIssue
                {
                    Severity = DiagnosticSeverity.Warning,
                    Message = string.Format("表结构检查失败: {0}", ex.Message),
                    Category = "Schema"
                });
            }
        }

        /// <summary>
        /// 检查数据一致性
        /// </summary>
        private static void CheckDataConsistency(DiagnosticResult result, string key)
        {
            try
            {
                using var db = new DataContext(key);

                // 检查常见的表是否存在
                var tablesToCheck = new[] { "sys_user", "User", "users" };
                var existingTables = new List<string>();

                foreach (var table in tablesToCheck)
                {
                    try
                    {
                        db.cmd.CommandText = string.Format("SELECT COUNT(*) FROM {0}", table);
                        var count = Convert.ToInt32(db.cmd.ExecuteScalar());
                        
                        if (count > 0)
                        {
                            existingTables.Add(string.Format("{0}({1}行)", table, count));
                        }
                    }
                    catch
                    {
                        // 表不存在，忽略
                    }
                }

                if (existingTables.Any())
                {
                    result.Metrics["UserTables"] = string.Join(", ", existingTables);
                }
            }
            catch (Exception ex)
            {
                result.Issues.Add(new DiagnosticIssue
                {
                    Severity = DiagnosticSeverity.Info,
                    Message = string.Format("数据一致性检查跳过: {0}", ex.Message),
                    Category = "Consistency"
                });
            }
        }

        /// <summary>
        /// 生成诊断报告
        /// </summary>
        public static string GenerateDiagnosticReport(DiagnosticResult result)
        {
            var sb = new System.Text.StringBuilder();

            sb.AppendLine("=== 数据库诊断报告 ===");
            sb.AppendLine(string.Format("诊断时间: {0:yyyy-MM-dd HH:mm:ss}", DateTime.Now));
            sb.AppendLine(string.Format("健康状态: {0}", (result.IsHealthy ? "✅ 健康" : "❌ 有问题")));
            sb.AppendLine();

            if (result.Metrics.Any())
            {
                sb.AppendLine("=== 指标统计 ===");
                foreach (var metric in result.Metrics)
                {
                    sb.AppendLine(string.Format("{0}: {1}", metric.Key, metric.Value));
                }
                sb.AppendLine();
            }

            if (result.Issues.Any())
            {
                sb.AppendLine("=== 发现的问题 ===");
                foreach (var issue in result.Issues)
                {
                    var icon = issue.Severity switch
                    {
                        DiagnosticSeverity.Info => "ℹ️",
                        DiagnosticSeverity.Warning => "⚠️",
                        DiagnosticSeverity.Error => "❌",
                        DiagnosticSeverity.Critical => "🚨",
                        _ => "❓"
                    };

                    sb.AppendLine(string.Format("{0} [{1}] {2}", icon, issue.Severity, issue.Message));
                    if (issue.Details != null)
                    {
                        sb.AppendLine(string.Format("   详情: {0}", issue.Details));
                    }
                }
                sb.AppendLine();
            }

            if (result.IsHealthy)
            {
                sb.AppendLine("✅ 数据库运行正常，无需操作。");
            }
            else
            {
                sb.AppendLine("❌ 发现问题，建议进行优化。");
                sb.AppendLine();
                sb.AppendLine("=== 建议操作 ===");
                foreach (var issue in result.Issues.Where(i => i.Severity != DiagnosticSeverity.Info))
                {
                    sb.AppendLine(string.Format("• {0}", GetSuggestion(issue)));
                }
            }

            return sb.ToString();
        }

        /// <summary>
        /// 获取改进建议
        /// </summary>
        private static string GetSuggestion(DiagnosticIssue issue)
        {
            return issue.Category switch
            {
                "Connection" => "检查数据库服务器状态和网络连接",
                "Configuration" => "验证数据库配置文件",
                "Performance" => "考虑添加索引或优化查询",
                "Index" => "为常用查询字段添加索引",
                "Schema" => "创建必要的数据库表",
                _ => "请联系数据库管理员"
            };
        }
    }
}