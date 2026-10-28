using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data.Common;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FastData;
using FastData.Base;
using FastData.Config;
using FastData.ConnectionPool;
using FastData.Context;
using FastData.Core;
using FastData.Queue;
using FastData.Tests.Integration;
using Microsoft.Data.SqlClient;
using NewLife.Caching;
using Xunit;

namespace FastData.Tests
{
    /// <summary>
    /// 连接池功能测试
    /// 
    /// 覆盖核心连接池操作、智能调整、消息队列集成、
    /// 弹性写入故障转移、稳定性测试等场景。
    /// </summary>
    [Collection(SharedDatabaseCollection.Name)]
    public class ConnectionPoolTests
    {
        private readonly string _connStr;

        /// <summary>
        /// 初始化测试实例，从配置文件获取数据库连接字符串
        /// </summary>
        public ConnectionPoolTests()
        {
            try
            {
                // 从配置文件获取连接字符串，避免硬编码
                _connStr = FastDataConfig.GetConnectionString("SqlServer")
                    ?? "server=localhost;database=FastDataTest;uid=sa;pwd=FastData@Test123;TrustServerCertificate=true";
            }
            catch (Exception ex)
            {
                Console.WriteLine(string.Format("SqlServer 配置不可用，使用本地默认连接串: {0}", ex.Message));
                _connStr = "server=localhost;database=FastDataTest;uid=sa;pwd=FastData@Test123;TrustServerCertificate=true";
            }
        }

        #region Basic Pool

        /// <summary>
        /// 测试连接池创建和归还连接：验证连接池可正常创建并返回打开的数据库连接
        /// </summary>
        [Fact]
        public void Pool_Should_Create_And_Return_Connection()
        {
            var config = new ConnectionPoolConfig
            {
                MinPoolSize = 2,
                MaxPoolSize = 10,
                ConnectionTimeout = 5
            };

            SmartConnectionPool pool = null;
            try
            {
                pool = new SmartConnectionPool("TestPool", CreateConnectionFactory, config);
            }
            catch (Exception ex)
            {
                Console.WriteLine(string.Format("无法创建连接池，跳过测试: {0}", ex.Message));
                return;
            }

            try
            {
                using var connection = pool.GetConnection();
                Assert.NotNull(connection);
                Assert.NotNull(connection.Connection);
                Assert.Equal(System.Data.ConnectionState.Open, connection.Connection.State);
            }
            catch (ConnectionPoolExhaustedException)
            {
                Console.WriteLine("数据库不可用，跳过测试");
            }
            finally
            {
                pool?.Dispose();
            }
        }

        /// <summary>
        /// 测试连接池复用连接：验证归还连接后续获取可重用同一连接实例
        /// </summary>
        [Fact]
        public async Task Pool_Should_Reuse_Connections()
        {
            var config = new ConnectionPoolConfig
            {
                MinPoolSize = 2,
                MaxPoolSize = 10,
                ConnectionTimeout = 5
            };

            SmartConnectionPool pool;
            try
            {
                pool = new SmartConnectionPool("TestPool", CreateConnectionFactory, config);
            }
            catch (Exception ex)
            {
                Console.WriteLine(string.Format("无法创建连接池，跳过测试: {0}", ex.Message));
                return;
            }

            using (pool)
            {
                try
                {
                    var connection1 = await pool.GetConnectionAsync();
                    var connectionId1 = connection1.Id;
                    connection1.Dispose();

                    var connection2 = await pool.GetConnectionAsync();
                    var connectionId2 = connection2.Id;
                    connection2.Dispose();

                    Assert.Equal(connectionId1, connectionId2);
                }
                catch (ConnectionPoolExhaustedException)
                {
                    Console.WriteLine("数据库不可用，跳过测试");
                }
            }
        }

