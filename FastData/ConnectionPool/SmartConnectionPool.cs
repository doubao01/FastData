using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FastUntility.Base;

namespace FastData.ConnectionPool
{
    /// <summary>
    /// 连接池配置
    /// </summary>
    public class ConnectionPoolConfig
    {
        // 连接池大小计算常量
        private const double CpuMultiplierForIO = 1.5;       // 数据库属于 IO 密集型，取 CPU 核心数 * 1.5
        private const double MemoryReservationPercent = 0.1; // 预留 10% 内存给连接池
        private const double BytesPerConnectionMB = 2;       // 每个连接约占用 2MB 内存
        private const int MinPoolSizeMinValue = 2;           // 最小连接数下限
        private const int MinPoolSizeFraction = 10;          // 最小连接数 = 最大连接数 / 10
        private const int MaxPoolSizeMinValue = 10;          // 最大连接数下限
        private const int MaxPoolSizeMaxValue = 200;         // 最大连接数上限

        /// <summary>
        /// 最小连接数（-1 表示根据环境自动计算）
        /// </summary>
        public int MinPoolSize { get; set; } = -1;

        /// <summary>
        /// 最大连接数（-1 表示根据环境自动计算）
        /// </summary>
        public int MaxPoolSize { get; set; } = -1;

        /// <summary>
        /// 是否根据环境自动调整连接池大小
        /// </summary>
        public bool AutoAdjustByEnvironment { get; set; } = true;

        /// <summary>
        /// 根据系统资源自动计算连接池配置
        /// </summary>
        public void AdjustByEnvironment()
        {
            if (!AutoAdjustByEnvironment)
                return;

            // 获取系统资源信息
            var processorCount = Environment.ProcessorCount;
#if !NETFRAMEWORK
            var memoryMB = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / (1024 * 1024);
#else
            // .NET Framework 4.5.2 不支持 GC.GetGCMemoryInfo，使用默认值
            var memoryMB = 2048;
#endif

            // 根据 CPU 核心数计算池大小
            var cpuBasedMax = (int)(processorCount * CpuMultiplierForIO);

            // 根据内存计算池大小
            var memoryBasedMax = (int)(memoryMB * MemoryReservationPercent / BytesPerConnectionMB);

            // 如果 MinPoolSize 未设置，设为 MaxPoolSize 的 10%
            if (MinPoolSize < 0)
            {
                MinPoolSize = Math.Max(MinPoolSizeMinValue, cpuBasedMax / MinPoolSizeFraction);
            }

            // 如果 MaxPoolSize 未设置，取 CPU 和内存计算的较小值
            if (MaxPoolSize < 0)
            {
                MaxPoolSize = Math.Min(cpuBasedMax, memoryBasedMax);
                // 限制范围
                MaxPoolSize = Math.Max(MaxPoolSizeMinValue, Math.Min(MaxPoolSizeMaxValue, MaxPoolSize));
            }

            // 确保 MinPoolSize 不超过 MaxPoolSize
            MinPoolSize = Math.Min(MinPoolSize, MaxPoolSize);
        }

        /// <summary>
        /// 连接超时时间（秒）
        /// </summary>
        public int ConnectionTimeout { get; set; } = 30;

        /// <summary>
        /// 连接生命周期（分钟）
        /// </summary>
        public int ConnectionLifetime { get; set; } = 30;

        /// <summary>
        /// 健康检查间隔（秒）
        /// </summary>
        public int HealthCheckInterval { get; set; } = 60;

        /// <summary>
        /// 泄漏检测阈值（秒）
        /// </summary>
        public int LeakDetectionThreshold { get; set; } = 300;

        /// <summary>
        /// 是否启用智能调整
        /// </summary>
        public bool EnableSmartAdjustment { get; set; } = true;

        /// <summary>
        /// 负载阈值（百分比）
        /// </summary>
        public int LoadThreshold { get; set; } = 80;

        /// <summary>
        /// 缩容阈值（百分比）
        /// </summary>
        public int ShrinkThreshold { get; set; } = 30;

        /// <summary>
        /// 连接创建最大重试次数
        /// </summary>
        public int MaxRetries { get; set; } = 3;

        /// <summary>
        /// 连接创建重试基础延迟（毫秒）
        /// </summary>
        public int RetryBaseDelayMs { get; set; } = 50;

        /// <summary>
        /// 连接验证命令超时（秒）
        /// </summary>
        public int ValidationCommandTimeout { get; set; } = 5;

        /// <summary>
        /// 智能调整间隔（秒）
        /// </summary>
        public int SmartAdjustmentInterval { get; set; } = 30;

        /// <summary>
        /// 每次最大扩容数量
        /// </summary>
        public int MaxExpandCount { get; set; } = 10;

        /// <summary>
        /// 每次最大缩容数量
        /// </summary>
        public int MaxShrinkCount { get; set; } = 5;

        /// <summary>
        /// 异常缩容阈值（连续失败次数达到此值时触发缩容）
        /// </summary>
        public int ErrorShrinkThreshold { get; set; } = 3;

        /// <summary>
        /// 异常缩容比例（每次缩容减少的百分比）
        /// </summary>
        public int ErrorShrinkPercentage { get; set; } = 20;

        /// <summary>
        /// 是否启用 Redis 可用性检测（Redis 可用时允许扩容）
        /// </summary>
        public bool EnableRedisCheck { get; set; } = false;

