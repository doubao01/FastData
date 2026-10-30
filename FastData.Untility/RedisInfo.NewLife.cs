#if !NETFRAMEWORK
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NewLife.Caching;
using Newtonsoft.Json;
using FastRedis.Config;
using System.Reflection;
using System.IO;
using FastUntility.Base;

namespace FastRedis
{
    /// <summary>
    /// redis操作类（NewLife.Redis 实现，单例模式）
    /// </summary>
    public static class RedisInfo
    {
        private static readonly Lazy<FullRedis> _redisLazy = new Lazy<FullRedis>(() =>
        {
            var config = RedisConfig.GetConfig();
            
            // 验证配置
            if (string.IsNullOrEmpty(config.WriteServerList))
            {
                throw new InvalidOperationException("Redis 配置无效：WriteServerList 不能为空，请在 db.config 中配置 Redis 服务器地址");
            }
            
            var redis = new FullRedis();
            redis.Init(config.WriteServerList);
            return redis;
        });

        /// <summary>
        /// 获取 Redis 单例实例
        /// </summary>
        public static FullRedis Redis => _redisLazy.Value;

        /// <summary>
        /// 切换到指定的 Redis 数据库索引
        /// </summary>
        /// <param name="db">数据库索引</param>
        /// <returns>切换前的数据库索引</returns>
        private static int SwitchDatabase(int db)
        {
            if (db < 0 || db > 15)
                throw new ArgumentException("Redis 数据库索引必须在 0-15 之间", nameof(db));
            
            var previousDb = Redis.Db;
            if (previousDb != db)
            {
                Redis.Db = db;
            }
            return previousDb;
        }

        /// <summary>
        /// 资源文件初始化
        /// </summary>
        public static void Init(string dbFile = "db.config", string projectName = null)
        {
            if (projectName == null)
                projectName = Assembly.GetCallingAssembly().GetName().Name;
            RedisConfig.GetConfig(projectName, dbFile);
        }

        /// <summary>
        /// 是否存在
        /// </summary>
        public static bool Exists(string key, int db = 0)
        {
            try
            {
                if (string.IsNullOrEmpty(key))
                    return false;
                
                var previousDb = SwitchDatabase(db);
                try
                {
                    var exists = Redis.ContainsKey(key);
                    MarkSuccess();
                    return exists;
                }
                finally
                {
                    SwitchDatabase(previousDb);
                }
            }
            catch (Exception ex)
            {
                SaveLog(ex, "Exists", key);
                return false;
            }
        }

        /// <summary>
        /// 是否存在 asy
        /// </summary>
        public static async Task<bool> ExistsAsync(string key, int db = 0)
        {
            try
            {
                if (string.IsNullOrEmpty(key))
                    return false;
                
                return await Task.Run(() => Exists(key, db));
            }
            catch (Exception ex)
            {
                SaveLog(ex, "ExistsAsync", key);
                return false;
            }
        }

        /// <summary>
        /// 设置值 item
        /// </summary>
        public static bool Set<T>(string key, T model, int hours = 24 * 30 * 12, int db = 0)
        {
            try
            {
                if (string.IsNullOrEmpty(key))
                    return false;
                
                var previousDb = SwitchDatabase(db);
                try
                {
                    Redis.Set(key, model, TimeSpan.FromHours(hours));
                    MarkSuccess();
                    return true;
                }
                finally
                {
                    SwitchDatabase(previousDb);
                }
            }
            catch (Exception ex)
            {
                SaveLog(ex, "Set<T>", key);
                return false;
            }
        }

        /// <summary>
        /// 设置值 item asy
        /// </summary>
        public static async Task<bool> SetAsync<T>(string key, T model, int hours = 24 * 30 * 12, int db = 0)
        {
            try
            {
                return await Task.Run(() => Set(key, model, hours, db));
            }
            catch (Exception ex)
            {
                SaveLog(ex, "SetAsync<T>", key);
                return false;
            }
        }

        /// <summary>
        /// 设置值 item (string)
        /// </summary>
        public static bool Set(string key, string model, int hours = 24 * 30 * 12, int db = 0)
        {
            try
            {
                if (string.IsNullOrEmpty(key))
                    return false;
                
                var previousDb = SwitchDatabase(db);
                try
                {
                    Redis.Set(key, model, TimeSpan.FromHours(hours));
                    return true;
                }
                finally
                {
                    SwitchDatabase(previousDb);
                }
            }
            catch (Exception ex)
            {
                SaveLog(ex, "Set", key);
                return false;
            }
        }

