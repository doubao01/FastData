using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FastData.DevTools;
using Xunit;

// FastData.DevTools.TaskScheduler / TaskStatus 与 BCL 的
// System.Threading.Tasks.TaskScheduler / TaskStatus 同名。
// 用户在自己的代码里引用本库时也会撞上，只能用别名或全名 —— 这是库的命名问题，
// 但改名属于破坏性变更，先在这里用别名绕开，问题已记录。
using SchedScheduler = FastData.DevTools.TaskScheduler;
using SchedStatus = FastData.DevTools.TaskStatus;

namespace FastData.Tests.DevTools
{
    /// <summary>
    /// TaskScheduler 的调度行为契约。
    /// <para>
    /// 背景：这个类 721 行，此前**零测试**。属于「看起来实现完整、从未被验证」
    /// 的那一类风险（QueryOptimizer 与 CodeGenerator 都是这么出问题的）。
    /// </para>
    /// <para>
    /// 注意：TaskScheduler 是静态类，状态跨用例共享。所有测试都放在同一个类里
    /// （xUnit 同类的用例串行执行），并在每个用例结束时 CancelAll 清理。
    /// </para>
    /// <para>
    /// 等待策略用轮询 + 超时，不用固定 Sleep：固定时长在 CI 上不稳，
    /// 机器慢就误报、机器快就白等。
    /// </para>
    /// </summary>
    public class TaskSchedulerTests : IDisposable
    {
        public TaskSchedulerTests()
        {
            // 每个用例开始前清场，避免上一个用例的残留任务影响判断
            SchedScheduler.CancelAll();
        }

        public void Dispose()
        {
            SchedScheduler.CancelAll();
        }

        /// <summary>
        /// 轮询等待条件成立，超时返回 false。
        /// </summary>
        private static bool WaitUntil(Func<bool> condition, int timeoutMs = 5000, int intervalMs = 25)
        {
            var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
            while (DateTime.UtcNow < deadline)
            {
                if (condition())
                    return true;
                System.Threading.Thread.Sleep(intervalMs);
            }
            return condition();
        }

        /// <summary>
        /// 调度应返回非空任务 ID，且能按 ID 查回。
        /// </summary>
        [Fact]
        public void Schedule_ShouldReturnIdAndBeRetrievable()
        {
            var id = SchedScheduler.Schedule("t1", () => { }, TimeSpan.FromMinutes(10));

            Assert.False(string.IsNullOrEmpty(id));

            var task = SchedScheduler.GetTask(id);
            Assert.NotNull(task);
            Assert.Equal("t1", task.Name);
        }

        /// <summary>
        /// 刚调度、尚未到点时状态应为 Scheduled。
        /// </summary>
        [Fact]
        public void Schedule_BeforeDue_ShouldBeScheduled()
        {
            var id = SchedScheduler.Schedule("t2", () => { }, TimeSpan.FromMinutes(10));

            var task = SchedScheduler.GetTask(id);

            Assert.Equal(SchedStatus.Scheduled, task.Status);
        }

        /// <summary>
        /// 到点后动作应真的被执行。
        /// 这是「定时功能是否真的能用」最核心的一条。
        /// </summary>
        [Fact]
        public void Schedule_ShouldActuallyExecuteAction()
        {
            var executed = false;

            SchedScheduler.Schedule("t3", () => { executed = true; }, TimeSpan.FromMilliseconds(50));

            Assert.True(WaitUntil(() => executed), "动作没有被执行 —— 定时器形同虚设");
        }

        /// <summary>
        /// 执行完成后状态应变为 Completed。
        /// </summary>
        [Fact]
        public void Schedule_AfterExecution_ShouldBeCompleted()
        {
            var done = false;

            var id = SchedScheduler.Schedule("t4", () => { done = true; }, TimeSpan.FromMilliseconds(50));

            Assert.True(WaitUntil(() => done));
            Assert.True(WaitUntil(() => SchedScheduler.GetTask(id).Status == SchedStatus.Completed),
                "执行后状态应为 Completed，实际为 " + SchedScheduler.GetTask(id).Status);
        }

        /// <summary>
        /// 取消未到点的任务：Cancel 返回 true，状态变 Cancelled，且动作不得执行。
        /// </summary>
        [Fact]
        public void Cancel_BeforeDue_ShouldPreventExecution()
        {
            var executed = false;

            var id = SchedScheduler.Schedule("t5", () => { executed = true; }, TimeSpan.FromMilliseconds(200));

            Assert.True(SchedScheduler.Cancel(id));
            Assert.Equal(SchedStatus.Cancelled, SchedScheduler.GetTask(id).Status);

            // 等到原定执行时间之后，确认动作没跑
            System.Threading.Thread.Sleep(400);
            Assert.False(executed, "任务已取消却仍然执行了");
        }

