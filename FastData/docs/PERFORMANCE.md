# 性能基线

本文件记录 SQL 生成链路的实测数据，用于版本间比对。
数据由 `benchmarks/FastData.Benchmarks` 产出，CI 的 `benchmark` job 每次构建都会重跑并归档完整报告。

## 为什么只基准这一层

SQL 生成是**纯 CPU 计算**，输入固定、输出确定，结果不受 runner 负载影响，适合做趋势比对。
数据库往返受网络与容器影响，波动往往大于被测代码本身，不适合进 CI 基线。

## 首次基线（2026-10-28，GitHub ubuntu-latest，net8.0）

| 方法 | 平均耗时 | 说明 |
|---|---|---|
| `SimpleComparison` | **11.03 µs** | 单条件 `u.Age > 18`（Baseline） |
| `CompoundConditions` | **11.75 µs** | 三条件 AND（+6.6%） |
| `StringOperations` | **13.15 µs** | `StartsWith` + `Contains`（+19.2%） |
| `BuildQuerySql` | **11.94 µs** | 完整 lambda → SQL 构造（+8.3%） |

测量质量：StdDev 0.014~0.072 µs，StdErr 占均值 0.06%~0.30%，
说明这批数字**可复现**，可作为后续比对的锚点。

## 怎么用这份数据

**看趋势，不看单点。** 单次运行会受 runner 状态影响；连续几版都上升才是信号。

**关注 `StringOperations`。** 它比单条件慢 19%，说明字符串操作符的表达式处理开销更大。
如果要优化，这里是收益最高的地方。

**告警阈值尚未设定。** 需要积累若干个版本的数据，才能区分「正常波动」与「真实退化」。
在此之前不做自动判定 —— 单次波动足以误报，误报比不报更糟。

## 怎么跑

```bash
dotnet run -c Release --project benchmarks/FastData.Benchmarks
```

无参数时默认跑全部（`--filter '*'`）。
显式指定：`-- --filter '*SimpleComparison*'`

结果在 `BenchmarkDotNet.Artifacts/results/`（工作目录下，即仓库根）。

## 更新本文件

CI 每次都会产出新报告。若某次改动**有意**影响性能，比对后更新上表并注明原因与日期；
若**无意**但数值明显变化，先查清原因再更新 —— 直接覆盖数字等于把退化洗白。
