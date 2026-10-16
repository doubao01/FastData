using System;
using FastData.Context;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Text;
using FastData.Config;
using FastData.Model;
using FastData.DbTypes;

namespace FastData.DevTools
{
    /// <summary>
    /// 数据库备份恢复工具
    /// </summary>
    public static class DatabaseBackupRestore
    {
        /// <summary>
        /// 创建数据库备份
        /// </summary>
        public static BackupResult CreateBackup(string dbKey, string backupPath, BackupOptions options = null)
        {
            var result = new BackupResult { Success = false, StartTime = DateTime.Now };

            try
            {
                options = options ?? new BackupOptions();

                var config = DataConfig.GetConfig(dbKey);
                if (config == null)
                {
                    result.ErrorMessage = string.Format("找不到数据库配置: {0}", dbKey);
                    return result;
                }

                result.DatabaseName = ExtractDatabaseName(config.ConnStr);

                switch (config.DbType)
                {
                    case DataDbType.MySql:
                        result = BackupMySql(dbKey, config, backupPath, options);
                        break;

                    case DataDbType.SqlServer:
                        result = BackupSqlServer(dbKey, config, backupPath, options);
                        break;

                    case DataDbType.PostgreSql:
                        result = BackupPostgreSql(dbKey, config, backupPath, options);
                        break;

                    case DataDbType.SQLite:
                        result = BackupSQLite(dbKey, config, backupPath, options);
                        break;

                    default:
                        result.ErrorMessage = string.Format("不支持的数据库类型: {0}", config.DbType);
                        break;
                }

                if (result.Success)
                {
                    result.EndTime = DateTime.Now;
                    result.Duration = result.EndTime - result.StartTime;
                    result.BackupPath = backupPath;
                }
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = string.Format("备份失败: {0}", ex.Message);
            }

            return result;
        }

        /// <summary>
        /// 从备份恢复数据库
        /// </summary>
        public static RestoreResult RestoreBackup(string dbKey, string backupPath, RestoreOptions options = null)
        {
            var result = new RestoreResult { Success = false, StartTime = DateTime.Now };

            try
            {
                options = options ?? new RestoreOptions();

                var config = DataConfig.GetConfig(dbKey);
                if (config == null)
                {
                    result.ErrorMessage = string.Format("找不到数据库配置: {0}", dbKey);
                    return result;
                }

                if (!File.Exists(backupPath))
                {
                    result.ErrorMessage = string.Format("备份文件不存在: {0}", backupPath);
                    return result;
                }

                result.BackupPath = backupPath;

                switch (config.DbType)
                {
                    case DataDbType.MySql:
                        result = RestoreMySql(dbKey, config, backupPath, options);
                        break;

                    case DataDbType.SqlServer:
                        result = RestoreSqlServer(dbKey, config, backupPath, options);
                        break;

                    case DataDbType.PostgreSql:
                        result = RestorePostgreSql(dbKey, config, backupPath, options);
                        break;

                    case DataDbType.SQLite:
                        result = RestoreSQLite(dbKey, config, backupPath, options);
                        break;

                    default:
                        result.ErrorMessage = string.Format("不支持的数据库类型: {0}", config.DbType);
                        break;
                }

                if (result.Success)
                {
                    result.EndTime = DateTime.Now;
                    result.Duration = result.EndTime - result.StartTime;
                }
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = string.Format("恢复失败: {0}", ex.Message);
            }

            return result;
        }

        /// <summary>
        /// 获取备份文件列表
        /// </summary>
        public static List<BackupFileInfo> GetBackupFiles(string backupDirectory)
        {
            var backups = new List<BackupFileInfo>();

            if (!Directory.Exists(backupDirectory))
            {
                return backups;
            }

            var files = Directory.GetFiles(backupDirectory, "*.bak", SearchOption.TopDirectoryOnly)
                .Concat(Directory.GetFiles(backupDirectory, "*.sql", SearchOption.TopDirectoryOnly))
                .Concat(Directory.GetFiles(backupDirectory, "*.db", SearchOption.TopDirectoryOnly))
                .Concat(Directory.GetFiles(backupDirectory, "*.sqlite", SearchOption.TopDirectoryOnly));

            foreach (var file in files)
            {
                var fileInfo = new FileInfo(file);
                backups.Add(new BackupFileInfo
                {
                    FileName = fileInfo.Name,
                    FilePath = file,
                    Size = fileInfo.Length,
                    CreatedAt = fileInfo.CreationTime,
                    ModifiedAt = fileInfo.LastWriteTime
                });
            }

            return backups.OrderByDescending(b => b.ModifiedAt).ToList();
        }

