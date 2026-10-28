using System.Collections.Generic;
using System.Linq;
using System.Configuration;
using FastData.DbTypes;
using FastData.Model;
using FastData.Base;
using FastData.ConnectionPool;
using FastUntility.Base;
using System;
using System.Reflection;
using System.IO;
using System.Xml;

namespace FastData.Config
{
    /// <summary>
    /// 数据库配置类
    /// </summary>
    internal class DataConfig : ConfigurationSection
    {
        [ConfigurationProperty("Default", IsRequired = false, DefaultValue = "")]
        public string Default
        {
            get
            {
                return this["Default"].ToString();
            }
        }

        [ConfigurationProperty("Active", IsRequired = false, DefaultValue = "")]
        public string Active
        {
            get
            {
                return this["Active"].ToString();
            }
        }

        [ConfigurationProperty("Connections", IsRequired = false)]
        [ConfigurationCollection(typeof(CollectionConfig), AddItemName = "Add")]
        public CollectionConfig Connections
        {
            get
            {
                return (CollectionConfig)this["Connections"];
            }
        }

        #region oralce 节点
        /// <summary>
        /// oralce 节点
        /// </summary>
        [ConfigurationProperty("Oracle")]
        [ConfigurationCollection(typeof(CollectionConfig), AddItemName = "Add")]
        public CollectionConfig Oracle
        {
            get
            {
                return (CollectionConfig)this["Oracle"];
            }
        }
        #endregion
        
        #region sqlserver 节点
         ///<summary>
         ///sqlserver 节点
         ///</summary>
        [ConfigurationProperty("SqlServer")]
        [ConfigurationCollection(typeof(CollectionConfig), AddItemName = "Add")]
        public CollectionConfig SqlServer
        {
            get
            {
                return (CollectionConfig)this["SqlServer"];
            }
        }
        #endregion

        #region mysql 节点
        /// <summary>
        /// mysql 节点
        /// </summary>
        [ConfigurationProperty("MySql")]
        [ConfigurationCollection(typeof(CollectionConfig), AddItemName = "Add")]
        public CollectionConfig MySql
        {
            get
            {
                return (CollectionConfig)this["MySql"];
            }
        }
        #endregion
        
        #region db2 节点
        /// <summary>
        /// db2 节点
        /// </summary>
        [ConfigurationProperty("DB2")]
        [ConfigurationCollection(typeof(CollectionConfig), AddItemName = "Add")]
        public CollectionConfig DB2
        {
            get
            {
                return (CollectionConfig)this["DB2"];
            }
        }
        #endregion

        #region SQLite 节点
        /// <summary>
        /// SQLite 节点
        /// </summary>
        [ConfigurationProperty("SQLite", IsRequired = false)]
        [ConfigurationCollection(typeof(CollectionConfig), AddItemName = "Add")]
        public CollectionConfig SQLite
        {
            get
            {
                return (CollectionConfig)this["SQLite"];
            }
        }
        #endregion

        #region PostgreSql 节点
        /// <summary>
        /// PostgreSql 节点
        /// </summary>
        [ConfigurationProperty("PostgreSql", IsRequired = false)]
        [ConfigurationCollection(typeof(CollectionConfig), AddItemName = "Add")]
        public CollectionConfig PostgreSql
        {
            get
            {
                return (CollectionConfig)this["PostgreSql"];
            }
        }
        #endregion

        #region Redis 节点
        /// <summary>
        /// Redis 配置
        /// </summary>
        [ConfigurationProperty("Redis", IsRequired = false)]
        public RedisElement Redis
        {
            get
            {
                return (RedisElement)this["Redis"];
            }
        }
        #endregion

        #region ConnectionPool 节点
        /// <summary>
        /// 连接池配置（内部存储为字典，由 ParseXmlConfig 填充）
        /// </summary>
        private Dictionary<string, int> _connectionPoolSettings = new Dictionary<string, int>();
        private Dictionary<string, string> _connectionPoolStringSettings = new Dictionary<string, string>();
        private bool _usesUnifiedConnections;

