using System;
using System.Collections.Generic;
using FastData.DevTools;
using Xunit;

namespace FastData.Tests.DevTools
{
    /// <summary>
    /// 恢复过程的失败上报契约。
    /// 这里验证的是「失败必须被记账、不得谎报成功」，
    /// 不需要真实数据库：执行动作由测试注入。
    /// </summary>
    public class RestoreReportingTests
    {
        private static string Statement(int index)
        {
            return string.Format("INSERT INTO t VALUES ({0});", index);
        }

        /// <summary>
        /// 默认（IgnoreErrors = false）：第一条失败即中止，后面的语句不再执行
        /// </summary>
        [Fact]
        public void FailFast_Stops_At_First_Failure()
        {
            var result = new RestoreResult();
            var options = new RestoreOptions(); // IgnoreErrors = false
            var executed = new List<string>();

            DatabaseBackupRestore.RunStatements(
                new[] { Statement(1), Statement(2), Statement(3) },
                result,
                options,
                s =>
                {
                    if (s == Statement(2))
                        throw new InvalidOperationException("重复主键");
                    executed.Add(s);
                });

            Assert.Equal(1, result.ExecutedStatements);
            Assert.Equal(1, result.FailedStatements);
            Assert.Single(result.Errors);
            Assert.Contains("重复主键", result.Errors[0]);
            Assert.DoesNotContain(Statement(3), executed);
        }

        /// <summary>
        /// 忽略错误时继续执行，但失败数照样累计 —— 上层据此判定 Success=false
        /// </summary>
        [Fact]
        public void IgnoreErrors_Continues_But_Still_Counts_Failures()
        {
            var result = new RestoreResult();
            var options = new RestoreOptions { IgnoreErrors = true };
            var attempted = 0;

            DatabaseBackupRestore.RunStatements(
                new[] { Statement(1), Statement(2), Statement(3), Statement(4) },
                result,
                options,
                s =>
                {
                    attempted++;
                    if (s == Statement(2) || s == Statement(4))
                        throw new InvalidOperationException("违反约束");
                });

            Assert.Equal(4, attempted);
            Assert.Equal(2, result.ExecutedStatements);
            Assert.Equal(2, result.FailedStatements);
            Assert.Equal(2, result.Errors.Count);
        }

        /// <summary>
        /// 全部成功时不应产生任何失败记录
        /// </summary>
        [Fact]
        public void All_Success_Records_No_Failure()
        {
            var result = new RestoreResult();
            var options = new RestoreOptions();

            DatabaseBackupRestore.RunStatements(
                new[] { Statement(1), Statement(2) },
                result,
                options,
                s => { });

            Assert.Equal(2, result.ExecutedStatements);
            Assert.Equal(0, result.FailedStatements);
            Assert.Empty(result.Errors);
        }

        /// <summary>
        /// 错误明细里保留可定位的语句片段，超长语句被截断
        /// </summary>
        [Fact]
        public void Error_Message_Contains_Truncated_Statement()
        {
            var result = new RestoreResult();
            var longStatement = new string('X', 300) + ";";

            DatabaseBackupRestore.RunStatements(
                new[] { longStatement },
                result,
                new RestoreOptions(),
                s => { throw new InvalidOperationException("语法错误"); });

            Assert.Single(result.Errors);
            Assert.Contains("语法错误", result.Errors[0]);
            Assert.Contains("...", result.Errors[0]);
        }

        /// <summary>
        /// 空语句与注释行不应进入执行队列
        /// </summary>
        [Fact]
        public void FilterStatements_Skips_Blank_And_Comments()
        {
            var statements = new List<string>
            {
                "",
                "   ",
                "-- 这是注释",
                "  -- 缩进的注释",
                "SELECT 1;"
            };

            var filtered = DatabaseBackupRestore.FilterStatements(statements);

            Assert.Single(filtered);
            Assert.Equal("SELECT 1;", filtered[0]);
        }

        /// <summary>
        /// 每条错误都带上自己的异常信息，便于定位是第几条挂的
        /// </summary>
        [Fact]
        public void Each_Error_Is_Recorded_Separately()
        {
            var result = new RestoreResult();

            DatabaseBackupRestore.RunStatements(
                new[] { Statement(1), Statement(2) },
                result,
                new RestoreOptions { IgnoreErrors = true },
                s =>
                {
                    throw new InvalidOperationException(s == Statement(1) ? "错误甲" : "错误乙");
                });

            Assert.Equal(2, result.Errors.Count);
            Assert.Contains("错误甲", result.Errors[0]);
            Assert.Contains("错误乙", result.Errors[1]);
        }
    }
}