        /// <summary>
        /// 删除过期的备份
        /// </summary>
        public static int DeleteExpiredBackups(string backupDirectory, int retentionDays)
        {
            var cutoffDate = DateTime.Now.AddDays(-retentionDays);
            var deletedCount = 0;

            var backups = GetBackupFiles(backupDirectory);
            foreach (var backup in backups.Where(b => b.CreatedAt < cutoffDate))
            {
                try
                {
                    File.Delete(backup.FilePath);
                    deletedCount++;
                }
                catch
                {
                    // 忽略删除失败
                }
            }

            return deletedCount;
        }

        #region 私有方法

        private static BackupResult BackupMySql(string dbKey, ConfigModel config, string backupPath, BackupOptions options)
        {
            var result = new BackupResult { Success = false };

            try
            {
                using var db = new DataContext(dbKey);

                var tables = GetTables(dbKey);
                var sql = new StringBuilder();

                foreach (var table in tables)
                {
                    if (options.IncludeData)
                    {
                        sql.AppendLine(string.Format("-- 表结构: {0}", table));
                        sql.AppendLine(GetTableSchema(dbKey, table));
                        sql.AppendLine();

                        if (options.IncludeData)
                        {
                            sql.AppendLine(string.Format("-- 数据: {0}", table));
                            sql.AppendLine(GetTableData(dbKey, table, options.BatchSize));
                            sql.AppendLine();
                        }
                    }
                }

                File.WriteAllText(backupPath, sql.ToString(), Encoding.UTF8);
                result.Success = true;
            }
            catch (Exception ex)
            {
                result.ErrorMessage = string.Format("MySQL 备份失败: {0}", ex.Message);
            }

            return result;
        }

        private static BackupResult BackupSqlServer(string dbKey, ConfigModel config, string backupPath, BackupOptions options)
        {
            var result = new BackupResult { Success = false };

            try
            {
                using var db = new DataContext(dbKey);

                var tables = GetTables(dbKey);
                var sql = new StringBuilder();

                foreach (var table in tables)
                {
                    sql.AppendLine(string.Format("-- 表结构: {0}", table));
                    sql.AppendLine(GetTableSchema(dbKey, table));
                    sql.AppendLine();

                    if (options.IncludeData)
                    {
                        sql.AppendLine(string.Format("-- 数据: {0}", table));
                        sql.AppendLine(GetTableData(dbKey, table, options.BatchSize));
                        sql.AppendLine();
                    }
                }

                File.WriteAllText(backupPath, sql.ToString(), Encoding.UTF8);
                result.Success = true;
            }
            catch (Exception ex)
            {
                result.ErrorMessage = string.Format("SQL Server 备份失败: {0}", ex.Message);
            }

            return result;
        }

        private static BackupResult BackupPostgreSql(string dbKey, ConfigModel config, string backupPath, BackupOptions options)
        {
            var result = new BackupResult { Success = false };

            try
            {
                using var db = new DataContext(dbKey);

                var tables = GetTables(dbKey);
                var sql = new StringBuilder();

                foreach (var table in tables)
                {
                    sql.AppendLine(string.Format("-- 表结构: {0}", table));
                    sql.AppendLine(GetTableSchema(dbKey, table));
                    sql.AppendLine();

                    if (options.IncludeData)
                    {
                        sql.AppendLine(string.Format("-- 数据: {0}", table));
                        sql.AppendLine(GetTableData(dbKey, table, options.BatchSize));
                        sql.AppendLine();
                    }
                }

                File.WriteAllText(backupPath, sql.ToString(), Encoding.UTF8);
                result.Success = true;
            }
            catch (Exception ex)
            {
                result.ErrorMessage = string.Format("PostgreSQL 备份失败: {0}", ex.Message);
            }

            return result;
        }