        /// <summary>
        /// 获取连接池配置
        /// </summary>
        public ConnectionPoolConfig GetConnectionPoolConfig()
        {
            var poolConfig = new ConnectionPoolConfig();
            if (_connectionPoolSettings.TryGetValue("MinPoolSize", out var min)) poolConfig.MinPoolSize = min;
            if (_connectionPoolSettings.TryGetValue("MaxPoolSize", out var max)) poolConfig.MaxPoolSize = max;
            if (_connectionPoolSettings.TryGetValue("ConnectionTimeout", out var ct)) poolConfig.ConnectionTimeout = ct;
            if (_connectionPoolSettings.TryGetValue("ConnectionLifetime", out var cl)) poolConfig.ConnectionLifetime = cl;
            if (_connectionPoolSettings.TryGetValue("HealthCheckInterval", out var hci)) poolConfig.HealthCheckInterval = hci;
            if (_connectionPoolSettings.TryGetValue("LeakDetectionThreshold", out var ldt)) poolConfig.LeakDetectionThreshold = ldt;
            if (_connectionPoolSettings.TryGetValue("LoadThreshold", out var lt)) poolConfig.LoadThreshold = lt;
            if (_connectionPoolSettings.TryGetValue("ShrinkThreshold", out var st)) poolConfig.ShrinkThreshold = st;
            if (_connectionPoolSettings.TryGetValue("MaxRetries", out var mr)) poolConfig.MaxRetries = mr;
            if (_connectionPoolSettings.TryGetValue("RetryBaseDelayMs", out var rbd)) poolConfig.RetryBaseDelayMs = rbd;
            if (_connectionPoolSettings.TryGetValue("ValidationCommandTimeout", out var vct)) poolConfig.ValidationCommandTimeout = vct;
            if (_connectionPoolSettings.TryGetValue("SmartAdjustmentInterval", out var sai)) poolConfig.SmartAdjustmentInterval = sai;
            if (_connectionPoolSettings.TryGetValue("MaxExpandCount", out var mec)) poolConfig.MaxExpandCount = mec;
            if (_connectionPoolSettings.TryGetValue("MaxShrinkCount", out var msc)) poolConfig.MaxShrinkCount = msc;
            if (_connectionPoolSettings.TryGetValue("EnableSmartAdjustment", out var esa)) poolConfig.EnableSmartAdjustment = esa == 1;
            if (_connectionPoolSettings.TryGetValue("ErrorShrinkThreshold", out var est)) poolConfig.ErrorShrinkThreshold = est;
            if (_connectionPoolSettings.TryGetValue("ErrorShrinkPercentage", out var esp)) poolConfig.ErrorShrinkPercentage = esp;
            if (_connectionPoolSettings.TryGetValue("EnableRedisCheck", out var erc)) poolConfig.EnableRedisCheck = erc == 1;
            if (_connectionPoolStringSettings.TryGetValue("RedisConnectionString", out var rcs)) poolConfig.RedisConnectionString = rcs;
            
            // 熔断器配置
            var cbConfig = new CircuitBreakerConfig();
            if (_connectionPoolSettings.TryGetValue("CircuitBreakerFailureThreshold", out var ft)) cbConfig.FailureThreshold = ft;
            if (_connectionPoolSettings.TryGetValue("CircuitBreakerOpenDurationSec", out var ods)) cbConfig.CircuitOpenDurationSec = ods;
            if (_connectionPoolSettings.TryGetValue("CircuitBreakerHalfOpenMaxRequests", out var homr)) cbConfig.HalfOpenMaxRequests = homr;
            if (_connectionPoolSettings.TryGetValue("CircuitBreakerEnabled", out var cben)) cbConfig.Enabled = cben == 1;
            poolConfig.CircuitBreaker = cbConfig;
            
            return poolConfig;
        }
        #endregion

        #region 获取配置节点
        /// <summary>
        /// 构造配置缓存键，避免不同项目、配置文件或环境下共享同一份缓存
        /// </summary>
        private static string BuildCacheKey(string prefix, string projectName, string dbFile)
        {
            var key = string.Format("{0}:{1}", prefix, string.IsNullOrEmpty(dbFile) ? "db.config" : dbFile);

            if (!string.IsNullOrEmpty(projectName))
                key = string.Format("{0}:{1}", key, projectName);

            var active = Environment.GetEnvironmentVariable("FASTDATA_ACTIVE");
            if (!string.IsNullOrEmpty(active))
                key = string.Format("{0}:{1}", key, active);

            return key;
        }

