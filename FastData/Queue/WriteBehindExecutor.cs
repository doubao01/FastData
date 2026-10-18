#if !NETFRAMEWORK
using System;
using System.Collections.Generic;
using System.Threading;
using FastData.Model;
using FastRedis.Messaging;
using FastRedis.Services;
using NewLife.Caching;
using NewLife.Log;

namespace FastData.Queue
{
    /// <summary>
    /// 写入后端执行器
    /// 负责执行写入操作，支持数据库降级到消息队列
    /// </summary>
    public static class WriteBehindExecutor
    {
        private static MessageQueueIntegrationService _mqService;
        private static readonly object _lock = new object();
        private static volatile bool _initialized = false;

        /// <summary>
        /// 初始化执行器（必须显式提供 Redis 连接）
        /// </summary>
        /// <param name="redisConnectionString">Redis 连接字符串</param>
        /// <param name="redisDb">Redis 数据库索引</param>
        /// <exception cref="ArgumentException">未提供 Redis 连接字符串时抛出</exception>
        public static void Initialize(string redisConnectionString = null, int redisDb = 0)
        {
            if (string.IsNullOrWhiteSpace(redisConnectionString))
            {
                XTrace.WriteLine("[WriteBehind] 警告: 未提供 Redis 连接字符串，使用默认连接 '127.0.0.1:6379'（仅适用于开发环境）");
                redisConnectionString = "127.0.0.1:6379";
            }

            lock (_lock)
            {
                if (_initialized) return;

                var redis = new FullRedis
                {
                    Server = redisConnectionString,
                    Db = redisDb,
                    Timeout = 15000
                };
                _mqService = new MessageQueueIntegrationService(redis);
                _initialized = true;
            }
        }

        /// <summary>
        /// 初始化执行器（使用现有 Redis 实例）
        /// </summary>
        /// <param name="redis">FullRedis 实例</param>
        public static void Initialize(FullRedis redis)
        {
            lock (_lock)
            {
                if (_initialized) return;
                _mqService = new MessageQueueIntegrationService(redis);
                _initialized = true;
            }
        }

        /// <summary>
        /// 运行时重新配置执行器
        /// 释放现有连接并使用新配置重新初始化
        /// </summary>
        /// <param name="redisConnectionString">新的 Redis 连接字符串</param>
        /// <param name="redisDb">新的 Redis 数据库索引</param>
        public static void Reconfigure(string redisConnectionString, int redisDb = 0)
        {
            if (string.IsNullOrWhiteSpace(redisConnectionString))
                throw new ArgumentException("Redis 连接字符串不能为空", nameof(redisConnectionString));

            Shutdown(); // 安全释放现有资源

            lock (_lock)
            {
                var redis = new FullRedis
                {
                    Server = redisConnectionString,
                    Db = redisDb,
                    Timeout = 15000
                };
                _mqService = new MessageQueueIntegrationService(redis);
                _initialized = true;
                XTrace.WriteLine($"[WriteBehind] 已重新配置 Redis 连接: {redisConnectionString}");
            }
        }

        /// <summary>
        /// 运行时重新配置执行器（使用现有 Redis 实例）
        /// </summary>
        /// <param name="redis">新的 FullRedis 实例</param>
        public static void Reconfigure(FullRedis redis)
        {
            if (redis == null)
                throw new ArgumentNullException(nameof(redis));

            Shutdown(); // 安全释放现有资源

            lock (_lock)
            {
                _mqService = new MessageQueueIntegrationService(redis);
                _initialized = true;
                XTrace.WriteLine("[WriteBehind] 已重新配置 Redis 实例");
            }
        }