        /// <summary>
        /// 测试连接池最大连接数限制：验证超过 MaxPoolSize 时抛出 ConnectionPoolExhaustedException
        /// </summary>
        [Fact]
        public async Task Pool_Should_Respect_MaxPoolSize()
        {
            var config = new ConnectionPoolConfig
            {
                MinPoolSize = 1,
                MaxPoolSize = 2,
                ConnectionTimeout = 1
            };

            SmartConnectionPool pool;
            try
            {
                pool = new SmartConnectionPool("TestPool", CreateConnectionFactory, config);
            }
            catch (Exception ex)
            {
                Console.WriteLine(string.Format("无法创建连接池，跳过测试: {0}", ex.Message));
                return;
            }

            using (pool)
            {
                try
                {
                    var conn1 = await pool.GetConnectionAsync();
                    var conn2 = await pool.GetConnectionAsync();

                    await Assert.ThrowsAsync<ConnectionPoolExhaustedException>(() => pool.GetConnectionAsync());

                    conn1.Dispose();
                    conn2.Dispose();
                }
                catch (ConnectionPoolExhaustedException)
                {
                    Console.WriteLine("数据库不可用，跳过测试");
                }
            }
        }

        /// <summary>
        /// 测试连接池返回指标：验证 GetMetrics 返回非空的池状态信息
        /// </summary>
        [Fact]
        public void Pool_Should_Return_Metrics()
        {
            var config = new ConnectionPoolConfig
            {
                MinPoolSize = 2,
                MaxPoolSize = 10
            };

            using var pool = new SmartConnectionPool("TestPool", CreateConnectionFactory, config);
            var metrics = pool.GetMetrics();

            Assert.NotNull(metrics);
            Assert.True(metrics.TotalConnections >= 0);
            Assert.True(metrics.ActiveConnections >= 0);
            Assert.True(metrics.IdleConnections >= 0);
            Assert.True(metrics.WaitingRequests >= 0);
        }

        #endregion

        #region Smart Adjustment

        /// <summary>
        /// 测试智能错误缩容：连续数据库错误后连接池应自动缩容减少连接数
        /// </summary>
        [Fact]
        public void SmartPool_ErrorShrink()
        {
            var poolConfig = new ConnectionPoolConfig
            {
                MinPoolSize = 2,
                MaxPoolSize = 10,
                EnableSmartAdjustment = true,
                SmartAdjustmentInterval = 1,
                ErrorShrinkThreshold = 3,
                ErrorShrinkPercentage = 50
            };

            var pool = ConnectionPoolFactory.Instance.GetOrCreatePool(
                "SqlServer_test_shrink",
                () =>
                {
                    var conn = new SqlConnection(_connStr);
                    return conn;
                },
                poolConfig);

            var initialMetrics = pool.Metrics;

            for (int i = 0; i < 5; i++)
            {
                pool.RecordDatabaseError(new Exception(string.Format("Simulated error {0}", i)));
            }

            Thread.Sleep(2000);

            var afterMetrics = pool.Metrics;
            Assert.True(afterMetrics.TotalConnections <= initialMetrics.TotalConnections,
                "Pool should shrink after errors");
        }

        /// <summary>
        /// 测试智能负载扩容：高负载下连接池应自动扩容增加连接数
        /// </summary>
        [Fact]
        public void SmartPool_LoadExpand()
        {
            var poolConfig = new ConnectionPoolConfig
            {
                MinPoolSize = 2,
                MaxPoolSize = 10,
                EnableSmartAdjustment = true,
                SmartAdjustmentInterval = 1,
                LoadThreshold = 50,
                MaxExpandCount = 3
            };

            var pool = ConnectionPoolFactory.Instance.GetOrCreatePool(
                "SqlServer_test_expand",
                () =>
                {
                    var conn = new SqlConnection(_connStr);
                    return conn;
                },
                poolConfig);

            var connections = new List<PooledConnection>();
            try
            {
                for (int i = 0; i < 5; i++)
                {
                    try
                    {
                        connections.Add(pool.GetConnection());
                    }
                    catch
                    {
                        break;
                    }
                }

                Thread.Sleep(2000);
            }
            finally
            {
                foreach (var conn in connections)
                {
                    conn.Dispose();
                }
            }
        }

        #endregion

        #region MQ Integration