        /// <summary>
        /// 设置值 item asy (string)
        /// </summary>
        public static async Task<bool> SetAsync(string key, string model, int hours = 24 * 30 * 12, int db = 0)
        {
            try
            {
                return await Task.Run(() => Set(key, model, hours, db));
            }
            catch (Exception ex)
            {
                SaveLog(ex, "SetAsync", key);
                return false;
            }
        }

        /// <summary>
        /// 设置值 item (分钟)
        /// </summary>
        public static bool Set(string key, string model, double Minutes, int db = 0)
        {
            try
            {
                if (string.IsNullOrEmpty(key))
                    return false;
                
                var previousDb = SwitchDatabase(db);
                try
                {
                    Redis.Set(key, model, TimeSpan.FromMinutes(Minutes));
                    return true;
                }
                finally
                {
                    SwitchDatabase(previousDb);
                }
            }
            catch (Exception ex)
            {
                SaveLog(ex, "Set", key);
                return false;
            }
        }

        /// <summary>
        /// 获取值 item
        /// </summary>
        public static string Get(string key, int db = 0)
        {
            try
            {
                if (string.IsNullOrEmpty(key))
                    return "";
                
                var previousDb = SwitchDatabase(db);
                try
                {
                    var value = Redis.Get<string>(key);
                    MarkSuccess();
                    return value;
                }
                finally
                {
                    SwitchDatabase(previousDb);
                }
            }
            catch (Exception ex)
            {
                SaveLog(ex, "Get", key);
                return "";
            }
        }

        /// <summary>
        /// 获取值 item asy
        /// </summary>
        public static async Task<string> GetAsync(string key, int db = 0)
        {
            try
            {
                return await Task.Run(() => Get(key, db));
            }
            catch (Exception ex)
            {
                SaveLog(ex, "GetAsync", key);
                return "";
            }
        }

        /// <summary>
        /// 获取值 item (泛型)
        /// </summary>
        public static T Get<T>(string key, int db = 0) where T : class, new()
        {
            try
            {
                if (string.IsNullOrEmpty(key))
                    return null;
                
                var previousDb = SwitchDatabase(db);
                try
                {
                    var json = Redis.Get<string>(key);
                    if (string.IsNullOrEmpty(json))
                        return null;
                    return JsonConvert.DeserializeObject<T>(json);
                }
                finally
                {
                    SwitchDatabase(previousDb);
                }
            }
            catch (Exception ex)
            {
                SaveLog(ex, "Get<T>", key);
                return null;
            }
        }

        /// <summary>
        /// 获取值 item asy (泛型)
        /// </summary>
        public static async Task<T> GetAsync<T>(string key, int db = 0) where T : class, new()
        {
            try
            {
                return await Task.Run(() => Get<T>(key, db));
            }
            catch (Exception ex)
            {
                SaveLog(ex, "GetAsync<T>", key);
                return null;
            }
        }

        /// <summary>
        /// 删除值 item
        /// </summary>
        public static bool Remove(string key, int db = 0)
        {
            try
            {
                if (string.IsNullOrEmpty(key))
                    return false;
                
                var previousDb = SwitchDatabase(db);
                try
                {
                    Redis.Remove(key);
                    return true;
                }
                finally
                {
                    SwitchDatabase(previousDb);
                }
            }
            catch (Exception ex)
            {
                SaveLog(ex, "Remove", key);
                return false;
            }
        }

        /// <summary>
        /// 删除值 item asy
        /// </summary>
        public static async Task<bool> RemoveAsync(string key, int db = 0)
        {
            try
            {
                return await Task.Run(() => Remove(key, db));
            }
            catch (Exception ex)
            {
                SaveLog(ex, "RemoveAsync", key);
                return false;
            }
        }

        /// <summary>
        /// 设置值 Dic（批量）
        /// </summary>
        public static bool SetDic<T>(Dictionary<string, T> dic, int db = 0)
        {
            try
            {
                var previousDb = SwitchDatabase(db);
                try
                {
                    Redis.SetAll(dic);
                    return true;
                }
                finally
                {
                    SwitchDatabase(previousDb);
                }
            }
            catch (Exception ex)
            {
                SaveLog(ex, "SetDic<T>", "");
                return false;
            }
        }

