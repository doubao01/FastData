# FastData.Demo

FastData 的 ASP.NET Core Web API 示例程序：24 个控制器、166 个端点，把 ORM 的各个能力都跑成可点的
HTTP 接口 —— 增删改查、分页、仓储、缓存、分表、多库、事务、AOP、同步、并发压测。

**这个项目是给人手动点着看的，不是测试套件。** 端点的正确性由 `FastData.Tests` 保证，
这里的价值是「能看见实际 SQL、实际返回」。

## 目标框架

`net10.0`（`FastData.Demo.csproj` 只声明这一个 TFM）。

## 快速开始

```bash
# 1. 改连接串。默认读 db.dev.config（内嵌资源），里面的 pwd=CHANGE_ME 必须替换
#    路径：FastData.Demo/db.dev.config

# 2. 起服务
dotnet run --project FastData.Demo

# 3. 监听地址：项目没有 launchSettings.json，由 --urls 或 ASPNETCORE_URLS 指定，
#    不指定时用框架缺省的 http://localhost:5000。Swagger UI 只在 Development 环境注册。
```

启动时会做三件事，都写在 `Program.cs` 里：

1. 手工 `DbProviderFactories.RegisterFactory` 注册 SqlClient / MySql / SQLite / Npgsql 四个驱动
   —— 框架默认只认自己认识的驱动名，不注册会在第一次开连接时抛「无法找到提供程序」。
2. **阻塞 3 秒**再执行 CodeFirst 建表（`FastMap.InstanceTable`，对 SqlServer / MySql / PostgreSql
   各跑一次），给连接池留初始化时间。这 3 秒是硬等，不是探测，属示例程序图省事的写法。
3. 打开 SQL 日志（`FastDb.EnableSqlLog = true`），慢查询阈值 500ms。Console 里能看到每条实际 SQL。

Kestrel 限流：最大并发连接 100、KeepAlive 2 分钟、请求头超时 30 秒。

## 接口地图

路由按控制器里 `[Route]` 的实际写法列出（ASP.NET Core 路由匹配不区分大小写，
`/api/sharding/init` 和 `/api/Sharding/init` 等价）。

`GET /` 返回服务信息与当前环境，是最快的连通性自检。

| 控制器 | 路由前缀 | 端点 | 用途 |
|--------|----------|------|------|
| `UsersController` | `/api/users` | 10 | 基础 CRUD、分页、部门查询、动态搜索 |
| `OrdersController` | `/api/orders` | 5 | 订单 CRUD、按用户查、改状态 |
| `RepositoryController` | `/api/Repository` | 7 | `IFastRepository` 读写分离仓储 |
| `AsyncController` | `/api/Async` | 12 | 全异步 CRUD、并发读、并发写、异步事务 |
| `PaginationController` | `/api/Pagination` | 5 | `PaginationResult<T>` 分页、字典分页 |
| `TransactionController` | `/api/Transaction` | 3 | 批量订单、余额转账、批量用户（回滚语义） |
| `ShardingController` | `/api/Sharding` | 14 | 时间 / 哈希 / 列表 / 查询频率四种分表策略 |
| `MultiDbShardingController` | `/api/multi-sharding` | 6 | 多库下的分表初始化与写入 |
| `MultiDbUsersController` | `/api/MultiDbUsers` | 5 | 多库切换读写 + 表名映射信息 |
| `MySqlUsersController` | `/api/mysql-users` | 6 | MySQL 专属 CRUD |
| `PgUsersController` | `/api/pg-users` | 6 | PostgreSQL 专属 CRUD |
| `SqliteUsersController` | `/api/sqlite-users` | 5 | SQLite 专属 CRUD |
| `CacheController` | `/api/Cache` | 9 | 缓存穿透防护、降级、预热、批量、计数器、Key 设计 |
| `ConfigController` | `/api/config` | 10 | 环境、连接配置（脱敏）、Redis 摘要、诊断、缓存清理 |
| `DataSyncController` | `/api/DataSync` | 4 | 增量同步状态、同步用户 / 订单、条数 |
| `SyncController` | `/api/sync` | 1 | 一次性全表同步 |
| `ReportController` | `/api/Report` | 11 | GroupBy / Join / 聚合 / 投影 / 分页报表与导出 |
| `DataExportController` | `/api/DataExport` | 7 | `ToDics` / `ToDataTable` / `ToArray` / JSON |
| `DynamicQueryController` | `/api/DynamicQuery` | 11 | 动态 Where 构建器、Any / All / First / Single、In / Between / Like |
| `DataValidationController` | `/api/DataValidation` | 10 | 空值安全、字段验证、异常处理、`WriteReturn` 展示 |
| `AopController` | `/api/Aop` | 2 | AOP 拦截器（前后置、异常） |
| `FeatureDemoController` | `/api/FeatureDemo` | 2 | 特性开关式示例 |
| `ConcurrencyTestController` | `/api/ConcurrencyTest` | 14 | 并发读写、连接池压测、混合压测 |
| `BenchmarkController` | `/api/benchmark` | 1 | 批量插入基准 |

