using System;
using FastData.Context;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Text;
using FastData.Config;
using FastData.DbTypes;

namespace FastData.DevTools
{
    /// <summary>
    /// 数据库比较和同步工具
    /// </summary>
    public static class DatabaseComparer
    {
        /// <summary>
        /// 比较两个数据库的差异
        /// </summary>
        public static DatabaseDiff CompareDatabases(string sourceKey, string targetKey)
        {
            var diff = new DatabaseDiff();

            try
            {
                // 比较表
                diff.TableDifferences = CompareTables(sourceKey, targetKey);

                // 比较列
                diff.ColumnDifferences = CompareColumns(sourceKey, targetKey);

                // 比较数据
                diff.DataDifferences = CompareData(sourceKey, targetKey);

                // 比较索引
                diff.IndexDifferences = CompareIndexes(sourceKey, targetKey);

                diff.HasDifferences = diff.TableDifferences.Any() || 
                                         diff.ColumnDifferences.Any() || 
                                         diff.DataDifferences.Any() ||
                                         diff.IndexDifferences.Any();
            }
            catch (Exception ex)
            {
                diff.Error = ex.Message;
                diff.HasDifferences = false;
            }

            return diff;
        }

        /// <summary>
        /// 生成同步 SQL 脚本
        /// </summary>
        public static string GenerateSyncScript(DatabaseDiff diff)
        {
            var sql = new StringBuilder();

            sql.AppendLine("-- 数据库同步脚本");
            sql.AppendLine(string.Format("-- 生成时间: {0:yyyy-MM-dd HH:mm:ss}", DateTime.Now));
            sql.AppendLine("-- 请在执行前仔细检查！");
            sql.AppendLine();

            // 生成表创建脚本
            if (diff.TableDifferences.Any())
            {
                sql.AppendLine("-- 表差异同步");
                foreach (var tableDiff in diff.TableDifferences.Where(d => d.Action == DiffAction.Create))
                {
                    sql.AppendLine(string.Format("-- 创建表: {0}", tableDiff.TableName));
                    // sql.AppendLine(string.Format("CREATE TABLE {0} (/* 列定义 */);", tableDiff.TableName));
                }

                foreach (var tableDiff in diff.TableDifferences.Where(d => d.Action == DiffAction.Drop))
                {
                    sql.AppendLine(string.Format("-- 删除表: {0}", tableDiff.TableName));
                    sql.AppendLine(string.Format("DROP TABLE {0};", tableDiff.TableName));
                }

                sql.AppendLine();
            }

            // 生成列修改脚本
            if (diff.ColumnDifferences.Any())
            {
                sql.AppendLine("-- 列差异同步");
                foreach (var colDiff in diff.ColumnDifferences.Where(d => d.Action == DiffAction.Add))
                {
                    sql.AppendLine(string.Format("-- 添加列: {0}.{1}", colDiff.TableName, colDiff.ColumnName));
                    // sql.AppendLine(string.Format("ALTER TABLE {0} ADD {1} {2};", colDiff.TableName, colDiff.ColumnName, colDiff.DataType));
                }

                foreach (var colDiff in diff.ColumnDifferences.Where(d => d.Action == DiffAction.Drop))
                {
                    sql.AppendLine(string.Format("-- 删除列: {0}.{1}", colDiff.TableName, colDiff.ColumnName));
                    sql.AppendLine(string.Format("ALTER TABLE {0} DROP COLUMN {1};", colDiff.TableName, colDiff.ColumnName));
                }

                sql.AppendLine();
            }

            if (sql.Length == 0)
            {
                sql.AppendLine("-- 无差异，无需同步");
            }

            return sql.ToString();
        }

        /// <summary>
        /// 比较表
        /// </summary>
        private static List<TableDifference> CompareTables(string sourceKey, string targetKey)
        {
            var differences = new List<TableDifference>();
            var sourceTables = GetTableNames(sourceKey);
            var targetTables = GetTableNames(targetKey);

            // 查找只在源数据库中的表
            foreach (var table in sourceTables.Except(targetTables))
            {
                differences.Add(new TableDifference
                {
                    TableName = table,
                    Action = DiffAction.Create
                });
            }

            // 查找只在目标数据库中的表
            foreach (var table in targetTables.Except(sourceTables))
            {
                differences.Add(new TableDifference
                {
                    TableName = table,
                    Action = DiffAction.Drop
                });
            }

            return differences;
        }

