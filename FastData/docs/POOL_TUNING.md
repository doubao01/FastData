# 连接池调优

先给结论：**绝大多数项目不需要动这里。** 写了 `<ConnectionPool />` 用默认值就行。

下面说清楚什么时候该调、调了会怎样。所有数值都对照 `SmartConnectionPool.cs` 的实际实现核实过，
不是照抄属性名猜的。

---

## 一、默认值怎么来的

不显式配 `MinPoolSize` / `MaxPoolSize` 时，启动时会按机器资源自动算：

```
MaxPoolSize = min(CPU 核心数 × 1.5, 可用内存 × 10% ÷ 2MB)
             再夹到 [10, 200]
MinPoolSize = max(2, CPU 核心数 × 1.5 ÷ 10)
```

例：

| 机器 | 自动算出的 MaxPoolSize |
|---|---|
| 2 核 / 2GB | 3 → 夹到 **10** |
| 4 核 / 8GB | 6 → 夹到 **10** |
| 8 核 / 16GB | 12 → **12** |
| 16 核 / 32GB | 24 → **24** |

`.NET Framework 4.5.2` 拿不到真实内存（`GC.GetGCMemoryInfo` 不可用），固定按 2GB 算。

---

## 二、单位不统一，这是最容易踩的坑

| 参数 | 单位 |
|---|---|
| `ConnectionTimeout` | **秒** |
| `ConnectionLifetime` | **分钟** |
| `HealthCheckInterval` | **秒** |
| `SmartAdjustmentInterval` | **秒** |
| `LeakDetectionThreshold` | **秒** |
| `ValidationCommandTimeout` | **秒** |
| `CircuitBreaker.CircuitOpenDurationSec` | **秒** |
| `RetryBaseDelayMs` | **毫秒** |

只有 `ConnectionLifetime` 是分钟，其余都是秒。设 `ConnectionLifetime="30"` 是 30 分钟，
设 `ConnectionTimeout="30"` 是 30 秒——差 60 倍。

---

## 三、真的可能需要调的（就三个）

### 1. `MaxPoolSize` —— 数据库连不上时报错，先看这个

这不是「想开多大就开多大」，它的上限是**数据库端**：

| 数据库 | 默认最大连接数 |
|---|---|
| MySQL | 151 |
| PostgreSQL | 100 |
| SQL Server | 32767（但受内存限制） |

配置超过数据库上限的话，并发高时不是「连接池满」，而是**数据库那边拒绝连接**，
报错信息是 `too many connections` 之类，跟这个配置项完全对不上号，很难查。

所以上限 200 是代码里硬夹的，超了会输出 Trace 警告并按 200 生效。

**什么时候调大**：并发请求数经常超过当前值，且数据库端还有余量。
**什么时候调小**：多个应用共用一个数据库实例，需要给彼此留额度。

### 2. `ConnectionTimeout` —— 数据库慢或网络抖动时

等待空闲连接的秒数。超时会抛 `ConnectionPoolExhaustedException`。

默认 30 秒。**调大**能扛住瞬时峰值，但意味着慢查询会把请求堆积得更久；
**调小**能快速失败，但要配合熔断或降级。

### 3. `CircuitBreaker.Enabled` —— 默认开启，别关

连续失败 5 次后熔断 30 秒，期间直接拒绝请求。这是保护机制：
数据库挂了的时候，没有熔断的话每个请求都要等满 `ConnectionTimeout` 才失败，
线程池会在几十秒内被打光。

**没有理由关掉它。** 旧版示例配置里写过 `CircuitBreakerEnabled="false"`，已移除。

---

## 四、通常不用动的（以及为什么）

### `LeakDetectionThreshold`（默认 300 秒）

**它只写日志，不回收连接。**

超过这个时间还在「使用中」的连接会被记一条日志并累加 `LeakedConnections` 指标，
但连接本身要等调用方 `Dispose` 才会归还。所以：

- 看到这条日志 → 说明**你的代码里有地方漏了 `using`**
- 调这个值不会解决泄漏，只是改变告警的时间点

排查方式：看日志里的 `connection.Id`，再对照代码中没走 `using` 的 `new DataContext(...)`。

### `EnableSmartAdjustment` 系列（`LoadThreshold` / `ShrinkThreshold` / `SmartAdjustmentInterval` / `MaxExpandCount` / `MaxShrinkCount`）