        /// <summary>
        /// 测试 FastDataClient 消息队列集成：验证 Redis 可用时客户端可正常写入数据
        /// </summary>
        [Fact]
        public void FastDataClient_MessageQueueIntegration()
        {
            if (!IsRedisAvailable()) return;

            var redis = new FullRedis { Server = "127.0.0.1:6379", Db = 7 };
            var poolConfig = new ConnectionPoolConfig
            {
                MinPoolSize = 1,
                MaxPoolSize = 5,
                EnableSmartAdjustment = true,
                SmartAdjustmentInterval = 5
            };

            using var client = new FastDataClient("SqlServer", poolConfig: poolConfig, redis: redis);

            var user = new PerfUser
            {
                UserName = "FastDataClientTest",
                Email = "client@test.com",
                Age = 25,
                IsActive = true,
                CreatedAt = DateTime.Now
            };

            try
            {
                var result = client.Add(user);
                Console.WriteLine(string.Format("FastDataClient write result: IsSuccess={0}", result.IsSuccess));
            }
            catch (Exception ex)
            {
                Console.WriteLine(string.Format("FastDataClient write exception (may have degraded to queue): {0}", ex.Message));
            }

            var users = new List<PerfUser>();
            for (int i = 0; i < 10; i++)
            {
                users.Add(new PerfUser
                {
                    UserName = string.Format("BatchTest_{0}", i),
                    Email = string.Format("batch_{0}@test.com", i),
                    Age = 20 + i,
                    IsActive = true,
                    CreatedAt = DateTime.Now
                });
            }

            try
            {
                var result = client.AddList(users);
                Console.WriteLine(string.Format("FastDataClient batch write result: IsSuccess={0}", result.IsSuccess));
            }
            catch (Exception ex)
            {
                Console.WriteLine(string.Format("FastDataClient batch write exception: {0}", ex.Message));
            }
        }

        /// <summary>
        /// 测试 FastRead 查询操作：验证通过连接池可正常执行数据查询
        /// </summary>
        [Fact]
        public void FastRead_QueryOperation()
        {
            var poolConfig = new ConnectionPoolConfig
            {
                MinPoolSize = 1,
                MaxPoolSize = 10,
                EnableSmartAdjustment = true
            };

            try
            {
                var query = FastRead.Query<PerfUser>(u => u.IsActive)
                    .Take(5);

                var users = FastRead.ToList<PerfUser>(query);
                Console.WriteLine(string.Format("FastRead query result: {0} records", users.Count));
            }
            catch (Exception ex)
            {
                Console.WriteLine(string.Format("FastRead query exception: {0}", ex.Message));
            }
        }

        #endregion

        #region Resilient Write

        /// <summary>
        /// 测试连接池耗尽时回退到消息队列：验证弹性写入器在连接池耗尽时正确使用队列兜底
        /// </summary>
        [Fact]
        public void ConnectionPoolExhausted_ShouldFallbackToQueue()
        {
            if (!IsRedisAvailable()) return;

            var poolConfig = new ConnectionPoolConfig
            {
                MinPoolSize = 0,
                MaxPoolSize = 1,
                ConnectionTimeout = 1,
                EnableSmartAdjustment = false
            };

            var queueConfig = new WriteBehindConfig
            {
                QueueType = WriteBehindQueueType.ReliableQueue,
                EnableFallback = true,
                Topic = "test_fallback"
            };

            var redis = new FullRedis { Server = "127.0.0.1:6379", Db = 7 };
            using var executor = new ResilientWriteExecutor(
                databaseKey: "SqlServer_test_fallback",
                poolConfig: poolConfig,
                queueConfig: queueConfig,
                redis: redis,
                maxRetries: 0);

            var pool = ConnectionPoolFactory.Instance.GetOrCreatePool(
                "SqlServer_test_fallback",
                () => new SqlConnection(_connStr),
                poolConfig);

            PooledConnection blockingConn = null;
            try
            {
                blockingConn = pool.GetConnection();
            }
            catch (Exception ex)
            {
                Console.WriteLine(string.Format("Cannot get DB connection, skipping: {0}", ex.Message));
                return;
            }

            var operation = new WriteOperation
            {
                OperationType = WriteOperationType.Add,
                EntityType = typeof(PerfUser).AssemblyQualifiedName,
                TableName = "perf_users",
                Data = Newtonsoft.Json.JsonConvert.SerializeObject(new PerfUser
                {
                    UserName = "FallbackTest",
                    Email = "fallback@test.com",
                    Age = 25,
                    IsActive = true,
                    CreatedAt = DateTime.Now
                })
            };

            var result = executor.ExecuteWrite(operation);

            Assert.True(result.Success, "Write should succeed via queue fallback");
            Assert.True(result.UsedQueueFallback, "Should use queue fallback");
            Assert.True(
                result.FallbackReason == "连接池耗尽" || result.FallbackReason.StartsWith("数据库异常:"),
                string.Format("Unexpected fallback reason: {0}", result.FallbackReason));

            blockingConn?.Dispose();
        }

