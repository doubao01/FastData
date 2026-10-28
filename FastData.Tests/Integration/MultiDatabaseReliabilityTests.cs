using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Threading.Tasks;
using FastData.Context;
using FastUntility.Page;
using Xunit;

namespace FastData.Tests.Integration
{
    /// <summary>
    /// Multi-database reliability tests for features that are expected to behave the same
    /// across SqlServer, MySql, and PostgreSql. Avoids raw SQL dialects and provider-specific parameters.
    /// </summary>
    public class MultiDatabaseReliabilityTests
    {
        private static readonly string[] Databases = { "SqlServer", "MySql", "PostgreSql" };

        static MultiDatabaseReliabilityTests()
        {
            try { DbProviderFactories.RegisterFactory("Microsoft.Data.SqlClient", Microsoft.Data.SqlClient.SqlClientFactory.Instance); } catch { }
            try { DbProviderFactories.RegisterFactory("MySql.Data.MySqlClient", MySql.Data.MySqlClient.MySqlClientFactory.Instance); } catch { }
            try { DbProviderFactories.RegisterFactory("Npgsql", Npgsql.NpgsqlFactory.Instance); } catch { }
        }

        public static IEnumerable<object[]> GetDatabases()
        {
            foreach (var db in Databases)
                yield return new object[] { db };
        }

        /// <summary>
        /// 重复执行「异步插入后立即查询」以暴露间歇性可见性问题（issue #17）。
        /// <para>
        /// 原用例单次执行，失败概率低，重跑就过，无法定位。这里把同一路径连跑多次，
        /// 把「偶发」放大成「大概率复现」，并在失败时带上实际 SQL 便于定位。
        /// </para>
        /// </summary>
        [Theory]
        [MemberData(nameof(GetDatabases))]
        public async Task AsyncCrud_Repeated_ShouldAlwaysSeeInsertedRow(string dbName)
        {
            if (!ShouldRunDbIntegration()) return;

            const int rounds = 30;

            for (var i = 0; i < rounds; i++)
            {
                var tag = NewTag("loop");
                var addResult = await FastWrite.AddAsy(CreateUser(tag, 20 + (i % 10)), key: dbName);

                Assert.True(addResult.IsSuccess,
                    string.Format("{0} 第 {1}/{2} 次 AddAsy 失败（UserName={3}）：{4}",
                        dbName, i + 1, rounds, tag, addResult.Message));

                var row = await FastRead.Use(dbName)
                    .Query<PerfUser>(u => u.UserName == tag)
                    .ToItemAsync<PerfUser>();

                Assert.True(row != null,
                    string.Format("{0} 第 {1}/{2} 次插入后立即查询不到 UserName={3}（插入返回成功，Message={4}）",
                        dbName, i + 1, rounds, tag, addResult.Message));
            }
        }

        [Theory]
        [MemberData(nameof(GetDatabases))]
        public async Task AsyncCrud_ByPredicate_WorksAcrossDatabases(string dbName)
        {
            if (!ShouldRunDbIntegration()) return;

            var tag = NewTag("async");
            var user = CreateUser(tag, 21);

            var addResult = await FastWrite.AddAsy(user, key: dbName);
            Assert.True(addResult.IsSuccess, $"{dbName} add failed: {addResult.Message}");

            var inserted = await FastRead.Use(dbName)
                .Query<PerfUser>(u => u.UserName == tag)
                .ToItemAsync<PerfUser>();

            if (inserted == null)
            {
                // 区分「数据没写进去」与「写进去了但 ORM 查不到」：
                // 后者是查询/映射问题，前者是写入/事务问题，两者的修法完全不同。
                var nativeCount = -1;
                try
                {
                    using (var probe = new DataContext(dbName))
                    {
                        probe.cmd.CommandText = "select count(1) from perf_users where UserName = @p_tag";
                        probe.cmd.Parameters.Clear();
                        var p = probe.cmd.CreateParameter();
                        p.ParameterName = "@p_tag";
                        p.Value = tag;
                        probe.cmd.Parameters.Add(p);
                        nativeCount = Convert.ToInt32(probe.cmd.ExecuteScalar());
                    }
                }
                catch (Exception probeEx)
                {
                    nativeCount = -2;
                    Console.WriteLine("原生 COUNT 探测失败: " + probeEx.Message);
                }

                Assert.True(false,
                    string.Format("{0} AddAsy 后查不到 UserName={1}；原生 COUNT={2}（0=数据未落库，>0=ORM 查询/映射问题，-1/-2=探测本身失败）",
                        dbName, tag, nativeCount));
            }

            var updatedEmail = $"updated_{tag}@example.com";
            var updateResult = await FastWrite.UpdateAsy(
                new PerfUser { Email = updatedEmail, Age = 32, IsActive = true, CreatedAt = DateTime.Now },
                u => u.UserName == tag,
                u => new { u.Email, u.Age, u.IsActive, u.CreatedAt },
                key: dbName);
            Assert.True(updateResult.IsSuccess, $"{dbName} update failed: {updateResult.Message}");

            var updated = FastRead.Use(dbName)
                .Query<PerfUser>(u => u.UserName == tag)
                .ToItem<PerfUser>();
            Assert.NotNull(updated);
            Assert.Equal(tag, updated.UserName);
            Assert.Equal(32, updated.Age);
            Assert.Equal(updatedEmail, updated.Email);

            var deleteResult = await FastWrite.DeleteAsy<PerfUser>(u => u.UserName == tag, key: dbName);
            Assert.True(deleteResult.IsSuccess, $"{dbName} delete failed: {deleteResult.Message}");

            Assert.Equal(0, FastRead.Use(dbName).Query<PerfUser>(u => u.UserName == tag).ToCount());
        }

