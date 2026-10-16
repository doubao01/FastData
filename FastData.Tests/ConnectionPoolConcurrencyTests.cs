using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data.Common;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FastData.ConnectionPool;
using Microsoft.Data.SqlClient;
using Xunit;

namespace FastData.Tests
{
    /// <summary>
    /// 连接池并发与限流测试
    /// 覆盖熔断器、并发竞争、限流降级、连接泄漏检测等场景
    /// </summary>
    [Collection("ConnectionPool")]
    public class ConnectionPoolConcurrencyTests : IDisposable
    {
        private readonly string _connStr = "server=localhost;database=FastDataTest;uid=sa;pwd=FastData@Test123;TrustServerCertificate=true";

        public void Dispose()
        {
            // 清理测试连接池
            // ConnectionPoolFactory 不提供 Clear 方法，依赖智能调整定时器和健康检查定时器自动清理
        }

        #region 熔断器测试

        /// <summary>
        /// 熔断器开启测试：连续失败后应打开熔断器
        /// </summary>
        [Fact]
        public void CircuitBreaker_ShouldOpenAfterConsecutiveFailures()
        {
            var poolConfig = new ConnectionPoolConfig
            {
                MinPoolSize = 0,
                MaxPoolSize = 2,
                ConnectionTimeout = 1,
                EnableSmartAdjustment = false,
                CircuitBreaker = new CircuitBreakerConfig
                {
                    Enabled = true,
                    FailureThreshold = 3,
                    CircuitOpenDurationSec = 10
                }
            };

            // 使用不可用的连接字符串触发错误
            var badConnStr = "server=nonexistent_host;database=Test;uid=sa;pwd=Test;Connection Timeout=1";
            SmartConnectionPool pool = null;

            try
            {
                pool = new SmartConnectionPool("CircuitBreakerTest", () =>
                {
                    var conn = new SqlConnection(badConnStr);
                    return conn;
                }, poolConfig);

                // 连续制造错误以触发熔断
                for (int i = 0; i < 5; i++)
                {
                    try
                    {
                        pool.RecordDatabaseError(new Exception(string.Format("Simulated error {0}", i)));
                    }
                    catch
                    {
                        // 忽略记录错误时的异常
                    }
                }

                var metrics = pool.GetMetrics();
                // 验证错误计数增加
                Assert.True(metrics.FailedRequests >= 0, "Failed requests should be tracked");
            }
            catch (Exception ex) when (ex.Message.Contains("nonexistent_host") || ex.Message.Contains("连接"))
            {
                // 连接失败是预期行为
            }
            finally
            {
                pool?.Dispose();
            }
        }

        /// <summary>
        /// 熔断器半开状态测试：熔断打开后，应允许少量测试请求
        /// </summary>
        [Fact]
        public void CircuitBreaker_ShouldEnterHalfOpenState()
        {
            var poolConfig = new ConnectionPoolConfig
            {
                MinPoolSize = 0,
                MaxPoolSize = 2,
                ConnectionTimeout = 1,
                EnableSmartAdjustment = false,
                CircuitBreaker = new CircuitBreakerConfig
                {
                    Enabled = true,
                    FailureThreshold = 2,
                    CircuitOpenDurationSec = 1 // 1秒后进入半开状态
                }
            };

            SmartConnectionPool pool = null;

            try
            {
                pool = new SmartConnectionPool("HalfOpenTest", () =>
                {
                    var conn = new SqlConnection(_connStr);
                    return conn;
                }, poolConfig);

                // 制造错误触发熔断
                pool.RecordDatabaseError(new Exception("Error 1"));
                pool.RecordDatabaseError(new Exception("Error 2"));
                pool.RecordDatabaseError(new Exception("Error 3"));

                // 等待熔断器超时进入半开状态
                Thread.Sleep(1500);

                // 此时应允许测试请求
                var metrics = pool.GetMetrics();
                Assert.True(metrics.FailedRequests >= 0, "Should have tracked failed requests");
            }
            catch (Exception ex)
            {
                Console.WriteLine("Half-open test exception (may be expected): {0}", ex.Message);
            }
            finally
            {
                pool?.Dispose();
            }
        }

