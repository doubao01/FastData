using System;
using System.Diagnostics;
using System.Reflection;
using System.Text;
using FastData.Config;
using Xunit;

namespace FastData.Tests
{
    /// <summary>
    /// 把 Trace 输出收集到内存，用于在测试里观察库内部吞掉的异常。
    /// </summary>
    internal sealed class CapturingTraceListener : TraceListener
    {
        private readonly StringBuilder _buffer = new StringBuilder();

        public override void Write(string message)
        {
            _buffer.Append(message);
        }

        public override void WriteLine(string message)
        {
            _buffer.AppendLine(message);
        }

        public override string ToString()
        {
            return _buffer.ToString();
        }
    }

    /// <summary>
    /// net452 下 DataConfig 加载行为的诊断（issue #18）。
    /// <para>
    /// 第一轮诊断的结论出乎意料：`TryLoadConfig` / `ParseXmlConfig` 的 catch
    /// **一次都没触发**（Trace 无输出）。也就是说没有异常，
    /// 配置解析是「成功」返回的，只是连接没填进去。
    /// </para>
    /// <para>
    /// 因此本类用反射逐层调用 private 方法，定位到底哪一环断了：
    /// TryLoadConfig(db.config) → Active 值 → TryLoadConfig(db.dev.config) → Connections 数量。
    /// </para>
    /// <para>
    /// 待 #18 修复后，本类应改为断言 DataConfig 能正确读到连接。
    /// </para>
    /// </summary>
    public class DataConfigLoadDiagnostics
    {
        private static readonly BindingFlags PrivateStatic =
            BindingFlags.NonPublic | BindingFlags.Static;

        private static T InvokePrivate<T>(string method, params object[] args)
        {
            var m = typeof(DataConfig).GetMethod(method, PrivateStatic);
            if (m == null)
                throw new InvalidOperationException("找不到方法：" + method);
            return (T)m.Invoke(null, args);
        }

        private static string Describe(DataConfig cfg)
        {
            if (cfg == null)
                return "<null>";

            return string.Format("Active=\"{0}\" Default=\"{1}\" Connections={2}",
                cfg.Active ?? "<空>",
                cfg.Default ?? "<空>",
                cfg.Connections == null ? -1 : cfg.Connections.Count);
        }

        [Fact]
        public void Report_HowDataConfigLoadsOnThisTargetFramework()
        {
            var listener = new CapturingTraceListener();
            Trace.Listeners.Add(listener);

            var log = new StringBuilder();
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            DataConfig baseConfig = null;
            DataConfig envConfig = null;
            string result;

            try
            {
                log.AppendLine("BaseDirectory = " + baseDir);
                log.AppendLine("db.config 存在 = " + System.IO.File.Exists(
                    System.IO.Path.Combine(baseDir, "db.config")));
                log.AppendLine("db.dev.config 存在 = " + System.IO.File.Exists(
                    System.IO.Path.Combine(baseDir, "db.dev.config")));

                // 逐层探测：先看 db.config 本身解析成什么
                baseConfig = InvokePrivate<DataConfig>("TryLoadConfig", "db.config");
                log.AppendLine("TryLoadConfig(\"db.config\") => " + Describe(baseConfig));

                // 再看环境配置
                envConfig = InvokePrivate<DataConfig>("TryLoadConfig", "db.dev.config");
                log.AppendLine("TryLoadConfig(\"db.dev.config\") => " + Describe(envConfig));

                // 最终公开入口的结果
                try
                {
                    var config = DataConfig.GetConfig("SqlServer");
                    result = config == null
                        ? "GetConfig 返回 null"
                        : string.Format("Key={0} ProviderName={1}",
                            config.Key, string.IsNullOrEmpty(config.ProviderName) ? "<空>" : config.ProviderName);
                }
                catch (Exception ex)
                {
                    result = string.Format("{0}: {1}", ex.GetType().Name, ex.Message);
                }
            }
            finally
            {
                Trace.Listeners.Remove(listener);
            }

            var trace = listener.ToString();
            Console.WriteLine("---- DataConfig 诊断（net452）----");
            Console.Write(log.ToString());
            Console.WriteLine("GetConfig(\"SqlServer\") => " + result);
            Console.WriteLine(trace.Length == 0
                ? "内部 Trace: （无输出 —— 没有异常被捕获）"
                : "内部 Trace:\n" + trace);
            Console.WriteLine("---- 诊断结束 ----");

            // 真断言：net452 下也应当能读到 SqlServer 连接。
            // 失败时把逐层探测结果拼进消息，#18 的真实断点会直接出现在 CI 日志里。
            var readable = result.IndexOf("ProviderName=", StringComparison.Ordinal) >= 0
                        && result.IndexOf("<空>", StringComparison.Ordinal) < 0;

            Assert.True(readable, string.Format(
                "DataConfig 在 net452 下未能读到连接。{0}--- 逐层探测 ---{0}{1}Trace: {2}",
                Environment.NewLine, log, trace));
        }
    }
}