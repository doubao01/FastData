# 分库分表

分片在 SQL 层做：按分片键算出表名后缀，再把要执行的 SQL 里的表名换掉。**没有**跨库分布式
事务、**没有**跨分表 JOIN、**没有**全局二级索引。

「分库」在目前只到「每个实体一个 `DatabaseKey`」这个粒度——按分片键的**值**选库做不到，
选表可以。

## 先看这段：哪些能用，哪些别用

| 能力 | 状态 |
|---|---|
| 写入路由：`ShardAdd` / `ShardAddList` / `ShardUpdate` / `ShardDelete` | 可用 |
| 读取路由：`ShardQuery` / `ShardQueryPage` | 可用，但读「已知限制」再决定 |
| 表名计算：`ShardingManager.GetTableName` / `GetTableNames` / `GetAllTableNames` | 可用 |
| 建分表：`ShardingManager.CreateTable` / `AutoCreateTable` | **不提供自动建表**：直接建表入口抛 `NotSupportedException`，自动创建默认关闭；显式启用后调用同样报错（[#26](https://github.com/doubao01/FastData/issues/26)） |
| `ShardingManager.QueryFromShard` / `InsertToShard` / `UpdateInShard` / `DeleteFromShard` | **过时兼容入口**：编译时警告，调用仍抛 `NotSupportedException`；提示中给出可用替代 API（[#28](https://github.com/doubao01/FastData/issues/28)） |
| 地理分片（`ShardingType.Geo`） | 只有枚举值和配置类，没有策略实现 |
| 查询频率分片 | 路由可用，但频次统计要业务自己喂 |

## 旧 CRUD 入口迁移

四个旧方法保留签名和原有拒绝执行行为，统一标为 `Obsolete(message, false)`。

| 旧入口 | 可用入口 |
|---|---|
| `QueryFromShard<T>(tableName, query)` | `FastDataClient.ShardQuery(predicate, queryParams)` / `ShardQueryPage`，或 `ShardingReadHelper.Query` / `QueryPage` |
| `InsertToShard(entity)` | `FastDataClient.ShardAdd(entity)` 或 `ShardingWriteHelper.Add(entity, key)` |
| `UpdateInShard(entity)` | `FastDataClient.ShardUpdate(entity, predicate, field)` 或 `ShardingWriteHelper.Update` |
| `DeleteFromShard(entity)` | `FastDataClient.ShardDelete(predicate, queryParams)` 或 `ShardingWriteHelper.Delete` |

查询和删除须提供业务条件及分片查询参数，旧的 `DataQuery` 对象不直接作为新入口参数使用。
可用入口要求实体满足 `class, new()`；写入返回 `WriteReturn`，应检查 `IsSuccess`，
而不是沿用旧接口的布尔返回值。物理表仍须自行建好，自动建表的拒绝行为见 [#26](https://github.com/doubao01/FastData/issues/26)。

## 四个入口

| 入口 | 用法 | 说明 |
|---|---|---|
| 门面（推荐） | `FastDataClient` 的 `Shard*` 方法 | 内部就是调下面两个助手 |
| 链式查询 | `Query<T>()` 之后的 `UseSharding` / `WithTimeRange` / `WithHashField` / `WithListField` | 终结方法走分片路由：`ToList` / `ToItem` / `ToCount` / `ToPagination` |
| 静态助手 | `ShardingReadHelper` / `ShardingWriteHelper` | 直接传 `key`，不依赖连接上下文 |
| 管理器 | `ShardingManager` | 配置与元数据；不要去调它的 CRUD 方法（见上表） |

```csharp
using var client = new FastDataClient("db1");

// 写：按实体的 CreateTime 路由到对应的月表
client.ShardAdd(new Log { Message = "boom", CreateTime = DateTime.Now });

// 读：必须给出分片键，否则会去枚举所有分表
var rows = client.ShardQuery<Log>(
    r => r.Message == "boom",
    new Dictionary<string, object>
    {
        ["CreateTime_Start"] = new DateTime(2026, 1, 1),
        ["CreateTime_End"] = new DateTime(2026, 3, 1)
    });

// 不经过门面时：ShardingReadHelper.Query<Log>(predicate, queryParams, "db1");
```

## 配置

`Time` / `Hash` / `List` / `Composite` / `QueryFrequency` 五种策略在 `ShardingManager`
静态构造里已注册，**不需要**手动注册；只有自定义策略才调 `ShardingManager.RegisterStrategy`。

配置通常是进程启动时按实体注册一次：

```csharp
ShardingManager.Configure<Log>(new ShardingConfig
{
    BaseTableName = "log",              // 留空则用 TableNameHelper 推导出的表名
    ShardingType = ShardingType.Time,
    TimeConfig = new TimeShardingConfig
    {
        TimeField = "CreateTime",
        Granularity = TimeGranularity.Month,
        StartTime = new DateTime(2026, 1, 1),   // 不填的话「查全部表」会返回空列表
        EndTime = null                          // 留空 = 一直算到当前时间
    },
    DatabaseKey = "db1",
    AutoCreateTable = false             // 默认关闭，完整分表结构应自行创建
});
```

| `ShardingConfig` 字段 | 说明 |
|---|---|
| `EntityType` | 由 `Configure<T>` 自动写入 |
| `BaseTableName` | 分表名 = 基础表名 + 后缀 |
| `ShardingType` | 选哪一种策略 |
| `SuffixFormat` | 时间、哈希、列表、组合键策略的 .NET 后缀格式；为空时沿用原来的表名规则 |
| `TimeConfig` / `HashConfig` / `ListConfig` / `CompositeConfig` / `FrequencyConfig` | 各策略自己的字段名、粒度、分片数、值映射 |
| `GeoConfig` | 同上，但对应策略没实现 |
| `MaxTableCount` | 默认 100，**只在时间分片的「枚举全部表」里生效** |
| `AutoCreateTable` | 默认 `false`；关闭或未配置时不执行操作，显式启用后调用会抛 `NotSupportedException`，不连接数据库或生成占位 DDL |
| `DatabaseKey` | 该实体的分表在哪个连接配置下 |

`SuffixFormat` 只格式化后缀，完整表名仍为 `{BaseTableName}_{后缀}`。`{0}` 分别接收
时间值、哈希分片索引、列表映射后缀，或组合键的值/分片索引；时间策略的周、季度格式
还可用 `{1}` 接收周序号、季度序号。例如 `{0:yyyy_MM}` 生成 `log_2026_01`，
`shard{0:D2}` 生成 `log_shard00`。单表路由和全表枚举使用同一套格式化逻辑。

`WithShardingConfig(config)` 配合 `UseSharding()` 可临时覆盖当前读取查询的分片路由配置，
用于 `ToList()` / `ToItem()` / `ToCount()` / `ToPagination()`，即使实体尚未全局注册也可使用。
覆盖不会修改 `ShardingManager` 的全局配置；连接仍由查询的 `Key` 决定，写入仍使用全局注册配置。

## 五种内置策略与表名规则

| `ShardingType` | 分片键配置 | 表名 | 默认分片数 |
|---|---|---|---|
| `Time` | `TimeConfig.TimeField` | 见下表 | 按时间粒度推 |
| `Hash` | `HashConfig.HashField` | `{Base}_{i:D4}`，`i = hash(值) % ShardCount` | 8 |
| `List` | `ListConfig.ListField` + `ValueMapping` | `{Base}_{映射后缀}`；映射里没有该值则 `{Base}_{原值}` | 按映射条数 |
| `Composite` | `CompositeConfig.CompositeFields` | `UseHash=true`（默认）→ `{Base}_{i:D4}`；否则 `{Base}_{值1_值2}`（`Separator` 默认 `_`） | 16 |
| `QueryFrequency` | `FrequencyConfig.Field` + `HotThreshold`（默认 100） | 热 → `{Base}_hot`；冷 → `{Base}_cold`，`ColdShardingType` 为 `ByHash` 时是 `{Base}_cold{hash}` | 冷数据默认 4 |

时间粒度：

| `TimeGranularity` | 表名 | 例子 |
|---|---|---|
| `Day` | `{Base}_{yyyyMMdd}` | `log_20261102` |
| `Week` | `{Base}_{yyyy}W{周序:D2}` | `log_2026W45`（ISO 周，周一起算） |
| `Month`（默认） | `{Base}_{yyyyMM}` | `log_202611` |
| `Quarter` | `{Base}_{yyyy}Q{季}` | `log_2026Q4` |
| `Year` | `{Base}_{yyyy}` | `log_2026` |

哈希算法（`HashConfig.Algorithm`）：

| `HashAlgorithm` | 实际实现 |
|---|---|
| `Modulo`（默认） | 31 多项式散列 |
| `Consistent` | **MD5 取绝对值——不是一致性哈希环**，加减分片数仍会大规模迁移 |
| `Crc32` | CRC32 |

## 查询参数决定去哪些表

`ShardQuery` / `ShardingReadHelper.Query` 的第二个参数是「分片参数」，它决定候选表集合，
**不参与 WHERE**（WHERE 由 predicate 决定，两者要自己保持一致）。

| 场景 | 候选表 |
|---|---|
| 时间分片 + `{TimeField}_Start` / `{TimeField}_End` | 范围内的表 |
| 时间分片 + 只给 `{TimeField}` | 当作起点，终点取 `EndTime` 或当前时间 |
| 时间分片 + 什么都不给 | 枚举全部表——**前提是配了 `TimeConfig.StartTime`**，没配则返回空列表，查询静默返回空集合 |
| 哈希分片 + 给了哈希字段值 | 单表 |
| 哈希分片 + 不给 | 全部 `ShardCount` 张表 |
| 列表分片 + 给了字段值 | 单表；值里带逗号时按 `IN` 处理，每个值得一张表 |
| 列表分片 + 不给 | **只枚举 `ValueMapping` 里出现过的后缀**，没进映射的值不会有表被命中 |
| 组合分片 + 给全组合字段 | 单表 |

## 已知限制

1. **分页是内存分页。** `ShardingReadHelper.QueryPage` 先把每张候选表的结果全取回来，
   再 `Skip` / `Take`。数据量大时这是「全表拉回 + 内存切片」，不是数据库分页。
   `ToCount()` 同样是把全部行拉回来再 `Count`。
2. **一张分表一次查询。** 每条 SQL 单独执行，没有 UNION，也没有跨表排序保证；
   候选表数量直接放大数据库往返和连接池占用（见[连接池调优](./POOL_TUNING.md)）。
3. **写入靠 SQL 文本替换。** `ShardingSqlHelper.ReplaceBaseTableName` 只认
   `insert into ` / `update ` / `delete from ` / `delete ` 四种前缀；匹配不上时**原样执行，
   也就是静默写到基础表**。`MERGE`、`INSERT INTO ... SELECT` 之类不要指望被改写。
4. **自动建表不属于当前可用能力**（[#26](https://github.com/doubao01/FastData/issues/26)）。
   `CreateTable` 和 `GenerateCreateTableSql` 直接抛 `NotSupportedException`；
   `AutoCreateTable` 默认关闭，显式启用后调用也会立即报错。五种内置策略的建表入口统一
   转发到这一拒绝路径，不再返回虚假的成功。完整字段和索引须由应用的建表脚本创建。
5. **查询频率统计在进程内。** `QueryFrequencyShardingStrategy` 的统计是静态字典：不持久化、
   多实例不同步，而且**没有任何读取路径会调用 `RecordQuery`**——要自己在业务里喂。
6. **地理分片没实现。** `ShardingType.Geo` 与 `GeoShardingConfig` 都在，但没有对应策略类，
   `GetStrategy` 会抛 `未注册的分表策略类型: Geo`。
7. **全局注册配置是进程级。** `ShardingManager` 的配置放在静态字典里；测试之间要
   `ShardingManager.Clear()`。读取可通过 `WithShardingConfig` 临时覆盖路由，写入不受覆盖影响。
8. **表名会被按库改写。** PostgreSQL 下转小写、MySQL 下加反引号（`FormatTableName`），
    物理表名要和这个规则一致，否则 PostgreSQL 上会找不到表。
9. **门面读写仍须 Configure。** 未配置的实体调 `ShardQuery` / `ShardAdd` 会抛
    `Entity type X is not configured for sharding.`

## 验证到什么程度

| 测试 | 数量 | 跑在哪 | 实际覆盖 |
|---|---|---|---|
| `ShardingTests` | 40 | `build-core`，6 个平台/TFM 组合 | 表名计算、策略行为、配置隔离、策略注册——纯逻辑，不连库 |
| `ShardingCrudIntegrationTests` | 12 | `integration-test` | **名字里有 Integration，但全程不连库**（SQLite 不支持动态建表），只验表名与配置 |
| `MultiDatabaseShardingCrudTests.ShardedCrud_RoutesToPhysicalTablesAcrossDatabases` | 1 个 Theory × N 个库 | `integration-test`（`FASTDATA_RUN_DB_INTEGRATION=true` + MySQL / PostgreSQL / SQL Server 容器） | 原有连库测试：`ShardAdd` / `ShardQuery` / `ShardUpdate` / `ShardQueryPage` / `ShardDelete` 全链路路由 |
| `ShardingConfigurationTests` | 10 | `build-core` | 默认后缀兼容、自定义格式路由与枚举一致、无效格式显式失败 |
| `MultiDatabaseShardingCrudTests.QueryConfigurationOverride_RoutesWithoutChangingGlobalConfiguration` | 6 | `integration-test` | 三种数据库、存在/不存在全局配置、四个读取入口、自定义后缀真实读写 |
| `ShardingTableCreationTests` | 12 | `build-core` | 默认关闭、关闭时无操作、启用时显式失败、五个策略不再静默成功 |
| `MultiDatabaseShardingCrudTests.AutomaticTableCreation_FailsWithoutCreatingTablesAndManualCrudRemainsUsable` | 3 | `integration-test` | 三种数据库中拒绝建表且没有新表；手动建完整表后的 CRUD 仍可用 |
| `LegacyShardingCrudTests` | 8 | `build-core` | 四个入口的警告属性、明确替代提示及原有运行时拒绝行为 |
| `MultiDatabaseShardingCrudTests.LegacyCrud_RejectionDoesNotChangeStoredDataAndFacadeCrudRemainsUsable` | 3 | `integration-test` | 三种数据库中旧调用不插入、修改或删除数据，可用门面的查询、更新、删除仍有效 |

没有提供：自动按实体字段生成分表 DDL，以及四个旧入口的真实 CRUD 执行。
旧入口的警告和拒绝行为已有覆盖；多线程并发分片查询、超大候选表集合下的分页表现尚未覆盖。

## 相关文档

- [变更跟踪](../MODERN_ORM_FEATURES.md)——属性快照与变更检测；迁移执行器未实现，调用时明确报错
- [已知问题](./KNOWN_ISSUES.md)——当前未处理的问题与理由
- [连接池调优](./POOL_TUNING.md)——分表数量对连接池的影响
- [配置说明](./CONFIGURATION.md)——`DatabaseKey` 对应的连接配置怎么写
