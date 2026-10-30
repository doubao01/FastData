# 文档代码示例的验证

## 问题

文档里的代码示例没有编译过，就没法保证读者照着敲能跑。这个仓库踩过两次：

- README 的分页示例写成 `.ToPage(new PageModel { PageId = 1 })` —— `ToPage<T>` 的
  泛型参数不在形参里，`T` 无法从 `PageModel` 推断，必须显式写 `.ToPage<User>(...)`，
  否则编译失败（CS0411）。
- 早期版本还有 `[Table(Name=...)]` 与实际特性签名不符等问题。

这类错误不会让 CI 变红，只会让第一个照文档上手的人卡住。

## 两层检查

### 1. 示例编译（硬验证）

`tools/DocSamples/DocSamples.cs` 逐字抄录 `FastData/README.md` 里所有 ```csharp 代码块，
补上 `using`、实体定义和 `namespace` 包装后编译。CI 在 `ubuntu-latest / net8.0` 上执行：

```bash
dotnet build tools/DocSamples/DocSamples.csproj -c Release -p:TargetFrameworks=net8.0
```

**规则：改 README 的代码块，必须同步改 `DocSamples.cs`。** 两边不一致时 CI 会失败——
这正说明某一边漏改了。

为了让示例可编译，`DocSamples.cs` 里有少量仅用于编译的噪音，注释已标明：

| 位置 | 处理 | 原因 |
|---|---|---|
| 实体 `User` / `Order` / `UserLog` | 自己定义 | README 里也有，等价抄录 |
| `Block5_MapQuery` | 用 `SqliteParameter` 代替 `SqlParameter` | 免引 `Microsoft.Data.SqlClient`；两者都派生自 `DbParameter`，而 `MapQuery` 形参就是 `DbParameter[]` |
| `ConfigModel()` | 返回 `null` | `ConditionBuilder` 的配置来自配置文件，示例不需要真读 |

### 2. API 名字拼写检查（软验证）

`tools/check-docs-api.py` 扫描全部 13 个文档的代码块，把出现的每个 `Xxx(` 调用名
拿去源码里找定义，找不到就报出来。覆盖 README + `FastData/docs/*.md`。

```bash
python3 tools/check-docs-api.py
```

它**不检查签名**——参数名、重载、必填参数、泛型参数都查不出来。它的价值是在
其余 12 个无法编译验证的文档里挡住"写了不存在的 API"这种最硬的错。

白名单 `tools/docs-api-allowlist.txt` 放的是文档示例里属于**读者自己的业务代码**
（`GetUserById`、`UserController` 之类）。库 API 写错名字不要往里加，去改文档。

## 为什么其余文档不能编译验证

`FastData/docs/` 下的 CACHING.md、SHARDING.md、MODERN_ORM_FEATURES.md 等，示例
都在演示"你的服务层/控制器里怎么用"，引用了读者侧的仓储、DTO、迁移类。要编译就得
凭空造一整套业务代码，而造出来的东西一旦和示例脱节，验证本身就失真了。

因此那些文档只做名字检查。若某个文档的示例可以脱离业务代码成立，欢迎抽出来加进
`DocSamples.cs`。