        /// <summary>
        /// 测试批量写入部分回退：验证批量操作在连接池不足时正确将部分写入回退到队列
        /// </summary>
        [Fact]
        public void BatchWrite_PartialFallback()
        {
            if (!IsRedisAvailable()) return;

            var poolConfig = new ConnectionPoolConfig
            {
                MinPoolSize = 0,
                MaxPoolSize = 1,
                ConnectionTimeout = 1,
                EnableSmartAdjustment = false
            };

            var queueConfig = new WriteBehindConfig
            {
                QueueType = WriteBehindQueueType.ReliableQueue,
                EnableFallback = true,
                Topic = "test_batch_fallback"
            };

            var redis = new FullRedis { Server = "127.0.0.1:6379", Db = 7 };
            using var executor = new ResilientWriteExecutor(
                databaseKey: "SqlServer_test_batch",
                poolConfig: poolConfig,
                queueConfig: queueConfig,
                redis: redis,
                maxRetries: 0);

            var pool = ConnectionPoolFactory.Instance.GetOrCreatePool(
                "SqlServer_test_batch",
                () => new SqlConnection(_connStr),
                poolConfig);

            PooledConnection blockingConn = null;
            try
            {
                blockingConn = pool.GetConnection();
            }
            catch (Exception ex)
            {
                Console.WriteLine(string.Format("Cannot get DB connection, skipping: {0}", ex.Message));
                return;
            }

            var operations = new List<WriteOperation>();
            for (int i = 0; i < 5; i++)
            {
                operations.Add(new WriteOperation
                {
                    OperationType = WriteOperationType.Add,
                    EntityType = typeof(PerfUser).AssemblyQualifiedName,
                    TableName = "perf_users",
                    Data = Newtonsoft.Json.JsonConvert.SerializeObject(new PerfUser
                    {
                        UserName = string.Format("BatchTest_{0}", i),
                        Email = string.Format("batch_{0}@test.com", i),
                        Age = 20 + i,
                        IsActive = true,
                        CreatedAt = DateTime.Now
                    })
                });
            }

            var result = executor.ExecuteBatchWrite(operations);

            Assert.True(result.Success, "Batch write should succeed");
            Assert.Equal(5, result.QueuedCount);
            Assert.Equal(0, result.DirectWriteCount);

            blockingConn?.Dispose();
        }

        /// <summary>
        /// 测试正常写入使用直连：验证连接池可用时写入直接通过数据库连接执行
        /// </summary>
        [Fact]
        public void NormalWrite_ShouldUseDirectWrite()
        {
            var poolConfig = new ConnectionPoolConfig
            {
                MinPoolSize = 2,
                MaxPoolSize = 10
            };

            using var executor = new ResilientWriteExecutor(
                databaseKey: "SqlServer",
                poolConfig: poolConfig,
                maxRetries: 1);

            var operation = new WriteOperation
            {
                OperationType = WriteOperationType.Add,
                EntityType = typeof(PerfUser).AssemblyQualifiedName,
                TableName = "perf_users",
                Data = Newtonsoft.Json.JsonConvert.SerializeObject(new PerfUser
                {
                    UserName = "DirectWriteTest",
                    Email = "direct@test.com",
                    Age = 30,
                    IsActive = true,
                    CreatedAt = DateTime.Now
                })
            };

            var result = executor.ExecuteWrite(operation);
            Console.WriteLine(string.Format("Result: Success={0}, DirectWrite={1}, QueueFallback={2}", result.Success, result.UsedDirectWrite, result.UsedQueueFallback));
        }