        /// <summary>
        /// 关闭执行器并释放资源
        /// 线程安全，可多次调用
        /// </summary>
        public static void Shutdown()
        {
            MessageQueueIntegrationService serviceToDispose = null;

            lock (_lock)
            {
                if (!_initialized) return;

                serviceToDispose = _mqService;
                _mqService = null;
                _initialized = false;
            }

            // 在锁外执行释放操作，避免阻塞其他线程
            try
            {
                serviceToDispose?.Dispose();
                XTrace.WriteLine("[WriteBehind] 已关闭并释放资源");
            }
            catch (Exception ex)
            {
                XTrace.WriteLine($"[WriteBehind] 释放资源时发生异常: {ex.Message}");
            }
        }

        /// <summary>
        /// 获取消息队列服务
        /// </summary>
        public static MessageQueueIntegrationService MqService => _mqService;

        /// <summary>
        /// 执行写入操作列表
        /// </summary>
        /// <param name="operations">操作列表</param>
        /// <param name="databaseKey">数据库 Key</param>
        /// <param name="overrideConfig">覆盖配置（可选）</param>
        /// <returns>执行结果</returns>
        public static WriteBehindResult Execute(List<WriteOperation> operations, string databaseKey = null, WriteBehindConfig overrideConfig = null)
        {
            var result = new WriteBehindResult { Success = true };

            // 按表分组操作
            var groupedOps = GroupByTable(operations);

            foreach (var group in groupedOps)
            {
                var tableName = group.Key;
                var tableOps = group.Value;
                var config = overrideConfig ?? WriteBehindRegistry.GetConfig(tableName);
                var useQueue = config != null && config.QueueType != WriteBehindQueueType.None;

                if (useQueue && config.EnableFallback)
                {
                    // 启用了队列降级模式
                    var tableResult = ExecuteWithFallback(tableOps, config, databaseKey);
                    MergeResult(result, tableResult);
                }
                else
                {
                    // 直接写数据库
                    var tableResult = ExecuteDirect(tableOps, databaseKey);
                    MergeResult(result, tableResult);
                }
            }

            return result;
        }

        /// <summary>
        /// 直接写入数据库
        /// </summary>
        private static WriteBehindResult ExecuteDirect(List<WriteOperation> operations, string databaseKey)
        {
            var result = new WriteBehindResult { Success = true };

            foreach (var op in operations)
            {
                try
                {
                    var writeResult = ExecuteSingleOperation(op, databaseKey);
                    if (writeResult.Success)
                    {
                        result.DirectWriteCount++;
                    }
                    else
                    {
                        result.FailedCount++;
                        result.Success = false;
                        result.Details.Add(writeResult);
                    }
                }
                catch (Exception ex)
                {
                    result.FailedCount++;
                    result.Success = false;
                    result.Details.Add(new WriteOperationResult
                    {
                        OperationType = op.OperationType,
                        TableName = op.TableName,
                        Success = false,
                        ErrorMessage = ex.Message
                    });
                }
            }

            return result;
        }

