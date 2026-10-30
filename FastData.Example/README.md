# FastData.Example

FastData ORM 的可运行示例集。两种口味：**业务场景**（完整的注册/下单/报表这类真实流程）和
**API 参考**（单个能力的用法）。

## 运行方式

```bash
# 交互式菜单
dotnet run --project FastData.Example --framework net10.0

# 直接选某个示例（管道输入编号）
echo "1" | dotnet run --project FastData.Example --framework net10.0
```

目标框架 `net8.0; net10.0`。两个 TFM 都定义了 `NET6_0_OR_GREATER`，所以配置加载、消息队列这些
`.NET 6+` 分支在两边都会编译进来——跑哪个版本行为一致。

## 菜单

编号与 `Program.cs` 的 `switch` 一一对应，改代码时两边要同步：

| 分组 | 编号 |
|------|------|
| 业务场景示例 | `1` ~ `14` |
| API 参考示例 | `20` ~ `29` |
| 运行全部业务场景 | `99` |
| 退出 | `0` |

## 业务场景示例（1 ~ 14）

| 编号 | 文件 | 内容 |
|------|------|------|
| 1 | `UserManagementExample.cs` | 用户管理：注册、登录、缓存、分页、软删除 |
| 2 | `OrderBusinessExample.cs` | 订单全生命周期：下单、支付、发货、取消、事务 |
| 3 | `CacheStrategyExample.cs` | 缓存策略：穿透、雪崩、预热、分布式锁 |
| 4 | `MessageQueueExample.cs` | 消息队列：IoT 削峰、订单通知、日志异步 |
| 5 | `MultiDbExample.cs` | 多数据库：读写分离、跨库查询、同一 Model 的多库表名映射 |
| 6 | `ShardingExample.cs` | 分表：按时间、按哈希、跨片查询 |
| 7 | `RepositoryExample.cs` | 仓储模式：DI 集成、泛型仓储 |
| 8 | `ReportExample.cs` | 报表统计：Join、GroupBy、JSON、DataTable 导出 |
| 9 | `DynamicQueryExample.cs` | 动态查询：条件构建器、Any/All/First/Single |
| 10 | `DataValidationExample.cs` | 数据校验：空值安全、字段验证、异常降级 |
| 11 | `ApiAuthenticationExample.cs` | API 认证：Token、JWT、RSA、AES、统一返回格式 |
| 12 | `ReliableQueueExample.cs` | 可信队列：连接池满/超时时自动降级到队列，批量写入 |
| 13 | `FastDataClientQueueExample.cs` | `FastDataClient` 消息队列：配置、链式写入（自动降级）、链式查询（异步推送）、批量操作 |
| 14 | `HighFrequencyCacheExample.cs` | 高频数据缓存：Web 缓存、Redis、数据一致性 |

## API 参考示例（20 ~ 29）

| 编号 | 文件 | 内容 |
|------|------|------|
| 20 | `BasicCrudExample.cs` | 基本 CRUD，用 SQLite 可直接跑 |
| 21 | `LambdaQueryExample.cs` | Lambda / 链式查询，用 SQLite 可直接跑 |
| 22 | `RawSqlExample.cs` | 原生 SQL：复杂查询、存储过程、批量 |
| 23 | `MapSqlExample.cs` | XML Map SQL（类 MyBatis）：复杂 SQL、动态条件 |
| 24 | `TransactionExample.cs` | 事务：基本事务、异常回滚、批量写入 |
| 25 | `PaginationExample.cs` | 分页 API |
| 26 | `AopExample.cs` | AOP 拦截器：SQL 日志、性能监控、异常处理、数据审计 |
| 27 | `CodeFirstExample.cs` | Code First 建表 |
| 28 | `BulkOperationsExample.cs` | 批量操作：BulkInsert、UpdateList、批量删除 |
| 29 | `RedisAdvancedExample.cs` | Redis 高级用法：缓存穿透防护、预热 |

## 存在但未接入菜单的示例

以下 5 个文件实现了完整示例，但 `Program.cs` 里没有对应 `case`，交互菜单进不去，
只能直接改 `Program.cs` 或从别处调用：

| 文件 | 内容 | 额外依赖 |
|------|------|----------|
| `DataSyncExample.cs` | `IDataSyncService` 数据同步 | 同步任务配置 |
| `EncryptionExample.cs` | 连接密码加密，避免明文落盘 | 加密密钥 |
| `FastDataClientExample.cs` | `FastDataClient` 统一入口：查询写入、Redis 缓存 | Redis |
| `RedisCacheExample.cs` | 主动缓存（手动管理）与自动缓存（`CacheAttribute`）两种模式 | Redis |
| `ShardingFullExample.cs` | SQL Server 分表：万级日志、哈希分表、查询频率分表 | SQL Server |

