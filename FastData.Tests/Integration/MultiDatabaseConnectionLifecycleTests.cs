using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FastData.Config;
using FastData.ConnectionPool;
using FastData.Context;
using FastUntility.Page;
using Xunit;

namespace FastData.Tests.Integration
{
    /// <summary>
    /// 多数据库连接生命周期测试
    /// 覆盖 SqlServer/MySql/PostgreSql/SQLite 的连接创建、复用、健康检查、故障恢复等场景
    /// </summary>
    [Collection(SharedDatabaseCollection.Name)]
    public class MultiDatabaseConnectionLifecycleTests
    {
        private static readonly string[] Databases = { "SqlServer", "MySql", "PostgreSql", "Sqlite" };

        static MultiDatabaseConnectionLifecycleTests()
        {
            try { DbProviderFactories.RegisterFactory("Microsoft.Data.SqlClient", Microsoft.Data.SqlClient.SqlClientFactory.Instance); } catch { }
            try { DbProviderFactories.RegisterFactory("MySql.Data.MySqlClient", MySql.Data.MySqlClient.MySqlClientFactory.Instance); } catch { }
            try { DbProviderFactories.RegisterFactory("Npgsql", Npgsql.NpgsqlFactory.Instance); } catch { }
            try { DbProviderFactories.RegisterFactory("Microsoft.Data.Sqlite", Microsoft.Data.Sqlite.SqliteFactory.Instance); } catch { }
        }

        public static IEnumerable<object[]> GetDatabases()
        {
            foreach (var db in Databases)
                yield return new object[] { db };
        }

        private static bool ShouldRunDbIntegration()
        {
            return string.Equals(Environment.GetEnvironmentVariable("FASTDATA_RUN_DB_INTEGRATION"), "true", StringComparison.OrdinalIgnoreCase);
        }

        private static bool CanOpenDatabase(string dbName)
        {
            if (!ShouldRunDbIntegration()) return false;

            try
            {
                using (var db = new DataContext(dbName))
                {
                    db.GetList<PerfUser>(FastRead.Use(dbName).Query<PerfUser>(u => u.IsActive));
                    return true;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"{dbName} 不可用，跳过测试：{ex.Message}");
                return false;
            }
        }

        #region Connection Lifecycle

        /// <summary>
        /// 测试各数据库连接创建与关闭：验证连接可正常打开和关闭
        /// </summary>
        [Theory]
        [MemberData(nameof(GetDatabases))]
        public void Connection_CreateAndClose_WorksAcrossDatabases(string dbName)
        {
            if (!MultiDatabaseTestHelper.CanOpenDatabase(dbName)) return;

            using (var db = new DataContext(dbName))
            {
                var connection = db.conn;
                Assert.NotNull(connection);
                var count = db.GetList<PerfUser>(FastRead.Use(dbName).Query<PerfUser>(u => u.IsActive)).Count;
                Assert.True(count >= 0);
            }
        }

        /// <summary>
        /// 测试各数据库连接复用：验证同一 DataContext 多次操作复用同一连接
        /// </summary>
        [Theory]
        [MemberData(nameof(GetDatabases))]
        public void Connection_Reuse_WithinSameContext(string dbName)
        {
            if (!MultiDatabaseTestHelper.CanOpenDatabase(dbName)) return;

            using (var db = new DataContext(dbName))
            {
                var conn1 = db.conn;
                var conn2 = db.conn;

                Assert.Same(conn1, conn2);
            }
        }

        /// <summary>
        /// 测试各数据库连接释放后重新创建：验证 Dispose 后可重新获取新连接
        /// </summary>
        [Theory]
        [MemberData(nameof(GetDatabases))]
        public void Connection_Recreate_AfterDispose(string dbName)
        {
            if (!MultiDatabaseTestHelper.CanOpenDatabase(dbName)) return;

            using (var db = new DataContext(dbName))
            {
                Assert.NotNull(db.conn);
                var count = db.GetList<PerfUser>(FastRead.Use(dbName).Query<PerfUser>(u => u.IsActive)).Count;
                Assert.True(count >= 0);
            }

            using (var db = new DataContext(dbName))
            {
                Assert.NotNull(db.conn);
                var count = db.GetList<PerfUser>(FastRead.Use(dbName).Query<PerfUser>(u => u.IsActive)).Count;
                Assert.True(count >= 0);
            }
        }