        /// <summary>
        /// 设置值 Dic asy（批量）
        /// </summary>
        public static async Task<bool> SetDicAsync<T>(Dictionary<string, T> dic, int db = 0)
        {
            try
            {
                return await Task.Run(() => SetDic(dic, db));
            }
            catch (Exception ex)
            {
                SaveLog(ex, "SetDicAsync<T>", "");
                return false;
            }
        }

        /// <summary>
        /// 获取值 dic（批量）
        /// </summary>
        public static IDictionary<string, T> GetDic<T>(string[] keys, int db = 0) where T : class, new()
        {
            try
            {
                var previousDb = SwitchDatabase(db);
                try
                {
                    return Redis.GetAll<T>(keys);
                }
                finally
                {
                    SwitchDatabase(previousDb);
                }
            }
            catch (Exception ex)
            {
                SaveLog(ex, "GetDic<T>", "");
                return new Dictionary<string, T>();
            }
        }

        /// <summary>
        /// 获取值 dic asy（批量）
        /// </summary>
        public static async Task<IDictionary<string, T>> GetDicAsync<T>(string[] keys, int db = 0) where T : class, new()
        {
            try
            {
                return await Task.Run(() => GetDic<T>(keys, db));
            }
            catch (Exception ex)
            {
                SaveLog(ex, "GetDicAsync<T>", "");
                return new Dictionary<string, T>();
            }
        }

        /// <summary>
        /// 删除值 dic（批量）
        /// </summary>
        public static bool RemoveDic(string[] keys, int db = 0)
        {
            try
            {
                var previousDb = SwitchDatabase(db);
                try
                {
                    Redis.Remove(keys);
                    return true;
                }
                finally
                {
                    SwitchDatabase(previousDb);
                }
            }
            catch (Exception ex)
            {
                SaveLog(ex, "RemoveDic", "");
                return false;
            }
        }

        /// <summary>
        /// 删除值 dic asy（批量）
        /// </summary>
        public static async Task<bool> RemoveDicAsync(string[] keys, int db = 0)
        {
            try
            {
                return await Task.Run(() => RemoveDic(keys, db));
            }
            catch (Exception ex)
            {
                SaveLog(ex, "RemoveDicAsync", "");
                return false;
            }
        }

        /// <summary>
        /// 获取或添加（缓存不存在时添加）
        /// </summary>
        public static T GetOrAdd<T>(string key, Func<T> factory, int hours = 24 * 30 * 12, int db = 0) where T : class, new()
        {
            try
            {
                var previousDb = SwitchDatabase(db);
                try
                {
                    if (Redis.ContainsKey(key))
                        return Get<T>(key, db);

                    var value = factory();
                    Set(key, value, hours, db);
                    return value;
                }
                finally
                {
                    SwitchDatabase(previousDb);
                }
            }
            catch (Exception ex)
            {
                SaveLog(ex, "GetOrAdd<T>", key);
                return new T();
            }
        }

        /// <summary>
        /// 获取或添加 asy（缓存不存在时添加）
        /// </summary>
        public static async Task<T> GetOrAddAsync<T>(string key, Func<Task<T>> factory, int hours = 24 * 30 * 12, int db = 0) where T : class, new()
        {
            try
            {
                var previousDb = SwitchDatabase(db);
                try
                {
                    if (await Task.Run(() => Redis.ContainsKey(key)))
                        return await GetAsync<T>(key, db);

                    var value = await factory();
                    await SetAsync(key, value, hours, db);
                    return value;
                }
                finally
                {
                    SwitchDatabase(previousDb);
                }
            }
            catch (Exception ex)
            {
                SaveLog(ex, "GetOrAddAsync<T>", key);
                return new T();
            }
        }

        /// <summary>
        /// 递增
        /// </summary>
        public static long Increment(string key, int value = 1, int db = 0)
        {
            try
            {
                var previousDb = SwitchDatabase(db);
                try
                {
                    return Redis.Increment(key, value);
                }
                finally
                {
                    SwitchDatabase(previousDb);
                }
            }
            catch (Exception ex)
            {
                SaveLog(ex, "Increment", key);
                return 0;
            }
        }

        /// <summary>
        /// 递增 asy
        /// </summary>
        public static async Task<long> IncrementAsync(string key, int value = 1, int db = 0)
        {
            try
            {
                return await Task.Run(() => Increment(key, value, db));
            }
            catch (Exception ex)
            {
                SaveLog(ex, "IncrementAsync", key);
                return 0;
            }
        }

