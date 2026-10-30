using System;
using FastData.Context;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace FastData.DevTools
{
    /// <summary>
    /// 任务调度器
    /// </summary>
    public static class TaskScheduler
    {
        private static readonly ConcurrentDictionary<string, ScheduledTask> _scheduledTasks = new ConcurrentDictionary<string, ScheduledTask>();
        private static readonly ConcurrentDictionary<string, CancellationTokenSource> _cancellationTokens = new ConcurrentDictionary<string, CancellationTokenSource>();
        private static readonly object _lock = new object();

        /// <summary>
        /// 调度任务
        /// </summary>
        public static string Schedule(string name, Action action, TimeSpan delay, bool repeat = false)
        {
            return Schedule(name, () => { action(); return Task.CompletedTask; }, delay, repeat);
        }

        /// <summary>
        /// 调度异步任务
        /// </summary>
        public static string Schedule(string name, Func<Task> action, TimeSpan delay, bool repeat = false)
        {
            var taskId = string.Format("{0}_{1:N}", name, Guid.NewGuid());

            var cts = new CancellationTokenSource();
            _cancellationTokens[taskId] = cts;

            var task = new ScheduledTask
            {
                TaskId = taskId,
                Name = name,
                Action = action,
                Delay = delay,
                Repeat = repeat,
                CreatedAt = DateTime.Now,
                Status = TaskStatus.Scheduled
            };

            _scheduledTasks[taskId] = task;

            _ = RunTask(task, cts.Token);

            return taskId;
        }

        /// <summary>
        /// 按Cron表达式调度任务
        /// </summary>
        public static string ScheduleCron(string name, Action action, string cronExpression)
        {
            return ScheduleCron(name, () => { action(); return Task.CompletedTask; }, cronExpression);
        }

        /// <summary>
        /// 按Cron表达式调度异步任务
        /// </summary>
        public static string ScheduleCron(string name, Func<Task> action, string cronExpression)
        {
            // 简化的 Cron 解析，实际应用中应使用专门的 Cron 库
            var delay = ParseSimpleCron(cronExpression);
            return Schedule(name, action, delay, repeat: true);
        }

        /// <summary>
        /// 取消任务
        /// </summary>
        public static bool Cancel(string taskId)
        {
            if (_cancellationTokens.TryRemove(taskId, out var cts))
            {
                cts.Cancel();

                if (_scheduledTasks.TryGetValue(taskId, out var task))
                {
                    task.Status = TaskStatus.Cancelled;
                }

                return true;
            }

            return false;
        }

        /// <summary>
        /// 取消所有任务
        /// </summary>
        public static void CancelAll()
        {
            foreach (var taskId in _cancellationTokens.Keys.ToList())
            {
                Cancel(taskId);
            }
        }

        /// <summary>
        /// 获取任务信息
        /// </summary>
        public static ScheduledTask GetTask(string taskId)
        {
            return _scheduledTasks.TryGetValue(taskId, out var task) ? task : null;
        }

        /// <summary>
        /// 获取所有任务
        /// </summary>
        public static List<ScheduledTask> GetAllTasks()
        {
            return _scheduledTasks.Values.ToList();
        }

        /// <summary>
        /// 获取活动任务
        /// </summary>
        public static List<ScheduledTask> GetActiveTasks()
        {
            return _scheduledTasks.Values.Where(t => t.Status == TaskStatus.Running || t.Status == TaskStatus.Scheduled).ToList();
        }

        /// <summary>
        /// 等待任务完成
        /// </summary>
        public static async Task WaitForCompletion(string taskId)
        {
            while (true)
            {
                if (_scheduledTasks.TryGetValue(taskId, out var task))
                {
                    if (task.Status == TaskStatus.Completed || task.Status == TaskStatus.Failed || task.Status == TaskStatus.Cancelled)
                    {
                        return;
                    }
                }
                else
                {
                    return;
                }

                await Task.Delay(100);
            }
        }

        /// <summary>
        /// 立即执行任务
        /// </summary>
        public static async Task ExecuteNow(string taskId)
        {
            if (_scheduledTasks.TryGetValue(taskId, out var task))
            {
                await RunTask(task, _cancellationTokens[taskId].Token, immediate: true);
            }
        }

        private static async Task RunTask(ScheduledTask task, CancellationToken cancellationToken, bool immediate = false)
        {
            if (immediate)
            {
                await ExecuteTask(task, cancellationToken);
            }
            else
            {
                try
                {
                    await Task.Delay(task.Delay, cancellationToken);

                    if (cancellationToken.IsCancellationRequested)
                    {
                        task.Status = TaskStatus.Cancelled;
                        return;
                    }

                    await ExecuteTask(task, cancellationToken);

                    if (task.Repeat && !cancellationToken.IsCancellationRequested)
                    {
                        _ = RunTask(task, cancellationToken);
                    }
                }
                catch (OperationCanceledException)
                {
                    task.Status = TaskStatus.Cancelled;
                }
                catch (Exception ex)
                {
                    task.Status = TaskStatus.Failed;
                    task.LastError = ex.Message;
                    LogAggregator.Exception(ex, string.Format("任务执行失败: {0}", task.Name), "TaskScheduler");
                }
            }
        }

        private static async Task ExecuteTask(ScheduledTask task, CancellationToken cancellationToken)
        {
            task.Status = TaskStatus.Running;
            task.LastRunAt = DateTime.Now;
            task.RunCount++;

            try
            {
                await task.Action();

                task.Status = task.Repeat ? TaskStatus.Scheduled : TaskStatus.Completed;
                task.LastError = null;
            }
            catch (OperationCanceledException)
            {
                task.Status = TaskStatus.Cancelled;
                throw;
            }
            catch (Exception ex)
            {
                task.Status = TaskStatus.Failed;
                task.LastError = ex.Message;
                throw;
            }
        }

        private static TimeSpan ParseSimpleCron(string cronExpression)
        {
            // 仅支持「秒 分 时」三段式，不是标准五段 cron。
            if (string.IsNullOrWhiteSpace(cronExpression))
            {
                throw new ArgumentException(
                    "Cron 表达式不能为空。本库仅支持「秒 分 时」三段式（如 \"30 0 0\" 表示 30 秒），" +
                    "不支持标准五段 cron。需要完整 cron 请改用 Quartz.NET 等专用库。",
                    nameof(cronExpression));
            }

            var parts = cronExpression.Split(' ');

            if (parts.Length != 3)
            {
                throw new ArgumentException(string.Format(
                    "无法解析 Cron 表达式 \"{0}\"：本库仅支持「秒 分 时」三段式（如 \"30 0 0\"），" +
                    "不支持标准五段 cron。需要完整 cron 请改用 Quartz.NET 等专用库。",
                    cronExpression), nameof(cronExpression));
            }

            int seconds, minutes, hours;
            if (!int.TryParse(parts[0], out seconds) ||
                !int.TryParse(parts[1], out minutes) ||
                !int.TryParse(parts[2], out hours))
            {
                throw new ArgumentException(string.Format(
                    "无法解析 Cron 表达式 \"{0}\"：三段都必须是整数（秒 分 时）。",
                    cronExpression), nameof(cronExpression));
            }

            if (seconds < 0 || minutes < 0 || hours < 0)
            {
                throw new ArgumentException(string.Format(
                    "无法解析 Cron 表达式 \"{0}\"：时间分量不能为负。", cronExpression), nameof(cronExpression));
            }

            var delay = new TimeSpan(hours, minutes, seconds);

            // 全零表示「立即重复」，会变成忙循环，必须拦下
            if (delay == TimeSpan.Zero)
            {
                throw new ArgumentException(string.Format(
                    "Cron 表达式 \"{0}\" 解析为 0 间隔，会导致忙循环。请指定至少 1 秒。",
                    cronExpression), nameof(cronExpression));
            }

            return delay;
        }
    }

    /// <summary>
    /// 调度任务
    /// </summary>
    public class ScheduledTask
    {
        /// <summary>
        /// 任务 ID
        /// </summary>
        public string TaskId { get; set; }
        /// <summary>
        /// 任务名称
        /// </summary>
        public string Name { get; set; }
        /// <summary>
        /// 任务动作
        /// </summary>
        public Func<Task> Action { get; set; }
        /// <summary>
        /// 调度延迟
        /// </summary>
        public TimeSpan Delay { get; set; }
        /// <summary>
        /// 是否重复执行
        /// </summary>
        public bool Repeat { get; set; }
        /// <summary>
        /// 创建时间
        /// </summary>
        public DateTime CreatedAt { get; set; }
        /// <summary>
        /// 上次运行时间
        /// </summary>
        public DateTime? LastRunAt { get; set; }
        /// <summary>
        /// 运行次数
        /// </summary>
        public int RunCount { get; set; }
        /// <summary>
        /// 任务状态
        /// </summary>
        public TaskStatus Status { get; set; }
        /// <summary>
        /// 最后一次错误信息
        /// </summary>
        public string LastError { get; set; }
    }

    /// <summary>
    /// 任务状态
    /// </summary>
    public enum TaskStatus
    {
        /// <summary>
        /// 已调度
        /// </summary>
        Scheduled,
        /// <summary>
        /// 运行中
        /// </summary>
        Running,
        /// <summary>
        /// 已完成
        /// </summary>
        Completed,
        /// <summary>
        /// 已失败
        /// </summary>
        Failed,
        /// <summary>
        /// 已取消
        /// </summary>
        Cancelled
    }

    /// <summary>
    /// 任务管理器
    /// </summary>
    public static class TaskManager
    {
        private static readonly ConcurrentDictionary<string, RunningTask> _runningTasks = new ConcurrentDictionary<string, RunningTask>();

        /// <summary>
        /// 创建任务
        /// </summary>
        public static string Create(string name, Func<Task> action)
        {
            var taskId = string.Format("{0}_{1:N}", name, Guid.NewGuid());

            var task = new RunningTask
            {
                TaskId = taskId,
                Name = name,
                Action = action,
                CreatedAt = DateTime.Now,
                Status = TaskState.Pending
            };

            _runningTasks[taskId] = task;

            return taskId;
        }

        /// <summary>
        /// 启动任务
        /// </summary>
        public static async Task Start(string taskId)
        {
            if (_runningTasks.TryGetValue(taskId, out var task))
            {
                task.Status = TaskState.Running;
                task.StartedAt = DateTime.Now;

                try
                {
                    await task.Action();
                    task.Status = TaskState.Completed;
                    task.CompletedAt = DateTime.Now;
                }
                catch (Exception ex)
                {
                    task.Status = TaskState.Failed;
                    task.CompletedAt = DateTime.Now;
                    task.Error = ex.Message;
                    LogAggregator.Exception(ex, string.Format("任务执行失败: {0}", task.Name), "TaskManager");
                }
            }
        }

        /// <summary>
        /// 创建并启动任务
        /// </summary>
        public static string StartNew(string name, Func<Task> action)
        {
            var taskId = Create(name, action);
            _ = Task.Run(() => Start(taskId));
            return taskId;
        }

        /// <summary>
        /// 取消任务
        /// </summary>
        public static bool Cancel(string taskId)
        {
            if (_runningTasks.TryGetValue(taskId, out var task))
            {
                task.Status = TaskState.Cancelled;
                task.CompletedAt = DateTime.Now;
                return true;
            }
            return false;
        }

        /// <summary>
        /// 获取任务信息
        /// </summary>
        public static RunningTask GetTask(string taskId)
        {
            return _runningTasks.TryGetValue(taskId, out var task) ? task : null;
        }

        /// <summary>
        /// 获取所有任务
        /// </summary>
        public static List<RunningTask> GetAllTasks()
        {
            return _runningTasks.Values.ToList();
        }

        /// <summary>
        /// 清理已完成的任务
        /// </summary>
        public static int Cleanup()
        {
            var completedTasks = _runningTasks.Values
                .Where(t => t.Status == TaskState.Completed || t.Status == TaskState.Failed || t.Status == TaskState.Cancelled)
                .ToList();

            foreach (var task in completedTasks)
            {
                _runningTasks.TryRemove(task.TaskId, out _);
            }

            return completedTasks.Count;
        }

        /// <summary>
        /// 获取任务统计
        /// </summary>
        public static TaskStatistics GetStatistics()
        {
            var tasks = _runningTasks.Values.ToList();

            return new TaskStatistics
            {
                TotalTasks = tasks.Count,
                PendingTasks = tasks.Count(t => t.Status == TaskState.Pending),
                RunningTasks = tasks.Count(t => t.Status == TaskState.Running),
                CompletedTasks = tasks.Count(t => t.Status == TaskState.Completed),
                FailedTasks = tasks.Count(t => t.Status == TaskState.Failed),
                CancelledTasks = tasks.Count(t => t.Status == TaskState.Cancelled),
                AverageDuration = tasks.Where(t => t.StartedAt.HasValue && t.CompletedAt.HasValue)
                    .Average(t => (t.CompletedAt.Value - t.StartedAt.Value).TotalMilliseconds)
            };
        }
    }

    /// <summary>
    /// 运行中的任务
    /// </summary>
    public class RunningTask
    {
        /// <summary>
        /// 任务 ID
        /// </summary>
        public string TaskId { get; set; }
        /// <summary>
        /// 任务名称
        /// </summary>
        public string Name { get; set; }
        /// <summary>
        /// 任务动作
        /// </summary>
        public Func<Task> Action { get; set; }
        /// <summary>
        /// 创建时间
        /// </summary>
        public DateTime CreatedAt { get; set; }
        /// <summary>
        /// 启动时间
        /// </summary>
        public DateTime? StartedAt { get; set; }
        /// <summary>
        /// 完成时间
        /// </summary>
        public DateTime? CompletedAt { get; set; }
        /// <summary>
        /// 任务状态
        /// </summary>
        public TaskState Status { get; set; }
        /// <summary>
        /// 错误信息
        /// </summary>
        public string Error { get; set; }
    }

    /// <summary>
    /// 任务状态
    /// </summary>
    public enum TaskState
    {
        /// <summary>
        /// 待处理
        /// </summary>
        Pending,
        /// <summary>
        /// 运行中
        /// </summary>
        Running,
        /// <summary>
        /// 已完成
        /// </summary>
        Completed,
        /// <summary>
        /// 已失败
        /// </summary>
        Failed,
        /// <summary>
        /// 已取消
        /// </summary>
        Cancelled
    }

    /// <summary>
    /// 任务统计
    /// </summary>
    public class TaskStatistics
    {
        /// <summary>
        /// 任务总数
        /// </summary>
        public int TotalTasks { get; set; }
        /// <summary>
        /// 待处理任务数
        /// </summary>
        public int PendingTasks { get; set; }
        /// <summary>
        /// 运行中任务数
        /// </summary>
        public int RunningTasks { get; set; }
        /// <summary>
        /// 已完成任务数
        /// </summary>
        public int CompletedTasks { get; set; }
        /// <summary>
        /// 失败任务数
        /// </summary>
        public int FailedTasks { get; set; }
        /// <summary>
        /// 已取消任务数
        /// </summary>
        public int CancelledTasks { get; set; }
        /// <summary>
        /// 平均耗时（毫秒）
        /// </summary>
        public double AverageDuration { get; set; }
    }

    /// <summary>
    /// 批量任务执行器
    /// </summary>
    public static class BatchTaskExecutor
    {
        /// <summary>
        /// 批量执行任务
        /// </summary>
        public static async Task<List<BatchTaskResult>> ExecuteAsync<T>(
            IEnumerable<T> items,
            Func<T, Task> action,
            int batchSize = 10,
            int maxConcurrency = 5)
        {
            var results = new List<BatchTaskResult>();
            var semaphore = new System.Threading.SemaphoreSlim(maxConcurrency);

            foreach (var batch in items.Batch(batchSize))
            {
                var tasks = batch.Select(async item =>
                {
                    await semaphore.WaitAsync();
                    try
                    {
                        var startTime = DateTime.Now;
                        await action(item);
                        var duration = DateTime.Now - startTime;

                        return new BatchTaskResult
                        {
                            Item = item,
                            Success = true,
                            Duration = duration
                        };
                    }
                    catch (Exception ex)
                    {
                        return new BatchTaskResult
                        {
                            Item = item,
                            Success = false,
                            Error = ex.Message,
                            Duration = TimeSpan.Zero
                        };
                    }
                    finally
                    {
                        semaphore.Release();
                    }
                });

                var batchResults = await Task.WhenAll(tasks);
                results.AddRange(batchResults);
            }

            return results;
        }

        /// <summary>
        /// 带重试的批量执行
        /// </summary>
        public static async Task<List<BatchTaskResult>> ExecuteWithRetryAsync<T>(
            IEnumerable<T> items,
            Func<T, Task> action,
            int maxRetries = 3,
            TimeSpan? retryDelay = null)
        {
            retryDelay = retryDelay ?? TimeSpan.FromSeconds(1);
            var results = new List<BatchTaskResult>();

            foreach (var item in items)
            {
                var result = new BatchTaskResult { Item = item };
                var attempt = 0;

                while (attempt <= maxRetries)
                {
                    try
                    {
                        var startTime = DateTime.Now;
                        await action(item);
                        result.Duration = DateTime.Now - startTime;
                        result.Success = true;
                        break;
                    }
                    catch (Exception ex)
                    {
                        attempt++;
                        if (attempt > maxRetries)
                        {
                            result.Error = ex.Message;
                            result.Success = false;
                        }
                        else
                        {
                            await Task.Delay(retryDelay.Value);
                        }
                    }
                }

                results.Add(result);
            }

            return results;
        }
    }

    /// <summary>
    /// 批量任务结果
    /// </summary>
    public class BatchTaskResult
    {
        /// <summary>
        /// 任务对应的数据项
        /// </summary>
        public object Item { get; set; }
        /// <summary>
        /// 是否成功
        /// </summary>
        public bool Success { get; set; }
        /// <summary>
        /// 错误信息
        /// </summary>
        public string Error { get; set; }
        /// <summary>
        /// 耗时
        /// </summary>
        public TimeSpan Duration { get; set; }
    }

    /// <summary>
    /// 批处理扩展方法
    /// </summary>
    public static class BatchExtensions
    {
        /// <summary>
        /// 将序列按指定大小分批
        /// </summary>
        /// <typeparam name="T">元素类型</typeparam>
        /// <param name="source">源序列</param>
        /// <param name="batchSize">每批大小</param>
        /// <returns>分批后的序列</returns>
        public static IEnumerable<IEnumerable<T>> Batch<T>(this IEnumerable<T> source, int batchSize)
        {
            var batch = new List<T>(batchSize);

            foreach (var item in source)
            {
                batch.Add(item);

                if (batch.Count == batchSize)
                {
                    yield return batch;
                    batch = new List<T>(batchSize);
                }
            }

            if (batch.Count > 0)
            {
                yield return batch;
            }
        }
    }
}