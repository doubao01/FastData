# FastData

[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)
[![NuGet](https://img.shields.io/badge/NuGet-Fast.Data-blue.svg)](https://www.nuget.org/packages/Fast.Data/)
[![.NET](https://img.shields.io/badge/.NET-4.5.2%20%7C%208.0%20%7C%2010.0-blueviolet)]()

**FastData** 是一个企业级 ORM 框架，支持 .NET Framework 4.5.2 至 .NET 10，提供 Lambda 查询、XML Map SQL、Code First、Db First、AOP、智能连接池、Redis 缓存、消息队列和数据同步等功能。

---

## 快速开始

### 安装

```bash
Install-Package Fast.Data
```

### 快速上手

```csharp
// 查询所有用户
var users = FastRead.Read.Query<User>(u => u.IsActive == true).ToList();

// 添加用户
var user = new User { Name = "张三", Email = "zhangsan@example.com" };
FastRead.Write.Add(user);

// 分页查询
var (items, total) = FastRead.Read.Query<User>()
    .ToPage(new PageModel { PageId = 1, PageSize = 20 });
```

### 详细文档

- 📖 [现代 ORM 特性详解](./FastData/MODERN_ORM_FEATURES.md)
- 📋 [CHANGELOG](./CHANGELOG.md)
- 🧠 [用户指令记忆](./.monkeycode/MEMORY.md)

---

## 项目结构

| 项目 | 说明 | 目标框架 |
|------|------|----------|
| [FastData](FastData/) | 核心 ORM 组件 + DevTools 工具集 | net452;net8.0;net10.0 |
| [FastData.Untility](FastData.Untility/) | 通用工具库（日志/加密/HTTP/Excel） | net452;net8.0;net10.0 |
| [FastData.ModelGenerator.WinForms](FastData.ModelGenerator.WinForms/) | 代码生成工具（仅 Windows） | net8.0-windows;net10.0-windows |
| [FastData.SyncTool.WinForms](FastData.SyncTool.WinForms/) | 数据同步工具（仅 Windows） | net10.0-windows |
| [FastData.Tests](FastData.Tests/) | 单元测试 | net8.0;net10.0 |
| [FastData.Demo](FastData.Demo/) | Web API 示例 | net10.0 |
| [FastData.Example](FastData.Example/) | 控制台示例 | net8.0;net10.0 |

---

## 核心特性

### 数据库支持
- ✅ SQL Server / MySQL / PostgreSQL / Oracle / SQLite / DB2

### 核心功能
- ✅ Lambda 查询表达式
- ✅ XML Map SQL 动态配置
- ✅ 分页查询支持
- ✅ Repository 分层接口
- ✅ Redis 缓存与消息队列
- ✅ AOP 拦截与日志
- ✅ 多种分表策略

### DevTools 工具集（21 个独立工具 + 1 组使用示例）

**基础工具（10 个）**：
- ApplicationWarmup - 应用启动预热工具
- CodeGenerator - 代码生成器
- DatabaseDiagnostic - 数据库诊断
- DatabaseComparer - 数据库比较
- DataImporter - 数据导入导出
- CacheManager - 缓存管理器
- AuditLogger - 审计日志
- SqlQueryBuilder - SQL 构建器
- PerformanceProfiler - 性能分析器
- DatabaseBackupRestore - 备份恢复工具

**高级工具（5 个）**：
- ConnectionPoolManager - 连接池管理器
- DistributedTransactionManager - 分布式事务管理器
- QueryOptimizer - 查询优化器
- ResultCache - 结果缓存工具
- ApiTester - API 测试与数据库监控工具（含 DatabaseMonitor）

**企业级工具（6 个）**：
- DistributedLockManager - 分布式锁管理器
- ApiClient - API 客户端工具
- LogAggregator - 日志聚合器
- EventBus - 事件总线
- ConfigurationManager - 配置管理器
- TaskScheduler - 任务调度器

**示例集（1 组）**：
- DevToolsExamples - 开发者工具完整使用示例集（Examples/DevToolsExamples.cs）

---

## 文档导航

### 项目文档
- [🧠 用户指令记忆](./.monkeycode/MEMORY.md)
- [ CHANGELOG](./CHANGELOG.md)

### 子项目文档
- [FastData 核心库](./FastData/README.md)
- [FastData.Untility 工具库](./FastData.Untility/README.md)
- [现代 ORM 特性详解](./FastData/MODERN_ORM_FEATURES.md)
- [FastData.Tests 测试项目](./FastData.Tests/README.md)
- [FastData.Demo Web API 示例](./FastData.Demo/README.md)
- [FastData.Example 控制台示例](./FastData.Example/README.md)
- [FastData.ModelGenerator.WinForms](./FastData.ModelGenerator.WinForms/README.md)
- [FastData.SyncTool.WinForms](./FastData.SyncTool.WinForms/README.md)

---

## 构建指南

### 跨平台构建（仅 .NET 6/8/10）

```bash
dotnet build -p:BuildPlatform=cross
```

### Windows 构建（包含 .NET Framework 4.5.2）

```bash
dotnet build -p:BuildPlatform=windows
```

---

## 版本信息

**当前版本**：v2.8.1  
**发布日期**：2026-10-18  
**生产就绪**：✅ 是  
**企业级就绪**：✅ 是

---

## 技术支持

- 🐛 报告问题：GitHub Issues
- 💬 技术讨论：GitHub Discussions
- 📧 邮件支持：support@fastdata.com

---

## 许可证

MIT License
