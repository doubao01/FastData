# 已知问题与修复状态

本文档汇总历次代码审计发现的问题及当前处置状态，避免重复分析。
最后更新：2026-10-28。

状态说明：**已修** = 代码已改并有测试或 CI 验证；**不修** = 评估后认为改动收益低于风险，附理由；**开放** = 已建 issue 待处理。

---

## 已修

### 假成功（返回成功但实际未生效）

这是本项目历史上最主要的一类缺陷，共同特征是「调用方拿到成功信号，但数据没写进去 / 读到的是空」。

| 位置 | 问题 |
|---|---|
| `DataContext.Add<T>` | `if (!insert.IsSuccess) { }` 空 if 块，随后返回默认结果 |
| `AddList`（PG/SQLite，同步+异步 4 段） | 逐条 `ExecuteNonQuery` 丢弃返回值，最后无条件 `IsSuccess = true` |
| `BaseExecute.ToBool` / `ToBoolAsync` | 用 `ExecuteNonQuery() > 0` 判定，NOCOUNT/TVP/ArrayBind 返回 -1 被误判失败 → 调用方重试 → 重复插入 |
| `BulkUpdate` | 逐行更新丢弃受影响行数 |
| `ApiClient` | HTTP 200 + 反序列化失败仍 `IsSuccess=true`、`Data=null` |
| 备份链路 `GetTables` / `GetTableSchema` / `GetTableData` | 空 catch 吞掉读库异常 → 写出「空但成功」的备份文件 |
| 恢复链路三处（MySQL/PG/SQLServer） | 每条语句 `catch { }` 后无条件 `Success = true` |
| 分页查询 | 异常被 catch 吞掉，返回 `TotalRecord=0` 的空壳，与「无匹配」无法区分 |
| `GetJson` | 重复绑定 WHERE 参数导致查询失败，异常被吞后返回 null |

### SQL 生成错误

| 位置 | 问题 |
|---|---|
| `BuildBaseSelectQuery` | `LIMIT` 拼在 `ORDER BY` 之前（MySQL/PG/SQLite 语法错误 → 返回 0 行） |
| `BuildBaseSelectQuery` | 字段列表为空时拼出 `select top 1  from ...`（空列清单语法错误） |
| `PaginationSqlBuilder`（SQL Server） | `row_number() over(order by u.Id)` 作用在派生表上，原表别名不可见 → 绑定失败 |
| `BaseModel.BuildSetClause` | 把自增列写进 SET 子句 → `Cannot update identity column` |
| `BaseModel`（SET / WHERE / INSERT） | 参数名缺 `config.Flag` 前缀 → `Specified parameter name 'X' is not valid` |

### 语义缺陷

| 位置 | 问题 |
|---|---|
| `GetList` 系列 4 处 | `Take == 1` 时只填 `Item` 不填 `List` → `Take(1).ToList()` 返回空 |
| `DataQuery.Take` | 默认值 0 无法区分「未设置」与「显式取 0 条」→ 新增 `HasTake` 标志 |
| `Update`（模板对象） | 未赋值的 `DateTime.MinValue` 超出 SQL Server datetime 下限 → `NormalizeValue` 转 DBNull |
| `AopAfter` | 回调抛异常导致「事务已提交却报失败」，调用方重试即重复写入 → 回调异常隔离 |

### 安全与敏感信息

| 位置 | 问题 |
|---|---|
| `BaseSymmetric` | AES 密钥/固定 IV 硬编码在公开源码；`GenerateKey()` 生成后丢弃误导 review |
| `AesHelper` | 不传 iv 时随机 IV 不输出，`Decrypt` 必然失败 |
| `DataConfig.MaskConnStr` | 脱敏只遮蔽 pwd/password，漏掉 uid / AccountKey / token / SharedAccessKey |
| `MigrationExamples` | `Password=123` 编入产品程序集（改为读环境变量） |
| `FastData.Demo/*.config` | 明文数据库口令（改为 `CHANGE_ME` 占位符） |
| `.monkeycode/` | 内部工作笔记提交进公开仓库（已移除并入 gitignore） |

### 配置加载

| 位置 | 问题 |
|---|---|
| `DataConfig.TryLoadConfig`、`MapConfig`（3 处） | 用字符串拼接 `BaseDirectory + 文件名`。`AppDomain.BaseDirectory` 是否带结尾分隔符依环境而异，拼出 `...\net452db.config` 这种错误路径 → `File.Exists` 为 false → 静默返回 null → 表现为「配置里一个 Key 都没有」。改为 `Path.Combine`。**任何 BaseDirectory 不带尾斜杠的宿主都会中招，不只是 net452。** |