        /// <summary>
        /// 获取配置节点
        /// </summary>
        /// <param name="key">配置键</param>
        /// <param name="projectName">项目名称</param>
        /// <param name="dbFile">配置文件名</param>
        /// <returns>配置模型</returns>
        public static ConfigModel GetConfig(string key = null, string projectName = null, string dbFile = "db.config")
        {
            // 自动扫描并注册数据库提供程序（.NET Core/.NET 5+ 需要）
            Infrastructure.DbProviderAutoRegistrar.Register();
            
            var cacheKey = BuildCacheKey("FastData.db.config", projectName, dbFile);
            var result = new ConfigModel();
            var list = new List<ConfigModel>();
            var config = new DataConfig();
            var defaultKey = string.Empty;

            if (DbCache.Exists(CacheType.Web, cacheKey))
            {
                var cachedConfig = DbCache.Get<List<ConfigModel>>(CacheType.Web, cacheKey);
                if (cachedConfig != null)
                    list = new List<ConfigModel>(cachedConfig);
            }

            if (list.Count == 0 && projectName == null)
            {
                // Load db.config to get Active value
                var baseConfig = TryLoadConfig(dbFile);
                var activeEnv = ResolveActiveEnvironment(baseConfig);

                // Try to load environment-specific config (e.g., db.dev.config, db.pro.config)
                if (!string.IsNullOrEmpty(activeEnv))
                {
                    var envDbFile = string.Format("db.{0}.config", activeEnv);
                    config = TryLoadConfig(envDbFile);
                }

                // Fall back to base db.config (if it has connections directly)
                if (config == null || config.Connections == null || config.Connections.Count == 0)
                    config = baseConfig;

                // If file-based approach failed, try embedded resource from calling assemblies
                if (config == null || (config.Connections == null || config.Connections.Count == 0))
                {
                    var assemblies = new[] {
                        Assembly.GetCallingAssembly(),
                        Assembly.GetEntryAssembly(),
                        Assembly.GetExecutingAssembly()
                    };
                    
                    foreach (var assembly in assemblies)
                    {
                        if (assembly == null) continue;
                        var name = assembly.GetName().Name;
                        
                        // Try environment-specific embedded resource first
                        if (!string.IsNullOrEmpty(activeEnv))
                        {
                            var envDbFile = string.Format("db.{0}.config", activeEnv);
                            var tempConfig = LoadFromEmbeddedResource(name, envDbFile);
                            if (tempConfig != null && tempConfig.Connections != null && tempConfig.Connections.Count != 0)
                            {
                                config = tempConfig;
                                projectName = name;
                                break;
                            }
                        }
                        
                        // Fall back to base embedded resource
                        var baseTempConfig = LoadFromEmbeddedResource(name, dbFile);
                        if (baseTempConfig != null && baseTempConfig.Connections != null && baseTempConfig.Connections.Count != 0)
                        {
                            config = baseTempConfig;
                            projectName = name;
                            break;
                        }
                    }
                }

                if (config == null)
                    config = new DataConfig();

                defaultKey = config.Default;

                if (config.Connections != null && config.Connections.Count != 0)
                {
                    foreach (var temp in config.Connections)
                    {
                        var element = temp as ElementConfig;
                        var dbType = ParseProviderToDbType(element.Provider);
                        if (dbType.HasValue)
                        {
                            var item = CreateConfigModel(element, dbType.Value);
                            if (item != null)
                            {
                                if (string.IsNullOrEmpty(item.Key) && element.IsDefault)
                                    item.Key = config.Default;
                                if (element.IsDefault)
                                    defaultKey = item.Key;
                                list.Add(item);
                            }
                        }
                    }
                }

                if (!config._usesUnifiedConnections)
                {
                    #region Db2
                    if (config.DB2 != null && config.DB2.Count != 0)
                    {
                        foreach (var temp in config.DB2)
                        {
                            var item = CreateConfigModel(temp as ElementConfig, DataDbType.DB2);
                            if (item != null)
                                list.Add(item);
                        }
                    }
                    #endregion

                    #region oracle
                    if (config.Oracle != null && config.Oracle.Count != 0)
                    {
                        foreach (var temp in config.Oracle)
                        {
                            var item = CreateConfigModel(temp as ElementConfig, DataDbType.Oracle);
                            if (item != null)
                                list.Add(item);
                        }
                    }
                    #endregion

                    #region mysql
                    if (config.MySql != null && config.MySql.Count != 0)
                    {
                        foreach (var temp in config.MySql)
                        {
                            var item = CreateConfigModel(temp as ElementConfig, DataDbType.MySql);
                            if (item != null)
                                list.Add(item);
                        }
                    }
                    #endregion

                    #region sqlserver
                    if (config.SqlServer != null && config.SqlServer.Count != 0)
                    {
                        foreach (var temp in config.SqlServer)
                        {
                            var item = CreateConfigModel(temp as ElementConfig, DataDbType.SqlServer);
                            if (item != null)
                                list.Add(item);
                        }
                    }
                    #endregion

                    #region sqlite
                    if (config.SQLite != null && config.SQLite.Count != 0)
                    {
                        foreach (var temp in config.SQLite)
                        {
                            var item = CreateConfigModel(temp as ElementConfig, DataDbType.SQLite);
                            if (item != null)
                                list.Add(item);
                        }
                    }
                    #endregion

                    #region PostgreSql
                    if (config.PostgreSql != null && config.PostgreSql.Count != 0)
                    {
                        foreach (var temp in config.PostgreSql)
                        {
                            var item = CreateConfigModel(temp as ElementConfig, DataDbType.PostgreSql);
                            if (item != null)
                                list.Add(item);
                        }
                    }
                    #endregion
                }
            }
            else if (list.Count == 0)
            {
                // Use file-based approach (same as projectName == null case)
                // This handles environment resolution correctly
                var baseConfig = TryLoadConfig(dbFile);
                var activeEnv = ResolveActiveEnvironment(baseConfig);

                // Try to load environment-specific config (e.g., db.dev.config)
                if (!string.IsNullOrEmpty(activeEnv))
                {
                    var envDbFile = string.Format("db.{0}.config", activeEnv);
                    config = TryLoadConfig(envDbFile);
                }

                // Fall back to base db.config (if it has connections directly)
                if (config == null || config.Connections == null || config.Connections.Count == 0)
                    config = baseConfig;

                defaultKey = config.Default;

                if (config.Connections != null && config.Connections.Count != 0)
                {
                    foreach (var temp in config.Connections)
                    {
                        var element = temp as ElementConfig;
                        var dbType = ParseProviderToDbType(element.Provider);
                        if (dbType.HasValue)
                        {
                            var item = CreateConfigModel(element, dbType.Value);
                            if (item != null)
                            {
                                if (string.IsNullOrEmpty(item.Key) && element.IsDefault)
                                    item.Key = defaultKey;
                                if (element.IsDefault)
                                    defaultKey = item.Key;
                                list.Add(item);
                            }
                        }
                    }
                }
            }

            var scopeKey = FastDb.CurrentKey;
            if (string.IsNullOrEmpty(key) && !string.IsNullOrEmpty(scopeKey))
                key = scopeKey;

            // Apply environment variable overrides
            ApplyEnvironmentOverrides(list);
            EnsureUniqueKeys(list);

            list = SetDefaultFirst(new List<ConfigModel>(list), defaultKey);
            DbCache.Set<List<ConfigModel>>(CacheType.Web, cacheKey, list);

            if (string.IsNullOrEmpty(key))
                result = GetDefaultConfig(list, defaultKey);
            else
                result = list.Find(a => string.Equals(a.Key, key, StringComparison.OrdinalIgnoreCase));

            if (result == null)
            {
                var availableKeys = string.Join(", ", list.Select(a => a.Key));
                var configPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "db.config");
                var errorMessage = string.Format(
                    "数据库配置 Key 不存在：{0}（注意：Key 匹配已忽略大小写）\n可用 Key: {1}\n配置文件路径：{2}\n\n解决方案：\n1. 检查 db.config 文件中是否定义了该 Key\n2. 确认 Key 名称拼写正确\n3. 确保配置文件已发布到输出目录",
                    key, availableKeys, configPath);
                throw new Exception(errorMessage);
            }

