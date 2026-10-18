using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data;
using Microsoft.Data.SqlClient;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FastData.Sharding;
using FastData.SyncTool.WinForms.Models;

namespace FastData.SyncTool.WinForms.Services
{
    /// <summary>
    /// 分表任务服务
    /// 管理后台分表任务的执行、暂停、取消
    /// </summary>
    public class ShardingTaskService
    {
        private readonly ConcurrentDictionary<string, ShardingTaskInfo> _tasks = new ConcurrentDictionary<string, ShardingTaskInfo>();
        private readonly LogService _logService;

        /// <summary>
        /// 任务状态变更事件
        /// </summary>
        public event Action<ShardingTaskInfo> TaskStatusChanged;

        /// <summary>
        /// 任务进度变更事件
        /// </summary>
        public event Action<ShardingTaskInfo> TaskProgressChanged;

        public ShardingTaskService(LogService logService)
        {
            _logService = logService;
        }

        /// <summary>
        /// 获取所有任务
        /// </summary>
        public List<ShardingTaskInfo> GetAllTasks()
        {
            return _tasks.Values.OrderByDescending(t => t.CreatedTime).ToList();
        }

        /// <summary>
        /// 获取任务
        /// </summary>
        public ShardingTaskInfo GetTask(string taskId)
        {
            _tasks.TryGetValue(taskId, out var task);
            return task;
        }

        /// <summary>
        /// 启动分表任务
        /// </summary>
        public ShardingTaskInfo StartTask(
            string taskName,
            string sourceTable,
            string connectionString,
            ShardingConfig shardingConfig)
        {
            var task = new ShardingTaskInfo
            {
                TaskName = taskName,
                SourceTable = sourceTable,
                ShardingType = shardingConfig.ShardingType.ToString(),
                ConfigDescription = GetConfigDescription(shardingConfig),
                ConnectionString = connectionString
            };

            task.CancellationTokenSource = new CancellationTokenSource();
            _tasks[task.TaskId] = task;

            // 后台启动任务
            Task.Run(() => ExecuteShardingTaskAsync(task, shardingConfig));

            _logService.Info($"分表任务已启动: {taskName} ({task.TaskId})");
            return task;
        }

        /// <summary>
        /// 暂停任务
        /// </summary>
        public void PauseTask(string taskId)
        {
            if (_tasks.TryGetValue(taskId, out var task))
            {
                if (task.Status == ShardingTaskStatus.Running)
                {
                    task.PauseEvent.Reset();
                    task.Status = ShardingTaskStatus.Paused;
                    TaskStatusChanged?.Invoke(task);
                    _logService.Info($"分表任务已暂停: {task.TaskName}");
                }
            }
        }

        /// <summary>
        /// 恢复任务
        /// </summary>
        public void ResumeTask(string taskId)
        {
            if (_tasks.TryGetValue(taskId, out var task))
            {
                if (task.Status == ShardingTaskStatus.Paused)
                {
                    task.PauseEvent.Set();
                    task.Status = ShardingTaskStatus.Running;
                    TaskStatusChanged?.Invoke(task);
                    _logService.Info($"分表任务已恢复: {task.TaskName}");
                }
            }
        }

        /// <summary>
        /// 取消任务
        /// </summary>
        public void CancelTask(string taskId)
        {
            if (_tasks.TryGetValue(taskId, out var task))
            {
                if (task.Status == ShardingTaskStatus.Running || task.Status == ShardingTaskStatus.Paused)
                {
                    task.CancellationTokenSource?.Cancel();
                    task.PauseEvent.Set(); // 解除暂停状态
                    task.Status = ShardingTaskStatus.Cancelled;
                    TaskStatusChanged?.Invoke(task);
                    _logService.Info($"分表任务已取消: {task.TaskName}");
                }
            }
        }

        /// <summary>
        /// 删除任务
        /// </summary>
        public bool DeleteTask(string taskId)
        {
            if (_tasks.TryGetValue(taskId, out var task))
            {
                if (task.Status == ShardingTaskStatus.Running)
                {
                    return false; // 运行中的任务不能删除
                }

                _tasks.TryRemove(taskId, out _);
                task.CancellationTokenSource?.Dispose();
                task.PauseEvent?.Dispose();
                _logService.Info($"分表任务已删除: {task.TaskName}");
                return true;
            }
            return false;
        }