        /// <summary>
        /// Redis 连接字符串（用于可用性检测）
        /// </summary>
        public string RedisConnectionString { get; set; }

        /// <summary>
        /// 熔断器配置
        /// </summary>
        public CircuitBreakerConfig CircuitBreaker { get; set; } = new CircuitBreakerConfig();
    }

    /// <summary>
    /// 熔断器状态
    /// </summary>
    public enum CircuitState
    {
        /// <summary>The circuit is closed and requests are allowed.</summary>
        Closed,      // 正常状态
        /// <summary>The circuit is open and requests are blocked.</summary>
        Open,        // 熔断状态
        /// <summary>The circuit is testing whether the dependency has recovered.</summary>
        HalfOpen     // 半开状态（测试恢复）
    }

    /// <summary>
    /// 熔断器配置
    /// </summary>
    public class CircuitBreakerConfig
    {
        /// <summary>
        /// 是否启用熔断器
        /// </summary>
        public bool Enabled { get; set; } = true;

        /// <summary>
        /// 连续失败阈值（达到此值触发熔断）
        /// </summary>
        public int FailureThreshold { get; set; } = 5;

        /// <summary>
        /// 熔断时长（秒，之后进入半开状态）
        /// </summary>
        public int CircuitOpenDurationSec { get; set; } = 30;

        /// <summary>
        /// 半开状态下允许的最大测试请求数
        /// </summary>
        public int HalfOpenMaxRequests { get; set; } = 3;
    }

    /// <summary>
    /// 连接包装器
    /// </summary>
    public class PooledConnection : IDisposable
    {
        private readonly SmartConnectionPool _pool;
        private bool _disposed;

        /// <summary>Gets the underlying database connection.</summary>
        public DbConnection Connection { get; }
        /// <summary>Gets the pooled connection identifier.</summary>
        public Guid Id { get; } = Guid.NewGuid();
        /// <summary>Gets the UTC creation time.</summary>
        public DateTime CreatedAt { get; } = DateTime.UtcNow;
        /// <summary>Gets the UTC time when the connection was last used.</summary>
        public DateTime LastUsedAt { get; private set; } = DateTime.UtcNow;
        /// <summary>Gets how many times the connection has been checked out.</summary>
        public int UseCount { get; private set; }
        /// <summary>Gets whether the connection is currently checked out.</summary>
        public bool IsInUse { get; internal set; }
        /// <summary>Gets the last caller recorded for leak diagnostics.</summary>
        public string LastUsedBy { get; private set; }

        internal PooledConnection(DbConnection connection, SmartConnectionPool pool)
        {
            Connection = connection ?? throw new ArgumentNullException(nameof(connection));
            _pool = pool ?? throw new ArgumentNullException(nameof(pool));
        }

        internal void MarkUsed(string caller = null)
        {
            _disposed = false;
            LastUsedAt = DateTime.UtcNow;
            UseCount++;
            IsInUse = true;
            LastUsedBy = caller;
        }

        internal void MarkReturned()
        {
            IsInUse = false;
            LastUsedBy = null;
        }

        /// <summary>
        /// 终结器：确保即使调用方忘记 Dispose 也能释放底层数据库连接
        /// </summary>
        ~PooledConnection()
        {
            Dispose(false);
        }