            return result;
        }
        #endregion

        private static List<ConfigModel> SetDefaultFirst(List<ConfigModel> list, string defaultKey)
        {
            if (string.IsNullOrEmpty(defaultKey))
                return list;

            var item = list.Find(a => a.Key == defaultKey);
            if (item == null || list.IndexOf(item) == 0)
                return list;

            list.Remove(item);
            list.Insert(0, item);
            return list;
        }

        private static ConfigModel GetDefaultConfig(List<ConfigModel> list, string defaultKey)
        {
            if (!string.IsNullOrEmpty(defaultKey))
            {
var item = list.Find(a => string.Equals(a.Key, defaultKey, StringComparison.OrdinalIgnoreCase));
                if (item != null)
                    return item;
            }

            if (list.Count == 0)
                return null;

            return list.First();
        }

        private static void EnsureUniqueKeys(List<ConfigModel> list)
        {
            var duplicate = list
                .Where(a => !string.IsNullOrEmpty(a.Key))
                .GroupBy(a => a.Key, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault(a => a.Count() > 1);

            if (duplicate != null)
                throw new ConfigurationErrorsException(string.Format("Duplicate FastData connection key '{0}'. Connection keys must be unique.", duplicate.Key));
        }

        private static ConfigModel CreateConfigModel(ElementConfig element, DataDbType dbType)
        {
            var item = CreateProviderConfig(dbType, element.Provider);
            if (item == null)
                return null;

            item.ConnStr = element.ConnStr;
            item.IsOutError = element.IsOutError;
            item.IsOutSql = element.IsOutSql;
            item.IsPropertyCache = element.IsPropertyCache;
            item.Key = element.Key;
            item.DbLinkName = element.DbLinkName;
            item.DesignModel = element.DesignModel;
            item.IsEncrypt = element.IsEncrypt;
            item.IsMapSave = element.IsMapSave;
            item.SqlErrorType = element.SqlErrorType;
            item.CacheType = element.CacheType;
            item.IsUpdateCache = element.IsUpdateCache;
            item.CommandTimeout = element.CommandTimeout;
            return item;
        }

        private static ConfigModel CreateConfigModel(XmlNode db, DataDbType dbType)
        {
            var item = CreateProviderConfig(dbType, GetAttr(db, "ProviderName"));
            if (item == null)
                return null;

            item.ConnStr = GetAttr(db, "ConnStr");
            item.IsOutError = GetBool(db, "IsOutError", true);
            item.IsOutSql = GetBool(db, "IsOutSql", true);
            item.IsPropertyCache = GetBool(db, "IsPropertyCache", true);
            item.Key = GetAttr(db, "Key");
            item.DbLinkName = GetAttr(db, "DbLinkName");
            item.DesignModel = GetAttr(db, "DesignModel");
            item.IsEncrypt = GetBool(db, "IsEncrypt", false);
            item.IsMapSave = GetBool(db, "IsMapSave", false);
            item.SqlErrorType = GetAttr(db, "SqlErrorType");
            item.CacheType = GetAttr(db, "CacheType");
            item.IsUpdateCache = GetBool(db, "IsUpdateCache", false);
            item.CommandTimeout = GetInt(db, "CommandTimeout", 30);
            item.DesignModel = item.DesignModel == null ? "DbFirst" : item.DesignModel;
            item.CacheType = item.CacheType == null ? "web" : item.CacheType;
            item.SqlErrorType = item.SqlErrorType == null ? "db" : item.SqlErrorType;
            return item;
        }

        private static string GetAttr(XmlNode node, string name)
        {
            var attr = node.Attributes == null ? null : node.Attributes[name];
            return attr == null ? null : attr.Value;
        }

        private static bool GetBool(XmlNode node, string name, bool defaultValue)
        {
            var value = GetAttr(node, name);
            if (value == null)
                return defaultValue;

            return string.Equals(value.ToStr(), "true", StringComparison.OrdinalIgnoreCase);
        }

        private static int GetInt(XmlNode node, string name, int defaultValue)
        {
            var value = GetAttr(node, name);
            if (value == null)
                return defaultValue;

            return int.TryParse(value, out var result) ? result : defaultValue;
        }

        private static bool IsTrue(string value)
        {
            return string.Equals(value.ToStr(), "true", StringComparison.OrdinalIgnoreCase);
        }

        private static ConfigModel CreateProviderConfig(DataDbType dbType, string providerName = null)
        {
            var item = new ConfigModel();
            item.DbType = dbType;

            switch (dbType)
            {
                case DataDbType.DB2:
                    item.Flag = "@";
                    item.ProviderName = Provider.DB2;
                    break;
                case DataDbType.Oracle:
                    item.Flag = ":";
                    item.ProviderName = Provider.Oracle;
                    break;
                case DataDbType.MySql:
                    item.Flag = "?";
                    item.ProviderName = Provider.MySql;
                    break;
                case DataDbType.SqlServer:
                    item.Flag = "@";
                    item.ProviderName = providerName ?? Provider.SqlServer;
                    break;
                case DataDbType.SQLite:
                    item.Flag = "@";
                    item.ProviderName = providerName ?? Provider.SQLite;
                    break;
                case DataDbType.PostgreSql:
                    item.Flag = ":";
                    item.ProviderName = Provider.PostgreSql;
                    break;
                default:
                    return null;
            }

            return item;
        }

        private static DataDbType? ParseProviderToDbType(string provider)
        {
            if (string.IsNullOrEmpty(provider))
                return null;

            if (provider == Provider.DB2)
                return DataDbType.DB2;
            if (provider == Provider.Oracle)
                return DataDbType.Oracle;
            if (provider == Provider.MySql)
                return DataDbType.MySql;
            if (provider == Provider.SqlServer || provider == "Microsoft.Data.SqlClient")
                return DataDbType.SqlServer;
            if (provider == Provider.SQLite || provider == "Microsoft.Data.Sqlite")
                return DataDbType.SQLite;
            if (provider == Provider.PostgreSql)
                return DataDbType.PostgreSql;

            return null;
        }

        /// <summary>
        /// Try to load config from file
        /// </summary>
        /// <summary>
        /// 尝试加载数据库配置文件
        /// </summary>
        private static DataConfig TryLoadConfig(string dbFile)
        {
            try
            {
                if (string.Equals(dbFile, "web.config", StringComparison.OrdinalIgnoreCase))
                    return (DataConfig)ConfigurationManager.GetSection("DataConfig");

                // 必须用 Path.Combine，不能字符串拼接：
                // AppDomain.BaseDirectory 是否带结尾分隔符依环境而异，
                // 拼接会得到 "...\net452db.config" 这种错误路径，
                // File.Exists 返回 false 后静默 return null —— 表现为「配置里一个 Key 都没有」。
                // net452 + xunit.console 的 BaseDirectory 不带结尾反斜杠，正是如此（issue #18）。
                var configPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, dbFile);
                if (!File.Exists(configPath))
                    return null;

                var content = File.ReadAllText(configPath);
                return ParseXmlConfig(content);
            }
            catch (Exception ex)
            {
                // 原来这里是裸 catch { return null; }：配置文件读到了、路径也对，
                // 但解析中途抛的异常被完全吞掉，调用方只能看到「可用 Key 为空」，
                // 无法区分「文件不存在」「XML 格式错」「配置节机制不兼容」。
                // issue #18 就是被这行藏住的（net452 下表现与 net8 不同）。
                // 这里只加可观测性，不改变返回值语义：失败仍返回 null。
                System.Diagnostics.Trace.TraceWarning(string.Format(
                    "DataConfig.TryLoadConfig 加载 {0} 失败：{1}: {2}",
                    dbFile, ex.GetType().Name, ex.Message));
                return null;
            }
        }