        /// <summary>
        /// 递减
        /// </summary>
        public static long Decrement(string key, int value = 1, int db = 0)
        {
            try
            {
                var previousDb = SwitchDatabase(db);
                try
                {
                    return Redis.Decrement(key, value);
                }
                finally
                {
                    SwitchDatabase(previousDb);
                }
            }
            catch (Exception ex)
            {
                SaveLog(ex, "Decrement", key);
                return 0;
            }
        }

        /// <summary>
        /// 递减 asy
        /// </summary>
        public static async Task<long> DecrementAsync(string key, int value = 1, int db = 0)
        {
            try
            {
                return await Task.Run(() => Decrement(key, value, db));
            }
            catch (Exception ex)
            {
                SaveLog(ex, "DecrementAsync", key);
                return 0;
            }
        }

        /// <summary>
        /// 设置过期时间
        /// </summary>
        public static bool SetExpire(string key, TimeSpan expire, int db = 0)
        {
            try
            {
                var previousDb = SwitchDatabase(db);
                try
                {
                    Redis.SetExpire(key, expire);
                    return true;
                }
                finally
                {
                    SwitchDatabase(previousDb);
                }
            }
            catch (Exception ex)
            {
                SaveLog(ex, "SetExpire", key);
                return false;
            }
        }

        /// <summary>
        /// 设置过期时间 asy
        /// </summary>
        public static async Task<bool> SetExpireAsync(string key, TimeSpan expire, int db = 0)
        {
            try
            {
                return await Task.Run(() => SetExpire(key, expire, db));
            }
            catch (Exception ex)
            {
                SaveLog(ex, "SetExpireAsync", key);
                return false;
            }
        }

        /// <summary>
        /// 获取过期时间
        /// </summary>
        public static TimeSpan GetExpire(string key, int db = 0)
        {
            try
            {
                var previousDb = SwitchDatabase(db);
                try
                {
                    return Redis.GetExpire(key);
                }
                finally
                {
                    SwitchDatabase(previousDb);
                }
            }
            catch (Exception ex)
            {
                SaveLog(ex, "GetExpire", key);
                return TimeSpan.Zero;
            }
        }

        /// <summary>
        /// 获取过期时间 asy
        /// </summary>
        public static async Task<TimeSpan> GetExpireAsync(string key, int db = 0)
        {
            try
            {
                return await Task.Run(() => GetExpire(key, db));
            }
            catch (Exception ex)
            {
                SaveLog(ex, "GetExpireAsync", key);
                return TimeSpan.Zero;
            }
        }

        /// <summary>
        /// 获取列表集合
        /// </summary>
        public static IList<T> GetList<T>(string key, int db = 0) where T : class
        {
            try
            {
                var previousDb = SwitchDatabase(db);
                try
                {
                    return Redis.GetList<T>(key);
                }
                finally
                {
                    SwitchDatabase(previousDb);
                }
            }
            catch (Exception ex)
            {
                SaveLog(ex, "GetList<T>", key);
                return new List<T>();
            }
        }

        /// <summary>
        /// 获取列表集合 asy
        /// </summary>
        public static async Task<IList<T>> GetListAsync<T>(string key, int db = 0) where T : class
        {
            try
            {
                return await Task.Run(() => GetList<T>(key, db));
            }
            catch (Exception ex)
            {
                SaveLog(ex, "GetListAsync<T>", key);
                return new List<T>();
            }
        }

        /// <summary>
        /// 获取哈希集合
        /// </summary>
        public static IDictionary<string, T> GetDictionary<T>(string key, int db = 0) where T : class
        {
            try
            {
                var previousDb = SwitchDatabase(db);
                try
                {
                    return Redis.GetDictionary<T>(key);
                }
                finally
                {
                    SwitchDatabase(previousDb);
                }
            }
            catch (Exception ex)
            {
                SaveLog(ex, "GetDictionary<T>", key);
                return new Dictionary<string, T>();
            }
        }

        /// <summary>
        /// 获取哈希集合 asy
        /// </summary>
        public static async Task<IDictionary<string, T>> GetDictionaryAsync<T>(string key, int db = 0) where T : class
        {
            try
            {
                return await Task.Run(() => GetDictionary<T>(key, db));
            }
            catch (Exception ex)
            {
                SaveLog(ex, "GetDictionaryAsync<T>", key);
                return new Dictionary<string, T>();
            }
        }

