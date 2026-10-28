#if (NET8_0_OR_GREATER || NETCOREAPP)
using FastData.Base;
using FastData.Config;
using FastData.Context;
using FastData.DbTypes;
using FastData.Infrastructure;
using FastData.Property;
using System;
using System.Data.Common;
using Xunit;

namespace FastData.Tests
{
    /// <summary>
    /// 验证应用侧无需手动注册 DbProviderFactory 即可使用 ORM。
    /// </summary>
    [Collection(SharedDatabaseCollection.Name)]
    public class ProviderAutoRegistrationTests
    {
        [Table(Name = "ProviderAutoRegistrationEntity")]
        public class ProviderAutoRegistrationEntity
        {
            public int Id { get; set; }
            public string Name { get; set; }
        }

        [Fact]
        public void GetConfig_ShouldAutoRegisterSqliteProviderFactory()
        {
            ClearConfigCache();

            var config = FastDataConfig.GetConfig("Sqlite");

            Assert.NotNull(config);
            Assert.Equal(DataDbType.SQLite, config.DbType);
            Assert.Equal("Microsoft.Data.Sqlite", config.ProviderName);

            var factory = DbProviderFactories.GetFactory(config.ProviderName);
            Assert.NotNull(factory);
            Assert.NotNull(factory.CreateParameter());
        }

        [Fact]
        public void AutoRegistrar_GetFactory_ShouldReturnRegisteredProvider()
        {
            ClearConfigCache();

            var config = FastDataConfig.GetConfig("Sqlite");
            var factory = DbProviderAutoRegistrar.GetFactory(config.ProviderName);

            Assert.NotNull(factory);
            Assert.NotNull(factory.CreateConnection());
        }

        [Fact]
        public void AutoRegistrar_MissingProvider_ShouldIncludeInstallCommand()
        {
            var ex = Assert.Throws<InvalidOperationException>(() =>
                DbProviderAutoRegistrar.GetFactory("Missing.Provider.For.FastData.Tests"));

            Assert.Contains("DbProviderFactory 未找到", ex.Message);
            Assert.Contains("dotnet add package Missing.Provider.For.FastData.Tests", ex.Message);
        }

        [Fact]
        public void AutoRegistrar_GetInstallCommand_ShouldMapKnownProviderPackage()
        {
            Assert.Equal("dotnet add package Microsoft.Data.Sqlite",
                DbProviderAutoRegistrar.GetInstallCommand("Microsoft.Data.Sqlite"));
            Assert.Equal("dotnet add package MySql.Data",
                DbProviderAutoRegistrar.GetInstallCommand("MySql.Data.MySqlClient"));
            Assert.Equal("dotnet add package Npgsql",
                DbProviderAutoRegistrar.GetInstallCommand("Npgsql"));
        }

        [Fact]
        public void DataContext_ShouldUseAutoRegisteredProviderForCrud()
        {
            ClearConfigCache();

            using (var db = new DataContext("Sqlite"))
            {
                Assert.Equal("Sqlite", db.config.Key);
                EnsureTable(db);
            }

            var orm = Db.Use("Sqlite");
            var add = orm.Add(new ProviderAutoRegistrationEntity
            {
                Id = 820001,
                Name = "auto-provider"
            });
            Assert.True(add.IsSuccess, add.Message);

            var list = orm.List<ProviderAutoRegistrationEntity>(x => x.Id == 820001);
            Assert.Single(list);

            var delete = orm.Delete<ProviderAutoRegistrationEntity>(x => x.Id == 820001);
            Assert.True(delete.IsSuccess, delete.Message);
        }

        private static void EnsureTable(DataContext db)
        {
            if (db.conn.State == System.Data.ConnectionState.Closed)
                db.conn.Open();

            db.cmd.Parameters.Clear();
            db.cmd.CommandText = "CREATE TABLE IF NOT EXISTS ProviderAutoRegistrationEntity (Id INTEGER PRIMARY KEY, Name TEXT)";
            db.cmd.ExecuteNonQuery();
            db.cmd.CommandText = "DELETE FROM ProviderAutoRegistrationEntity";
            db.cmd.ExecuteNonQuery();
        }

        private static void ClearConfigCache()
        {
            DbCache.Remove("web", "FastData.db.config");
            DbCache.Remove("web", "FastData.redis.config");
        }
    }
}
#endif