        [Theory]
        [MemberData(nameof(GetDatabases))]
        public void ClientApi_QueryUpdateDelete_WorksAcrossDatabases(string dbName)
        {
            if (!ShouldRunDbIntegration()) return;

            var tag = NewTag("client");
            using var client = Db.Use(dbName);

            var addResult = client.Add(CreateUser(tag, 24));
            Assert.True(addResult.IsSuccess, $"{dbName} client add failed: {addResult.Message}");

            Assert.Equal(1, client.Count<PerfUser>(u => u.UserName == tag));

            var updateResult = client.Update(
                new PerfUser { Age = 41, IsActive = true, CreatedAt = DateTime.Now },
                u => u.UserName == tag,
                u => new { u.Age, u.IsActive, u.CreatedAt });
            Assert.True(updateResult.IsSuccess, $"{dbName} client update failed: {updateResult.Message}");

            var page = client.Query<PerfUser>(u => u.UserName == tag)
                .OrderBy<PerfUser>(u => u.Id)
                .ToPage<PerfUser>(new PageModel { PageId = 1, PageSize = 10 });
            Assert.Single(page.list);
            Assert.Equal(41, page.list[0].Age);

            var deleteResult = client.Delete<PerfUser>(u => u.UserName == tag);
            Assert.True(deleteResult.IsSuccess, $"{dbName} client delete failed: {deleteResult.Message}");
            Assert.Equal(0, client.Count<PerfUser>(u => u.UserName == tag));
        }

        [Theory]
        [MemberData(nameof(GetDatabases))]
        public void TransactionRollback_DoesNotPersistInsertAcrossDatabases(string dbName)
        {
            if (!ShouldRunDbIntegration()) return;

            var tag = NewTag("rollback");
            using (var db = new DataContext(dbName))
            {
                db.BeginTrans();
                var addResult = db.Add(CreateUser(tag, 27));
                Assert.True(addResult.WriteReturn.IsSuccess, $"{dbName} transactional add failed: {addResult.WriteReturn.Message}");
                db.RollbackTrans();
            }

            var count = FastRead.Use(dbName).Query<PerfUser>(u => u.UserName == tag).ToCount();
            Assert.Equal(0, count);
        }

        [Theory]
        [MemberData(nameof(GetDatabases))]
        public void QueryShapes_CountTakePageEmpty_WorkAcrossDatabases(string dbName)
        {
            if (!ShouldRunDbIntegration()) return;

            var tag = NewTag("shape");
            for (var i = 0; i < 5; i++)
            {
                var addResult = FastWrite.Add(CreateUser($"{tag}_{i}", 30 + i), key: dbName);
                Assert.True(addResult.IsSuccess, $"{dbName} seed add failed: {addResult.Message}");
            }

            var query = FastRead.Use(dbName).Query<PerfUser>(u => u.UserName.Contains(tag));
            Assert.Equal(5, query.ToCount());

            var takeQuery = FastRead.Use(dbName)
                .Query<PerfUser>(u => u.UserName.Contains(tag))
                .OrderBy<PerfUser>(u => u.Id)
                .Take(2);
            var take = takeQuery.ToList<PerfUser>();
            Assert.True(take.Count == 2,
                string.Format("{0} Take(2) 应返回 2 行，实际 {1} 行（TotalCount={2}）",
                    dbName, take.Count, query.ToCount()));

            var page = FastRead.Use(dbName)
                .Query<PerfUser>(u => u.UserName.Contains(tag))
                .OrderBy<PerfUser>(u => u.Id)
                .ToPage<PerfUser>(new PageModel { PageId = 2, PageSize = 2 });
            Assert.NotNull(page.list);
            Assert.True(page.list.Count == 2,
                string.Format("{0} 第 2 页应返回 2 行，实际 {1} 行；TotalRecord={2}；SQL: {3}",
                    dbName, page.list.Count, page.pModel.TotalRecord, page.Sql));
            Assert.True(page.pModel.TotalRecord >= 5);

            var empty = FastRead.Use(dbName).Query<PerfUser>(u => u.UserName == $"missing_{tag}").ToList<PerfUser>();
            Assert.Empty(empty);

            var deleteResult = FastWrite.Delete<PerfUser>(u => u.UserName.Contains(tag), key: dbName);
            Assert.True(deleteResult.IsSuccess, $"{dbName} cleanup delete failed: {deleteResult.Message}");
        }

        private static bool ShouldRunDbIntegration()
        {
            return string.Equals(Environment.GetEnvironmentVariable("FASTDATA_RUN_DB_INTEGRATION"), "true", StringComparison.OrdinalIgnoreCase);
        }

        private static string NewTag(string prefix)
        {
            return $"rel_{prefix}_{Guid.NewGuid():N}".Substring(0, 30);
        }

        private static PerfUser CreateUser(string userName, int age)
        {
            return new PerfUser
            {
                UserName = userName,
                Email = $"{userName}@example.com",
                Age = age,
                IsActive = true,
                CreatedAt = DateTime.Now
            };
        }
    }
}
