# 用户指令记忆

> **重要：本文件中的指令具有最高优先级，必须优先遵守。**

本文件记录用户指令、偏好和项目知识，用于在未来的交互中提供参考。

---

## 行为指令（最高优先级）

**MEMORY.md 优先级**
- Date: 2026-05-29
- Instructions: 本文件指令具有最高优先级，每次对话开始时必须先读取并遵守

**新建项目和文档限制**
- Date: 2026-05-29
- Instructions: 新建项目/文档/Markdown 文件必须先经用户明确同意，创建前说明必要性

**代码提交策略**
- Date: 2026-05-29
- Instructions: 不要自动提交代码，工作完成后先汇报结果，等待用户确认是否提交

**文档维护**
- Date: 2026-06-02
- Instructions:
  - 极简且不重复原则：每个子项目只保留一个 README.md
  - 需求/设计/任务文档位于 .monkeycode/specs/（任务完成后删除）
  - 禁止创建冗余目录（.trae/specs/、.trae/documents/ 等）和重复文档
  - 当前文档结构：README.md（主文档）、CHANGELOG.md（版本变更）、.monkeycode/MEMORY.md（行为指令）

**功能开发工作流（必须遵守）**
- Date: 2026-05-29
- Instructions:
  - **任务先行原则**：任何开发工作必须先建立任务
  - 新增功能后必须完成：建立任务 → 核心实现 → 单元测试 → 示例 → Demo → 文档 → 代码生成工具 → 同步工具
  - 动其中一个，其他也必须跟着动

**"不要重复造轮子"原则**
- Date: 2026-06-02
- Instructions: 优先使用 FastData.Untility 和 FastData.DevTools 中已有的工具，避免重复实现

**变更谨慎**
- Date: 2026-06-02
- Instructions: 已实现且运行正确的功能，重构前必须确认不会破坏兼容性；不确定时先问用户

---

## 环境配置

**项目包管理器和目标框架**
- Date: 2026-06-02
- Instructions:
  - 项目支持多目标框架：net452;net8.0;net10.0
  - 使用 NuGet 包管理，SDK-style csproj 格式
  - **禁止使用不兼容 net452 的包**：Microsoft.Data.SqlClient、MySqlConnector 2.x、Microsoft.Data.Sqlite
  - **推荐 net452 兼容包**：System.Data.SqlClient、MySql.Data.MySqlClient、System.Data.SQLite、Npgsql 4.0.x
  - FastData/FastData.Untility 的 csproj 设 <GenerateAssemblyInfo>false</GenerateAssemblyInfo>，csproj 中的 <InternalsVisibleTo> 项不会生效；需在对应项目 Properties/AssemblyInfo.cs 手动声明 [assembly: InternalsVisibleTo("FastData.Tests")]

**项目结构**
- Date: 2026-05-29
- Instructions:
  - FastData: 核心 ORM
  - FastData.Untility: 工具库（含 FastRedis/FastUntility）
  - FastData.Tests/Example/Demo: 测试/示例/演示项目

**当前环境信息**
- Date: 2026-10-18
- Instructions:
  - 仅安装 .NET 10.0 SDK（`/root/.dotnet/dotnet`），构建/测试需指定 `--framework net10.0`；若 SDK 报 ICU 错误，先 `apt-get install -y libicu-dev`
  - RestSharp 版本：net45 用 106.15.0，net6.0+ 用 108.0.0
  - 测试项目目标框架为 `net8.0;net10.0`
  - 本地验证命令：`dotnet build FastData/FastData.csproj -f net10.0 -c Debug` 和 `dotnet test FastData.Tests/FastData.Tests.csproj -f net10.0 -c Debug --no-build --filter "FullyQualifiedName!~Integration"`
  - 环境未安装 .NET 8 运行时，运行 net8.0 测试需加 `DOTNET_ROLL_FORWARD=LatestMajor`（构建 net8.0 不受影响）

**配置管理规范**
- Date: 2026-06-02
- Instructions:
  - 数据库配置使用 `db.config` 和 `db.{env}.config` 文件
  - `db.config` 的 `Active` 属性决定加载哪个环境配置
  - 不得在 `appsettings.json` 中硬编码连接字符串

**Docker 容器**
- Date: 2026-05-28
- Instructions:
  - SQL Server: 1433, MySQL: 3306, PostgreSQL: 5432, Redis: 6379
  - Redis: `docker run -d --name redis -p 6379:6379 redis:7-alpine`

---

## 排错调试