        /// <summary>
        /// 执行分表任务
        /// </summary>
        private async Task ExecuteShardingTaskAsync(ShardingTaskInfo task, ShardingConfig shardingConfig)
        {
            try
            {
                task.Status = ShardingTaskStatus.Running;
                task.StartTime = DateTime.Now;
                TaskStatusChanged?.Invoke(task);

                var cancellationToken = task.CancellationTokenSource.Token;

                using (var conn = new SqlConnection(task.ConnectionString))
                {
                    await conn.OpenAsync(cancellationToken);

                    // 1. 统计源表总记录数
                    var countSql = $"SELECT COUNT(*) FROM [{task.SourceTable}]";
                    using (var cmd = new SqlCommand(countSql, conn))
                    {
                        task.TotalRecords = (int)await cmd.ExecuteScalarAsync(cancellationToken);
                    }
                    TaskProgressChanged?.Invoke(task);

                    // 2. 读取源表结构并创建分表
                    var schema = await GetSourceTableSchemaAsync(conn, task.SourceTable, cancellationToken);
                    if (schema.Columns.Count == 0)
                    {
                        throw new InvalidOperationException($"源表 {task.SourceTable} 不存在或没有可复制的列");
                    }

                    var strategy = ShardingManager.GetStrategy(shardingConfig.ShardingType);
                    var tableNames = strategy.GetAllTableNames(shardingConfig) ?? new List<string>();
                    var createdTables = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                    foreach (var tableName in tableNames)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        await EnsureShardingTableAsync(conn, tableName, schema, cancellationToken);
                        createdTables.Add(tableName);
                    }

                    // 3. 分批读取源数据并插入分表
                    var batchSize = 1000;
                    var offset = 0;

                    while (offset < task.TotalRecords)
                    {
                        // 检查暂停
                        task.PauseEvent.Wait(cancellationToken);

                        // 检查取消
                        cancellationToken.ThrowIfCancellationRequested();

                        // 分批查询
                        var selectSql = $@"
                            SELECT * FROM [{task.SourceTable}]
                            ORDER BY (SELECT NULL)
                            OFFSET {offset} ROWS
                            FETCH NEXT {batchSize} ROWS ONLY";

                        using (var cmd = new SqlCommand(selectSql, conn))
                        using (var reader = await cmd.ExecuteReaderAsync(cancellationToken))
                        {
                            var columnNames = new List<string>();
                            foreach (DataRow row in reader.GetSchemaTable().Rows)
                            {
                                columnNames.Add(row["ColumnName"].ToString());
                            }

                            while (await reader.ReadAsync(cancellationToken))
                            {
                                // 检查暂停
                                task.PauseEvent.Wait(cancellationToken);

                                // 检查取消
                                cancellationToken.ThrowIfCancellationRequested();

                                try
                                {
                                    // 构建实体数据
                                    var entity = new Dictionary<string, object>();
                                    foreach (var colName in columnNames)
                                    {
                                        var value = reader[colName];
                                        entity[colName] = value is DBNull ? null : value;
                                    }

                                    // 确定目标分表
                                    var targetTable = ShardingNameResolver.Resolve(
                                        shardingConfig,
                                        field => entity.TryGetValue(field, out var v) ? v : null);

                                    // 按需创建目标分表（防止分表命名与枚举结果不一致）
                                    if (createdTables.Add(targetTable))
                                    {
                                        await EnsureShardingTableAsync(conn, targetTable, schema, cancellationToken);
                                    }

                                    // 插入分表
                                    await InsertToShardingTableAsync(conn, targetTable, entity, schema, cancellationToken);

                                    task.SuccessRecords++;
                                }
                                catch (Exception ex)
                                {
                                    task.FailedRecords++;
                                    if (task.FailedRecords <= 100) // 只记录前100个错误
                                    {
                                        _logService.Warn($"分表插入失败: {ex.Message}");
                                    }
                                }

                                task.ProcessedRecords++;

                                // 每100条更新一次进度
                                if (task.ProcessedRecords % 100 == 0)
                                {
                                    TaskProgressChanged?.Invoke(task);
                                }
                            }
                        }

                        offset += batchSize;
                    }
                }

                task.Status = ShardingTaskStatus.Completed;
                task.CompleteTime = DateTime.Now;
                TaskStatusChanged?.Invoke(task);
                TaskProgressChanged?.Invoke(task);

                _logService.Info($"分表任务完成: {task.TaskName}, 成功: {task.SuccessRecords}, 失败: {task.FailedRecords}");
            }
            catch (OperationCanceledException)
            {
                task.Status = ShardingTaskStatus.Cancelled;
                task.CompleteTime = DateTime.Now;
                TaskStatusChanged?.Invoke(task);
            }
            catch (Exception ex)
            {
                task.Status = ShardingTaskStatus.Failed;
                task.ErrorMessage = ex.Message;
                task.CompleteTime = DateTime.Now;
                TaskStatusChanged?.Invoke(task);
                _logService.Error($"分表任务失败: {task.TaskName}, 错误: {ex.Message}");
            }
        }