        private static BackupResult BackupSQLite(string dbKey, ConfigModel config, string backupPath, BackupOptions options)
        {
            var result = new BackupResult { Success = false };

            try
            {
                // SQLite 直接复制数据库文件
                var dbPath = ExtractDatabasePath(config.ConnStr);
                if (File.Exists(dbPath))
                {
                    File.Copy(dbPath, backupPath, true);
                    result.Success = true;
                }
                else
                {
                    result.ErrorMessage = string.Format("SQLite 数据库文件不存在: {0}", dbPath);
                }
            }
            catch (Exception ex)
            {
                result.ErrorMessage = string.Format("SQLite 备份失败: {0}", ex.Message);
            }

            return result;
        }

        private static RestoreResult RestoreMySql(string dbKey, ConfigModel config, string backupPath, RestoreOptions options)
        {
            var result = new RestoreResult { Success = false };

            try
            {
                var sql = File.ReadAllText(backupPath, Encoding.UTF8);
                var statements = ParseSqlStatements(sql);

                using var db = new DataContext(dbKey);

                if (options.DropExisting)
                {
                    var tables = GetTables(dbKey);
                    foreach (var table in tables)
                    {
                        try
                        {
                            db.cmd.CommandText = string.Format("DROP TABLE IF EXISTS {0}", table);
                            db.cmd.ExecuteNonQuery();
                        }
                        catch { }
                    }
                }

                foreach (var statement in statements)
                {
                    if (!string.IsNullOrWhiteSpace(statement) && !statement.Trim().StartsWith("--"))
                    {
                        try
                        {
                            db.cmd.CommandText = statement;
                            db.cmd.ExecuteNonQuery();
                        }
                        catch { }
                    }
                }

                result.Success = true;
            }
            catch (Exception ex)
            {
                result.ErrorMessage = string.Format("MySQL 恢复失败: {0}", ex.Message);
            }

            return result;
        }

        private static RestoreResult RestoreSqlServer(string dbKey, ConfigModel config, string backupPath, RestoreOptions options)
        {
            var result = new RestoreResult { Success = false };

            try
            {
                var sql = File.ReadAllText(backupPath, Encoding.UTF8);
                var statements = ParseSqlStatements(sql);

                using var db = new DataContext(dbKey);

                if (options.DropExisting)
                {
                    var tables = GetTables(dbKey);
                    foreach (var table in tables)
                    {
                        try
                        {
                            db.cmd.CommandText = string.Format("DROP TABLE IF EXISTS {0}", table);
                            db.cmd.ExecuteNonQuery();
                        }
                        catch { }
                    }
                }

                foreach (var statement in statements)
                {
                    if (!string.IsNullOrWhiteSpace(statement) && !statement.Trim().StartsWith("--"))
                    {
                        try
                        {
                            db.cmd.CommandText = statement;
                            db.cmd.ExecuteNonQuery();
                        }
                        catch { }
                    }
                }

                result.Success = true;
            }
            catch (Exception ex)
            {
                result.ErrorMessage = string.Format("SQL Server 恢复失败: {0}", ex.Message);
            }

            return result;
        }

        private static RestoreResult RestorePostgreSql(string dbKey, ConfigModel config, string backupPath, RestoreOptions options)
        {
            var result = new RestoreResult { Success = false };

            try
            {
                var sql = File.ReadAllText(backupPath, Encoding.UTF8);
                var statements = ParseSqlStatements(sql);

                using var db = new DataContext(dbKey);

                if (options.DropExisting)
                {
                    var tables = GetTables(dbKey);
                    foreach (var table in tables)
                    {
                        try
                        {
                            db.cmd.CommandText = string.Format("DROP TABLE IF EXISTS {0}", table);
                            db.cmd.ExecuteNonQuery();
                        }
                        catch { }
                    }
                }

                foreach (var statement in statements)
                {
                    if (!string.IsNullOrWhiteSpace(statement) && !statement.Trim().StartsWith("--"))
                    {
                        try
                        {
                            db.cmd.CommandText = statement;
                            db.cmd.ExecuteNonQuery();
                        }
                        catch { }
                    }
                }

                result.Success = true;
            }
            catch (Exception ex)
            {
                result.ErrorMessage = string.Format("PostgreSQL 恢复失败: {0}", ex.Message);
            }

            return result;
        }

