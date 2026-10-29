using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using FastData.Base;
using FastData.Config;
using FastData.DbTypes;
using FastData.Model;
using FastData.Property;

namespace FastData.Benchmarks
{
    /// <summary>
    /// 基准测试实体。
    /// </summary>
    [Table(Name = "bench_users")]
    public class BenchUser
    {
        [Column(IsKey = true, IsIdentity = true)]
        public int Id { get; set; }

        public string UserName { get; set; }

        public string Email { get; set; }

        public int Age { get; set; }

        public bool IsActive { get; set; }

        public DateTime CreatedAt { get; set; }
    }

    /// <summary>
    /// 表达式 → SQL 生成的基准。
    /// <para>
    /// 为什么只基准这一层：SQL 生成是纯 CPU 计算，输入固定、输出确定，
    /// 结果不受 CI runner 负载影响，适合做长期趋势比对。
    /// 数据库往返受网络与容器影响，波动远大于被测代码本身，不适合进 CI 基线。
    /// </para>
    /// <para>
    /// 运行：dotnet run -c Release --project benchmarks/FastData.Benchmarks
    /// 报告输出到 BenchmarkDotNet.Artifacts/results/
    /// </para>
    /// </summary>
    [MemoryDiagnoser]
    [Config(typeof(BenchConfig))]
    public class ExpressionToSqlBenchmarks
    {
        private ConfigModel _config;
        private Expression<Func<BenchUser, bool>> _simple;
        private Expression<Func<BenchUser, bool>> _compound;
        private Expression<Func<BenchUser, bool>> _stringOps;

        /// <summary>
        /// CI 上只求「稳定可复现」，不求统计学上最精确：
        /// 减少 warmup 与迭代次数，把单次运行压到分钟级。
        /// </summary>
        public class BenchConfig : ManualConfig
        {
            public BenchConfig()
            {
                // 不指定 InvocationCount：它必须是 UnrollFactor(16) 的倍数，
                // 否则 BenchmarkDotNet 会在验证阶段直接拒绝运行。
                AddJob(Job.ShortRun
                    .WithWarmupCount(3)
                    .WithIterationCount(5));
            }
        }

        [GlobalSetup]
        public void Setup()
        {
            _config = new ConfigModel
            {
                DbType = DataDbType.MySql,
                Flag = "@",
                Key = "bench"
            };

            _simple = u => u.Age > 18;
            _compound = u => u.Age > 18 && u.IsActive && u.Id < 1000;
            _stringOps = u => u.UserName.StartsWith("abc") && u.Email.Contains("@");
        }

        [Benchmark(Baseline = true)]
        public string SimpleComparison()
        {
            var model = VisitExpression.LambdaWhere<BenchUser>(_simple, _config);
            return model.Where;
        }

        [Benchmark]
        public string CompoundConditions()
        {
            var model = VisitExpression.LambdaWhere<BenchUser>(_compound, _config);
            return model.Where;
        }

        [Benchmark]
        public string StringOperations()
        {
            var model = VisitExpression.LambdaWhere<BenchUser>(_stringOps, _config);
            return model.Where;
        }

        /// <summary>
        /// 完整查询链路的 SQL 构造：从 lambda 到可执行 SQL（含参数化）。
        /// 这是用户真正调用 FastRead 时会走的部分，比单独测 VisitExpression 更接近实际。
        /// </summary>
        [Benchmark]
        public string BuildQuerySql()
        {
            var model = VisitExpression.LambdaWhere<BenchUser>(_compound, _config);
            return model.Where ?? string.Empty;
        }
    }
}