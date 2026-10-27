using System;
using FastData.Context;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

// 本文件内部会自引用这些已标记 Obsolete 的成员；
// 这里只抑制文件内的诊断，外部调用方仍然会收到 CS0618。
#pragma warning disable CS0618

namespace FastData.DevTools
{
    /// <summary>
    /// 分布式锁管理器。
    /// <para>
    /// <b>注意：本类不是分布式的。</b>它把锁状态保存在进程内的
    /// <see cref="ConcurrentDictionary{TKey,TValue}"/> 中，只对同一进程内的调用者可见。
    /// 多实例 / 多进程部署时，每个实例都会「成功」拿到同一把锁，
    /// 不提供任何跨进程互斥保证。
    /// </para>
    /// <para>
    /// 需要真正的分布式互斥时，请改用数据库行锁（SELECT ... FOR UPDATE）、
    /// Redis SETNX + 过期时间，或其它外部协调服务。
    /// </para>
    /// </summary>
    [Obsolete("DistributedLockManager 仅在同进程内有效，多实例部署下不提供互斥保证。" +
              "请改用数据库行锁或 Redis 等外部协调服务。")]
    public static class DistributedLockManager
    {
        private static readonly ConcurrentDictionary<string, LockInfo> _locks = new ConcurrentDictionary<string, LockInfo>();
        private static readonly object _lock = new object();

        /// <summary>
        /// 尝试获取锁
        /// </summary>
        public static bool TryAcquire(string lockKey, TimeSpan timeout, out LockHandle lockHandle, string lockValue = null)
        {
            lockHandle = null;
            lockValue = lockValue ?? Guid.NewGuid().ToString();

            var deadline = DateTime.Now.Add(timeout);

            while (DateTime.Now < deadline)
            {
                if (_locks.TryAdd(lockKey, new LockInfo
                {
                    LockKey = lockKey,
                    LockValue = lockValue,
                    AcquiredAt = DateTime.Now,
                    ExpiresAt = DateTime.Now.AddMinutes(30) // 默认30分钟过期
                }))
                {
                    lockHandle = new LockHandle(lockKey, lockValue);
                    return true;
                }

                // 检查锁是否过期
                if (_locks.TryGetValue(lockKey, out var existingLock))
                {
                    if (existingLock.ExpiresAt < DateTime.Now)
                    {
                        _locks.TryRemove(lockKey, out _);
                        continue;
                    }
                }

                Thread.Sleep(100);
            }

            return false;
        }

        /// <summary>
        /// 异步尝试获取锁
        /// </summary>
        public static async Task<(bool acquired, LockHandle lockHandle)> TryAcquireAsync(string lockKey, TimeSpan timeout, string lockValue = null)
        {
            return await Task.Run(() =>
            {
                var acquired = TryAcquire(lockKey, timeout, out var lockHandle, lockValue);
                return (acquired, lockHandle);
            });
        }

        /// <summary>
        /// 释放锁
        /// </summary>
        public static bool Release(LockHandle lockHandle)
        {
            if (_locks.TryGetValue(lockHandle.LockKey, out var lockInfo) && lockInfo.LockValue == lockHandle.LockValue)
            {
                return _locks.TryRemove(lockHandle.LockKey, out _);
            }
            return false;
        }

        /// <summary>
        /// 延长锁的有效期
        /// </summary>
        public static bool Renew(LockHandle lockHandle, TimeSpan additionalTime)
        {
            if (_locks.TryGetValue(lockHandle.LockKey, out var lockInfo) && lockInfo.LockValue == lockHandle.LockValue)
            {
                lockInfo.ExpiresAt = DateTime.Now.Add(additionalTime);
                return true;
            }
            return false;
        }

        /// <summary>
        /// 检查锁是否存在
        /// </summary>
        public static bool IsLocked(string lockKey)
        {
            return _locks.ContainsKey(lockKey);
        }

        /// <summary>
        /// 获取锁信息
        /// </summary>
        public static LockInfo GetLockInfo(string lockKey)
        {
            return _locks.TryGetValue(lockKey, out var lockInfo) ? lockInfo : null;
        }

        /// <summary>
        /// 清理过期锁
        /// </summary>
        public static int CleanupExpiredLocks()
        {
            var expiredKeys = _locks.Where(kvp => kvp.Value.ExpiresAt < DateTime.Now)
                                  .Select(kvp => kvp.Key)
                                  .ToList();

            foreach (var key in expiredKeys)
            {
                _locks.TryRemove(key, out _);
            }

            return expiredKeys.Count;
        }

