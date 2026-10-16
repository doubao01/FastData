using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FastData.Config;
using FastData.Context;

namespace FastData.DevTools
{
    /// <summary>
    /// 数据库连接池管理器
    /// </summary>
    public static class ConnectionPoolManager
    {
        private static readonly ConcurrentDictionary<string, ConnectionPool> _pools = new ConcurrentDictionary<string, ConnectionPool>();
        private static readonly object _lock = new object();

        /// <summary>
        /// 创建连接池
        /// </summary>
        public static ConnectionPool CreatePool(string dbKey, ConnectionPoolOptions options = null)
        {
            if (_pools.ContainsKey(dbKey))
            {
                return _pools[dbKey];
            }

            lock (_lock)
            {
                if (_pools.ContainsKey(dbKey))
                {
                    return _pools[dbKey];
                }

                options = options ?? ConnectionPoolOptions.Default;
                var config = DataConfig.GetConfig(dbKey);
                if (config == null)
                {
                    throw new ArgumentException(string.Format("找不到数据库配置: {0}", dbKey));
                }

                var pool = new ConnectionPool(dbKey, config, options);
                _pools.TryAdd(dbKey, pool);
                return pool;
            }
        }

        /// <summary>
        /// 获取连接池
        /// </summary>
        public static ConnectionPool GetPool(string dbKey)
        {
            if (_pools.TryGetValue(dbKey, out var pool))
            {
                return pool;
            }
            return CreatePool(dbKey);
        }

        /// <summary>
        /// 获取连接
        /// </summary>
        public static PooledConnection GetConnection(string dbKey)
        {
            var pool = GetPool(dbKey);
            return pool.GetConnection();
        }

        /// <summary>
        /// 异步获取连接
        /// </summary>
        public static async Task<PooledConnection> GetConnectionAsync(string dbKey)
        {
            var pool = GetPool(dbKey);
            return await pool.GetConnectionAsync();
        }

        /// <summary>
        /// 获取连接池统计
        /// </summary>
        public static ConnectionPoolStats GetStats(string dbKey)
        {
            var pool = GetPool(dbKey);
            return pool.GetStats();
        }

        /// <summary>
        /// 清理连接池
        /// </summary>
        public static void Cleanup(string dbKey)
        {
            if (_pools.TryGetValue(dbKey, out var pool))
            {
                pool.Cleanup();
            }
        }

        /// <summary>
        /// 清理所有连接池
        /// </summary>
        public static void CleanupAll()
        {
            foreach (var pool in _pools.Values)
            {
                pool.Cleanup();
            }
        }

        /// <summary>
        /// 重置连接池
        /// </summary>
        public static void Reset(string dbKey)
        {
            if (_pools.TryRemove(dbKey, out var pool))
            {
                pool.Dispose();
            }
        }

        /// <summary>
        /// 重置所有连接池
        /// </summary>
        public static void ResetAll()
        {
            foreach (var key in _pools.Keys.ToList())
            {
                Reset(key);
            }
        }
    }

    /// <summary>
    /// 连接池
    /// </summary>
    public class ConnectionPool : IDisposable
    {
        private readonly string _dbKey;
        internal readonly object _config;
        private readonly ConnectionPoolOptions _options;
        private readonly ConcurrentBag<PooledConnection> _idleConnections = new ConcurrentBag<PooledConnection>();
        private readonly ConcurrentBag<PooledConnection> _activeConnections = new ConcurrentBag<PooledConnection>();
        private int _totalConnections;
        private bool _disposed;

        internal ConnectionPool(string dbKey, object config, ConnectionPoolOptions options)
        {
            _dbKey = dbKey;
            _config = config;
            _options = options;
        }

        /// <summary>
        /// 同步获取一个连接
        /// </summary>
        /// <returns>池化连接</returns>
        public PooledConnection GetConnection()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(ConnectionPool));

            // 尝试从空闲连接获取
            if (_idleConnections.TryTake(out var connection))
            {
                if (connection.IsHealthy())
                {
                    _activeConnections.Add(connection);
                    return connection;
                }
                else
                {
                    connection.Dispose();
                    Interlocked.Decrement(ref _totalConnections);
                }
            }

            // 创建新连接
            if (_totalConnections < _options.MaxPoolSize || _options.MaxPoolSize <= 0)
            {
                connection = CreateNewConnection();
                _activeConnections.Add(connection);
                return connection;
            }

