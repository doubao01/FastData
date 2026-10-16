using System;
using FastData.Context;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Caching;
using System.Threading.Tasks;

namespace FastData.DevTools
{
    /// <summary>
    /// 结果缓存工具
    /// </summary>
    public static class ResultCache
    {
        private static readonly MemoryCache _cache = MemoryCache.Default;
        private static readonly ConcurrentDictionary<string, CacheStatistics> _stats = new ConcurrentDictionary<string, CacheStatistics>();
        private static readonly object _lock = new object();

        /// <summary>
        /// 获取缓存项
        /// </summary>
        public static T Get<T>(string key)
        {
            var item = _cache.Get(key);
            if (item is T typedItem)
            {
                UpdateStatistics(key, true);
                return typedItem;
            }

            UpdateStatistics(key, false);
            return default;
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

            if (!_stats.ContainsKey(key))
            {
                _stats[key] = new CacheStatistics { Key = key };
            }
            _stats[key].LastUpdated = DateTime.Now;
        }

        /// <summary>
        /// 获取或创建缓存项
        /// </summary>
        public static T GetOrCreate<T>(string key, Func<T> factory, TimeSpan? expiration = null)
        {
            var item = Get<T>(key);
            if (item != null)
            {
                return item;
            }

            var value = factory();
            Set(key, value, expiration);
            return value;
        }

        /// <summary>
        /// 异步获取或创建缓存项
        /// </summary>
        public static async Task<T> GetOrCreateAsync<T>(string key, Func<Task<T>> factory, TimeSpan? expiration = null)
        {
            var item = Get<T>(key);
            if (item != null)
            {
                return item;
            }

            var value = await factory();
            Set(key, value, expiration);
            return value;
        }

        /// <summary>
        /// 移除缓存项
        /// </summary>
        public static void Remove(string key)
        {
            _cache.Remove(key);
            _stats.TryRemove(key, out _);
        }

        /// <summary>
        /// 检查缓存是否存在
        /// </summary>
        public static bool Exists(string key)
        {
            return _cache.Contains(key);
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
            _stats.Clear();
        }

        /// <summary>
        /// 按前缀清除缓存
        /// </summary>
        public static void ClearByPrefix(string prefix)
        {
            var keys = _cache.Where(kvp => kvp.Key.StartsWith(prefix))
                           .Select(kvp => kvp.Key)
                           .ToList();

            foreach (var key in keys)
            {
                _cache.Remove(key);
                _stats.TryRemove(key, out _);
            }
        }

        /// <summary>
        /// 获取缓存统计
        /// </summary>
        public static CacheStatistics GetStatistics(string key)
        {
            return _stats.TryGetValue(key, out var stats) ? stats : null;
        }

        /// <summary>
        /// 获取所有缓存统计
        /// </summary>
        public static List<CacheStatistics> GetAllStatistics()
        {
            return _stats.Values.ToList();
        }

        /// <summary>
        /// 生成缓存报告
        /// </summary>
        public static CacheReport GenerateReport()
        {
            var stats = _stats.Values.ToList();
            return new CacheReport
            {
                TotalKeys = _cache.Count(),
                TotalHits = stats.Sum(s => s.Hits),
                TotalMisses = stats.Sum(s => s.Misses),
                HitRate = stats.Any() ? (double)stats.Sum(s => s.Hits) / (stats.Sum(s => s.Hits) + stats.Sum(s => s.Misses)) : 0,
                MostHitKey = stats.OrderByDescending(s => s.Hits).FirstOrDefault()?.Key,
                LeastHitKey = stats.OrderBy(s => s.Hits).FirstOrDefault()?.Key,
                GeneratedAt = DateTime.Now
            };
        }

        /// <summary>
        /// 缓存预热
        /// </summary>
        public static void WarmUp<T>(string key, Func<T> factory, TimeSpan? expiration = null)
        {
            if (!Exists(key))
            {
                Set(key, factory(), expiration);
            }
        }

