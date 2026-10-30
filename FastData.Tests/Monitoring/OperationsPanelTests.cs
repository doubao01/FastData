using System;
using System.Collections.Generic;
using System.Linq;
using FastData.Monitoring;
using Xunit;

namespace FastData.Tests.Monitoring
{
    /// <summary>
    /// 运维面板的输出契约。
    /// <para>
    /// 这个面板的价值在于「一眼看出有没有问题」，所以测试重点是：
    /// 输出格式能不能被机器解析（JSON）、能不能被人读懂（文本）、
    /// 等级聚合是不是取最差的、告警会不会刷屏。
    /// </para>
    /// </summary>
    public class OperationsPanelTests
    {
        private static OperationsSnapshot MakeSnapshot(params HealthItem[] items)
        {
            var snapshot = new OperationsSnapshot
            {
                Timestamp = new DateTime(2026, 10, 30, 12, 0, 0),
                Duration = TimeSpan.FromMilliseconds(42)
            };
            snapshot.Items.AddRange(items);
            snapshot.Overall = items.Length == 0
                ? HealthLevel.Ok
                : items.Max(i => i.Level);
            return snapshot;
        }

        /// <summary>
        /// 总体等级必须取所有检查项里最差的那个。
        /// 否则「CPU 严重 + 磁盘正常」会被判成正常，面板就失去意义了。
        /// </summary>
        [Fact]
        public void Overall_ShouldBeWorstLevel()
        {
            var snapshot = MakeSnapshot(
                new HealthItem { Name = "CPU", Level = HealthLevel.Ok },
                new HealthItem { Name = "磁盘", Level = HealthLevel.Critical },
                new HealthItem { Name = "内存", Level = HealthLevel.Warning });

            Assert.Equal(HealthLevel.Critical, snapshot.Overall);
            Assert.False(snapshot.IsHealthy);
        }

        /// <summary>
        /// 全部正常时 IsHealthy 为 true。
        /// </summary>
        [Fact]
        public void AllOk_ShouldBeHealthy()
        {
            var snapshot = MakeSnapshot(
                new HealthItem { Name = "CPU", Level = HealthLevel.Ok },
                new HealthItem { Name = "内存", Level = HealthLevel.Ok });

            Assert.Equal(HealthLevel.Ok, snapshot.Overall);
            Assert.True(snapshot.IsHealthy);
        }

        /// <summary>
        /// 空快照应视为正常，而不是崩掉或报异常。
        /// </summary>
        [Fact]
        public void EmptySnapshot_ShouldBeOk()
        {
            var snapshot = MakeSnapshot();

            Assert.Equal(HealthLevel.Ok, snapshot.Overall);
            Assert.True(snapshot.IsHealthy);
        }

        /// <summary>
        /// 文本输出应包含时间、总体状态、每个检查项。
        /// </summary>
        [Fact]
        public void Render_ShouldContainKeyInfo()
        {
            var snapshot = MakeSnapshot(
                new HealthItem
                {
                    Name = "连接池 SqlServer",
                    Level = HealthLevel.Critical,
                    Message = "检测到 2 个连接泄漏"
                });

            var text = snapshot.Render();

            Assert.Contains("2026-10-30 12:00:00", text);
            Assert.Contains("严重", text);
            Assert.Contains("连接池 SqlServer", text);
            Assert.Contains("检测到 2 个连接泄漏", text);
        }

        /// <summary>
        /// 文本输出中问题项排在前面，人一眼就能看到要处理什么。
        /// </summary>
        [Fact]
        public void Render_ShouldPutProblemsFirst()
        {
            var snapshot = MakeSnapshot(
                new HealthItem { Name = "正常项A", Level = HealthLevel.Ok },
                new HealthItem { Name = "严重项", Level = HealthLevel.Critical },
                new HealthItem { Name = "正常项B", Level = HealthLevel.Ok });

            var text = snapshot.Render();

            var criticalIndex = text.IndexOf("严重项", StringComparison.Ordinal);
            var okIndex = text.IndexOf("正常项A", StringComparison.Ordinal);

            Assert.True(criticalIndex >= 0 && okIndex >= 0);
            Assert.True(criticalIndex < okIndex, "严重项应排在正常项之前");
        }

