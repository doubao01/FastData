# FastData.Untility

FastData 生态的**通用工具包**。名字叫 Untility（历史上的拼写错误，包名沿用至今），
实际装的是三套命名空间：

| 命名空间 | 内容 |
|----------|------|
| `FastUntility.*` | 基础工具：日志、JSON/XML、Excel、HTTP、缓存、加密、日期、集合、分页结果 |
| `FastRedis.*` | Redis 门面（`RedisInfo`）、可靠队列 / Stream 生产消费 |
| `FastData.Tooling.*` | 数据库元数据读取、代码生成、数据同步、远程指令 |

所以 `using FastUntility.Base;` 和 `using FastData.Tooling.Sync;` 来自同一个程序集，不是两个包。

## 目标框架

`net452;net8.0;net10.0`。**net452 与现代目标不是同一套 API**，这是本库最容易踩的坑：

| 能力 | net452 | net8.0 / net10.0 |
|------|--------|------------------|
| Redis 客户端 | `NServiceKit.Redis` | `NewLife.Redis`（`FullRedis`） |
| `RedisInfo` 实现 | `RedisInfo.cs` | `RedisInfo.NewLife.cs` |
| Redis 仓储 | `Repository/RedisRepository.cs` | `Repository/RedisRepository.NewLife.cs` |
| 消息队列（可靠队列 / Stream） | **不存在**（整文件 `#if !NETFRAMEWORK`） | 可用 |
| `BaseSession` / `BaseCookie` | 有（System.Web） | **移除** |
| WCF / Web API 宿主、Windows 服务宿主 | 有 | **移除** |

`Messaging/` 下五个文件整体被 `#if !NETFRAMEWORK` 包住；`BaseSession`、`BaseCookie`、
`Host/*`、`WinService/*`、`RedisInfo.cs`、`RedisRepository.cs` 则在 csproj 里对非 net452 直接
`<Compile Remove>`。写跨框架代码时不要假设这些类型存在。

## 安装

```bash
dotnet add package FastData.Untility     # 注意包名，不是 FastUntility
```

包内自带本 README（`PackageReadmeFile`），`InternalsVisibleTo` 给了 `FastData.Tests`。

## 模块与入口类型

| 目录 | 真实类型 | 说明 |
|------|----------|------|
| `Base/` | `BaseLog`、`BaseJson`、`BaseXml`、`BaseMap`、`BaseDic`、`BaseRegular`、`BaseCode`、`BaseExcel`、`BaseUrl`、`BaseSymmetric`、`Compression`、`DateHelper`、`CollectionHelper`、`LogManager`、`FrameworkCompat` | 见下 |
| `Security/` | `AesHelper`、`RsaHelper`、`HmacHelper`、`ApiKeyHelper`、`JwtHelper`、`ServerMonitor` | 注意：**没有 `CryptoHelper` 这个类**，加密算法分散在四个 Helper 里，同处 `CryptoHelper.cs` 文件 |
| `Cache/` | `BaseCache`（MemoryCache 封装） | net452 另有 `BaseSession` / `BaseCookie` |
| `Messaging/` | `MessageQueueFactory`、`IMessageProducer` / `IMessageConsumer`、`MessageQueueOptions`、`MessageQueueType` | 仅现代目标 |
| `Repository/` | `IRedisRepository`、`RedisRepository` | 双实现，按目标框架切换 |
| `Monitor/` | `ExceptionManager`、`MessageSender` / `QQBotMessageSender`、`MonitorConfig`、`RemoteCommandManager` | QQ 机器人远程运维指令 |
| `Database/` | `IDatabaseMetadataReader`、`ProviderMetadataReader`、`MetadataReaderFactory`、`DatabaseTable` / `DatabaseColumn` / `DatabaseConnectionOptions` | 读表结构与列 |
| `CodeGeneration/` | `ModelCodeGenerator`、`EnhancedCodeGenerator`、`ApiClientGenerator`、`JsonToModelConverter`、`XmlMapSqlGenerator` | 生成实体 / 客户端 / SQL 映射 |
| `Sync/` | `SyncConfigManager`、`TimeRangeCalculator`、`SyncTaskConfig`、`TableSyncConfig`、`TablePrimaryKeyConfig`、`TableSyncStatus`、`SyncResult`、`SyncDataType`、`SyncLogLevel` | 增量同步（命名空间 `FastData.Tooling.Sync`） |
| `Page/` | `ApiResponse` / `ApiResponse<T>`、`Result` / `Result<T>`、`PageModel`、`PageResult`、`PaginationRequest`、`PaginationResult` | 统一响应与分页 |
| `Builder/` | `LambdaBuilder` | 动态拼表达式树 |
| `Abstractions/` | `IDataSyncService` | 同步服务契约 |
| `Attributes/` | `RemarkAttribute` | 字段备注 |

