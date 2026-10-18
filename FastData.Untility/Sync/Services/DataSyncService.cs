using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.IO;
using System.Threading;
using System.Xml;

namespace FastData.Tooling.Sync
{
    /// <summary>
    /// 数据同步日志级别
    /// </summary>
    public enum SyncLogLevel
    {
        Info,
        Warn,
        Error,
        Debug
    }

    /// <summary>
    /// 数据同步日志回调
    /// </summary>
    public class DataSyncService
    {
        /// <summary>
        /// 日志回调函数，外部可注入自定义日志处理逻辑
        /// </summary>
        public Action<SyncLogLevel, string> LogCallback { get; set; }

        /// <summary>
        /// 写入日志
        /// </summary>
        private void Log(SyncLogLevel level, string message)
        {
            var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
            var fullMessage = string.Format("[{0}] [{1}] {2}", timestamp, level.ToString().ToUpperInvariant(), message);
            
            // 调用外部回调
            LogCallback?.Invoke(level, fullMessage);
            
            // 同时输出到 Debug 输出窗口
            System.Diagnostics.Debug.WriteLine(fullMessage);
        }

        /// <summary>
        /// 同步单个表的数据
        /// </summary>
        /// <param name="options">同步选项</param>
        /// <param name="cancellationToken">取消令牌（支持行级取消）</param>
        /// <returns>同步结果</returns>
        public DataSyncResult SyncTable(DataSyncOptions options, CancellationToken cancellationToken = default(CancellationToken))
        {
            if (options == null)
                throw new ArgumentNullException("options");

            Log(SyncLogLevel.Info, string.Format("========== 开始同步任务 =========="));
            Log(SyncLogLevel.Info, string.Format("任务ID: {0}", string.IsNullOrWhiteSpace(options.TaskId) ? options.SourceTable + "_to_" + options.TargetTable : options.TaskId));
            Log(SyncLogLevel.Info, string.Format("源表: {0} -> 目标表: {1}", options.SourceTable, options.TargetTable));
            Log(SyncLogLevel.Info, string.Format("同步模式: {0}", options.EnableTimeRange ? "时间范围增量" : (options.IsAutoIncrementKey ? "主键增量" : "全量同步")));
            Log(SyncLogLevel.Info, string.Format("去重模式: {0}", options.AlwaysDeduplicate ? "是（逐行插入/更新）" : "否（批量插入）"));
            Log(SyncLogLevel.Info, string.Format("批次大小: {0}", options.BatchSize <= 0 ? 500 : options.BatchSize));
            Log(SyncLogLevel.Info, string.Format("最大重试次数: {0}", options.RetryCount < 0 ? 0 : options.RetryCount));

            var syncStartTime = DateTime.Now;
            ValidateOptions(options);
            var batchSize = options.BatchSize <= 0 ? 500 : options.BatchSize;
            var maxRetryCount = options.RetryCount < 0 ? 0 : options.RetryCount;
            var taskId = string.IsNullOrWhiteSpace(options.TaskId) ? options.SourceTable + "_to_" + options.TargetTable : options.TaskId;

            // 步骤 1: 创建中间库表结构
            if (options.AutoCreateIntermediateSchema)
            {
                Log(SyncLogLevel.Info, "[步骤 1/7] 正在创建中间库表结构...");
                CreateIntermediateSchema(options);
                Log(SyncLogLevel.Info, "[步骤 1/7] 中间库表结构创建完成");
            }
            else
            {
                Log(SyncLogLevel.Debug, "[步骤 1/7] 跳过中间库表结构创建");
            }

            // 步骤 2: 应用全局配置
            Log(SyncLogLevel.Info, "[步骤 2/7] 应用同步配置...");
            ApplyGlobalConfig(options);
            Log(SyncLogLevel.Info, string.Format("[步骤 2/7] 配置应用完成 - 时间范围: {0}天, 去重: {1}", options.RangeDays, options.AlwaysDeduplicate ? "是" : "否"));

            // 步骤 3: 从源库读取数据
            var table = new DataTable();
            var sourceSql = string.Empty;
            Log(SyncLogLevel.Info, "[步骤 3/7] 正在连接源数据库并读取数据...");
            using (var source = CreateConnection(options.SourceProvider, options.SourceConnectionString))
            using (var command = source.CreateCommand())
            {
                Log(SyncLogLevel.Debug, string.Format("源库Provider: {0}", options.SourceProvider));
                Log(SyncLogLevel.Debug, string.Format("源库连接状态: 准备连接"));
                
                source.Open();
                Log(SyncLogLevel.Debug, "源库连接成功");

                sourceSql = BuildSourceSql(options, command);
                command.CommandText = sourceSql;
                Log(SyncLogLevel.Info, string.Format("执行查询SQL: {0}", sourceSql));
                if (command.Parameters.Count > 0)
                {
                    foreach (DbParameter param in command.Parameters)
                    {
                        Log(SyncLogLevel.Debug, string.Format("SQL参数: {0} = {1}", param.ParameterName, param.Value));
                    }
                }

                using (var reader = command.ExecuteReader())
                {
                    table.Load(reader);
                }
                Log(SyncLogLevel.Info, string.Format("[步骤 3/7] 数据读取完成，共 {0} 行", table.Rows.Count));
            }

            // 步骤 4: 连接目标库并执行同步
            var writeCount = 0;
            var failedCount = 0;
            var retryCount = 0;
            var recoveredCount = 0;
            var skippedCount = 0;

            // 根据目标库的参数上限与列数收敛批次大小，避免单条 INSERT 参数数超限
            if (table.Columns.Count > 0)
            {
                var maxParams = GetMaxParametersPerStatement(options.TargetProvider);
                var maxRowsPerBatch = Math.Max(1, maxParams / table.Columns.Count);
                if (batchSize > maxRowsPerBatch)
                {
                    Log(SyncLogLevel.Warn, string.Format("批次大小 {0} 超过目标库参数上限，自动收敛为 {1}", batchSize, maxRowsPerBatch));
                    batchSize = maxRowsPerBatch;
                }
            }

            Log(SyncLogLevel.Info, "[步骤 4/7] 正在连接目标数据库...");
            using (var intermediate = OpenIntermediateConnection(options))
            using (var target = CreateConnection(options.TargetProvider, options.TargetConnectionString))
            {
                Log(SyncLogLevel.Debug, string.Format("目标库Provider: {0}", options.TargetProvider));
                target.Open();
                Log(SyncLogLevel.Info, "[步骤 4/7] 目标库连接成功");

                // 步骤 5: 恢复失败记录
                if (options.ResumeFailedRecords)
                {
                    Log(SyncLogLevel.Info, "[步骤 5/7] 正在恢复历史失败记录...");
                    recoveredCount = ResumeFailedRecords(options, target, intermediate, maxRetryCount);
                    Log(SyncLogLevel.Info, string.Format("[步骤 5/7] 失败记录恢复完成，成功恢复 {0} 条", recoveredCount));
                }
                else
                {
                    Log(SyncLogLevel.Debug, "[步骤 5/7] 跳过失败记录恢复");
                }

                // 步骤 6: 同步数据行
                Log(SyncLogLevel.Info, "[步骤 6/7] 开始同步数据行...");
                var batchRows = new List<DataRow>();
                var processedRows = 0;
                var batchIndex = 0;

                foreach (DataRow row in table.Rows)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    processedRows++;
                    int rowRetryCount = 0;

                    // 去重模式：逐行插入
                    if (options.AlwaysDeduplicate && !string.IsNullOrEmpty(options.PrimaryKeyColumns))
                    {
                        if (TryInsertRowWithDedup(target, options.TargetTable, table, row, maxRetryCount, out rowRetryCount, out var inserted, options.PrimaryKeyColumns))
                        {
                            if (inserted)
                                writeCount++;
                            else
                                skippedCount++;
                        }
                        else
                        {
                            failedCount++;
                            Log(SyncLogLevel.Warn, string.Format("行插入失败 (行 {0}/{1})，已保存到失败记录", processedRows, table.Rows.Count));
                            SaveFailedRecord(options, intermediate, taskId, table, row);
                        }
                    }
                    else
                    {
                        // 批量插入模式
                        batchRows.Add(row);
                        if (batchRows.Count >= batchSize)
                        {
                            batchIndex++;
                            Log(SyncLogLevel.Debug, string.Format("执行批量插入 批次 #{0}，共 {1} 行", batchIndex, batchRows.Count));
                            try
                            {
                                InsertRowBatch(target, options.TargetTable, table, batchRows);
                                writeCount += batchRows.Count;
                                Log(SyncLogLevel.Debug, string.Format("批量插入 批次 #{0} 成功", batchIndex));
                            }
                            catch (Exception ex)
                            {
                                Log(SyncLogLevel.Warn, string.Format("批量插入 批次 #{0} 失败，降级为逐行插入: {1}", batchIndex, ex.Message));
                                // 批量失败则逐行重试
                                var rowSuccessCount = 0;
                                var rowFailedCount = 0;
                                foreach (var batchRow in batchRows)
                                {
                                    try
                                    {
                                        InsertRow(target, options.TargetTable, table, batchRow);
                                        writeCount++;
                                        rowSuccessCount++;
                                    }
                                    catch (Exception rowEx)
                                    {
                                        Log(SyncLogLevel.Error, string.Format("逐行插入失败: {0}", rowEx.Message));
                                        failedCount++;
                                        rowFailedCount++;
                                        SaveFailedRecord(options, intermediate, taskId, table, batchRow);
                                    }
                                }
                                Log(SyncLogLevel.Info, string.Format("逐行降级完成: 成功 {0} 行，失败 {1} 行", rowSuccessCount, rowFailedCount));
                            }
                            batchRows.Clear();
                        }
                    }

                    retryCount += rowRetryCount;

                    // 每处理 1000 行打印一次进度
                    if (processedRows % 1000 == 0)
                    {
                        Log(SyncLogLevel.Info, string.Format("同步进度: 已处理 {0}/{1} 行，已写入 {2} 行，失败 {3} 行",
                            processedRows, table.Rows.Count, writeCount, failedCount));
                    }
                }

                // 处理剩余批次
                Log(SyncLogLevel.Info, string.Format("[步骤 6/7] 数据处理完成，正在处理剩余 {0} 行...", batchRows.Count));
                if (batchRows.Count > 0 && !options.AlwaysDeduplicate)
                {
                    batchIndex++;
                    Log(SyncLogLevel.Debug, string.Format("执行最终批量插入 批次 #{0}，共 {1} 行", batchIndex, batchRows.Count));
                    try
                    {
                        InsertRowBatch(target, options.TargetTable, table, batchRows);
                        writeCount += batchRows.Count;
                        Log(SyncLogLevel.Debug, string.Format("最终批量插入 批次 #{0} 成功", batchIndex));
                    }
                    catch (Exception ex)
                    {
                        Log(SyncLogLevel.Warn, string.Format("最终批量插入 批次 #{0} 失败，降级为逐行插入: {1}", batchIndex, ex.Message));
                        // 批量失败则逐行重试
                        var rowSuccessCount = 0;
                        var rowFailedCount = 0;
                        foreach (var batchRow in batchRows)
                        {
                            try
                            {
                                InsertRow(target, options.TargetTable, table, batchRow);
                                writeCount++;
                                rowSuccessCount++;
                            }
                            catch (Exception rowEx)
                            {
                                Log(SyncLogLevel.Error, string.Format("最终逐行插入失败: {0}", rowEx.Message));
                                failedCount++;
                                rowFailedCount++;
                                SaveFailedRecord(options, intermediate, taskId, table, batchRow);
                            }
                        }
                        Log(SyncLogLevel.Info, string.Format("最终逐行降级完成: 成功 {0} 行，失败 {1} 行", rowSuccessCount, rowFailedCount));
                    }
                }

                // 步骤 7: 清理中间库数据
                if (options.CleanIntermediateData)
                {
                    Log(SyncLogLevel.Info, "[步骤 7/7] 正在清理中间库成功记录...");
                    CleanIntermediateData(options, intermediate);
                    Log(SyncLogLevel.Info, "[步骤 7/7] 中间库清理完成");
                }
                else
                {
                    Log(SyncLogLevel.Debug, "[步骤 7/7] 跳过中间库清理");
                }
            }

