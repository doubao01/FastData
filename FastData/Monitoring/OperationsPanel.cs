using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FastData.ConnectionPool;
using FastUntility.Monitor;
using FastUntility.Security;

namespace FastData.Monitoring
{
    /// <summary>
    /// 运维面板：把散落在各处的健康指标聚合到一个快照里，并在出问题时推送告警。
    /// <para>
    /// 解决的问题：这个库本身有服务器监控、连接池指标、Redis 状态、接口探测、性能统计、
    /// 异常通知六套能力，但分布在六个不同的类里，没有一个地方能一次看到全部状态。
    /// 运维要判断「现在到底有没有问题」，得逐个调用、自己拼结论。
    /// </para>
    /// <para>
    /// 用法：
    /// <code>
    /// var panel = new OperationsPanel(new OperationsPanelOptions
    /// {
    ///     ProbeUrls = { "https://api.example.com/health" }
    /// })
    /// .WithSender(FastIM.Sender)
    /// .WithAlertUsers("10001");
    ///
    /// // 定时任务里跑：采集 + 异常时自动推 QQ
    /// await panel.RunAsync();
    ///
    /// // 或者挂到宿主自己的 /health 端点
    /// var snapshot = await panel.CollectAsync();
    /// return snapshot.ToJson();
    /// </code>
    /// </para>
    /// <para>
    /// 设计取舍：不依赖 ASP.NET Core。这是 ORM 库，不该把宿主绑死在某个 Web 框架上。
    /// 输出 JSON 字符串，怎么暴露由宿主决定。
    /// </para>
    /// </summary>
    public class OperationsPanel
    {
        private readonly OperationsPanelOptions _options;
        private readonly object _alertLock = new object();

        private IMessageSender _sender;
        private List<string> _alertUsers = new List<string>();
        private List<string> _alertGroups = new List<string>();

        private HealthLevel _lastAlertedLevel = HealthLevel.Ok;
        private DateTime _lastAlertAt = DateTime.MinValue;

        /// <summary>
        /// 创建运维面板。
        /// </summary>
        /// <param name="options">配置；为空则用默认值（全开、阈值用内置值）</param>
        public OperationsPanel(OperationsPanelOptions options = null)
        {
            _options = options ?? new OperationsPanelOptions();
        }

        /// <summary>
        /// 指定告警发送器。不指定则 <see cref="RunAsync"/> 只采集不推送。
        /// </summary>
        /// <param name="sender">消息发送器，可用 <c>FastIM.Sender</c></param>
        public OperationsPanel WithSender(IMessageSender sender)
        {
            _sender = sender;
            return this;
        }

        /// <summary>
        /// 指定接收告警的 QQ 号。
        /// </summary>
        public OperationsPanel WithAlertUsers(params string[] qqNumbers)
        {
            if (qqNumbers != null)
                _alertUsers = qqNumbers.Where(n => !string.IsNullOrWhiteSpace(n)).ToList();
            return this;
        }

        /// <summary>
        /// 指定接收告警的 QQ 群号。
        /// </summary>
        public OperationsPanel WithAlertGroups(params string[] groupNumbers)
        {
            if (groupNumbers != null)
                _alertGroups = groupNumbers.Where(n => !string.IsNullOrWhiteSpace(n)).ToList();
            return this;
        }

        /// <summary>
        /// 采集一次快照。不推送。
        /// </summary>
        public async Task<OperationsSnapshot> CollectAsync(CancellationToken cancellationToken = default)
        {
            var stopwatch = Stopwatch.StartNew();
            var snapshot = new OperationsSnapshot { Timestamp = DateTime.Now };

            var tasks = new List<Task<List<HealthItem>>>();

            if (_options.CollectServer)
                tasks.Add(Task.Run(() => CollectServer()));
            if (_options.CollectConnectionPools)
                tasks.Add(Task.Run(() => CollectConnectionPools()));
            if (_options.CollectRedis)
                tasks.Add(Task.Run(() => CollectRedis()));
            if (_options.CollectPerformance)
                tasks.Add(Task.Run(() => CollectPerformance()));

            // 接口探测本身是 IO，单独走
            if (_options.ProbeUrls != null && _options.ProbeUrls.Count > 0)
                tasks.Add(ProbeEndpointsAsync(cancellationToken));

            foreach (var task in tasks)
            {
                List<HealthItem> items;
                try
                {
                    items = await task.ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    items = new List<HealthItem>
                    {
                        new HealthItem
                        {
                            Name = "采集失败",
                            Level = HealthLevel.Warning,
                            Message = string.Format("{0}: {1}", ex.GetType().Name, ex.Message)
                        }
                    };
                }

                if (items != null)
                    snapshot.Items.AddRange(items);
            }

            stopwatch.Stop();
            snapshot.Duration = stopwatch.Elapsed;
            snapshot.Overall = snapshot.Items.Count == 0
                ? HealthLevel.Ok
                : snapshot.Items.Max(i => i.Level);

            return snapshot;
        }

