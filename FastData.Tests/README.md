# FastData.Tests

FastData ORM 生态的测试项目。xUnit 驱动，分两批执行：不依赖外部数据库的单元测试，以及需要真实
MySQL / PostgreSQL / SQL Server 的集成测试。

## 测试框架

| 组件 | 版本 |
|------|------|
| xUnit | 2.6.2 |
| xunit.runner.visualstudio | 2.5.4 |
| Microsoft.NET.Test.Sdk | 17.8.0 |
| 目标框架 | `net8.0; net10.0` |

版本统一由仓库根的 `Directory.Packages.props` 管理，csproj 里只写 `PackageReference`、不写版本号。

## 测试结构

70 个 `.cs` 文件（不含 `obj/` 生成物），其中 7 个是测试辅助产物（5 个集合定义/初始化/配置，
加上 `StubHttpServer.cs`、`MultiDatabaseTestHelper.cs`），其余 63 个含测试用例。
表中数字是 `[Fact]` / `[Theory]` 的**静态计数**，`[Theory]` 展开成多组数据后实际执行数会更多——
用它衡量规模，不要拿它和 CI 的 passed 数对账。

### 根级测试（35 个）

| 领域 | 文件 | 用例数 | 说明 |
|------|------|--------|------|
| 配置 | `ConfigParsingTests.cs` | 4 | 配置文件解析 |
| 配置 | `ConfigFullChainTests.cs` | 3 | 配置 → DataContext → CRUD 端到端（串行集合） |
| 配置 | `ActiveEnvironmentTests.cs` | 5 | 环境切换（db.dev/db.pro） |
| 配置 | `ApplicationWarmupTests.cs` | 5 | 启动预热 |
| 配置 | `ProviderAutoRegistrationTests.cs` | 5 | 无需手工注册 `DbProviderFactory` |
| 配置 | `DatabaseProviderMappingsTests.cs` | 6 | Provider 名称规范化 |
| 配置 | `DbInitTests.cs` | 3 | 库初始化脚本 |
| 配置 | `DbTableNamesTests.cs` | 6 | 多库表名映射 |
| 查询 | `ExpressionParsingTests.cs` | 40 | 表达式解析，覆盖边界与常见查询模式 |
| 查询 | `BaseFieldTests.cs` | 7 | 单字段与匿名对象两种字段写法 |
| 查询 | `ConditionBuilderTests.cs` | 3 | 条件拼接 |
| 查询 | `PaginationTests.cs` | 16 | 分页结果、投影、字典结果 |
| 查询 | `DeleteOptimizationTests.cs` | 4 | DELETE 语句生成 |
| 查询 | `WriteOperationTests.cs` | 4 | 写入模型 |
| ORM | `OrmCrudTests.cs` | 26 | CRUD 覆盖，**在集成 job 跑**（需要真库） |
| ORM | `OrmPerformanceTests.cs` | 12 | ORM 核心操作性能基线 |
| 连接池 | `ConnectionPoolTests.cs` | 16 | 池操作、智能调整、弹性写入 |
| 连接池 | `ConnectionPoolConcurrencyTests.cs` | 8 | 熔断、并发竞争、限流降级、泄漏检测 |
| 连接池 | `ConnectionPoolConfigTests.cs` | 4 | 池参数解析 |
| 压测 | `StressTests.cs` | 18 | 高并发场景，两个用例在集成 job 跑 |
| 分表 | `ShardingTests.cs` | 40 | 策略（时间/哈希/列表/组合/查询频率）+ Manager 生命周期 + 合并进来的 CRUD |
| 分表 | `ShardingCrudIntegrationTests.cs` | 12 | 多库物理分表 CRUD、分页、路由 |
| 功能 | `CacheTests.cs` | 16 | 内存缓存与 Redis 缓存 |
| 功能 | `MessageQueueTests.cs` | 9 | 可靠队列 / Stream / 写入队列 |
| 功能 | `AopTests.cs` | 7 | AOP 拦截器 |
| 功能 | `ExceptionManagerTests.cs` | 18 | 异常记录、历史清理、远端上报 |
| 安全 | `SecurityTests.cs` | 13 | 服务器监控、JWT、加解密 |
| 安全 | `EncryptionTests.cs` | 13 | 连接字符串加密（`FD2:` 格式与 v1 兼容） |
| FastIM | `FastIMTests.cs` | 4 | QQ 机器人门面（串行集合） |
| FastIM | `FastIMCommandRegistrationTests.cs` | 5 | 12 个指令的注册集合与 `dbrestart` 语义（串行集合） |
| 辅助 | `ConfigEnvironmentCollection.cs` | — | 配置类测试的串行集合定义（禁用并行） |
| 辅助 | `FastIMCollection.cs` | — | FastIM 静态状态的串行集合定义（禁用并行） |
| 辅助 | `SharedDatabaseCollection.cs` | — | 共享 `perf_users` 表的串行集合定义 |
| 辅助 | `DbInitializer.cs` | — | 为各数据库建测试表 |
| 辅助 | `TestConfig.cs` | — | 测试配置初始化（走自动扫描注册） |

