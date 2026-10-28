using Xunit;

namespace FastData.Tests
{
    /// <summary>
    /// 共享 perf_users 表的测试集合。
    /// <para>
    /// 背景（issue #17）：多个集成测试类并行执行时，都在同一张 perf_users 表上
    /// 「开事务 → 写 → 回滚」。SQL Server 的锁管理比 MySQL/PG 激进，
    /// 并发时会出现 `Transaction was deadlocked on lock resources ... chosen as the deadlock victim`，
    /// 被选中当牺牲者的那个测试就失败——所以每次失败的用例都不一样，
    /// 表现为「间歇性失败、重跑就过」。
    /// </para>
    /// <para>
    /// 归入同一 Collection 的测试类之间不会并行执行，从而消除这类互相踩锁。
    /// 代价是这些类串行跑，CI 时间变长；稳定性优先。
    /// </para>
    /// </summary>
    [CollectionDefinition(Name)]
    public sealed class SharedDatabaseCollection
    {
        /// <summary>集合名称。</summary>
        public const string Name = "SharedDatabase";
    }
}