## 用法（签名均取自源码）

### 日志

```csharp
BaseLog.SaveLog("用户已创建", "user-service.log");        // 追加写文件
await BaseLog.SaveLogAsync("批量完成", "job.log");
```

没有 `LogHelper.Info/Error` 这类分级 API —— 分级日志走 `LogManager` 或各子模块自己的日志类型。

### JSON / XML

```csharp
string json = BaseJson.ObjectToJson(user);
var u = BaseJson.JsonToObject<AppUser>(json);
List<AppUser> list = BaseJson.JsonToList<AppUser>(json);
var rows = BaseJson.DataReaderToDictionaryList(reader);   // DataReader → 字典列表
```

底层是 Newtonsoft.Json；现代目标同时引了 `System.Text.Json` 供内部使用。

### HTTP

```csharp
string body = await BaseUrl.GetUrlAsync("https://api.example.com/users/1");
string created = BaseUrl.PostUrl("https://api.example.com/users", dic);
string soap = BaseUrl.PostSoap(url, "GetUser", param);
```

`GetUrl` / `PostUrl` / `PostContent` / `PutUrl` / `DeleteUrl` / `PostSoap` 各有同步与 `*Async` 两份，
内部走 RestSharp，`TryAddWithoutValidation` 式的请求头累积问题已修（见 `BaseUrlSingletonTests`）。

### 缓存（进程内）

```csharp
BaseCache.Set("user:1", user, Hours: 24);
var cached = BaseCache.Get<AppUser>("user:1");
BaseCache.Remove("user:1");
bool hit = BaseCache.Exists("user:1");
```

`BaseCache` 是 `MemoryCache` 封装，**不是分布式缓存**；需要 Redis 走 `RedisInfo`。

### Redis

```csharp
RedisInfo.Init("db.config", "FastData.Demo");   // 从 XML 配置加载连接
RedisInfo.Set("user:1", user, hours: 1);
var u = RedisInfo.Get<AppUser>("user:1");
bool ok = await RedisInfo.SetAsync("user:2", user, hours: 24);
```

`RedisInfo.Init` 读的是 `db.config` 系列 XML，与 FastData 主库共用同一份配置来源。

### 消息队列（仅 net8 / net10）

```csharp
var factory = new MessageQueueFactory(RedisInfo.Redis);   // 需要 NewLife 的 FullRedis

var producer = factory.CreateReliableProducer("fastdata");      // 削峰，单消费
await producer.PublishAsync("topic:sensor", payload);

var stream = factory.CreateStreamProducer("fastdata");
await stream.PublishAsync("topic:realtime", payload);
var consumer = factory.CreateStreamConsumer("group-a", "fastdata");
```

`MessageQueueType` 区分 Reliable 与 Stream；消费端接口是 `IMessageConsumer`，
批量写用 `PublishBatchAsync`。

### 加密

