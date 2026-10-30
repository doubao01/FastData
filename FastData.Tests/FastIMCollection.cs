using Xunit;

namespace FastData.Tests
{
    /// <summary>
    /// FastIM 相关测试共用的 collection 名。用常量而非字面量，避免三处拼写漂移。
    /// </summary>
    public static class FastIMCollectionNames
    {
        /// <summary>FastIM 静态门面状态相关测试。</summary>
        public const string StaticState = "FastIM static state";
    }

    /// <summary>
    /// FastIM 是进程级静态门面（<c>_state</c> 单例），任何调用 <c>FastIM.Configure</c>
    /// 的测试都会改写全局状态。xUnit 默认让不同测试类并行执行，两个类各自 Configure
    /// 之后，先配置的那个类再发消息就会走到后者的 Sender 上 —— 表现为
    /// <c>Assert.Contains(sender.PrivateMessages, ...)</c> 随机失败（macOS net10.0 上
    /// 实际挂过一次）。
    ///
    /// <c>DisableParallelization = true</c> 让这个 collection 不与其它 collection 并行，
    /// 类之间再按 collection 顺序串行。新增的测试只要碰 FastIM 静态状态，就必须挂上它。
    /// </summary>
    [CollectionDefinition(FastIMCollectionNames.StaticState, DisableParallelization = true)]
    public sealed class FastIMCollection
    {
    }
}