        /// <summary>
        /// 源表结构信息
        /// </summary>
        private sealed class TableSchemaInfo
        {
            /// <summary>
            /// 建表列定义
            /// </summary>
            public List<string> Columns { get; } = new List<string>();

            /// <summary>
            /// 可插入的列（排除计算列与 rowversion 等只读列）
            /// </summary>
            public List<string> InsertColumns { get; } = new List<string>();

            /// <summary>
            /// 标识列名称（无则为 null）
            /// </summary>
            public string IdentityColumn { get; set; }
        }

        /// <summary>
        /// 读取源表结构
        /// </summary>
        private async Task<TableSchemaInfo> GetSourceTableSchemaAsync(SqlConnection conn, string sourceTable, CancellationToken cancellationToken)
        {
            var schema = new TableSchemaInfo();
            var schemaSql = @"
                SELECT c.COLUMN_NAME,
                       c.DATA_TYPE,
                       c.CHARACTER_MAXIMUM_LENGTH,
                       c.IS_NULLABLE,
                       c.NUMERIC_PRECISION,
                       c.NUMERIC_SCALE,
                       c.DATETIME_PRECISION,
                       COLUMNPROPERTY(OBJECT_ID(QUOTENAME(c.TABLE_SCHEMA) + '.' + QUOTENAME(c.TABLE_NAME)), c.COLUMN_NAME, 'IsIdentity') AS IS_IDENTITY,
                       COLUMNPROPERTY(OBJECT_ID(QUOTENAME(c.TABLE_SCHEMA) + '.' + QUOTENAME(c.TABLE_NAME)), c.COLUMN_NAME, 'IsComputed') AS IS_COMPUTED
                FROM INFORMATION_SCHEMA.COLUMNS c
                WHERE c.TABLE_NAME = @table
                ORDER BY c.ORDINAL_POSITION";

            using (var cmd = new SqlCommand(schemaSql, conn))
            {
                cmd.Parameters.AddWithValue("@table", sourceTable);
                using (var reader = await cmd.ExecuteReaderAsync(cancellationToken))
                {
                    while (await reader.ReadAsync(cancellationToken))
                    {
                        var colName = reader.GetString(0);
                        var dataType = reader.GetString(1);
                        var maxLength = reader.IsDBNull(2) ? (int?)null : reader.GetInt32(2);
                        var isNullable = reader.GetString(3) == "YES";
                        var numericPrecision = reader.IsDBNull(4) ? (int?)null : Convert.ToInt32(reader.GetValue(4));
                        var numericScale = reader.IsDBNull(5) ? (int?)null : Convert.ToInt32(reader.GetValue(5));
                        var dateTimePrecision = reader.IsDBNull(6) ? (int?)null : Convert.ToInt32(reader.GetValue(6));
                        var isIdentity = !reader.IsDBNull(7) && Convert.ToInt32(reader.GetValue(7)) == 1;
                        var isComputed = !reader.IsDBNull(8) && Convert.ToInt32(reader.GetValue(8)) == 1;

                        var columnDef = $"{EscapeIdentifier(colName)} {BuildColumnType(dataType, maxLength, numericPrecision, numericScale, dateTimePrecision)}";
                        if (isIdentity)
                        {
                            columnDef += " IDENTITY(1,1)";
                            schema.IdentityColumn = colName;
                        }
                        if (!isNullable)
                        {
                            columnDef += " NOT NULL";
                        }

                        schema.Columns.Add(columnDef);

                        var isReadOnlyType = dataType.Equals("timestamp", StringComparison.OrdinalIgnoreCase)
                            || dataType.Equals("rowversion", StringComparison.OrdinalIgnoreCase);
                        if (!isComputed && !isReadOnlyType)
                        {
                            schema.InsertColumns.Add(colName);
                        }
                    }
                }
            }

            return schema;
        }