        private static RestoreResult RestoreSQLite(string dbKey, ConfigModel config, string backupPath, RestoreOptions options)
        {
            var result = new RestoreResult { Success = false };

            try
            {
                var dbPath = ExtractDatabasePath(config.ConnStr);

                if (options.DropExisting && File.Exists(dbPath))
                {
                    File.Delete(dbPath);
                }

                File.Copy(backupPath, dbPath, true);
                result.Success = true;
            }
            catch (Exception ex)
            {
                result.ErrorMessage = string.Format("SQLite 恢复失败: {0}", ex.Message);
            }

            return result;
        }

        private static List<string> GetTables(string dbKey)
        {
            var tables = new List<string>();

            try
            {
                using var db = new DataContext(dbKey);
                db.cmd.CommandText = @"
                    SELECT TABLE_NAME FROM INFORMATION_SCHEMA.TABLES
                    WHERE TABLE_TYPE = 'BASE TABLE'
                    ORDER BY TABLE_NAME";

                using var reader = db.cmd.ExecuteReader();
                while (reader.Read())
                {
                    tables.Add(reader[0].ToString());
                }
            }
            catch { }

            return tables;
        }

        private static string GetTableSchema(string dbKey, string tableName)
        {
            var schema = new StringBuilder();

            try
            {
                using var db = new DataContext(dbKey);
                db.cmd.CommandText = string.Format(@"
                    SELECT COLUMN_NAME, DATA_TYPE, IS_NULLABLE, COLUMN_DEFAULT
                    FROM INFORMATION_SCHEMA.COLUMNS
                    WHERE TABLE_NAME = '{0}'
                    ORDER BY ORDINAL_POSITION", tableName);

                var columns = new List<string>();

                using var reader = db.cmd.ExecuteReader();
                while (reader.Read())
                {
                    var columnName = reader[0].ToString();
                    var dataType = reader[1].ToString();
                    var isNullable = reader[2].ToString() == "YES";
                    var defaultValue = reader[3]?.ToString();

                    var columnDef = string.Format("{0} {1}", columnName, dataType);
                    if (!isNullable) columnDef += " NOT NULL";
                    if (!string.IsNullOrEmpty(defaultValue)) columnDef += string.Format(" DEFAULT {0}", defaultValue);

                    columns.Add(columnDef);
                }

                schema.AppendLine(string.Format("CREATE TABLE {0} (", tableName));
                schema.AppendLine(string.Join(",\n", columns));
                schema.AppendLine(");");
            }
            catch { }

            return schema.ToString();
        }

        private static string GetTableData(string dbKey, string tableName, int batchSize)
        {
            var data = new StringBuilder();

            try
            {
                using var db = new DataContext(dbKey);
                db.cmd.CommandText = string.Format("SELECT * FROM {0}", tableName);

                using var reader = db.cmd.ExecuteReader();
                var columns = new List<string>();

                for (int i = 0; i < reader.FieldCount; i++)
                {
                    columns.Add(reader.GetName(i));
                }

                var rowCount = 0;
                while (reader.Read())
                {
                    var values = columns.Select(c =>
                    {
                        var value = reader[c];
                        if (value == DBNull.Value)
                            return "NULL";
                        else if (value is string str)
                            return string.Format("'{0}'", str.Replace("'", "''"));
                        else if (value is DateTime dt)
                            return string.Format("'{0:yyyy-MM-dd HH:mm:ss}'", dt);
                        else if (value is bool b)
                            return b ? "1" : "0";
                        else
                            return value.ToString();
                    });

                    data.AppendLine(string.Format("INSERT INTO {0} ({1}) VALUES ({2});", tableName, string.Join(", ", columns), string.Join(", ", values)));

                    rowCount++;
                    if (batchSize > 0 && rowCount % batchSize == 0)
                    {
                        data.AppendLine();
                    }
                }
            }
            catch { }

            return data.ToString();
        }

        private static List<string> ParseSqlStatements(string sql)
        {
            var statements = new List<string>();
            var current = new StringBuilder();

            foreach (var line in sql.Split('\n'))
            {
                var trimmedLine = line.Trim();

                if (string.IsNullOrEmpty(trimmedLine) || trimmedLine.StartsWith("--"))
                {
                    continue;
                }

                current.AppendLine(trimmedLine);

                if (trimmedLine.EndsWith(";"))
                {
                    statements.Add(current.ToString());
                    current.Clear();
                }
            }

            if (current.Length > 0)
            {
                statements.Add(current.ToString());
            }

            return statements;
        }

        private static string ExtractDatabaseName(string connectionString)
        {
            var parts = connectionString.Split(';');
            foreach (var part in parts)
            {
                if (part.Trim().StartsWith("Database=", StringComparison.OrdinalIgnoreCase) ||
                    part.Trim().StartsWith("Initial Catalog=", StringComparison.OrdinalIgnoreCase))
                {
                    return part.Split('=')[1].Trim();
                }
            }
            return "";
        }

        private static string ExtractDatabasePath(string connectionString)
        {
            var parts = connectionString.Split(';');
            foreach (var part in parts)
            {
                if (part.Trim().StartsWith("Data Source=", StringComparison.OrdinalIgnoreCase))
                {
                    return part.Split('=')[1].Trim();
                }
            }
            return "";
        }

        #endregion
    }

