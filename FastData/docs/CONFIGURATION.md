# 配置说明

配置文件默认是 `db.config`（多环境时另有 `db.dev.config` / `db.pro.config`），
放在程序输出目录下。库从 `AppDomain.BaseDirectory` 读取。

## 最小可用配置

只有三样必填：

```xml
<DataConfig Default="SqlServer">
  <Connections>
    <Add Key="SqlServer"
         Provider="Microsoft.Data.SqlClient"
         ConnStr="server=127.0.0.1;database=mydb;uid=sa;pwd=***" />
  </Connections>
</DataConfig>
```

| 属性 | 说明 |
|---|---|
| `Key` | 连接标识，代码里用 `FastWrite.Add(entity, key: "SqlServer")` 引用 |
| `Provider` | 数据库驱动，见下表 |
| `ConnStr` | 连接字符串，格式由驱动决定 |

### Provider 取值

| 数据库 | Provider |
|---|---|
| SQL Server（.NET 6+） | `Microsoft.Data.SqlClient` |
| SQL Server（.NET Framework） | `System.Data.SqlClient` |
| MySQL | `MySql.Data.MySqlClient` |
| PostgreSQL | `Npgsql` |
| SQLite | `Microsoft.Data.Sqlite` |
| Oracle | `Oracle.ManagedDataAccess.Client` |
| DB2 | `IBM.Data.DB2.iSeries` |

`DataConfig` 已同时识别 `System.Data.SqlClient` 与 `Microsoft.Data.SqlClient`，
迁移时不必改代码。

## 多环境

`db.config` 只写一行指定当前环境：

```xml
<DataConfig Active="dev" />
```

然后 `db.dev.config` / `db.pro.config` 里写各自的连接。
也可以用环境变量 `FASTDATA_ACTIVE` 覆盖，优先级高于文件。

## 连接节点常用属性

必填之外，这几个按需加。**不写就用默认值**。

| 属性 | 默认 | 说明 |
|---|---|---|
| `IsDefault` | false | 同环境多个连接时，用哪个作默认 |
| `IsOutError` | true | 是否把错误写日志 |
| `SqlErrorType` | 空 | 填 `db` 时错误写数据库表，否则写文件 |
| `CacheType` | `web` | `web` = 进程内内存缓存，`redis` = Redis |
| `DesignModel` | `DbFirst` | `CodeFirst` 时按实体建表 |
| `IsEncrypt` | false | `ConnStr` 是否经过加密（需先配置加密密钥） |

### 关于 CacheType

**没配好 Redis 时不要写 `redis`。** 配了 `redis` 但 Redis 不可达，
缓存会静默失效——系统看起来正常，但命中率归零、每个请求都打到数据库。

排查方式：

```csharp
FastRedis.RedisInfo.IsAvailable   // false 表示缓存已降级
FastRedis.RedisInfo.LastError     // 失败原因
```

## Redis 节点

只需要地址，其余有默认值：

```xml
<RedisConfig WriteServerList="127.0.0.1:6379" ReadServerList="127.0.0.1:6379" />
```

| 属性 | 默认 | 说明 |
|---|---|---|
| `WriteServerList` | — | 写服务器，`ip:port`，多个用逗号分隔 |
| `ReadServerList` | — | 读服务器，不填则与写相同 |
| `Password` | 空 | 无密码可省略 |
| `Db` | 0 | 数据库索引 |
| `ConnectTimeout` | 5000 | 连接超时（毫秒） |
| `MaxWritePoolSize` | 60 | 写连接池大小 |
| `MaxReadPoolSize` | 60 | 读连接池大小 |
| `AutoStart` | true | 是否自动启动连接池 |

## 连接池节点

写了 `<ConnectionPool />` 就启用，属性全用默认值。不写这个节点则不启用连接池。

```xml
<ConnectionPool />
```

按需调整这两项就够，其余 20 多个参数保持默认：

| 属性 | 默认 | 说明 |
|---|---|---|
| `MinPoolSize` | 按 CPU 核心数计算 | 常驻连接数 |
| `MaxPoolSize` | 按 CPU/内存计算，上限 200 | 最大连接数 |

`MaxPoolSize` 超过 200 会被夹到 200 并输出警告——设再大也没用，
因为数据库端的连接数上限通常更小（MySQL 默认 151）。

**要调其它参数之前先看 [连接池调优](./POOL_TUNING.md)。**
那里面写清了每个参数的实际行为，包括两个容易误判的地方：
真正应急扩容发生在取连接时（不受 `LoadThreshold` 控制），
以及 `LeakDetectionThreshold` 只写日志、不回收连接。

## IM 平台节点（可选）

用于把异常通知到 QQ / 企业微信等，**不写则完全关闭**。

```xml
<IMPlatform>
  <QQBot IsEnabled="true" BotId="123456" BotToken="***" ApiUrl="http://127.0.0.1:5700" />
  <ExceptionNotify IsEnabled="true" MinLevel="Error" />
</IMPlatform>
```

不写 `<IMPlatform>` 节点时，`FastIM` 的相关功能全部静默关闭，不影响其它功能。

## 常见误区

**「不写属性 = 用默认值」只对属性成立，对节点不是。**
`<ConnectionPool />` 写了但一个属性都不填 → 启用连接池 + 全部默认。
完全不写 `<ConnectionPool>` → 不启用连接池。