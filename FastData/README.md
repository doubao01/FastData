# FastData

轻量级多目标框架 ORM。在 .NET Framework 4.5.2 到 .NET 10 上跑同一套代码，
靠 `DbProviderFactory` 反射适配各家数据库，不把任何驱动钉死在依赖里。

单文件 XML 配置、Lambda 查询、CodeFirst 建表、分表、缓存、消息队列降级，
都在这一层里。

## 目标框架

| 框架 | 说明 |
|---|---|
| `net452` | .NET Framework 4.5.2（C# 7.3 语法上限） |
| `net8.0` / `net10.0` | Modern .NET，消息队列等功能仅在此可用 |

## 安装

```bash
dotnet add package Fast.Data
```

## 支持的数据库

| 数据库 | Provider | 备注 |
|---|---|---|
| SQL Server | `Microsoft.Data.SqlClient` | .NET 6+ 推荐 |
| SQL Server | `System.Data.SqlClient` | .NET Framework，或沿用旧配置 |
| MySQL | `MySql.Data.MySqlClient` | |
| PostgreSQL | `Npgsql` | |
| SQLite | `Microsoft.Data.Sqlite` / `System.Data.SQLite` | 两者都识别 |
| Oracle | `Oracle.ManagedDataAccess.Client` | |
| DB2 | `IBM.Data.DB2.Core` | NuGet 包版；**自动注册器不覆盖 DB2**，需自己 `DbProviderFactories.RegisterFactory`（net452 下由驱动安装写入 machine.config） |

前六项（SqlClient×2、MySql、Npgsql、Sqlite×2、Oracle）在应用启动时按需自动注册，
驱动 DLL 在依赖图里就能用；不在表里的 Provider 要自己注册后才能通过名字取到工厂。
DB2 的 invariant name 随驱动版本不同（NuGet 包是 `IBM.Data.DB2.Core`，
`.iSeries` 那套是另一条产品线的包名），以驱动文档为准。

数据库枚举 `DataDbType`：`Oracle=1, MySql=2, SqlServer=3, DB2=4, SQLite=5, PostgreSql=6`。

## 三分钟上手

**1. 建配置** — 输出目录下的 `db.config`：

```xml
<?xml version="1.0" encoding="utf-8"?>
<DataConfig Default="DefaultDb">
  <Connections>
    <Add Provider="Microsoft.Data.SqlClient"
         Key="DefaultDb"
         ConnStr="Server=127.0.0.1;Database=TestDb;User Id=sa;Password=***;" />
  </Connections>
</DataConfig>
```

只有 `Key` / `Provider` / `ConnStr` 三个必填，其余属性都有默认值。
完整配置项见[配置说明](./docs/CONFIGURATION.md)。

**2. 启动时初始化一次**：

```csharp
FastMap.InstanceMap();                                    // 加载数据源与 Map
FastMap.InstanceProperties(nameSpace: "YourApp.Models");   // 扫描实体特性
```

**3. 用起来**：

```csharp
var db = new FastDataClient("DefaultDb");

var users = db.Query<User>(u => u.IsActive)
              .Where(u => u.Age > 18)
              .OrderBy(u => u.CreateTime)        // 升序
              .Take(10)
              .ToList<User>();

db.Add(new User { UserName = "张三", Age = 30 });
db.Update(new User { UserName = "李四" }, u => u.Id == 1);
db.Delete<User>(u => u.Id == 1);
```

两个容易踩的点：

- `DataQuery<T>` 上的 `OrderBy` 是**升序**，降序用 `OrderByDescending`。带 `isDesc` 参数的那个重载属于非泛型的旧 `DataQuery`，两者默认值相反。
- `Take` 是扩展方法，调用后返回非泛型的 `DataQuery`——所以它必须放在链尾，之后不能再接 `Where`；`ToList` 也要显式写 `ToList<User>()`。

## 核心类

| 类 | 作用 |
|---|---|
| `FastDataClient` | 统一门面，绑定数据库 Key，**新代码首选** |
| `FastRead` / `FastWrite` | 不绑定 Key 的静态读写入口 |
| `FastReadDb` / `FastWriteDb` | 绑定 Key 的读写入口，省掉每次传 key |
| `FastMap` | XML Map 的加载与执行 |
| `FastDb` | 全局开关（SQL 日志等）与上下文管理 |
| `ShardingReadHelper` / `ShardingWriteHelper` | 分表读写 |
| `ConditionBuilder` | 对象化 WHERE 拼接，全参数化 |

## 常用写法

### CodeFirst 建表

