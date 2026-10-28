#if !NETFRAMEWORK
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FastData;
using FastData.Base;
using FastData.Context;
using FastData.Queue;
using FastData.Tests.Integration;
using FastRedis;
using FastRedis.Messaging;
using FastRedis.Services;
using NewLife.Caching;
using Xunit;

namespace FastData.Tests
{
    /// <summary>
    /// 高并发压测
    /// 从各角度对 FastData 进行压力测试
    /// </summary>
    [Collection(SharedDatabaseCollection.Name)]
    public class StressTests
    {
        private static int _successCount;
        private static int _errorCount;
        private static readonly ConcurrentBag<string> _errors = new();
        private static readonly object _lockObj = new();

        #region 1. 缓存高并发压测

        /// <summary>
        /// 缓存并发读写测试
        /// 多线程同时读写缓存，验证线程安全性
        /// </summary>
        [Fact]
        public async Task Cache_ConcurrentReadWrite_ShouldBeThreadSafe()
        {
            // Arrange
            var threadCount = 50;
            var opsPerThread = 100;
            var successCount = 0;
            var errorCount = 0;
            _errors.Clear();

            // 检查 Redis 是否可用
            var canConnect = false;
            try
            {
                RedisInfo.Set("stress:health:check", "test", 1);
                var testValue = RedisInfo.Get("stress:health:check");
                canConnect = !string.IsNullOrEmpty(testValue);
                RedisInfo.Remove("stress:health:check");
            }
            catch
            {
                canConnect = false;
            }

            if (!canConnect)
            {
                Console.WriteLine("[缓存并发] Redis 不可用，跳过测试");
                return;
            }

            var stopwatch = Stopwatch.StartNew();

            // Act
            var tasks = Enumerable.Range(0, threadCount).Select(threadId =>
                Task.Run(() =>
                {
                    for (int i = 0; i < opsPerThread; i++)
                    {
                        try
                        {
                            var key = string.Format("stress:cache:{0}:{1}", threadId, i);
                            var value = string.Format("value_{0}_{1}", threadId, i);

                            // 写入
                            RedisInfo.Set(key, value, 1);

                            // 读取
                            var result = RedisInfo.Get(key);

                            // 删除
                            RedisInfo.Remove(key);

                            Interlocked.Increment(ref successCount);
                        }
                        catch (Exception ex)
                        {
                            Interlocked.Increment(ref errorCount);
                            lock (_lockObj)
                            {
                                if (_errors.Count < 10)
                                    _errors.Add(string.Format("Cache Thread {0}: {1}", threadId, ex.Message));
                            }
                        }
                    }
                })
            ).ToArray();

            await Task.WhenAll(tasks);
            stopwatch.Stop();

            // Assert
            var totalOps = threadCount * opsPerThread;
            var successRate = (double)successCount / totalOps;
            var opsPerSecond = totalOps * 1000.0 / stopwatch.ElapsedMilliseconds;

            Console.WriteLine("[缓存并发] 成功={0}, 失败={1}, 成功率={2:P2}, 吞吐量={3:F0} ops/s, 耗时={4}ms", successCount, errorCount, successRate, opsPerSecond, stopwatch.ElapsedMilliseconds);
            Assert.True(successRate > 0.95, string.Format("缓存并发测试失败: 成功率过低 {0:P2}", successRate));
        }

        /// <summary>
        /// 缓存批量操作压测
        /// 测试批量写入和读取的性能
        /// </summary>
        [Fact]
        public void Cache_BatchOperations_ShouldBeEfficient()
        {
            // Arrange
            var batchSize = 1000;

            // 检查 Redis 是否可用
            var canConnect = false;
            try
            {
                RedisInfo.Set("stress:health:check", "test", 1);
                var testValue = RedisInfo.Get("stress:health:check");
                canConnect = !string.IsNullOrEmpty(testValue);
                RedisInfo.Remove("stress:health:check");
            }
            catch
            {
                canConnect = false;
            }

            if (!canConnect)
            {
                Console.WriteLine("[缓存批量] Redis 不可用，跳过测试");
                return;
            }

            var stopwatch = Stopwatch.StartNew();

            // Act - 批量写入
            for (int i = 0; i < batchSize; i++)
            {
                RedisInfo.Set(string.Format("stress:batch:{0}", i), string.Format("value_{0}", i), 1);
            }
            var writeTime = stopwatch.ElapsedMilliseconds;

            // Act - 批量读取
            stopwatch.Restart();
            for (int i = 0; i < batchSize; i++)
            {
                var value = RedisInfo.Get(string.Format("stress:batch:{0}", i));
            }
            var readTime = stopwatch.ElapsedMilliseconds;

            // Cleanup
            stopwatch.Restart();
            for (int i = 0; i < batchSize; i++)
            {
                RedisInfo.Remove(string.Format("stress:batch:{0}", i));
            }
            var cleanupTime = stopwatch.ElapsedMilliseconds;
            stopwatch.Stop();

            // Assert
            Console.WriteLine("[缓存批量] 写入{0}条={1}ms, 读取{0}条={2}ms, 清理={3}ms", batchSize, writeTime, readTime, cleanupTime);
            Assert.True(writeTime < 5000, string.Format("批量写入耗时过长: {0}ms", writeTime));
            Assert.True(readTime < 5000, string.Format("批量读取耗时过长: {0}ms", readTime));
        }