**这里有个认知差：并发高峰时的扩容不靠它。**

真实链路是：

```
GetConnection()
  → 信号量等待（许可数 = MaxPoolSize）
  → 从空闲队列取
  → 取不到 → 直接 CreateNewConnection()   ← 立即建，不等 SmartAdjustment
```

`SmartAdjustment` 每 `SmartAdjustmentInterval` 秒跑一次，做的事是**提前预热**：
负载超过 `LoadThreshold` 就预建几个连接放进空闲队列，让下一次请求少一次建连延迟。

所以：

- 高峰期该建的连接，`GetConnection` 自己就建了，**不受 `LoadThreshold` 控制**
- 调 `LoadThreshold` 影响的是「起高峰前预热得多积极」，不是「能不能扩容」
- 每次最多扩 `MaxExpandCount`（默认 10）个，缩 `MaxShrinkCount`（默认 5）个

### `ErrorShrinkThreshold` / `ErrorShrinkPercentage`

近期错误数达到阈值时主动缩容并**清零错误计数**。

注意它的判定在扩容之前（`if` / `else if`）——所以错误多的时候会优先缩容，
不会同时扩容。

### `EnableRedisCheck` / `RedisConnectionString`

默认关闭。开启后，只有 Redis 可达时才允许扩容。

这个设计的前提是「缓存扛不住时不要再给数据库加压」。但如果你没配 Redis，
或者 Redis 挂掉时希望系统降级继续跑，就别开——开了之后 Redis 不可用会**阻止扩容**。

### `ValidationCommandTimeout`（默认 5 秒）

取连接时验证有效性（`SELECT 1`）的命令超时。默认值够用。
只在网络极差、且验证本身经常超时时才需要调大。

### `MaxRetries` / `RetryBaseDelayMs`

建连失败时的重试：第 n 次重试等 `RetryBaseDelayMs × 2^(n-1)` 毫秒。

默认 3 次、50ms 起——总共约 350ms。**别为了「更稳」把重试次数调大**：
数据库真的不可达时，重试只会让请求等更久，而熔断器已经会拦住后续请求。

---

## 五、配置示例

### 最小（推荐）

```xml
<ConnectionPool />
```

全默认值。

### 多应用共用数据库，需要限流

```xml
<ConnectionPool MinPoolSize="5" MaxPoolSize="30" ConnectionTimeout="10" />
```

### 高并发、数据库有充足余量

```xml
<ConnectionPool MinPoolSize="20" MaxPoolSize="100" ConnectionTimeout="30" />
```

### 出问题时的临时排查配置

```xml
<ConnectionPool MinPoolSize="2" MaxPoolSize="20" LeakDetectionThreshold="60" />
```

怀疑连接泄漏时把阈值调小，能更快看到日志。

---

## 六、怎么判断「当前配置够不够」

看 `ConnectionPoolMetrics`（`SmartConnectionPool.GetMetrics()`）。

**注意 `WaitingRequests` 这个名字有误导**：它的计算是
`MaxPoolSize - _semaphore.CurrentCount`，即「已占用的许可数」，
不是「正在排队等待的请求数」。正常有请求在处理时它就会大于 0，
所以**不能用它判断池是否够用**。改名属于破坏性变更，这里如实说明，按下面的判据看。

| 指标 | 实际含义 | 怎么用 |
|---|---|---|
| `TotalConnections` | 当前连接总数 | 长期贴着 `MaxPoolSize` → 池可能不够 |
| `ActiveConnections` | 使用中的连接数 | 同上 |
| `IdleConnections` | 空闲连接数 | **长期接近 0 且 Active 贴满上限 → 池确实不够** |
| `AverageWaitTimeMs` | 成功取连接的平均等待毫秒 | 接近 `ConnectionTimeout × 1000` → 快耗尽了 |
| `LeakedConnections` | 检测到的疑似泄漏 | **只要大于 0 就是代码有 bug，别调参** |
| `FailedRequests` | 取连接失败次数 | 持续增长 → 池不够 或 数据库有问题 |

排查顺序：

1. 先看 `LeakedConnections` —— 大于 0 说明有地方漏了 `using`，是代码问题不是配置问题
2. 再看 `IdleConnections` 是否长期为 0 且 `ActiveConnections` 贴满 `MaxPoolSize` —— 这才是容量不足
3. 最后看熔断器状态 —— 数据库本身有问题时，调池大小没用