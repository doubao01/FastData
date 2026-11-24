using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Reflection;
using FastData.Context;
using FastData.Property;
using FastData.Sharding;
using FastUntility.Page;
using Xunit;

namespace FastData.Tests.Integration
{
    [Collection("Sharding")]
    public class MultiDatabaseShardingCrudTests : IDisposable
    {
        private static readonly string[] Databases = { "SqlServer", "MySql", "PostgreSql" };

        static MultiDatabaseShardingCrudTests()
        {
            try { DbProviderFactories.RegisterFactory("Microsoft.Data.SqlClient", Microsoft.Data.SqlClient.SqlClientFactory.Instance); } catch { }
            try { DbProviderFactories.RegisterFactory("MySql.Data.MySqlClient", MySql.Data.MySqlClient.MySqlClientFactory.Instance); } catch { }
            try { DbProviderFactories.RegisterFactory("Npgsql", Npgsql.NpgsqlFactory.Instance); } catch { }
        }

        public MultiDatabaseShardingCrudTests()
        {
            ShardingManager.Clear();
        }

        public void Dispose()
        {
            ShardingManager.Clear();
        }

        [Theory]
        [InlineData("SqlServer")]
        [InlineData("MySql")]
        [InlineData("PostgreSql")]
        [Trait("Category", "Integration")]
        public void LegacyCrud_RejectionDoesNotChangeStoredDataAndFacadeCrudRemainsUsable(string dbName)
        {
            if (!ShouldRunDbIntegration()) return;

            var baseTableName = "shard_legacy_" + NewTag();
            var tableName = baseTableName + "_202601";
            ConfigureSharding(dbName, baseTableName);
            var queryParams = new Dictionary<string, object>
            {
                { "CreatedAt_Start", new DateTime(2026, 1, 1) },
                { "CreatedAt_End", new DateTime(2026, 1, 31) }
            };
            var original = CreateOrder("legacy_original_" + NewTag(), new DateTime(2026, 1, 15), "Pending");
            var unexpected = CreateOrder("legacy_unexpected_" + NewTag(), original.CreatedAt, "Pending");

            DropTable(dbName, tableName);
            CreateTable(dbName, tableName);
            try
            {
                var client = new FastDataClient(dbName);
                Assert.True(client.ShardAdd(original).IsSuccess);

                AssertLegacyCrudRejected("QueryFromShard", original, tableName);
                AssertLegacyCrudRejected("InsertToShard", unexpected, tableName);
                original.Status = "LegacyChanged";
                AssertLegacyCrudRejected("UpdateInShard", original, tableName);
                AssertLegacyCrudRejected("DeleteFromShard", original, tableName);

                Assert.Equal(1, CountPhysicalRows(dbName, tableName, original.OrderNo));
                Assert.Equal(0, CountPhysicalRows(dbName, tableName, unexpected.OrderNo));
                var stored = Assert.Single(client.ShardQuery<ShardOrder>(row => row.OrderNo == original.OrderNo, queryParams));
                Assert.Equal("Pending", stored.Status);

                var updated = new ShardOrder { OrderNo = original.OrderNo, CreatedAt = original.CreatedAt, Status = "Updated" };
                Assert.True(client.ShardUpdate(updated, row => row.OrderNo == original.OrderNo, row => new { row.Status }).IsSuccess);
                Assert.Equal("Updated", Assert.Single(client.ShardQuery<ShardOrder>(row => row.OrderNo == original.OrderNo, queryParams)).Status);
                Assert.True(client.ShardDelete<ShardOrder>(row => row.OrderNo == original.OrderNo, queryParams).IsSuccess);
                Assert.Equal(0, CountPhysicalRows(dbName, tableName, original.OrderNo));
            }
            finally
            {
                DropTable(dbName, tableName);
            }
        }