### 子目录测试

| 目录 | 文件 | 用例数 | 说明 |
|------|------|--------|------|
| `Abstractions/` | `DateTimeProviderTests.cs` | 14 | 可替换时钟 |
| `Adapter/` | `DatabaseAdapterFactoryTests.cs` | 3 | 适配器工厂与同步数据类型 |
| `Base/` | `BaseUrlSingletonTests.cs` | 5 | issue #13 回归：共享 HttpClient 的请求头不得重复累积 |
| `Config/` | `DataConfigTests.cs` | 1 | 配置加载 |
| `Config/` | `DataSyncOptionsTests.cs` | 6 | 同步选项默认值与全局配置 |
| `Config/` | `SyncConfigManagerTests.cs` | 9 | 同步任务配置增删查 |
| `DevTools/` | `ApiClientTests.cs` | 13 | ApiClient 端到端契约（配 `StubHttpServer`） |
| `DevTools/` | `BackupFailureContractTests.cs` | 3 | 备份链路「空但成功」回归 |
| `DevTools/` | `RestoreReportingTests.cs` | 6 | 恢复失败必须记账、不得谎报 |
| `DevTools/` | `CodeGeneratorTests.cs` | 11 | 代码生成输出契约 |
| `DevTools/` | `DataImporterCsvTests.cs` | 10 | CSV 解析 |
| `DevTools/` | `QueryOptimizerTests.cs` | 8 | SQL 改写安全性 |
| `DevTools/` | `LogAggregatorTests.cs` | 4 | 停止必须能有界返回 |
| `DevTools/` | `TaskSchedulerTests.cs` | 13 | 调度行为 |
| `DevTools/` | `StubHttpServer.cs` | — | 最小 HTTP 服务端，测试辅助 |
| `Integration/` | `PerDatabaseOrmTests.cs` | 33 | 每个数据库的完整 ORM 能力 |
| `Integration/` | `MultiDatabaseConnectionLifecycleTests.cs` | 12 | 连接生命周期、健康检查、扩缩容 |
| `Integration/` | `MultiDatabaseConcurrencyTests.cs` | 3 | 并发读写与池竞争 |
| `Integration/` | `MultiDatabaseTransactionTests.cs` | 4 | 提交、回滚、事务内更新 |
| `Integration/` | `MultiDatabaseEdgeTests.cs` | 7 | 空结果、Unicode、长字符串、布尔边界 |
| `Integration/` | `MultiDatabaseReliabilityTests.cs` | 5 | 驱动差异下的可靠性 |
| `Integration/` | `MultiDatabaseRedisQueueTests.cs` | 2 | 跨库 Redis 队列写入与降级 |
| `Integration/` | `MultiDatabaseShardingCrudTests.cs` | 1 | 多库分表 CRUD |
| `Integration/` | `DatabaseIntegrationTests.cs` | 1 | 集成测试共用实体与冒烟 |
| `Integration/` | `MultiDatabaseTestHelper.cs` | — | 集成测试辅助 |
| `Monitoring/` | `OperationsPanelTests.cs` | 12 | 运维面板输出契约 |
| `Security/` | `SensitiveDataTests.cs` | 3 | 敏感信息脱敏与假成功回归 |
| `Sync/` | `Sync/DataRowSerializerTests.cs` | 13 | DataRow 序列化 |
| `Sync/` | `DataRowSerializerTypeTests.cs` | 12 | 列类型名解析与还原 |
| `Sync/` | `DataSyncServiceHelperTests.cs` | 20 | 同步核心纯函数逻辑 |
| `Sync/` | `PrimaryKeyConfigServiceTests.cs` | 10 | 主键配置 |
| `Sync/` | `ShardingNameResolverTests.cs` | 20 | 分表名解析（所有策略） |
| `Sync/` | `SyncConfigManagerTests.cs` | 9 | 配置持久化（原子写入）与游标 |
| `Sync/` | `TimeRangeCalculatorTests.cs` | 6 | 同步时间窗计算 |
| `Sync/` | `LoggerTests.cs` | 5 | 任务级日志文件、级别过滤 |

