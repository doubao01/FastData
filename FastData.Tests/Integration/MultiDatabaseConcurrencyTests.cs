using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FastData.Config;
using FastData.ConnectionPool;
using Microsoft.Data.SqlClient;
using Xunit;

namespace FastData.Tests.Integration
{
    /// <summary>
    /// 多数据库并发测试
    /// </summary>
    [Collection(SharedDatabaseCollection.Name)]
    public class MultiDatabaseConcurrencyTests
    {
        static MultiDatabaseConcurrencyTests()
        {
            MultiDatabaseTestHelper.RegisterProviders();
        }

        public static IEnumerable<object[]> GetDatabases() => MultiDatabaseTestHelper.GetRelationalAndSqliteDatabases();

        [Theory]
        [MemberData(nameof(GetDatabases))]
        public async Task Concurrency_ReadWrite(string dbName)
        {
            if (!MultiDatabaseTestHelper.CanOpenDatabase(dbName)) return;
            var threadCount = 20;
            var opsPerThread = 10;
            var success = 0;
            var errors = 0;
            var errorMessages = new ConcurrentBag<string>();
            var tags = new ConcurrentBag<string>();

            var tasks = Enumerable.Range(0, threadCount).Select(i => Task.Run(() =>
            {
                for (int j = 0; j < opsPerThread; j++)
                {
                    try
                    {
                        var tag = MultiDatabaseTestHelper.NewTag($"rw_{i}_{j}");
                        tags.Add(tag);
                        var addResult = FastWrite.Add(MultiDatabaseTestHelper.CreateUser(tag, 25), key: dbName);
                        if (!addResult.IsSuccess)
                            throw new InvalidOperationException(addResult.Message);
                        FastRead.Use(dbName).Query<PerfUser>(u => u.IsActive).ToCount();
                        Interlocked.Increment(ref success);
                    }
                    catch (Exception ex)
                    {
                        Interlocked.Increment(ref errors);
                        if (errorMessages.Count < 5)
                            errorMessages.Add(ex.Message);
                    }
                }
            })).ToArray();

            await Task.WhenAll(tasks);
            Assert.Equal(threadCount * opsPerThread, success);
            Assert.Equal(0, errors);

            foreach (var tag in tags)
                FastWrite.Delete<PerfUser>(u => u.UserName == tag, key: dbName);
        }

        [Theory]
        [MemberData(nameof(GetDatabases))]
        public async Task Concurrency_BatchWrite(string dbName)
        {
            if (!MultiDatabaseTestHelper.CanOpenDatabase(dbName)) return;
            var threadCount = 10;
            var success = 0;
            var errors = 0;
            var errorMessages = new ConcurrentBag<string>();
            var allTags = new ConcurrentBag<string>();

            var tasks = Enumerable.Range(0, threadCount).Select(i => Task.Run(() =>
            {
                try
                {
                    var tag = MultiDatabaseTestHelper.NewTag($"batch_{i}");
                    var users = Enumerable.Range(0, 5).Select(j =>
                    {
                        allTags.Add($"{tag}_{j}");
                        return MultiDatabaseTestHelper.CreateUser($"{tag}_{j}", 20 + j);
                    }).ToList();

                    var r = FastWrite.AddList(users, key: dbName);
                    if (!r.IsSuccess)
                        throw new InvalidOperationException(r.Message);
                    Interlocked.Increment(ref success);
                }
                catch (Exception ex)
                {
                    Interlocked.Increment(ref errors);
                    if (errorMessages.Count < 5)
                        errorMessages.Add(ex.Message);
                }
            })).ToArray();

            await Task.WhenAll(tasks);
            Assert.Equal(threadCount, success);
            Assert.Equal(0, errors);

            foreach (var t in allTags)
                FastWrite.Delete<PerfUser>(u => u.UserName == t, key: dbName);
        }

        [Theory]
        [MemberData(nameof(GetDatabases))]
        public async Task Concurrency_ConnectionPool(string dbName)
        {
            if (!MultiDatabaseTestHelper.CanOpenDatabase(dbName)) return;
            if (string.Equals(dbName, "PostgreSql", StringComparison.OrdinalIgnoreCase)) return;
            var cfg = new ConnectionPoolConfig { MinPoolSize = 5, MaxPoolSize = 20 };
            var poolName = $"{dbName}_conc_{Guid.NewGuid():N}";
            var pool = ConnectionPoolFactory.Instance.GetOrCreatePool(
                poolName,
                () =>
                {
                    var cs = FastDataConfig.GetConnectionString(dbName);
                    return dbName switch
                    {
                        "SqlServer" => new SqlConnection(cs),
                        "MySql" => new MySql.Data.MySqlClient.MySqlConnection(cs),
                        "PostgreSql" => new Npgsql.NpgsqlConnection(cs),
                        "Sqlite" => new Microsoft.Data.Sqlite.SqliteConnection(cs),
                        "SQLite" => new Microsoft.Data.Sqlite.SqliteConnection(cs),
                        _ => throw new ArgumentOutOfRangeException()
                    };
                }, cfg);

            var success = 0;
            var errors = 0;
            try
            {
                var tasks = Enumerable.Range(0, 30).Select(i => Task.Run(() =>
                {
                    try
                    {
                        using (var c = pool.GetConnection())
                        using (var cmd = c.Connection.CreateCommand())
                        {
                            cmd.CommandText = "SELECT 1";
                            cmd.ExecuteScalar();
                        }
                        Interlocked.Increment(ref success);
                    }
                    catch
                    {
                        Interlocked.Increment(ref errors);
                    }
                })).ToArray();

                await Task.WhenAll(tasks);
                Assert.Equal(30, success);
                Assert.Equal(0, errors);
            }
            finally
            {
                ConnectionPoolFactory.Instance.RemovePool(poolName);
            }
        }
    }
}