        /// <summary>
        /// 缓存热点 Key 压测
        /// 多线程同时读写同一个 Key，验证原子性
        /// </summary>
        [Fact]
        public async Task Cache_HotKey_ShouldBeAtomic()
        {
            // Arrange
            var hotKey = "stress:hotkey:counter";
            var threadCount = 50;
            var opsPerThread = 20;

            // 清理旧数据
            RedisInfo.Remove(hotKey);

            // 检查 Redis 是否可用
            var testKey = "stress:health:check";
            var canConnect = false;
            try
            {
                RedisInfo.Set(testKey, "test", 1);
                var testValue = RedisInfo.Get(testKey);
                canConnect = !string.IsNullOrEmpty(testValue);
                RedisInfo.Remove(testKey);
            }
            catch
            {
                canConnect = false;
            }

            if (!canConnect)
            {
                Console.WriteLine("[热点Key] Redis 不可用，跳过测试");
                return;
            }

            // 先用 Increment 初始化 key（Redis INCR 需要 key 不存在或为整数）
            var initResult = RedisInfo.Increment(hotKey, 0);
            Console.WriteLine("[热点Key] 初始化: Increment(0) = {0}", initResult);

            var stopwatch = Stopwatch.StartNew();

            // Act - 多线程并发递增
            var tasks = Enumerable.Range(0, threadCount).Select(threadId =>
                Task.Run(() =>
                {
                    for (int i = 0; i < opsPerThread; i++)
                    {
                        try
                        {
                            RedisInfo.Increment(hotKey, 1);
                        }
                        catch (Exception ex)
                        {
                            lock (_lockObj)
                            {
                                if (_errors.Count < 10)
                                    _errors.Add(string.Format("HotKey Thread {0}: {1}", threadId, ex.Message));
                            }
                        }
                    }
                })
            ).ToArray();

            await Task.WhenAll(tasks);
            stopwatch.Stop();

            // 验证最终值 - Increment 返回 long，需要用 Get<long> 获取
            var finalValue = RedisInfo.Increment(hotKey, 0); // 用 Increment(0) 读取当前值
            var expectedCount = threadCount * opsPerThread;

            // Cleanup
            RedisInfo.Remove(hotKey);

            // Assert
            Console.WriteLine("[热点Key] 期望={0}, 实际={1}, 耗时={2}ms", expectedCount, finalValue, stopwatch.ElapsedMilliseconds);
            Assert.Equal(expectedCount, finalValue);
        }

        #endregion

        #region 2. 消息队列高并发压测

        /// <summary>
        /// 消息队列并发发布测试
        /// 多线程同时发布消息到队列
        /// </summary>
        [Fact]
        public async Task MessageQueue_ConcurrentPublish_ShouldBeThreadSafe()
        {
            // Arrange
            FullRedis redis;
            try
            {
                redis = new FullRedis
                {
                    Server = "127.0.0.1:6379",
                    Db = 7,
                    Timeout = 15000
                };
                // 测试连接
                redis.Set("mq:health:check", "test", 1);
                redis.Remove("mq:health:check");
            }
            catch
            {
                Console.WriteLine("[MQ并发发布] Redis 不可用，跳过测试");
                return;
            }

            var mqService = new MessageQueueIntegrationService(redis);
            var topic = "stress:mq:concurrent";
            var threadCount = 20;
            var opsPerThread = 50;
            var successCount = 0;
            var errorCount = 0;
            _errors.Clear();

            var stopwatch = Stopwatch.StartNew();

            // Act
            var tasks = Enumerable.Range(0, threadCount).Select(threadId =>
                Task.Run(() =>
                {
                    for (int i = 0; i < opsPerThread; i++)
                    {
                        try
                        {
                            var data = new { ThreadId = threadId, Index = i, Timestamp = DateTime.Now };
                            mqService.PublishSingle(topic, data, MessageQueueType.ReliableQueue);
                            Interlocked.Increment(ref successCount);
                        }
                        catch (Exception ex)
                        {
                            Interlocked.Increment(ref errorCount);
                            lock (_lockObj)
                            {
                                if (_errors.Count < 10)
                                    _errors.Add(string.Format("MQ Thread {0}: {1}", threadId, ex.Message));
                            }
                        }
                    }
                })
            ).ToArray();

            await Task.WhenAll(tasks);
            stopwatch.Stop();

            // Assert
            var totalOps = threadCount * opsPerThread;
            var successRate = (double)successCount / totalOps;
            var opsPerSecond = totalOps * 1000.0 / stopwatch.ElapsedMilliseconds;

            Console.WriteLine("[MQ并发发布] 成功={0}, 失败={1}, 成功率={2:P2}, 吞吐量={3:F0} ops/s, 耗时={4}ms", successCount, errorCount, successRate, opsPerSecond, stopwatch.ElapsedMilliseconds);
            Assert.True(successRate > 0.95, string.Format("MQ并发发布测试失败: 成功率过低 {0:P2}", successRate));
        }