        /// <summary>
        /// Apply environment variable overrides to config list
        /// Environment variable format: FASTDATA_CONN_{KEY} (e.g., FASTDATA_CONN_SQLSERVER)
        /// </summary>
        private static void ApplyEnvironmentOverrides(List<ConfigModel> list)
        {
            foreach (var item in list)
            {
                if (string.IsNullOrEmpty(item.Key)) continue;
                
                var envVarName = string.Format("FASTDATA_CONN_{0}", item.Key.ToUpper());
                var envConnStr = Environment.GetEnvironmentVariable(envVarName);
                if (!string.IsNullOrEmpty(envConnStr))
                {
                    item.ConnStr = envConnStr;
                }
            }
        }

        /// <summary>
        /// Resolve active environment from config or environment variable
        /// Priority: FASTDATA_ACTIVE env var > db.config Active attribute > default "dev"
        /// </summary>
        private static string ResolveActiveEnvironment(DataConfig baseConfig)
        {
            // 1. Check environment variable first
            var envActive = Environment.GetEnvironmentVariable("FASTDATA_ACTIVE");
            if (!string.IsNullOrEmpty(envActive))
                return NormalizeEnvironment(envActive);

            // 2. Check db.config Active attribute
            if (baseConfig != null && !string.IsNullOrEmpty(baseConfig.Active))
                return NormalizeEnvironment(baseConfig.Active);

            // 3. Default to "dev"
            return "dev";
        }

        /// <summary>
        /// Normalize environment name aliases
        /// dev/development/Development → dev
        /// pro/production/Production → pro
        /// staging/Staging → staging
        /// Others: lowercase
        /// </summary>
        private static string NormalizeEnvironment(string env)
        {
            if (string.IsNullOrEmpty(env)) return "dev";
            
            var lower = env.ToLower();
            switch (lower)
            {
                case "dev":
                case "development":
                    return "dev";
                case "pro":
                case "production":
                    return "pro";
                case "staging":
                    return "staging";
                default:
                    return lower;
            }
        }