        /// <summary>
        /// 根据数据类型构建列类型定义
        /// </summary>
        private static string BuildColumnType(string dataType, int? maxLength, int? numericPrecision, int? numericScale, int? dateTimePrecision)
        {
            switch (dataType.ToLowerInvariant())
            {
                case "char":
                case "varchar":
                case "nchar":
                case "nvarchar":
                case "binary":
                case "varbinary":
                    if (maxLength == -1)
                        return $"{dataType}(max)";
                    if (maxLength.HasValue && maxLength.Value > 0)
                        return $"{dataType}({maxLength.Value})";
                    return dataType;

                case "decimal":
                case "numeric":
                    if (numericPrecision.HasValue)
                        return $"{dataType}({numericPrecision.Value},{numericScale ?? 0})";
                    return dataType;

                case "datetime2":
                case "datetimeoffset":
                case "time":
                    if (dateTimePrecision.HasValue)
                        return $"{dataType}({dateTimePrecision.Value})";
                    return dataType;

                default:
                    return dataType;
            }
        }

        /// <summary>
        /// 转义 SQL Server 标识符
        /// </summary>
        private static string EscapeIdentifier(string name)
        {
            return "[" + (name ?? string.Empty).Replace("]", "]]") + "]";
        }

        /// <summary>
        /// 确保分表存在（不存在则创建）
        /// </summary>
        private async Task EnsureShardingTableAsync(SqlConnection conn, string targetTable, TableSchemaInfo schema, CancellationToken cancellationToken)
        {
            if (schema.Columns.Count == 0)
                return;

            var createSql = $@"
                IF OBJECT_ID(@table, 'U') IS NULL
                CREATE TABLE {EscapeIdentifier(targetTable)} (
                    {string.Join(",\n                    ", schema.Columns)}
                );";

            using (var cmd = new SqlCommand(createSql, conn))
            {
                cmd.Parameters.AddWithValue("@table", targetTable);
                await cmd.ExecuteNonQueryAsync(cancellationToken);
            }
        }

        /// <summary>
        /// 插入数据到分表
        /// </summary>
        private async Task InsertToShardingTableAsync(
            SqlConnection conn,
            string tableName,
            Dictionary<string, object> entity,
            TableSchemaInfo schema,
            CancellationToken cancellationToken)
        {
            var insertColumns = schema.InsertColumns;
            if (insertColumns.Count == 0)
                return;

            var columns = string.Join(", ", insertColumns.Select(EscapeIdentifier));
            var parameters = string.Join(", ", insertColumns.Select((c, i) => $"@p{i}"));
            var sql = $"INSERT INTO {EscapeIdentifier(tableName)} ({columns}) VALUES ({parameters})";

            var useIdentityInsert = !string.IsNullOrEmpty(schema.IdentityColumn);
            if (useIdentityInsert)
            {
                using (var identityCmd = new SqlCommand($"SET IDENTITY_INSERT {EscapeIdentifier(tableName)} ON", conn))
                {
                    await identityCmd.ExecuteNonQueryAsync(cancellationToken);
                }
            }

            try
            {
                using (var cmd = new SqlCommand(sql, conn))
                {
                    for (var i = 0; i < insertColumns.Count; i++)
                    {
                        var value = entity.TryGetValue(insertColumns[i], out var v) ? v : null;
                        cmd.Parameters.AddWithValue($"@p{i}", value ?? DBNull.Value);
                    }
                    await cmd.ExecuteNonQueryAsync(cancellationToken);
                }
            }
            finally
            {
                if (useIdentityInsert)
                {
                    using (var identityCmd = new SqlCommand($"SET IDENTITY_INSERT {EscapeIdentifier(tableName)} OFF", conn))
                    {
                        await identityCmd.ExecuteNonQueryAsync(CancellationToken.None);
                    }
                }
            }
        }

        /// <summary>
        /// 获取配置描述
        /// </summary>
        private string GetConfigDescription(ShardingConfig config)
        {
            switch (config.ShardingType)
            {
                case ShardingType.Time:
                    return $"时间分表: {config.TimeConfig.TimeField} ({config.TimeConfig.Granularity})";
                case ShardingType.Hash:
                    return $"哈希分表: {config.HashConfig.HashField} ({config.HashConfig.ShardCount}个分表)";
                case ShardingType.List:
                    return $"列表分表: {config.ListConfig.ListField}";
                case ShardingType.Composite:
                    return $"组合分表: {string.Join(",", config.CompositeConfig.CompositeFields)}";
                case ShardingType.QueryFrequency:
                    return $"频率分表: {config.FrequencyConfig.Field}";
                default:
                    return config.ShardingType.ToString();
            }
        }
    }
}
