using System;
using FastData.Context;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using FastData.Config;
using FastData.Model;

namespace FastData.DevTools
{
    /// <summary>
    /// 分布式事务管理器
    /// </summary>
    public static class DistributedTransactionManager
    {
        private static readonly Dictionary<string, DistributedTransaction> _activeTransactions = new Dictionary<string, DistributedTransaction>();
        private static readonly object _lock = new object();

        /// <summary>
        /// 开始分布式事务
        /// </summary>
        public static DistributedTransaction BeginTransaction(params string[] dbKeys)
        {
            if (dbKeys == null || dbKeys.Length == 0)
            {
                throw new ArgumentException("至少需要一个数据库连接");
            }

            var transactionId = Guid.NewGuid().ToString();
            var contexts = new List<TransactionContext>();

            foreach (var dbKey in dbKeys)
            {
                var config = DataConfig.GetConfig(dbKey);
                if (config == null)
                {
                    RollbackTransaction(transactionId);
                    throw new ArgumentException(string.Format("找不到数据库配置: {0}", dbKey));
                }

                var context = new TransactionContext
                {
                    DbKey = dbKey,
                    Config = config
                };

                var db = new DataContext(dbKey);
                context.Connection = db.conn;
                context.Transaction = db.conn.BeginTransaction();
                contexts.Add(context);
            }

            var distributedTx = new DistributedTransaction
            {
                TransactionId = transactionId,
                Contexts = contexts,
                Status = TransactionStatus.Active
            };

            lock (_lock)
            {
                _activeTransactions[transactionId] = distributedTx;
            }

            return distributedTx;
        }

        /// <summary>
        /// 提交事务
        /// </summary>
        public static Result CommitTransaction(string transactionId)
        {
            DistributedTransaction distributedTx;

            lock (_lock)
            {
                if (!_activeTransactions.TryGetValue(transactionId, out distributedTx))
                {
                    return Result.Fail("事务不存在");
                }
            }

            try
            {
                // 两阶段提交协议
                // 阶段 1: 准备阶段
                foreach (var context in distributedTx.Contexts)
                {
                    try
                    {
                        if (context.Connection != null && context.Transaction != null)
                        {
                            context.Transaction.Commit();
                        }
                    }
                    catch (Exception ex)
                    {
                        // 准备阶段失败，执行回滚
                        RollbackTransaction(transactionId);
                        return Result.Fail(string.Format("事务提交失败: {0}", ex.Message));
                    }
                }

                distributedTx.Status = TransactionStatus.Committed;

                lock (_lock)
                {
                    _activeTransactions.Remove(transactionId);
                }

                Cleanup(distributedTx);

                return Result.Ok();
            }
            catch (Exception ex)
            {
                RollbackTransaction(transactionId);
                return Result.Fail(string.Format("事务提交异常: {0}", ex.Message));
            }
        }

        /// <summary>
        /// 回滚事务
        /// </summary>
        public static Result RollbackTransaction(string transactionId)
        {
            DistributedTransaction distributedTx;

            lock (_lock)
            {
                if (!_activeTransactions.TryGetValue(transactionId, out distributedTx))
                {
                    return Result.Fail("事务不存在");
                }
            }

            try
            {
                foreach (var context in distributedTx.Contexts)
                {
                    try
                    {
                        if (context.Connection != null && context.Transaction != null)
                        {
                            context.Transaction.Rollback();
                        }
                    }
                    catch
                    {
                        // 忽略回滚错误
                    }
                }

                distributedTx.Status = TransactionStatus.RolledBack;

                lock (_lock)
                {
                    _activeTransactions.Remove(transactionId);
                }

                Cleanup(distributedTx);

                return Result.Ok();
            }
            catch (Exception ex)
            {
                return Result.Fail(string.Format("事务回滚异常: {0}", ex.Message));
            }
        }