        /// <summary>
        /// Get Redis config from current active environment config
        /// </summary>
        /// <param name="dbFile">配置文件名</param>
        /// <returns>Redis config, or null if not configured</returns>
        public static RedisConfig GetRedisConfig(string dbFile = "db.config")
        {
            var cacheKey = BuildCacheKey("FastData.redis.config", null, dbFile);
            
            if (DbCache.Exists(CacheType.Web, cacheKey))
                return DbCache.Get<RedisConfig>(CacheType.Web, cacheKey);

            DataConfig config = null;
            
            // Load base config to get Active value
            var baseConfig = TryLoadConfig(dbFile);
            var activeEnv = ResolveActiveEnvironment(baseConfig);

            // Try environment-specific config
            if (!string.IsNullOrEmpty(activeEnv))
            {
                var envDbFile = string.Format("db.{0}.config", activeEnv);
                config = TryLoadConfig(envDbFile);
            }

            // Fall back to base config
            if (config == null)
                config = baseConfig;

            // Extract Redis config
            var redisConfig = new RedisConfig();
            if (config?.Redis != null)
            {
                redisConfig.Server = config.Redis.Server;
                redisConfig.Db = config.Redis.Db;
                redisConfig.Password = config.Redis.Password;
                redisConfig.ConnectTimeout = config.Redis.ConnectTimeout;
                redisConfig.SyncTimeout = config.Redis.SyncTimeout;
            }

            // Check environment variable overrides
            var envServer = Environment.GetEnvironmentVariable("FASTDATA_REDIS_SERVER");
            if (!string.IsNullOrEmpty(envServer))
                redisConfig.Server = envServer;

            var envDb = Environment.GetEnvironmentVariable("FASTDATA_REDIS_DB");
            if (!string.IsNullOrEmpty(envDb) && int.TryParse(envDb, out var dbIndex))
                redisConfig.Db = dbIndex;

            var envPassword = Environment.GetEnvironmentVariable("FASTDATA_REDIS_PASSWORD");
            if (!string.IsNullOrEmpty(envPassword))
                redisConfig.Password = envPassword;

            DbCache.Set<RedisConfig>(CacheType.Web, cacheKey, redisConfig);
            return redisConfig;
        }

        public static bool DataType(string key = null, string projectName = null, string dbFile = "db.config")
        {
            var cacheKey = BuildCacheKey("FastData.db.config", projectName, dbFile);

            if (!DbCache.Exists(CacheType.Web, cacheKey))
                DataConfig.GetConfig(key, projectName, dbFile);

            var list = DbCache.Get<List<ConfigModel>>(CacheType.Web, cacheKey);
            if (list == null || list.Count == 0)
                return false;

            return list.Select(a => a.DbType).Distinct().Take(2).Count() > 1;
        }