```csharp
FastWrite.CodeFirst<User>(key: "DefaultDb", isDropExists: false);
await FastWrite.CodeFirstAsync<User>(key: "DefaultDb", isDropExists: false);
```

按实体生成建表语句，自动适配各数据库方言。

### 分页与计数

```csharp
var page = db.Query<User>(u => u.IsActive)
             .OrderBy(u => u.Id)
             .ToPage<User>(new PageModel { PageId = 1, PageSize = 10 });

var count = db.Query<User>(u => u.Age > 18).ToCount();
var top20 = db.Query<User>(u => u.IsActive).Take(20).ToList<User>();
```

`ToPage` 的返回值带 `TotalRecord` 和 `Sql`，取数为空时先看 `IsSuccess` 与 `Sql`
再判断是不是真的没数据。

### 条件动态拼接

面向配置驱动、规则化查询。值全部走参数，不存在拼接注入。

```csharp
var sql = new ConditionBuilder(config)
    .Equal<User>(u => u.Age, 18).And()
    .Contains<User>(u => u.UserName, "张").Or()
    .In<User>(u => u.Status, new object[] { 1, 2, 3 })
    .Build(out var parameters);
```

17 种操作符：`Equal` `NotEqual` `GreaterThan` `GreaterThanOrEqual` `LessThan`
`LessThanOrEqual` `Like` `NotLike` `Contains` `StartsWith` `EndsWith` `In`
`NotIn` `Between` `NotBetween` `IsNull` `IsNotNull`。
详见[条件拼接](./docs/CONDITION_BUILDER.md)。

### XML Map

```csharp
FastMap.InstanceMap(dbKey: "DefaultDb", mapFile: "SqlMap.config");

var users = db.MapQuery<User>("GetActiveUsers", new[]
{
    new SqlParameter("@IsActive", true),
    new SqlParameter("@MinAge", 18)
});
```

### 分表

```csharp
ShardingManager.Configure<UserLog>(new ShardingConfig
{
    BaseTableName = "UserLog",
    ShardingType = ShardingType.Time,
    TimeConfig = new TimeShardingConfig
    {
        TimeField = "CreateTime",
        Granularity = TimeGranularity.Month
    }
});

var recent = db.ShardQuery<Order>(
    o => o.CreateTime > DateTime.Now.AddMonths(-3),
    queryParams: new Dictionary<string, object> { { "CreateTime", DateTime.Now.AddMonths(-3) } });

db.ShardAdd(order);
db.ShardUpdate(new Order { OrderNo = order.OrderNo, Status = "paid" },
    o => o.OrderNo == order.OrderNo,
    o => new { o.Status });
```

### AOP 拦截

`IFastAop` 有五个回调，全部必须实现：

```csharp
public class LoggingAop : IFastAop
{
    public void Before(BeforeContext context)       => Console.WriteLine($"执行: {context.sql}");
    public void After(AfterContext context)         => Console.WriteLine($"完成: {context.sql}");
    public void Exception(ExceptionContext context) => Console.WriteLine($"异常: {context.ex.Message}");
    public void MapBefore(MapBeforeContext context) => { }
    public void MapAfter(MapAfterContext context)   => { }
}

FastMap.InstanceMap(aop: new LoggingAop());
```

上下文属性都是小写：`sql`、`param`、`dbType`、`result`、`tableName`，异常在 `ExceptionContext.ex`。

`Before`、`After`、`Exception` 拦普通 SQL，`MapBefore`、`MapAfter` 拦 XML Map。
回调里抛异常不会影响已提交的事务。

### 缓存

```csharp
RedisInfo.Set("user:1", user, hours: 300);   // hours 参数，默认 12 个月
var u = RedisInfo.Get<User>("user:1");
```

注意三个时间单位：`RedisInfo.Set` 的 `hours` 是**小时**、`[Cache(ExpireTime = n)]` 是**秒**，
而 `Set(key, model, double Minutes)` 那个重载是分钟。写错就是几个月的缓存或几秒的缓存。
两种缓存模式、键设计、降级写法见[缓存](./docs/CACHING.md)。

### 消息队列

数据库不可用时把写入转投 Redis，恢复后自动刷回：

```csharp
FastWrite.ConfigureQueue<Order>(new WriteBehindConfig
{
    QueueType = WriteBehindQueueType.ReliableQueue,
    Topic = "orders",
    EnableFallback = true,
    EnableAutoRecovery = true
});

var db = new FastDataClient("DefaultDb");
var result = db.WriteQueue().Add(order1).Add(order2).Execute();   // 返回 WriteBehindResult
```

`WriteQueue()` 是 `FastDataClient` 上的实例方法，没有静态版 `FastWrite.QueueBuilder()`。
仅 .NET 6+ 可用。四种场景与完整 API 见[消息队列](./docs/MESSAGE_QUEUE.md)。