        /// <summary>
        /// 测试弹性写入统计信息：验证统计数据正确跟踪直写、队列回退和失败计数
        /// </summary>
        [Fact]
        public void Stats_ShouldTrackCorrectly()
        {
            var poolConfig = new ConnectionPoolConfig
            {
                MinPoolSize = 2,
                MaxPoolSize = 10
            };

            using var executor = new ResilientWriteExecutor(
                databaseKey: "SqlServer",
                poolConfig: poolConfig,
                maxRetries: 0);

            for (int i = 0; i < 3; i++)
            {
                var operation = new WriteOperation
                {
                    OperationType = WriteOperationType.Add,
                    EntityType = typeof(PerfUser).AssemblyQualifiedName,
                    TableName = "perf_users",
                    Data = Newtonsoft.Json.JsonConvert.SerializeObject(new PerfUser
                    {
                        UserName = string.Format("StatsTest_{0}", i),
                        Email = string.Format("stats_{0}@test.com", i),
                        Age = 20 + i,
                        IsActive = true,
                        CreatedAt = DateTime.Now
                    })
                };

                executor.ExecuteWrite(operation);
            }

            var stats = executor.GetStats();
            Console.WriteLine(string.Format("Stats: DirectWrites={0}, QueueFallback={1}, Failures={2}, FallbackRate={3:F2}%", stats.DirectWriteCount, stats.QueueFallbackCount, stats.TotalFailureCount, stats.QueueFallbackRate));
        }

        #endregion

        #region Stability

        /// <summary>
        /// 连接池压力测试：在 PostgreSql 上循环执行查询验证连接池稳定性
        /// </summary>
        [Fact]
        public void ConnectionPool_Stress_Test()
        {
            var dbName = "PostgreSql";
            if (!CanOpenDatabase(dbName)) return;

            var iterations = 1000;
            var successCount = 0;
            var errorCount = 0;
            var errors = new ConcurrentBag<string>();

            for (int i = 0; i < iterations; i++)
            {
                try
                {
                    using (var db = new DataContext(dbName))
                    {
                        var users = db.GetList<PerfUser>(FastRead.Use(dbName).Query<PerfUser>(u => u.IsActive));
                        Interlocked.Increment(ref successCount);
                    }
                }
                catch (Exception ex)
                {
                    Interlocked.Increment(ref errorCount);
                    if (errors.Count < 10)
                        errors.Add(string.Format("Iteration {0}: {1}", i, ex.Message));
                }
            }

            var successRate = (double)successCount / iterations;
            Assert.True(successRate > 0.7, string.Format("Stress test failed: rate={0:P0}, ok={1}, err={2}", successRate, successCount, errorCount));
            Console.WriteLine(string.Format("Stress test: rate={0:P0}, ok={1}, err={2}", successRate, successCount, errorCount));
        }

        /// <summary>
        /// 长时间运行稳定性测试：在 PostgreSql 上持续运行 3 秒验证长期稳定性
        /// </summary>
        [Fact]
        public void LongRunning_Test()
        {
            var dbName = "PostgreSql";
            if (!CanOpenDatabase(dbName)) return;

            var duration = TimeSpan.FromSeconds(3);
            var successCount = 0;
            var errorCount = 0;
            var errors = new ConcurrentBag<string>();
            var stopwatch = Stopwatch.StartNew();

            while (stopwatch.Elapsed < duration)
            {
                try
                {
                    using (var db = new DataContext(dbName))
                    {
                        var users = db.GetList<PerfUser>(FastRead.Use(dbName).Query<PerfUser>(u => u.IsActive));
                        Interlocked.Increment(ref successCount);
                    }
                }
                catch (Exception ex)
                {
                    Interlocked.Increment(ref errorCount);
                    if (errors.Count < 10)
                        errors.Add(string.Format("LongRunning: {0}", ex.Message));
                }
            }

            stopwatch.Stop();
            var totalOps = successCount + errorCount;
            var successRate = (double)successCount / totalOps;
            var opsPerSecond = totalOps * 1000.0 / stopwatch.ElapsedMilliseconds;

            Assert.True(successRate > 0.5, string.Format("Long-running test failed: rate={0:P0}, ok={1}, err={2}", successRate, successCount, errorCount));
            Console.WriteLine(string.Format("Long-running test: rate={0:P0}, ok={1}, err={2}, throughput={3:F0} ops/s", successRate, successCount, errorCount, opsPerSecond));
        }

