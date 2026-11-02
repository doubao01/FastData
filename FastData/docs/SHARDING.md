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
| 建分表：`ShardingManager.CreateTable` / `AutoCreateTable` | **占位实现，别用**——只建一列 `Id` |
| `ShardingManager.QueryFromShard` / `InsertToShard` / `UpdateInShard` / `DeleteFromShard` | **未实现，调用即抛 `NotSupportedException`** |
| 地理分片（`ShardingType.Geo`） | 只有枚举值和配置类，没有策略实现 |
| 查询频率分片 | 路由可用，但频次统计要业务自己喂 |

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
    AutoCreateTable = false             // 建议关掉，内置建表是占位实现
});
```

| `ShardingConfig` 字段 | 说明 |
|---|---|
| `EntityType` | 由 `Configure<T>` 自动写入 |
| `BaseTableName` | 分表名 = 基础表名 + 后缀 |
| `ShardingType` | 选哪一种策略 |
| `SuffixFormat` | **声明了但代码里从未读取**，表名格式在 `ShardingNameResolver` 里写死 |
| `TimeConfig` / `HashConfig` / `ListConfig` / `CompositeConfig` / `FrequencyConfig` | 各策略自己的字段名、粒度、分片数、值映射 |
| `GeoConfig` | 同上，但对应策略没实现 |
| `MaxTableCount` | 默认 100，**只在时间分片的「枚举全部表」里生效** |
| `AutoCreateTable` | 默认 `true`，由 `ShardingManager.AutoCreateTable` 读取；建表本身是占位实现 |
| `DatabaseKey` | 该实体的分表在哪个连接配置下 |

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
4. **内置建表是占位的。** `ShardingManager.GenerateCreateTableSql` 固定返回
   `CREATE TABLE IF NOT EXISTS [表名] (Id INT PRIMARY KEY)`，不看实体字段；`CreateTable`
   还把异常全吞成 `false`。分表请自己建，并把 `AutoCreateTable` 关掉。
5. **查询频率统计在进程内。** `QueryFrequencyShardingStrategy` 的统计是静态字典：不持久化、
   多实例不同步，而且**没有任何读取路径会调用 `RecordQuery`**——要自己在业务里喂。
6. **地理分片没实现。** `ShardingType.Geo` 与 `GeoShardingConfig` 都在，但没有对应策略类，
   `GetStrategy` 会抛 `未注册的分表策略类型: Geo`。
7. **`SuffixFormat` 配了不生效**（见上表）。
8. **`WithShardingConfig` 配了不生效。** `ShardingConfigOverride` 只有写入点没有读取点，
   分片路由实际读的是 `ShardingManager.GetConfig<T>()`。
9. **配置是进程级全局。** `ShardingManager` 是静态类，配置放在静态字典里；测试之间、
   多租户场景要 `ShardingManager.Clear()`。
10. **表名会被按库改写。** PostgreSQL 下转小写、MySQL 下加反引号（`FormatTableName`），
    物理表名要和这个规则一致，否则 PostgreSQL 上会找不到表。
11. **用之前必须 Configure。** 未配置的实体调 `ShardQuery` / `ShardAdd` 会抛
    `Entity type X is not configured for sharding.`

## 验证到什么程度

| 测试 | 数量 | 跑在哪 | 实际覆盖 |
|---|---|---|---|
| `ShardingTests` | 40 | `build-core`，6 个平台/TFM 组合 | 表名计算、策略行为、配置隔离、策略注册——纯逻辑，不连库 |
| `ShardingCrudIntegrationTests` | 12 | `integration-test` | **名字里有 Integration，但全程不连库**（SQLite 不支持动态建表），只验表名与配置 |
| `MultiDatabaseShardingCrudTests.ShardedCrud_RoutesToPhysicalTablesAcrossDatabases` | 1 个 Theory × N 个库 | `integration-test`（`FASTDATA_RUN_DB_INTEGRATION=true` + MySQL / PostgreSQL / SQL Server 容器） | 唯一真连库的：`ShardAdd` / `ShardQuery` / `ShardUpdate` / `ShardQueryPage` / `ShardDelete` 全链路路由 |

没有覆盖：`QueryFromShard` 等四个未实现方法、`AutoCreateTable` 建出来的表结构、
多线程并发分片查询、超大候选表集合下的分页表现。

## 相关文档

- [现代 ORM 特性](./MODERN_ORM_FEATURES.md)——变更跟踪；其「数据库迁移」一节是声明而非实现，见[已知问题](./KNOWN_ISSUES.md)
- [已知问题](./KNOWN_ISSUES.md)——当前未处理的问题与理由
- [连接池调优](./POOL_TUNING.md)——分表数量对连接池的影响
- [配置说明](./CONFIGURATION.md)——`DatabaseKey` 对应的连接配置怎么写