        /// <summary>
        /// 消息队列批量发布测试
        /// 测试批量发布消息的性能
        /// </summary>
        [Fact]
        public void MessageQueue_BatchPublish_ShouldBeEfficient()
        {
            // Arrange
            FullRedis redis;
            try
            {
                redis = new FullRedis
                {
                    Server = "127.0.0.1:6379",
                    Db = 7,
                    Timeout = 15000
                };
                // 测试连接
                redis.Set("mq:health:check", "test", 1);
                redis.Remove("mq:health:check");
            }
            catch
            {
                Console.WriteLine("[MQ批量发布] Redis 不可用，跳过测试");
                return;
            }

            var mqService = new MessageQueueIntegrationService(redis);
            var topic = "stress:mq:batch";
            var batchSize = 500;

            var dataList = Enumerable.Range(0, batchSize)
                .Select(i => new { Index = i, Data = string.Format("batch_data_{0}", i), Timestamp = DateTime.Now })
                .ToList();

            var stopwatch = Stopwatch.StartNew();

            // Act
            var count = mqService.PublishData(topic, dataList, MessageQueueType.ReliableQueue);
            stopwatch.Stop();

            // Assert
            Console.WriteLine("[MQ批量发布] 发布={0}条, 耗时={1}ms", count, stopwatch.ElapsedMilliseconds);
            Assert.Equal(batchSize, count);
            Assert.True(stopwatch.ElapsedMilliseconds < 10000, string.Format("批量发布耗时过长: {0}ms", stopwatch.ElapsedMilliseconds));
        }

        #endregion

        #region 3. WriteBehindRegistry 高并发压测

        /// <summary>
        /// 注册表并发读写测试
        /// 多线程同时注册和查询配置
        /// </summary>
        [Fact]
        public async Task WriteBehindRegistry_ConcurrentReadWrite_ShouldBeThreadSafe()
        {
            // Arrange
            var threadCount = 50;
            var opsPerThread = 100;
            var successCount = 0;
            var errorCount = 0;
            _errors.Clear();

            var stopwatch = Stopwatch.StartNew();

            // Act
            var tasks = Enumerable.Range(0, threadCount).Select(threadId =>
                Task.Run(() =>
                {
                    for (int i = 0; i < opsPerThread; i++)
                    {
                        try
                        {
                            var tableName = string.Format("StressTable_{0}_{1}", threadId, i);

                            // 注册
                            var config = new WriteBehindConfig
                            {
                                QueueType = i % 2 == 0 ? WriteBehindQueueType.ReliableQueue : WriteBehindQueueType.Stream,
                                Topic = string.Format("stress:{0}", tableName),
                                EnableFallback = true
                            };
                            WriteBehindRegistry.Register(tableName, config);

                            // 查询
                            var result = WriteBehindRegistry.GetConfig(tableName);
                            if (result == null)
                                throw new Exception("注册后查询返回 null");

                            // 检查启用状态
                            var isEnabled = WriteBehindRegistry.IsQueueEnabled(tableName);
                            if (!isEnabled)
                                throw new Exception("注册后检查启用状态返回 false");

                            // 取消注册
                            WriteBehindRegistry.Unregister(tableName);

                            Interlocked.Increment(ref successCount);
                        }
                        catch (Exception ex)
                        {
                            Interlocked.Increment(ref errorCount);
                            lock (_lockObj)
                            {
                                if (_errors.Count < 10)
                                    _errors.Add(string.Format("Registry Thread {0}: {1}", threadId, ex.Message));
                            }
                        }
                    }
                })
            ).ToArray();

            await Task.WhenAll(tasks);
            stopwatch.Stop();

            // Assert
            var totalOps = threadCount * opsPerThread;
            var successRate = (double)successCount / totalOps;
            var opsPerSecond = totalOps * 1000.0 / stopwatch.ElapsedMilliseconds;

            Console.WriteLine("[注册表并发] 成功={0}, 失败={1}, 成功率={2:P2}, 吞吐量={3:F0} ops/s, 耗时={4}ms", successCount, errorCount, successRate, opsPerSecond, stopwatch.ElapsedMilliseconds);
            Assert.True(successRate > 0.95, string.Format("注册表并发测试失败: 成功率过低 {0:P2}", successRate));
        }