            // 等待空闲连接
            throw new TimeoutException(string.Format("连接池已满，无法获取连接 (MaxPoolSize: {0})", _options.MaxPoolSize));
        }

        /// <summary>
        /// 异步获取一个连接
        /// </summary>
        /// <returns>池化连接</returns>
        public async Task<PooledConnection> GetConnectionAsync()
        {
            return await Task.Run(() => GetConnection());
        }

        private PooledConnection CreateNewConnection()
        {
            var db = new DataContext(_dbKey);
            var connection = new PooledConnection(db, this);
            Interlocked.Increment(ref _totalConnections);
            return connection;
        }

        internal void ReturnConnection(PooledConnection connection)
        {
            if (connection.IsHealthy() && !_disposed)
            {
                _activeConnections.TryTake(out _);
                _idleConnections.Add(connection);
            }
            else
            {
                _activeConnections.TryTake(out _);
                connection.Dispose();
                Interlocked.Decrement(ref _totalConnections);
            }
        }

        /// <summary>
        /// 获取连接池统计信息
        /// </summary>
        /// <returns>连接池统计</returns>
        public ConnectionPoolStats GetStats()
        {
            return new ConnectionPoolStats
            {
                DbKey = _dbKey,
                TotalConnections = _totalConnections,
                ActiveConnections = _activeConnections.Count,
                IdleConnections = _idleConnections.Count,
                MaxPoolSize = _options.MaxPoolSize,
                MinPoolSize = _options.MinPoolSize
            };
        }

        /// <summary>
        /// 清理空闲连接
        /// </summary>
        public void Cleanup()
        {
            while (_idleConnections.TryTake(out var connection))
            {
                connection.Dispose();
                Interlocked.Decrement(ref _totalConnections);
            }
        }

        /// <summary>
        /// 释放连接池及其所有连接
        /// </summary>
        public void Dispose()
        {
            if (_disposed) return;

            _disposed = true;

            Cleanup();

            while (_activeConnections.TryTake(out var connection))
            {
                connection.Dispose();
                Interlocked.Decrement(ref _totalConnections);
            }
        }
    }

    /// <summary>
    /// 池化连接
    /// </summary>
    public class PooledConnection : IDisposable
    {
        private readonly DataContext _context;
        private readonly ConnectionPool _pool;
        private bool _disposed;

        /// <summary>
        /// 初始化池化连接
        /// </summary>
        /// <param name="context">数据上下文</param>
        /// <param name="pool">所属连接池</param>
        public PooledConnection(DataContext context, ConnectionPool pool)
        {
            _context = context;
            _pool = pool;
            CreatedAt = DateTime.Now;
        }

        /// <summary>
        /// 连接创建时间
        /// </summary>
        public DateTime CreatedAt { get; }
        /// <summary>
        /// 数据上下文
        /// </summary>
        public DataContext Context => _context;
        /// <summary>
        /// 底层数据库连接
        /// </summary>
        public IDbConnection Connection => _context.conn;
        /// <summary>
        /// 数据库命令
        /// </summary>
        public IDbCommand Command => _context.cmd;

        /// <summary>
        /// 判断连接是否健康可用
        /// </summary>
        /// <returns>是否健康</returns>
        public bool IsHealthy()
        {
            try
            {
                if (Connection == null || Connection.State != ConnectionState.Open)
                    return false;

                return !(Connection is IDbConnection conn && DateTime.Now - CreatedAt > TimeSpan.FromMinutes(30));
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 归还连接到连接池
        /// </summary>
        public void Dispose()
        {
            if (_disposed) return;

            _disposed = true;

            if (_pool != null && !_disposed)
            {
                _pool.ReturnConnection(this);
            }
            else
            {
                _context?.Dispose();
            }
        }
    }

    /// <summary>
    /// 连接池选项
    /// </summary>
    public class ConnectionPoolOptions
    {
        /// <summary>
        /// 最小连接数
        /// </summary>
        public int MinPoolSize { get; set; } = 5;
        /// <summary>
        /// 最大连接数
        /// </summary>
        public int MaxPoolSize { get; set; } = 100;
        /// <summary>
        /// 连接超时时间
        /// </summary>
        public TimeSpan ConnectionTimeout { get; set; } = TimeSpan.FromSeconds(30);
        /// <summary>
        /// 连接生命周期
        /// </summary>
        public TimeSpan ConnectionLifetime { get; set; } = TimeSpan.FromMinutes(30);
        /// <summary>
        /// 是否启用统计
        /// </summary>
        public bool EnableStatistics { get; set; } = true;

        /// <summary>
        /// 默认选项
        /// </summary>
        public static ConnectionPoolOptions Default => new ConnectionPoolOptions();
    }

    /// <summary>
    /// 连接池统计
    /// </summary>
    public class ConnectionPoolStats
    {
        /// <summary>
        /// 数据库标识
        /// </summary>
        public string DbKey { get; set; }
        /// <summary>
        /// 连接总数
        /// </summary>
        public int TotalConnections { get; set; }
        /// <summary>
        /// 活动连接数
        /// </summary>
        public int ActiveConnections { get; set; }
        /// <summary>
        /// 空闲连接数
        /// </summary>
        public int IdleConnections { get; set; }
        /// <summary>
        /// 最大连接数
        /// </summary>
        public int MaxPoolSize { get; set; }
        /// <summary>
        /// 最小连接数
        /// </summary>
        public int MinPoolSize { get; set; }

        /// <summary>
        /// 连接使用率百分比
        /// </summary>
        public double UsagePercentage => MaxPoolSize > 0 ? (double)TotalConnections / MaxPoolSize * 100 : 0;

        /// <summary>
        /// 返回连接池统计的字符串表示
        /// </summary>
        /// <returns>字符串表示</returns>
        public override string ToString()
        {
            return string.Format("DbKey: {0}, Active: {1}, Idle: {2}, Total: {3}/{4} ({5:F1}%)", DbKey, ActiveConnections, IdleConnections, TotalConnections, MaxPoolSize, UsagePercentage);
        }
    }
}