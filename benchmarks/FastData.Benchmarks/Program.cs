using BenchmarkDotNet.Running;

namespace FastData.Benchmarks
{
    /// <summary>
    /// 基准入口。
    /// <para>
    /// 用法：
    ///   dotnet run -c Release --project benchmarks/FastData.Benchmarks            # 全部
    ///   dotnet run -c Release --project benchmarks/FastData.Benchmarks -- --filter '*Simple*'
    /// </para>
    /// <para>
    /// 结果在 BenchmarkDotNet.Artifacts/results/ 下，包含 markdown 与 json。
    /// CI 只跑一轮并归档产物，不做阈值判定 —— 阈值判定需要长期趋势数据，
    /// 单次结果波动足以造成误报。
    /// </para>
    /// </summary>
    public static class Program
    {
        public static void Main(string[] args)
        {
            BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
        }
    }
}