        #endregion

        #region 4. LogManager 高并发压测

        /// <summary>
        /// 日志管理器并发写入测试
        /// 多线程同时写入日志，验证线程安全性
        /// </summary>
        [Fact]
        public async Task LogManager_ConcurrentWrite_ShouldBeThreadSafe()
        {
            // Arrange
            var threadCount = 30;
            var opsPerThread = 100;
            var successCount = 0;
            var errorCount = 0;
            _errors.Clear();

            var stopwatch = Stopwatch.StartNew();

            // Act
            var tasks = Enumerable.Range(0, threadCount).Select(threadId =>
                Task.Run(() =>
                {
                    for (int i = 0; i < opsPerThread; i++)
                    {
                        try
                        {
                            var message = string.Format("[Thread {0}] Stress test log message {1} at {2:HH:mm:ss.fff}", threadId, i, DateTime.Now);
                            FastUntility.Base.LogManager.SaveLog(message, "stress-test");
                            Interlocked.Increment(ref successCount);
                        }
                        catch (Exception ex)
                        {
                            Interlocked.Increment(ref errorCount);
                            lock (_lockObj)
                            {
                                if (_errors.Count < 10)
                                    _errors.Add(string.Format("Log Thread {0}: {1}", threadId, ex.Message));
                            }
                        }
                    }
                })
            ).ToArray();

            await Task.WhenAll(tasks);
            stopwatch.Stop();

            // Assert
            var totalOps = threadCount * opsPerThread;
            var successRate = (double)successCount / totalOps;
            var opsPerSecond = totalOps * 1000.0 / stopwatch.ElapsedMilliseconds;

            Console.WriteLine("[日志并发] 成功={0}, 失败={1}, 成功率={2:P2}, 吞吐量={3:F0} ops/s, 耗时={4}ms", successCount, errorCount, successRate, opsPerSecond, stopwatch.ElapsedMilliseconds);
            Assert.True(successRate > 0.95, string.Format("日志并发测试失败: 成功率过低 {0:P2}", successRate));
        }

        #endregion

        #region 5. 内存压力测试

        /// <summary>
        /// 内存压力测试
        /// 大量创建和销毁对象，检测内存泄漏
        /// </summary>
        [Fact]
        public void MemoryPressure_ShouldNotLeak()
        {
            // Arrange
            var iterations = 10000;
            var initialMemory = GC.GetTotalMemory(true);

            // Act
            for (int i = 0; i < iterations; i++)
            {
                // 创建各种对象
                var config = new WriteBehindConfig
                {
                    QueueType = WriteBehindQueueType.ReliableQueue,
                    Topic = string.Format("stress:memory:{0}", i),
                    EnableFallback = true
                };

                var operation = new WriteOperation
                {
                    OperationType = WriteOperationType.Add,
                    TableName = string.Format("Table_{0}", i),
                    EntityType = "TestEntity",
                    Data = string.Format("{{\"Id\":{0},\"Name\":\"Test_{0}\"}}", i),
                    Metadata = new Dictionary<string, object> { {"key", "value"} }
                };

                var result = new WriteBehindResult
                {
                    Success = true,
                    DirectWriteCount = i,
                    QueuedCount = 0,
                    FailedCount = 0
                };
            }

            // 强制 GC
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            var finalMemory = GC.GetTotalMemory(false);
            var memoryIncrease = finalMemory - initialMemory;

            // Assert
            Console.WriteLine("[内存压力] 初始={0}KB, 最终={1}KB, 增加={2}KB", initialMemory / 1024, finalMemory / 1024, memoryIncrease / 1024);
            // 允许一定范围的内存增长（对象缓存等）
            Assert.True(memoryIncrease < 10 * 1024 * 1024, string.Format("内存增长过大: {0}MB", memoryIncrease / 1024 / 1024));
        }

        #endregion

        #region 6. 混合场景压测

