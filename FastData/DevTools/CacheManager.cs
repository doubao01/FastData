using System;
using FastData.Context;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Caching;
using FastData.Model;

namespace FastData.DevTools
{
    /// <summary>
    /// 二级缓存管理器
    /// </summary>
    public static class CacheManager
    {
        private static readonly MemoryCache _cache = MemoryCache.Default;
        private static readonly ConcurrentDictionary<string, HashSet<string>> _entityCacheKeys = new();

        /// <summary>
        /// 获取缓存项
        /// </summary>
        public static T Get<T>(string key)
        {
            var item = _cache.Get(key);
            return item is T typedItem ? typedItem : default;
        }

        /// <summary>
        /// 设置缓存项
        /// </summary>
        public static void Set<T>(string key, T value, TimeSpan? expiration = null)
        {
            var policy = new CacheItemPolicy
            {
                AbsoluteExpiration = expiration.HasValue
                    ? DateTimeOffset.Now.Add(expiration.Value)
                    : DateTimeOffset.Now.AddMinutes(30)
            };

            _cache.Set(key, value, policy);
        }

        /// <summary>
        /// 移除缓存项
        /// </summary>
        public static void Remove(string key)
        {
            _cache.Remove(key);
        }

        /// <summary>
        /// 检查缓存是否存在
        /// </summary>
        public static bool Exists(string key)
        {
            return _cache.Contains(key);
        }

        /// <summary>
        /// 获取所有缓存键
        /// </summary>
        public static List<string> GetAllCacheKeys()
        {
            return _cache.Select(kvp => kvp.Key).ToList();
        }

        /// <summary>
        /// 清空所有缓存
        /// </summary>
        public static void Clear()
        {
            foreach (var item in _cache)
            {
                _cache.Remove(item.Key);
            }
            _entityCacheKeys.Clear();
        }

        /// <summary>
        /// 获取或创建缓存项
        /// </summary>
        public static T GetOrAdd<T>(string key, Func<T> factory, TimeSpan? expiration = null)
        {
            var item = _cache.Get(key);
            if (item is T typedItem)
            {
                return typedItem;
            }

            var value = factory();
            Set(key, value, expiration);
            return value;
        }

        /// <summary>
        /// 注册实体缓存键
        /// </summary>
        public static void RegisterEntityCache<T>(string cacheKey)
        {
            var entityName = typeof(T).Name;
            if (!_entityCacheKeys.ContainsKey(entityName))
            {
                _entityCacheKeys[entityName] = new HashSet<string>();
            }
            _entityCacheKeys[entityName].Add(cacheKey);
        }

        /// <summary>
        /// 清除实体相关缓存
        /// </summary>
        public static void ClearEntityCache<T>()
        {
            var entityName = typeof(T).Name;
            if (_entityCacheKeys.TryGetValue(entityName, out var keys))
            {
                foreach (var key in keys)
                {
                    _cache.Remove(key);
                }
                _entityCacheKeys[entityName].Clear();
            }
        }

        /// <summary>
        /// 获取缓存统计信息
        /// </summary>
        public static CacheStats GetStats()
        {
            var keys = _cache.Select(kvp => kvp.Key).ToList();
            return new CacheStats
            {
                TotalKeys = keys.Count,
                MemoryLimit = _cache.CacheMemoryLimit,
                PhysicalMemoryLimit = _cache.PhysicalMemoryLimit,
                PollingInterval = _cache.PollingInterval,
                Keys = keys
            };
        }
    }

    /// <summary>
    /// 缓存统计信息
    /// </summary>
    public class CacheStats
    {
        /// <summary>
        /// 缓存键总数
        /// </summary>
        public int TotalKeys { get; set; }
        /// <summary>
        /// 缓存内存上限（字节）
        /// </summary>
        public long MemoryLimit { get; set; }
        /// <summary>
        /// 物理内存上限（字节）
        /// </summary>
        public long PhysicalMemoryLimit { get; set; }
        /// <summary>
        /// 缓存轮询检查间隔
        /// </summary>
        public TimeSpan PollingInterval { get; set; }
        /// <summary>
        /// 当前所有缓存键
        /// </summary>
        public List<string> Keys { get; set; } = new List<string>();
    }

