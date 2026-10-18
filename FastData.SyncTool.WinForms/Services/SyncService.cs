using System;
using System.Collections.Generic;
using System.Threading;
using FastData.Tooling.Sync;

namespace FastData.SyncTool.WinForms.Services
{
    public class SyncExecutionResult
    {
        public bool Success { get; set; }
        public int ReadCount { get; set; }
        public int WriteCount { get; set; }
        public int FailedCount { get; set; }
        public int RetryCount { get; set; }
        public int RecoveredCount { get; set; }
        public TimeSpan Duration { get; set; }
        public string ErrorMessage { get; set; }
        public string SourceTable { get; set; }
        public string TargetTable { get; set; }
    }

    public class BatchSyncResult
    {
        public bool Success { get; set; }
        public int TotalTables { get; set; }
        public int SuccessTables { get; set; }
        public int FailedTables { get; set; }
        public int SkippedTables { get; set; }
        public int TotalRead { get; set; }
        public int TotalWrite { get; set; }
        public int TotalFailed { get; set; }
        public TimeSpan TotalDuration { get; set; }
        public List<SyncExecutionResult> TableResults { get; set; } = new List<SyncExecutionResult>();
        public string ErrorMessage { get; set; }
    }

    public class SyncProgressInfo
    {
        public string CurrentTable { get; set; }
        public int CompletedTables { get; set; }
        public int TotalTables { get; set; }
        public int CurrentRead { get; set; }
        public int CurrentWrite { get; set; }
        public string Status { get; set; }
    }

    public class SyncService : IDisposable
    {
        private readonly DataSyncService syncService = new DataSyncService();
        private readonly object ctsLock = new object();
        private CancellationTokenSource cts;
        private bool disposed;

        public event EventHandler<SyncProgressInfo> ProgressChanged;
        public event EventHandler<SyncExecutionResult> TableCompleted;

        public SyncExecutionResult ExecuteSync(DataSyncOptions options, CancellationToken cancellationToken = default(CancellationToken))
        {
            var result = new SyncExecutionResult();
            var startTime = DateTime.Now;
            var taskId = string.IsNullOrWhiteSpace(options.TaskId) ? "sync" : options.TaskId;
            result.SourceTable = options.SourceTable;
            result.TargetTable = options.TargetTable;

            try
            {
                Logger.Write(LogLevel.Info, string.Format("开始同步: {0} -> {1}", options.SourceTable, options.TargetTable), taskId);

                var syncResult = syncService.SyncTable(options, cancellationToken);

                result.ReadCount = syncResult.ReadCount;
                result.WriteCount = syncResult.WriteCount;
                result.FailedCount = syncResult.FailedCount;
                result.RetryCount = syncResult.RetryCount;
                result.RecoveredCount = syncResult.RecoveredCount;
                result.Duration = DateTime.Now - startTime;
                result.Success = syncResult.FailedCount == 0;

                Logger.Write(LogLevel.Info, string.Format("同步完成: 读取 {0}, 写入 {1}, 失败 {2}", result.ReadCount, result.WriteCount, result.FailedCount), taskId);
            }
            catch (OperationCanceledException)
            {
                result.Success = false;
                result.ErrorMessage = "用户取消";
                result.Duration = DateTime.Now - startTime;
                Logger.Write(LogLevel.Warn, "同步已取消", taskId);
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = ex.Message;
                result.Duration = DateTime.Now - startTime;
                Logger.Write(LogLevel.Error, "同步失败: " + ex.Message, taskId);
            }
            return result;
        }

        public BatchSyncResult ExecuteBatchSync(IList<TableSyncConfig> tableConfigs, DataSyncOptions baseOptions, Func<DataSyncOptions, TableSyncConfig, DataSyncOptions> optionsBuilder)
        {
            var batchResult = new BatchSyncResult();
            var startTime = DateTime.Now;
            CancellationToken token;
            lock (ctsLock)
            {
                if (cts != null)
                {
                    cts.Dispose();
                }
                cts = new CancellationTokenSource();
                token = cts.Token;
            }

            var enabledConfigs = new List<TableSyncConfig>();
            foreach (var tc in tableConfigs)
            {
                if (tc.IsEnabled) enabledConfigs.Add(tc);
            }
            batchResult.TotalTables = enabledConfigs.Count;

            for (int i = 0; i < enabledConfigs.Count; i++)
            {
                if (token.IsCancellationRequested)
                {
                    batchResult.SkippedTables = enabledConfigs.Count - i;
                    batchResult.ErrorMessage = "用户取消";
                    break;
                }

                var tc = enabledConfigs[i];
                OnProgressChanged(new SyncProgressInfo
                {
                    CurrentTable = tc.TableName,
                    CompletedTables = i,
                    TotalTables = enabledConfigs.Count,
                    Status = "正在同步: " + tc.TableName
                });

                var options = optionsBuilder != null ? optionsBuilder(baseOptions, tc) : baseOptions;
                var result = ExecuteSync(options, token);

                batchResult.TableResults.Add(result);
                if (result.Success) batchResult.SuccessTables++;
                else batchResult.FailedTables++;

                batchResult.TotalRead += result.ReadCount;
                batchResult.TotalWrite += result.WriteCount;
                batchResult.TotalFailed += result.FailedCount;

                OnTableCompleted(result);
            }

            batchResult.TotalDuration = DateTime.Now - startTime;
            batchResult.Success = batchResult.FailedTables == 0 && batchResult.SkippedTables == 0;
            return batchResult;
        }

        public void CancelBatchSync()
        {
            CancellationTokenSource source;
            lock (ctsLock)
            {
                source = cts;
            }

            if (source != null && !source.IsCancellationRequested)
            {
                source.Cancel();
                Logger.Write(LogLevel.Warn, "用户取消批量同步", null);
            }
        }

        private void OnProgressChanged(SyncProgressInfo info)
        {
            ProgressChanged?.Invoke(this, info);
        }

        private void OnTableCompleted(SyncExecutionResult result)
        {
            TableCompleted?.Invoke(this, result);
        }

        public void Dispose()
        {
            if (!disposed)
            {
                lock (ctsLock)
                {
                    if (cts != null) { cts.Dispose(); cts = null; }
                }
                disposed = true;
            }
        }
    }
}