    /// <summary>
    /// 备份结果
    /// </summary>
    public class BackupResult
    {
        /// <summary>
        /// 是否成功
        /// </summary>
        public bool Success { get; set; }
        /// <summary>
        /// 数据库名称
        /// </summary>
        public string DatabaseName { get; set; }
        /// <summary>
        /// 备份文件路径
        /// </summary>
        public string BackupPath { get; set; }
        /// <summary>
        /// 开始时间
        /// </summary>
        public DateTime StartTime { get; set; }
        /// <summary>
        /// 结束时间
        /// </summary>
        public DateTime EndTime { get; set; }
        /// <summary>
        /// 耗时
        /// </summary>
        public TimeSpan Duration { get; set; }
        /// <summary>
        /// 错误信息
        /// </summary>
        public string ErrorMessage { get; set; }
    }

    /// <summary>
    /// 恢复结果
    /// </summary>
    public class RestoreResult
    {
        /// <summary>
        /// 是否成功
        /// </summary>
        public bool Success { get; set; }
        /// <summary>
        /// 备份文件路径
        /// </summary>
        public string BackupPath { get; set; }
        /// <summary>
        /// 开始时间
        /// </summary>
        public DateTime StartTime { get; set; }
        /// <summary>
        /// 结束时间
        /// </summary>
        public DateTime EndTime { get; set; }
        /// <summary>
        /// 耗时
        /// </summary>
        public TimeSpan Duration { get; set; }
        /// <summary>
        /// 错误信息
        /// </summary>
        public string ErrorMessage { get; set; }
    }

    /// <summary>
    /// 备份选项
    /// </summary>
    public class BackupOptions
    {
        /// <summary>
        /// 是否包含数据
        /// </summary>
        public bool IncludeData { get; set; } = true;
        /// <summary>
        /// 是否包含表结构
        /// </summary>
        public bool IncludeSchema { get; set; } = true;
        /// <summary>
        /// 每批处理的行数
        /// </summary>
        public int BatchSize { get; set; } = 1000;
        /// <summary>
        /// 是否压缩备份
        /// </summary>
        public bool Compress { get; set; } = false;
    }

    /// <summary>
    /// 恢复选项
    /// </summary>
    public class RestoreOptions
    {
        /// <summary>
        /// 是否先删除已存在的对象
        /// </summary>
        public bool DropExisting { get; set; } = false;
        /// <summary>
        /// 是否忽略恢复过程中的错误
        /// </summary>
        public bool IgnoreErrors { get; set; } = false;
    }

    /// <summary>
    /// 备份文件信息
    /// </summary>
    public class BackupFileInfo
    {
        /// <summary>
        /// 文件名
        /// </summary>
        public string FileName { get; set; }
        /// <summary>
        /// 文件完整路径
        /// </summary>
        public string FilePath { get; set; }
        /// <summary>
        /// 文件大小（字节）
        /// </summary>
        public long Size { get; set; }
        /// <summary>
        /// 创建时间
        /// </summary>
        public DateTime CreatedAt { get; set; }
        /// <summary>
        /// 修改时间
        /// </summary>
        public DateTime ModifiedAt { get; set; }
    }
}