using System;
using System.Collections.Generic;
using FastData.DevTools;
using Xunit;

namespace FastData.Tests.DevTools
{
    /// <summary>
    /// 备份链路「空但成功」的回归测试。
    /// <para>
    /// 审查发现：GetTables / GetTableSchema / GetTableData 三处空 catch 会吞掉读库异常，
    /// 结果是备份文件写出来是空的，但结果对象仍返回 Success=true。
    /// 这三处已改为抛出 InvalidOperationException，由 BackupXxx 的 catch 统一置失败。
    /// 这里验证的是「失败必须可判定」这一契约，不需要真实数据库。
    /// </para>
    /// </summary>
    public class BackupFailureContractTests
    {
        /// <summary>
        /// 备份不存在的配置键时必须失败，且给出原因——不能返回 Success=true 的空备份。
        /// </summary>
        [Fact]
        public void CreateBackup_UnknownKey_Fails_With_Message()
        {
            var result = DatabaseBackupRestore.CreateBackup(
                "这个键不存在_" + Guid.NewGuid().ToString("N"),
                "/tmp/never-written-" + Guid.NewGuid().ToString("N") + ".sql");

            Assert.False(result.Success);
            Assert.False(string.IsNullOrEmpty(result.ErrorMessage));
        }

        /// <summary>
        /// 恢复不存在的备份文件时必须失败并说明原因。
        /// </summary>
        [Fact]
        public void RestoreBackup_MissingFile_Fails_With_Message()
        {
            var result = DatabaseBackupRestore.RestoreBackup(
                "Sqlite",
                "/tmp/definitely-not-exists-" + Guid.NewGuid().ToString("N") + ".sql");

            Assert.False(result.Success);
            Assert.Contains("不存在", result.ErrorMessage);
        }

        /// <summary>
        /// 部分恢复的结果对象必须能区分「已回滚」与「部分提交」。
        /// </summary>
        [Fact]
        public void RestoreResult_Exposes_Failure_Details()
        {
            var result = new RestoreResult { Success = false };

            result.Errors.Add("第一条失败");
            result.FailedStatements = 1;
            result.TotalStatements = 10;
            result.ExecutedStatements = 0;
            result.RolledBack = true;

            Assert.False(result.Success);
            Assert.Single(result.Errors);
            Assert.True(result.RolledBack);
            Assert.False(result.IsPartial);
        }
    }
}