#if (NET8_0_OR_GREATER || NETCOREAPP)
using FastData.Base;
using FastData.Config;
using FastData.Context;
using FastData.DbTypes;
using FastData.Property;
using System;
using System.Configuration;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace FastData.Tests
{
    /// <summary>
    /// 配置文件解析到 DataContext/CRUD 的端到端验证。
    /// </summary>
    [Collection(nameof(ConfigEnvironmentCollection))]
    public class ConfigFullChainTests
    {
        [Table(Name = "ConfigChainEntity")]
        public class ConfigChainEntity
        {
            public int Id { get; set; }
            public string Name { get; set; }
            public bool IsActive { get; set; }
        }

        [Table(Name = "ShortCrudEntity")]
        public class ShortCrudEntity
        {
            public int Id { get; set; }
            public string Name { get; set; }
            public int Score { get; set; }
            public bool IsActive { get; set; }
        }

        [Fact]
        public void DbConfig_ActiveEnvironment_ToDataContextCrud_ShouldWork()
        {
            ClearConfigCache();

            var active = FastDataConfig.GetActiveEnvironment();
            Assert.Equal("dev", active);

            var config = FastDataConfig.GetConfig("Sqlite");
            Assert.NotNull(config);
            Assert.Equal("Sqlite", config.Key);
            Assert.Equal(DataDbType.SQLite, config.DbType);
            Assert.Equal("Microsoft.Data.Sqlite", config.ProviderName);
            Assert.Equal("@", config.Flag);
            Assert.Contains("FastDataTest-OrmPerf.db", config.ConnStr);
            Assert.Equal("web", config.CacheType);
            Assert.Equal("file", config.SqlErrorType);
            Assert.True(config.IsOutSql);
            Assert.False(config.IsOutError);
            Assert.True(config.IsPropertyCache);
            Assert.Equal("DbFirst", config.DesignModel);
            Assert.False(config.IsMapSave);
            Assert.False(config.IsEncrypt);
            Assert.False(config.IsUpdateCache);

            var defaulted = FastDataConfig.GetConfig("MySql");
            Assert.NotNull(defaulted);
            Assert.Equal("web", defaulted.CacheType);
            Assert.Equal("db", defaulted.SqlErrorType);
            Assert.True(defaulted.IsOutSql);
            Assert.True(defaulted.IsOutError);
            Assert.True(defaulted.IsPropertyCache);
            Assert.Equal("DbFirst", defaulted.DesignModel);
            Assert.False(defaulted.IsMapSave);
            Assert.False(defaulted.IsEncrypt);
            Assert.False(defaulted.IsUpdateCache);

            var poolConfig = FastDataConfig.GetConnectionPoolConfig();
            Assert.Equal(20, poolConfig.MinPoolSize);
            Assert.Equal(500, poolConfig.MaxPoolSize);
            Assert.Equal(30, poolConfig.ConnectionTimeout);
            Assert.Equal(30, poolConfig.ConnectionLifetime);
            Assert.Equal(60, poolConfig.HealthCheckInterval);
            Assert.Equal(300, poolConfig.LeakDetectionThreshold);
            Assert.True(poolConfig.EnableSmartAdjustment);
            Assert.Equal(80, poolConfig.LoadThreshold);
            Assert.Equal(30, poolConfig.ShrinkThreshold);
            Assert.Equal(3, poolConfig.MaxRetries);
            Assert.Equal(50, poolConfig.RetryBaseDelayMs);
            Assert.Equal(5, poolConfig.ValidationCommandTimeout);
            Assert.Equal(30, poolConfig.SmartAdjustmentInterval);
            Assert.Equal(10, poolConfig.MaxExpandCount);
            Assert.Equal(5, poolConfig.MaxShrinkCount);
            Assert.Equal(3, poolConfig.ErrorShrinkThreshold);
            Assert.Equal(20, poolConfig.ErrorShrinkPercentage);
            Assert.False(poolConfig.EnableRedisCheck);
            Assert.False(poolConfig.CircuitBreaker.Enabled);
            Assert.Equal(5, poolConfig.CircuitBreaker.FailureThreshold);
            Assert.Equal(30, poolConfig.CircuitBreaker.CircuitOpenDurationSec);
            Assert.Equal(3, poolConfig.CircuitBreaker.HalfOpenMaxRequests);

            var connectionString = FastDataConfig.GetConnectionString("Sqlite");
            Assert.Equal(config.ConnStr, connectionString);

            var summary = FastDataConfig.GetConnectionSummary("Sqlite");
            Assert.NotNull(summary);
            Assert.Equal("Sqlite", summary["key"]);
            Assert.Equal("SQLite", summary["dbType"]);

            using (var db = new DataContext("Sqlite"))
            {
                Assert.Equal("Sqlite", db.config.Key);
                Assert.Equal(DataDbType.SQLite, db.config.DbType);

                EnsureTable(db);
            }

            var orm = Db.Use("Sqlite");
            var addResult = orm.Add(new ConfigChainEntity
            {
                Id = 810001,
                Name = "config-chain",
                IsActive = true
            });
            Assert.True(addResult.IsSuccess, addResult.Message);

            var item = orm.First<ConfigChainEntity>(x => x.Id == 810001);
            Assert.NotNull(item);
            Assert.Equal("config-chain", item.Name);

            var deleteResult = orm.Delete<ConfigChainEntity>(x => x.Id == 810001);
            Assert.True(deleteResult.IsSuccess, deleteResult.Message);
        }

        [Fact]
        public void DbUse_ShortCrudApi_ShouldCoverCommonOperations()
        {
            ClearConfigCache();

            using (var db = new DataContext("Sqlite"))
            {
                EnsureShortCrudTable(db);
            }

            var orm = Db.Use("Sqlite");
            var addResult = orm.AddRange(new List<ShortCrudEntity>
            {
                new ShortCrudEntity { Id = 820001, Name = "short-alpha", Score = 10, IsActive = true },
                new ShortCrudEntity { Id = 820002, Name = "short-beta", Score = 20, IsActive = true },
                new ShortCrudEntity { Id = 820003, Name = "short-gamma", Score = 30, IsActive = false }
            });
            Assert.True(addResult.IsSuccess, addResult.Message);

            var all = orm.List<ShortCrudEntity>();
            Assert.Equal(3, all.Count);

            var active = orm.List<ShortCrudEntity>(x => x.IsActive);
            Assert.Equal(2, active.Count);

            var first = orm.First<ShortCrudEntity>(x => x.Name == "short-beta");
            Assert.NotNull(first);
            Assert.Equal(20, first.Score);

            Assert.Equal(3, orm.Count<ShortCrudEntity>());
            Assert.Equal(2, orm.Count<ShortCrudEntity>(x => x.IsActive));

            var page = orm.Page<ShortCrudEntity>(1, 2);
            Assert.Equal(3, page.Total);
            Assert.Equal(2, page.Data.Count);

            var typedSql = orm.Sql<ShortCrudEntity>("SELECT * FROM ShortCrudEntity WHERE Score >= 20 ORDER BY Id");
            Assert.Equal(new[] { 820002, 820003 }, typedSql.Select(x => x.Id).ToArray());

            var rows = orm.Sql("SELECT Name, Score FROM ShortCrudEntity WHERE Id = 820001");
            Assert.Single(rows);
            Assert.Contains("short-alpha", rows[0].Values);

            var execResult = orm.Exec("UPDATE ShortCrudEntity SET Score = 25 WHERE Id = 820002");
            Assert.True(execResult.IsSuccess, execResult.Message);
            Assert.Equal(25, orm.First<ShortCrudEntity>(x => x.Id == 820002).Score);

            var deleteResult = orm.Delete<ShortCrudEntity>(x => x.Id >= 820001 && x.Id <= 820003);
            Assert.True(deleteResult.IsSuccess, deleteResult.Message);
            Assert.Equal(0, orm.Count<ShortCrudEntity>());
        }

        [Fact]
        public void DbConfig_DuplicateConnectionKey_ShouldFailFast()
        {
            ClearConfigCache();
            Environment.SetEnvironmentVariable("FASTDATA_ACTIVE", "duplicate-test");

            try
            {
                var ex = Assert.Throws<ConfigurationErrorsException>(() => FastDataConfig.GetConfig());
                Assert.Contains("Duplicate FastData connection key", ex.Message);
                Assert.Contains("Sqlite", ex.Message);
            }
            finally
            {
                Environment.SetEnvironmentVariable("FASTDATA_ACTIVE", null);
                ClearConfigCache();
            }
        }

        private static void EnsureTable(DataContext db)
        {
            if (db.conn.State == System.Data.ConnectionState.Closed)
                db.conn.Open();

            db.cmd.Parameters.Clear();
            db.cmd.CommandText = "CREATE TABLE IF NOT EXISTS ConfigChainEntity (Id INTEGER PRIMARY KEY, Name TEXT, IsActive INTEGER)";
            db.cmd.ExecuteNonQuery();
            db.cmd.CommandText = "DELETE FROM ConfigChainEntity";
            db.cmd.ExecuteNonQuery();
        }

        private static void EnsureShortCrudTable(DataContext db)
        {
            if (db.conn.State == System.Data.ConnectionState.Closed)
                db.conn.Open();

            db.cmd.Parameters.Clear();
            db.cmd.CommandText = "CREATE TABLE IF NOT EXISTS ShortCrudEntity (Id INTEGER PRIMARY KEY, Name TEXT, Score INTEGER, IsActive INTEGER)";
            db.cmd.ExecuteNonQuery();
            db.cmd.CommandText = "DELETE FROM ShortCrudEntity";
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