### 端点明细（按控制器）

**Users** `/api/users` — `GET /`、`GET /{id:int}`、`GET /active`、`GET /department/{department}`、
`GET /paged`、`GET /search`、`GET /health`、`POST /`、`PUT /{id}`、`DELETE /{id}`

**Orders** `/api/orders` — `GET /`、`GET /{id}`、`GET /user/{userId}`、`POST /`、`PUT /{id}/status`

**Repository** `/api/Repository` — `GET /users`、`GET /users/{id}`、`GET /users/page/{page}/{pageSize}`、
`GET /info`、`POST /users`、`PUT /users/{id}`、`DELETE /users/{id}`

**Async** `/api/Async` — `GET /users`、`GET /users/{id}`、`GET /users/paged`、`GET /users/count`、
`GET /users/first`、`GET /concurrent`、`POST /users`、`POST /users/batch`、`POST /concurrent-write`、
`POST /transaction`、`PUT /users/{id}`、`DELETE /users/{id}`

**Pagination** `/api/Pagination` — `GET /users`、`GET /users/async`、`GET /users/dictionary`、
`GET /users/department/{department}`、`POST /users/search`

**Transaction** `/api/Transaction` — `POST /batch-orders`、`POST /transfer`、`POST /batch-users`

**Sharding** `/api/Sharding` — `POST /init`、`POST /insert-data`、`POST /sync`、`GET /stats`、
`POST /time/configure`、`GET /time/query`、`POST /hash/configure`、`GET /hash/query`、
`POST /list/configure`、`GET /list/query`、`POST /frequency/configure`、
`POST /frequency/record`、`POST /frequency/simulate`、`GET /frequency/hot`

**MultiDbSharding** `/api/multi-sharding` — `POST /init`、`POST /time`、`POST /hash`、`POST /list`、
`POST /frequency`、`POST /sync`

**MultiDbUsers** `/api/MultiDbUsers` — `GET /`、`GET /{dbKey}`、`GET /orders/{dbKey}`、
`POST /`、`GET /mapping-info`

**MySqlUsers / PgUsers / SqliteUsers** — 三个结构相同：`GET /`、`GET /{id:int}`、`POST /`、
`PUT /{id:int}`、`DELETE /{id:int}`；MySQL 与 PostgreSQL 另有 `GET /department/{department}`

**Cache** `/api/Cache` — `GET /basic`、`GET /penetration-protection`、`GET /fallback`、
`GET /batch`、`GET /counter`、`GET /key-design`、`GET /expiration`、`GET /user-service/{userId}`、
`POST /warmup`

**Config** `/api/config` — `GET /environment`、`GET /debug`、`GET /connections`、
`GET /connections/{key}`、`GET /redis`、`GET /summary`、`GET /diagnose`、`GET /cache/stats`、
`DELETE /cache/{key}`、`DELETE /cache`

**DataSync** `/api/DataSync` — `GET /status`、`GET /count`、`POST /sync-users`、`POST /sync-orders`

**Sync** `/api/sync` — `POST /all`

**Report** `/api/Report` — `GET /user-stats`、`GET /order-stats`、`GET /monthly-trend`、
`GET /user-order-report`、`GET /projection`、`GET /aggregate`、`GET /groupby-json`、
`GET /paged-report`、`GET /export/json`、`GET /export/datatable`、`GET /export/dics`

**DataExport** `/api/DataExport` — `GET /dics`、`GET /datatable`、`GET /array`、`GET /projection`、
`GET /orders`、`GET /json`、`GET /stats`