        /// <summary>
        /// 取消不存在的任务应返回 false，而不是抛异常。
        /// </summary>
        [Fact]
        public void Cancel_UnknownId_ShouldReturnFalse()
        {
            Assert.False(SchedScheduler.Cancel("no-such-task-" + Guid.NewGuid().ToString("N")));
        }

        /// <summary>
        /// 重复任务应执行多次。
        /// </summary>
        [Fact]
        public void Schedule_Repeat_ShouldExecuteMultipleTimes()
        {
            var count = 0;

            SchedScheduler.Schedule("t6", () => { System.Threading.Interlocked.Increment(ref count); },
                TimeSpan.FromMilliseconds(40), repeat: true);

            Assert.True(WaitUntil(() => count >= 3, timeoutMs: 5000),
                "重复任务应至少执行 3 次，实际 " + count);
        }

        /// <summary>
        /// 动作抛异常时状态应为 Failed，且不能把调度器本身搞崩。
        /// </summary>
        [Fact]
        public void Schedule_ActionThrows_ShouldMarkFailed()
        {
            var id = SchedScheduler.Schedule("t7",
                () => { throw new InvalidOperationException("boom"); },
                TimeSpan.FromMilliseconds(40));

            Assert.True(WaitUntil(() => SchedScheduler.GetTask(id).Status == SchedStatus.Failed),
                "抛异常后状态应为 Failed，实际为 " + SchedScheduler.GetTask(id).Status);

            // 调度器仍可用
            var ok = false;
            SchedScheduler.Schedule("t7b", () => { ok = true; }, TimeSpan.FromMilliseconds(40));
            Assert.True(WaitUntil(() => ok), "一个任务失败后调度器应仍能工作");
        }

        /// <summary>
        /// 异步动作应被 await（而不是启动后不管）。
        /// </summary>
        [Fact]
        public async Task Schedule_Async_ShouldAwaitAction()
        {
            var completed = false;

            var id = SchedScheduler.Schedule("t8", async () =>
            {
                await Task.Delay(50);
                completed = true;
            }, TimeSpan.FromMilliseconds(40));

            Assert.True(WaitUntil(() => completed, timeoutMs: 5000));
            Assert.True(WaitUntil(() => SchedScheduler.GetTask(id).Status == SchedStatus.Completed));

            await Task.CompletedTask;
        }

        /// <summary>
        /// CancelAll 后不应再有活动任务。
        /// </summary>
        [Fact]
        public void CancelAll_ShouldClearActiveTasks()
        {
            for (var i = 0; i < 5; i++)
                SchedScheduler.Schedule("bulk" + i, () => { }, TimeSpan.FromMinutes(10));

            Assert.True(SchedScheduler.GetActiveTasks().Count >= 5);

            SchedScheduler.CancelAll();

            Assert.True(WaitUntil(() => SchedScheduler.GetActiveTasks().Count == 0),
                "CancelAll 后仍有活动任务：" + SchedScheduler.GetActiveTasks().Count);
        }

        /// <summary>
        /// GetAllTasks 应包含已调度但未执行的任务。
        /// </summary>
        [Fact]
        public void GetAllTasks_ShouldIncludeScheduled()
        {
            var id = SchedScheduler.Schedule("t9", () => { }, TimeSpan.FromMinutes(10));

            var all = SchedScheduler.GetAllTasks();

            Assert.Contains(all, t => t.TaskId == id);
        }

        /// <summary>
        /// Cron 只支持「秒 分 时」三段式，不是标准五段 cron。
        /// <para>
        /// 这个用例锁定当前支持的格式，避免有人误以为支持完整 Cron。
        /// </para>
        /// </summary>
        [Fact]
        public void ScheduleCron_SupportsThreePartFormat()
        {
            var id = SchedScheduler.ScheduleCron("c1", () => { }, "30 0 0"); // 30 秒

            Assert.False(string.IsNullOrEmpty(id));
            var task = SchedScheduler.GetTask(id);
            Assert.NotNull(task);
            Assert.Equal(TimeSpan.FromSeconds(30), task.Delay);

            SchedScheduler.Cancel(id);
        }

        /// <summary>
        /// 关键回归：无法解析的 Cron 表达式必须抛异常，不能静默降级成「每分钟一次」。
        /// <para>
        /// 原实现解析失败时 <c>return TimeSpan.FromMinutes(1)</c>：
        /// 用户写标准五段 cron（如 <c>0 9 * * 1</c>，期望每周一 9 点），
        /// 会被静默改成每分钟执行 —— 生产环境每小时跑 60 次。
        /// 这比直接报错危险得多。
        /// </para>
        /// </summary>
        [Theory]
        [InlineData("0 9 * * 1")]      // 标准五段
        [InlineData("*/5 * * * *")]    // 标准五段带步长
        [InlineData("every minute")]   // 完全不是 cron
        [InlineData("")]               // 空串
        public void ScheduleCron_InvalidExpression_ShouldThrow(string expression)
        {
            Assert.Throws<ArgumentException>(
                () => SchedScheduler.ScheduleCron("bad", () => { }, expression));
        }
    }
}