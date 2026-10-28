# QuickStart

5 分钟跑通 FastData 的最小示例。

## 跑起来

```bash
cd samples/QuickStart
dotnet run
```

不需要安装数据库。用的是 SQLite 文件库，跑完会在输出目录留下 `quickstart.db`。
想重来一遍就删掉这个文件。

## 这段代码做了什么

| 步骤 | 用到的 API | 说明 |
|---|---|---|
| 建表 | `DataContext.EnsureConnectionOpen()` + `cmd` | 真实项目里表通常已由 DBA 建好，这里自包含 |
| 插入 | `FastWrite.Add(entity, key)` | 单条写入，返回 `WriteReturn.IsSuccess` |
| 查询 | `FastRead.Query<T>(lambda, key).ToList<T>()` | Lambda 强类型条件 |
| 分页 | `...OrderBy(...).ToPage<T>(PageModel)` | 返回 `list` 与 `pModel.TotalRecord` |
| 更新 | `FastWrite.Update(model, predicate, fieldSelector, key)` | **必须传 fieldSelector**，见下 |
| 删除 | `FastWrite.Delete<T>(predicate, key)` | 按条件删除 |

## 两个容易踩的坑

**1. 更新一定要传字段选择器**

```csharp
// 对：只更新 Name
FastWrite.Update(
    new User { Name = "新名字" },
    u => u.Name == "旧名字",
    u => new { u.Name },        // ← 这个
    key: "Sqlite");

// 错：不传 fieldSelector 会把实体里所有未赋值的字段一起写回
//     （Age=0、CreatedAt=0001-01-01 之类），可能撞上非空约束
```

**2. 直接用 `DataContext.cmd` 前要打开连接**

```csharp
using (var db = new DataContext("Sqlite"))
{
    db.EnsureConnectionOpen();   // ← 少了这句会抛
                                 //   "ExecuteNonQuery can only be called when the connection is open."
    db.cmd.CommandText = "select 1";
    db.cmd.ExecuteNonQuery();
}
```

走 `FastRead` / `FastWrite` 这些门面方法时框架会自己处理，只有像上面这样手工用 `cmd` 才需要注意。

## 换别的数据库

改 `db.config` 里的 Provider 与连接串即可，其余代码不动：

```xml
<Add Key="Sqlite"
     Provider="Microsoft.Data.Sqlite"
     ConnStr="Data Source=quickstart.db"
     IsDefault="true" />
```

MySQL 用 `MySql.Data.MySqlClient`，PostgreSQL 用 `Npgsql`，
SQL Server 用 `Microsoft.Data.SqlClient`。