        /// <summary>
        /// 采集并在需要时推送告警。
        /// <para>
        /// 推送条件（两个都要满足）：
        /// 1. 距上次推送超过 <see cref="OperationsPanelOptions.AlertThrottleSeconds"/>
        /// 2. 等级比上次推送时更差（或恢复为正常）
        /// </para>
        /// </summary>
        /// <returns>本次采集的快照，调用方可自行记录</returns>
        public async Task<OperationsSnapshot> RunAsync(CancellationToken cancellationToken = default)
        {
            var snapshot = await CollectAsync(cancellationToken).ConfigureAwait(false);

            if (_sender == null)
                return snapshot;

            if (!ShouldAlert(snapshot.Overall))
                return snapshot;

            var message = BuildAlertMessage(snapshot);

            foreach (var user in _alertUsers)
            {
                try { _sender.SendPrivateMessage(user, message); }
                catch { /* 单个接收者失败不影响其他人 */ }
            }

            foreach (var group in _alertGroups)
            {
                try { _sender.SendGroupMessage(group, message); }
                catch { /* 同上 */ }
            }

            lock (_alertLock)
            {
                _lastAlertedLevel = snapshot.Overall;
                _lastAlertAt = DateTime.Now;
            }

            return snapshot;
        }

        /// <summary>
        /// 判断这次是否该推送。
        /// </summary>
        private bool ShouldAlert(HealthLevel current)
        {
            lock (_alertLock)
            {
                // 节流：距上次推送太近就不推，避免刷屏
                if ((DateTime.Now - _lastAlertAt).TotalSeconds < _options.AlertThrottleSeconds)
                    return false;

                if (!_options.AlertOnlyOnWorsen)
                    return current != HealthLevel.Ok;

                // 变差要推
                if (current > _lastAlertedLevel)
                    return true;

                // 从异常恢复到正常，推一次让人知道问题过去了
                if (current == HealthLevel.Ok && _lastAlertedLevel != HealthLevel.Ok)
                    return true;

                // 一直维持同一等级：不重复推
                return false;
            }
        }

        private static string BuildAlertMessage(OperationsSnapshot snapshot)
        {
            var sb = new System.Text.StringBuilder();

            if (snapshot.Overall == HealthLevel.Ok)
            {
                sb.AppendLine("【FastData 恢复】所有检查项已恢复正常");
            }
            else
            {
                sb.AppendFormat("【FastData {0}】{1:yyyy-MM-dd HH:mm:ss}",
                    OperationsSnapshot.LevelText(snapshot.Overall), snapshot.Timestamp).AppendLine();
            }

            // 文本形式里已经按等级排好序，取前几项避免消息过长
            var problems = snapshot.Items
                .Where(i => i.Level != HealthLevel.Ok)
                .OrderByDescending(i => i.Level)
                .Take(8)
                .ToList();

            foreach (var item in problems)
            {
                sb.AppendFormat("[{0}] {1}", OperationsSnapshot.LevelText(item.Level), item.Name);
                if (!string.IsNullOrEmpty(item.Message))
                    sb.Append(" - ").Append(item.Message);
                sb.AppendLine();
            }

            var rest = snapshot.Items.Count(i => i.Level != HealthLevel.Ok) - problems.Count;
            if (rest > 0)
                sb.AppendFormat("...另有 {0} 项异常", rest).AppendLine();

            return sb.ToString().TrimEnd();
        }

        #region 各部分采集