        /// <summary>
        /// 获取事务上下文
        /// </summary>
        public static DistributedTransaction GetTransaction(string transactionId)
        {
            lock (_lock)
            {
                return _activeTransactions.TryGetValue(transactionId, out var tx) ? tx : null;
            }
        }

        /// <summary>
        /// 检查事务是否活动
        /// </summary>
        public static bool IsActive(string transactionId)
        {
            lock (_lock)
            {
                return _activeTransactions.ContainsKey(transactionId);
            }
        }

        /// <summary>
        /// 清理资源
        /// </summary>
        private static void Cleanup(DistributedTransaction distributedTx)
        {
            foreach (var context in distributedTx.Contexts)
            {
                try
                {
                    context.Transaction?.Dispose();
                    context.Connection?.Close();
                    context.Connection?.Dispose();
                }
                catch
                {
                    // 忽略清理错误
                }
            }
        }

        /// <summary>
        /// 获取所有活动事务
        /// </summary>
        public static List<DistributedTransaction> GetActiveTransactions()
        {
            lock (_lock)
            {
                return _activeTransactions.Values.ToList();
            }
        }

        /// <summary>
        /// 清理所有活动事务
        /// </summary>
        public static void CleanupAll()
        {
            List<string> transactionIds;

            lock (_lock)
            {
                transactionIds = _activeTransactions.Keys.ToList();
            }

            foreach (var transactionId in transactionIds)
            {
                RollbackTransaction(transactionId);
            }
        }
    }

    /// <summary>
    /// 分布式事务
    /// </summary>
    public class DistributedTransaction
    {
        /// <summary>
        /// 事务 ID
        /// </summary>
        public string TransactionId { get; set; }
        /// <summary>
        /// 参与事务的上下文列表
        /// </summary>
        public List<TransactionContext> Contexts { get; set; }
        /// <summary>
        /// 事务状态
        /// </summary>
        public TransactionStatus Status { get; set; }
        /// <summary>
        /// 事务创建时间
        /// </summary>
        public DateTime CreatedAt { get; set; } = DateTime.Now;

        /// <summary>
        /// 提交事务
        /// </summary>
        public Result Commit()
        {
            return DistributedTransactionManager.CommitTransaction(TransactionId);
        }

        /// <summary>
        /// 回滚事务
        /// </summary>
        public Result Rollback()
        {
            return DistributedTransactionManager.RollbackTransaction(TransactionId);
        }

        /// <summary>
        /// 获取指定数据库的命令
        /// </summary>
        public IDbCommand GetCommand(string dbKey)
        {
            var context = Contexts.FirstOrDefault(c => c.DbKey == dbKey);
            if (context == null)
            {
                throw new ArgumentException(string.Format("数据库连接不在事务中: {0}", dbKey));
            }

            var command = context.Connection.CreateCommand();
            command.Transaction = context.Transaction;
            return command;
        }

        /// <summary>
        /// 在事务中执行操作
        /// </summary>
        public Result ExecuteInTransaction(string dbKey, Action<IDbCommand> action)
        {
            var command = GetCommand(dbKey);
            try
            {
                action(command);
                return Result.Ok();
            }
            catch (Exception ex)
            {
                return Result.Fail(ex.Message);
            }
        }

        /// <summary>
        /// 在事务中执行查询
        /// </summary>
        public List<T> QueryInTransaction<T>(string dbKey, Func<IDbCommand, List<T>> queryFunc)
        {
            var command = GetCommand(dbKey);
            return queryFunc(command);
        }
    }

    /// <summary>
    /// 事务上下文
    /// </summary>
    public class TransactionContext
    {
        /// <summary>
        /// 数据库标识
        /// </summary>
        public string DbKey { get; set; }
        internal object Config { get; set; }
        /// <summary>
        /// 数据库连接
        /// </summary>
        public IDbConnection Connection { get; set; }
        /// <summary>
        /// 数据库事务
        /// </summary>
        public IDbTransaction Transaction { get; set; }
    }