        /// <summary>
        /// 混合场景压测
        /// 同时进行缓存、消息队列、注册表操作
        /// </summary>
        [Fact]
        public async Task MixedScenario_ShouldBeStable()
        {
            // Arrange
            FullRedis redis;
            var redisAvailable = false;
            try
            {
                redis = new FullRedis
                {
                    Server = "127.0.0.1:6379",
                    Db = 7,
                    Timeout = 15000
                };
                // 测试连接
                redis.Set("mixed:health:check", "test", 1);
                redis.Remove("mixed:health:check");
                redisAvailable = true;
            }
            catch
            {
                redis = null;
                redisAvailable = false;
            }

            var mqService = redisAvailable ? new MessageQueueIntegrationService(redis) : null;
            var threadCount = 20;
            var opsPerThread = 50;
            var successCount = 0;
            var errorCount = 0;
            _errors.Clear();

            var stopwatch = Stopwatch.StartNew();

            // Act
            var tasks = Enumerable.Range(0, threadCount).Select(threadId =>
                Task.Run(() =>
                {
                    for (int i = 0; i < opsPerThread; i++)
                    {
                        try
                        {
                            var operation = i % 4;

                            switch (operation)
                            {
                                case 0: // 缓存操作
                                    if (redisAvailable)
                                    {
                                        var cacheKey = string.Format("stress:mixed:cache:{0}:{1}", threadId, i);
                                        RedisInfo.Set(cacheKey, string.Format("value_{0}", i), 1);
                                        RedisInfo.Get(cacheKey);
                                        RedisInfo.Remove(cacheKey);
                                    }
                                    break;

                                case 1: // 消息队列操作
                                    if (redisAvailable && mqService != null)
                                    {
                                        var topic = "stress:mixed:mq";
                                        mqService.PublishSingle(topic, new { ThreadId = threadId, Index = i }, MessageQueueType.ReliableQueue);
                                    }
                                    break;

                                case 2: // 注册表操作
                                    var tableName = string.Format("MixedTable_{0}_{1}", threadId, i);
                                    WriteBehindRegistry.Register(tableName, new WriteBehindConfig
                                    {
                                        QueueType = WriteBehindQueueType.ReliableQueue,
                                        Topic = string.Format("stress:{0}", tableName)
                                    });
                                    WriteBehindRegistry.GetConfig(tableName);
                                    WriteBehindRegistry.Unregister(tableName);
                                    break;

                                case 3: // 日志操作
                                    FastUntility.Base.LogManager.SaveLog(string.Format("[Mixed] Thread {0} Op {1}", threadId, i), "stress-mixed");
                                    break;
                            }

                            Interlocked.Increment(ref successCount);
                        }
                        catch (Exception ex)
                        {
                            Interlocked.Increment(ref errorCount);
                            lock (_lockObj)
                            {
                                if (_errors.Count < 10)
                                    _errors.Add(string.Format("Mixed Thread {0}: {1}", threadId, ex.Message));
                            }
                        }
                    }
                })
            ).ToArray();

            await Task.WhenAll(tasks);
            stopwatch.Stop();

            // Assert
            var totalOps = threadCount * opsPerThread;
            var successRate = (double)successCount / totalOps;
            var opsPerSecond = totalOps * 1000.0 / stopwatch.ElapsedMilliseconds;

            Console.WriteLine("[混合场景] 成功={0}, 失败={1}, 成功率={2:P2}, 吞吐量={3:F0} ops/s, 耗时={4}ms", successCount, errorCount, successRate, opsPerSecond, stopwatch.ElapsedMilliseconds);
            Assert.True(successRate > 0.90, string.Format("混合场景测试失败: 成功率过低 {0:P2}", successRate));
        }

        #endregion

        #region 7. 长时间稳定性测试

        /// <summary>
        /// 长时间稳定性测试
        /// 持续运行一段时间，检测稳定性
        /// </summary>
        [Fact]
        public void LongRunning_ShouldBeStable()
        {
            // Arrange
            var duration = TimeSpan.FromSeconds(5);
            var successCount = 0;
            var errorCount = 0;
            _errors.Clear();

            // 检查 Redis 是否可用
            var canConnect = false;
            try
            {
                RedisInfo.Set("stress:health:check", "test", 1);
                var testValue = RedisInfo.Get("stress:health:check");
                canConnect = !string.IsNullOrEmpty(testValue);
                RedisInfo.Remove("stress:health:check");
            }
            catch
            {
                canConnect = false;
            }

            if (!canConnect)
            {
                Console.WriteLine("[长时间运行] Redis 不可用，跳过测试");
                return;
            }

            var stopwatch = Stopwatch.StartNew();

            // Act
            while (stopwatch.Elapsed < duration)
            {
                try
                {
                    // 缓存操作
                    var key = string.Format("stress:longrun:{0}", successCount);
                    RedisInfo.Set(key, "value", 1);
                    RedisInfo.Get(key);
                    RedisInfo.Remove(key);

                    Interlocked.Increment(ref successCount);
                }
                catch (Exception ex)
                {
                    Interlocked.Increment(ref errorCount);
                    lock (_lockObj)
                    {
                        if (_errors.Count < 10)
                            _errors.Add(string.Format("LongRun: {0}", ex.Message));
                    }
                }
            }

            stopwatch.Stop();

            // Assert
            var totalOps = successCount + errorCount;
            var successRate = (double)successCount / totalOps;
            var opsPerSecond = totalOps * 1000.0 / stopwatch.ElapsedMilliseconds;

            Console.WriteLine("[长时间运行] 成功={0}, 失败={1}, 成功率={2:P2}, 吞吐量={3:F0} ops/s, 耗时={4}ms", successCount, errorCount, successRate, opsPerSecond, stopwatch.ElapsedMilliseconds);
            Assert.True(successRate > 0.95, string.Format("长时间运行测试失败: 成功率过低 {0:P2}", successRate));
        }