        /// <summary>
        /// JSON 输出必须是合法结构：括号配对、引号配对。
        /// </summary>
        [Fact]
        public void ToJson_ShouldBeWellFormed()
        {
            var snapshot = MakeSnapshot(
                new HealthItem
                {
                    Name = "Redis",
                    Level = HealthLevel.Warning,
                    Message = "缓存已降级",
                    Data = { { "available", "False" } }
                });

            var json = snapshot.ToJson();

            Assert.StartsWith("{", json);
            Assert.EndsWith("}", json);
            Assert.Equal(json.Count(c => c == '{'), json.Count(c => c == '}'));
            Assert.Contains("\"overall\":\"警告\"", json);
            Assert.Contains("\"name\":\"Redis\"", json);
            Assert.Contains("\"available\":\"False\"", json);
        }

        /// <summary>
        /// 关键：JSON 必须正确转义。
        /// 消息里出现引号、反斜杠、换行时若不转义，宿主拿到的 /health 响应就是坏 JSON，
        /// 监控系统解析失败 —— 而这个失败的场景恰恰是「系统本来就出问题了」的时候。
        /// </summary>
        [Fact]
        public void ToJson_ShouldEscapeSpecialCharacters()
        {
            var snapshot = MakeSnapshot(
                new HealthItem
                {
                    Name = "异常",
                    Level = HealthLevel.Critical,
                    Message = "路径 C:\\temp\\x 里有 \"引号\" 和\n换行\t制表"
                });

            var json = snapshot.ToJson();

            Assert.Contains("\\\\temp", json);   // 反斜杠转义
            Assert.Contains("\\\"引号\\\"", json);
            Assert.Contains("\\n", json);
            Assert.Contains("\\t", json);
        }

        /// <summary>
        /// 空消息应输出 null 而不是空字符串加引号，便于消费方区分。
        /// </summary>
        [Fact]
        public void ToJson_NullMessage_ShouldBeNull()
        {
            var snapshot = MakeSnapshot(
                new HealthItem { Name = "CPU", Level = HealthLevel.Ok, Message = null });

            var json = snapshot.ToJson();

            Assert.Contains("\"message\":null", json);
        }

        /// <summary>
        /// 等级的中文名映射。
        /// </summary>
        [Theory]
        [InlineData(HealthLevel.Ok, "正常")]
        [InlineData(HealthLevel.Warning, "警告")]
        [InlineData(HealthLevel.Critical, "严重")]
        public void LevelText_ShouldMapCorrectly(HealthLevel level, string expected)
        {
            Assert.Equal(expected, OperationsSnapshot.LevelText(level));
        }

        /// <summary>
        /// 默认配置应开箱可用：全采集、阈值合理、不探测接口（没有地址）。
        /// </summary>
        [Fact]
        public void DefaultOptions_ShouldBeSensible()
        {
            var options = new OperationsPanelOptions();

            Assert.True(options.CollectServer);
            Assert.True(options.CollectConnectionPools);
            Assert.True(options.CollectRedis);
            Assert.True(options.CollectPerformance);
            Assert.Empty(options.ProbeUrls);
            Assert.True(options.AlertOnlyOnWorsen);
            Assert.True(options.CpuWarnPercent < options.CpuCriticalPercent);
            Assert.True(options.MemoryWarnPercent < options.MemoryCriticalPercent);
        }

        /// <summary>
        /// 实际采集一次，不能抛异常，且至少产出服务器指标。
        /// 这是端到端的最低保证：面板能跑起来。
        /// </summary>
        [Fact]
        public async System.Threading.Tasks.Task CollectAsync_ShouldNotThrowAndProduceItems()
        {
            var panel = new OperationsPanel(new OperationsPanelOptions
            {
                ProbeUrls = new List<string>()   // 不探测外部接口，避免测试依赖网络
            });

            var snapshot = await panel.CollectAsync();

            Assert.NotNull(snapshot);
            Assert.NotEmpty(snapshot.Items);
            Assert.True(snapshot.Duration >= TimeSpan.Zero);
            // CPU / 内存 / 磁盘至少会有一项
            Assert.Contains(snapshot.Items, i => i.Name.Contains("CPU") || i.Name.Contains("内存") || i.Name.Contains("磁盘"));
        }

        /// <summary>
        /// 没有发送器时 RunAsync 只采集不推送，不应抛异常。
        /// </summary>
        [Fact]
        public async System.Threading.Tasks.Task RunAsync_NoSender_ShouldJustCollect()
        {
            var panel = new OperationsPanel(new OperationsPanelOptions());

            var snapshot = await panel.RunAsync();

            Assert.NotNull(snapshot);
        }
    }
}