## 并行化控制

xUnit 默认让不同测试类并行跑。凡是触碰**进程级静态状态**或**同一张表**的测试必须显式串行化，
否则会互相污染，表现为「随机某个平台偶发失败」。仓库里现有五个集合：

| 集合 | 成员 | 保护对象 |
|------|------|----------|
| `SharedDatabase` | 13 个类（OrmCrud、OrmPerformance、ConnectionPool、DbInit、ProviderAutoRegistration、StressTests 及 7 个 `Integration/`） | 共享的 `perf_users` 表 |
| `Sharding` | `ShardingTests`、`ShardingCrudIntegrationTests`、`MultiDatabaseShardingCrudTests` | `ShardingManager` 静态注册表 |
| `ConnectionPool` | `ConnectionPoolConcurrencyTests` | 连接池静态计数与熔断状态 |
| `StaticState` | `FastIMTests`、`FastIMCommandRegistrationTests` | `FastIM` 的 `_state` 与指令表 |
| `ConfigEnvironmentCollection` | `ActiveEnvironmentTests`、`ConfigFullChainTests` | `DataConfig` 静态缓存 + 环境变量改写 |

两种机制要分清：

- **加入同一个集合**（`[Collection("X")]` + `[CollectionDefinition("X")]`）——只保证集合内串行，
  该集合仍可与其它集合并行。`SharedDatabase`、`Sharding`、`ConnectionPool` 用的是这一档。
- **`DisableParallelization = true`** ——该集合同时不与其他集合并行，用于会改进程级环境变量或
  静态单例的测试。目前只有 `StaticState` 和 `ConfigEnvironmentCollection` 需要这一档。

新增测试的判断标准：**被测代码里有没有 `static` 字段被写、或者是否和别人共表**。有，就得挂集合。

## 运行测试

```bash
# 不依赖外部数据库的部分（CI 第一个测试步骤用的就是这条）
dotnet test FastData.Tests/FastData.Tests.csproj --framework net10.0 -v minimal \
  --filter "FullyQualifiedName!~Integration&FullyQualifiedName!~OrmCrudTests"

# 单个测试类
dotnet test FastData.Tests --filter "FullyQualifiedName~ShardingTests"

# 真实多数据库集成测试（需要本地起 MySQL / PostgreSQL / SQL Server）
FASTDATA_RUN_DB_INTEGRATION=true dotnet test FastData.Tests/FastData.Tests.csproj \
  --framework net10.0 -m:1 \
  --filter "FullyQualifiedName~Integration|FullyQualifiedName~OrmCrudTests"
```

`-m:1` 不要省。集成测试共用容器里的库，并行跑会互相踩表。

CI 把 `OrmCrudTests` 和 `StressTests.Concurrent` / `StressTests.MixedOperations` 从第一个步骤
排除、放到集成 job 里跑——它们需要真库，在没有数据库的机器上以前是靠 `Console.WriteLine("SKIP")`
伪装通过的，issue #3 之后不再允许这种做法。

## 当前结果

CI run#90（2026-10-30），12 个 job 全绿：

| 批次 | 结果 |
|------|------|
| 单元测试（无外部库） | 568 passed / 0 failed / 1 skipped |
| 集成测试（MySQL 8.0 / PostgreSQL 16 / SQL Server 2022） | 205 passed / 0 failed |

跳过的那 1 个是环境相关用例，不是被过滤掉的。

## 已知问题

- `StressTests` 的成功率阈值（0.3 / 0.5）宽到几乎没有约束力，实际等于只验证「进程没崩」。
  提高阈值的代价是无库环境下会大片变红，暂时保留，见 `FastData/docs/KNOWN_ISSUES.md`。
- 集成测试依赖 GitHub Actions 的 service container；本地跑需要自己起三个数据库并保证库名、
  端口与 `.github/workflows/build.yml` 一致（库名 `FastDataTest` / `fastdatatest`，不是
  `fastdatademo`）。

## 依赖

- xUnit 2.6.2 / Microsoft.NET.Test.Sdk 17.8.0
- FastData、FastData.Untility
- Microsoft.Data.SqlClient、MySql.Data、Npgsql、Microsoft.Data.Sqlite

## 许可证

MIT License - see [LICENSE](../LICENSE) for details.