代码里没写它们为什么没进菜单，上面只列事实（它们需要什么才能跑）。前四个和菜单里已有的
示例功能有重叠，估计是新增示例时漏了接线；`ShardingFullExample` 跑一次会写不少数据，
不适合放进「全跑一遍」的 `99`。

想知道这些示例的样子，直接读文件——它们各自都有 `Run()` 入口。

## 关键 API 速查

### Lambda / 链式查询

```csharp
var users = FastRead.Query<User>(u => u.IsActive)
    .Where<User>(u => u.Age > 18)
    .OrderBy<User>(u => u.Name)
    .Take(100)
    .ToList();
```

### XML Map SQL

```csharp
var users = FastMap.Query<List<User>>("GetActiveUsers", new[]
{
    new SqlParameter("@IsActive", true)
});
```

### 分页

```csharp
var page = FastRead.Query<User>(u => u.IsActive)
    .OrderBy<User>(u => u.Id)
    .ToPage(new PageModel { PageIndex = 1, PageSize = 10 });
```

### Code First

```csharp
var result = FastWrite.CodeFirst<User>("DefaultDb", isDropExists: false);
```

### 批量插入

```csharp
var result = FastWrite.AddList(users);
```

### 写入队列（链式）

```csharp
// 链式 API 支持显式指定 Redis；消费按原数据库 Key 回写
var queued = FastWrite.QueueBuilder("MySql")
    .WithRedis("127.0.0.1:6379", redisDb: 7)
    .WithQueue(new WriteBehindConfig
    {
        QueueType = WriteBehindQueueType.ReliableQueue,
        Topic = "users:write"
    })
    .Add(user)
    .Execute();
```

### 分表读写

按条件自动路由到物理分表，跨片结果合并；`key` 省略时取 `FastDb.CurrentKey`。
注意实体必须先 `ShardingManager` 配置并启用，否则抛 `InvalidOperationException`。

```csharp
// 读：跨片查询与分页（predicate + queryParams 决定命中哪些分表）
var orders = ShardingReadHelper.Query<Order>(predicate, queryParams);
var page   = ShardingReadHelper.QueryPage<Order>(predicate, queryParams, pageIndex, pageSize);

// 写：Add 从实体字段推断目标分表；Update / Delete 用 predicate + queryParams 路由
ShardingWriteHelper.Add(order);
ShardingWriteHelper.AddList(orders);
ShardingWriteHelper.Update(order, predicate);
ShardingWriteHelper.Delete<Order>(predicate, queryParams);
```

`FastDataClient` 上还有对应的一层门面：`ShardQuery<T>` / `ShardQueryPage<T>` / `ShardAdd<T>` /
`ShardAddList<T>` / `ShardDelete<T>`，绑定在 client 自己的 Key 上。

## 配置

示例不走 `appsettings.json`，用的是 FastData 自己的 `db.config` 体系：

```
FastData.Example/
├── db.config              # 主配置，Active="dev" 决定加载哪一个
├── db.dev.config          # 开发环境：SqlServer / MySql / PostgreSql / Sqlite 四套连接
├── db.pro.config          # 生产环境占位模板
└── db.pro.config.example  # 同一份模板的副本（早期遗留，二者内容等价）
```

`db.config` 只有一个属性（外加一行 `section` 注册）：

```xml
<DataConfig Active="dev" />
```

存在 `db.{env}.config` 时优先加载对应环境配置，否则回落到 `db.config` 内联的连接列表。
环境名支持 `dev` / `development` / `pro` / `production` / `staging`。

**要跑示例，先填 `db.dev.config` 里至少一套真实连接串**。想切生产配置：把 `Active` 改成 `pro` 即可。

`db.pro.config` 和 `db.pro.config.example` 现在都是**占位模板**（`YOUR_SERVER` / `YOUR_USER` /
`YOUR_PWD`），仓库里没有真实凭据。你往里填真实密码后：这两个文件**都已被 git 跟踪**，
`.gitignore` 拦不住修改，提交前自己 `git status` 确认一下，或者干脆只在本地填 `db.pro.config`、
别碰 `.example`。

## 构建

```bash
# 构建两个目标框架
dotnet build FastData.Example

# 只构 net10.0
dotnet build FastData.Example --framework net10.0
```

## 依赖

- FastData
- FastData.Untility
- FastRedis

## 许可证

MIT License - see [LICENSE](../LICENSE) for details.