        [Theory]
        [InlineData("SqlServer")]
        [InlineData("MySql")]
        [InlineData("PostgreSql")]
        [Trait("Category", "Integration")]
        public void AutomaticTableCreation_FailsWithoutCreatingTablesAndManualCrudRemainsUsable(string dbName)
        {
            if (!ShouldRunDbIntegration()) return;

            var baseTableName = "shard_noauto_" + NewTag();
            var tableName = baseTableName + "_202601";
            ConfigureSharding(dbName, baseTableName);
            ShardingManager.GetConfig<ShardOrder>().AutoCreateTable = true;
            var order = CreateOrder("noauto_" + NewTag(), new DateTime(2026, 1, 15), "Pending");

            DropTable(dbName, tableName);
            try
            {
                Assert.False(PhysicalTableExists(dbName, tableName));
                Assert.Throws<NotSupportedException>(() => ShardingManager.AutoCreateTable(order));
                Assert.Throws<NotSupportedException>(() => ShardingManager.CreateTable<ShardOrder>(tableName));
                Assert.False(PhysicalTableExists(dbName, tableName));

                CreateTable(dbName, tableName);
                Assert.True(new FastDataClient(dbName).ShardAdd(order).IsSuccess);
                Assert.Equal(1, CountPhysicalRows(dbName, tableName, order.OrderNo));
            }
            finally
            {
                DropTable(dbName, tableName);
            }
        }

        [Theory]
        [InlineData("SqlServer", true)]
        [InlineData("MySql", true)]
        [InlineData("PostgreSql", true)]
        [InlineData("SqlServer", false)]
        [InlineData("MySql", false)]
        [InlineData("PostgreSql", false)]
        [Trait("Category", "Integration")]
        public void QueryConfigurationOverride_RoutesWithoutChangingGlobalConfiguration(string dbName, bool configureGlobal)
        {
            if (!ShouldRunDbIntegration()) return;

            var baseTableName = "shard_override_" + NewTag();
            var tableName = baseTableName + "_2026_01";
            var customConfig = new ShardingConfig
            {
                DatabaseKey = dbName,
                BaseTableName = baseTableName,
                ShardingType = ShardingType.Time,
                SuffixFormat = "{0:yyyy_MM}",
                AutoCreateTable = false,
                TimeConfig = new TimeShardingConfig
                {
                    TimeField = "CreatedAt",
                    Granularity = TimeGranularity.Month,
                    StartTime = new DateTime(2026, 1, 1),
                    EndTime = new DateTime(2026, 1, 31)
                }
            };

            DropTable(dbName, tableName);
            CreateTable(dbName, tableName);
            try
            {
                ShardingManager.Configure<ShardOrder>(customConfig);
                var client = new FastDataClient(dbName);
                var order = CreateOrder("override_" + NewTag(), new DateTime(2026, 1, 15), "Pending");
                Assert.True(client.ShardAdd(order).IsSuccess);

                if (configureGlobal)
                    ConfigureSharding(dbName, "global_" + NewTag());
                else
                    ShardingManager.Clear();

                var globalConfig = ShardingManager.GetConfig<ShardOrder>();
                var query = FastRead.Query<ShardOrder>(row => true, key: dbName)
                    .UseSharding()
                    .WithShardingConfig(customConfig);

                Assert.Equal(order.OrderNo, Assert.Single(query.ToList()).OrderNo);
                Assert.Equal(order.OrderNo, query.ToItem().OrderNo);
                Assert.Equal(1, query.ToCount());
                var page = query.ToPagination(1, 10);
                Assert.Equal(1, page.Total);
                Assert.Equal(order.OrderNo, Assert.Single(page.Data).OrderNo);
                Assert.Same(globalConfig, ShardingManager.GetConfig<ShardOrder>());
            }
            finally
            {
                DropTable(dbName, tableName);
            }
        }

        public static IEnumerable<object[]> GetDatabases()
        {
            foreach (var db in Databases)
                yield return new object[] { db };
        }