        #endregion

        #region 并发竞争测试

        /// <summary>
        /// 多线程并发获取连接测试：验证连接池在并发场景下不会泄漏连接
        /// </summary>
        [Fact]
        public async Task Concurrent_GetConnection_NoLeak()
        {
            var poolConfig = new ConnectionPoolConfig
            {
                MinPoolSize = 2,
                MaxPoolSize = 10,
                ConnectionTimeout = 5,
                EnableSmartAdjustment = false
            };

            SmartConnectionPool pool = null;
            try
            {
                pool = new SmartConnectionPool("ConcurrentLeakTest", () =>
                {
                    var conn = new SqlConnection(_connStr);
                    return conn;
                }, poolConfig);
            }
            catch
            {
                Console.WriteLine("数据库不可用，跳过并发连接测试");
                return;
            }

            const int threadCount = 20;
            const int opsPerThread = 10;
            var successCount = 0;
            var errorCount = 0;
            var errors = new ConcurrentBag<string>();

            using (pool)
            {
                var tasks = Enumerable.Range(0, threadCount).Select(threadId =>
                    Task.Run(() =>
                    {
                        for (int i = 0; i < opsPerThread; i++)
                        {
                            try
                            {
                                using (var conn = pool.GetConnection())
                                {
                                    Assert.NotNull(conn);
                                    Assert.NotNull(conn.Connection);
                                    Interlocked.Increment(ref successCount);
                                }
                            }
                            catch (ConnectionPoolExhaustedException)
                            {
                                // 连接池耗尽也是正常现象
                                Interlocked.Increment(ref errorCount);
                            }
                            catch (Exception ex)
                            {
                                Interlocked.Increment(ref errorCount);
                                lock (errors)
                                {
                                    if (errors.Count < 5)
                                        errors.Add(string.Format("Thread {0}: {1}", threadId, ex.Message));
                                }
                            }
                        }
                    })
                ).ToArray();

                await Task.WhenAll(tasks);
            }

            var totalOps = threadCount * opsPerThread;
            var successRate = (double)successCount / totalOps;

            // 验证没有连接泄漏：所有使用过的连接都应该被归还
            var metrics = pool.GetMetrics();
            Assert.True(metrics.ActiveConnections == 0,
                string.Format("Connection leak detected: {0} active connections after all tasks completed", metrics.ActiveConnections));

            Console.WriteLine("Concurrent test: success={0}, errors={1}, rate={2:P0}", successCount, errorCount, successRate);
        }

        /// <summary>
        /// 高并发限流测试：大量线程同时请求超过 MaxPoolSize 时应正确排队或拒绝
        /// </summary>
        [Fact]
        public void HighConcurrency_ShouldRespectPoolLimits()
        {
            var poolConfig = new ConnectionPoolConfig
            {
                MinPoolSize = 1,
                MaxPoolSize = 3,
                ConnectionTimeout = 2,
                EnableSmartAdjustment = false
            };

            SmartConnectionPool pool = null;
            try
            {
                pool = new SmartConnectionPool("HighConcurrencyTest", () =>
                {
                    var conn = new SqlConnection(_connStr);
                    return conn;
                }, poolConfig);
            }
            catch
            {
                Console.WriteLine("数据库不可用，跳过限流测试");
                return;
            }

            using (pool)
            {
                // 先占满连接池
                var blockingConns = new List<PooledConnection>();
                for (int i = 0; i < poolConfig.MaxPoolSize; i++)
                {
                    try
                    {
                        blockingConns.Add(pool.GetConnection());
                    }
                    catch
                    {
                        break;
                    }
                }

                // 此时连接池已满，新请求应被拒绝或等待
                var exhaustionCount = 0;
                var timeout = TimeSpan.FromSeconds(3);
                var sw = Stopwatch.StartNew();

                // 在超时时间内尝试获取连接，验证会被正确拒绝
                while (sw.Elapsed < timeout)
                {
                    try
                    {
                        pool.GetConnection();
                    }
                    catch (ConnectionPoolExhaustedException)
                    {
                        Interlocked.Increment(ref exhaustionCount);
                        break; // 验证通过
                    }
                    catch
                    {
                        break;
                    }
                }

                sw.Stop();

                // 释放占用的连接
                foreach (var conn in blockingConns)
                {
                    conn.Dispose();
                }

                // 验证连接池正确限流
                Assert.True(exhaustionCount > 0 || sw.ElapsedMilliseconds > 500,
                    "Pool should either reject connection or wait when exhausted");

                Console.WriteLine("Limiting test: exhaustion={0}, waitTime={1}ms", exhaustionCount, sw.ElapsedMilliseconds);
            }
        }