            // 计算同步结果
            var syncDuration = DateTime.Now - syncStartTime;
            var result = new DataSyncResult
            {
                ReadCount = table.Rows.Count,
                WriteCount = writeCount,
                FailedCount = failedCount,
                SkippedCount = skippedCount,
                RetryCount = retryCount,
                RecoveredCount = recoveredCount,
                Message = string.Format("同步完成 [读取 {0} 行，写入 {1} 行，跳过 {2} 行，失败 {3} 行，重试 {4} 次，恢复 {5} 条] [耗时 {6:F1}秒] [去重模式：{7}]",
                    table.Rows.Count, writeCount, skippedCount, failedCount, retryCount, recoveredCount, syncDuration.TotalSeconds, options.AlwaysDeduplicate ? "是" : "否"),
                MaxPkValue = GetMaxPrimaryKeyValueFromDb(options.SourceProvider, options.SourceConnectionString, options.SourceTable, options.PrimaryKeyColumns)
                    ?? GetMaxPrimaryKeyValue(table, options.PrimaryKeyColumns)
            };

            // 更新同步进度
            if (options.ConfigManager != null && !string.IsNullOrEmpty(options.TaskId))
            {
                if (options.EnableTimeRange && result.ReadCount > 0)
                {
                    options.ConfigManager.UpdateLastSyncTime(options.TaskId, syncTime: DateTime.Now);
                    Log(SyncLogLevel.Debug, string.Format("已更新任务同步时间 (时间范围模式): {0}", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")));
                }
                else if (!options.EnableTimeRange && result.MaxPkValue.HasValue)
                {
                    // 以字符串形式保存主键最大值，避免 ticks 溢出（主键可能超出 DateTime 范围）
                    options.ConfigManager.UpdateLastPrimaryKeyValue(options.TaskId, result.MaxPkValue.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    Log(SyncLogLevel.Debug, string.Format("已更新任务主键最大值 (主键增量模式): {0}", result.MaxPkValue.Value));
                }
            }

            Log(SyncLogLevel.Info, string.Format("========== 同步任务完成 =========="));
            Log(SyncLogLevel.Info, string.Format("最终统计: 读取 {0} 行，成功写入 {1} 行，跳过 {2} 行，失败 {3} 行", result.ReadCount, result.WriteCount, result.SkippedCount, result.FailedCount));
            Log(SyncLogLevel.Info, string.Format("重试统计: 重试 {0} 次，恢复 {1} 条失败记录", result.RetryCount, result.RecoveredCount));
            Log(SyncLogLevel.Info, string.Format("耗时统计: {0:F1} 秒", syncDuration.TotalSeconds));
            Log(SyncLogLevel.Info, string.Format("源SQL: {0}", sourceSql));

            return result;
        }

        /// <summary>
        /// 应用全局配置
        /// </summary>
        internal static void ApplyGlobalConfig(DataSyncOptions options)
        {
            if (options.EnableGlobalConfig)
            {
                // 使用全局配置的 RangeDays
                if (options.GlobalRangeDays > 0)
                    options.RangeDays = options.GlobalRangeDays;

                // 使用全局配置的去重模式（覆盖任务级别的去重设置）
                options.AlwaysDeduplicate = options.GlobalAlwaysDeduplicate;
            }
        }

        private static void ValidateOptions(DataSyncOptions options)
        {
            if (string.IsNullOrWhiteSpace(options.SourceProvider))
                throw new ArgumentException("源库 Provider 不能为空");
            if (string.IsNullOrWhiteSpace(options.SourceConnectionString))
                throw new ArgumentException("源库连接字符串不能为空");
            if (string.IsNullOrWhiteSpace(options.TargetProvider))
                throw new ArgumentException("目标库 Provider 不能为空");
            if (string.IsNullOrWhiteSpace(options.TargetConnectionString))
                throw new ArgumentException("目标库连接字符串不能为空");
            if (string.IsNullOrWhiteSpace(options.SourceTable))
                throw new ArgumentException("源表不能为空");
            if (string.IsNullOrWhiteSpace(options.TargetTable))
                throw new ArgumentException("目标表不能为空");

            // 校验标识符，避免 SQL 注入
            ValidateIdentifier(options.SourceTable, "源表");
            ValidateIdentifier(options.TargetTable, "目标表");

            if (!string.IsNullOrEmpty(options.PrimaryKeyColumns))
            {
                foreach (var col in options.PrimaryKeyColumns.Split(','))
                    ValidateIdentifier(col.Trim(), "主键列");
            }

            if (!string.IsNullOrEmpty(options.TimeColumn))
                ValidateIdentifier(options.TimeColumn, "时间列");

            if (options.SyncColumns != null)
            {
                foreach (var col in options.SyncColumns)
                    ValidateIdentifier(col?.Trim(), "同步列");
            }
        }

        /// <summary>
        /// 校验 SQL 标识符，拒绝包含引号、分号、空白等可用于注入的字符
        /// </summary>
        internal static void ValidateIdentifier(string value, string label)
        {
            if (string.IsNullOrWhiteSpace(value))
                return;

            if (!System.Text.RegularExpressions.Regex.IsMatch(value, @"^[A-Za-z_][A-Za-z0-9_$#@\.]*$"))
                throw new ArgumentException(string.Format("{0} 名称不合法: {1}", label, value));
        }

        /// <summary>
        /// 获取目标库的参数前缀（Oracle 使用 ':'，其余使用 '@'）
        /// </summary>
        internal static string GetParameterPrefix(DbConnection connection)
        {
            var typeName = connection?.GetType().FullName ?? string.Empty;
            return typeName.IndexOf("Oracle", StringComparison.OrdinalIgnoreCase) >= 0 ? ":" : "@";
        }

        private static void CreateIntermediateSchema(DataSyncOptions options)
        {
            if (string.IsNullOrWhiteSpace(options.IntermediateProvider) || string.IsNullOrWhiteSpace(options.IntermediateConnectionString))
                throw new ArgumentException("中间库 Provider 和连接字符串不能为空");

            using (var connection = CreateConnection(options.IntermediateProvider, options.IntermediateConnectionString))
            {
                connection.Open();
                var script = new IntermediateSchemaBuilder().BuildScript(options.IntermediateProvider);
                var statements = script.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var statement in statements)
                    ExecuteIgnoreError(connection, statement);
            }
        }

private static string BuildSourceSql(DataSyncOptions options, DbCommand command)
        {
            var columnList = options.SyncColumns != null && options.SyncColumns.Count > 0
                ? string.Join(",", options.SyncColumns)
                : "*";
            var sql = "select " + columnList + " from " + options.SourceTable;
            
            // 1. 如果启用了时间范围，按时间增量
            if (options.EnableTimeRange && !string.IsNullOrEmpty(options.TimeColumn))
            {
                var timeWhere = BuildTimeRangeFilter(options, command);
                if (!string.IsNullOrEmpty(timeWhere))
                    return sql + " where " + timeWhere;
            }
            
            // 2. 静态数据：按主键增量（如果有 LastPkValue/LastSyncTime 记录上次的主键最大值）
            if (!options.EnableTimeRange && !string.IsNullOrEmpty(options.PrimaryKeyColumns) && options.IsAutoIncrementKey)
            {
                var config = options.ConfigManager?.GetTaskConfig(options.TaskId);
                var lastPkValue = config?.LastPrimaryKeyValue;
                if (string.IsNullOrEmpty(lastPkValue) && config?.LastSyncTime.HasValue == true)
                {
                    // 兼容旧配置：主键最大值曾以 DateTime.Ticks 保存
                    lastPkValue = config.LastSyncTime.Value.Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture);
                }

                if (!string.IsNullOrEmpty(lastPkValue))
                {
                    var pkColumns = options.PrimaryKeyColumns.Split(',');
                    var prefix = GetParameterPrefix(command.Connection);
                    var parameter = command.CreateParameter();
                    parameter.ParameterName = prefix + "lastPkValue";
                    parameter.Value = long.TryParse(lastPkValue, out var parsedPk) ? (object)parsedPk : lastPkValue;
                    command.Parameters.Add(parameter);
                    return sql + " where " + pkColumns[0] + " > " + prefix + "lastPkValue";
                }
            }

            // 3. 没有时间范围且没有主键增量：全量查询
            return sql;
        }