**DynamicQuery** `/api/DynamicQuery` — `GET /search`、`GET /or-query`、`GET /any`、`GET /all`、
`GET /first`、`GET /single`、`GET /in`、`GET /between`、`GET /like`、`GET /complex`、`GET /count`

**DataValidation** `/api/DataValidation` — `GET /null-safety`、`GET /null-safety-batch`、
`GET /null-safety-collection`、`GET /safe-query`、`GET /write-return`、
`POST /validate-add`、`PUT /validate-update/{id}`、`POST /safe-write`、
`POST /safe-batch-write`、`POST /integrity-check`

**Aop** `/api/Aop` — `GET /test`、`GET /info`

**FeatureDemo** `/api/FeatureDemo` — `GET /test`、`GET /config`

**ConcurrencyTest** `/api/ConcurrencyTest` — `POST /reset`、`GET /stats`、`GET /query/single`、
`GET /query/batch`、`GET /query/paged`、`GET /query/aggregate`、`GET /query/multi-db`、
`GET /pool/stress`、`POST /write/insert`、`POST /write/bulk`、`POST /mixed/stress`、
`POST /transaction`、`PUT /write/update`、`DELETE /write/delete`

**Benchmark** `/api/benchmark` — `POST /batch-insert`

## 配置

配置不在 `appsettings.json` 里。`appsettings.json` 只剩一条 `ConnectionStrings:SqlServer`（其他演示用），
数据源全部由 **db.config 系列 XML** 决定：

```
FastData.Demo/db.config         Active="dev"  —— 只指向环境
FastData.Demo/db.dev.config     开发环境：CodeFirst、完整日志、小连接池
FastData.Demo/db.pro.config     生产环境：更严格连接池与日志级别
```

两个环境文件都是占位配置（`pwd=CHANGE_ME`），可以直接入库；真正的凭据只在本地替换或走环境变量。
`db.pro.config.example` 是给「不想动已有 pro 配置」的人用的副本，与前者内容同构。

三个文件都以 `EmbeddedResource` 编进程序集（逻辑名 `FastData.Demo.db.config` 等），
所以**改完配置必须重新 build**，改磁盘上的文件不会生效。切环境两种方式：

```bash
# 改 db.config 的 Active 属性，或
FASTDATA_ACTIVE=pro dotnet run --project FastData.Demo
```

`db.dev.config` 里的必填项只有 `Key` / `Provider` / `ConnStr`，其余走默认；
`pwd=CHANGE_ME` 是占位符，不替换则连接必然失败（这是刻意的：演示项目不再硬编码真实密码）。

`db.dev.config` 还声明了 CodeFirst 与缓存类型：`CacheType` 默认 `web`（进程内内存缓存，开箱即用）。
**没有 Redis 却把 `CacheType` 写成 `redis`，缓存会静默失效**——命中率归零但不报错，
这条坑写在配置文件注释里。

## 缓存的实际实现

`CacheController` 用的是 `ICacheService`，注册的是 `InMemoryCacheService`
（`ConcurrentDictionary` + TTL），不是 Redis 客户端。所以「缓存演示」在没有 Redis 的机器上也能跑；
Redis 摘要只在 `/api/config/redis` 里作为配置读取展示。
需要真正的分布式缓存，走 FastData 自身配置里的 `CacheType="redis"`。

## 依赖

项目引用：`FastData`、`FastData.Untility`。

NuGet（版本统一在 `Directory.Packages.props`）：`Swashbuckle.AspNetCore`、`Microsoft.Data.SqlClient`、
`MySql.Data`、`System.Data.SQLite.Core`、`Npgsql`。

## 没有的东西

- **没有消息队列端点**。可靠队列 / Stream 的用法在 `FastData.Example`（控制台）和
  `FastData.Tests/MessageQueueTests.cs` 里，Demo 里一个 `/api/mq/*` 都不存在。
- **没有停机即恢复的种子数据**。所有表靠启动时 CodeFirst 建壳，数据要自己调接口造。
- **多库演示需要外部实例**：`MySqlUsers` / `PgUsers` / `MultiDb*` 打到 MySQL / PostgreSQL，
  没起就用不了；SQLite 那个是文件库，开箱可用。
- `ConcurrencyTest` 与 `Benchmark` 会真压库，别对着共享实例点。

## 许可证

MIT License - see [LICENSE](../LICENSE) for details.