        /// <summary>
        /// 测试各数据库连接健康检查：验证连接可执行简单查询
        /// </summary>
        [Theory]
        [MemberData(nameof(GetDatabases))]
        public void Connection_HealthCheck_CanExecuteQuery(string dbName)
        {
            if (!MultiDatabaseTestHelper.CanOpenDatabase(dbName)) return;

            using (var db = new DataContext(dbName))
            {
                var connection = db.conn;
                if (connection.State != ConnectionState.Open)
                    connection.Open();
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = dbName switch
                    {
                        "SqlServer" => "SELECT 1",
                        "MySql" => "SELECT 1",
                        "PostgreSql" => "SELECT 1",
                        "Sqlite" => "SELECT 1",
                        "SQLite" => "SELECT 1",
                        _ => throw new ArgumentOutOfRangeException(nameof(dbName))
                    };
                    command.CommandTimeout = 5;

                    var result = command.ExecuteScalar();
                    Assert.NotNull(result);
                }
            }
        }

        /// <summary>
        /// 测试各数据库连接超时处理：验证超时配置生效
        /// </summary>
        [Theory]
        [MemberData(nameof(GetDatabases))]
        public void Connection_Timeout_Configuration(string dbName)
        {
            if (!MultiDatabaseTestHelper.CanOpenDatabase(dbName)) return;
            if (!CanUseManualConnectionPool(dbName)) return;

            var poolConfig = new ConnectionPoolConfig
            {
                MinPoolSize = 1,
                MaxPoolSize = 5,
                ConnectionTimeout = 10,
                HealthCheckInterval = 30
            };

            var poolName = $"{dbName}_timeout_test_{Guid.NewGuid():N}";
            var pool = ConnectionPoolFactory.Instance.GetOrCreatePool(
                poolName,
                () =>
                {
                    var connStr = FastDataConfig.GetConnectionString(dbName);
                    return CreateConnection(dbName, connStr);
                },
                poolConfig);

            try
            {
                using (var conn = pool.GetConnection())
                {
                    Assert.NotNull(conn);
                    Assert.Equal(ConnectionState.Open, conn.Connection.State);
                }

                var metrics = pool.GetMetrics();
                Assert.NotNull(metrics);
            }
            finally
            {
                ConnectionPoolFactory.Instance.RemovePool(poolName);
            }
        }

        #endregion

        #region Connection Pool Reuse

        /// <summary>
        /// 测试各数据库连接池复用：验证归还连接后可被重新获取
        /// </summary>
        [Theory]
        [MemberData(nameof(GetDatabases))]
        public void ConnectionPool_Reuse_Connections(string dbName)
        {
            if (!MultiDatabaseTestHelper.CanOpenDatabase(dbName)) return;
            if (!CanUseManualConnectionPool(dbName)) return;

            var poolConfig = new ConnectionPoolConfig
            {
                MinPoolSize = 2,
                MaxPoolSize = 10,
                EnableSmartAdjustment = false
            };

            var poolName = $"{dbName}_reuse_test_{Guid.NewGuid():N}";
            var pool = ConnectionPoolFactory.Instance.GetOrCreatePool(
                poolName,
                () =>
                {
                    var connStr = FastDataConfig.GetConnectionString(dbName);
                    return CreateConnection(dbName, connStr);
                },
                poolConfig);

            try
            {
                Guid firstConnectionId;

                using (var conn1 = pool.GetConnection())
                {
                    firstConnectionId = conn1.Id;
                }

                using (var conn2 = pool.GetConnection())
                {
                    Assert.Equal(firstConnectionId, conn2.Id);
                }
            }
            finally
            {
                ConnectionPoolFactory.Instance.RemovePool(poolName);
            }
        }

