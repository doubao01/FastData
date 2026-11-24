# 变更跟踪（Change Tracking）

本页只介绍已存在的实体属性快照与变更检测 API，不把数据库迁移列为可用能力。

## 基本用法

变更跟踪器保存实体属性的原始值，在调用 `GetChanges` 时与当前值比较；它不自动执行数据库写入。

```csharp
using System;
using FastData.ChangeTracking;

public static class TrackingExample
{
    public static void Run()
    {
        var tracker = new ChangeTracker();
        var user = new TrackedUser { Id = 1, Name = "初始名称", Age = 25 };
        tracker.Track(user);

        user.Name = "新名称";
        user.Age = 30;

        foreach (var change in tracker.GetChanges(user))
            Console.WriteLine("{0}: {1}", change.PropertyName, change.ChangeType);

        var updateSql = tracker.GetUpdateSql(user, "tracked_users");
        Console.WriteLine(updateSql);
    }
}

public class TrackedUser
{
    public int Id { get; set; }
    public string Name { get; set; }
    public int Age { get; set; }
}
```

## 快照与生命周期

| API | 行为 |
|---|---|
| `Track(entity)` | 保存实体属性快照 |
| `GetChanges(entity)` | 返回与快照不同的属性；未跟踪实体返回空列表 |
| `HasChanges(entity)` | 检查是否存在属性变更 |
| `UpdateSnapshot(entity)` | 用当前属性值替换原始快照 |
| `Untrack(entity)` | 停止跟踪一个实体 |
| `Clear()` | 清除全部快照 |

只有在应用自己的持久化操作成功后，才应更新快照；更新快照本身不代表数据库已保存。
不再使用实体时可停止跟踪，以释放快照引用。

## SQL 模板的边界

`GetUpdateSql(entity, tableName)` 返回使用属性名和 `@参数名` 的 UPDATE 模板，当前条件固定为
`WHERE Id = @Id`，且不把 `Id` 放入更新字段。没有变更时返回 `null`。

它不创建数据库表、不绑定参数、不执行 SQL，也不提供完整的实体列映射或数据库方言适配。
调用方须自行处理目标数据库的参数和执行过程；原始值与当前值保存在返回的 `PropertyChange` 中。
变更比较使用属性值的 `Equals`，不提供对象图的深度变更跟踪或并发修改协调。

## 数据库迁移状态：未实现

`MigrationManager` 仅为兼容现有引用保留。以下入口会明确抛出 `NotSupportedException`：

- `Migrate()`：不执行迁移的 `Up()`。
- `Rollback(version)`：不执行迁移的 `Down()`。
- `GetMigrationHistory()`：不返回伪造的应用历史。

`AddMigration()` 仍保留登记和参数校验，但登记不代表迁移已执行。
`SqlMigrationBuilder` 只构造 SQL 文本，不提供迁移记录持久化、版本管理或回滚执行。
本版本不宣称具备数据库迁移能力，处置记录见 [#22](https://github.com/doubao01/FastData/issues/22)。

## 验证范围

`MigrationManagerSafetyTests` 在现有 `build-core` 测试作业中验证拒绝执行、重复调用不触发回调、
拒绝伪造历史和原有参数校验。这里验证的是未实现入口不会静默执行，不是迁移功能可用。
