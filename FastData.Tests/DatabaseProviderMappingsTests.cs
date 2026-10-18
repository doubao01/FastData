using FastData.Database;
using Xunit;

namespace FastData.Tests
{
    /// <summary>
    /// Provider 名称规范化测试
    /// 
    /// 验证 DatabaseProviderMappings.NormalizeProviderName 能将 invariant 名称、显示名称
    /// 与常见短名统一规范化为可供 DbProviderFactories 使用的 invariant 名称，
    /// 避免界面把 "SqlServer" 这类短名直接传给工厂导致连接失败。
    /// </summary>
    public class DatabaseProviderMappingsTests
    {
        [Theory]
        [InlineData("Microsoft.Data.SqlClient", "Microsoft.Data.SqlClient")]
        [InlineData("System.Data.SqlClient", "System.Data.SqlClient")]
        [InlineData("npgsql", "Npgsql")]
        [InlineData("mysql.data.mysqlclient", "MySql.Data.MySqlClient")]
        public void NormalizeProviderName_InvariantName_ReturnsCanonicalCasing(string input, string expected)
        {
            Assert.Equal(expected, DatabaseProviderMappings.NormalizeProviderName(input));
        }

        [Theory]
        [InlineData("SqlServer", "Microsoft.Data.SqlClient")]
        [InlineData("sqlserver", "Microsoft.Data.SqlClient")]
        [InlineData("MSSQL", "Microsoft.Data.SqlClient")]
        [InlineData("MySql", "MySql.Data.MySqlClient")]
        [InlineData("PostgreSql", "Npgsql")]
        [InlineData("Postgres", "Npgsql")]
        [InlineData("pgsql", "Npgsql")]
        [InlineData("SQLite", "System.Data.SQLite")]
        [InlineData("Oracle", "Oracle.ManagedDataAccess.Client")]
        public void NormalizeProviderName_ShortName_ReturnsInvariantName(string input, string expected)
        {
            Assert.Equal(expected, DatabaseProviderMappings.NormalizeProviderName(input));
        }

        [Theory]
        [InlineData("SQL Server", "Microsoft.Data.SqlClient")]
        [InlineData("MySQL", "MySql.Data.MySqlClient")]
        [InlineData("PostgreSQL", "Npgsql")]
        public void NormalizeProviderName_DisplayName_ReturnsInvariantName(string input, string expected)
        {
            Assert.Equal(expected, DatabaseProviderMappings.NormalizeProviderName(input));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void NormalizeProviderName_BlankInput_ReturnsNull(string input)
        {
            Assert.Null(DatabaseProviderMappings.NormalizeProviderName(input));
        }

        [Fact]
        public void NormalizeProviderName_UnknownName_ReturnsTrimmedInput()
        {
            Assert.Equal("Custom.DB", DatabaseProviderMappings.NormalizeProviderName("  Custom.DB  "));
        }

        [Fact]
        public void NormalizeProviderName_ResultIsAlwaysUsableAsInvariantName()
        {
            foreach (var input in new[] { "SqlServer", "MySQL", "PostgreSql", "SQLite", "Oracle" })
            {
                var normalized = DatabaseProviderMappings.NormalizeProviderName(input);
                Assert.Contains(normalized, DatabaseProviderMappings.AllProviderNames);
            }
        }
    }
}