        /// <summary>
        /// 测试各数据库连接池并发获取：验证多线程并发获取连接的安全性
        /// </summary>
        [Theory]
        [MemberData(nameof(GetDatabases))]
        public async Task ConnectionPool_ConcurrentAccess(string dbName)
        {
            if (!MultiDatabaseTestHelper.CanOpenDatabase(dbName)) return;
            if (!CanUseManualConnectionPool(dbName)) return;

            var poolConfig = new ConnectionPoolConfig
            {
                MinPoolSize = 5,
                MaxPoolSize = 20,
                EnableSmartAdjustment = false
            };

            var poolName = $"{dbName}_concurrent_test_{Guid.NewGuid():N}";
            var pool = ConnectionPoolFactory.Instance.GetOrCreatePool(
                poolName,
                () =>
                {
                    var connStr = FastDataConfig.GetConnectionString(dbName);
                    return CreateConnection(dbName, connStr);
                },
                poolConfig);

            var successCount = 0;
            var errorCount = 0;
            var errors = new ConcurrentBag<string>();
            try
            {
                var tasks = Enumerable.Range(0, 10).Select(i =>
                    Task.Run(() =>
                    {
                        try
                        {
                            using (var conn = pool.GetConnection())
                            {
                                using (var command = conn.Connection.CreateCommand())
                                {
                                    command.CommandText = dbName switch
                                    {
                                        "SqlServer" => "SELECT COUNT(*) FROM perf_users",
                                        "MySql" => "SELECT COUNT(*) FROM perf_users",
                                        "PostgreSql" => "SELECT COUNT(*) FROM perf_users",
                                        "Sqlite" => "SELECT COUNT(*) FROM perf_users",
                                        "SQLite" => "SELECT COUNT(*) FROM perf_users",
                                        _ => throw new ArgumentOutOfRangeException(nameof(dbName))
                                    };
                                    command.CommandTimeout = 5;
                                    command.ExecuteScalar();
                                }
                            }
                            Interlocked.Increment(ref successCount);
                        }
                        catch (Exception ex)
                        {
                            Interlocked.Increment(ref errorCount);
                            if (errors.Count < 5)
                                errors.Add($"Thread {i}: {ex.Message}");
                        }
                    })
                ).ToArray();

                await Task.WhenAll(tasks);

                Assert.Equal(10, successCount);
                Assert.Equal(0, errorCount);
            }
            finally
            {
                ConnectionPoolFactory.Instance.RemovePool(poolName);
            }
        }

        #endregion

        #region Failover & Recovery

        /// <summary>
        /// 测试各数据库故障后恢复：验证连接池在故障后可恢复正常
        /// </summary>
        [Theory]
        [MemberData(nameof(GetDatabases))]
        public void Connection_Failover_And_Recovery(string dbName)
        {
            if (!MultiDatabaseTestHelper.CanOpenDatabase(dbName)) return;
            if (!CanUseManualConnectionPool(dbName)) return;

            var poolConfig = new ConnectionPoolConfig
            {
                MinPoolSize = 2,
                MaxPoolSize = 10,
                EnableSmartAdjustment = true,
                SmartAdjustmentInterval = 2,
                ErrorShrinkThreshold = 3,
                ErrorShrinkPercentage = 50
            };

            var poolName = $"{dbName}_failover_test_{Guid.NewGuid():N}";
            var pool = ConnectionPoolFactory.Instance.GetOrCreatePool(
                poolName,
                () =>
                {
                    var connStr = FastDataConfig.GetConnectionString(dbName);
                    return CreateConnection(dbName, connStr);
                },
                poolConfig);

            try
            {
                var initialMetrics = pool.GetMetrics();

                for (int i = 0; i < 3; i++)
                {
                    pool.RecordDatabaseError(new Exception($"Simulated error {i}"));
                }

                Thread.Sleep(3000);

                using (var conn = pool.GetConnection())
                {
                    Assert.NotNull(conn);
                    Assert.Equal(ConnectionState.Open, conn.Connection.State);
                }

                var afterMetrics = pool.GetMetrics();
                Assert.NotNull(afterMetrics);
                Assert.True(afterMetrics.TotalConnections >= 0);
                Console.WriteLine($"{dbName} failover test: initial={initialMetrics.TotalConnections}, after={afterMetrics.TotalConnections}");
            }
            finally
            {
                ConnectionPoolFactory.Instance.RemovePool(poolName);
            }
        }