        /// <summary>
        /// 比较列
        /// </summary>
        private static List<ColumnDifference> CompareColumns(string sourceKey, string targetKey)
        {
            var differences = new List<ColumnDifference>();
            var commonTables = GetTableNames(sourceKey).Intersect(GetTableNames(targetKey));

            foreach (var table in commonTables)
            {
                var sourceColumns = GetColumnNames(table, sourceKey);
                var targetColumns = GetColumnNames(table, targetKey);

                // 查找只在源数据库中的列
                foreach (var column in sourceColumns.Except(targetColumns))
                {
                    differences.Add(new ColumnDifference
                    {
                        TableName = table,
                        ColumnName = column,
                        Action = DiffAction.Add
                    });
                }

                // 查找只在目标数据库中的列
                foreach (var column in targetColumns.Except(sourceColumns))
                {
                    differences.Add(new ColumnDifference
                    {
                        TableName = table,
                        ColumnName = column,
                        Action = DiffAction.Drop
                    });
                }
            }

            return differences;
        }

        /// <summary>
        /// 比较数据
        /// </summary>
        private static List<DataDifference> CompareData(string sourceKey, string targetKey)
        {
            var differences = new List<DataDifference>();
            var commonTables = GetTableNames(sourceKey).Intersect(GetTableNames(targetKey));

            foreach (var table in commonTables)
            {
                var sourceCount = GetTableRowCount(table, sourceKey);
                var targetCount = GetTableRowCount(table, targetKey);

                if (sourceCount != targetCount)
                {
                    differences.Add(new DataDifference
                    {
                        TableName = table,
                        SourceCount = sourceCount,
                        TargetCount = targetCount,
                        Difference = sourceCount - targetCount
                    });
                }
            }

            return differences;
        }

        /// <summary>
        /// 比较索引
        /// </summary>
        private static List<IndexDifference> CompareIndexes(string sourceKey, string targetKey)
        {
            var differences = new List<IndexDifference>();
            var sourceIndexes = GetIndexNames(sourceKey);
            var targetIndexes = GetIndexNames(targetKey);

            // 查找只在源数据库中的索引
            foreach (var index in sourceIndexes.Except(targetIndexes))
            {
                differences.Add(new IndexDifference
                {
                    IndexName = index,
                    Action = DiffAction.Create
                });
            }

            // 查找只在目标数据库中的索引
            foreach (var index in targetIndexes.Except(sourceIndexes))
            {
                differences.Add(new IndexDifference
                {
                    IndexName = index,
                    Action = DiffAction.Drop
                });
            }

            return differences;
        }

        /// <summary>
        /// 获取表名列表
        /// </summary>
        private static HashSet<string> GetTableNames(string key)
        {
            var tables = new HashSet<string>();
            var config = DataConfig.GetConfig(key);
            if (config == null) return tables;

            var sql = config.DbType switch
            {
                DataDbType.SqlServer => "SELECT TABLE_NAME FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_TYPE = 'BASE TABLE'",
                DataDbType.MySql => "SELECT TABLE_NAME FROM information_schema.tables WHERE TABLE_TYPE = 'BASE TABLE'",
                DataDbType.PostgreSql => "SELECT tablename FROM pg_tables WHERE schemaname = 'public'",
                DataDbType.Oracle => "SELECT table_name FROM user_tables",
                DataDbType.SQLite => "SELECT name FROM sqlite_master WHERE type='table'",
                _ => null
            };

            if (sql == null) return tables;

            try
            {
                using var db = new DataContext(key);
                db.cmd.CommandText = sql;
                using var reader = db.cmd.ExecuteReader();
                while (reader.Read())
                {
                    tables.Add(reader[0].ToString());
                }
            }
            catch
            {
                // 忽略错误
            }

            return tables;
        }

        /// <summary>
        /// 获取列名列表
        /// </summary>
        private static HashSet<string> GetColumnNames(string tableName, string key)
        {
            var columns = new HashSet<string>();
            var config = DataConfig.GetConfig(key);
            if (config == null) return columns;

            var sql = config.DbType switch
            {
                DataDbType.SqlServer => string.Format("SELECT COLUMN_NAME FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = '{0}'", tableName),
                DataDbType.MySql => string.Format("SELECT column_name FROM information_schema.columns WHERE table_name = '{0}'", tableName),
                DataDbType.PostgreSql => string.Format("SELECT column_name FROM information_schema.columns WHERE table_name = '{0}'", tableName),
                DataDbType.Oracle => string.Format("SELECT column_name FROM user_tab_columns WHERE table_name = UPPER('{0}')", tableName),
                DataDbType.SQLite => string.Format("PRAGMA table_info({0})", tableName),
                _ => null
            };

            if (sql == null) return columns;

            try
            {
                using var db = new DataContext(key);
                db.cmd.CommandText = sql;

                if (config.DbType == DataDbType.SQLite)
                {
                    using var reader = db.cmd.ExecuteReader();
                    while (reader.Read())
                    {
                        columns.Add(reader["name"].ToString());
                    }
                }
                else
                {
                    using var reader = db.cmd.ExecuteReader();
                    while (reader.Read())
                    {
                        columns.Add(reader[0].ToString());
                    }
                }
            }
            catch
            {
                // 忽略错误
            }

            return columns;
        }