    /// <summary>
    /// 查询缓存装饰器
    /// </summary>
    public static class QueryCacheDecorator
    {
        /// <summary>
        /// 带缓存的查询
        /// </summary>
        public static List<T> QueryWithCache<T>(
            System.Linq.Expressions.Expression<Func<T, bool>> expression,
            TimeSpan? cacheDuration = null,
            string dbKey = null,
            string cacheKey = null) where T : class, new()
        {
            var key = cacheKey ?? GenerateCacheKey(expression, dbKey);

            var cached = CacheManager.Get<List<T>>(key);
            if (cached != null)
            {
                return cached;
            }

            var result = FastData.FastRead.Query<T>(expression, null, dbKey).ToList();
            CacheManager.Set(key, result, cacheDuration);
            CacheManager.RegisterEntityCache<T>(key);

            return result;
        }

        /// <summary>
        /// 带缓存的列表查询
        /// </summary>
        public static List<T> ListWithCache<T>(
            TimeSpan? cacheDuration = null,
            string dbKey = null,
            string cacheKey = null) where T : class, new()
        {
            var key = cacheKey ?? string.Format("List_{0}_{1}", typeof(T).Name, dbKey ?? "default");

            var cached = CacheManager.Get<List<T>>(key);
            if (cached != null)
            {
                return cached;
            }

            var result = FastData.FastRead.Query<T>(e => true, null, dbKey).ToList();
            CacheManager.Set(key, result, cacheDuration);
            CacheManager.RegisterEntityCache<T>(key);

            return result;
        }

        /// <summary>
        /// 带缓存的单条查询
        /// </summary>
        public static T FirstWithCache<T>(
            System.Linq.Expressions.Expression<Func<T, bool>> expression,
            TimeSpan? cacheDuration = null,
            string dbKey = null,
            string cacheKey = null) where T : class, new()
        {
            var key = cacheKey ?? GenerateCacheKey(expression, dbKey);

            var cached = CacheManager.Get<T>(key);
            if (cached != null)
            {
                return cached;
            }

            var result = FastData.FastRead.Query<T>(expression, null, dbKey).ToItem<T>();
            if (result != null)
            {
                CacheManager.Set(key, result, cacheDuration);
                CacheManager.RegisterEntityCache<T>(key);
            }

            return result;
        }

        /// <summary>
        /// 生成缓存键
        /// </summary>
        private static string GenerateCacheKey<T>(System.Linq.Expressions.Expression<Func<T, bool>> expression, string dbKey)
        {
            var exprStr = expression?.ToString() ?? "all";
            return string.Format("Query_{0}_{1}_{2}", typeof(T).Name, exprStr.GetHashCode(), dbKey ?? "default");
        }
    }

    /// <summary>
    /// 缓存自动失效拦截器
    /// </summary>
    public static class CacheInvalidationInterceptor
    {
        /// <summary>
        /// 拦截添加操作并使缓存失效
        /// </summary>
        public static WriteReturn InterceptAdd<T>(T entity, string dbKey = null) where T : class, new()
        {
            var result = FastData.FastWrite.Add<T>(entity, null, dbKey);
            if (result.IsSuccess)
            {
                CacheManager.ClearEntityCache<T>();
            }
            return result;
        }

        /// <summary>
        /// 拦截更新操作并使缓存失效
        /// </summary>
        public static WriteReturn InterceptUpdate<T>(T entity, string dbKey = null) where T : class, new()
        {
            var result = FastData.FastWrite.Update<T>(entity, null, null, dbKey);
            if (result.IsSuccess)
            {
                CacheManager.ClearEntityCache<T>();
            }
            return result;
        }

        /// <summary>
        /// 拦截删除操作并使缓存失效
        /// </summary>
        public static WriteReturn InterceptDelete<T>(System.Linq.Expressions.Expression<Func<T, bool>> expression, string dbKey = null) where T : class, new()
        {
            var result = FastData.FastWrite.Delete<T>(expression, null, dbKey);
            if (result.IsSuccess)
            {
                CacheManager.ClearEntityCache<T>();
            }
            return result;
        }

        /// <summary>
        /// 拦截批量添加操作并使缓存失效
        /// </summary>
        public static WriteReturn InterceptAddRange<T>(IEnumerable<T> entities, string dbKey = null) where T : class, new()
        {
            var result = FastData.FastWrite.AddList<T>(entities.ToList(), dbKey);
            if (result.IsSuccess)
            {
                CacheManager.ClearEntityCache<T>();
            }
            return result;
        }
    }
}