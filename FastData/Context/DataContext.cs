using System;
using System.Collections.Generic;
using System.Linq;
using System.Data.Common;
using FastUntility.Page;
using FastUntility.Base;
using FastData.Base;
using FastData.Model;
using FastData.DbTypes;
using FastData.Config;
using System.Data;
using FastData.Property;
using FastData.Aop;
using FastData.ConnectionPool;
#if !NETFRAMEWORK
using Microsoft.Data.Sqlite;
#endif

namespace FastData.Context
{
    /// <summary>
    /// 数据库上下文（轻量级 DbContext）
    ///
    /// 职责：
    /// 1. 管理数据库连接（支持连接池）
    /// 2. 管理 DbCommand 对象
    /// 3. 管理事务（BeginTransaction / Commit / Rollback）
    /// 4. AOP 拦截（Before / After / Error）
    /// 5. 资源释放（实现 IDisposable，遵循 RAII 模式）
    ///
    /// 注意事项：
    /// - 使用完毕后必须调用 Dispose() 释放资源
    /// - 推荐使用 using 语句确保资源被正确释放
    /// - 支持连接池：_usePool=true 时使用 PooledConnection
    /// - 兼容旧 API：暴露小写别名（config/cmd/conn）以兼容历史代码
    /// </summary>
    public partial class DataContext : IDisposable
    {
        private bool _disposed;
        private ConfigModel _config;
        private DbConnection _connection;
        private DbCommand _command;
        private DbTransaction _transaction;
        private PooledConnection _pooledConnection;
        private bool _usePool;

        /// <summary>
        /// Gets the resolved database configuration for this context.
        /// </summary>
        public ConfigModel Config => _config;

        /// <summary>
        /// Gets the resolved database configuration. Kept for compatibility with earlier FastData APIs.
        /// </summary>
        public ConfigModel config => _config;

        /// <summary>
        /// Gets the command object owned by this context. Kept for compatibility with earlier FastData APIs.
        /// </summary>
        public DbCommand cmd => _command;

        /// <summary>
        /// Gets the connection object owned by this context. Kept for compatibility with earlier FastData APIs.
        /// </summary>
        public DbConnection conn => _connection;

        /// <summary>
        /// 创建并应用连接级命令设置（命令超时等）
        /// </summary>
        private DbCommand CreateCommand()
        {
            var command = _connection.CreateCommand();
            var timeout = _config?.CommandTimeout ?? 0;
            if (timeout > 0)
                command.CommandTimeout = timeout;

            return command;
        }

        /// <summary>
        /// Disposes a command and clears its parameters.
        /// </summary>
        /// <param name="command">Command to dispose.</param>
        public void DisposeCommand(DbCommand command)
        {
            if (command == null) return;

            // 多数 ADO.NET 提供程序的参数对象实现了 IDisposable，需统一释放
            if (command.Parameters != null)
            {
                foreach (var param in command.Parameters.Cast<DbParameter>())
                {
                    (param as IDisposable)?.Dispose();
                }
            }

            command.Parameters.Clear();
            command.Dispose();
        }

        /// <summary>
        /// Aop Before
        /// </summary>
        /// <param name="sql"></param>
        /// <param name="name"></param>
        /// <param name="param"></param>
        /// <param name="config"></param>
        private void AopBefore(List<string> tableName, string sql, List<DbParameter> param, ConfigModel config, bool isRead, AopType type)
        {
            if (FastMap.fastAop != null)
            {
                var context = new BeforeContext
                {
                    tableName = tableName,
                    sql = sql,
                    param = param,
                    dbType = config.DbType,
                    isRead = isRead,
                    isWrite = !isRead
                };

                FastMap.fastAop.Before(context);
            }
        }

        private void AopAfter(List<string> tableName, string sql, List<DbParameter> param, ConfigModel config, bool isRead, AopType type, object result)
        {
            if (FastMap.fastAop != null)
            {
                var context = new AfterContext
                {
                    tableName = tableName,
                    sql = sql,
                    param = param,
                    dbType = config.DbType,
                    isRead = isRead,
                    isWrite = !isRead,
                    result = result
                };

                FastMap.fastAop.After(context);
            }
        }