        /// <summary>
        /// Returns the connection to its owning pool.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Releases this pooled connection.
        /// </summary>
        /// <param name="disposing">True when called from Dispose; false from the finalizer.</param>
        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                _disposed = true;
                if (disposing)
                {
                    _pool.ReturnConnection(this);
                }
                else
                {
                    // 走到终结器说明调用方没有 Dispose：连接没有归还池，
                    // 对应的信号量许可也永远不会释放（池会逐渐假性耗尽）。
                    // 终结器线程里不能安全地把连接还给池（_pool 可能已被回收），
                    // 只能直接释放底层连接并留下可发现的痕迹。
                    System.Diagnostics.Trace.TraceWarning(string.Format(
                        "PooledConnection {0} 未归还即被终结：调用方遗漏了 Dispose，连接池许可已泄漏。",
                        Id));
                    try { Connection?.Dispose(); } catch { }
                }
            }
        }
    }

    /// <summary>
    /// 连接池指标
    /// </summary>
    public class ConnectionPoolMetrics
    {
        /// <summary>Gets or sets total connections owned by the pool.</summary>
        public int TotalConnections { get; set; }
        /// <summary>Gets or sets checked-out connection count.</summary>
        public int ActiveConnections { get; set; }
        /// <summary>Gets or sets idle connection count.</summary>
        public int IdleConnections { get; set; }
        /// <summary>Gets or sets waiting request count.</summary>
        public int WaitingRequests { get; set; }
        /// <summary>Gets or sets total checkout request count.</summary>
        public long TotalRequests { get; set; }
        /// <summary>Gets or sets successful checkout request count.</summary>
        public long SuccessfulRequests { get; set; }
        /// <summary>Gets or sets failed checkout request count.</summary>
        public long FailedRequests { get; set; }
        /// <summary>Gets or sets detected leaked connection count.</summary>
        public long LeakedConnections { get; set; }
        /// <summary>Gets or sets average wait time in milliseconds.</summary>
        public double AverageWaitTimeMs { get; set; }
        /// <summary>Gets or sets average checked-out duration in milliseconds.</summary>
        public double AverageUseTimeMs { get; set; }
        /// <summary>Gets or sets the last health-check timestamp.</summary>
        public DateTime LastHealthCheck { get; set; }
        /// <summary>Gets or sets pool uptime.</summary>
        public TimeSpan Uptime { get; set; }
    }

    /// <summary>
    /// 智能连接池管理器
    /// </summary>
    public class SmartConnectionPool : IDisposable
    {
        // 环境检测常量
        private const double HighMemoryUsageThresholdPercent = 85; // 内存使用率超过此值时限制扩容
        private const double LowMemoryExpandFactor = 0.5;          // 内存高时扩容因子
        private const double RecentExpandExpandFactor = 0.3;       // 近期已扩容时的扩容因子
        private const double ErrorExpandFactor = 0.5;              // 有错误时的扩容因子
        private const int RedisCheckIntervalSeconds = 30;          // Redis 可用性检测间隔
        private const int ExpandFrequencySeconds = 60;             // 扩容频率间隔
        private const int RedisConnectTimeoutSeconds = 2;          // Redis 连接超时

        private readonly string _name;
        private readonly ConnectionPoolConfig _config;
        private readonly Func<DbConnection> _connectionFactory;
        private readonly ConcurrentBag<PooledConnection> _availableConnections;
        private readonly ConcurrentDictionary<Guid, PooledConnection> _inUseConnections;
        private readonly SemaphoreSlim _semaphore;
        private readonly Timer _healthCheckTimer;
        private readonly Timer _smartAdjustmentTimer;
        private readonly object _lock = new object();
        private readonly object _adjustmentLock = new object();
        private readonly object _circuitLock = new object();
        private bool _disposed;
        private bool _sqlLog;
        private Action<string> _logCallback;

        // 熔断器字段
        private CircuitState _circuitState = CircuitState.Closed;
        private int _consecutiveFailures;
        private DateTime _circuitOpenedAt;
        private int _halfOpenRequests;

        // 智能调整字段
        private long _recentErrors;
        private DateTime _lastErrorTime;
        private DateTime _lastExpandTime;
        private bool _isRedisAvailable;
        private DateTime _lastRedisCheckTime;

        /// <summary>
        /// 启用连接池日志输出
        /// </summary>
        public void EnableSqlLog(Action<string> logCallback = null)
        {
            _sqlLog = true;
            if (logCallback != null)
                _logCallback = logCallback;
        }

        /// <summary>
        /// 日志输出（受_sqlLog控制，支持自定义回调）
        /// </summary>
        private void Log(string message)
        {
            if (!_sqlLog)
                return;

            if (_logCallback != null)
                _logCallback.Invoke(string.Format("连接池 {0}: {1}", _name, message));
            else
                BaseLog.SaveLog(string.Format("连接池 {0}: {1}", _name, message), "ConnectionPool");
        }

        // 指标
        private long _totalRequests;
        private long _successfulRequests;
        private long _failedRequests;
        private long _leakedConnections;
        private long _totalWaitTimeMs;
        private DateTime _startTime;

        // 原子计数器（避免频繁 ConcurrentBag.Count 遍历）
        private int _totalCount;
        private int _activeCount;

        /// <summary>
        /// 连接池名称
        /// </summary>
        public string Name => _name;

        /// <summary>
        /// 连接池配置
        /// </summary>
        public ConnectionPoolConfig Config => _config;

        /// <summary>
        /// 当前指标
        /// </summary>
        public ConnectionPoolMetrics Metrics => GetMetrics();

        /// <summary>
        /// Initializes a smart connection pool.
        /// </summary>
        /// <param name="name">Pool name.</param>
        /// <param name="connectionFactory">Factory used to create database connections.</param>
        /// <param name="config">Optional pool configuration.</param>
        public SmartConnectionPool(string name, Func<DbConnection> connectionFactory, ConnectionPoolConfig config = null)
        {
            _name = name ?? throw new ArgumentNullException(nameof(name));
            _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
            _config = config ?? new ConnectionPoolConfig();

            _availableConnections = new ConcurrentBag<PooledConnection>();
            _inUseConnections = new ConcurrentDictionary<Guid, PooledConnection>();
            
            // 根据环境自动调整连接池大小
            _config.AdjustByEnvironment();
            
            _semaphore = new SemaphoreSlim(_config.MaxPoolSize, _config.MaxPoolSize);
            _startTime = DateTime.UtcNow;

            // 预创建最小连接数并验证有效性（连接预热）
            for (int i = 0; i < _config.MinPoolSize; i++)
            {
                var conn = CreateNewConnection();
                if (conn != null)
                {
                    // 验证连接有效性
                    if (ValidateConnection(conn))
                    {
                        _availableConnections.Add(conn);
                    }
                    else
                    {
                        // 预热失败，销毁并重试
                        Log(string.Format("连接池 {0} 预热连接失败，重试创建", _name));
                        DestroyConnection(conn);
                        var retryConn = CreateNewConnection();
                        if (retryConn != null)
                            _availableConnections.Add(retryConn);
                    }
                }
            }
            _totalCount = _availableConnections.Count;

            // 启动健康检查定时器
            _healthCheckTimer = new Timer(
                HealthCheckCallback,
                null,
                TimeSpan.FromSeconds(_config.HealthCheckInterval),
                TimeSpan.FromSeconds(_config.HealthCheckInterval));

            // 启动智能调整定时器
            if (_config.EnableSmartAdjustment)
            {
                _smartAdjustmentTimer = new Timer(
                    SmartAdjustmentCallback,
                    null,
                    TimeSpan.FromSeconds(_config.SmartAdjustmentInterval),
                    TimeSpan.FromSeconds(_config.SmartAdjustmentInterval));
            }
        }

        /// <summary>
        /// 获取连接
        /// </summary>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>池化连接</returns>
        public async Task<PooledConnection> GetConnectionAsync(CancellationToken cancellationToken = default)
        {
            // 熔断器检查
            if (!CanExecuteRequest())
            {
                Interlocked.Increment(ref _failedRequests);
                throw new CircuitBreakerOpenException(string.Format("连接池 {0} 熔断器打开，拒绝请求", _name));
            }

            var stopwatch = Stopwatch.StartNew();
            var permitAcquired = false;
            Interlocked.Increment(ref _totalRequests);

            try
            {
                // 等待可用连接
                if (!await _semaphore.WaitAsync(TimeSpan.FromSeconds(_config.ConnectionTimeout), cancellationToken))
                {
                    Interlocked.Increment(ref _failedRequests);
                    RecordFailure();
                    // 抛出连接池耗尽异常，用于触发降级到消息队列
                    throw new ConnectionPoolExhaustedException(_name, _config.ConnectionTimeout);
                }

                permitAcquired = true;
                stopwatch.Stop();
                Interlocked.Add(ref _totalWaitTimeMs, stopwatch.ElapsedMilliseconds);

                // 尝试从空闲连接中获取
                if (_availableConnections.TryTake(out var connection))
                {
                    // 检查连接是否过期
                    if (IsConnectionExpired(connection))
                    {
                        DestroyConnection(connection);
                        connection = await CreateNewConnectionAsync(cancellationToken);
                    }
                    else
                    {
                        // 验证连接有效性
                        if (!await ValidateConnectionAsync(connection))
                        {
                            DestroyConnection(connection);
                            connection = await CreateNewConnectionAsync(cancellationToken);
                        }
                    }
                }
                else
                {
                    // 创建新连接
                    connection = await CreateNewConnectionAsync(cancellationToken);
                }

                if (connection == null)
                {
                    _semaphore.Release();
                    permitAcquired = false;
                    Interlocked.Increment(ref _failedRequests);
                    RecordFailure();
                    throw new ConnectionPoolExhaustedException(_name,
                        string.Format("连接池 '{0}' 无法创建新连接，数据库可能不可达或认证失败", _name));
                }

                connection.MarkUsed();
                _inUseConnections.TryAdd(connection.Id, connection);
                Interlocked.Increment(ref _activeCount);
                Interlocked.Increment(ref _successfulRequests);
                RecordSuccess();

                permitAcquired = false;
                return connection;
            }
            catch (ConnectionPoolExhaustedException)
            {
                if (permitAcquired)
                    _semaphore.Release();

                throw;
            }
            catch
            {
                if (permitAcquired)
                    _semaphore.Release();

                Interlocked.Increment(ref _failedRequests);
                RecordFailure();
                throw;
            }
        }

        /// <summary>
        /// 获取连接（同步版本，避免 GetAwaiter().GetResult() 死锁）
        /// </summary>
        public PooledConnection GetConnection()
        {
            // 熔断器检查
            if (!CanExecuteRequest())
            {
                Interlocked.Increment(ref _failedRequests);
                throw new CircuitBreakerOpenException(string.Format("连接池 {0} 熔断器打开，拒绝请求", _name));
            }

            var stopwatch = Stopwatch.StartNew();
            var permitAcquired = false;
            Interlocked.Increment(ref _totalRequests);

            try
            {
                // 等待可用连接（同步等待）
                if (!_semaphore.Wait(TimeSpan.FromSeconds(_config.ConnectionTimeout)))
                {
                    Interlocked.Increment(ref _failedRequests);
                    RecordFailure();
                    // 抛出连接池耗尽异常，用于触发降级到消息队列
                    throw new ConnectionPoolExhaustedException(_name, _config.ConnectionTimeout);
                }

                permitAcquired = true;
                stopwatch.Stop();
                Interlocked.Add(ref _totalWaitTimeMs, stopwatch.ElapsedMilliseconds);

                // 尝试从空闲连接中获取
                if (_availableConnections.TryTake(out var connection))
                {
                    // 检查连接是否过期
                    if (IsConnectionExpired(connection))
                    {
                        DestroyConnection(connection);
                        connection = CreateNewConnection();
                    }
                    else
                    {
                        // 验证连接有效性
                        if (!ValidateConnection(connection))
                        {
                            DestroyConnection(connection);
                            connection = CreateNewConnection();
                        }
                    }
                }
                else
                {
                    // 创建新连接
                    connection = CreateNewConnection();
                }

                if (connection == null)
                {
                    _semaphore.Release();
                    permitAcquired = false;
                    Interlocked.Increment(ref _failedRequests);
                    RecordFailure();
                    throw new ConnectionPoolExhaustedException(_name,
                        string.Format("连接池 '{0}' 无法创建新连接，数据库可能不可达或认证失败", _name));
                }

                connection.MarkUsed();
                _inUseConnections.TryAdd(connection.Id, connection);
                Interlocked.Increment(ref _activeCount);
                Interlocked.Increment(ref _successfulRequests);
                RecordSuccess();

                permitAcquired = false;
                return connection;
            }
            catch (ConnectionPoolExhaustedException)
            {
                if (permitAcquired)
                    _semaphore.Release();

                throw;
            }
            catch
            {
                if (permitAcquired)
                    _semaphore.Release();

                Interlocked.Increment(ref _failedRequests);
                RecordFailure();
                throw;
            }
        }

        /// <summary>
        /// 归还连接
        /// </summary>
        internal void ReturnConnection(PooledConnection connection)
        {
            if (connection == null) return;

            if (_inUseConnections.TryRemove(connection.Id, out var removed))
            {
                connection.MarkReturned();
                Interlocked.Decrement(ref _activeCount);

                // 仅检查连接是否过期，不在归还时执行 ValidateConnection（SELECT 1）
                // 连接有效性验证已在 GetConnection/GetConnectionAsync 取出时执行
                // 归还时验证会带来不必要的 SELECT 1 开销，且在连接已关闭时会误判销毁
                if (IsConnectionExpired(connection))
                {
                    DestroyConnection(connection);
                }
                else
                {
                    _availableConnections.Add(connection);
                }

                _semaphore.Release();
            }
        }

        /// <summary>
        /// 获取指标
        /// </summary>
        public ConnectionPoolMetrics GetMetrics()
        {
            return new ConnectionPoolMetrics
            {
                TotalConnections = _totalCount,
                ActiveConnections = _inUseConnections.Count,
                IdleConnections = Math.Max(0, _totalCount - _inUseConnections.Count),
                WaitingRequests = Math.Max(0, _config.MaxPoolSize - _semaphore.CurrentCount),
                TotalRequests = _totalRequests,
                SuccessfulRequests = _successfulRequests,
                FailedRequests = _failedRequests,
                LeakedConnections = _leakedConnections,
                AverageWaitTimeMs = _successfulRequests > 0 ? (double)_totalWaitTimeMs / _successfulRequests : 0,
                AverageUseTimeMs = 0,
                LastHealthCheck = DateTime.UtcNow,
                Uptime = DateTime.UtcNow - _startTime
            };
        }

        /// <summary>
        /// 创建新连接（带指数退避重试）
        /// </summary>
        private PooledConnection CreateNewConnection()
        {
            for (int attempt = 0; attempt <= _config.MaxRetries; attempt++)
            {
                try
                {
                    var connection = _connectionFactory();
                    if (connection != null)
                    {
                        connection.Open();
                        Interlocked.Increment(ref _totalCount);
                        return new PooledConnection(connection, this);
                    }
                }
                catch (Exception ex)
                {
                    if (attempt == _config.MaxRetries)
                    {
                        Log(string.Format("连接池 {0} 创建连接失败（已重试 {1} 次）: {2}", _name, _config.MaxRetries, ex.Message));
                        return null;
                    }

                    var delay = _config.RetryBaseDelayMs * (1 << attempt);
                    Log(string.Format("连接池 {0} 创建连接失败（第 {1} 次），{2}ms 后重试: {3}", _name, attempt + 1, delay, ex.Message));
                    Thread.Sleep(delay);
                }
            }
            return null;
        }

        /// <summary>
        /// 创建新连接（异步版本，带指数退避重试）
        /// </summary>
        private async Task<PooledConnection> CreateNewConnectionAsync(CancellationToken cancellationToken = default)
        {
            for (int attempt = 0; attempt <= _config.MaxRetries; attempt++)
            {
                try
                {
                    var connection = _connectionFactory();
                    if (connection != null)
                    {
                        connection.Open();
                        Interlocked.Increment(ref _totalCount);
                        return new PooledConnection(connection, this);
                    }
                }
                catch (Exception ex)
                {
                    if (attempt == _config.MaxRetries)
                    {
                        Log(string.Format("连接池 {0} 创建连接失败（已重试 {1} 次）: {2}", _name, _config.MaxRetries, ex.Message));
                        return null;
                    }

                    var delay = _config.RetryBaseDelayMs * (1 << attempt);
                    Log(string.Format("连接池 {0} 创建连接失败（第 {1} 次），{2}ms 后重试: {3}", _name, attempt + 1, delay, ex.Message));
                    await Task.Delay(delay, cancellationToken);
                }
            }
            return null;
        }

        /// <summary>
        /// 销毁连接
        /// </summary>
        private void DestroyConnection(PooledConnection connection)
        {
            try
            {
                // 从使用中连接集合中移除（仅当连接在使用中时）
                if (_inUseConnections.ContainsKey(connection.Id))
                {
                    _inUseConnections.TryRemove(connection.Id, out _);
                }
                connection.Connection?.Close();
                connection.Connection?.Dispose();
            }
            catch (Exception ex)
            {
                Log(string.Format("连接池 {0} 销毁连接失败: {1}", _name, ex.Message));
            }
            finally
            {
                // 计数递减必须放在 finally：Close()/Dispose() 抛异常时若跳过递减，
                // _totalCount 会永久漂移，池会逐渐「假性满员」。
                Interlocked.Decrement(ref _totalCount);
            }
        }

        /// <summary>
        /// 检查连接是否过期
        /// </summary>
        private bool IsConnectionExpired(PooledConnection connection)
        {
            return DateTime.UtcNow - connection.CreatedAt > TimeSpan.FromMinutes(_config.ConnectionLifetime);
        }

        /// <summary>
        /// 验证连接有效性（同步，执行 SELECT 1 验证数据库可达）
        /// </summary>
        private bool ValidateConnection(PooledConnection connection)
        {
            try
            {
                if (connection.Connection?.State != ConnectionState.Open)
                    return false;

                using var cmd = connection.Connection.CreateCommand();
                cmd.CommandText = "SELECT 1";
                cmd.CommandTimeout = _config.ValidationCommandTimeout;
                cmd.ExecuteScalar();
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 验证连接有效性（异步）
        /// </summary>
        private async Task<bool> ValidateConnectionAsync(PooledConnection connection)
        {
            try
            {
                if (connection.Connection?.State != ConnectionState.Open)
                    return false;

                // 执行简单查询验证连接
                using var cmd = connection.Connection.CreateCommand();
                cmd.CommandText = "SELECT 1";
                cmd.CommandTimeout = _config.ValidationCommandTimeout;
                await cmd.ExecuteScalarAsync();
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 健康检查回调
        /// </summary>
        private void HealthCheckCallback(object state)
        {
            try
            {
                // 清空空闲连接袋，逐个检查并重新添加健康的连接
                var allIdleConnections = new List<PooledConnection>();
                while (_availableConnections.TryTake(out var conn))
                {
                    allIdleConnections.Add(conn);
                }

                foreach (var conn in allIdleConnections)
                {
                    if (IsConnectionExpired(conn) || !ValidateConnection(conn))
                    {
                        DestroyConnection(conn);
                    }
                    else
                    {
                        _availableConnections.Add(conn);
                    }
                }

                // 检测泄漏连接
                foreach (var kvp in _inUseConnections)
                {
                    var connection = kvp.Value;
                    if (DateTime.UtcNow - connection.LastUsedAt > TimeSpan.FromSeconds(_config.LeakDetectionThreshold))
                    {
                        Interlocked.Increment(ref _leakedConnections);
                        Log(string.Format("连接池 {0} 检测到可能的连接泄漏: {1}, 最后使用时间: {2}", _name, connection.Id, connection.LastUsedAt));
                    }
                }

                // 确保最小连接数
                while (_totalCount < _config.MinPoolSize)
                {
                    var newConn = CreateNewConnection();
                    if (newConn != null)
                        _availableConnections.Add(newConn);
                    else
                        break;
                }
            }
            catch (Exception ex)
            {
                Log(string.Format("连接池 {0} 健康检查失败: {1}", _name, ex.Message));
            }
        }

        /// <summary>
        /// 智能调整回调（加锁防并发执行）
        /// </summary>
        private void SmartAdjustmentCallback(object state)
        {
            if (!Monitor.TryEnter(_adjustmentLock))
                return; // 已有调整在执行，跳过本次

            try
            {
                var metrics = GetMetrics();
                var loadPercentage = metrics.TotalConnections > 0 
                    ? (double)metrics.ActiveConnections / metrics.TotalConnections * 100 
                    : 0;

                // 检测环境状态
                var environmentStatus = CheckEnvironmentStatus();

                // 重新获取最新指标，避免竞态条件
                var currentMetrics = GetMetrics();

                // 1. 异常检测缩容：当近期错误多时，主动缩容
                if (_recentErrors >= _config.ErrorShrinkThreshold)
                {
                    var currentTotal = currentMetrics.TotalConnections;
                    var shrinkTarget = Math.Max(_config.MinPoolSize, 
                        currentTotal * (100 - _config.ErrorShrinkPercentage) / 100);
                    var shrinkCount = Math.Min(_config.MaxShrinkCount, currentTotal - shrinkTarget);
                    
                    if (shrinkCount > 0)
                    {
                        for (int i = 0; i < shrinkCount; i++)
                        {
                            if (_availableConnections.TryTake(out var conn))
                            {
                                DestroyConnection(conn);
                            }
                        }
                        Log(string.Format("连接池 {0} 异常缩容: 近期错误 {1} 次，移除 {2} 个连接", _name, _recentErrors, shrinkCount));
                    }
                    
                    // 重置错误计数
                    Interlocked.Exchange(ref _recentErrors, 0);
                }
                // 2. 负载过高时扩容（需要环境支持）
                else if (loadPercentage > _config.LoadThreshold && 
                         environmentStatus.CanExpand &&
                         currentMetrics.TotalConnections < _config.MaxPoolSize)
                {
                    var expandCount = Math.Min(_config.MaxExpandCount, 
                        _config.MaxPoolSize - currentMetrics.TotalConnections);
                    
                    // 根据环境状态调整扩容数量
                    if (environmentStatus.ExpandFactor < 1.0)
                    {
                        expandCount = Math.Max(1, (int)(expandCount * environmentStatus.ExpandFactor));
                    }
                    
                    for (int i = 0; i < expandCount; i++)
                    {
                        var newConn = CreateNewConnection();
                        if (newConn != null)
                            _availableConnections.Add(newConn);
                        else
                            break;
                    }
                    _lastExpandTime = DateTime.UtcNow;
                    Log(string.Format("连接池 {0} 扩容: 新增 {1} 个连接（负载 {2:F1}%）", _name, expandCount, loadPercentage));
                }
                // 3. 负载过低时缩容
                else if (loadPercentage < _config.ShrinkThreshold && currentMetrics.TotalConnections > _config.MinPoolSize)
                {
                    var shrinkCount = Math.Min(_config.MaxShrinkCount, currentMetrics.IdleConnections - _config.MinPoolSize);
                    if (shrinkCount > 0)
                    {
                        for (int i = 0; i < shrinkCount; i++)
                        {
                            if (_availableConnections.TryTake(out var conn))
                            {
                                DestroyConnection(conn);
                            }
                        }
                        Log(string.Format("连接池 {0} 缩容: 移除 {1} 个连接（负载 {2:F1}%）", _name, shrinkCount, loadPercentage));
                    }
                }
            }
            catch (Exception ex)
            {
                Log(string.Format("连接池 {0} 智能调整失败: {1}", _name, ex.Message));
            }
            finally
            {
                Monitor.Exit(_adjustmentLock);
            }
        }

        /// <summary>
        /// 环境状态检测结果
        /// </summary>
        private class EnvironmentStatus
        {
            public bool CanExpand { get; set; } = true;
            public double ExpandFactor { get; set; } = 1.0;
            public string Reason { get; set; }
        }

        /// <summary>
        /// 检测环境状态（服务器资源、Redis 可用性等）
        /// </summary>
        private EnvironmentStatus CheckEnvironmentStatus()
        {
            var status = new EnvironmentStatus();
            var reasons = new List<string>();

            try
            {
                // 1. 检查服务器资源
#if !NETFRAMEWORK
                var memoryInfo = GC.GetGCMemoryInfo();
                var memoryUsagePercent = 100.0 - (double)memoryInfo.TotalAvailableMemoryBytes / 
                    (memoryInfo.TotalAvailableMemoryBytes + memoryInfo.MemoryLoadBytes) * 100;
#else
                // .NET Framework 4.5.2 不支持 GC.GetGCMemoryInfo，跳过内存检查
                var memoryUsagePercent = 0.0;
#endif
                
                // 内存使用超过阈值时限制扩容
                if (memoryUsagePercent > HighMemoryUsageThresholdPercent)
                {
                    status.ExpandFactor *= LowMemoryExpandFactor;
                    reasons.Add(string.Format("内存使用率高({0:F1}%)", memoryUsagePercent));
                }

                // 2. 检查 Redis 可用性（如果配置了）
                if (_config.EnableRedisCheck && !string.IsNullOrEmpty(_config.RedisConnectionString))
                {
                    // 定期检测 Redis
                    if (DateTime.UtcNow - _lastRedisCheckTime > TimeSpan.FromSeconds(RedisCheckIntervalSeconds))
                    {
                        _isRedisAvailable = CheckRedisAvailability(_config.RedisConnectionString);
                        _lastRedisCheckTime = DateTime.UtcNow;
                    }

                    if (!_isRedisAvailable)
                    {
                        status.CanExpand = false;
                        reasons.Add("Redis 不可用");
                    }
                }

                // 3. 检查近期扩容频率（避免频繁扩缩容）
                if (DateTime.UtcNow - _lastExpandTime < TimeSpan.FromSeconds(ExpandFrequencySeconds))
                {
                    status.ExpandFactor *= RecentExpandExpandFactor;
                    reasons.Add("近期已扩容");
                }

                // 4. 检查近期错误率
                if (_recentErrors > 0)
                {
                    status.ExpandFactor *= ErrorExpandFactor;
                    reasons.Add(string.Format("近期有错误({0})", _recentErrors));
                }

                if (reasons.Any())
                {
                    status.Reason = string.Join(", ", reasons);
                }
            }
            catch (Exception ex)
            {
                Log(string.Format("环境检测异常: {0}", ex.Message));
            }

            return status;
        }

        /// <summary>
        /// 检查 Redis 可用性
        /// </summary>
        private bool CheckRedisAvailability(string connectionString)
        {
            try
            {
                var parts = connectionString.Split(':');
                if (parts.Length == 2 && int.TryParse(parts[1], out var port))
                {
                    using var client = new System.Net.Sockets.TcpClient();
                    var result = client.BeginConnect(parts[0], port, null, null);
                    var success = result.AsyncWaitHandle.WaitOne(TimeSpan.FromSeconds(RedisConnectTimeoutSeconds));
                    if (success)
                    {
                        client.EndConnect(result);
                    }
                    return success;
                }
            }
            catch
            {
                // Redis 连接失败
            }
            return false;
        }

        /// <summary>
        /// 记录数据库异常（供外部调用）
        /// </summary>
        /// <param name="ex">异常信息</param>
        public void RecordDatabaseError(Exception ex)
        {
            Interlocked.Increment(ref _recentErrors);
            _lastErrorTime = DateTime.UtcNow;
            Log(string.Format("记录数据库异常: {0}（近期错误 {1} 次）", ex.Message, _recentErrors));
        }

        /// <summary>
        /// 获取近期错误计数
        /// </summary>
        public long RecentErrors => Interlocked.Read(ref _recentErrors);

        /// <summary>
        /// 终结器：确保即使调用方忘记 Dispose 也能释放定时器和信号量
        /// </summary>
        ~SmartConnectionPool()
        {
            Dispose(false);
        }

        /// <summary>
        /// 释放资源
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Releases resources held by the connection pool.
        /// </summary>
        /// <param name="disposing">True when called from Dispose; false from the finalizer.</param>
        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    _healthCheckTimer?.Dispose();
                    _smartAdjustmentTimer?.Dispose();

                    // 销毁所有空闲连接
                    while (_availableConnections.TryTake(out var conn))
                    {
                        DestroyConnection(conn);
                    }

                    // 销毁所有使用中连接
                    foreach (var kvp in _inUseConnections)
                    {
                        DestroyConnection(kvp.Value);
                    }

                    _semaphore?.Dispose();
                }
                // 无需要清理的非托管资源
                _disposed = true;
            }
        }

        #region Circuit Breaker Methods

        /// <summary>
        /// 检查是否可以执行请求（熔断器检查）
        /// </summary>
        private bool CanExecuteRequest()
        {
            if (!_config.CircuitBreaker.Enabled)
                return true;

            lock (_circuitLock)
            {
                switch (_circuitState)
                {
                    case CircuitState.Closed:
                        return true;

                    case CircuitState.Open:
                        // 检查是否已过熔断时长
                        if (DateTime.UtcNow >= _circuitOpenedAt.AddSeconds(_config.CircuitBreaker.CircuitOpenDurationSec))
                        {
                            TryTransitionToHalfOpen();
                            if (_halfOpenRequests < _config.CircuitBreaker.HalfOpenMaxRequests)
                            {
                                _halfOpenRequests++;
                                return true;
                            }
                        }
                        return false;

                    case CircuitState.HalfOpen:
                        // 半开状态下只允许少量测试请求
                        if (_halfOpenRequests < _config.CircuitBreaker.HalfOpenMaxRequests)
                        {
                            _halfOpenRequests++;
                            return true;
                        }
                        return false;

                    default:
                        return true;
                }
            }
        }

        /// <summary>
        /// 记录成功
        /// </summary>
        private void RecordSuccess()
        {
            if (!_config.CircuitBreaker.Enabled)
                return;

            lock (_circuitLock)
            {
                if (_circuitState == CircuitState.HalfOpen)
                {
                    // 半开状态下成功，恢复到关闭状态
                    Log(string.Format("熔断器 {0} 恢复正常状态（半开->关闭）", _name));
                    _circuitState = CircuitState.Closed;
                    _consecutiveFailures = 0;
                    _halfOpenRequests = 0;
                }
                else if (_circuitState == CircuitState.Closed)
                {
                    // 关闭状态下重置失败计数
                    _consecutiveFailures = 0;
                }
            }
        }

        /// <summary>
        /// 记录失败
        /// </summary>
        private void RecordFailure()
        {
            if (!_config.CircuitBreaker.Enabled)
                return;

            lock (_circuitLock)
            {
                _consecutiveFailures++;

                if (_circuitState == CircuitState.HalfOpen)
                {
                    // 半开状态下失败，重新打开熔断器
                    Log(string.Format("熔断器 {0} 触发熔断（半开->打开，连续失败 {1} 次）", _name, _consecutiveFailures));
                    _circuitState = CircuitState.Open;
                    _circuitOpenedAt = DateTime.UtcNow;
                    _halfOpenRequests = 0;
                }
                else if (_circuitState == CircuitState.Closed && 
                         _consecutiveFailures >= _config.CircuitBreaker.FailureThreshold)
                {
                    // 关闭状态下达到失败阈值，打开熔断器
                    Log(string.Format("熔断器 {0} 触发熔断（关闭->打开，连续失败 {1} 次）", _name, _consecutiveFailures));
                    _circuitState = CircuitState.Open;
                    _circuitOpenedAt = DateTime.UtcNow;
                }
            }
        }

        /// <summary>
        /// 尝试转换到半开状态
        /// </summary>
        private void TryTransitionToHalfOpen()
        {
            Log(string.Format("熔断器 {0} 进入半开状态（打开->半开）", _name));
            _circuitState = CircuitState.HalfOpen;
            _halfOpenRequests = 0;
            _consecutiveFailures = 0;
        }

        #endregion
    }

    /// <summary>
    /// 熔断器打开异常
    /// </summary>
    public class CircuitBreakerOpenException : Exception
    {
        /// <summary>
        /// Initializes a circuit-breaker-open exception.
        /// </summary>
        /// <param name="message">Exception message.</param>
        public CircuitBreakerOpenException(string message) : base(message) { }
    }

    /// <summary>
    /// 连接池耗尽异常
    /// 当连接池满且等待超时时抛出，用于触发降级到消息队列
    /// </summary>
    public class ConnectionPoolExhaustedException : Exception
    {
        /// <summary>
        /// 连接池名称
        /// </summary>
        public string PoolName { get; }

        /// <summary>
        /// 等待时间（秒）
        /// </summary>
        public int WaitTimeoutSeconds { get; }

        /// <summary>
        /// Initializes a connection-pool exhausted exception.
        /// </summary>
        /// <param name="poolName">Pool name.</param>
        /// <param name="waitTimeoutSeconds">Wait timeout in seconds.</param>
        public ConnectionPoolExhaustedException(string poolName, int waitTimeoutSeconds)
            : base(string.Format("连接池 {0} 已耗尽，等待 {1} 秒后超时", poolName, waitTimeoutSeconds))
        {
            PoolName = poolName;
            WaitTimeoutSeconds = waitTimeoutSeconds;
        }

        /// <summary>
        /// Initializes a connection-pool exhausted exception with a custom message.
        /// </summary>
        /// <param name="poolName">Pool name.</param>
        /// <param name="message">Exception message.</param>
        public ConnectionPoolExhaustedException(string poolName, string message)
            : base(message)
        {
            PoolName = poolName;
        }
    }
}
