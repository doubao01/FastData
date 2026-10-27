using System;
using FastUntility.Base;

#if NETFRAMEWORK
using System.Runtime.Caching;
#else
using Microsoft.Extensions.Caching.Memory;
#endif

namespace FastUntility.Cache
{
    /// <summary>
    /// 缓存
    /// </summary>
    public static class BaseCache
    {
#if NETFRAMEWORK
        public static ObjectCache cache = MemoryCache.Default;
#else
        private static readonly MemoryCache _cache = new MemoryCache(new MemoryCacheOptions());
#endif

        /// <summary>
        /// 设置缓存
        /// </summary>
        /// <param name="key">键</param>
        /// <param name="value">值</param>
        /// <param name="Hours">过期小时</param>
        public static void Set(string key, string value, int Hours = 24 * 30 * 12)
        {
            if (!string.IsNullOrEmpty(key))
            {
#if NETFRAMEWORK
                cache.Remove(key);
                var policy = new CacheItemPolicy();
                policy.AbsoluteExpiration = DateTime.Now.AddHours(Hours);
                cache.Set(key, value, policy);
#else
                _cache.Remove(key);
                var options = new MemoryCacheEntryOptions();
                options.AbsoluteExpiration = DateTimeOffset.Now.AddHours(Hours);
                _cache.Set(key, value, options);
#endif
            }
        }

        /// <summary>
        /// 设置缓存
        /// </summary>
        /// <param name="key">键</param>
        /// <param name="value">值</param>
        /// <param name="Hours">过期小时</param>
        public static void Set<T>(string key, T value, int Hours = 24 * 30 * 12) where T : class, new()
        {
            if (!string.IsNullOrEmpty(key))
            {
#if NETFRAMEWORK
                cache.Remove(key);
                var policy = new CacheItemPolicy();
                policy.AbsoluteExpiration = DateTime.Now.AddHours(Hours);
                cache.Set(key, value, policy);
#else
                _cache.Remove(key);
                var options = new MemoryCacheEntryOptions();
                options.AbsoluteExpiration = DateTimeOffset.Now.AddHours(Hours);
                _cache.Set(key, value, options);
#endif
            }
        }

        /// <summary>
        /// 获取缓存
        /// </summary>
        /// <param name="key">键</param>
        public static string Get(string key)
        {
            try
            {
                if (!string.IsNullOrEmpty(key))
                {
#if NETFRAMEWORK
                    return cache.Get(key).ToStr();
#else
                    return _cache.Get<string>(key);
#endif
                }
                else
                    return null;
            }
            catch (Exception ex)
            {
                // 降级合理：缓存读不到时返回 null（调用方已有可判定信号），
                // 不能因缓存故障抛异常打断业务；但需留痕。
                BaseLog.SaveLog("BaseCache.Get 读取缓存失败，key：" + key + "，异常：" + ex, "BaseCache_exp");
                return null;
            }
        }

        /// <summary>
        /// 获取缓存
        /// </summary>
        /// <param name="key">键</param>
        public static T Get<T>(string key) where T : class, new()
        {
            try
            {
                if (!string.IsNullOrEmpty(key))
                {
#if NETFRAMEWORK
                    var result = new T();
                    var obj = cache.Get(key);
                    if (obj != null)
                        result = (T)obj;
                    return result;
#else
                    return _cache.Get<T>(key) ?? new T();
#endif
                }
                else
                    return new T();
            }
            catch (Exception ex)
            {
                // 数据错误：缓存读取/反序列化失败却返回 new T()，调用方无法区分
                // “缓存为空”与“读取失败”，容易拿到脏数据。签名不能改，用日志让失败可见。
                BaseLog.SaveLog("BaseCache.Get<T> 读取缓存失败，key：" + key + "，异常：" + ex, "BaseCache_exp");
                return new T();
            }
        }

        /// <summary>
        /// 删除缓存
        /// </summary>
        /// <param name="key">键</param>
        public static void Remove(string key)
        {
            if (!string.IsNullOrEmpty(key))
            {
#if NETFRAMEWORK
                cache.Remove(key);
#else
                _cache.Remove(key);
#endif
            }
        }

        /// <summary>
        /// 是否存在
        /// </summary>
        /// <param name="key">键</param>
        public static bool Exists(string key)
        {
            if (!string.IsNullOrEmpty(key))
            {
#if NETFRAMEWORK
                return cache.Contains(key);
#else
                return _cache.TryGetValue(key, out _);
#endif
            }
            else
                return false;
        }
    }
}