        private static string BuildTimeRangeFilter(DataSyncOptions options, DbCommand command)
        {
            var timeColumn = options.TimeColumn;
            if (string.IsNullOrEmpty(timeColumn))
                return null;

            var startTime = DateTime.MinValue;
            var endTime = options.EndTime ?? DateTime.Now;

            // 首次同步：全量或从 0 开始
            var config = options.ConfigManager?.GetTaskConfig(options.TaskId);
            if (config == null || config.IsFirstSync)
            {
                if (options.IsFullSyncForFirstTime)
                    return null; // 首次全量
            }
            else if (config.LastSyncTime.HasValue)
            {
                // 后续同步：从 (上次同步时间 - rangeDays) 开始
                startTime = TimeRangeCalculator.GetSyncStartTime(config.LastSyncTime, options.RangeDays, false);
            }

            if (startTime == DateTime.MinValue)
                return null;

            var prefix = GetParameterPrefix(command.Connection);

            var parameterStart = command.CreateParameter();
            parameterStart.ParameterName = prefix + "startTime";
            parameterStart.Value = startTime;
            command.Parameters.Add(parameterStart);

            var parameterEnd = command.CreateParameter();
            parameterEnd.ParameterName = prefix + "endTime";
            parameterEnd.Value = endTime;
            command.Parameters.Add(parameterEnd);

            return timeColumn + " >= " + prefix + "startTime AND " + timeColumn + " <= " + prefix + "endTime";
        }