        /// <summary>
        /// 获取表行数
        /// </summary>
        private static int GetTableRowCount(string tableName, string key)
        {
            try
            {
                var sql = string.Format("SELECT COUNT(*) FROM {0}", tableName);
                using var db = new DataContext(key);
                db.cmd.CommandText = sql;
                return Convert.ToInt32(db.cmd.ExecuteScalar());
            }
            catch
            {
                return 0;
            }
        }

        /// <summary>
        /// 获取索引名列表
        /// </summary>
        private static HashSet<string> GetIndexNames(string key)
        {
            var indexes = new HashSet<string>();
            var config = DataConfig.GetConfig(key);
            if (config == null) return indexes;

            var sql = config.DbType switch
            {
                DataDbType.SqlServer => "SELECT name FROM sys.indexes WHERE object_id IN (SELECT object_id FROM sys.tables)",
                DataDbType.MySql => "SELECT DISTINCT index_name FROM information_schema.statistics WHERE table_schema = DATABASE()",
                DataDbType.PostgreSql => "SELECT indexname FROM pg_indexes WHERE schemaname = 'public'",
                DataDbType.Oracle => "SELECT index_name FROM user_indexes WHERE table_name IN (SELECT table_name FROM user_tables)",
                DataDbType.SQLite => "SELECT name FROM sqlite_master WHERE type='index'",
                _ => null
            };

            if (sql == null) return indexes;

            try
            {
                using var db = new DataContext(key);
                db.cmd.CommandText = sql;
                using var reader = db.cmd.ExecuteReader();
                while (reader.Read())
                {
                    indexes.Add(reader[0].ToString());
                }
            }
            catch
            {
                // 忽略错误
            }

            return indexes;
        }
    }

    /// <summary>
    /// 数据库差异
    /// </summary>
    public class DatabaseDiff
    {
        /// <summary>
        /// 是否存在差异
        /// </summary>
        public bool HasDifferences { get; set; }
        /// <summary>
        /// 表差异列表
        /// </summary>
        public List<TableDifference> TableDifferences { get; set; } = new List<TableDifference>();
        /// <summary>
        /// 列差异列表
        /// </summary>
        public List<ColumnDifference> ColumnDifferences { get; set; } = new List<ColumnDifference>();
        /// <summary>
        /// 数据差异列表
        /// </summary>
        public List<DataDifference> DataDifferences { get; set; } = new List<DataDifference>();
        /// <summary>
        /// 索引差异列表
        /// </summary>
        public List<IndexDifference> IndexDifferences { get; set; } = new List<IndexDifference>();
        /// <summary>
        /// 比较过程中的错误信息
        /// </summary>
        public string Error { get; set; }
    }

    /// <summary>
    /// 表差异
    /// </summary>
    public class TableDifference
    {
        /// <summary>
        /// 表名
        /// </summary>
        public string TableName { get; set; }
        /// <summary>
        /// 差异操作
        /// </summary>
        public DiffAction Action { get; set; }
    }

    /// <summary>
    /// 列差异
    /// </summary>
    public class ColumnDifference
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
        /// 差异操作
        /// </summary>
        public DiffAction Action { get; set; }
        /// <summary>
        /// 列数据类型
        /// </summary>
        public string DataType { get; set; }
    }

    /// <summary>
    /// 数据差异
    /// </summary>
    public class DataDifference
    {
        /// <summary>
        /// 表名
        /// </summary>
        public string TableName { get; set; }
        /// <summary>
        /// 源表行数
        /// </summary>
        public int SourceCount { get; set; }
        /// <summary>
        /// 目标表行数
        /// </summary>
        public int TargetCount { get; set; }
        /// <summary>
        /// 行数差值（源 - 目标）
        /// </summary>
        public int Difference { get; set; }
    }

    /// <summary>
    /// 索引差异
    /// </summary>
    public class IndexDifference
    {
        /// <summary>
        /// 索引名
        /// </summary>
        public string IndexName { get; set; }
        /// <summary>
        /// 差异操作
        /// </summary>
        public DiffAction Action { get; set; }
    }

    /// <summary>
    /// 差异操作
    /// </summary>
    public enum DiffAction
    {
        /// <summary>创建</summary>
        Create,
        /// <summary>删除</summary>
        Drop,
        /// <summary>修改</summary>
        Modify,
        /// <summary>新增</summary>
        Add
    }
}