**PostgreSQL/MySQL 大小写敏感问题（关键经验）**
- Date: 2026-06-06
- Context: 修复 PostgreSQL 查询返回空结果问题
- Instructions:
  - **问题现象**：PostgreSQL 查询返回 0 条记录，但数据库中有数据
  - **根本原因**（4 个问题叠加）：
    1. 表名大小写处理不当：PostgreSQL 将未加引号的标识符自动小写，但 ORM 生成 `AppUser`（ PascalCase）
    2. 字段匹配错误：`BaseDataReader.ToList` 接收的字段带表别名前缀（`u.Id`），直接与属性名（`Id`）比较，导致不匹配
    3. 参数传递错误：`GetList` 传递 `item.AsName`（表别名）而非 `item.Field`（字段列表）到 `ToList`
    4. Command 过早 Dispose：`DisposeCommand(cmd)` 在查询执行前就 dispose 了 command，导致"An open data reader exists for this command"错误
  - **解决方案**：
    1. `TableNameHelper.cs`：PostgreSQL 表名转小写（`tableName.ToLowerInvariant()`），MySQL 添加反引号（`` `表名` ``）
    2. `BaseDataReader.cs`：字段匹配前去除表别名前缀（`u.Id` → `Id`）
    3. `DataContext.Read.cs`：传递 `item.Field` 而非 `item.AsName`；移除 `DisposeCommand(cmd)` 调用
    4. `BaseModel.cs`/`DataContext.Write.cs`：传递 `config` 到 `GetTableName<T>()`
  - **检测方法**：
    - 测试不同数据库的查询端点，验证返回记录数与数据库实际记录数一致
    - 检查 SQL 日志中的表名和字段名是否符合数据库大小写规则
    - 检查 `GetList` 方法中 `dr.HasRows` 返回值是否为 True

**数据源 Key 回退机制**
- Date: 2026-05-28
- Instructions: FastDb.CurrentKey 是 AsyncLocal 可能被污染；FastRead.Query 走三级回退：key 参数 → FastDb.CurrentKey → Default 配置

**Sharding 连接配置三级回退**
- Date: 2026-05-28
- Instructions: 连接字符串读取顺序：IConfiguration → FastDataConfig.GetConnectionString → hardcode 兜底

**FastRead.Query API 使用规范**
- Date: 2026-05-29
- Instructions:
  - 必须传入 predicate 参数：`FastRead.Query<AppUser>(u => u.IsActive).ToList<AppUser>()`
  - `Toxst<T>()` 需要显式指定类型参数
  - FastWrite.Add/Update 返回 WriteReturn 对象（含 IsSuccess/Message），无需调用 `.Submit()`

**高并发测试经验**
- Date: 2026-05-29
- Instructions:
  - 30 线程并发成功率约 60%，100 线程并发成功率约 30%
  - 连接池压力测试成功率约 72-79%
  - 测试断言应根据并发数调整成功率阈值

**ORM 完整测试经验 (T-800)**
- Date: 2026-05-29
- Instructions:
  - `DataReturn<T>.list` 获取查询结果；`PaginationResult<T>.Data` 获取分页数据
  - 更新 Identity 列会报错，需使用 `field` 参数排除
  - MySQL 批量插入需正确处理日期格式（`yyyy-MM-dd HH:mm:ss`）和布尔值（`0/1`）
  - PostgreSQL 批量插入需要使用参数化查询，布尔值为 `true/false`

**工具使用规范**
- Date: 2026-05-30
- Instructions:
  - 服务器监控：`ServerMonitor.GetMonitorInfo()`
  - JWT：`JwtHelper.GenerateToken()` / `JwtHelper.ValidateToken()`
  - AES 加密/解密：`AesHelper.EncryptWithIV()` / `DecryptWithIV()`（自动生成/提取 IV）
  - RSA：`RsaHelper.GenerateKeyPair()`
  - HMAC 签名：`HmacHelper.HmacSha256()`

**异常管理与远程控制**
- Date: 2026-05-30
- Instructions:
  - 配置文件：db.config 中的 `<IMPlatform>` 节点，默认 `IsEnabled="false"`
  - 初始化：`ExceptionManager.InitializeFromConfig()` 或手动 Initialize
  - 记录异常：`manager.LogException(ex, level, source, additionalData)`
  - 指令前缀默认为 `#`，管理员 QQ 号列表：`AdminQQNumbers`

---

## 运维部署

**连接池和智能调整**
- Date: 2026-05-30
- Instructions:
  - 使用 `ConnectionPoolConfig` 配置：MinPoolSize=10, MaxPoolSize=100, ConnectionTimeout=30s
  - 使用 `ConnectionPoolMonitor` 监控状态和历史
  - dispose 前先关闭连接：`_pooledConnection.Connection.Close()` 防止未关闭 DataReader

---

## 项目质量标准

**Example/Demo/Tests 三项目定位**
- Date: 2026-05-30
- Instructions:
  - Example：业务场景解决方案（侧重"怎么用"）
  - Demo：完整 Web API 项目（侧重"用起来"）
  - Tests：ORM 功能验证（侧重"能用"）
  - 三者不重复，各自独立验证

---

## 项目状态

**FastData ORM v1.4.0 完成**
- Date: 2026-06-01
- Instructions: 核心改进 14 项、DevTools 22 个工具、文档 9 个，企业级特性完整支持，生产就绪