        /// <summary>
        /// 批量设置缓存
        /// </summary>
        public static void SetBatch<T>(Dictionary<string, T> items, TimeSpan? expiration = null)
        {
            foreach (var kvp in items)
            {
                Set(kvp.Key, kvp.Value, expiration);
            }
        }

        /// <summary>
        /// 批量获取缓存
        /// </summary>
        public static Dictionary<string, T> GetBatch<T>(IEnumerable<string> keys)
        {
            var result = new Dictionary<string, T>();
            foreach (var key in keys)
            {
                var value = Get<T>(key);
                if (value != null)
                {
                    result[key] = value;
                }
            }
            return result;
        }

        /// <summary>
        /// 刷新过期缓存
        /// </summary>
        public static void RefreshExpired<T>(Func<string, T> factory)
        {
            var expiredKeys = _stats.Where(s =>
                s.Value.LastUpdated.HasValue &&
                DateTime.Now - s.Value.LastUpdated.Value > TimeSpan.FromMinutes(30))
                .Select(s => s.Key)
                .ToList();

            foreach (var key in expiredKeys)
            {
                try
                {
                    var value = factory(key);
                    Set(key, value);
                }
                catch
                {
                    // 忽略刷新错误
                }
            }
        }

        #region 私有方法

        private static void UpdateStatistics(string key, bool hit)
        {
            if (!_stats.ContainsKey(key))
            {
                _stats[key] = new CacheStatistics { Key = key };
            }

            if (hit)
            {
                _stats[key].Hits++;
            }
            else
            {
                _stats[key].Misses++;
            }

            _stats[key].LastAccessed = DateTime.Now;
        }

        #endregion
    }

    /// <summary>
    /// 缓存统计
    /// </summary>
    public class CacheStatistics
    {
        /// <summary>
        /// 缓存键
        /// </summary>
        public string Key { get; set; }
        /// <summary>
        /// 命中次数
        /// </summary>
        public long Hits { get; set; }
        /// <summary>
        /// 未命中次数
        /// </summary>
        public long Misses { get; set; }
        /// <summary>
        /// 最后访问时间
        /// </summary>
        public DateTime? LastAccessed { get; set; }
        /// <summary>
        /// 最后更新时间
        /// </summary>
        public DateTime? LastUpdated { get; set; }

        /// <summary>
        /// 命中率（0-1）
        /// </summary>
        public double HitRate => Hits + Misses > 0 ? (double)Hits / (Hits + Misses) : 0;
    }

    /// <summary>
    /// 缓存报告
    /// </summary>
    public class CacheReport
    {
        /// <summary>
        /// 缓存键总数
        /// </summary>
        public int TotalKeys { get; set; }
        /// <summary>
        /// 累计命中次数
        /// </summary>
        public long TotalHits { get; set; }
        /// <summary>
        /// 累计未命中次数
        /// </summary>
        public long TotalMisses { get; set; }
        /// <summary>
        /// 整体命中率
        /// </summary>
        public double HitRate { get; set; }
        /// <summary>
        /// 命中次数最多的键
        /// </summary>
        public string MostHitKey { get; set; }
        /// <summary>
        /// 命中次数最少的键
        /// </summary>
        public string LeastHitKey { get; set; }
        /// <summary>
        /// 报告生成时间
        /// </summary>
        public DateTime GeneratedAt { get; set; }
    }

    /// <summary>
    /// 缓存装饰器
    /// </summary>
    public static class CacheDecorator
    {
        /// <summary>
        /// 缓存装饰器 - 同步
        /// </summary>
        public static T WithCache<T>(string key, Func<T> factory, TimeSpan? expiration = null)
        {
            return ResultCache.GetOrCreate(key, factory, expiration);
        }

        /// <summary>
        /// 缓存装饰器 - 异步
        /// </summary>
        public static async Task<T> WithCacheAsync<T>(string key, Func<Task<T>> factory, TimeSpan? expiration = null)
        {
            return await ResultCache.GetOrCreateAsync(key, factory, expiration);
        }