        /// <summary>
        /// 从嵌入式资源加载配置
        /// </summary>
        /// <param name="projectName">项目名称</param>
        /// <param name="dbFile">配置文件名</param>
        /// <returns>数据配置</returns>
        private static DataConfig LoadFromEmbeddedResource(string projectName, string dbFile)
        {
            try
            {
                Assembly assembly = null;
                
                // Try to find assembly from current AppDomain first
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (asm.GetName().Name == projectName && !asm.IsDynamic)
                    {
                        assembly = asm;
                        break;
                    }
                }
                
                // If not found in AppDomain, try Assembly.Load
                if (assembly == null)
                {
                    try
                    {
                        assembly = Assembly.Load(projectName);
                    }
                    catch
                    {
                        // Assembly not found in default probing paths
                        // Try to load from file system
                        var basePath = AppDomain.CurrentDomain.BaseDirectory;
                        var possiblePaths = new[]
                        {
                            Path.Combine(basePath, projectName + ".dll"),
                            Path.Combine(basePath, "bin", projectName + ".dll"),
                            Path.Combine(basePath, "publish", projectName + ".dll")
                        };
                        
                        foreach (var path in possiblePaths)
                        {
                            if (File.Exists(path))
                            {
                                try
                                {
                                    assembly = Assembly.LoadFrom(path);
                                    break;
                                }
                                catch
                                {
                                    // Failed to load from this path
                                }
                            }
                        }
                        
                        if (assembly == null)
                            return null;
                    }
                }
                
                var resourceName = string.Format("{0}.{1}", projectName, dbFile);
                using (var resource = assembly.GetManifestResourceStream(resourceName))
                {
                    if (resource != null)
                    {
                        using (var reader = new StreamReader(resource))
                        {
                            var content = reader.ReadToEnd();
                            return ParseXmlConfig(content);
                        }
                    }
                }
            }
            catch
            {
                // Failed to load from embedded resource
            }
            return null;
        }

        /// <summary>
        /// Parse XML config content into DataConfig object
        /// </summary>
        private static DataConfig ParseXmlConfig(string xmlContent)
        {
            try
            {
                var doc = new System.Xml.XmlDocument();
                doc.LoadXml(xmlContent);

                var dataConfigNode = doc.SelectSingleNode("//DataConfig");
                if (dataConfigNode == null)
                    return null;

                var config = new DataConfig();

                // Read Active attribute
                var activeAttr = dataConfigNode.Attributes?["Active"];
                if (activeAttr != null)
                    config["Active"] = activeAttr.Value;

                // Read Default attribute
                var defaultAttr = dataConfigNode.Attributes?["Default"];
                if (defaultAttr != null)
                    config["Default"] = defaultAttr.Value;

                // Read Connections
                var connectionsNode = dataConfigNode.SelectSingleNode("Connections");
                if (connectionsNode != null)
                {
                    var collection = new CollectionConfig();
                    foreach (System.Xml.XmlNode addNode in connectionsNode.SelectNodes("Add"))
                    {
                        var element = new ElementConfig();
                        element.Key = addNode.Attributes?["Key"]?.Value ?? "";
                        element.Provider = addNode.Attributes?["Provider"]?.Value ?? "";
                        element.ConnStr = addNode.Attributes?["ConnStr"]?.Value ?? "";
                        element.IsDefault = addNode.Attributes?["IsDefault"]?.Value?.ToLower() == "true";
                        element.IsOutSql = GetBool(addNode, "IsOutSql", true);
                        element.IsOutError = GetBool(addNode, "IsOutError", true);
                        element.IsPropertyCache = addNode.Attributes?["IsPropertyCache"]?.Value?.ToLower() != "false";
                        element.SqlErrorType = addNode.Attributes?["SqlErrorType"]?.Value ?? "db";
                        element.CacheType = addNode.Attributes?["CacheType"]?.Value ?? "web";
                        element.DesignModel = addNode.Attributes?["DesignModel"]?.Value ?? "DbFirst";
                        element.DbLinkName = addNode.Attributes?["DbLinkName"]?.Value ?? "";
                        element.IsMapSave = addNode.Attributes?["IsMapSave"]?.Value?.ToLower() == "true";
                        element.IsEncrypt = addNode.Attributes?["IsEncrypt"]?.Value?.ToLower() == "true";
                        element.IsUpdateCache = addNode.Attributes?["IsUpdateCache"]?.Value?.ToLower() == "true";
                        collection.AddElement(element);
                    }
                    config["Connections"] = collection;
                    config._usesUnifiedConnections = true;
                }

                // Read Redis
                var redisNode = dataConfigNode.SelectSingleNode("Redis");
                if (redisNode != null)
                {
                    var redisElement = new RedisElement();
                    redisElement.Server = redisNode.Attributes?["Server"]?.Value ?? "";
                    redisElement.Db = int.TryParse(redisNode.Attributes?["Db"]?.Value, out var db) ? db : 0;
                    redisElement.Password = redisNode.Attributes?["Password"]?.Value ?? "";
                    redisElement.ConnectTimeout = int.TryParse(redisNode.Attributes?["ConnectTimeout"]?.Value, out var ct) ? ct : 5000;
                    redisElement.SyncTimeout = int.TryParse(redisNode.Attributes?["SyncTimeout"]?.Value, out var st) ? st : 5000;
                    config["Redis"] = redisElement;
                }

                // Read ConnectionPool
                var poolNode = dataConfigNode.SelectSingleNode("ConnectionPool");
                if (poolNode != null)
                {
                    var poolSettings = new Dictionary<string, int>();
                    var boolSettings = new Dictionary<string, bool>();
                    var stringSettings = new Dictionary<string, string>();

                    foreach (System.Xml.XmlAttribute attr in poolNode.Attributes)
                    {
                        var name = attr.Name;
                        if (name == "EnableSmartAdjustment" || name == "EnableRedisCheck")
                            boolSettings[name] = attr.Value.ToLower() == "true";
                        else if (name == "RedisConnectionString")
                            stringSettings[name] = attr.Value;
                        else if (int.TryParse(attr.Value, out var val))
                            poolSettings[name] = val;
                    }

                    // 熔断器配置（CircuitBreaker.* 开头的属性）
                    foreach (System.Xml.XmlAttribute attr in poolNode.Attributes)
                    {
                        var name = attr.Name;
                        if (name.StartsWith("CircuitBreaker"))
                        {
                            if (name == "CircuitBreakerEnabled")
                                boolSettings[name] = attr.Value.ToLower() == "true";
                            else if (int.TryParse(attr.Value, out var val))
                                poolSettings[name] = val;
                        }
                    }

                    config._connectionPoolSettings = poolSettings;
                    config._connectionPoolStringSettings = stringSettings;
                    if (boolSettings.TryGetValue("EnableSmartAdjustment", out var enableSmart))
                        poolSettings["EnableSmartAdjustment"] = enableSmart ? 1 : 0;
                    if (boolSettings.TryGetValue("EnableRedisCheck", out var enableRedisCheck))
                        poolSettings["EnableRedisCheck"] = enableRedisCheck ? 1 : 0;
                    if (boolSettings.TryGetValue("CircuitBreakerEnabled", out var cbEnabled))
                        poolSettings["CircuitBreakerEnabled"] = cbEnabled ? 1 : 0;
                }

                return config;
            }
            catch (Exception ex)
            {
                // 同 TryLoadConfig：不改变返回值语义（仍返回 null），但必须让失败可见。
                // 这一层更容易出问题——它内部的 config["Active"] 等语句依赖
                // ConfigurationSection 的索引器，该写法在不同目标框架下行为不一致（issue #18）。
                System.Diagnostics.Trace.TraceWarning(string.Format(
                    "DataConfig.ParseXmlConfig 解析失败：{0}: {1}",
                    ex.GetType().Name, ex.Message));
                return null;
            }
        }

        #region 公共 API（供外部项目调用）
        /// <summary>
        /// 获取当前活跃环境名称
        /// </summary>
        public static string GetActiveEnvironment()
        {
            var baseConfig = TryLoadConfig("db.config");
            return ResolveActiveEnvironment(baseConfig);
        }

        /// <summary>
        /// 获取 Redis 配置（完整对象，供内部使用）
        /// </summary>
        public static RedisConfig GetRedisConfigPublic()
        {
            return GetRedisConfig();
        }

        /// <summary>
        /// 获取数据库连接列表（密码脱敏）
        /// </summary>
        public static List<Dictionary<string, string>> GetConnectionSummaries()
        {
            var result = new List<Dictionary<string, string>>();
            var cacheKey = BuildCacheKey("FastData.db.config", null, "db.config");
            var cacheType = "web";
            
            // Trigger config loading if cache is empty
            if (!Base.DbCache.Exists(cacheType, cacheKey))
            {
                GetConfig();
            }
            
            if (!Base.DbCache.Exists(cacheType, cacheKey))
                return result;

            var list = Base.DbCache.Get<List<ConfigModel>>(cacheType, cacheKey);
            foreach (var c in list)
            {
                result.Add(new Dictionary<string, string>
                {
                    { "key", c.Key ?? "" },
                    { "dbType", c.DbType.ToString() },
                    { "provider", c.ProviderName ?? "" },
                    { "connStr", MaskConnStr(c.ConnStr) },
                    { "isOutSql", c.IsOutSql.ToString() },
                    { "isOutError", c.IsOutError.ToString() }
                });
            }
            return result;
        }

        /// <summary>
        /// 获取 Redis 配置摘要（密码脱敏）
        /// </summary>
        public static Dictionary<string, string> GetRedisSummary()
        {
            var redis = GetRedisConfig();
            if (redis == null)
                return new Dictionary<string, string> { { "configured", "false" } };

            return new Dictionary<string, string>
            {
                { "configured", "true" },
                { "server", redis.Server ?? "" },
                { "db", redis.Db.ToString() },
                { "password", string.IsNullOrEmpty(redis.Password) ? "" : "***" },
                { "connectTimeout", redis.ConnectTimeout.ToString() },
                { "syncTimeout", redis.SyncTimeout.ToString() }
            };
        }

        internal static string MaskConnStr(string connStr)
        {
            if (string.IsNullOrEmpty(connStr)) return "";
            // 只遮蔽 pwd/password 会漏掉 uid、user id、access key、token 等凭据字段，
            // 脱敏结果被贴进 issue/日志时仍然泄漏账号与密钥。
            return System.Text.RegularExpressions.Regex.Replace(
                connStr,
                @"(?i)(pwd|password|uid|user\s*id|userid|username|access\s*key|accesskey|accountkey|sharedaccesskey|token|secret|apikey|api_key)=[^;]*",
                "$1=***");
        }

        /// <summary>
        /// 获取连接池配置（从配置文件读取，无配置则返回 null）
        /// </summary>
        /// <param name="dbFile">配置文件名</param>
        /// <returns>连接池配置</returns>
        public static ConnectionPoolConfig GetConnectionPoolConfigPublic(string dbFile = "db.config")
        {
            var baseConfig = TryLoadConfig(dbFile);
            var activeEnv = ResolveActiveEnvironment(baseConfig);

            DataConfig config = null;
            if (!string.IsNullOrEmpty(activeEnv))
            {
                var envDbFile = string.Format("db.{0}.config", activeEnv);
                config = TryLoadConfig(envDbFile);
            }

            if (config == null)
                config = baseConfig;

            if (config == null || config._connectionPoolSettings.Count == 0)
                return null;

            return config.GetConnectionPoolConfig();
        }

        /// <summary>
        /// 获取连接池配置摘要
        /// </summary>
        public static Dictionary<string, string> GetConnectionPoolSummary()
        {
            var poolConfig = GetConnectionPoolConfigPublic();
            if (poolConfig == null)
                return new Dictionary<string, string> { { "configured", "false" } };

            return new Dictionary<string, string>
            {
                { "configured", "true" },
                { "minPoolSize", poolConfig.MinPoolSize.ToString() },
                { "maxPoolSize", poolConfig.MaxPoolSize.ToString() },
                { "connectionTimeout", poolConfig.ConnectionTimeout.ToString() },
                { "connectionLifetime", poolConfig.ConnectionLifetime.ToString() },
                { "healthCheckInterval", poolConfig.HealthCheckInterval.ToString() },
                { "leakDetectionThreshold", poolConfig.LeakDetectionThreshold.ToString() },
                { "loadThreshold", poolConfig.LoadThreshold.ToString() },
                { "shrinkThreshold", poolConfig.ShrinkThreshold.ToString() },
                { "maxRetries", poolConfig.MaxRetries.ToString() },
                { "retryBaseDelayMs", poolConfig.RetryBaseDelayMs.ToString() },
                { "validationCommandTimeout", poolConfig.ValidationCommandTimeout.ToString() },
                { "smartAdjustmentInterval", poolConfig.SmartAdjustmentInterval.ToString() },
                { "maxExpandCount", poolConfig.MaxExpandCount.ToString() },
                { "maxShrinkCount", poolConfig.MaxShrinkCount.ToString() },
                { "enableSmartAdjustment", poolConfig.EnableSmartAdjustment.ToString() }
            };
        }
        #endregion
    }
}
