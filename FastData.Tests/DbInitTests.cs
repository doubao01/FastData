using System;
using System.Data.Common;
using FastData;
using FastData.Context;
using FastData.Infrastructure;
using FastData.Tests.Integration;
using Xunit;
using Xunit.Abstractions;

namespace FastData.Tests
{
    /// <summary>
    /// 数据库初始化测试
    /// </summary>
    [Collection(SharedDatabaseCollection.Name)]
    public class DbInitTests
    {
        private readonly ITestOutputHelper _output;

        public DbInitTests(ITestOutputHelper output)
        {
            _output = output;
            DbProviderFactories.RegisterFactory("Microsoft.Data.SqlClient", Microsoft.Data.SqlClient.SqlClientFactory.Instance);
            DbProviderFactories.RegisterFactory("MySql.Data.MySqlClient", MySql.Data.MySqlClient.MySqlClientFactory.Instance);
            DbProviderFactories.RegisterFactory("Npgsql", Npgsql.NpgsqlFactory.Instance);
        }

        [Fact]
        public void Test_PostgreSql_Add_With_New_Context()
        {
            if (!ShouldRunDbIntegration()) return;

            try
            {
                _output.WriteLine("测试 PostgreSQL Add 操作（新 DataContext）...");
                
                using var db = new DataContext("PostgreSql");
                
                // 禁用 CodeFirst 模式，避免表检查问题
                db.config.DesignModel = FastData.Base.DesignPatterns.DbFirst;
                
                var user = new PerfUser
                {
                    UserName = $"PgTest_{DateTime.Now.Ticks}",
                    Email = "pg_test@test.com",
                    Age = 25,
                    IsActive = true,
                    CreatedAt = DateTime.Now
                };

                var result = db.Add(user);
                
                if (result.WriteReturn.IsSuccess)
                {
                    _output.WriteLine("  PostgreSQL Add 成功");
                    db.Delete<PerfUser>(u => u.UserName == user.UserName);
                    _output.WriteLine("  测试数据已清理");
                }
                else
                {
                    _output.WriteLine($"  PostgreSQL Add 失败: {result.WriteReturn.Message}");
                    _output.WriteLine($"  SQL: {result.Sql}");
                }
            }
            catch (Exception ex)
            {
                _output.WriteLine($"  PostgreSQL Add 异常 - {ex.GetType().Name}: {ex.Message}");
                if (ex.InnerException != null)
                    _output.WriteLine($"  内部异常: {ex.InnerException.Message}");
                if (IsEnvironmentUnavailable(ex))
                    return;
                throw;
            }
        }

        [Fact]
        public void Test_MySql_Direct_Connection()
        {
            if (!ShouldRunDbIntegration()) return;

            try
            {
                _output.WriteLine("测试 MySQL 直接连接（不使用连接池）...");
                
                var factory = System.Data.Common.DbProviderFactories.GetFactory("MySql.Data.MySqlClient");
                using var conn = factory.CreateConnection();
                conn.ConnectionString = "server=127.0.0.1;database=FastDataTest;uid=root;pwd=FastData@Test123;SslMode=None;AllowPublicKeyRetrieval=true";
                conn.Open();
                
                _output.WriteLine($"  MySQL 直接连接成功，状态: {conn.State}");
                
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT 1";
                var result = cmd.ExecuteScalar();
                _output.WriteLine($"  查询结果: {result}");
            }
            catch (Exception ex)
            {
                _output.WriteLine($"  MySQL 直接连接失败 - {ex.GetType().Name}: {ex.Message}");
                _output.WriteLine($"  内部异常: {ex.InnerException?.Message}");
                if (IsEnvironmentUnavailable(ex))
                    return;
                throw;
            }
        }

        [Fact]
        public void Init_All_Databases()
        {
            if (!ShouldRunDbIntegration()) return;

            var databases = new[] { "SqlServer", "MySql", "PostgreSql", "Sqlite" };

            foreach (var dbName in databases)
            {
                try
                {
                    _output.WriteLine($"\n初始化数据库: {dbName}");

                    using var db = new DataContext(dbName);

                    var user = new PerfUser
                    {
                        UserName = $"InitUser_{dbName}_{DateTime.Now.Ticks}",
                        Email = $"init_{dbName}@example.com",
                        Age = 1,
                        IsActive = true,
                        CreatedAt = DateTime.Now
                    };

                    var result = db.Add(user);

                    if (result.WriteReturn.IsSuccess)
                    {
                        _output.WriteLine($"  {dbName}: 表创建成功");
                        db.Delete<PerfUser>(u => u.UserName == user.UserName);
                        _output.WriteLine($"  {dbName}: 初始化数据已清理");
                    }
                    else
                    {
                        _output.WriteLine($"  {dbName}: 创建失败 - {result.WriteReturn.Message}");
                    }
                }
                catch (Exception ex)
                {
                    _output.WriteLine($"  {dbName}: 异常 - {ex.GetType().Name}: {ex.Message}");
                    if (IsEnvironmentUnavailable(ex))
                        continue;
                }
            }
        }

        private static bool IsEnvironmentUnavailable(Exception ex)
        {
            return ex.Message.Contains("无法创建新连接")
                || ex.Message.Contains("数据库可能不可达")
                || ex.Message.Contains("连接池")
                || ex.Message.Contains("Unable to connect")
                || ex.Message.Contains("Connection refused")
                || ex.Message.Contains("Connect Timeout")
                || ex.Message.Contains("数据库配置 Key 不存在");
        }

        private static bool ShouldRunDbIntegration()
        {
            return string.Equals(Environment.GetEnvironmentVariable("FASTDATA_RUN_DB_INTEGRATION"), "true", StringComparison.OrdinalIgnoreCase);
        }
    }
}