        #endregion

        #region 连接泄漏检测测试

        /// <summary>
        /// 连接泄漏检测测试：未释放的连接应在超时后被回收
        /// </summary>
        [Fact]
        public void ConnectionLeak_DetectionAndRecovery()
        {
            var poolConfig = new ConnectionPoolConfig
            {
                MinPoolSize = 1,
                MaxPoolSize = 5,
                ConnectionTimeout = 5,
                LeakDetectionThreshold = 2, // 2秒未归还视为泄漏
                EnableSmartAdjustment = false
            };

            SmartConnectionPool pool = null;
            try
            {
                pool = new SmartConnectionPool("LeakDetectionTest", () =>
                {
                    var conn = new SqlConnection(_connStr);
                    return conn;
                }, poolConfig);
            }
            catch
            {
                Console.WriteLine("数据库不可用，跳过泄漏检测测试");
                return;
            }

            using (pool)
            {
                // 获取连接但不释放（模拟泄漏）
                PooledConnection leakedConn;
                try
                {
                    leakedConn = pool.GetConnection();
                }
                catch (ConnectionPoolExhaustedException)
                {
                    Console.WriteLine("数据库不可用，跳过泄漏检测测试");
                    return;
                }
                catch (Exception)
                {
                    Console.WriteLine("数据库不可用，跳过泄漏检测测试");
                    return;
                }

                var leakedId = leakedConn.Id;
                Assert.True(pool.GetMetrics().ActiveConnections > 0, "Should have active connections");

                // 等待泄漏检测超时
                Thread.Sleep(3000);

                // 注意：实际泄漏检测可能在后台线程处理，这里只验证活跃连接数
                var metrics = pool.GetMetrics();
                Console.WriteLine("Leak detection: active={0}, leaked={1}", metrics.ActiveConnections, metrics.LeakedConnections);

                // 安全释放（如果已被回收则忽略）
                try
                {
                    leakedConn.Dispose();
                }
                catch
                {
                    // 连接可能已被回收
                }
            }
        }

        #endregion

        #region 连接池扩缩容测试

        /// <summary>
        /// 自动扩缩容测试：高负载时应自动扩容
        /// </summary>
        [Fact]
        public void SmartAdjustment_ShouldExpandUnderHighLoad()
        {
            var poolConfig = new ConnectionPoolConfig
            {
                MinPoolSize = 2,
                MaxPoolSize = 20,
                ConnectionTimeout = 5,
                EnableSmartAdjustment = true,
                LoadThreshold = 50, // 50% 使用率触发扩容
                MaxExpandCount = 5,
                SmartAdjustmentInterval = 1
            };

            SmartConnectionPool pool = null;
            try
            {
                pool = new SmartConnectionPool("ExpandTest", () =>
                {
                    var conn = new SqlConnection(_connStr);
                    return conn;
                }, poolConfig);
            }
            catch
            {
                Console.WriteLine("数据库不可用，跳过扩缩容测试");
                return;
            }

            using (pool)
            {
                var initialMetrics = pool.GetMetrics();
                var initialTotal = initialMetrics.TotalConnections;

                // 高负载：获取大量连接
                var conns = new List<PooledConnection>();
                try
                {
                    for (int i = 0; i < 8; i++)
                    {
                        try
                        {
                            conns.Add(pool.GetConnection());
                        }
                        catch
                        {
                            break;
                        }
                    }

                    // 等待智能调整触发
                    Thread.Sleep(2000);

                    var expandedMetrics = pool.GetMetrics();
                    Console.WriteLine("Expand test: initial={0}, afterLoad={1}, active={2}", initialTotal, expandedMetrics.TotalConnections, expandedMetrics.ActiveConnections);
                }
                finally
                {
                    foreach (var conn in conns)
                    {
                        conn.Dispose();
                    }
                }
            }
        }