        private List<HealthItem> CollectServer()
        {
            var items = new List<HealthItem>();

            try
            {
                var info = ServerMonitor.GetMonitorInfo();

                var cpuLevel = LevelFor(info.CpuUsage, _options.CpuWarnPercent, _options.CpuCriticalPercent);
                items.Add(new HealthItem
                {
                    Name = "CPU",
                    Level = cpuLevel,
                    Message = cpuLevel == HealthLevel.Ok ? null : "使用率偏高",
                    Data = { { "usage", string.Format("{0:F1}%", info.CpuUsage) } }
                });

                var memLevel = LevelFor(info.MemoryUsage, _options.MemoryWarnPercent, _options.MemoryCriticalPercent);
                items.Add(new HealthItem
                {
                    Name = "内存",
                    Level = memLevel,
                    Message = memLevel == HealthLevel.Ok ? null : "使用率偏高",
                    Data =
                    {
                        { "usage", string.Format("{0:F1}%", info.MemoryUsage) },
                        { "available", FormatMb(info.AvailableMemory) }
                    }
                });

                if (info.Disks != null)
                {
                    foreach (var disk in info.Disks)
                    {
                        // 容器/Android 环境下 /proc、/sys、/dev 下会挂出几十个伪文件系统，
                        // 它们的总大小为 0、使用率算出 NaN。不过滤的话面板会被这些噪音淹没，
                        // 真正出问题的那个分区反而看不见。
                        if (!IsRealisticDisk(disk))
                            continue;

                        var diskLevel = LevelFor(disk.UsagePercentage, _options.DiskWarnPercent, _options.DiskCriticalPercent);
                        items.Add(new HealthItem
                        {
                            Name = "磁盘 " + disk.Name,
                            Level = diskLevel,
                            Message = diskLevel == HealthLevel.Ok ? null : "剩余空间不足",
                            Data =
                            {
                                { "usage", string.Format("{0:F1}%", disk.UsagePercentage) },
                                { "free", FormatMb(disk.FreeSpace) }
                            }
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                items.Add(new HealthItem
                {
                    Name = "服务器指标",
                    Level = HealthLevel.Warning,
                    Message = "采集失败：" + ex.Message
                });
            }

            return items;
        }

        private List<HealthItem> CollectConnectionPools()
        {
            var items = new List<HealthItem>();

            try
            {
                var all = ConnectionPoolFactory.Instance.GetAllMetrics();

                if (all == null || all.Count == 0)
                {
                    items.Add(new HealthItem
                    {
                        Name = "连接池",
                        Level = HealthLevel.Ok,
                        Message = "尚未创建任何连接池"
                    });
                    return items;
                }

                foreach (var pair in all)
                {
                    var m = pair.Value;
                    var name = "连接池 " + pair.Key;
                    var level = HealthLevel.Ok;
                    var message = (string)null;

                    // 泄漏是代码 bug，不是配置问题 —— 优先级最高
                    if (m.LeakedConnections > 0)
                    {
                        level = HealthLevel.Critical;
                        message = string.Format("检测到 {0} 个连接泄漏：有地方漏了 using，调参数没用", m.LeakedConnections);
                    }
                    else if (m.ActiveConnections >= m.TotalConnections && m.IdleConnections == 0 && m.TotalConnections > 0)
                    {
                        level = HealthLevel.Warning;
                        message = "连接已用满且无空闲：并发高峰可能排队，考虑调大 MaxPoolSize";
                    }
                    // 注意：FailedRequests 是启动以来的累计值，永不清零。
                    // 用它做告警条件会导致「历史上失败过一次，之后永远报警」，没有意义。
                    // 这里只作为数据展示，是否异常由泄漏与当前占用情况判断。

                    items.Add(new HealthItem
                    {
                        Name = name,
                        Level = level,
                        Message = message,
                        Data =
                        {
                            { "total", m.TotalConnections.ToString() },
                            { "active", m.ActiveConnections.ToString() },
                            { "idle", m.IdleConnections.ToString() },
                            { "avgWaitMs", string.Format("{0:F1}", m.AverageWaitTimeMs) },
                            { "failedSinceStart", m.FailedRequests.ToString() }
                        }
                    });
                }
            }
            catch (Exception ex)
            {
                items.Add(new HealthItem
                {
                    Name = "连接池",
                    Level = HealthLevel.Warning,
                    Message = "采集失败：" + ex.Message
                });
            }

            return items;
        }

        private List<HealthItem> CollectRedis()
        {
            var items = new List<HealthItem>();

#if NETFRAMEWORK
            items.Add(new HealthItem
            {
                Name = "Redis",
                Level = HealthLevel.Ok,
                Message = "当前目标框架不包含 Redis 客户端，跳过"
            });
#else
            try
            {
                var available = FastRedis.RedisInfo.IsAvailable;

                items.Add(new HealthItem
                {
                    Name = "Redis",
                    Level = available ? HealthLevel.Ok : HealthLevel.Warning,
                    // 不可用时不等于业务出错：缓存会降级，请求都打到数据库。
                    // 但命中率归零是个必须知道的信号，所以给 Warning 而非 Ok。
                    Message = available ? null : "缓存已降级，请求会全部穿透到数据库：" + (FastRedis.RedisInfo.LastError ?? "原因未知"),
                    Data =
                    {
                        { "available", available.ToString() },
                        { "lastSuccess", FastRedis.RedisInfo.LastSuccessAt.HasValue
                            ? FastRedis.RedisInfo.LastSuccessAt.Value.ToString("yyyy-MM-dd HH:mm:ss") : "从未成功" }
                    }
                });
            }
            catch (Exception ex)
            {
                items.Add(new HealthItem
                {
                    Name = "Redis",
                    Level = HealthLevel.Ok,
                    Message = "未启用：" + ex.Message
                });
            }
#endif

            return items;
        }

        private List<HealthItem> CollectPerformance()
        {
            var items = new List<HealthItem>();

            try
            {
                var monitor = PerformanceHelper.GetMonitor();
                if (monitor == null)
                    return items;

                var summary = monitor.GetSummary();
                if (summary == null || summary.TotalOperations == 0)
                    return items;

                var failRate = summary.TotalOperations == 0
                    ? 0
                    : (double)summary.TotalFailedOperations / summary.TotalOperations;

                var level = failRate >= 0.05 ? HealthLevel.Critical
                          : failRate >= 0.01 ? HealthLevel.Warning
                          : HealthLevel.Ok;

                items.Add(new HealthItem
                {
                    Name = "操作成功率",
                    Level = level,
                    Message = level == HealthLevel.Ok ? null
                        : string.Format("失败率 {0:P1}，共 {1} 次操作", failRate, summary.TotalOperations),
                    Data =
                    {
                        { "total", summary.TotalOperations.ToString() },
                        { "failed", summary.TotalFailedOperations.ToString() },
                        { "avgMs", string.Format("{0:F1}", summary.AverageDuration) },
                        { "maxMs", string.Format("{0:F1}", summary.MaxDuration) }
                    }
                });

                var slow = monitor.GetSlowQueries(_options.SlowQueryThresholdMs);
                if (slow != null && slow.Count > 0)
                {
                    items.Add(new HealthItem
                    {
                        Name = "慢查询",
                        Level = HealthLevel.Warning,
                        Message = string.Format("{0} 条超过 {1} ms", slow.Count, _options.SlowQueryThresholdMs),
                        Data =
                        {
                            { "slowest", slow.Max(s => s.Duration).TotalMilliseconds.ToString("F0") + " ms" }
                        }
                    });
                }
            }
            catch (Exception ex)
            {
                items.Add(new HealthItem
                {
                    Name = "性能指标",
                    Level = HealthLevel.Ok,
                    Message = "未启用：" + ex.Message
                });
            }

            return items;
        }

        private async Task<List<HealthItem>> ProbeEndpointsAsync(CancellationToken cancellationToken)
        {
            var items = new List<HealthItem>();

            using (var client = new System.Net.Http.HttpClient())
            {
                client.Timeout = TimeSpan.FromSeconds(_options.ProbeTimeoutSeconds);

                foreach (var url in _options.ProbeUrls)
                {
                    if (string.IsNullOrWhiteSpace(url))
                        continue;

                    var stopwatch = Stopwatch.StartNew();
                    try
                    {
                        using (var response = await client.GetAsync(url, cancellationToken).ConfigureAwait(false))
                        {
                            stopwatch.Stop();
                            var code = (int)response.StatusCode;
                            var ok = response.IsSuccessStatusCode;

                            items.Add(new HealthItem
                            {
                                Name = "接口 " + url,
                                Level = ok ? HealthLevel.Ok : HealthLevel.Critical,
                                Message = ok ? null : "返回 " + code,
                                Data =
                                {
                                    { "status", code.ToString() },
                                    { "elapsedMs", stopwatch.ElapsedMilliseconds.ToString() }
                                }
                            });
                        }
                    }
                    catch (Exception ex)
                    {
                        stopwatch.Stop();
                        items.Add(new HealthItem
                        {
                            Name = "接口 " + url,
                            Level = HealthLevel.Critical,
                            // 接口不可达是明确的故障，不是"可能有问题"
                            Message = "不可达：" + ex.Message,
                            Data = { { "elapsedMs", stopwatch.ElapsedMilliseconds.ToString() } }
                        });
                    }
                }
            }

            return items;
        }

        #endregion

        #region 辅助

        /// <summary>
        /// 判断这个分区值不值得展示 —— 只报真正会被写满、写满会出问题的文件系统。
        /// </summary>
        private static bool IsRealisticDisk(DiskInfo disk)
        {
            if (disk == null) return false;

            // 总大小为 0 的是伪文件系统（proc/sysfs/cgroup 等），没有「空间」概念
            if (disk.TotalSize <= 0) return false;

            // 使用率算出 NaN 说明除零，同样不是真实分区
            if (double.IsNaN(disk.UsagePercentage)) return false;

            var name = disk.Name ?? string.Empty;

            // 按挂载点兜底：这些前缀下面即使有大小也不该拿来判断磁盘水位
            if (name.StartsWith("/proc", StringComparison.Ordinal) ||
                name.StartsWith("/sys", StringComparison.Ordinal) ||
                name.StartsWith("/dev", StringComparison.Ordinal))
                return false;

            return true;
        }

        private static HealthLevel LevelFor(double value, double warn, double critical)
        {
            if (value >= critical) return HealthLevel.Critical;
            if (value >= warn) return HealthLevel.Warning;
            return HealthLevel.Ok;
        }

        private static string FormatMb(long bytes)
        {
            if (bytes <= 0) return "0 MB";
            return string.Format("{0:F0} MB", bytes / 1024.0 / 1024.0);
        }

        #endregion
    }
}