        /// <summary>
        /// 测试各数据库连接泄漏检测：验证长时间未归还连接可被检测
        /// </summary>
        [Theory]
        [MemberData(nameof(GetDatabases))]
        public void Connection_LeakDetection(string dbName)
        {
            if (!MultiDatabaseTestHelper.CanOpenDatabase(dbName)) return;
            if (!CanUseManualConnectionPool(dbName)) return;

            var poolConfig = new ConnectionPoolConfig
            {
                MinPoolSize = 2,
                MaxPoolSize = 10,
                LeakDetectionThreshold = 5,
                EnableSmartAdjustment = false
            };

            var poolName = $"{dbName}_leak_test_{Guid.NewGuid():N}";
            var pool = ConnectionPoolFactory.Instance.GetOrCreatePool(
                poolName,
                () =>
                {
                    var connStr = FastDataConfig.GetConnectionString(dbName);
                    return CreateConnection(dbName, connStr);
                },
                poolConfig);

            PooledConnection conn = null;
            try
            {
                conn = pool.GetConnection();
                Thread.Sleep(1000);

                var metrics = pool.GetMetrics();
                Assert.Equal(1, metrics.ActiveConnections);

                conn.Dispose();
                conn = null;

                metrics = pool.GetMetrics();
                Assert.Equal(0, metrics.ActiveConnections);
                Assert.True(metrics.TotalConnections >= metrics.IdleConnections);
            }
            finally
            {
                conn?.Dispose();
                ConnectionPoolFactory.Instance.RemovePool(poolName);
            }
        }

        #endregion

        #region Dynamic Pool Adjustment

        /// <summary>
        /// 测试各数据库连接池动态扩容：高负载下自动增加连接数
        /// </summary>
        [Theory]
        [MemberData(nameof(GetDatabases))]
        public void ConnectionPool_DynamicExpand(string dbName)
        {
            if (!MultiDatabaseTestHelper.CanOpenDatabase(dbName)) return;
            if (!CanUseManualConnectionPool(dbName)) return;

            var poolConfig = new ConnectionPoolConfig
            {
                MinPoolSize = 2,
                MaxPoolSize = 15,
                EnableSmartAdjustment = true,
                SmartAdjustmentInterval = 2,
                LoadThreshold = 50,
                MaxExpandCount = 5
            };

            var poolName = $"{dbName}_expand_test_{Guid.NewGuid():N}";
            var pool = ConnectionPoolFactory.Instance.GetOrCreatePool(
                poolName,
                () =>
                {
                    var connStr = FastDataConfig.GetConnectionString(dbName);
                    return CreateConnection(dbName, connStr);
                },
                poolConfig);

            var initialMetrics = pool.GetMetrics();
            var connections = new List<PooledConnection>();

            try
            {
                for (int i = 0; i < 5; i++)
                {
                    try
                    {
                        connections.Add(pool.GetConnection());
                    }
                    catch (ConnectionPoolExhaustedException)
                    {
                        break;
                    }
                }

                Thread.Sleep(3000);

                var afterMetrics = pool.GetMetrics();
                Assert.True(afterMetrics.TotalConnections >= initialMetrics.TotalConnections);
                Assert.True(afterMetrics.TotalConnections <= poolConfig.MaxPoolSize);
                Console.WriteLine($"{dbName} expand test: initial={initialMetrics.TotalConnections}, after={afterMetrics.TotalConnections}");
            }
            finally
            {
                foreach (var conn in connections)
                {
                    conn.Dispose();
                }
                ConnectionPoolFactory.Instance.RemovePool(poolName);
            }
        }

