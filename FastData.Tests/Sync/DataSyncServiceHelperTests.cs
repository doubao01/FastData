using System;
using System.Data;
using System.Data.Common;
using FastData.Tooling.Sync;
using Xunit;

namespace FastData.Tests.Sync
{
    /// <summary>
    /// 同步核心辅助逻辑测试
    /// 
    /// 覆盖 DataSyncService 中的纯函数逻辑：参数前缀选择（Oracle/通用库）、
    /// 单语句参数上限、SQL 标识符校验、主键数值转换与全局配置应用。
    /// </summary>
    public class DataSyncServiceHelperTests
    {
        #region GetMaxParametersPerStatement

        [Theory]
        [InlineData(null, 900)]
        [InlineData("", 900)]
        [InlineData("System.Data.SQLite", 900)]
        [InlineData("MySql.Data.MySqlClient", 60000)]
        [InlineData("Npgsql", 60000)]
        [InlineData("PostgreSQL", 60000)]
        [InlineData("System.Data.SqlClient", 2000)]
        [InlineData("Microsoft.Data.SqlClient", 2000)]
        [InlineData("SqlServer", 2000)]
        [InlineData("Oracle.ManagedDataAccess.Client", 1000)]
        [InlineData("SomeUnknownProvider", 900)]
        public void GetMaxParametersPerStatement_ReturnsProviderSpecificLimit(string provider, int expected)
        {
            Assert.Equal(expected, DataSyncService.GetMaxParametersPerStatement(provider));
        }

        #endregion

        #region GetParameterPrefix

        [Fact]
        public void GetParameterPrefix_OracleConnection_ReturnsColon()
        {
            using (var connection = new OracleConnectionStub())
            {
                Assert.Equal(":", DataSyncService.GetParameterPrefix(connection));
            }
        }

        [Fact]
        public void GetParameterPrefix_NonOracleConnection_ReturnsAt()
        {
            using (var connection = new SqlConnectionStub())
            {
                Assert.Equal("@", DataSyncService.GetParameterPrefix(connection));
            }
        }

        [Fact]
        public void GetParameterPrefix_NullConnection_ReturnsAt()
        {
            Assert.Equal("@", DataSyncService.GetParameterPrefix(null));
        }

        #endregion

        #region ValidateIdentifier

        [Theory]
        [InlineData("Order")]
        [InlineData("dbo.AppUser")]
        [InlineData("User_Log$1")]
        [InlineData("temp#table")]
        [InlineData("table@1")]
        public void ValidateIdentifier_ValidNames_DoNotThrow(string name)
        {
            DataSyncService.ValidateIdentifier(name, "测试");
        }