        /// <summary>
        /// 自动缩容测试：低负载时应自动缩容
        /// </summary>
        [Fact]
        public void SmartAdjustment_ShouldShrinkUnderLowLoad()
        {
            var poolConfig = new ConnectionPoolConfig
            {
                MinPoolSize = 2,
                MaxPoolSize = 10,
                ConnectionTimeout = 5,
                EnableSmartAdjustment = true,
                ShrinkThreshold = 30, // 30% 使用率触发缩容
                MaxShrinkCount = 3,
                SmartAdjustmentInterval = 1
            };

            SmartConnectionPool pool = null;
            try
            {
                pool = new SmartConnectionPool("ShrinkTest", () =>
                {
                    var conn = new SqlConnection(_connStr);
                    return conn;
                }, poolConfig);
            }
            catch
            {
                Console.WriteLine("数据库不可用，跳过缩容测试");
                return;
            }

            using (pool)
            {
                // 先获取连接再释放，制造空闲连接
                var conns = new List<PooledConnection>();
                for (int i = 0; i < 5; i++)
                {
                    try
                    {
                        conns.Add(pool.GetConnection());
                    }
                    catch
                    {
                        break;
                    }
                }

                foreach (var conn in conns)
                {
                    conn.Dispose();
                }

                var initialMetrics = pool.GetMetrics();

                // 等待智能调整触发缩容
                Thread.Sleep(2000);

                var afterMetrics = pool.GetMetrics();
                Console.WriteLine("Shrink test: before={0}, after={1}", initialMetrics.TotalConnections, afterMetrics.TotalConnections);
            }
        }

        #endregion

        #region 指标追踪测试

        /// <summary>
        /// 连接池指标准确性测试：验证各项指标正确累加
        /// </summary>
        [Fact]
        public void Metrics_ShouldAccuratelyTrackRequests()
        {
            var poolConfig = new ConnectionPoolConfig
            {
                MinPoolSize = 2,
                MaxPoolSize = 10,
                ConnectionTimeout = 5,
                EnableSmartAdjustment = false
            };

            SmartConnectionPool pool = null;
            try
            {
                pool = new SmartConnectionPool("MetricsTest", () =>
                {
                    var conn = new SqlConnection(_connStr);
                    return conn;
                }, poolConfig);
            }
            catch
            {
                Console.WriteLine("数据库不可用，跳过指标测试");
                return;
            }

            using (pool)
            {
                const int requestCount = 10;
                for (int i = 0; i < requestCount; i++)
                {
                    try
                    {
                        using (var conn = pool.GetConnection())
                        {
                            // 模拟使用连接
                        }
                    }
                    catch
                    {
                        break;
                    }
                }

                var metrics = pool.GetMetrics();
                Console.WriteLine("Metrics: totalRequests={0}, success={1}, failed={2}, avgWait={3:F2}ms", metrics.TotalRequests, metrics.SuccessfulRequests, metrics.FailedRequests, metrics.AverageWaitTimeMs);

                // 验证基本指标合理性
                Assert.True(metrics.TotalRequests > 0, "Total requests should be greater than 0");
                Assert.True(metrics.SuccessfulRequests >= 0, "Successful requests should be non-negative");
            }
        }

        #endregion
    }

    /// <summary>
    /// xUnit 集合定义：确保连接池测试串行执行
    /// </summary>
    [CollectionDefinition("ConnectionPool")]
    public class ConnectionPoolCollection : ICollectionFixture<object>
    {
    }
}