    /// <summary>
    /// 事务状态
    /// </summary>
    public enum TransactionStatus
    {
        /// <summary>
        /// 活动
        /// </summary>
        Active,
        /// <summary>
        /// 已提交
        /// </summary>
        Committed,
        /// <summary>
        /// 已回滚
        /// </summary>
        RolledBack,
        /// <summary>
        /// 失败
        /// </summary>
        Failed
    }

    /// <summary>
    /// 事务作用域
    /// </summary>
    public class TransactionScope : IDisposable
    {
        private readonly DistributedTransaction _transaction;
        private readonly bool _autoCommit;
        private bool _committed;
        private bool _disposed;

        /// <summary>
        /// 初始化事务作用域（自动提交）
        /// </summary>
        /// <param name="dbKeys">参与的数据库标识</param>
        public TransactionScope(params string[] dbKeys)
            : this(dbKeys, true)
        {
        }

        /// <summary>
        /// 初始化事务作用域
        /// </summary>
        /// <param name="dbKeys">参与的数据库标识</param>
        /// <param name="autoCommit">是否自动提交</param>
        public TransactionScope(string[] dbKeys, bool autoCommit)
        {
            _autoCommit = autoCommit;
            _transaction = DistributedTransactionManager.BeginTransaction(dbKeys);
        }

        /// <summary>
        /// 关联的分布式事务
        /// </summary>
        public DistributedTransaction Transaction => _transaction;

        /// <summary>
        /// 提交事务
        /// </summary>
        /// <returns>提交结果</returns>
        public Result Commit()
        {
            if (_committed)
            {
                return Result.Fail("事务已提交");
            }

            var result = _transaction.Commit();
            _committed = true;
            return result;
        }

        /// <summary>
        /// 标记事务完成（自动提交模式下会提交）
        /// </summary>
        public void Complete()
        {
            if (!_committed && _autoCommit)
            {
                Commit();
            }
        }

        /// <summary>
        /// 释放事务作用域，未提交时回滚
        /// </summary>
        public void Dispose()
        {
            if (_disposed) return;

            _disposed = true;

            if (!_committed)
            {
                _transaction.Rollback();
            }
        }
    }

    /// <summary>
    /// 分布式事务辅助类
    /// </summary>
    public static class DistributedTransactionHelper
    {
        /// <summary>
        /// 在单个事务中执行多个数据库操作
        /// </summary>
        public static Result ExecuteInSingleTransaction(string[] dbKeys, Action<DistributedTransaction> action)
        {
            var transaction = DistributedTransactionManager.BeginTransaction(dbKeys);

            try
            {
                action(transaction);

                if (transaction.Status == TransactionStatus.Active)
                {
                    return transaction.Commit();
                }

                return Result.Fail("事务状态异常");
            }
            catch (Exception ex)
            {
                transaction.Rollback();
                return Result.Fail(ex.Message);
            }
        }

        /// <summary>
        /// 使用事务作用域执行操作
        /// </summary>
        public static Result ExecuteInScope(string[] dbKeys, Action<DistributedTransaction> action)
        {
            using (var scope = new TransactionScope(dbKeys))
            {
                try
                {
                    action(scope.Transaction);
                    scope.Complete();
                    return Result.Ok();
                }
                catch (Exception ex)
                {
                    return Result.Fail(ex.Message);
                }
            }
        }

        /// <summary>
        /// 异步执行分布式事务
        /// </summary>
        public static async Task<Result> ExecuteAsync(string[] dbKeys, Func<DistributedTransaction, Task<Result>> action)
        {
            return await Task.Run(() =>
            {
                var transaction = DistributedTransactionManager.BeginTransaction(dbKeys);

                try
                {
                    var result = action(transaction).Result;

                    if (result.Success && transaction.Status == TransactionStatus.Active)
                    {
                        return transaction.Commit();
                    }

                    transaction.Rollback();
                    return result;
                }
                catch (Exception ex)
                {
                    transaction.Rollback();
                    return Result.Fail(ex.Message);
                }
            });
        }
    }
}