        private void AopException(Exception ex, string name, ConfigModel config, AopType type)
        {
            if (FastMap.fastAop != null)
            {
                var context = new ExceptionContext
                {
                    dbType = config.DbType,
                    ex = ex,
                    name = name,
                    type = type
                };
                FastMap.fastAop.Exception(context);
            }
        }

        /// <summary>
        /// Finalizes the context and releases unmanaged resources if Dispose was not called.
        /// </summary>
        ~DataContext()
        {
            Dispose(false);
        }

        /// <summary>
        /// Releases the command, connection, transaction and pooled connection resources.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Releases resources held by this context.
        /// </summary>
        /// <param name="disposing">True when called from Dispose; false when called from the finalizer.</param>
        protected virtual void Dispose(bool disposing)
        {
            if (_disposed) return;

            if (disposing)
            {
                // 归还连接前必须清理未提交事务，否则事务状态会残留在池化连接上
                if (_transaction != null)
                {
                    try { _transaction.Rollback(); } catch { }
                    try { _transaction.Dispose(); } catch { }
                    _transaction = null;
                }

                if (_command != null)
                {
                    try { _command.Transaction = null; } catch { }
                }

                DisposeCommand(_command);

                if (_usePool && _pooledConnection != null)
                {
                    // 仅调用 Dispose 归还连接到池，不要先 Close()
                    // Close() 会导致 ReturnConnection 验证失败（连接已关闭），连接被销毁而非归还池
                    _pooledConnection.Dispose();
                    _pooledConnection = null;
                }
                else
                {
                    if (_connection != null)
                    {
                        try { _connection.Close(); } catch { }
                        _connection.Dispose();
                    }
                }
            }

            _command = null;
            _connection = null;
            _transaction = null;
            _pooledConnection = null;
            _config = null;
            _disposed = true;
        }

        /// <summary>
        /// Opens the underlying connection if it is not already open.
        /// </summary>
        public void EnsureConnectionOpen()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(DataContext));

            if (_connection == null)
                throw new InvalidOperationException("Connection is null. DataContext was not initialized properly.");