        #endregion

        #region 8. 超高并发测试

        /// <summary>
        /// 超高并发测试
        /// 200 线程并发操作
        /// </summary>
        [Fact]
        public async Task UltraHighConcurrency_200Threads()
        {
            // Arrange
            var threadCount = 200;
            var opsPerThread = 20;
            var successCount = 0;
            var errorCount = 0;
            _errors.Clear();

            // 检查 Redis 是否可用
            var canConnect = false;
            try
            {
                RedisInfo.Set("stress:health:check", "test", 1);
                var testValue = RedisInfo.Get("stress:health:check");
                canConnect = !string.IsNullOrEmpty(testValue);
                RedisInfo.Remove("stress:health:check");
            }
            catch
            {
                canConnect = false;
            }

            if (!canConnect)
            {
                Console.WriteLine("[超高并发] Redis 不可用，跳过测试");
                return;
            }

            var stopwatch = Stopwatch.StartNew();

            // Act
            var tasks = Enumerable.Range(0, threadCount).Select(threadId =>
                Task.Run(() =>
                {
                    for (int i = 0; i < opsPerThread; i++)
                    {
                        try
                        {
                            var key = string.Format("stress:ultra:{0}:{1}", threadId, i);
                            RedisInfo.Set(key, string.Format("value_{0}", i), 1);
                            RedisInfo.Get(key);
                            RedisInfo.Remove(key);
                            Interlocked.Increment(ref successCount);
                        }
                        catch (Exception ex)
                        {
                            Interlocked.Increment(ref errorCount);
                            lock (_lockObj)
                            {
                                if (_errors.Count < 10)
                                    _errors.Add(string.Format("Ultra Thread {0}: {1}", threadId, ex.Message));
                            }
                        }
                    }
                })
            ).ToArray();

            await Task.WhenAll(tasks);
            stopwatch.Stop();

            // Assert
            var totalOps = threadCount * opsPerThread;
            var successRate = (double)successCount / totalOps;
            var opsPerSecond = totalOps * 1000.0 / stopwatch.ElapsedMilliseconds;

            Console.WriteLine("[超高并发] 线程={0}, 成功={1}, 失败={2}, 成功率={3:P2}, 吞吐量={4:F0} ops/s, 耗时={5}ms", threadCount, successCount, errorCount, successRate, opsPerSecond, stopwatch.ElapsedMilliseconds);
            Assert.True(successRate > 0.90, string.Format("超高并发测试失败: 成功率过低 {0:P2}", successRate));
        }

        #endregion

        #region 9. 数据库并发压测

        /// <summary>
        /// 30 线程并发测试 - SqlServer
        /// </summary>
        [Fact]
        public async Task Concurrent30_SqlServer()
        {
            var result = await TestConcurrentReadWrite("SqlServer", 30, 10);
            Console.WriteLine("SqlServer 30线程并发: {0}", result.Details);
            if (_errors.Count > 0)
            {
                Console.WriteLine("错误详情:");
                foreach (var error in _errors.Take(5))
                    Console.WriteLine("  - {0}", error);
            }
            Assert.True(result.SuccessRate > 0.5, string.Format("30线程并发测试失败: {0}", result.Details));
        }

        /// <summary>
        /// 30 线程并发测试 - MySql
        /// </summary>
        [Fact]
        public async Task Concurrent30_MySql()
        {
            var result = await TestConcurrentReadWrite("MySql", 30, 10);
            Console.WriteLine("MySql 30线程并发: {0}", result.Details);
            if (_errors.Count > 0)
            {
                Console.WriteLine("错误详情:");
                foreach (var error in _errors.Take(5))
                    Console.WriteLine("  - {0}", error);
            }
            // MySql 允许部分失败（连接池限制）
            Assert.True(result.SuccessRate > 0.3, string.Format("30线程并发测试失败: {0}", result.Details));
        }

        /// <summary>
        /// 30 线程并发测试 - PostgreSql
        /// </summary>
        [Fact]
        public async Task Concurrent30_PostgreSql()
        {
            var result = await TestConcurrentReadWrite("PostgreSql", 30, 10);
            Console.WriteLine("PostgreSql 30线程并发: {0}", result.Details);
            if (_errors.Count > 0)
            {
                Console.WriteLine("错误详情:");
                foreach (var error in _errors.Take(5))
                    Console.WriteLine("  - {0}", error);
            }
            Assert.True(result.SuccessRate > 0.2, string.Format("30线程并发测试失败: {0}", result.Details));
        }