### 资源与并发

| 位置 | 问题 |
|---|---|
| `SmartConnectionPool.DestroyConnection` | `_totalCount` 递减在 try 内，`Close()` 抛异常时跳过 → 计数漂移、池假性满员 |
| `PooledConnection` 终结器 | 调用方漏 Dispose 时许可永不释放且无痕迹 |
| `LogAggregator.ProcessLogsAsync` | 自我递归续接形成不断增长的任务链 |
| `LogAggregator.StopAsync` | 无超时、无取消，可能永久挂起 |
| `DbLogTable.LogException` | 记录错误日志时自身再抛异常，把原始异常顶掉 |
| `BaseUrl.http` | 每次访问都往单例 `HttpClient` 加一次 keep-alive 头 |

### 测试与 CI

| 问题 |
|---|
| 集成测试被 `--filter` 永久排除，从未在 CI 运行过 |
| 23 个 `OrmCrudTests` 无库时走 `Console.WriteLine("SKIP"); return;`，xUnit 记为 Passed |
| `release.yml` 把 `NUGET_API_KEY` 写在 step env 却在同 step 的 `if` 里读 → 条件恒假，发布从未执行 |
| `Directory.Build.props` 的 `TreatWarningsAsWarnings` 是不存在的属性，一直被静默忽略 |
| README 项目矩阵与 csproj 实际 TFM 不符 |

---

## 不修（附理由）

**`DevTools` 下的 `ApiTester` / `AuditLogger` / `DatabaseComparer` / `QueryOptimizer`**
零引用、合计 2870 行。但 README 第 76-89 行公开宣传了这些工具，删除属于产品决策，
需同步改文档与版本说明，不属于代码清理。

**`DistributedLockManager` / `DistributedTransactionManager`**
用进程内字典冒充分布式能力，已标 `[Obsolete]` 并在类注释写明不是分布式的。
未删除是因为它们仍是公开 API。（`error: true` 不可用：CS0619 无法被 pragma 抑制，
类内部自引用会直接编译失败。）

**剩余约 45 处空 catch**
逐处核对后确认绝大多数属于合理降级：`Dispose` 路径（释放失败不应影响正确性）、
探测式 `Assembly.Load`（失败即试下一个）、可选探测（缓存/驱动/环境信息）。
无差别改成抛异常会把库打崩。危险的那几处（备份、恢复、分页、ApiClient）已单独修复。

**`FastMap.QueryPage` / `QueryPage<T>` 仍为伪异步**
底层 `DataContext.GetPageSql` 没有异步版本。真正异步化需要先实现异步分页 SQL 执行器，
属于新功能而非缺陷修复。已在代码注释中注明它不提供 I/O 异步。

**改用 `Microsoft.Data.SqlClient`**
`Microsoft.Data.SqlClient` 5.2.2 最低要求 net462，而核心库需支持 net452。
代码里的 `using` 已按 `#if NET6_0_OR_GREATER` 条件编译走新驱动；
`Provider.SqlServer` 常量值仍为 `"System.Data.SqlClient"`，改它有兼容风险。

**`StressTests` 的 0.3 / 0.5 成功率阈值**
形同虚设（70% 失败也算通过），但提高阈值会让无数据库环境全红，
需要连同用例归属一起调整。

---

## 开放

| Issue | 内容 |
|---|---|
| [#17](https://github.com/doubao01/FastData/issues/17) | `AsyncCrud_ByPredicate` 在 SQL Server 上间歇失败（AddAsy 后查不到刚插入的行）。run#24-27 通过、#33 失败、#35 重跑通过、#37/#38 失败、#41 通过。可疑点：连接池事务残留、`GetIdentityValueAsync` 改写 cmd 的时序。 |

---

## 验证方式

集成测试覆盖 MySQL / PostgreSQL / SQL Server 三种真实数据库，
CI 中 `integration-test` job 起 service container 并创建 `perf_users` 表。

**net452 测试**：`FastData.Tests.Net452` 在 Windows runner 上用 `xunit.console.exe` 运行，
覆盖不依赖数据库的子集（加密、脱敏、异常管理、DateTimeProvider、数据同步选项）。
数据库往返无法在 net452 上测（GitHub 的 Windows runner 不支持 service container）。

本地 `dotnet test` 在部分环境（PRoot/容器）无法启动 testhost，
需要以 CI 结果为准。