        [Theory]
        [MemberData(nameof(GetDatabases))]
        public void ShardedCrud_RoutesToPhysicalTablesAcrossDatabases(string dbName)
        {
            if (!ShouldRunDbIntegration()) return;

            var tag = NewTag();
            var janTable = $"shard_orders_{tag}_202601";
            var febTable = $"shard_orders_{tag}_202602";

            ConfigureSharding(dbName, $"shard_orders_{tag}");
            DropTable(dbName, janTable);
            DropTable(dbName, febTable);
            CreateTable(dbName, janTable);
            CreateTable(dbName, febTable);

            try
            {
                using var client = Db.Use(dbName);
                var janOrder = CreateOrder($"{tag}_jan", new DateTime(2026, 1, 15), "new");
                var febOrder = CreateOrder($"{tag}_feb", new DateTime(2026, 2, 10), "new");

                var addJan = client.ShardAdd(janOrder);
                var addFeb = client.ShardAdd(febOrder);
                Assert.True(addJan.IsSuccess, $"{dbName} shard add jan failed: {addJan.Message}");
                Assert.True(addFeb.IsSuccess, $"{dbName} shard add feb failed: {addFeb.Message}");

                Assert.Equal(1, CountPhysicalRows(dbName, janTable, janOrder.OrderNo));
                Assert.Equal(0, CountPhysicalRows(dbName, janTable, febOrder.OrderNo));
                Assert.Equal(1, CountPhysicalRows(dbName, febTable, febOrder.OrderNo));

                var janRows = client.ShardQuery<ShardOrder>(
                    o => o.OrderNo.Contains(tag),
                    new Dictionary<string, object>
                    {
                        { "CreatedAt_Start", new DateTime(2026, 1, 1) },
                        { "CreatedAt_End", new DateTime(2026, 1, 31) }
                    });
                Assert.Single(janRows);
                Assert.Equal(janOrder.OrderNo, janRows[0].OrderNo);

                var update = client.ShardUpdate(
                    new ShardOrder { OrderNo = febOrder.OrderNo, CreatedAt = febOrder.CreatedAt, Status = "paid", Amount = 99 },
                    o => o.OrderNo == febOrder.OrderNo,
                    o => new { o.Status, o.Amount });
                Assert.True(update.IsSuccess, $"{dbName} shard update failed: {update.Message}");

                var febRows = client.ShardQuery<ShardOrder>(
                    o => o.OrderNo == febOrder.OrderNo,
                    new Dictionary<string, object>
                    {
                        { "CreatedAt_Start", new DateTime(2026, 2, 1) },
                        { "CreatedAt_End", new DateTime(2026, 2, 28) }
                    });
                Assert.Single(febRows);
                Assert.Equal("paid", febRows[0].Status);
                Assert.Equal(99, febRows[0].Amount);

                var page = client.ShardQueryPage<ShardOrder>(
                    o => o.OrderNo.Contains(tag),
                    new Dictionary<string, object>
                    {
                        { "CreatedAt_Start", new DateTime(2026, 1, 1) },
                        { "CreatedAt_End", new DateTime(2026, 2, 28) }
                    },
                    pageIndex: 1,
                    pageSize: 10);
                Assert.Equal(2, page.pModel.TotalRecord);
                Assert.Equal(2, page.list.Count);

                var delete = client.ShardDelete<ShardOrder>(
                    o => o.OrderNo == janOrder.OrderNo,
                    new Dictionary<string, object>
                    {
                        { "CreatedAt_Start", new DateTime(2026, 1, 1) },
                        { "CreatedAt_End", new DateTime(2026, 1, 31) }
                    });
                Assert.True(delete.IsSuccess, $"{dbName} shard delete failed: {delete.Message}");
                Assert.Equal(0, CountPhysicalRows(dbName, janTable, janOrder.OrderNo));
                Assert.Equal(1, CountPhysicalRows(dbName, febTable, febOrder.OrderNo));
            }
            finally
            {
                DropTable(dbName, janTable);
                DropTable(dbName, febTable);
            }
        }

        private static void ConfigureSharding(string dbName, string baseTableName)
        {
            ShardingManager.Configure<ShardOrder>(new ShardingConfig
            {
                DatabaseKey = dbName,
                BaseTableName = baseTableName,
                ShardingType = ShardingType.Time,
                TimeConfig = new TimeShardingConfig
                {
                    TimeField = "CreatedAt",
                    Granularity = TimeGranularity.Month,
                    StartTime = new DateTime(2026, 1, 1),
                    EndTime = new DateTime(2026, 2, 28)
                }
            });
        }

        private static ShardOrder CreateOrder(string orderNo, DateTime createdAt, string status)
        {
            return new ShardOrder
            {
                OrderNo = orderNo,
                CreatedAt = createdAt,
                Status = status,
                Amount = 12
            };
        }

