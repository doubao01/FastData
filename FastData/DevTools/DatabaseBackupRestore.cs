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
            var startTime = DateTime.Now;
            var result = new BackupResult { Success = false, StartTime = startTime };

            try
            {
                options = options ?? new BackupOptions();

                var config = DataConfig.GetConfig(dbKey);
                if (config == null)
                {
                    result.ErrorMessage = string.Format("找不到数据库配置: {0}", dbKey);
                    return result;
                }

                var databaseName = ExtractDatabaseName(config.ConnStr);
                result.DatabaseName = databaseName;

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

                // 内层实现会新建结果对象，公共字段在这里重新回填，避免 Duration 变成「0001-01-01 到现在」
                result.EndTime = DateTime.Now;
                result.Duration = result.EndTime - startTime;
                result.BackupPath = backupPath;
                if (string.IsNullOrEmpty(result.DatabaseName))
                    result.DatabaseName = databaseName;
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
            var startTime = DateTime.Now;
            var result = new RestoreResult { Success = false, StartTime = startTime };

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

                // 内层实现会新建结果对象，公共字段在这里重新回填
                result.StartTime = startTime;
                result.BackupPath = backupPath;
                result.EndTime = DateTime.Now;
                result.Duration = result.EndTime - result.StartTime;
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
            return ExecuteScriptRestore(dbKey, backupPath, options, "MySQL");
        }

        private static RestoreResult RestoreSqlServer(string dbKey, ConfigModel config, string backupPath, RestoreOptions options)
        {
            return ExecuteScriptRestore(dbKey, backupPath, options, "SQL Server");
        }

        private static RestoreResult RestorePostgreSql(string dbKey, ConfigModel config, string backupPath, RestoreOptions options)
        {
            return ExecuteScriptRestore(dbKey, backupPath, options, "PostgreSQL");
        }

        /// <summary>
        /// 按 SQL 脚本恢复数据库（MySQL / SQL Server / PostgreSQL 共用实现）。
        /// </summary>
        /// <remarks>
        /// <para>
        /// 失败不再被吞掉：默认遇到第一条失败语句即中止并回滚
        /// （<see cref="RestoreOptions.IgnoreErrors"/> 为 false）；
        /// 显式设置 IgnoreErrors 时会继续执行剩余语句，但只要有一条失败，
        /// <see cref="RestoreResult.Success"/> 就为 false，明细在 <see cref="RestoreResult.Errors"/>。
        /// </para>
        /// <para>
        /// MySQL 的 DDL 会隐式提交事务，回滚无法撤销已执行的 DDL；
        /// SQL Server / PostgreSQL 支持事务性 DDL，回滚更彻底。
        /// 需要在 MySQL 上获得原子性时，应改用物理备份工具而不是 SQL 脚本回放。
        /// </para>
        /// </remarks>
        private static RestoreResult ExecuteScriptRestore(string dbKey, string backupPath, RestoreOptions options, string engineName)
        {
            var result = new RestoreResult { Success = false };
            options = options ?? new RestoreOptions();

            try
            {
                var sql = File.ReadAllText(backupPath, Encoding.UTF8);
                var statements = FilterStatements(ParseSqlStatements(sql));
                result.TotalStatements = statements.Count;

                if (statements.Count == 0)
                {
                    result.ErrorMessage = string.Format("{0} 备份文件中没有可执行语句: {1}", engineName, backupPath);
                    return result;
                }

                using var db = new DataContext(dbKey);
                var transactionStarted = false;

                if (options.UseTransaction)
                {
                    db.BeginTransaction();
                    transactionStarted = true;
                }

                try
                {
                    if (options.DropExisting)
                    {
                        var tables = GetTables(dbKey);
                        result.DroppedTables = tables.Count;

                        var drops = new List<string>();
                        foreach (var table in tables)
                            drops.Add(string.Format("DROP TABLE IF EXISTS {0}", table));

                        RunStatements(drops, result, options, s => ExecuteCommand(db, s));
                    }

                    if (result.FailedStatements == 0)
                    {
                        RunStatements(statements, result, options, s => ExecuteCommand(db, s));
                    }
                }
                finally
                {
                    if (transactionStarted && result.FailedStatements > 0 && !options.IgnoreErrors)
                    {
                        db.RollbackTransaction();
                        result.RolledBack = true;
                    }
                }

                if (transactionStarted && !result.RolledBack)
                {
                    db.CommitTransaction();
                }

                result.Success = result.FailedStatements == 0;
                result.IsPartial = !result.Success && result.ExecutedStatements > 0;

                if (!result.Success)
                {
                    result.ErrorMessage = result.RolledBack
                        ? string.Format(
                            "{0} 恢复失败并已回滚：{1} 条语句执行失败（共 {2} 条）。首条错误：{3}",
                            engineName, result.FailedStatements, result.TotalStatements, FirstError(result))
                        : string.Format(
                            "{0} 恢复失败，数据库可能处于部分恢复状态：{1} 条语句失败，{2} 条已提交（共 {3} 条）。首条错误：{4}",
                            engineName, result.FailedStatements, result.ExecutedStatements, result.TotalStatements,
                            FirstError(result));
                }
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = string.Format("{0} 恢复失败: {1}", engineName, ex.Message);
            }

            return result;
        }

        /// <summary>
        /// 按顺序执行一组语句，遇失败按 <see cref="RestoreOptions.IgnoreErrors"/> 决定是否中止。
        /// </summary>
        internal static void RunStatements(IEnumerable<string> statements, RestoreResult result, RestoreOptions options, Action<string> execute)
        {
            foreach (var statement in statements)
            {
                if (!ExecuteOneCore(statement, result, options, execute))
                    break;
            }
        }

        private static void ExecuteCommand(DataContext db, string statement)
        {
            db.cmd.CommandText = statement;
            db.cmd.ExecuteNonQuery();
        }

        /// <summary>
        /// 执行单条语句的核心逻辑，执行动作由调用方注入，便于在不接数据库时验证失败记账契约。
        /// </summary>
        internal static bool ExecuteOneCore(string statement, RestoreResult result, RestoreOptions options, Action<string> execute)
        {
            if (execute == null)
                throw new ArgumentNullException(nameof(execute));

            options = options ?? new RestoreOptions();

            try
            {
                execute(statement);
                result.ExecutedStatements++;
                return true;
            }
            catch (Exception ex)
            {
                result.FailedStatements++;
                result.Errors.Add(string.Format("{0} | 语句: {1}", ex.Message, SummarizeStatement(statement)));
                return options.IgnoreErrors;
            }
        }

        internal static List<string> FilterStatements(List<string> statements)
        {
            var executable = new List<string>();

            foreach (var statement in statements)
            {
                if (string.IsNullOrWhiteSpace(statement))
                    continue;

                if (statement.TrimStart().StartsWith("--", StringComparison.Ordinal))
                    continue;

                executable.Add(statement);
            }

            return executable;
        }

        private static string SummarizeStatement(string statement)
        {
            var singleLine = statement.Replace("\r", " ").Replace("\n", " ").Trim();
            return singleLine.Length <= 120 ? singleLine : singleLine.Substring(0, 120) + "...";
        }

        private static string FirstError(RestoreResult result)
        {
            return result.Errors.Count > 0 ? result.Errors[0] : string.Empty;
        }

        private static RestoreResult RestoreSQLite(string dbKey, ConfigModel config, string backupPath, RestoreOptions options)
        {
            var result = new RestoreResult { Success = false };
            options = options ?? new RestoreOptions();
            string preservedOriginal = null;
            string dbPath = null;

            try
            {
                dbPath = ExtractDatabasePath(config.ConnStr);
                if (string.IsNullOrWhiteSpace(dbPath))
                {
                    result.ErrorMessage = "无法从连接字符串解析 SQLite 数据库路径，拒绝恢复以免写错文件。";
                    return result;
                }

                if (options.DropExisting && File.Exists(dbPath))
                {
                    // 先留一份原库副本：删库后拷贝失败时还能退回去，避免数据全失
                    preservedOriginal = dbPath + ".before-restore-" + DateTime.Now.ToString("yyyyMMddHHmmss");
                    File.Copy(dbPath, preservedOriginal, true);
                    File.Delete(dbPath);
                }

                File.Copy(backupPath, dbPath, true);
                result.Success = true;
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = string.Format("SQLite 恢复失败: {0}", ex.Message);

                if (preservedOriginal != null && dbPath != null)
                {
                    try
                    {
                        File.Copy(preservedOriginal, dbPath, true);
                        result.RolledBack = true;
                        TryDelete(preservedOriginal);
                    }
                    catch (Exception rollbackEx)
                    {
                        result.Errors.Add(string.Format(
                            "回退原数据库失败，原库副本位于 {0}：{1}", preservedOriginal, rollbackEx.Message));
                    }
                }
            }

            return result;
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch
            {
                // 清理失败不影响恢复结果
            }
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
            catch (Exception ex)
            {
                // 不能吞掉：读不到数据时上层会写出一个「空但成功」的备份文件。
                // 抛出让 BackupXxx 的 catch 统一置 Success=false 并给出原因。
                throw new InvalidOperationException(string.Format("读取表清单失败：{0}", ex.Message), ex);
            }

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
            catch (Exception ex)
            {
                // 不能吞掉：读不到数据时上层会写出一个「空但成功」的备份文件。
                // 抛出让 BackupXxx 的 catch 统一置 Success=false 并给出原因。
                throw new InvalidOperationException(string.Format("读取表结构失败：{0}", ex.Message), ex);
            }

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
            catch (Exception ex)
            {
                // 不能吞掉：读不到数据时上层会写出一个「空但成功」的备份文件。
                // 抛出让 BackupXxx 的 catch 统一置 Success=false 并给出原因。
                throw new InvalidOperationException(string.Format("读取表数据失败：{0}", ex.Message), ex);
            }

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
        /// 是否成功。只要有任意一条语句失败，这里就是 false。
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
        /// <summary>
        /// 失败语句明细（每条含异常信息与被截断的语句文本）
        /// </summary>
        public List<string> Errors { get; set; } = new List<string>();
        /// <summary>
        /// 备份文件中待执行的语句总数
        /// </summary>
        public int TotalStatements { get; set; }
        /// <summary>
        /// 已成功执行的语句数
        /// </summary>
        public int ExecutedStatements { get; set; }
        /// <summary>
        /// 执行失败的语句数
        /// </summary>
        public int FailedStatements { get; set; }
        /// <summary>
        /// 恢复前删除的表数量（仅 DropExisting 时统计）
        /// </summary>
        public int DroppedTables { get; set; }
        /// <summary>
        /// 是否已回滚
        /// </summary>
        public bool RolledBack { get; set; }
        /// <summary>
        /// 是否处于部分恢复状态（已执行部分语句但整体未成功）
        /// </summary>
        public bool IsPartial { get; set; }
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
        /// 是否忽略恢复过程中的错误。
        /// <para>
        /// 默认 false：遇到第一条失败语句即中止并回滚。
        /// 设为 true 时继续执行剩余语句，但只要有失败，
        /// <see cref="RestoreResult.Success"/> 仍为 false（不会谎报成功），
        /// 明细见 <see cref="RestoreResult.Errors"/>。
        /// </para>
        /// </summary>
        public bool IgnoreErrors { get; set; } = false;
        /// <summary>
        /// 是否在单个事务中执行恢复（默认 true）。
        /// <para>
        /// MySQL 的 DDL 会隐式提交事务，回滚无法撤销已执行的 DDL；
        /// SQL Server / PostgreSQL 支持事务性 DDL，回滚更彻底。
        /// </para>
        /// </summary>
        public bool UseTransaction { get; set; } = true;
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