        private static DbConnection CreateConnection(string provider, string connectionString)
        {
            var factory = DbProviderFactories.GetFactory(provider);
            var connection = factory.CreateConnection();
            if (connection == null)
                throw new InvalidOperationException("无法创建数据库连接");

            connection.ConnectionString = connectionString;
            return connection;
        }

        /// <summary>
        /// 打开中间库连接；未配置中间库时返回 null
        /// </summary>
        private static DbConnection OpenIntermediateConnection(DataSyncOptions options)
        {
            if (string.IsNullOrWhiteSpace(options.IntermediateProvider) || string.IsNullOrWhiteSpace(options.IntermediateConnectionString))
                return null;

            var connection = CreateConnection(options.IntermediateProvider, options.IntermediateConnectionString);
            connection.Open();
            return connection;
        }

        /// <summary>
        /// 获取单条 SQL 语句允许的最大参数个数（按目标库类型）
        /// </summary>
        internal static int GetMaxParametersPerStatement(string provider)
        {
            if (string.IsNullOrEmpty(provider))
                return 900;

            var name = provider.ToLowerInvariant();
            if (name.Contains("sqlite"))
                return 900;
            if (name.Contains("mysql"))
                return 60000;
            if (name.Contains("npgsql") || name.Contains("postgres"))
                return 60000;
            if (name.Contains("sqlclient") || name.Contains("sqlserver"))
                return 2000;
            if (name.Contains("oracle"))
                return 1000;

            return 900;
        }