```csharp
string enc = AesHelper.Encrypt(plain, key);              // 不传 iv 时 IV 前置写入密文
string dec = AesHelper.Decrypt(enc, key);
string enc2 = AesHelper.EncryptWithIV(plain, key);       // 显式模式，密钥按字节派生

var (pub, pri) = RsaHelper.GenerateKeyPair(2048);
string sig = RsaHelper.Sign(data, pri);
bool ok = RsaHelper.Verify(data, sig, pub);

string mac = HmacHelper.HmacSha256(payload, secret);
string jwt = JwtHelper.GenerateToken(payload, secret);
```

AES 密钥按字节派生（中文密钥不会因 UTF-16 编码错位），非 ASCII 密钥的旧 bug 已修。
RSA 的默认摘要算法是 SHA256，可传 `hashAlgorithm` 改；HMAC 家族是
`HmacSha256` / `HmacSha384` / `HmacSha512` / `HmacMd5`，配套 `VerifyHmac` 做常量时间比对。

### Excel

```csharp
var model = BaseExcel.Init("用户导出", headerDict, titleDict);
BaseExcel.FillContent(rows, model, excludeColumns);
byte[] xlsx = BaseExcel.Result(model, titleDict);
```

基于 NPOI，走「表头定义 → 填内容 → 出字节流」三步，不直接接收实体列表。

### 日期 / 集合

```csharp
long ts = DateHelper.GetTimestamp();
DateTime start = DateHelper.GetMonthStart(DateTime.Now);
string ago = DateHelper.GetRelativeTime(DateTime.Now.AddMinutes(-5));   // "5 分钟前"

if (list.IsNullOrEmpty()) return;                 // 扩展方法，null 安全
var page = list.Page(pageIndex: 1, pageSize: 10);
var (data, total) = list.PageWithTotal(1, 10);
list.ForEachSafe((item, i) => Console.WriteLine($"{i}: {item}"));
var byCategory = list.GroupToDictionary(x => x.Category);
list.BatchExecute(100, batch => Save(batch));
```

### 统一响应

```csharp
return ApiResponse<AppUser>.Ok(user);
return ApiResponse.Fail("参数错误", code: 400);
return ApiResponse.NotFound();

Result r = Result.Ok("完成");
Result<AppUser> rf = Result<AppUser>.Fail(ex, errorCode: "E001");
```

### 远程运维指令

`RemoteCommandManager` 注册 12 条文本指令，通过 QQ 机器人（`QQBotMessageSender`）下发：
`help` / `status` / `memory` / `cpu` / `disk` / `process` / `dbstatus` / `dbclose` /
`dbrestart` / `gc` / `version` / `time`。所有指令都走 `IRemoteCommandHandler`，
`FastData.FastIM` 的门面方法会自动注册全部 12 条。

`dbrestart` 的语义是**按原参数重建连接池并预热**（依赖 `IConnectionPoolReopenProvider`）；
实现方不支持重建时如实返回「已关闭，按需重建」，不会把关闭说成重启。

## 依赖

net452 组：RestSharp 106.15.0、NPOI 2.5.6、Newtonsoft.Json 13.0.4、
Microsoft.AspNet.WebApi.* 5.2.9、NServiceKit.Redis 1.0.17、System.ValueTuple 4.5.0。

net8/net10 组：RestSharp 114.0.0、NPOI 2.8.0、Newtonsoft.Json 13.0.4、System.Text.Json 10.0.8、
Microsoft.Extensions.Caching.Memory 10.0.8、System.Configuration.ConfigurationManager 10.0.8、
NewLife.Redis 6.5.2026.601，以及钉版的 System.Security.Cryptography.Xml / Pkcs 9.0.22
（ConfigurationManager 传递引入的旧版本带高危公告）。

**本项目不引用任何数据库驱动**（SqlClient / MySql / Npgsql / SQLite 都不在其中）。
驱动注册是宿主的事，`FastData.Demo` 里就是在 `Program.cs` 手工 `DbProviderFactories.RegisterFactory`。

## 许可证

MIT License - see [LICENSE](../LICENSE) for details.