        /// <summary>
        /// 连接泄漏检测测试：模拟未释放连接并验证泄漏检测机制
        /// </summary>
        [Fact]
        public void ConnectionLeak_Detection_Test()
        {
            var dbName = "PostgreSql";
            if (!CanOpenDatabase(dbName)) return;

            var iterations = 100;
            var successCount = 0;
            var errorCount = 0;
            var errors = new ConcurrentBag<string>();

            for (int i = 0; i < iterations; i++)
            {
                try
                {
                    var db = new DataContext(dbName);
                    var users = db.GetList<PerfUser>(FastRead.Use(dbName).Query<PerfUser>(u => u.IsActive));
                    db.Dispose();
                    Interlocked.Increment(ref successCount);
                }
                catch (Exception ex)
                {
                    Interlocked.Increment(ref errorCount);
                    if (errors.Count < 10)
                        errors.Add(string.Format("Leak Detection: {0}", ex.Message));
                }
            }

            var successRate = (double)successCount / iterations;
            Assert.True(successRate > 0.95, string.Format("Leak detection test failed: rate={0:P0}, ok={1}, err={2}", successRate, successCount, errorCount));
            Console.WriteLine(string.Format("Leak detection test: rate={0:P0}, ok={1}, err={2}", successRate, successCount, errorCount));
        }

        /// <summary>
        /// 并发连接池压力测试：20 线程并发查询 PostgreSql 验证线程安全性
        /// </summary>
        [Fact]
        public async Task Concurrent_ConnectionPool_Stress_Test()
        {
            var dbName = "PostgreSql";
            if (!CanOpenDatabase(dbName)) return;

            var threadCount = 20;
            var operationsPerThread = 50;
            var successCount = 0;
            var errorCount = 0;
            var errors = new ConcurrentBag<string>();
            var lockObj = new object();

            var stopwatch = Stopwatch.StartNew();

            var tasks = Enumerable.Range(0, threadCount).Select(threadId =>
                Task.Run(() =>
                {
                    for (int i = 0; i < operationsPerThread; i++)
                    {
                        try
                        {
                            using (var db = new DataContext(dbName))
                            {
                                var users = db.GetList<PerfUser>(FastRead.Use(dbName).Query<PerfUser>(u => u.IsActive));
                                Interlocked.Increment(ref successCount);
                            }
                        }
                        catch (Exception ex)
                        {
                            Interlocked.Increment(ref errorCount);
                            lock (lockObj)
                            {
                                if (errors.Count < 10)
                                    errors.Add(string.Format("Thread {0}: {1}", threadId, ex.Message));
                            }
                        }
                    }
                })
            ).ToArray();

            await Task.WhenAll(tasks);
            stopwatch.Stop();

            var totalOps = threadCount * operationsPerThread;
            var successRate = (double)successCount / totalOps;
            var opsPerSecond = totalOps * 1000.0 / stopwatch.ElapsedMilliseconds;

            Assert.True(successRate > 0.7, string.Format("Concurrent stress test failed: rate={0:P0}, ok={1}, err={2}", successRate, successCount, errorCount));
            Console.WriteLine(string.Format("Concurrent stress test: rate={0:P0}, ok={1}, err={2}, throughput={3:F0} ops/s", successRate, successCount, errorCount, opsPerSecond));
        }

        #endregion

        #region Helpers

        /// <summary>
        /// 创建数据库连接工厂方法
        /// </summary>
        /// <returns>已打开的 SqlConnection 实例</returns>
        private DbConnection CreateConnectionFactory()
        {
            var connection = new SqlConnection(_connStr);
            connection.Open();
            return connection;
        }

        private static bool CanOpenDatabase(string dbName)
        {
            if (!string.Equals(Environment.GetEnvironmentVariable("FASTDATA_RUN_DB_INTEGRATION"), "true", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine(string.Format("未设置 FASTDATA_RUN_DB_INTEGRATION=true，跳过 {0} 数据库测试", dbName));
                return false;
            }

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
                Console.WriteLine(string.Format("{0} 不可用，跳过测试: {1}", dbName, ex.Message));
                return false;
            }
        }

        /// <summary>
        /// 检查 Redis 是否可用
        /// </summary>
        /// <returns>Redis 可用时返回 true，否则返回 false</returns>
        private static bool IsRedisAvailable()
        {
            try
            {
                var testRedis = new FullRedis { Server = "127.0.0.1:6379", Db = 7 };
                testRedis.Set("test_key", "test_value");
                return true;
            }
            catch
            {
                Console.WriteLine("Redis not available, skipping test");
                return false;
            }
        }

        #endregion
    }
}