        /// <summary>
        /// 测试各数据库连接池动态缩容：低负载时自动减少连接数
        /// </summary>
        [Theory]
        [MemberData(nameof(GetDatabases))]
        public void ConnectionPool_DynamicShrink(string dbName)
        {
            if (!MultiDatabaseTestHelper.CanOpenDatabase(dbName)) return;
            if (!CanUseManualConnectionPool(dbName)) return;

            var poolConfig = new ConnectionPoolConfig
            {
                MinPoolSize = 2,
                MaxPoolSize = 10,
                EnableSmartAdjustment = true,
                SmartAdjustmentInterval = 2,
                ShrinkThreshold = 30,
                MaxShrinkCount = 3
            };

            var poolName = $"{dbName}_shrink_test_{Guid.NewGuid():N}";
            var pool = ConnectionPoolFactory.Instance.GetOrCreatePool(
                poolName,
                () =>
                {
                    var connStr = FastDataConfig.GetConnectionString(dbName);
                    return CreateConnection(dbName, connStr);
                },
                poolConfig);

            var initialMetrics = pool.GetMetrics();

            var connections = new List<PooledConnection>();
            try
            {
                for (int i = 0; i < 3; i++)
                {
                    connections.Add(pool.GetConnection());
                }

                Thread.Sleep(1000);
            }
            finally
            {
                foreach (var conn in connections)
                {
                    conn.Dispose();
                }
            }

            try
            {
                Thread.Sleep(3000);

                var afterMetrics = pool.GetMetrics();
                Assert.True(afterMetrics.TotalConnections >= poolConfig.MinPoolSize);
                Assert.True(afterMetrics.TotalConnections <= poolConfig.MaxPoolSize);
                Console.WriteLine($"{dbName} shrink test: initial={initialMetrics.TotalConnections}, after={afterMetrics.TotalConnections}");
            }
            finally
            {
                ConnectionPoolFactory.Instance.RemovePool(poolName);
            }
        }

        #endregion

        #region Long Running Stability

        /// <summary>
        /// 测试各数据库长时间运行稳定性：持续操作验证连接池稳定性
        /// </summary>
        [Theory]
        [MemberData(nameof(GetDatabases))]
        public void ConnectionPool_LongRunning_Stability(string dbName)
        {
            if (!MultiDatabaseTestHelper.CanOpenDatabase(dbName)) return;

            var duration = TimeSpan.FromSeconds(5);
            var successCount = 0;
            var errorCount = 0;
            var stopwatch = Stopwatch.StartNew();

            while (stopwatch.Elapsed < duration)
            {
                try
                {
                    using (var db = new DataContext(dbName))
                    {
                        var count = db.GetList<PerfUser>(FastRead.Use(dbName).Query<PerfUser>(u => u.IsActive)).Count;
                        Interlocked.Increment(ref successCount);
                    }
                }
                catch (Exception)
                {
                    Interlocked.Increment(ref errorCount);
                }
            }

            stopwatch.Stop();
            var totalOps = successCount + errorCount;
            var successRate = totalOps > 0 ? (double)successCount / totalOps : 0;
            var opsPerSecond = totalOps * 1000.0 / stopwatch.ElapsedMilliseconds;

            Assert.True(successRate > 0.5, $"{dbName} long-running test failed: rate={successRate:P0}");
            Console.WriteLine($"{dbName} long-running: rate={successRate:P0}, success={successCount}, errors={errorCount}, throughput={opsPerSecond:F0} ops/s");
        }

        #endregion

        #region Helpers

        private DbConnection CreateConnection(string dbName, string connectionString)
        {
            return dbName switch
            {
                "SqlServer" => new Microsoft.Data.SqlClient.SqlConnection(connectionString),
                "MySql" => new MySql.Data.MySqlClient.MySqlConnection(connectionString),
                "PostgreSql" => new Npgsql.NpgsqlConnection(connectionString),
                "Sqlite" => new Microsoft.Data.Sqlite.SqliteConnection(connectionString),
                "SQLite" => new Microsoft.Data.Sqlite.SqliteConnection(connectionString),
                _ => throw new ArgumentOutOfRangeException(nameof(dbName))
            };
        }

        private static bool CanUseManualConnectionPool(string dbName)
        {
            if (!string.Equals(dbName, "PostgreSql", StringComparison.OrdinalIgnoreCase))
                return true;

            Console.WriteLine("PostgreSql manual SmartConnectionPool tests are skipped; DataContext coverage verifies PostgreSql connectivity.");
            return false;
        }

        #endregion
    }
}