        private static int CountPhysicalRows(string dbName, string tableName, string orderNo)
        {
            using var db = new DataContext(dbName);
            var sql = $"select count(0) Count from {QuoteTable(dbName, tableName)} where OrderNo={db.config.Flag}OrderNo";
            var parameter = DbProviderFactories.GetFactory(db.config.ProviderName).CreateParameter();
            parameter.ParameterName = "OrderNo";
            parameter.Value = orderNo;
            var result = db.ExecuteSqlList(sql, new[] { parameter }, isAop: false);
            foreach (var value in result.DicList[0].Values)
                return Convert.ToInt32(value);

            return 0;
        }

        private static void CreateTable(string dbName, string tableName)
        {
            using var db = new DataContext(dbName);
            var table = QuoteTable(dbName, tableName);
            var sql = dbName == "PostgreSql"
                ? $"create table {table} (Id serial primary key, OrderNo varchar(100) not null, CreatedAt timestamp not null, Status varchar(50) null, Amount integer not null)"
                : $"create table {table} (Id int identity(1,1) primary key, OrderNo varchar(100) not null, CreatedAt datetime not null, Status varchar(50) null, Amount int not null)";

            if (dbName == "MySql")
                sql = $"create table {table} (Id int auto_increment primary key, OrderNo varchar(100) not null, CreatedAt datetime not null, Status varchar(50) null, Amount int not null)";

            db.ExecuteSql(sql, isAop: false);
        }

        private static bool PhysicalTableExists(string dbName, string tableName)
        {
            using var db = new DataContext(dbName);
            var sql = "select count(0) Count from information_schema.tables where table_name=" + db.config.Flag + "TableName";
            var parameter = DbProviderFactories.GetFactory(db.config.ProviderName).CreateParameter();
            parameter.ParameterName = "TableName";
            parameter.Value = dbName == "PostgreSql" ? tableName.ToLowerInvariant() : tableName;
            var result = db.ExecuteSqlList(sql, new[] { parameter }, isAop: false);
            Assert.NotEmpty(result.DicList);
            foreach (var value in result.DicList[0].Values)
                return Convert.ToInt32(value) > 0;

            throw new InvalidOperationException("Table existence query returned no count.");
        }

        private static void DropTable(string dbName, string tableName)
        {
            using var db = new DataContext(dbName);
            var table = QuoteTable(dbName, tableName);
            var sql = dbName == "SqlServer" ? $"if object_id('{tableName}', 'U') is not null drop table {table}" : $"drop table if exists {table}";
            db.ExecuteSql(sql, isAop: false);
        }

        private static string QuoteTable(string dbName, string tableName)
        {
            if (dbName == "MySql") return $"`{tableName}`";
            if (dbName == "PostgreSql") return tableName.ToLowerInvariant();
            return tableName;
        }

        private static bool ShouldRunDbIntegration()
        {
            return string.Equals(Environment.GetEnvironmentVariable("FASTDATA_RUN_DB_INTEGRATION"), "true", StringComparison.OrdinalIgnoreCase);
        }

        private static void AssertLegacyCrudRejected(string methodName, ShardOrder entity, string tableName)
        {
            var definition = typeof(ShardingManager).GetMethod(methodName, BindingFlags.Public | BindingFlags.Static);
            Assert.NotNull(definition);
            var method = definition.MakeGenericMethod(typeof(ShardOrder));
            var arguments = methodName == "QueryFromShard"
                ? new object[] { tableName, null }
                : new object[] { entity };
            var invocation = Assert.Throws<TargetInvocationException>(() => method.Invoke(null, arguments));
            Assert.IsType<NotSupportedException>(invocation.InnerException);
        }

        private static string NewTag()
        {
            return $"s{Guid.NewGuid():N}".Substring(0, 12);
        }
    }

    [Table(Name = "shard_orders")]
    public class ShardOrder
    {
        [Column(IsKey = true, IsIdentity = true)]
        public int Id { get; set; }

        public string OrderNo { get; set; }

        public DateTime CreatedAt { get; set; }

        public string Status { get; set; }

        public int Amount { get; set; }
    }
}
