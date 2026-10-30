using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data.Common;
using FastUntility.Monitor;

namespace FastData.ConnectionPool
{
    /// <summary>
    /// 连接池工厂
    /// </summary>
    /// <remarks>
    /// 实现 <see cref="IConnectionPoolInfoProvider"/> 后可直接作为远程指令（dbstatus / dbclose / dbrestart）
    /// 的连接池数据源；实现 <see cref="IConnectionPoolReopenProvider"/> 后 dbrestart 才会真正重建并预热连接池。
    /// </remarks>
    public class ConnectionPoolFactory : IDisposable, IConnectionPoolInfoProvider, IConnectionPoolReopenProvider
    {
        private static readonly Lazy<ConnectionPoolFactory> _instance = 
            new Lazy<ConnectionPoolFactory>(() => new ConnectionPoolFactory());

        private readonly ConcurrentDictionary<string, SmartConnectionPool> _pools;
        // 记录每个池的构造参数，供重启时按原样重建（Factory 自身不持有连接串，无法自行推导）
        private readonly ConcurrentDictionary<string, PoolBuildArgs> _buildArgs;
        private bool _disposed;

        /// <summary>
        /// 单例实例
        /// </summary>
        public static ConnectionPoolFactory Instance => _instance.Value;

        private ConnectionPoolFactory()
        {
            _pools = new ConcurrentDictionary<string, SmartConnectionPool>();
            _buildArgs = new ConcurrentDictionary<string, PoolBuildArgs>();
        }

        /// <summary>
        /// 获取或创建连接池
        /// </summary>
        /// <param name="name">连接池名称</param>
        /// <param name="connectionFactory">连接工厂</param>
        /// <param name="config">连接池配置</param>
        /// <returns>智能连接池</returns>
        public SmartConnectionPool GetOrCreatePool(string name, Func<DbConnection> connectionFactory, ConnectionPoolConfig config = null)
        {
            if (connectionFactory != null)
                _buildArgs[name] = new PoolBuildArgs(connectionFactory, config);

            return _pools.GetOrAdd(name, key => new SmartConnectionPool(key, connectionFactory, config));
        }

        /// <summary>
        /// 获取连接池
        /// </summary>
        public SmartConnectionPool GetPool(string name)
        {
            _pools.TryGetValue(name, out var pool);
            return pool;
        }

        /// <summary>
        /// 移除连接池
        /// </summary>
        public bool RemovePool(string name)
        {
            _buildArgs.TryRemove(name, out _);

            if (_pools.TryRemove(name, out var pool))
            {
                pool.Dispose();
                return true;
            }
            return false;
        }

        /// <summary>
        /// 关闭连接池
        /// </summary>
        public void ClosePool(string name)
        {
            if (_pools.TryRemove(name, out var pool))
            {
                pool.Dispose();
            }
        }

        /// <summary>
        /// 重建连接池并预热
        /// </summary>
        /// <param name="poolName">连接池名称</param>
        /// <param name="message">结果说明</param>
        /// <returns>是否重启成功</returns>
        public bool TryReopenPool(string poolName, out string message)
        {
            if (string.IsNullOrEmpty(poolName))
            {
                message = "连接池名称不能为空";
                return false;
            }

            if (!_buildArgs.TryGetValue(poolName, out var args) || args.ConnectionFactory == null)
            {
                message = string.Format("未找到连接池 {0} 的创建参数，无法重建；请先通过 GetOrCreatePool 创建", poolName);
                return false;
            }

            ClosePool(poolName);

            try
            {
                // 重建时沿用原构造参数，SmartConnectionPool 构造函数内部会按 MinPoolSize 预热并校验连接
                var pool = GetOrCreatePool(poolName, args.ConnectionFactory, args.Config);
                var metrics = pool?.GetMetrics();

                if (metrics == null || metrics.TotalConnections <= 0)
                {
                    message = string.Format("连接池 {0} 已重建，但预热未建立任何连接（请检查数据库连通性）", poolName);
                    return false;
                }

                message = string.Format("已重启连接池 {0}，预热 {1} 个连接（活跃 {2} / 空闲 {3}）",
                    poolName, metrics.TotalConnections, metrics.ActiveConnections, metrics.IdleConnections);
                return true;
            }
            catch (Exception ex)
            {
                message = string.Format("重建连接池 {0} 失败: {1}", poolName, ex.Message);
                return false;
            }
        }

        /// <summary>
        /// 获取所有连接池的概要信息（远程指令 dbstatus 使用）
        /// </summary>
        public Dictionary<string, ConnectionPoolInfo> GetAllPoolInfo()
        {
            var result = new Dictionary<string, ConnectionPoolInfo>();

            foreach (var kvp in _pools)
            {
                var metrics = kvp.Value.GetMetrics();
                if (metrics == null)
                    continue;

                result[kvp.Key] = new ConnectionPoolInfo
                {
                    ActiveConnections = metrics.ActiveConnections,
                    IdleConnections = metrics.IdleConnections,
                    TotalConnections = metrics.TotalConnections,
                    PendingRequests = metrics.WaitingRequests
                };
            }

            return result;
        }

        /// <summary>
        /// 关闭所有连接池
        /// </summary>
        public void CloseAllPools()
        {
            foreach (var pool in _pools.Values)
            {
                pool.Dispose();
            }
            _pools.Clear();
        }

        /// <summary>
        /// 获取所有连接池指标
        /// </summary>
        public Dictionary<string, ConnectionPoolMetrics> GetAllMetrics()
        {
            var metrics = new Dictionary<string, ConnectionPoolMetrics>();
            foreach (var kvp in _pools)
            {
                metrics[kvp.Key] = kvp.Value.GetMetrics();
            }
            return metrics;
        }

        /// <summary>
        /// 释放资源
        /// </summary>
        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
                foreach (var pool in _pools.Values)
                {
                    pool.Dispose();
                }
                _pools.Clear();
                _buildArgs.Clear();
            }
        }

        /// <summary>
        /// 连接池构造参数快照
        /// </summary>
        private sealed class PoolBuildArgs
        {
            public PoolBuildArgs(Func<DbConnection> connectionFactory, ConnectionPoolConfig config)
            {
                ConnectionFactory = connectionFactory;
                Config = config;
            }

            public Func<DbConnection> ConnectionFactory { get; }

            public ConnectionPoolConfig Config { get; }
        }
    }
}