        /// <summary>
        /// 获取 Set 集合
        /// </summary>
        public static ICollection<T> GetSet<T>(string key, int db = 0) where T : class
        {
            try
            {
                var previousDb = SwitchDatabase(db);
                try
                {
                    return Redis.GetSet<T>(key);
                }
                finally
                {
                    SwitchDatabase(previousDb);
                }
            }
            catch (Exception ex)
            {
                SaveLog(ex, "GetSet<T>", key);
                return new HashSet<T>();
            }
        }

        /// <summary>
        /// 获取 Set 集合 asy
        /// </summary>
        public static async Task<ICollection<T>> GetSetAsync<T>(string key, int db = 0) where T : class
        {
            try
            {
                return await Task.Run(() => GetSet<T>(key, db));
            }
            catch (Exception ex)
            {
                SaveLog(ex, "GetSetAsync<T>", key);
                return new HashSet<T>();
            }
        }

        #region 出错日志
        /// <summary>
        /// 最近一次成功操作的时间（用于判断 Redis 是否真的在工作）。
        /// </summary>
        private static long _lastSuccessTicks;

        /// <summary>
        /// 上次输出告警的时间，用于节流，避免 Redis 长时间不可用时刷屏。
        /// </summary>
        private static long _lastWarnTicks;

        private static volatile bool _available = true;

        /// <summary>
        /// Redis 当前是否可用。
        /// <para>
        /// 缓存不可用时本库一律**降级**：读写失败只返回 false / 空值，不影响业务正确性。
        /// 但降级必须可观测 —— 否则「配置了 Redis」与「Redis 真的在工作」无法区分，
        /// 表现为缓存命中率恒为 0、每次请求都穿透到数据库，而日志里只有一堆没人看的堆栈。
        /// </para>
        /// <para>
        /// 监控系统应定期检查这个属性；为 false 说明缓存层已经形同虚设。
        /// </para>
        /// </summary>
        public static bool IsAvailable => _available;

        /// <summary>
        /// 最近一次失败的错误信息；从未失败则为 null。
        /// </summary>
        public static string LastError { get; private set; }

        /// <summary>
        /// 最近一次成功操作的时间；从未成功则为 null。
        /// </summary>
        public static DateTime? LastSuccessAt
        {
            get
            {
                var ticks = Interlocked.Read(ref _lastSuccessTicks);
                return ticks == 0 ? (DateTime?)null : new DateTime(ticks, DateTimeKind.Utc);
            }
        }

        /// <summary>
        /// 把健康状态重置为「可用」。
        /// Redis 恢复后、或调用方主动探活成功后调用。
        /// </summary>
        public static void ResetHealth()
        {
            _available = true;
            LastError = null;
        }

        private static void MarkSuccess()
        {
            Interlocked.Exchange(ref _lastSuccessTicks, DateTime.UtcNow.Ticks);

            if (!_available)
                _available = true;
        }

        private static void SaveLog<T>(Exception ex, string CurrentMethod)
        {
            LogManager.SaveLog(string.Format("方法：{0},对象：{1},出错详情：{2}", CurrentMethod, typeof(T).Name, ex.ToString()), "redis_exp");
            MarkFailure(ex, CurrentMethod);
        }

        private static void SaveLog(Exception ex, string CurrentMethod, string key)
        {
            LogManager.SaveLog(string.Format("方法：{0},键：{1},出错详情：{2}", CurrentMethod, key, ex.ToString()), "redis_exp");
            MarkFailure(ex, CurrentMethod);
        }

        /// <summary>
        /// 标记 Redis 不可用，并**节流**输出一次可见告警。
        /// </summary>
        /// <remarks>
        /// 节流是必要的：Redis 长时间不可用时每次缓存操作都会失败，
        /// 若不节流，日志会被同一条错误刷满，反而掩盖其它问题。
        /// 首次失败立即告警，之后每 60 秒最多一次。
        /// </remarks>
        private static void MarkFailure(Exception ex, string method)
        {
            _available = false;
            LastError = string.Format("{0}: {1}", ex.GetType().Name, ex.Message);

            var now = DateTime.UtcNow.Ticks;
            var last = Interlocked.Read(ref _lastWarnTicks);
            if (now - last < TimeSpan.FromSeconds(60).Ticks)
                return;

            if (Interlocked.CompareExchange(ref _lastWarnTicks, now, last) != last)
                return;

            System.Diagnostics.Trace.TraceError(string.Format(
                "Redis 不可用，缓存已降级（不影响业务正确性，但命中率归零）：{0} @ {1}。详见 redis_exp 日志。",
                LastError, method));
        }
        #endregion
    }
}
#endif