            if (_connection.State != ConnectionState.Open)
                _connection.Open();
        }

        #region 初始化
        /// <summary>
        /// 初始化（延迟打开连接）
        /// </summary>
        /// <param name="key">配置键</param>
        /// <param name="projectName">项目名称</param>
        /// <param name="poolConfig">连接池配置</param>
        public DataContext(string key = null, string projectName = null, ConnectionPoolConfig poolConfig = null)
        {
            try
            {
                _config = DataConfig.GetConfig(key, projectName);
                if (_config == null)
                    throw new InvalidOperationException(string.Format("Config is null for key={0}, project={1}", key, projectName));
                if (string.IsNullOrEmpty(_config.ProviderName))
                    throw new InvalidOperationException(string.Format("ProviderName is null for key={0}, config.Key={1}", key, _config.Key));
                
                var connStr = _config.ConnStr;
                if (_config.IsEncrypt && !string.IsNullOrEmpty(connStr))
                {
                    try
                    {
                        connStr = BaseSymmetric.Decrypto(connStr);
                    }
                    catch
                    {
                        DbLog.LogSql(true, "连接字符串解密失败，使用原始连接字符串", _config.DbType, 0);
                    }
                }

                if (poolConfig == null)
                    poolConfig = DataConfig.GetConnectionPoolConfigPublic();

                // SQLite 连接非常轻量（本质是文件句柄），无需使用连接池
                // 直接创建连接即可，避免连接池带来的序列化开销和语义问题
                if (_config.ProviderName == Provider.MicrosoftDataSqlite)
                {
#if !NETFRAMEWORK
                    _connection = new Microsoft.Data.Sqlite.SqliteConnection(connStr);
                    _command = CreateCommand();
#else
                    throw new NotSupportedException("SQLite 在 .NET Framework 4.5.2 中不受支持");
#endif
                }
                else
                {
                    var factory = Infrastructure.DbProviderAutoRegistrar.GetFactory(_config.ProviderName);
                    if (factory == null)
                        throw new InvalidOperationException(string.Format("DbProviderFactory not found for provider: {0}", _config.ProviderName));

                    if (poolConfig != null)
                    {
                        _usePool = true;
                        var poolKey = string.IsNullOrEmpty(_config.Key) ? "default" : _config.Key;
                        var pool = ConnectionPoolFactory.Instance.GetOrCreatePool(
                            poolKey,
                            () =>
                            {
                                var c = factory.CreateConnection();
                                c.ConnectionString = connStr;
                                return c;
                            },
                            poolConfig);

                        _pooledConnection = pool.GetConnection();
                        _connection = _pooledConnection.Connection;
                        _command = CreateCommand();
                    }
                    else
                    {
                        _connection = factory.CreateConnection();
                        _connection.ConnectionString = connStr;
                        _command = CreateCommand();
                    }
                }
            }
            catch (Exception ex)
            {
                AopException(ex, "DataContext :" + key, _config ?? new ConfigModel(), AopType.DataContext);

                if (string.Equals(_config?.SqlErrorType, SqlErrorType.Db, StringComparison.OrdinalIgnoreCase))
                    DbLogTable.LogException(_config, ex, "DataContext", "");
                else
                    DbLog.LogException(true, _config?.DbType ?? DataDbType.SqlServer, ex, "DataContext", "");
                
                throw;
            }
        }

        /// <summary>
        /// Begins a database transaction and binds it to the context command.
        /// </summary>
        public void BeginTransaction()
        {
            BeginTransaction(IsolationLevel.Unspecified);
        }

        /// <summary>
        /// 以指定隔离级别开始事务；当前已有未完成事务时直接报错，避免静默覆盖。
        /// </summary>
        public void BeginTransaction(IsolationLevel isolationLevel)
        {
            EnsureConnectionOpen();

            if (_transaction != null)
                throw new InvalidOperationException("当前 DataContext 已存在未完成事务，请先 Commit 或 Rollback");

            if (_command == null)
                _command = CreateCommand();

            if (_command.Connection == null)
                _command.Connection = _connection;

            _transaction = isolationLevel == IsolationLevel.Unspecified
                ? _connection.BeginTransaction()
                : _connection.BeginTransaction(isolationLevel);

            if (_transaction != null)
                _command.Transaction = _transaction;
        }

        /// <summary>
        /// Commits the current transaction if one exists.
        /// </summary>
        public void CommitTransaction()
        {
            if (_transaction == null)
                return;

            try
            {
                _transaction.Commit();
            }
            finally
            {
                ClearTransaction();
            }
        }

        /// <summary>
        /// Rolls back the current transaction if one exists.
        /// </summary>
        public void RollbackTransaction()
        {
            if (_transaction == null)
                return;

            try
            {
                _transaction.Rollback();
            }
            finally
            {
                ClearTransaction();
            }
        }

        private void ClearTransaction()
        {
            try { _transaction?.Dispose(); } catch { }
            _transaction = null;

            if (_command != null)
            {
                try { _command.Transaction = null; } catch { }
            }
        }

        /// <summary>
        /// Begins a transaction. Compatibility alias for BeginTransaction.
        /// </summary>
        public void BeginTrans() => BeginTransaction();

        /// <summary>
        /// Commits the current transaction. Compatibility alias for CommitTransaction.
        /// </summary>
        public void SubmitTrans() => CommitTransaction();

        /// <summary>
        /// Rolls back the current transaction. Compatibility alias for RollbackTransaction.
        /// </summary>
        public void RollbackTrans() => RollbackTransaction();
        #endregion
    }
}