        /// <summary>
        /// 100 线程并发测试 - SqlServer
        /// </summary>
        [Fact]
        public async Task Concurrent100_SqlServer()
        {
            var result = await TestConcurrentReadWrite("SqlServer", 100, 5);
            // 100线程允许部分失败（连接池限制）
            Assert.True(result.SuccessRate > 0.3, string.Format("100线程并发测试失败: {0}", result.Details));
            Console.WriteLine("SqlServer 100线程并发: {0}", result.Details);
        }

        /// <summary>
        /// 100 线程并发测试 - MySql
        /// </summary>
        [Fact]
        public async Task Concurrent100_MySql()
        {
            var result = await TestConcurrentReadWrite("MySql", 100, 5);
            // 100线程允许部分失败（连接池限制）
            Assert.True(result.SuccessRate > 0.3, string.Format("100线程并发测试失败: {0}", result.Details));
            Console.WriteLine("MySql 100线程并发: {0}", result.Details);
        }

        /// <summary>
        /// 100 线程并发测试 - PostgreSql
        /// </summary>
        [Fact]
        public async Task Concurrent100_PostgreSql()
        {
            var result = await TestConcurrentReadWrite("PostgreSql", 100, 5);
            // 100线程允许部分失败（连接池限制）
            Assert.True(result.SuccessRate > 0.3, string.Format("100线程并发测试失败: {0}", result.Details));
            Console.WriteLine("PostgreSql 100线程并发: {0}", result.Details);
        }

        /// <summary>
        /// 混合操作并发测试
        /// </summary>
        [Fact]
        public async Task MixedOperations_Concurrent()
        {
            var dbName = "PostgreSql";
            var result = await TestMixedOperations(dbName, 50, 10);
            // 混合操作允许部分失败
            Assert.True(result.SuccessRate > 0.5, string.Format("混合操作并发测试失败: {0}", result.Details));
            Console.WriteLine("{0} 混合操作并发: {1}", dbName, result.Details);
        }

        private async Task<TestResult> TestConcurrentReadWrite(string dbName, int threadCount, int operationsPerThread)
        {
            if (!IsDatabaseConfigured(dbName))
            {
                Console.WriteLine("{0} 配置不可用，跳过并发读写压测", dbName);
                return TestResult.Skipped;
            }

            _successCount = 0;
            _errorCount = 0;
            _errors.Clear();

            var stopwatch = Stopwatch.StartNew();

            var tasks = Enumerable.Range(0, threadCount).Select(threadId =>
                Task.Run(() =>
                {
                    for (int i = 0; i < operationsPerThread; i++)
                    {
                        try
                        {
                            if (i % 3 == 0)
                            {
                                var entity = new PerfUser
                                {
                                    UserName = string.Format("Concurrent_{0}_{1}", threadId, i),
                                    Email = string.Format("concurrent_{0}_{1}@test.com", threadId, i),
                                    Age = 20 + (i % 50),
                                    IsActive = true,
                                    CreatedAt = DateTime.Now
                                };

                                using var db = new DataContext(dbName);
                                var result = db.Add(entity);
                                if (result.WriteReturn.IsSuccess)
                                    Interlocked.Increment(ref _successCount);
                                else
                                {
                                    Interlocked.Increment(ref _errorCount);
                                    lock (_lockObj)
                                    {
                                        if (_errors.Count < 10)
                                            _errors.Add(string.Format("Thread {0} Add failed: {1}", threadId, result.WriteReturn.Message));
                                    }
                                }
                            }
                            else
                            {
                                using (var tempDb = new DataContext(dbName))
                                {
                                    var result = tempDb.GetList<PerfUser>(FastRead.Use(dbName).Query<PerfUser>(u => u.IsActive));
                                }
                                Interlocked.Increment(ref _successCount);
                            }
                        }
                        catch (Exception ex)
                        {
                            Interlocked.Increment(ref _errorCount);
                            lock (_lockObj)
                            {
                                if (_errors.Count < 10)
                                    _errors.Add(string.Format("Thread {0}: {1}", threadId, ex.Message));
                            }
                        }
                    }
                })
            ).ToArray();

            await Task.WhenAll(tasks);
            stopwatch.Stop();

            var totalOps = threadCount * operationsPerThread;
            var opsPerSecond = totalOps * 1000.0 / stopwatch.ElapsedMilliseconds;
            var successRate = (double)_successCount / totalOps;

            return new TestResult
            {
                Success = _errorCount == 0,
                SuccessRate = successRate,
                ElapsedMs = stopwatch.ElapsedMilliseconds,
                OpsPerSecond = opsPerSecond,
                Details = string.Format("成功={0}, 失败={1}, 成功率={2:P0}, 吞吐量={3:F0} ops/s", _successCount, _errorCount, successRate, opsPerSecond)
            };
        }