        [Theory]
        [InlineData("Order; DROP TABLE x")]
        [InlineData("Order'")]
        [InlineData("Order --")]
        [InlineData("1Order")]
        [InlineData("Order Name")]
        [InlineData("Order\"")]
        public void ValidateIdentifier_InvalidNames_Throw(string name)
        {
            Assert.Throws<ArgumentException>(() => DataSyncService.ValidateIdentifier(name, "测试"));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void ValidateIdentifier_BlankValue_IsIgnored(string name)
        {
            DataSyncService.ValidateIdentifier(name, "测试");
        }

        #endregion

        #region TryConvertToInt64

        [Theory]
        [InlineData(1)]
        [InlineData(42L)]
        [InlineData((short)7)]
        public void TryConvertToInt64_NumericTypes_ReturnTrue(object value)
        {
            Assert.True(DataSyncService.TryConvertToInt64(value, out var result));
            Assert.Equal(Convert.ToInt64(value), result);
        }

        [Theory]
        [InlineData("123", 123L)]
        [InlineData("9223372036854775807", long.MaxValue)]
        public void TryConvertToInt64_NumericString_ReturnTrue(string value, long expected)
        {
            Assert.True(DataSyncService.TryConvertToInt64(value, out var result));
            Assert.Equal(expected, result);
        }

        [Fact]
        public void TryConvertToInt64_GuidValue_ReturnsFalse()
        {
            Assert.False(DataSyncService.TryConvertToInt64(Guid.NewGuid(), out _));
        }

        [Fact]
        public void TryConvertToInt64_NonNumericString_ReturnsFalse()
        {
            Assert.False(DataSyncService.TryConvertToInt64("abc", out _));
        }

        [Fact]
        public void TryConvertToInt64_NullAndDbNull_ReturnFalse()
        {
            Assert.False(DataSyncService.TryConvertToInt64(null, out _));
            Assert.False(DataSyncService.TryConvertToInt64(DBNull.Value, out _));
        }

        #endregion

        #region GetMaxPrimaryKeyValue

        [Fact]
        public void GetMaxPrimaryKeyValue_NumericColumn_ReturnsMax()
        {
            var table = new DataTable();
            table.Columns.Add("Id", typeof(int));
            table.Rows.Add(1);
            table.Rows.Add(5);
            table.Rows.Add(3);

            Assert.Equal(5L, DataSyncService.GetMaxPrimaryKeyValue(table, "Id"));
        }

        [Fact]
        public void GetMaxPrimaryKeyValue_NonNumericColumn_ReturnsNull()
        {
            var table = new DataTable();
            table.Columns.Add("Id", typeof(string));
            table.Rows.Add(Guid.NewGuid().ToString());

            Assert.Null(DataSyncService.GetMaxPrimaryKeyValue(table, "Id"));
        }

        [Fact]
        public void GetMaxPrimaryKeyValue_EmptyTable_ReturnsNull()
        {
            var table = new DataTable();
            table.Columns.Add("Id", typeof(int));

            Assert.Null(DataSyncService.GetMaxPrimaryKeyValue(table, "Id"));
        }

        [Fact]
        public void GetMaxPrimaryKeyValue_MissingColumn_ReturnsNull()
        {
            var table = new DataTable();
            table.Columns.Add("Other", typeof(int));
            table.Rows.Add(1);

            Assert.Null(DataSyncService.GetMaxPrimaryKeyValue(table, "Id"));
        }

        #endregion

        #region ApplyGlobalConfig

        [Fact]
        public void ApplyGlobalConfig_Enabled_OverridesRangeAndDedup()
        {
            var options = new DataSyncOptions
            {
                EnableGlobalConfig = true,
                GlobalRangeDays = 7,
                GlobalAlwaysDeduplicate = false,
                RangeDays = 3,
                AlwaysDeduplicate = true
            };

            DataSyncService.ApplyGlobalConfig(options);

            Assert.Equal(7, options.RangeDays);
            Assert.False(options.AlwaysDeduplicate);
        }

        [Fact]
        public void ApplyGlobalConfig_Disabled_KeepsTaskLevelValues()
        {
            var options = new DataSyncOptions
            {
                EnableGlobalConfig = false,
                GlobalRangeDays = 7,
                GlobalAlwaysDeduplicate = false,
                RangeDays = 3,
                AlwaysDeduplicate = true
            };

            DataSyncService.ApplyGlobalConfig(options);

            Assert.Equal(3, options.RangeDays);
            Assert.True(options.AlwaysDeduplicate);
        }

        #endregion

        #region 模型默认值

        [Fact]
        public void DataSyncResult_SkippedCount_DefaultsToZero()
        {
            Assert.Equal(0, new DataSyncResult().SkippedCount);
        }

        [Fact]
        public void DataSyncOptions_GlobalAlwaysDeduplicate_DefaultsToTrue()
        {
            Assert.True(new DataSyncOptions().GlobalAlwaysDeduplicate);
        }

        #endregion

        #region 测试桩

        private class OracleConnectionStub : DbConnection
        {
            public override string ConnectionString { get; set; }
            public override string Database => "oracle";
            public override string DataSource => "oracle";
            public override string ServerVersion => "1.0";
            public override ConnectionState State => ConnectionState.Closed;
            public override void ChangeDatabase(string databaseName) { }
            public override void Close() { }
            public override void Open() { }
            protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel) => null;
            protected override DbCommand CreateDbCommand() => null;
        }

        private class SqlConnectionStub : DbConnection
        {
            public override string ConnectionString { get; set; }
            public override string Database => "sql";
            public override string DataSource => "sql";
            public override string ServerVersion => "1.0";
            public override ConnectionState State => ConnectionState.Closed;
            public override void ChangeDatabase(string databaseName) { }
            public override void Close() { }
            public override void Open() { }
            protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel) => null;
            protected override DbCommand CreateDbCommand() => null;
        }

        #endregion
    }
}
