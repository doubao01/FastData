# FastData.SyncTool（数据同步工具）

跨数据库数据同步工具，支持 SQL Server、MySQL、PostgreSQL、SQLite 之间的数据同步。

## 目标框架

| 框架 | 说明 |
|------|------|
| net10.0-windows | .NET 10 Windows Desktop |

---

## 功能概览

| 功能 | 说明 |
|------|------|
| **多数据库同步** | SQL Server / MySQL / PostgreSQL / SQLite 之间互相同步 |
| **中间库模式** | 源库 → 中间库 → 目标库，支持断点续传 |
| **增量同步** | 按时间字段增量同步 |
| **全量同步** | 按主键全量同步 |
| **失败重试** | 自动重试 + 失败记录恢复 |
| **定时调度** | 可配置间隔（5-3600 秒） |
| **批量处理** | 可配置批次大小 |
| **去重模式** | 插入前自动检查业务主键 |
| **分表同步** | 支持 5 种分表策略同步 |

---

## 项目结构

```
FastData.SyncTool.WinForms/
├── MainForm.cs                     # 主窗口
├── Program.cs                      # 程序入口
├── Components/                     # 模块化 UI 组件
│   ├── DbConfigControl.cs          # 数据库配置
│   ├── DbConnectionPanel.cs        # 连接面板
│   ├── SyncConfigControl.cs        # 同步配置
│   ├── SyncConfigPanel.cs          # 同步配置面板
│   ├── TableListManager.cs         # 表清单管理
│   ├── TableSelectorDialog.cs      # 表选择对话框
│   ├── FieldSelectorDialog.cs      # 字段选择对话框
│   ├── LogViewerControl.cs         # 日志查看
│   ├── TaskManager.cs              # 任务管理
│   ├── TaskManagerControl.cs       # 任务管理界面
│   ├── PrimaryKeyConfigDialog.cs   # 主键配置对话框
│   ├── ReplayControl.cs            # 数据补录
│   ├── ShardingConfigVisualizer.cs # 分表配置可视化
│   └── Sharding*.cs                # 分表相关界面
├── Services/                       # 服务层
│   ├── DatabaseService.cs          # 数据库操作
│   ├── SyncService.cs              # 同步服务
│   ├── TaskSchedulerService.cs     # 定时调度服务
│   ├── ShardingTaskService.cs      # 分表任务服务
│   └── LogService.cs               # 日志服务
├── IoC/                            # 依赖注入
│   ├── ServiceCollectionExtensions.cs
│   └── ServiceContainer.cs
└── Models/                         # 视图模型
```

---

## 同步配置示例

以下字段名称与 `DataSyncOptions` / `SyncTaskConfig` 保持一致。

### SQL Server → MySQL 同步

```json
{
  "TaskId": "ss_to_mysql",
  "SourceProvider": "System.Data.SqlClient",
  "SourceConnectionString": "server=localhost;database=FastDataDemo;uid=sa;pwd=xxx",
  "TargetProvider": "MySql.Data.MySqlClient",
  "TargetConnectionString": "server=127.0.0.1;database=FastDataDemo;uid=root;pwd=xxx",
  "SourceTable": "AppUser",
  "TargetTable": "AppUser",
  "SyncColumns": [ "Id", "UserName", "Email", "Phone", "Age", "IsActive", "CreateTime" ],
  "PrimaryKeyColumns": "Id",
  "BatchSize": 1000,
  "AlwaysDeduplicate": true,
  "RetryCount": 3
}
```

### SQL Server → PostgreSQL 同步

```json
{
  "TaskId": "ss_to_pg",
  "SourceProvider": "System.Data.SqlClient",
  "SourceConnectionString": "server=localhost;database=FastDataDemo;uid=sa;pwd=xxx",
  "TargetProvider": "Npgsql",
  "TargetConnectionString": "Host=localhost;Database=fastdatademo;Username=postgres;Password=xxx",
  "SourceTable": "AppUser",
  "TargetTable": "AppUser",
  "SyncColumns": [ "Id", "UserName", "Email", "IsActive", "CreateTime" ],
  "PrimaryKeyColumns": "Id",
  "BatchSize": 1000
}
```

---

## 同步策略

| 策略 | 触发条件 | 行为 |
|------|---------|------|
| 全量同步 | 无时间字段 | 按主键逐行同步 |
| 增量同步 | 有时间字段 + RangeDays | 按时间范围同步 |
| 去重插入 | AlwaysDeduplicate=true | INSERT 前检查存在性 |
| UPSERT | AlwaysDeduplicate=false | 存在则 UPDATE，不存在 INSERT |
| 直接插入 | 无主键 | 直接 INSERT |

---

## 全局配置

```csharp
var config = new TableSyncConfig
{
    EnableGlobalConfig = true,      // 启用全局统一配置
    GlobalRangeDays = 7,            // 全局同步范围（天）
    AlwaysDeduplicate = true,       // 始终根据业务主键去重
    EnableMessageQueue = true,      // 启用消息队列
    MessageQueueType = MessageQueueType.ReliableQueue,
    MessageQueueTopic = "sync:users",
    ConsumerConcurrency = 8         // 消费者并发数
};
```

---

## 构建

```bash
dotnet build FastData.SyncTool.WinForms/FastData.SyncTool.WinForms.csproj --framework net10.0-windows
```

## 依赖

- FastData（ORM 核心）
- FastData.Untility（同步核心与工具库，命名空间 `FastData.Tooling.Sync`）
- Microsoft.Data.SqlClient（SQL Server 驱动）
- System.Data.OleDb（OLE DB 驱动）

## 许可证

MIT License