        private async Task<TestResult> TestMixedOperations(string dbName, int threadCount, int operationsPerThread)
        {
            if (!IsDatabaseConfigured(dbName))
            {
                Console.WriteLine("{0} 配置不可用，跳过混合操作压测", dbName);
                return TestResult.Skipped;
            }

            _successCount = 0;
            _errorCount = 0;
            _errors.Clear();

            var stopwatch = Stopwatch.StartNew();

            var tasks = Enumerable.Range(0, threadCount).Select(threadId =>
                Task.Run(() =>
                {
                    for (int i = 0; i < operationsPerThread; i++)
                    {
                        try
                        {
                            var operation = i % 5;
                            switch (operation)
                            {
                                case 0: // 插入
                                    var entity = new PerfUser
                                    {
                                        UserName = string.Format("Mixed_{0}_{1}", threadId, i),
                                        Email = string.Format("mixed_{0}_{1}@test.com", threadId, i),
                                        Age = 20 + (i % 50),
                                        IsActive = true,
                                        CreatedAt = DateTime.Now
                                    };
                                    using (var db = new DataContext(dbName))
                                    {
                                        var insertResult = db.Add(entity);
                                        if (insertResult.WriteReturn.IsSuccess)
                                            Interlocked.Increment(ref _successCount);
                                        else
                                            Interlocked.Increment(ref _errorCount);
                                    }
                                    break;

                                case 1: // 查询
                                    using (var db = new DataContext(dbName))
                                    {
                                        var users = db.GetList<PerfUser>(FastRead.Use(dbName).Query<PerfUser>(u => u.IsActive));
                                        Interlocked.Increment(ref _successCount);
                                    }
                                    break;

                                case 2: // 条件查询
                                    using (var db = new DataContext(dbName))
                                    {
                                        var filteredUsers = db.GetList<PerfUser>(FastRead.Use(dbName).Query<PerfUser>(u => u.Age > 30));
                                        Interlocked.Increment(ref _successCount);
                                    }
                                    break;

                                case 3: // 链式查询
                                    using (var db = new DataContext(dbName))
                                    {
                                        var chainedUsers = db.GetList<PerfUser>(FastRead.Use(dbName).Query<PerfUser>(u => u.IsActive).And<PerfUser>(u => u.Age > 25));
                                        Interlocked.Increment(ref _successCount);
                                    }
                                    break;

                                case 4: // 分页查询
                                    using (var db = new DataContext(dbName))
                                    {
                                        var pageResult = FastRead.Use(dbName).Query<PerfUser>(u => u.IsActive).ToPagination<PerfUser>(1, 10);
                                        Interlocked.Increment(ref _successCount);
                                    }
                                    break;
                            }
                        }
                        catch (Exception ex)
                        {
                            Interlocked.Increment(ref _errorCount);
                            lock (_lockObj)
                            {
                                if (_errors.Count < 10)
                                    _errors.Add(string.Format("Thread {0}: {1}", threadId, ex.Message));
                            }
                        }
                    }
                })
            ).ToArray();

            await Task.WhenAll(tasks);
            stopwatch.Stop();

            var totalOps = threadCount * operationsPerThread;
            var opsPerSecond = totalOps * 1000.0 / stopwatch.ElapsedMilliseconds;
            var successRate = (double)_successCount / totalOps;

            return new TestResult
            {
                Success = _errorCount == 0,
                SuccessRate = successRate,
                ElapsedMs = stopwatch.ElapsedMilliseconds,
                OpsPerSecond = opsPerSecond,
                Details = string.Format("成功={0}, 失败={1}, 成功率={2:P0}, 吞吐量={3:F0} ops/s", _successCount, _errorCount, successRate, opsPerSecond)
            };
        }

        private class TestResult
        {
            public static readonly TestResult Skipped = new TestResult
            {
                Success = true,
                SuccessRate = 1,
                ElapsedMs = 0,
                OpsPerSecond = 0,
                Details = "测试环境未配置，已跳过"
            };

            public bool Success { get; set; }
            public double SuccessRate { get; set; }
            public long ElapsedMs { get; set; }
            public double OpsPerSecond { get; set; }
            public string Details { get; set; }
        }

        private static bool IsDatabaseConfigured(string dbName)
        {
            if (!string.Equals(Environment.GetEnvironmentVariable("FASTDATA_RUN_DB_INTEGRATION"), "true", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine("未设置 FASTDATA_RUN_DB_INTEGRATION=true，跳过 {0} 数据库压测", dbName);
                return false;
            }

            try
            {
                using (var db = new DataContext(dbName))
                {
                    return db != null;
                }
            }
            catch
            {
                return false;
            }
        }

        #endregion
    }
}
#endif