## 实体特性

```csharp
[Table(Name = "Users")]
[Cache(IsEnable = true, ExpireTime = 300, Key = "user:{Id}", CacheType = "Redis")]
public class User
{
    [Column(IsIdentity = true), Primary]
    public long Id { get; set; }

    [Column(Length = 100, IsNull = false, Comments = "用户名")]
    public string UserName { get; set; }

    public int Age { get; set; }
    public bool IsActive { get; set; }
    public int Status { get; set; }
    public DateTime CreateTime { get; set; }
}
```

| 特性 | 可用参数 |
|---|---|
| `[Table(Name = "...")]` | `Name` 表名、`Comments` 备注、`DbTableNames` |
| `[Column(...)]` | `IsIdentity` `Length` `Precision` `Scale` `DataType` `IsNull` `Comments` `IsKey` |
| `[Primary]` | 无参，标记主键 |
| `[Cache(...)]` | 声明式缓存，见[缓存](./docs/CACHING.md) |

**`[Table]` 和 `[Column]` 都不能改列名。** 两者都没有承载列名的构造参数或属性，
框架取的是属性名本身（`GetModelColumns` 里直接用的 `p.Name`）。所以：

- `[Table("Users")]` 编译不过，必须写 `[Table(Name = "Users")]`
- `[Column("user_name")]` 编译不过，`UserName` 属性就对应 `UserName` 列

数据库里列名和属性名不一致时，只能靠 SQL Map 手写 SQL，或者把属性名改成与列名一致。

`IsIdentity` 只在 CodeFirst 建表时用来生成自增语法；`Primary` 决定主键，
两者可以标在同一属性上。

## 命名空间

| 命名空间 | 内容 |
|---|---|
| `FastData` | 顶层入口：`FastRead` `FastWrite` `FastMap` `FastDb` `FastDataClient` |
| `FastData.Base` | SQL 构建、表达式访问 |
| `FastData.Model` | `ConfigModel` `DataQuery` `WriteReturn` |
| `FastData.Context` | 数据库上下文 |
| `FastData.Config` | XML 配置加载 |
| `FastData.Adapter` | SQL 方言实现 |
| `FastData.Sharding` | 分表策略 |
| `FastData.Queue` | 消息队列 |
| `FastData.Repository` | Repository 模式 |
| `FastData.Aop` | AOP 接口 |
| `FastData.DbTypes` | `DataDbType` |
| `FastData.Extensions` | LINQ 扩展方法 |
| `FastData.Property` | `Primary` `Column` `Cache` 特性 |

## 依赖

- `FastData.Untility`（同仓项目引用）—— 通用工具、缓存、监控。`FastRedis` 命名空间的
  Redis 封装也在这个项目里，不是外部包；AOP 的 `IFastAop` 定义在 `FastData/Aop/`。
- `System.Configuration.ConfigurationManager` —— 读取 XML 配置。

> NuGet 上已发布的 3.2.4 声明依赖 `Fast.Redis` / `Fast.Untility` / `FastAop` 三个外部包，
> 那是旧结构打的包。当前源码已把这三者收进来，下一次发版的依赖清单会变。

## 文档

| 文档 | 内容 |
|---|---|
| [配置说明](./docs/CONFIGURATION.md) | 配置文件结构、全部属性、常见误区 |
| [条件拼接](./docs/CONDITION_BUILDER.md) | `ConditionBuilder` 完整操作符与扩展方式 |
| [缓存](./docs/CACHING.md) | 两种缓存、键设计、过期单位、降级 |
| [消息队列](./docs/MESSAGE_QUEUE.md) | 削峰、降级、多方推送、消息模型 |
| [连接池调优](./docs/POOL_TUNING.md) | 每个参数的实际行为与误判点 |
| [现代 ORM 特性](./docs/MODERN_ORM_FEATURES.md) | 变更跟踪、数据库迁移 |
| [ORM 最佳实践](./docs/ORM_BEST_PRACTICES.md) | 推荐用法、过时 API 迁移 |
| [Provider 自动注册](./docs/AUTO_PROVIDER_REGISTRATION.md) | .NET Core+ 下的驱动扫描 |
| [性能](./docs/PERFORMANCE.md) | 基准数据与调优方向 |
| [已知问题](./docs/KNOWN_ISSUES.md) | 当前未处理的问题与理由 |
| [架构演进历史](./docs/ARCHITECTURE_HISTORY.md) | 历史归档，非当前设计 |

## 许可证

MIT，见 [LICENSE](../LICENSE)。