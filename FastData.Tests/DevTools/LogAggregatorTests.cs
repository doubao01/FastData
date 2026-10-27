using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using FastData.DevTools;
using Xunit;

namespace FastData.Tests.DevTools
{
    /// <summary>
    /// LogAggregator 的停止语义：必须能有界返回，不能永久挂起。
    /// </summary>
    public class LogAggregatorTests
    {
        private sealed class RecordingProvider : ILogProvider
        {
            public readonly List<LogEntry> Entries = new List<LogEntry>();

            public Task WriteLogAsync(LogEntry entry)
            {
                lock (Entries)
                {
                    Entries.Add(entry);
                }

                return Task.CompletedTask;
            }
        }

        /// <summary>
        /// 写入会一直阻塞，直到测试放行；用来制造「队列排不空」的场景。
        /// </summary>
        private sealed class BlockingProvider : ILogProvider
        {
            private readonly SemaphoreSlim _gate;

            public BlockingProvider(SemaphoreSlim gate)
            {
                _gate = gate;
            }

            public Task WriteLogAsync(LogEntry entry)
            {
                return _gate.WaitAsync();
            }
        }

        [Fact]
        public async Task StopAsync_Returns_Immediately_When_Queue_Empty()
        {
            var stopwatch = Stopwatch.StartNew();
            var remaining = await LogAggregator.StopAsync(TimeSpan.FromSeconds(5));
            stopwatch.Stop();

            Assert.Equal(0, remaining);
            // 宽松上限：CI 上整套测试并行执行，续体调度可能被拖延十几秒。
            // 这里要区分的是「返回」与「挂住」，不是秒级性能。
            Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(30),
                "空队列时 StopAsync 应立即返回，实际耗时 " + stopwatch.Elapsed);
        }

        [Fact]
        public async Task StopAsync_Drains_Queued_Logs()
        {
            var provider = new RecordingProvider();
            LogAggregator.AddProvider(provider);

            try
            {
                for (var i = 0; i < 20; i++)
                {
                    LogAggregator.Info("test message " + i, "LogAggregatorTests");
                }

                var remaining = await LogAggregator.StopAsync(TimeSpan.FromSeconds(10));

                Assert.Equal(0, remaining);
                lock (provider.Entries)
                {
                    Assert.True(provider.Entries.Count >= 20,
                        "应至少写入 20 条，实际 " + provider.Entries.Count);
                }
            }
            finally
            {
                LogAggregator.RemoveProvider(provider);
            }
        }

        /// <summary>
        /// 关键回归：消费者卡住时，StopAsync 必须按超时返回，而不是永久挂起。
        /// </summary>
        [Fact]
        public async Task StopAsync_Times_Out_Instead_Of_Hanging()
        {
            using var gate = new SemaphoreSlim(0);
            var provider = new BlockingProvider(gate);
            LogAggregator.AddProvider(provider);

            try
            {
                for (var i = 0; i < 5; i++)
                {
                    LogAggregator.Info("blocked " + i, "LogAggregatorTests");
                }

                var stopTask = LogAggregator.StopAsync(TimeSpan.FromMilliseconds(150));

                // 30 秒兜底：旧实现会无限等下去，这个断言就是用来拦住「永久挂起」的。
                // 不卡紧凑墙钟：CI 并行跑整套测试时续体调度会被拖延十几秒，
                // 那是环境噪声，不是实现问题。
                var finished = await Task.WhenAny(stopTask, Task.Delay(TimeSpan.FromSeconds(30)));

                Assert.Same(stopTask, finished);

                var remaining = await stopTask;
                Assert.True(remaining > 0, "消费被阻塞时应剩有未处理日志");
            }
            finally
            {
                gate.Release(100); // 放行，让后台任务收尾
                LogAggregator.RemoveProvider(provider);
            }
        }

        /// <summary>
        /// 取消令牌必须能让 StopAsync 立刻返回
        /// </summary>
        [Fact]
        public async Task StopAsync_Honors_Cancellation()
        {
            using var cts = new CancellationTokenSource();
            using var gate = new SemaphoreSlim(0);
            var provider = new BlockingProvider(gate);
            LogAggregator.AddProvider(provider);

            try
            {
                LogAggregator.Info("cancel test", "LogAggregatorTests");

                var stopTask = LogAggregator.StopAsync(Timeout.InfiniteTimeSpan, cts.Token);
                cts.CancelAfter(100);

                var finished = await Task.WhenAny(stopTask, Task.Delay(TimeSpan.FromSeconds(30)));
                Assert.Same(stopTask, finished);
            }
            finally
            {
                gate.Release(100);
                LogAggregator.RemoveProvider(provider);
            }
        }
    }
}
