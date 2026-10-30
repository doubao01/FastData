# Changelog

本文档记录 FastData 的所有重要变更。格式基于 [Keep a Changelog](https://keepachangelog.com/zh-CN/1.0.0/)。

## [Unreleased]

### Added

- **运维面板（`FastData.Monitoring.OperationsPanel`）**
  - 把服务器监控、连接池指标、Redis 状态、接口探测、性能统计、异常通知六套分散能力聚合为一次快照
  - `CollectAsync` 并发采集，单部分失败不影响其他部分；`Render` 输出文本（问题项置顶），`ToJson` 输出可直接作为 `/health` 响应体的 JSON
  - `RunAsync` 在状态变差时推送 QQ 告警，同等级不重复推；恢复时补发通知
  - 不依赖 ASP.NET Core，暴露方式由宿主决定；兼容 net452，无新增三方依赖

- **连接池远程运维能力（`FastUntility.Monitor`）**
  - 新增 `IConnectionPoolReopenProvider` 接口，声明「按名称重建并预热连接池」能力
  - `ConnectionPoolFactory` 实现 `IConnectionPoolInfoProvider` 与 `IConnectionPoolReopenProvider`，可直接作为 `dbstatus` / `dbclose` / `dbrestart` 的数据源
  - `ConnectionPoolFactory` 记录每个池的构造参数（连接工厂 + 配置），使重建时能按原样恢复
  - `FastIM.Configure` 新增接收 `IConnectionPoolInfoProvider` 的重载，默认使用 `ConnectionPoolFactory.Instance`

### Fixed

- **`dbrestart` 指令谎报重启（`FastData.Untility/Monitor`）**
  - 原实现只调用 `ClosePool` 就返回「已重启连接池: X」，池实际未重建也未预热
  - 现在：提供者实现 `IConnectionPoolReopenProvider` 时执行关闭+重建+预热，预热无连接时返回失败并说明原因；
    未实现时如实回复「已关闭，将在下次访问时按需重建」，不再把关闭说成重启

- **`FastIM` 门面只注册 4 个内置指令（`FastData/FastIM.cs`）**
  - 此前仅 help / status / memory / gc 可用，cpu / disk / process / dbstatus / dbclose / dbrestart / version / time 经 FastIM 入口一律回「未知指令」
  - 现已注册全部 12 个，与 `ExceptionManager` 的注册集合一致

## [2.8.1] - 2026-10-18

### Fixed

- **数据同步工具与分表解析（FastData.Untility/Sync & FastData.SyncTool.WinForms）**
  - **分表命名统一**：新增 `ShardingNameResolver`，统一时间、哈希、列表、复合等策略的分表名解析规则，替换各策略内独立脆弱的拼接逻辑
  - **SQL 标识符转义安全**：对源表、目标表及所有同步列名增加统一白名单/字符校验及双引号/方括号安全转义，防止 SQL 注入
  - **跨数据库 Provider 规范化**：`DatabaseProviderMappings` 新增 `NormalizeProviderName`，界面下拉对齐驱动 Invariant Name，修复驱动解析失败
  - **分页与偏移计算**：修复 `DataSyncService` 最后一页取模截断导致漏同步数据的问题，修复主键增量排序方向与分页条件
  - **游标持久化类型溢出**：主键增量同步使用 `LastPrimaryKeyValue` 字符串游标取代基于 `DateTime.Ticks` 的混用，避免大数值溢出与类型失真
  - **全量去重可配置性**：`DataSyncOptions` 引入 `GlobalAlwaysDeduplicate` 选项，支持显式控制中间库去重行为
  - **日志服务与线程安全**：重构 `Logger` 采用线程安全队列与单消费者后台写入，修复死锁与并发写冲突
  - **配置管理与异常处理**：优化 `SyncConfigManager` 配置文件缺失/损坏时的回退与原子写入，避免配置静默丢失
  - **WinForms 界面与调度解耦**：移除主界面阻塞式轮询，重构调度服务与取消令牌传递，修补分表任务多余参数编译错误
  - **文档准确性**：重写 `FastData.SyncTool.WinForms/README.md`，纠正虚构的项目结构、错误命名空间与不匹配的配置示例

### Added

- **单元测试**
  - 新增 `ShardingNameResolverTests` 覆盖时间/哈希/列表/复合全策略分表名生成
  - 新增 `DatabaseProviderMappingsTests` 覆盖 Invariant 名映射与短名容错
  - 新增 `SyncConfigManagerTests`、`LoggerTests`、`DataSyncServiceHelperTests`、`DataRowSerializerTypeTests` 覆盖同步服务辅助逻辑

## [2.8.0] - 2026-10-18

### Fixed

- **写入路径空条件保护**
  - `Update`/`Delete`/`UpdateAsync`/`DeleteAsync` 在 Lambda 无法解析为 WHERE 时快速失败，避免无条件全表更新/删除
  - `BulkUpdate`/`BulkDelete` 补齐空条件守卫、去除表别名、补全 WHERE 参数
  - `LambdaWhere` 无法解析表达式时返回 `IsSuccess=false`，不再静默当作无条件

- **分页逻辑统一**
  - 新增 `PaginationSqlBuilder`，同步/异步分页共用单一实现
  - 修正多数据库偏移量、GroupBy 计数、页码夹取后的起止行号计算

- **连接池与队列**
  - `SmartConnectionPool` 修复等待成功后进入 null/异常分支导致的信号量许可泄漏
  - `WriteBehindExecutor` 按实际入队数量统计成功/失败，未入队计入 FailedCount

- **缓存隔离**
  - `DataConfig.BuildCacheKey` 纳入项目名、数据库文件与 `FASTDATA_ACTIVE` 环境变量，修复跨项目缓存串用

- **表达式解析**
  - `BaseField.OrderBy`/`GroupBy` 支持单字段表达式，未知表达式不再静默返回空列表
  - `QueryField` 单字段投影不再退化为 `u.*`
  - 修复布尔成员在二元比较中重复拼接表别名的问题

### Added

- **企业级特性**
  - `ConfigModel.CommandTimeout`（默认 30 秒），经 `DataContext.CreateCommand()` 统一注入所有命令
  - `DataContext.BeginTransaction(IsolationLevel)` 重载，重复开启抛 `InvalidOperationException`，提交/回滚后清理事务
  - `Dispose` 时回滚未提交事务

- **测试**
  - 新增 `BaseFieldTests` 覆盖单字段与匿名对象的字段/排序/分组解析

### Changed

- **工程化**
  - 测试项目目标框架调整为 `net8.0;net10.0`
  - `FastData.csproj` 移除 net452 下冗余的 RestSharp/NPOI 版本覆盖
  - `PackageId` 统一为 `Fast.Data`
  - GitHub Actions 构建矩阵补充 `-f/--framework` 参数
  - 软删除写入配置删除值；导出不再强制截断为单行

## [2.7.0] - 2026-10-16

### Fixed

- **DevTools 编译与架构修复**
  - 修复 `FastData.csproj` 对 DevTools 的排除策略，仅在 net452 下排除，net8.0/net10.0 全量编译
  - 修复 8 个 DevTools 历史编译错误文件（AuditLogger、CacheManager、DatabaseComparer、DatabaseDiagnostic、DataImporter、DatabaseBackupRestore、DistributedTransactionManager、DevToolsExamples）
  - 修复 `TaskScheduler.cs` 未等待异步调用的 CS4014 警告
  - 修复 `MapXml.cs` 中 DES 解密调用过时 API 的 CS0618 警告，并修正输入参数为空串的真实缺陷
  - 修复 `SmartConnectionPool.cs` 成功/失败统计遗漏路径与半开限流计数未递增问题
  - 加固 `SqlServerDialect.cs` 的 `ApplyTake` 实现，支持大小写、多余空白、`SELECT DISTINCT` 及已存在 `TOP` 的幂等保护

- **表名帮助类去重与整合**
  - 将 `FastData.ChangeTracking.TableNameHelper` 统一转发至 `FastData.Base.TableNameHelper`
  - `FastData.Base.TableNameHelper` 支持 `TableNameAttribute` 特性映射与 `Type` 实例入参重载
  - `ApplicationWarmup.cs` 移除脆弱的动态反射查找，直接调用强类型重载

### Added

- **DevTools 完整 XML 文档注释**
  - 为 DevTools 模块内全部 18 个包含公共成员的文件补齐规范 XML 文档注释（涵盖 519 个公共成员），使 CS1591 警告清零

### Changed

- **文档与规范对齐**
  - 对齐 `README.md` 中的 DevTools 工具清单：准确描述为 21 个核心独立工具 + 1 组完整使用示例集
  - 对齐 `ARCHITECTURE_OPTIMIZATION_REPORT.md`，明确基准测试项目处于规划设计阶段，保持解决方案 7 个核心与配套项目的一致性

## [2.6.0] - 2026-06-13

### Added

- **代码生成工具增强**
  - 新增 `ShardingReadHelper`/`ShardingWriteHelper` 分表读写示例
  - 更新 `FastDataClientQueueExample` 链式 Redis 队列配置
  - 更新 `ReliableQueueExample` 多数据库队列降级场景
  - 更新 README 反映最新 API 和使用方式

### Changed

- **文档更新**
  - `FastData.Example/README.md` 新增分表/队列示例说明
  - `FastData.Tests/README.md` 更新测试结果和集成测试命令
  - `README.md` 版本更新到 v2.6.0

### Technical Details

- 构建验证：`dotnet build -c Release --framework net10.0`：0 errors
- 全量测试：527 passed, 1 skipped, 0 failed
- 多数据库集成测试：109/109 通过（SqlServer/MySql/PostgreSql/Sqlite，Redis 可用时覆盖队列）
- `git diff --check`：clean

## [2.5.0] - 2026-06-12

### Changed

- **分表 CRUD 多数据库增强**
  - `ShardAdd` / `ShardUpdate` / `ShardDelete` / `ShardQuery` / `ShardQueryPage` 现在实际路由到物理分表
  - 分表写入路径按数据库方言格式化表名，覆盖 SQL Server / MySQL / PostgreSQL 的真实集成场景
  - 分表更新不再依赖基础表主键元数据，避免仅创建物理分表时生成空 SQL

- **Redis 队列写入可靠性增强**
  - 写入队列操作保留 `DatabaseKey`，队列消费时按原数据库 key 回写
  - 队列实体类型改为保存 `AssemblyQualifiedName`，提升跨程序集反序列化稳定性
  - 队列消费、降级恢复和直接执行路径统一使用强类型 `FastWrite<T>` / `DataContext<T>` 调用，避免反序列化为 `object` 后写入错误表

- **配置简化**
  - 简化连接配置节点：必填项只保留 `Key`、`Provider`、`ConnStr`，其他属性均有合理默认值
  - 统一手写 XML 解析与 `ElementConfig` 的默认值行为
  - 移除配置文件中冗余的注释和过长的连接池配置示例

- **连接池可靠性改进**
  - 修复 `PooledConnection.Dispose()` 在借用 - 归还循环中泄漏连接的问题
  - 连接借用时重置 `_disposed` 标志，确保同一 wrapper 可安全重复使用
  - 优化连接释放路径，finalizer 不再归还连接到池，避免失联对象重新入池

- **多数据库兼容性增强**
  - `CodeFirst<T>` 现在按 `DataDbType` 生成各数据库方言的 DDL
  - 新增 `QuoteIdentifier()`、`BuildDropTableSql()`、`GetClrSqlType(..., dbType, isIdentity)`
  - 支持 PostgreSQL SERIAL/BIGSERIAL、SQLite INTEGER、MySQL AUTO_INCREMENT、Oracle IDENTITY 语法

- **命令对象生命周期优化**
  - 替换多处 `DisposeCommand(cmd)` 为 `cmd.Parameters.Clear()`，避免 dispose 后继续复用
  - 对齐同步/异步读取路径的 SQL Server `TOP` 查询构建，统一使用 `BuildBaseSelectQuery()`
  - 修复 `GetJsonAsync` 重复追加 predicate 参数的问题

- **Provider 自动注册改进**
  - 新增 `DbProviderAutoRegistrar.GetFactory()` / `EnsureProvider()` / `GetInstallCommand()`
  - 修正扫描逻辑，不再因第一个同名程序集不含 Factory 就提前返回
  - 缺失驱动时抛出包含 NuGet 安装命令的明确异常

### Added

- **链式 Redis 队列配置**
  - `FastWrite.QueueBuilder(dbName).WithRedis(...).WithQueue(...).Add(entity).Execute()` 支持在链式写入中显式配置 Redis
  - 支持传入 Redis 连接字符串或已有 `FullRedis` 实例

- **新增入口 API**
  - `Db.Use(key)` / `Db.Default`：更短的 ORM 静态门面
  - `FastDataClient.List/First/Count/Page`：常用查询短方法
  - `FastDataClient.Sql/Exec`：原生 SQL 短别名

- **新增测试**
  - `MultiDatabaseShardingCrudTests`：SQL Server / MySQL / PostgreSQL 分表 CRUD 与分页真实集成测试
  - `MultiDatabaseRedisQueueTests`：Redis 队列跨数据库写入、消费与链式 Redis 配置真实集成测试
  - `MultiDatabaseConnectionLifecycleTests`：连接生命周期、健康检查、连接池并发、扩缩容与泄漏 smoke 测试
  - `MultiDatabaseTransactionTests`：事务提交、回滚、批量插入和事务内更新测试
  - `MultiDatabaseConcurrencyTests`：并发读写、批量写入和连接池并发测试
  - `MultiDatabaseEdgeTests`：空结果、特殊字符、Unicode、长字符串、分页和布尔值边界测试
  - `ConfigFullChainTests`：配置端到端测试（默认值、连接池默认值、重复 Key fail-fast）
  - `MultiDatabaseReliabilityTests`：三库通用可靠性测试（async CRUD/事务回滚/Count/Take/Page）
  - `ProviderAutoRegistrationTests`：Provider 自动注册测试
  - `ApplicationWarmupTests`：启动预热机制测试
  - `DeleteOptimizationTests`：Delete 优化可靠性测试
  - `FastIMTests`：QQ 机器人门面测试

- **新增工具**
  - `ApplicationWarmup.cs`：应用启动预热工具（预加载配置/连接池/Provider）

### Fixed

- 修复分表读查询仍访问基础表的问题
- 修复分表新增、更新、删除未替换为物理分表的问题
- 修复 Redis 队列消费时 `Add<object>` / `Update<object>` / `Delete<object>` 导致写入错误表的问题
- 修复队列降级恢复路径缺少数据库 key，可能回写到默认库的问题
- 修复 SQL Server `Take/ToItem` 分支丢失 `WHERE` 子句的问题（同步路径已修复，异步路径本次对齐）
- 修复 MySQL/PostgreSQL/SQLite 分页 offset 行为：改为 0-based (`pModel.StarId - 1`)
- 修复 `UpdateAsync<T>(model, predicate, ...)` 异常分支漏写 `IsSuccess=false` 和 `Message`
- 修复 `DataContext` 连接池键使用调用方 raw key 而非解析后 `_config.Key` 的问题
- 修复配置缓存命中时重复加载并追加同一批连接，导致重复 Key 误报

### Technical Details

- 构建验证：`dotnet build -c Release --framework net10.0`：0 errors
- 非数据库测试：21/21 通过
- 多数据库可靠性测试：12/12 通过（SqlServer/MySql/PostgreSql）
- 多数据库分表/Redis 队列/连接/并发/事务/边界集成测试：109/109 通过（SqlServer/MySql/PostgreSql/Sqlite，Redis 可用时覆盖队列）
- 全量 net10 测试：527 passed, 1 skipped, 0 failed
- PerDatabase ORM 基线：33/33 通过
- `git diff --check`：clean

## [2.4.0] - 2026-06-02

### Changed

- **代码质量重构**
  - 统一异步方法命名规范：`AddListAsy` → `AddListAsync`、`UpdateAsy` → `UpdateAsync` 等
  - 重构 `AsyncHelper.cs`，移除反模式的 `Task.Run` 包装，使用 `TaskFactory.StartNew` 实现真正的异步操作
  - 修复 `ExceptionManager.cs` 单例实现，使用 `Lazy<T>` 实现线程安全
  - 改进 `DataContext.cs` 的资源管理，实现完整的 `IDisposable` 模式，修复连接池泄漏问题
  - 优化 `VisitExpression.cs` 访问级别，支持测试项目访问

- **Demo / Example 项目代码质量提升**
  - 替换所有 `Task.Run` 为 `Task.Factory.StartNew`，与核心库 AsyncHelper 保持一致
  - 修复 19 处 `DataQuery<T>` 泛型类型参数冗余问题
  - 移除 Demo 项目对 FastRedis/NewLife 的硬依赖，改用内存缓存实现
  - 修复 `SimpleLogger.Info()` 空参数重载
  - 修复 `BenchmarkController` 中 `Task.Factory.StartNew` 包装 async 方法的问题
  - 修复 Demo/Example 项目 CPM 版本冲突

### Added

- **依赖注入支持**
  - 新增 `FastDataDependencyInjection.cs` 提供 `IServiceCollection` 扩展方法
  - 支持 `AddFastData`、`AddFastDataWithKey`、`AddFastDataContext` 等注册方式
  - 新增 `FastDataOptionsBuilder` 配置构建器
  - 新增 `IFastRepositoryFactory` 抽象，解耦 Repository 创建
  - 新增 `FastRepositoryFactory` 实现

- **FastDataConfig 扩展**
  - 新增 `GetConfig(string key, string projectName)` 方法，支持完整配置对象访问

- **Provider.cs 数据库驱动常量**
  - 整理 `Provider.cs` 数据库驱动名称常量，集中管理
  - 添加详细的 .NET Framework 4.5.2 兼容性说明注释
  - 明确每个驱动在 net452 下的可用性

### Fixed

- 修复 `SqlErrorType` 缺少 `Db` 别名导致的向后兼容问题
- 修复 `Dispose(cmd)` 调用错误，改为 `DisposeCommand(cmd)`
- 修复 `FastData.Base.Config.CodeFirst` 引用错误，统一使用 `FastData.Base.DesignPatterns.CodeFirst`
- 修复 `OrderByDescending<T>` 泛型方法误用问题

### Documentation

- 精简文档结构，删除 11 个过时/重复文档
  - 删除 `.monkeycode/docs/` 下的 5 个报告文档
  - 删除 `.monkeycode/specs/` 下的 3 个任务规范文档
  - 删除 `FastData/Documentation/README.md`、`FastData/CODE_STYLE.md`、`FastData/DevTools/README.md`
  - 清理空目录：`.monkeycode/docs/`、`.monkeycode/specs/`、`FastData/Documentation/`
- 更新 MEMORY.md 文档结构说明
- 同步核心代码注释与文档描述保持一致

## [2.3.0] - 2026-05-31

### Added

- **FastUntility 新增工具类**
  - `Base/DateHelper.cs` - 日期时间工具
    - 时间戳转换（秒/毫秒）
    - 相对时间描述（"3 分钟前"、"2 小时前"）
    - 日期计算（周/月/季/年开始和结束时间）
    - 工作日判断、年龄计算
    - 中文日期格式化
  - `Base/CollectionHelper.cs` - 集合扩展工具
    - 空值安全扩展（IsNullOrEmpty/HasValue）
    - 安全遍历（ForEachSafe）
    - 分页扩展（Page/PageWithTotal）
    - 去重与分组（DistinctBy/GroupToDictionary）
    - 集合运算（Intersect/Except/Union/Concat）
    - 批量操作（Batch/BatchExecute）
    - 统计扩展（SumSafe/MaxSafe/MinSafe/AverageSafe）
    - 随机操作（Shuffle/Random）
    - 树形结构转换（ToTree）
  - `Page/ApiResponse.cs` - 统一 API 响应格式
    - `ApiResponse<T>` 泛型响应
    - `ApiResponse` 无数据响应
    - 静态工厂方法（Ok/Fail/NotFound/Unauthorized/Forbidden）
    - 内置时间戳和请求 ID
  - `Page/Result.cs` - 统一结果类型
    - `Result` 无数据结果
    - `Result<T>` 带数据结果
    - 与 ApiResponse 互转

- **Demo 新增控制器**
  - `ReportController.cs` - 报表统计
    - GroupBy 分组聚合
    - Join 关联查询
    - ToJson/ToDics/ToDataTable 导出
    - 投影查询（Select）
    - 聚合统计（Count/Sum/Avg）
  - `DataExportController.cs` - 数据导出
    - ToDics/ToDataTable/ToArray 导出
    - 投影导出
    - 分页导出
    - 字典导出
  - `AsyncController.cs` - 异步并发
    - AddAsy/UpdateAsy/DeleteAsy
    - ToListAsync 异步查询
    - 并发查询（多任务同时执行）
    - 并发写入（批量插入）
    - 事务操作
  - `DynamicQueryController.cs` - 动态查询
    - Where 条件构建器（动态拼接条件）
    - Any/All 存在性判断
    - First/Single 单条查询
    - In/Between/Like 范围查询
    - 动态排序和分页
  - `DataValidationController.cs` - 数据校验
    - NullSafety 空值安全
    - 字段验证（添加/更新前验证）
    - 异常处理（安全查询/安全写入）
    - WriteReturn 信息展示
    - 数据完整性验证

### Changed

- **日志系统改进**
  - `FastData/ConnectionPool/SmartConnectionPool.cs` - Console.WriteLine → BaseLog.SaveLog
  - `FastData/ConnectionPool/ConnectionPoolMonitor.cs` - Console.WriteLine → BaseLog.SaveLog
  - `FastData.Untility/Monitor/MonitorConfig.cs` - Console.WriteLine → BaseLog.SaveLog
  - `FastData.Untility/Monitor/MessageSender.cs` - Console.WriteLine → BaseLog.SaveLog
  - `FastData.Untility/Security/ServerMonitor.cs` - Console.WriteLine → BaseLog.SaveLog
  - `FastData/FastWrite.cs` - 空 catch → DbLog.LogException

- **异常处理改进**
  - `FastData.Untility/Security/ServerMonitor.cs` - 4 处空 catch → BaseLog.SaveLog
  - 所有空 catch 块现在都记录错误日志

- **代码质量改进**
  - 添加缺失的 using 语句（FastUntility.Base）
  - 修复 API 不一致问题
  - 补充 XML 文档注释

### Fixed

- **编译错误修复**
  - FastWrite.cs:678 - BaseLog 不存在 → DbLog.LogException
  - DynamicQueryController.cs - Where<T>条件构建器 API 修正
  - DataExportController.cs - ToArray/ToDics/ToPage API 修正
  - AsyncController.cs - 异步 API 签名修正

### Performance

- **测试结果**
  - 192/197 测试通过 (97.5%)
  - OrmCrudTests: 26/26 通过
  - ShardingTests: 40/40 通过
  - ConnectionPoolTests: 16/16 通过 (42s)
  - StressTests: 13/18 通过 (5 失败为容量限制，非 bug)

### Technical Details

- 新增 4 个工具类文件
- 新增 5 个 Demo 控制器文件
- 修复 10+ 处 Console.WriteLine
- 修复 7 处空 catch 块
- 代码质量扫描：全 solution 0 错误

---

## [2.2.0] - 2026-05-29

### Added

- **ModelGenerator 新增功能**
  - Tab 5: JSON 转 Model 转换器
    - 支持粘贴 JSON 文本或加载 JSON 文件
    - 自动类型推断（string/long/double/bool/List）
    - 支持嵌套对象和数组
    - 生成带 JsonPropertyName 特性的 C# Model 类
  - Tab 6: API 代码生成器
    - 使用 RestSharp 生成 API 客户端代码
    - 支持多种认证方式（Bearer/JWT/API Key/Token/Basic Auth）
    - 自动生成响应 Model（从 JSON 响应）
    - 生成 Service 层接口
  
- **代码生成器完善**
  - JsonToModelConverter 类 - JSON 解析和 Model 生成
  - ApiClientGenerator 类 - RestSharp API 客户端代码生成
  - 支持自定义类名、命名空间
  - 实时预览功能

- **文档更新**
  - FastData.ModelGenerator.WinForms/USER_GUIDE.md - 详细使用手册
  - 更新 README.md - Model Generator 使用说明

### Changed

- **API 代码生成器改用 RestSharp**
  - 替换 HttpClient 为 RestSharp
  - 支持更简洁的 API 调用语法
  - 内置多种认证方式支持
  - RestSharp 108.0.0 (.NET 6+/8/10)
  - RestSharp 106.11.7 (.NET Framework 4.5.2)

- **目标框架调整**
  - FastData.Tooling 支持 net452/net6.0/net8.0/net10.0
  - 保持与 .NET Framework 4.5.2 兼容

- **项目重构**
  - 合并 FastData.Shared 到 FastData.ModelGenerator.WinForms
  - ConnectionConfigManager 和 ConnectionManagerForm 移至 Components/ 目录
  - 删除 FastData.Shared 项目，简化项目结构

### Technical Details

- **JsonToModelConverter**
  - 自动解析 JSON 对象、数组、基本类型
  - PascalCase 命名转换
  - 特殊字符转义处理
  - 支持 System.Text.Json.JsonPropertyName 特性

- **ApiClientGenerator**
  - 生成 RestClient 封装类
  - 支持异步方法（async/await）
  - JWT Bearer 认证集成
  - 自动生成响应 Model

## [2.1.0] - 2026-05-28

### Added

- **MySQL 批量插入性能测试**
  - 单条插入测试
  - 批量插入测试（事务）
  - 不同批量大小性能对比
  - FastWrite 批量写入测试

- **MySQL → PostgreSQL 同步测试**
  - 数据同步测试
  - 批量同步性能测试
  - 数据一致性验证
  - 增量同步测试

- **ORM API 完整性验证**
  - Read API 测试（Query, Where, Contains, StartsWith, EndsWith）
  - Chainable API 测试（Where, Or, And, Select, OrderBy）
  - Write API 测试（Add, AddList, Update, Delete）
  - DataQuery Generic API 测试
  - Pagination API 测试
  - SQL Log API 测试
  - Sharding API 测试
  - Where Builder 测试

- **连接管理功能**
  - FastData.Shared 共享库
  - ConnectionManagerForm 连接管理界面
  - 支持新增、修改、删除连接
  - 连接配置持久化（JSON）
  - ModelGenerator 集成连接管理

- **Docker 支持**
  - docker-compose.yml 统一管理所有数据库
  - SQL Server, MySQL, PostgreSQL, SQLite 容器化

- **同步工具架构图**
  - 整体架构图
  - 同步流程图
  - 状态管理图
  - 数据流向图
  - 组件关系图
  - 部署架构图

### Changed

- 更新 CHANGELOG.md 记录最新变更
- 合并开发进度文档

## [2.0.0] - 2026-05-27

### Added

- **分表功能完整示例和演示**（bc832f7, 6304250, b1d0216, d2efa79, 89783ea, b0f4334）
  - ShardingFullExample.cs：SQL Server 分表完整示例
    - 批量数据插入（10000 条日志、5000 条订单）
    - 时间/哈希/列表/查询频率分表策略
    - 链式 API 演示
  - ShardingController.cs：Demo API 端点
    - /api/sharding/init：初始化分表环境
    - /api/sharding/insert-data：插入批量测试数据
    - /api/sharding/time/configure, /time/query：时间分表
    - /api/sharding/hash/configure, /hash/query：哈希分表
    - /api/sharding/list/configure, /list/query：列表分表
    - /api/sharding/frequency/configure, /frequency/record, /frequency/simulate, /frequency/hot：查询频率分表
    - /api/sharding/sync：数据同步到分表
    - /api/sharding/stats：分表统计信息
  - ShardingSyncControl.cs：SyncTool 分表同步 UI
    - 源数据库配置
    - 分表类型选择（时间/哈希/列表）
    - 创建分表
    - 数据同步
    - 分表统计
  - ShardingCrudControl.cs：分表 CRUD 操作界面
    - 查询：执行 SQL 查询并显示结果
    - 插入：向分表插入数据
    - 更新：更新分表数据
    - 删除：删除分表数据
    - 双击表名自动填充
  - ShardingTaskService.cs：后台分表任务服务
    - 启动分表任务（后台线程执行）
    - 暂停/恢复任务
    - 取消任务
    - 删除已完成任务
    - 分批处理（每批1000条）
    - 自动创建分表结构
  - ShardingTaskControl.cs：任务列表管理界面
    - DataGridView 显示所有任务进度
    - 颜色状态标识（运行中=蓝/暂停=黄/完成=绿/失败=红）
    - 暂停/恢复/取消/删除按钮
    - 每秒自动刷新进度
  - ShardingSyncControl.cs：增强的分表同步控制
    - 5种分表策略可视化配置
    - 预览分表结构
    - 启动后台任务
  - ShardingConfigSnapshot.cs：分表配置快照模型
    - 配置导入/导出（JSON格式）
    - 跨环境一致性验证
    - 验证结果（错误/警告）
  - ShardingConfigVisualizer.cs：可视化配置编辑器
    - DataGridView 显示所有配置
    - PropertyGrid 显示配置详情
    - 添加/编辑/删除配置
    - 颜色标识分表类型
    - 验证与开发环境一致性
    - 应用配置到分表管理器
  - ShardingConfigEditDialog.cs：配置编辑对话框
    - 5种分表策略独立配置面板
    - 可视化值映射编辑
  - ShardingDataControl.cs：数据操作控件
    - 分表统计：显示所有表名、记录数、类型、大小
    - 跨表查询：支持UNION ALL跨表查询，导出结果
    - 批量增删改：批量插入/更新/删除，危险操作确认
    - 数据导出：CSV格式导出
  - ShardingImportControl.cs：数据导入控件
    - 文件选择：支持CSV/Excel格式
    - 编码选择：UTF-8/GBK/GB2312
    - 表头检测：首行作为表头
    - 主键配置：指定主键字段
    - 数据预览：最多显示100条
    - 后台导入：带进度条的异步导入
    - 取消导入：支持中途取消
    - 自动分表路由：根据策略自动路由到目标表
    - Upsert逻辑：不存在则插入，存在则更新
    - 数据库独有记录保持不变
  - DataQuery.cs：分表属性改为 public
  - MainForm.cs：集成 ShardingSyncControl

- 多目标框架支持（net45/net6.0/net8.0/net10.0）
- SDK-style csproj 格式迁移（所有项目）
- 条件编译处理框架差异（NETFRAMEWORK/NET6_0_OR_GREATER）
- IFastRepository 接口拆分：IReadRepository、IWriteRepository、IMapRepository
- 连接字符串加密支持（DataContext.IsEncrypt + BaseSymmetric.Decrypto）
- Redis 单例模式（Lazy<FullRedis>）
- NewLife.Redis 6.0.2024.1006 替换 StackExchange.Redis（net6.0+）
- Newtonsoft.Json 升级至 13.0.3
- NPOI 分版本：2.5.6(net45) / 2.7.0(net6.0+)
- System.CodeDom 8.0.0（net6.0+ 条件编译）
- BinaryFormatter 替换为 System.Text.Json（net6.0+）
- FastData.Demo 验证项目（完整技术栈验证）
- 业务主键配置支持复合主键（如 `order_id,line_no`）
- 同步工具组件化重构：MainForm 从 1856 行拆分为 509 行 + 7 个组件 + 3 个服务
- 批量同步支持（BatchSyncResult 结果统计）
- 同步取消功能（CancelBatchSync）
- 百分比进度条（替代滚动条）
- 颜色日志输出（DEBUG 灰/INFO 白/WARN 黄/ERROR 红）
- 日志级别过滤、任务 ID 过滤、日志导出
- 定时任务管理对话框（执行历史、暂停/恢复）
- 同步结果详情对话框（成功/失败/跳过表数、读写统计）
- IoC 依赖注入容器（ServiceContainer）
- Logger 日志工具（Debug/Info/Warn/Error，按日期/任务分割文件）
- 数据库容器内存优化（SQL Server 1.5GB + MySQL 512MB + PostgreSQL 256MB）
- xUnit 2.6.2 测试框架迁移
- 162 个单元测试全部通过（net10.0）
- NuGet 包生成（FastUntility/FastData.Tooling/FastData/FastRedis）
- 综合验证测试脚本（verify-all.sh，34 项测试）
- 大表主键加载优化（GetMaxPrimaryKeyValueFromDb 直接查询数据库）
- 依赖注入服务注册扩展（SyncService/LogService/TaskSchedulerService）
- MainForm 拆分为 4 个 UserControl（DbConfigControl/SyncConfigControl/TaskManagerControl/ReplayControl）
- **消息队列支持**（基于 NewLife.Redis）
  - RedisReliableQueue 可信队列（单消费、消费确认、消息不丢失，适合数据库存储削峰）
  - RedisStream 多消费组队列（多消费组独立消费，适合多方推送解耦）
  - IMessageProducer/IMessageConsumer 接口抽象
  - MessageQueueFactory 工厂类
  - MessageQueueIntegrationService 集成服务
  - TableSyncConfig 配置驱动（EnableMessageQueue/MessageQueueType/MessageQueueTopic）
  - FastData.Demo API 端点（/api/mq/demo/reliable, /api/mq/demo/stream）
  - FastData.Example 示例代码
- **Lambda 查询扩展 API**
  - FastRead.Query<T>() Lambda 表达式查询
  - FastRead.Select<T, TResult>() 匿名类型投影查询
  - 支持复杂条件组合（Where/And/Or/In/Like/Between）
  - ProjectedQuery<T, TResult> 投影查询类
- **分页查询 API**
  - FastRead.ToPagination<T>() / ToPaginationAsync<T>() 泛型版本
  - FastRead.ToPagination() / ToPaginationAsync() 字典版本
  - PaginationRequest/PaginationResult<T> 模型
  - PaginationController Web API 示例
- **链式 WHERE 条件**
  - DataQuery.ChainedConditions 链式条件列表
  - FastRead.Where<T>() / Or<T>() 扩展方法
  - FastRead.And<T>() - Where 的别名
  - FastRead.Like<T>() / Contains<T>() / StartsWith<T>() / EndsWith<T>() - LIKE 条件
  - FastRead.In<T>() - IN 条件
  - FastRead.Between<T>() - BETWEEN 条件
  - WhereBuilder 统一 WHERE 子句构建
  - DataQuery.EntityType 属性（存储实体类型）
- **FastWrite 匿名类型支持**
  - Add/AddRange/Update/Delete 支持 tableName 参数
  - 无 new() 约束，支持匿名类型推断
- **XML Map SQL 生成器**
  - Model Generator 新增 XML Map SQL 生成功能
  - 支持多表关联查询配置

### Fixed

- DataRowSerializer 反序列化 IDictionary vs Dictionary 类型问题
- 业务主键配置参数未传递到同步方法（GetPrimaryKeyColumns 优先使用配置参数）

### Added (Latest)

- **FastWrite 队列匿名类型支持**
  - Add<T>(tableName, model)：支持匿名类型 INSERT
  - AddRange<T>(tableName, models)：支持匿名类型批量 INSERT
  - Update<T>(tableName, model)：支持匿名类型 UPDATE
  - Delete<T>(tableName, model)：支持匿名类型 DELETE
  - 用法：`FastWrite.QueueBuilder().Add("TableName", new { ... }).Execute()`
- **匿名类型投影查询（Select）**
  - ProjectedQuery<T, TResult>：投影查询类，支持匿名类型
  - FastRead.Select()：扩展方法，支持 `.Select(p => new { p.Id, p.Name })` 语法
  - 支持 ToList()、ToPagination()、ToItem()、ToCount() 等操作
  - 16 个单元测试验证分页计算和投影逻辑
- **分页查询 API（简化版）**
  - PaginationRequest：分页请求参数（Page/PageSize）
  - PaginationResult<T>：分页结果（Total/TotalPages/Page/PageSize/HasPrevious/HasNext/Data）
  - FastRead.ToPagination()：扩展方法，传入 page 和 pageSize 返回分页结果
  - FastRead.ToPaginationAsync()：异步版本
  - 支持泛型版本（PaginationResult<T>）和字典版本（PaginationResult）
  - FastData.Demo PaginationController：Web API 分页示例
  - FastData.Example PaginationExample：分页查询使用教程
- **Model Generator XML Map SQL 生成器**
  - XmlMapSqlGenerator.cs：从数据库表结构生成 FastData XML Map SQL
  - 自动生成 Select All / Select By PK / Select Dynamic / Insert / Update / Delete
  - MainForm 新增「预览XML」和「生成XML Map」按钮
- **FastData.Example 场景化教程示例**
  - MapSqlExample.cs：XML Map SQL 使用示例
  - TransactionExample.cs：事务使用示例
  - MultiDbExample.cs：多数据库使用示例
  - RawSqlExample.cs：原始 SQL 示例
  - Program.cs 菜单更新：覆盖全部 11 种 ORM 功能场景
- **分表功能（数据分片）**
  - IShardingStrategy 接口：分表策略抽象
  - ShardingConfig：分表配置模型（Time/Hash/List/Composite/Geo/QueryFrequency）
  - ShardingManager：分表管理器（策略注册/配置/管理）
  - TimeShardingStrategy：时间分表策略（Day/Week/Month/Quarter/Year 粒度）
  - HashShardingStrategy：哈希分表策略（Modulo/Consistent/CRC32 算法）
  - ListShardingStrategy：列表分表策略（枚举值映射）
  - CompositeShardingStrategy：组合键分表策略（组合键哈希/拼接）
  - QueryFrequencyShardingStrategy：查询频率分表策略（热数据/冷数据分离）
  - ShardingReadHelper：分表查询助手（Query/QueryPage）
  - ShardingWriteHelper：分表写入助手（Add/AddList/Update/Delete）
  - ShardingTests：21 个分表单元测试
  - README.md：分表功能完整文档
- **SyncTool 分表迁移组件**
  - ShardingMigrationControl：分表迁移管理组件
  - 支持 Time/Hash/List/Composite 分表类型
  - 批量迁移、进度显示、状态监控
  - 自动创建分表配置
- **链式分表查询 API**
  - DataQuery<T>.UseSharding()：启用分表查询
  - DataQuery<T>.WithShardingParam()：添加分表参数
  - DataQuery<T>.WithTimeRange()：时间范围分表参数
  - DataQuery<T>.WithHashField()：哈希字段分表参数
  - DataQuery<T>.WithListField()：列表字段分表参数
  - DataQuery<T>.WithShardingConfig()：覆盖全局分表配置
  - 默认不开启分表，需显式调用 UseSharding()
- **FastData.Example 分表示例**
  - ShardingExample.cs：覆盖所有分表策略和 API
  - Program.cs 新增选项 11：分表（数据分片）
- **FastData.Demo 分表 API**
  - ShardingController：分表功能演示控制器
  - 支持 Time/Hash/List/Composite/QueryFrequency 配置和查询
  - 查询频率记录和热数据查询 API
- **SyncTool 分表 CRUD 界面**
  - ShardingCrudControl：分表管理组件
  - 支持配置所有分表策略（Time/Hash/List/Composite/QueryFrequency）
  - 分表列表查询和条件查询
  - 数据查询和导出功能
- **分表 CRUD 测试**
  - ShardingCrudTests：33 个分表 CRUD 测试用例
  - 覆盖配置、启用/禁用、增删改查、链式 API
  - 总计 162 个单元测试全部通过

### Changed

- FastData.Tooling 目录重构：按功能分层（Logging/Models/Services/Utils）
- DataSyncService.CheckRowExists/UpdateRow/UpsertRow 接受 primaryKeyColumns 参数
- GetPrimaryKeyColumns() 优先使用 primaryKeyColumns 参数，回退到 DataTable.PrimaryKey
- CallContext → AsyncLocal（FastDb.cs 条件编译）
- 自定义 Assert 类 → xUnit 断言
- RedisInfo StackExchange 实现删除，统一使用 NewLife.Redis
- 文档整合：DEVELOPMENT_PROGRESS.md 合并开发进度文档

## [1.5.0] - 2026-05-26

### Added

- 代码质量优化完成（AsyncHelper、批量插入、JSON 修复）
- 单元测试从 3 个扩展到 69 个
- Docker 数据库环境搭建（SQL Server Express + MySQL 8.0 + PostgreSQL 15）
- DatabaseProviderMappings 统一数据库提供程序引用
- 可测试性抽象（DateTimeProvider）
- 完整同步测试脚本（Python）

### Fixed

- 手动 JSON 解析改用 JavaScriptSerializer
- SyncTool 数据补录 Tab 布局 row 索引越界
- ModelGenerator UI 布局冲突

### Changed

- 消除魔法字符串，使用 DatabaseProviderMappings 统一引用
- 提取 AsyncHelper 类，统一处理 Task.Run 反模式
- 批量插入优化，减少数据库往返次数

## [1.0.0] - 2026-05-25

### Added

- 核心 ORM 引擎（FastRead/FastWrite/FastDb/FastMap）
- Lambda 强类型查询
- XML Map SQL 管理
- 多数据库支持（Oracle/MySQL/SQL Server/SQLite/PostgreSQL/DB2）
- Code First / Db First 双模式
- AOP 拦截器
- Repository 模式
- 数据同步工具（SyncTool.WinForms）
- Model 生成工具（ModelGenerator.WinForms）
- FastRedis 缓存提供者
- FastUntility 通用工具库（日志/Excel/HTTP/XML/缓存）