        private static bool TryInsertRowWithDedup(DbConnection connection, string tableName, DataTable table, DataRow row, int maxRetryCount, out int retryCount, out bool inserted, string primaryKeyColumns = null)
        {
            retryCount = 0;
            inserted = false;
            for (var i = 0; i <= maxRetryCount; i++)
            {
                try
                {
                    var exists = CheckRowExists(connection, tableName, table, row, primaryKeyColumns);
                    if (exists)
                    {
                        return true;
                    }
                    else
                    {
                        InsertRow(connection, tableName, table, row);
                        inserted = true;
                        return true;
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine(string.Format("TryInsertRowWithDedup attempt {0} failed: {1}", i, ex.Message));
                    if (i == maxRetryCount)
                        return false;

                    retryCount++;
                }
            }

            return false;
        }

        private static bool TryInsertRow(DbConnection connection, string tableName, DataTable table, DataRow row, int maxRetryCount, out int retryCount, string primaryKeyColumns = null)
        {
            retryCount = 0;
            for (var i = 0; i <= maxRetryCount; i++)
            {
                try
                {
                    UpsertRow(connection, tableName, table, row, primaryKeyColumns);
                    return true;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine(string.Format("TryInsertRow attempt {0} failed: {1}", i, ex.Message));
                    if (i == maxRetryCount)
                        return false;

                    retryCount++;
                }
            }

            return false;
        }

        private static void UpsertRow(DbConnection connection, string tableName, DataTable table, DataRow row, string primaryKeyColumns = null)
        {
            var exists = CheckRowExists(connection, tableName, table, row, primaryKeyColumns);
            if (exists)
                UpdateRow(connection, tableName, table, row, primaryKeyColumns);
            else
                InsertRow(connection, tableName, table, row);
        }

        private static bool CheckRowExists(DbConnection connection, string tableName, DataTable table, DataRow row, string primaryKeyColumns = null)
        {
            var pkColumns = GetPrimaryKeyColumns(table, primaryKeyColumns);
            if (pkColumns.Count == 0)
                return false;

            using (var command = connection.CreateCommand())
            {
                var prefix = GetParameterPrefix(connection);
                var conditions = new System.Collections.Generic.List<string>();
                for (var i = 0; i < pkColumns.Count; i++)
                {
                    var column = pkColumns[i];
                    var paramName = prefix + "pk" + i;
                    conditions.Add(column + " = " + paramName);

                    var parameter = command.CreateParameter();
                    parameter.ParameterName = paramName;
                    parameter.Value = row[column] == DBNull.Value ? DBNull.Value : row[column];
                    command.Parameters.Add(parameter);
                }

                command.CommandText = "select count(1) from " + tableName + " where " + string.Join(" AND ", conditions);
                var result = command.ExecuteScalar();
                return result != null && Convert.ToInt32(result) > 0;
            }
        }

        private static void UpdateRow(DbConnection connection, string tableName, DataTable table, DataRow row, string primaryKeyColumns = null)
        {
            using (var command = connection.CreateCommand())
            {
                var prefix = GetParameterPrefix(connection);
                var pkColumns = GetPrimaryKeyColumns(table, primaryKeyColumns);
                var setClauses = new System.Collections.Generic.List<string>();
                var whereClauses = new System.Collections.Generic.List<string>();

                for (var i = 0; i < table.Columns.Count; i++)
                {
                    var column = table.Columns[i].ColumnName;
                    var paramName = prefix + "p" + i;
                    setClauses.Add(column + " = " + paramName);

                    var parameter = command.CreateParameter();
                    parameter.ParameterName = paramName;
                    parameter.Value = row[column] == DBNull.Value ? DBNull.Value : row[column];
                    command.Parameters.Add(parameter);
                }

                for (var i = 0; i < pkColumns.Count; i++)
                {
                    var column = pkColumns[i];
                    var paramName = prefix + "pk" + i;
                    whereClauses.Add(column + " = " + paramName);

                    var parameter = command.CreateParameter();
                    parameter.ParameterName = paramName;
                    parameter.Value = row[column] == DBNull.Value ? DBNull.Value : row[column];
                    command.Parameters.Add(parameter);
                }

                command.CommandText = "update " + tableName + " set " + string.Join(",", setClauses) + " where " + string.Join(" AND ", whereClauses);
                command.ExecuteNonQuery();
            }
        }

        private static System.Collections.Generic.IList<string> GetPrimaryKeyColumns(DataTable table, string primaryKeyColumns = null)
        {
            var pkColumns = new System.Collections.Generic.List<string>();
            
            if (!string.IsNullOrEmpty(primaryKeyColumns))
            {
                foreach (var col in primaryKeyColumns.Split(','))
                {
                    var trimmed = col.Trim();
                    if (!string.IsNullOrEmpty(trimmed))
                        pkColumns.Add(trimmed);
                }
                return pkColumns;
            }
            
            foreach (DataColumn column in table.PrimaryKey)
            {
                pkColumns.Add(column.ColumnName);
            }
            return pkColumns;
        }

        internal static long? GetMaxPrimaryKeyValue(DataTable table, string pkColumnsStr)
        {
            if (table.Rows.Count == 0 || string.IsNullOrEmpty(pkColumnsStr))
                return null;

            var pkColumn = pkColumnsStr.Split(',')[0].Trim();
            if (!table.Columns.Contains(pkColumn))
                return null;

            long maxValue = 0;
            bool hasValue = false;

            foreach (DataRow row in table.Rows)
            {
                if (row[pkColumn] == DBNull.Value)
                    continue;

                // 非数值主键（如 GUID/字符串）无法作为增量游标，安全跳过
                if (!TryConvertToInt64(row[pkColumn], out var value))
                    return null;

                if (!hasValue || value > maxValue)
                {
                    maxValue = value;
                    hasValue = true;
                }
            }

            return hasValue ? maxValue : (long?)null;
        }

        internal static bool TryConvertToInt64(object value, out long result)
        {
            result = 0;
            if (value == null || value == DBNull.Value)
                return false;

            try
            {
                if (value is long l) { result = l; return true; }
                if (value is int i) { result = i; return true; }
                if (value is short s) { result = s; return true; }
                if (value is byte b) { result = b; return true; }
                result = Convert.ToInt64(value, System.Globalization.CultureInfo.InvariantCulture);
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 直接从数据库查询最大主键值（优化大表性能）
        /// </summary>
        private static long? GetMaxPrimaryKeyValueFromDb(string provider, string connectionString, string tableName, string pkColumnsStr)
        {
            if (string.IsNullOrEmpty(pkColumnsStr))
                return null;

            var pkColumn = pkColumnsStr.Split(',')[0].Trim();

            try
            {
                using (var connection = CreateConnection(provider, connectionString))
                {
                    connection.Open();
                    using (var command = connection.CreateCommand())
                    {
                        command.CommandText = string.Format("SELECT MAX({0}) FROM {1}", pkColumn, tableName);
                        var result = command.ExecuteScalar();
                        if (result != null && result != DBNull.Value && TryConvertToInt64(result, out var value))
                            return value;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(string.Format("查询最大主键值失败: {0}", ex.Message));
            }

            return null;
        }

        private static void SaveFailedRecord(DataSyncOptions options, DbConnection intermediateConnection, string taskId, DataTable table, DataRow row)
        {
            if (intermediateConnection == null)
                return;

            SaveFailedRecord(intermediateConnection, taskId, SerializeRow(table, row));
        }

        private static void SaveFailedRecord(DbConnection connection, string taskId, string payload)
        {
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "insert into fd_sync_record (record_id, batch_id, record_key, payload, status, retry_count, error_message) values (@record_id, @batch_id, @record_key, @payload, @status, @retry_count, @error_message)";
                AddParameter(command, "@record_id", Guid.NewGuid().ToString("N"));
                AddParameter(command, "@batch_id", taskId);
                AddParameter(command, "@record_key", DBNull.Value);
                AddParameter(command, "@payload", payload);
                AddParameter(command, "@status", "Failed");
                AddParameter(command, "@retry_count", 0);
                AddParameter(command, "@error_message", "写入目标库失败");
                command.ExecuteNonQuery();
            }
        }

        private static int ResumeFailedRecords(DataSyncOptions options, DbConnection target, DbConnection intermediate, int maxRetryCount)
        {
            if (intermediate == null)
                return 0;

            var recoveredCount = 0;
            var failedRecords = LoadFailedRecords(intermediate);
            foreach (DataRow row in failedRecords.Rows)
            {
                var recordId = Convert.ToString(row["record_id"]);
                var payload = Convert.ToString(row["payload"]);
                var data = BuildDataTableFromPayload(payload);
                if (data.Rows.Count == 0)
                    continue;

                int rowRetryCount;
                // 使用配置的主键列进行去重，避免重复插入导致主键冲突
                if (TryInsertRow(target, options.TargetTable, data, data.Rows[0], maxRetryCount, out rowRetryCount, options.PrimaryKeyColumns))
                {
                    MarkRecordSuccess(intermediate, recordId);
                    recoveredCount++;
                }
                else
                {
                    // 恢复失败：累加重试次数，便于后续排查
                    IncrementRecordRetry(intermediate, recordId, "恢复失败记录时写入目标库失败");
                }
            }

            return recoveredCount;
        }

        private static DataTable LoadFailedRecords(DbConnection connection)
        {
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "select record_id, payload from fd_sync_record where status = 'Failed'";
                var table = new DataTable();
                using (var reader = command.ExecuteReader())
                    table.Load(reader);
                return table;
            }
        }

        private static void MarkRecordSuccess(DbConnection connection, string recordId)
        {
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "update fd_sync_record set status = 'Success' where record_id = @record_id";
                AddParameter(command, "@record_id", recordId);
                command.ExecuteNonQuery();
            }
        }

        private static void IncrementRecordRetry(DbConnection connection, string recordId, string errorMessage)
        {
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "update fd_sync_record set retry_count = retry_count + 1, error_message = @error_message where record_id = @record_id";
                AddParameter(command, "@record_id", recordId);
                AddParameter(command, "@error_message", errorMessage);
                command.ExecuteNonQuery();
            }
        }

        private static void InsertRowBatch(DbConnection connection, string tableName, DataTable table, List<DataRow> rows)
        {
            if (rows.Count == 0) return;

            using (var command = connection.CreateCommand())
            {
                var prefix = GetParameterPrefix(connection);
                var columns = new string[table.Columns.Count];
                var valueRows = new List<string>();
                var paramIndex = 0;

                for (var i = 0; i < table.Columns.Count; i++)
                {
                    columns[i] = table.Columns[i].ColumnName;
                }

                foreach (var row in rows)
                {
                    var rowParams = new List<string>();
                    for (var i = 0; i < table.Columns.Count; i++)
                    {
                        var paramName = prefix + "p" + paramIndex++;
                        rowParams.Add(paramName);

                        var parameter = command.CreateParameter();
                        parameter.ParameterName = paramName;
                        parameter.Value = row[columns[i]] == DBNull.Value ? DBNull.Value : row[columns[i]];
                        command.Parameters.Add(parameter);
                    }
                    valueRows.Add("(" + string.Join(",", rowParams) + ")");
                }

                command.CommandText = "insert into " + tableName + " (" + string.Join(",", columns) + ") values " + string.Join(",", valueRows);
                command.ExecuteNonQuery();
            }
        }

        private static void InsertRow(DbConnection connection, string tableName, DataTable table, DataRow row)
        {
            using (var command = connection.CreateCommand())
            {
                var prefix = GetParameterPrefix(connection);
                var columns = new string[table.Columns.Count];
                var parameters = new string[table.Columns.Count];
                for (var i = 0; i < table.Columns.Count; i++)
                {
                    var column = table.Columns[i].ColumnName;
                    var parameterName = prefix + "p" + i;
                    columns[i] = column;
                    parameters[i] = parameterName;

                    var parameter = command.CreateParameter();
                    parameter.ParameterName = parameterName;
                    parameter.Value = row[column] == DBNull.Value ? DBNull.Value : row[column];
                    command.Parameters.Add(parameter);
                }

                command.CommandText = "insert into " + tableName + " (" + string.Join(",", columns) + ") values (" + string.Join(",", parameters) + ")";
                command.ExecuteNonQuery();
            }
        }

        private static void AddParameter(DbCommand command, string name, object value)
        {
            var parameter = command.CreateParameter();
            parameter.ParameterName = name;
            parameter.Value = value ?? DBNull.Value;
            command.Parameters.Add(parameter);
        }

        private static string SerializeRow(DataTable table, DataRow row)
        {
            return DataRowSerializer.Serialize(table, row);
        }

        private static DataTable BuildDataTableFromPayload(string payload)
        {
            return DataRowSerializer.Deserialize(payload);
        }

        private static void CleanIntermediateData(DataSyncOptions options, DbConnection intermediate)
        {
            if (intermediate == null)
                return;

            ExecuteIgnoreError(intermediate, "delete from fd_sync_record where status = 'Success'");
            ExecuteIgnoreError(intermediate, "delete from fd_sync_batch where status = 'Success'");
        }

        private static void ExecuteIgnoreError(DbConnection connection, string sql)
        {
            try
            {
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = sql;
                    command.ExecuteNonQuery();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(string.Format("清理中间库数据失败: {0}, 错误: {1}", sql, ex.Message));
            }
        }
    }
}