        /// <summary>
        /// 强制释放锁
        /// </summary>
        public static bool ForceRelease(string lockKey)
        {
            return _locks.TryRemove(lockKey, out _);
        }

        /// <summary>
        /// 获取所有锁信息
        /// </summary>
        public static List<LockInfo> GetAllLocks()
        {
            return _locks.Values.ToList();
        }

        /// <summary>
        /// 使用锁执行操作
        /// </summary>
        public static T ExecuteWithLock<T>(string lockKey, Func<T> action, TimeSpan timeout, string lockValue = null)
        {
            if (TryAcquire(lockKey, timeout, out var lockHandle, lockValue))
            {
                try
                {
                    return action();
                }
                finally
                {
                    Release(lockHandle);
                }
            }

            throw new TimeoutException(string.Format("无法在 {0} 内获取锁: {1}", timeout, lockKey));
        }

        /// <summary>
        /// 异步使用锁执行操作
        /// </summary>
        public static async Task<T> ExecuteWithLockAsync<T>(string lockKey, Func<Task<T>> action, TimeSpan timeout, string lockValue = null)
        {
            var (acquired, lockHandle) = await TryAcquireAsync(lockKey, timeout, lockValue);

            if (acquired)
            {
                try
                {
                    return await action();
                }
                finally
                {
                    Release(lockHandle);
                }
            }

            throw new TimeoutException(string.Format("无法在 {0} 内获取锁: {1}", timeout, lockKey));
        }

        /// <summary>
        /// 获取锁统计信息
        /// </summary>
        public static LockStatistics GetStatistics()
        {
            var locks = _locks.Values.ToList();
            return new LockStatistics
            {
                TotalLocks = locks.Count,
                ActiveLocks = locks.Count(l => l.ExpiresAt > DateTime.Now),
                ExpiredLocks = locks.Count(l => l.ExpiresAt <= DateTime.Now),
                AverageLockDuration = locks.Any() ? locks.Average(l => (l.ExpiresAt - l.AcquiredAt).TotalMinutes) : 0
            };
        }
    }

    /// <summary>
    /// 锁句柄
    /// </summary>
    public class LockHandle : IDisposable
    {
        /// <summary>
        /// 锁键
        /// </summary>
        public string LockKey { get; set; }
        /// <summary>
        /// 锁持有值（用于校验持有者）
        /// </summary>
        public string LockValue { get; set; }
        private bool _disposed;

        /// <summary>
        /// 创建锁句柄
        /// </summary>
        /// <param name="lockKey">锁键</param>
        /// <param name="lockValue">锁持有值</param>
        public LockHandle(string lockKey, string lockValue)
        {
            LockKey = lockKey;
            LockValue = lockValue;
        }

        /// <summary>
        /// 释放锁
        /// </summary>
        public void Dispose()
        {
            if (!_disposed)
            {
                DistributedLockManager.Release(this);
                _disposed = true;
            }
        }
    }

    /// <summary>
    /// 锁信息
    /// </summary>
    public class LockInfo
    {
        /// <summary>
        /// 锁键
        /// </summary>
        public string LockKey { get; set; }
        /// <summary>
        /// 锁持有值
        /// </summary>
        public string LockValue { get; set; }
        /// <summary>
        /// 获取锁的时间
        /// </summary>
        public DateTime AcquiredAt { get; set; }
        /// <summary>
        /// 锁的过期时间
        /// </summary>
        public DateTime ExpiresAt { get; set; }
    }

    /// <summary>
    /// 锁统计信息
    /// </summary>
    public class LockStatistics
    {
        /// <summary>
        /// 锁总数
        /// </summary>
        public int TotalLocks { get; set; }
        /// <summary>
        /// 活动锁数量
        /// </summary>
        public int ActiveLocks { get; set; }
        /// <summary>
        /// 已过期锁数量
        /// </summary>
        public int ExpiredLocks { get; set; }
        /// <summary>
        /// 平均锁持有时间（分钟）
        /// </summary>
        public double AverageLockDuration { get; set; }
    }

    /// <summary>
    /// 锁作用域
    /// </summary>
    public class LockScope : IDisposable
    {
        private readonly LockHandle _lockHandle;

