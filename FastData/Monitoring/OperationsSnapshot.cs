using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace FastData.Monitoring
{
    /// <summary>
    /// 健康等级。
    /// </summary>
    public enum HealthLevel
    {
        /// <summary>正常</summary>
        Ok = 0,
        /// <summary>需要关注，暂不影响功能</summary>
        Warning = 1,
        /// <summary>已经影响功能，需要立即处理</summary>
        Critical = 2
    }

    /// <summary>
    /// 单项健康状态。
    /// </summary>
    public class HealthItem
    {
        /// <summary>检查项名称，如「连接池 SqlServer」「Redis」「接口 /api/health」</summary>
        public string Name { get; set; }

        /// <summary>等级</summary>
        public HealthLevel Level { get; set; }

        /// <summary>人类可读的说明，出问题时写清原因与下一步动作</summary>
        public string Message { get; set; }

        /// <summary>附加数据（键值对，用于面板展示或排查）</summary>
        public Dictionary<string, string> Data { get; set; } = new Dictionary<string, string>();
    }

    /// <summary>
    /// 运维快照：某一时刻的系统全景。
    /// </summary>
    public class OperationsSnapshot
    {
        /// <summary>采集时间</summary>
        public DateTime Timestamp { get; set; }

        /// <summary>总体等级（取所有检查项里最差的）</summary>
        public HealthLevel Overall { get; set; }

        /// <summary>各检查项</summary>
        public List<HealthItem> Items { get; set; } = new List<HealthItem>();

        /// <summary>采集耗时</summary>
        public TimeSpan Duration { get; set; }

        /// <summary>
        /// 渲染为文本，适合终端输出、写日志、贴到聊天窗口。
        /// </summary>
        public string Render()
        {
            var sb = new StringBuilder();
            sb.AppendLine("===== FastData 运维面板 =====");
            sb.AppendLine(string.Format("时间: {0:yyyy-MM-dd HH:mm:ss}    总体: {1}    耗时: {2} ms",
                Timestamp, LevelText(Overall), (int)Duration.TotalMilliseconds));
            sb.AppendLine();

            foreach (var item in Items.OrderByDescending(i => i.Level))
            {
                sb.AppendFormat("[{0,-8}] {1}", LevelText(item.Level), item.Name);
                if (!string.IsNullOrEmpty(item.Message))
                    sb.Append(" - ").Append(item.Message);
                sb.AppendLine();

                if (item.Data != null && item.Data.Count > 0)
                {
                    foreach (var kv in item.Data)
                        sb.AppendFormat("             {0}: {1}", kv.Key, kv.Value).AppendLine();
                }
            }

            return sb.ToString();
        }

        /// <summary>
        /// 渲染为 JSON，可直接作为宿主的 /health 端点响应体。
        /// </summary>
        /// <remarks>
        /// 手写序列化而不是用 System.Text.Json：本库要兼容 net452，那里没有这个包，
        /// 而为了一个固定结构的小对象引入 Newtonsoft 不值得。
        /// </remarks>
        public string ToJson()
        {
            var sb = new StringBuilder();
            sb.Append("{\"timestamp\":\"").Append(Timestamp.ToString("o")).Append('"');
            sb.Append(",\"overall\":\"").Append(LevelText(Overall)).Append('"');
            sb.Append(",\"durationMs\":").Append((int)Duration.TotalMilliseconds);
            sb.Append(",\"items\":[");

            for (var i = 0; i < Items.Count; i++)
            {
                var item = Items[i];
                if (i > 0) sb.Append(',');

                sb.Append("{\"name\":").Append(Quote(item.Name));
                sb.Append(",\"level\":\"").Append(LevelText(item.Level)).Append('"');
                sb.Append(",\"message\":").Append(Quote(item.Message));
                sb.Append(",\"data\":{");

                if (item.Data != null)
                {
                    var first = true;
                    foreach (var kv in item.Data)
                    {
                        if (!first) sb.Append(',');
                        first = false;
                        sb.Append(Quote(kv.Key)).Append(':').Append(Quote(kv.Value));
                    }
                }

                sb.Append("}}");
            }

            sb.Append("]}");
            return sb.ToString();
        }

        /// <summary>是否一切正常</summary>
        public bool IsHealthy
        {
            get { return Overall == HealthLevel.Ok; }
        }

        /// <summary>取等级的中文名</summary>
        public static string LevelText(HealthLevel level)
        {
            switch (level)
            {
                case HealthLevel.Critical: return "严重";
                case HealthLevel.Warning: return "警告";
                default: return "正常";
            }
        }

        private static string Quote(string value)
        {
            if (value == null)
                return "null";

            var sb = new StringBuilder(value.Length + 2);
            sb.Append('"');
            foreach (var c in value)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < ' ')
                            sb.AppendFormat("\\u{0:x4}", (int)c);
                        else
                            sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
            return sb.ToString();
        }
    }

    /// <summary>
    /// 运维面板配置。
    /// </summary>
    public class OperationsPanelOptions
    {
        /// <summary>CPU 使用率告警阈值（百分比）</summary>
        public double CpuWarnPercent { get; set; } = 80;
        /// <summary>CPU 使用率严重阈值（百分比）</summary>
        public double CpuCriticalPercent { get; set; } = 95;

        /// <summary>内存使用率告警阈值（百分比）</summary>
        public double MemoryWarnPercent { get; set; } = 85;
        /// <summary>内存使用率严重阈值（百分比）</summary>
        public double MemoryCriticalPercent { get; set; } = 95;

        /// <summary>磁盘使用率告警阈值（百分比）</summary>
        public double DiskWarnPercent { get; set; } = 85;
        /// <summary>磁盘使用率严重阈值（百分比）</summary>
        public double DiskCriticalPercent { get; set; } = 95;

        /// <summary>
        /// 要探测的服务接口地址列表。为空则不探测。
        /// </summary>
        public List<string> ProbeUrls { get; set; } = new List<string>();

        /// <summary>单个接口探测的超时秒数</summary>
        public int ProbeTimeoutSeconds { get; set; } = 5;

        /// <summary>是否采集服务器指标（CPU/内存/磁盘）</summary>
        public bool CollectServer { get; set; } = true;

        /// <summary>是否采集连接池指标</summary>
        public bool CollectConnectionPools { get; set; } = true;

        /// <summary>是否采集 Redis 状态</summary>
        public bool CollectRedis { get; set; } = true;

        /// <summary>是否采集性能指标与慢查询</summary>
        public bool CollectPerformance { get; set; } = true;

        /// <summary>慢查询耗时阈值（毫秒）</summary>
        public double SlowQueryThresholdMs { get; set; } = 1000;

        /// <summary>
        /// 两次推送之间的最小间隔秒数。
        /// 无论状态如何变化都至少间隔这么久，避免刷屏。
        /// </summary>
        public int AlertThrottleSeconds { get; set; } = 300;

        /// <summary>
        /// 只在等级变差时推送（默认 true）。
        /// <para>
        /// 状态一直是「警告」时不会重复推送；从「正常」变「警告」、或「警告」变「严重」才推。
        /// 恢复为正常时也会推一次，让人知道问题过去了。
        /// </para>
        /// </summary>
        public bool AlertOnlyOnWorsen { get; set; } = true;
    }
}