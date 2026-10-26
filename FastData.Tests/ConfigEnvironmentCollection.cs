using Xunit;

namespace FastData.Tests
{
    /// <summary>
    /// 配置相关测试的串行化集合。
    ///
    /// 背景：ConfigFullChainTests / ActiveEnvironmentTests 会修改进程级环境变量
    /// FASTDATA_ACTIVE（DataConfig.BuildCacheKey 将其拼入缓存键，ResolveActiveEnvironment
    /// 以其选择 db.{env}.config）。xUnit 默认跨测试类并行，其他测试类（如 OrmPerformanceTests）
    /// 在并行窗口内调用 FastRead.Query → GetConfig 时会被劫持到 duplicate-test/pro 配置，
    /// 产生 "Duplicate FastData connection key 'Sqlite'" 等偶发失败。
    ///
    /// 凡修改 FASTDATA_ACTIVE / FASTDATA_CONN_* 环境变量的测试类，必须加入本集合。
    /// </summary>
    [CollectionDefinition(nameof(ConfigEnvironmentCollection), DisableParallelization = true)]
    public class ConfigEnvironmentCollection
    {
    }
}