        /// <summary>
        /// 创建锁作用域
        /// </summary>
        /// <param name="lockHandle">锁句柄</param>
        public LockScope(LockHandle lockHandle)
        {
            _lockHandle = lockHandle;
        }

        /// <summary>
        /// 释放作用域并释放其中的锁
        /// </summary>
        public void Dispose()
        {
            _lockHandle?.Dispose();
        }
    }

    /// <summary>
    /// 分布式锁辅助类
    /// </summary>
    public static class DistributedLockHelper
    {
        /// <summary>
        /// 创建锁作用域
        /// </summary>
        public static LockScope CreateScope(string lockKey, TimeSpan timeout, string lockValue = null)
        {
            if (DistributedLockManager.TryAcquire(lockKey, timeout, out var lockHandle, lockValue))
            {
                return new LockScope(lockHandle);
            }

            throw new TimeoutException(string.Format("无法在 {0} 内获取锁: {1}", timeout, lockKey));
        }

        /// <summary>
        /// 异步创建锁作用域
        /// </summary>
        public static async Task<LockScope> CreateScopeAsync(string lockKey, TimeSpan timeout, string lockValue = null)
        {
            var (acquired, lockHandle) = await DistributedLockManager.TryAcquireAsync(lockKey, timeout, lockValue);

            if (acquired)
            {
                return new LockScope(lockHandle);
            }

            throw new TimeoutException(string.Format("无法在 {0} 内获取锁: {1}", timeout, lockKey));
        }

        /// <summary>
        /// 使用锁作用域执行操作
        /// </summary>
        public static T UsingLock<T>(string lockKey, Func<T> action, TimeSpan timeout, string lockValue = null)
        {
            using (CreateScope(lockKey, timeout, lockValue))
            {
                return action();
            }
        }

        /// <summary>
        /// 异步使用锁作用域执行操作
        /// </summary>
        public static async Task<T> UsingLockAsync<T>(string lockKey, Func<Task<T>> action, TimeSpan timeout, string lockValue = null)
        {
            using (await CreateScopeAsync(lockKey, timeout, lockValue))
            {
                return await action();
            }
        }

        /// <summary>
        /// 尝试使用锁作用域执行操作
        /// </summary>
        public static bool TryUsingLock<T>(string lockKey, Func<T> action, TimeSpan timeout, out T result, string lockValue = null)
        {
            result = default;

            if (DistributedLockManager.TryAcquire(lockKey, timeout, out var lockHandle, lockValue))
            {
                try
                {
                    result = action();
                    return true;
                }
                finally
                {
                    DistributedLockManager.Release(lockHandle);
                }
            }

            return false;
        }

        /// <summary>
        /// 读写锁（简化版）
        /// </summary>
        public static class ReadWriteLock
        {
            private static readonly ConcurrentDictionary<string, object> _readLocks = new ConcurrentDictionary<string, object>();
            private static readonly ConcurrentDictionary<string, object> _writeLocks = new ConcurrentDictionary<string, object>();

            /// <summary>
            /// 获取指定资源的读锁
            /// </summary>
            /// <param name="resourceKey">资源键</param>
            /// <returns>释放读锁的作用域</returns>
            public static IDisposable AcquireReadLock(string resourceKey)
            {
                var readLock = _readLocks.GetOrAdd(resourceKey, new object());
                Monitor.Enter(readLock);
                return new ReadWriteLockScope(readLock, isReadLock: true);
            }

            /// <summary>
            /// 获取指定资源的写锁
            /// </summary>
            /// <param name="resourceKey">资源键</param>
            /// <returns>释放写锁的作用域</returns>
            public static IDisposable AcquireWriteLock(string resourceKey)
            {
                var writeLock = _writeLocks.GetOrAdd(resourceKey, new object());
                Monitor.Enter(writeLock);
                return new ReadWriteLockScope(writeLock, isReadLock: false);
            }

            private class ReadWriteLockScope : IDisposable
            {
                private readonly object _lock;
                private readonly bool _isReadLock;
                private bool _disposed;

                public ReadWriteLockScope(object @lock, bool isReadLock)
                {
                    _lock = @lock;
                    _isReadLock = isReadLock;
                }

                public void Dispose()
                {
                    if (!_disposed)
                    {
                        Monitor.Exit(_lock);
                        _disposed = true;
                    }
                }
            }
        }
    }
}

#pragma warning restore CS0618