        /// <summary>
        /// 带降级的写入（先尝试数据库，失败则写队列）
        /// </summary>
        /// <param name="operations">写入操作列表</param>
        /// <param name="config">写入配置</param>
        /// <param name="databaseKey">数据库键</param>
        /// <returns>写入结果</returns>
        private static WriteBehindResult ExecuteWithFallback(List<WriteOperation> operations, WriteBehindConfig config, string databaseKey)
        {
            var result = new WriteBehindResult { Success = true };
            var failedOps = new List<WriteOperation>();

            // 第一轮：尝试直接写数据库
            foreach (var op in operations)
            {
                try
                {
                    var writeResult = ExecuteSingleOperation(op, databaseKey);
                    if (writeResult.Success)
                    {
                        result.DirectWriteCount++;
                        result.Details.Add(writeResult);
                    }
                    else
                    {
                        // 写入失败，加入降级队列
                        failedOps.Add(op);
                    }
                }
                catch (Exception ex)
                {
                    // 数据库异常，加入降级队列
                    failedOps.Add(op);
                    XTrace.WriteLine($"[WriteBehind] 数据库写入失败，将降级到队列: {op.TableName} - {ex.Message}");
                }
            }

            // 第二轮：将失败的操作写入消息队列
            if (failedOps.Count > 0)
            {
                try
                {
                    EnsureInitialized();
                    var topic = config.Topic ?? operations[0].TableName.ToLower();
                    var queueType = config.QueueType == WriteBehindQueueType.ReliableQueue
                        ? MessageQueueType.ReliableQueue
                        : MessageQueueType.Stream;

                    var queuedCount = _mqService.PublishData(topic, failedOps, queueType);
                    result.QueuedCount += queuedCount;
                    result.FallbackOccurred = true;

                    for (var i = 0; i < failedOps.Count; i++)
                    {
                        var op = failedOps[i];
                        var queued = i < queuedCount;

                        if (!queued)
                            result.FailedCount++;

                        result.Details.Add(new WriteOperationResult
                        {
                            OperationType = op.OperationType,
                            TableName = op.TableName,
                            Success = queued,
                            UsedQueue = queued,
                            ErrorMessage = queued ? null : "写入队列失败，操作未持久化"
                        });
                    }

                    if (queuedCount < failedOps.Count)
                        result.Success = false;

                    XTrace.WriteLine($"[WriteBehind] 已将 {queuedCount}/{failedOps.Count} 条操作降级到队列: {topic}");
                }
                catch (Exception ex)
                {
                    // 队列也失败了
                    result.FailedCount += failedOps.Count;
                    result.Success = false;
                    XTrace.WriteLine($"[WriteBehind] 队列写入也失败: {ex.Message}");

                    foreach (var op in failedOps)
                    {
                        result.Details.Add(new WriteOperationResult
                        {
                            OperationType = op.OperationType,
                            TableName = op.TableName,
                            Success = false,
                            ErrorMessage = $"数据库和队列均失败: {ex.Message}"
                        });
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// 执行单个写入操作
        /// </summary>
        private static WriteOperationResult ExecuteSingleOperation(WriteOperation operation, string databaseKey)
        {
            var result = new WriteOperationResult
            {
                OperationType = operation.OperationType,
                TableName = operation.TableName
            };

            try
            {
                var key = operation.DatabaseKey ?? databaseKey;
                var writeReturn = QueueWriteInvoker.Execute(operation, key);

                result.Success = writeReturn.IsSuccess;
                if (!writeReturn.IsSuccess)
                {
                    result.ErrorMessage = writeReturn.Message;
                }
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = ex.Message;
                throw; // 重新抛出，让上层处理降级
            }

            return result;
        }

        /// <summary>
        /// 按表名分组操作
        /// </summary>
        private static Dictionary<string, List<WriteOperation>> GroupByTable(List<WriteOperation> operations)
        {
            var groups = new Dictionary<string, List<WriteOperation>>(StringComparer.OrdinalIgnoreCase);
            foreach (var op in operations)
            {
                if (!groups.TryGetValue(op.TableName, out var list))
                {
                    list = new List<WriteOperation>();
                    groups[op.TableName] = list;
                }
                list.Add(op);
            }
            return groups;
        }

        /// <summary>
        /// 合并结果
        /// </summary>
        private static void MergeResult(WriteBehindResult target, WriteBehindResult source)
        {
            target.DirectWriteCount += source.DirectWriteCount;
            target.QueuedCount += source.QueuedCount;
            target.FailedCount += source.FailedCount;
            target.FallbackOccurred = target.FallbackOccurred || source.FallbackOccurred;
            target.Details.AddRange(source.Details);

            if (!source.Success)
            {
                target.Success = false;
            }
        }

        /// <summary>
        /// 确保已初始化
        /// 使用双重检查锁定模式确保线程安全
        /// </summary>
        private static void EnsureInitialized()
        {
            if (!_initialized)
            {
                lock (_lock)
                {
                    if (!_initialized)
                    {
                        Initialize(); // 使用默认连接
                    }
                }
            }
        }
    }
}
#endif