        /// <summary>
        /// 缓存装饰器 - 带条件
        /// </summary>
        public static T WithCache<T>(string key, Func<T> factory, TimeSpan? expiration, Func<T, bool> shouldCache)
        {
            var cached = ResultCache.Get<T>(key);
            if (cached != null)
            {
                return cached;
            }

            var value = factory();
            if (shouldCache(value))
            {
                ResultCache.Set(key, value, expiration);
            }
            return value;
        }

        /// <summary>
        /// 缓存装饰器 - 带失败重试
        /// </summary>
        public static T WithCacheAndRetry<T>(string key, Func<T> factory, TimeSpan? expiration, int maxRetries = 3)
        {
            var cached = ResultCache.Get<T>(key);
            if (cached != null)
            {
                return cached;
            }

            int attempt = 0;
            while (true)
            {
                attempt++;
                try
                {
                    var value = factory();
                    ResultCache.Set(key, value, expiration);
                    return value;
                }
                catch when (attempt < maxRetries)
                {
                    Task.Delay(100 * attempt).Wait();
                }
            }
        }

        /// <summary>
        /// 缓存装饰器 - 滑动过期
        /// </summary>
        public static T WithSlidingCache<T>(string key, Func<T> factory, TimeSpan slidingExpiration)
        {
            var cached = ResultCache.Get<T>(key);
            if (cached != null)
            {
                // 更新过期时间
                ResultCache.Set(key, cached, slidingExpiration);
                return cached;
            }

            var value = factory();
            ResultCache.Set(key, value, slidingExpiration);
            return value;
        }

        /// <summary>
        /// 多级缓存
        /// </summary>
        public static T WithMultiLevelCache<T>(string key, Func<T> factory, TimeSpan? l1Expiration = null, TimeSpan? l2Expiration = null)
        {
            // L1 缓存
            var l1Cached = ResultCache.Get<T>(string.Format("L1_{0}", key));
            if (l1Cached != null)
            {
                return l1Cached;
            }

            // L2 缓存
            var l2Cached = ResultCache.Get<T>(string.Format("L2_{0}", key));
            if (l2Cached != null)
            {
                // 提升 L2 到 L1
                ResultCache.Set(string.Format("L1_{0}", key), l2Cached, l1Expiration ?? TimeSpan.FromMinutes(5));
                return l2Cached;
            }

            // 从数据源获取
            var value = factory();
            ResultCache.Set(string.Format("L1_{0}", key), value, l1Expiration ?? TimeSpan.FromMinutes(5));
            ResultCache.Set(string.Format("L2_{0}", key), value, l2Expiration ?? TimeSpan.FromHours(1));
            return value;
        }
    }

    /// <summary>
    /// 缓存失效策略
    /// </summary>
    public static class CacheInvalidationStrategy
    {
        /// <summary>
        /// 时间失效
        /// </summary>
        public static void InvalidateByTime(string pattern, TimeSpan olderThan)
        {
            var cutoff = DateTime.Now - olderThan;
            var stats = ResultCache.GetAllStatistics();

            foreach (var stat in stats.Where(s => s.LastUpdated.HasValue && s.LastUpdated.Value < cutoff))
            {
                ResultCache.Remove(stat.Key);
            }
        }

        /// <summary>
        /// 使用率失效
        /// </summary>
        public static void InvalidateByUsageRate(string pattern, double minHitRate)
        {
            var stats = ResultCache.GetAllStatistics();

            foreach (var stat in stats.Where(s => s.HitRate < minHitRate))
            {
                ResultCache.Remove(stat.Key);
            }
        }

        /// <summary>
        /// 空间失效（LRU）
        /// </summary>
        public static void InvalidateBySpace(int maxItems)
        {
            var stats = ResultCache.GetAllStatistics();
            if (stats.Count > maxItems)
            {
                var itemsToRemove = stats.OrderBy(s => s.LastAccessed).Take(stats.Count - maxItems);
                foreach (var item in itemsToRemove)
                {
                    ResultCache.Remove(item.Key);
                }
            }
        }

        /// <summary>
        /// 依赖失效
        /// </summary>
        public static void InvalidateByDependency(string dependentKey)
        {
            var pattern = string.Format("{0}_*", dependentKey);
            ResultCache.ClearByPrefix(pattern);
        }
    }
}