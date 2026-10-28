using System;
using System.Diagnostics;
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
    /// 存在的理由：同样的用例在 net8.0 上通过、在 net452 上失败，
    /// 而真实异常被 TryLoadConfig / ParseXmlConfig 的 catch 吞掉，
    /// 只剩「可用 Key 为空」一个现象。
    /// 这个用例不判断对错，只把内部 Trace 打出来 —— 先看见，再决定怎么修。
    /// </para>
    /// <para>
    /// 待 #18 修复后，本类应改为断言 DataConfig 能正确读到连接。
    /// </para>
    /// </summary>
    public class DataConfigLoadDiagnostics
    {
        [Fact]
        public void Report_HowDataConfigLoadsOnThisTargetFramework()
        {
            var listener = new CapturingTraceListener();
            Trace.Listeners.Add(listener);

            string result;
            try
            {
                var config = DataConfig.GetConfig("SqlServer");
                if (config == null)
                    result = "GetConfig 返回 null";
                else
                    result = string.Format("Key={0} ProviderName={1} ConnStr={2}",
                        config.Key,
                        string.IsNullOrEmpty(config.ProviderName) ? "<空>" : config.ProviderName,
                        string.IsNullOrEmpty(config.ConnStr) ? "<空>" : "已读取");
            }
            catch (Exception ex)
            {
                result = string.Format("{0}: {1}", ex.GetType().Name, ex.Message);
            }
            finally
            {
                Trace.Listeners.Remove(listener);
            }

            // 用 xunit 的输出通道打出来，CI 日志里可见
            Console.WriteLine("---- DataConfig 诊断（net452）----");
            Console.WriteLine("GetConfig(\"SqlServer\") => " + result);
            Console.WriteLine("BaseDirectory = " + AppDomain.CurrentDomain.BaseDirectory);
            Console.WriteLine("db.config 存在 = " + System.IO.File.Exists(
                System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "db.config")));

            var trace = listener.ToString();
            Console.WriteLine(trace.Length == 0
                ? "内部 Trace: （无输出 —— 说明没走到 catch）"
                : "内部 Trace:\n" + trace);
            Console.WriteLine("---- 诊断结束 ----");

            // 这不是「只输出不断言」的用例——断言 DataConfig 在当前框架下能读到连接。
            // 失败信息里带上内部 Trace，这样 #18 的真实异常会直接出现在 CI 日志里。
            Assert.True(result.IndexOf("已读取", StringComparison.Ordinal) >= 0,
                string.Format(
                    "DataConfig 在 net452 下未能读到连接：{0}{1}内部 Trace：{2}",
                    result,
                    Environment.NewLine,
                    listener.ToString()));
        }
    }
}