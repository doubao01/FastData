using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Data.Common;
using FastUntility.Page;
using FastData.Base;
using FastData.Config;
using FastData.DbTypes;
using FastData.Model;
using FastData.Repository;
using System.Diagnostics;
using System.IO;
using FastUntility.Base;
using FastData.CacheModel;
using FastData.Check;
using System.Reflection;
using FastData.Context;
using System.Xml;
using FastData.Aop;

namespace FastData
{
    /// <summary>
    /// FastData XML 映射操作（静态方法）
    /// 
    /// 职责：
    /// 1. XML 映射文件加载（InstanceMap / InstanceMapResource）
    /// 2. 实体属性映射（InstanceProperties）
    /// 3. 表结构映射（InstanceTable）
    /// 4. XML 映射查询（Query / QueryAsync / QueryPage）
    /// 5. XML 映射写入（Write / WriteAsync）
    /// 6. AOP 切面支持（fastAop）
    /// </summary>
    public static class FastMap
    {
        private static volatile IFastAop _fastAop;

        /// <summary>
        /// AOP 切面接口
        /// 用于在 Map 查询和写入操作的前后插入自定义逻辑（如日志、权限校验、缓存处理等）
        /// </summary>
        public static IFastAop fastAop
        {
            get { return _fastAop; }
            set { _fastAop = value; }
        }

        /// <summary>
        /// 初始化实体属性元数据缓存
        /// 遍历指定命名空间下的所有实体类型，将属性信息缓存到 Redis 或 Web 缓存中
        /// </summary>
        /// <param name="nameSpace">实体类所在的命名空间</param>
        /// <param name="dbFile">配置文件名</param>
        /// <param name="aop">AOP接口</param>
        public static void InstanceProperties(string nameSpace, string dbFile = "db.config", IFastAop aop = null)
        {
            fastAop = aop;
            var projectName = FastDb.GetProjectName();
            FastRedis.RedisInfo.Init(dbFile, projectName);
            var config = DataConfig.GetConfig(null, projectName, dbFile);
            var assembly = AppDomain.CurrentDomain.GetAssemblies().ToList().Find(a => a.FullName.Split(',')[0] == projectName);
            if (assembly == null)
                assembly = Assembly.Load(projectName);

            if (assembly != null)
            {
                assembly.ExportedTypes.ToList().ForEach(t => {
                    var typeInfo = (t as TypeInfo);
                    if (typeInfo.Namespace != null && typeInfo.Namespace == nameSpace)
                    {
                        var key = string.Format("{0}.{1}", typeInfo.Namespace, typeInfo.Name);
                        var cacheList = new List<PropertyModel>();
                        typeInfo.DeclaredProperties.ToList().ForEach(a => {
                            var model = new PropertyModel();
                            model.Name = a.Name;
                            model.PropertyType = a.PropertyType;
                            cacheList.Add(model);
                        });

                        DbCache.Set<List<PropertyModel>>(config.CacheType, key, cacheList);
                    }
                });
            }
        }

        /// <summary>
        /// 初始化 CodeFirst 表结构检查
        /// 遍历指定命名空间下的实体类型，检查数据库表是否存在，不存在则自动创建
        /// </summary>
        /// <param name="nameSpace">实体类所在的命名空间</param>
        /// <param name="dbKey">数据库键</param>
        /// <param name="dbFile">配置文件名</param>
        /// <param name="aop">AOP接口</param>
        public static void InstanceTable(string nameSpace, string dbKey = null, string dbFile = "db.config", IFastAop aop = null)
        {
            fastAop = aop;
            var projectName = FastDb.GetProjectName();
            FastRedis.RedisInfo.Init(dbFile, projectName);
            var query = new DataQuery();
            query.Config = DataConfig.GetConfig(dbKey, projectName, dbFile);
            query.Key = dbKey;

            MapXml.CreateLogTable(query);

            var assembly = AppDomain.CurrentDomain.GetAssemblies().ToList().Find(a => a.FullName.Split(',')[0] == projectName);
            if (assembly == null)
                assembly = Assembly.Load(projectName);

            if (assembly != null)
            {
                assembly.ExportedTypes.ToList().ForEach(a => {
                    var typeInfo = (a as TypeInfo);
                    if (typeInfo.Namespace != null && typeInfo.Namespace == nameSpace)
                        BaseTable.Check(query, a.Name, typeInfo.DeclaredProperties.ToList(), typeInfo.GetCustomAttributes().ToList());
                });
            }
        }

        /// <summary>
        /// 从嵌入资源加载 XML 映射文件
        /// 与 InstanceMap 的区别在于：从程序集嵌入资源（而非文件系统）读取 SQL Map 配置文件
        /// </summary>
        /// <param name="dbKey">数据库键</param>
        /// <param name="dbFile">配置文件名</param>
        /// <param name="mapFile">Map配置文件名</param>
        /// <param name="aop">AOP接口</param>
        public static void InstanceMapResource(string dbKey = null, string dbFile = "db.config", string mapFile = "SqlMap.config", IFastAop aop = null)
        {
            fastAop = aop;
            var projectName = FastDb.GetProjectName();
            FastRedis.RedisInfo.Init(dbFile, projectName);
            var config = DataConfig.GetConfig(dbKey, projectName, dbFile);
            using (var db = new DataContext(dbKey))
            {
                var assembly = Assembly.Load(projectName);
                var map = new MapConfigModel();
                using (var resource = assembly.GetManifestResourceStream(string.Format("{0}.{1}", projectName, mapFile)))
                {
                    if (resource != null)
                    {
                        using (var reader = new StreamReader(resource))
                        {
                            var content = reader.ReadToEnd();
                            var xmlDoc = new XmlDocument();
                            xmlDoc.LoadXml(content);
                            var nodelList = xmlDoc.SelectNodes("configuration/MapConfig/SqlMap/Add");
                            foreach (XmlNode item in nodelList)
                            {
                                map.Path.Add(item.Attributes["File"].Value);
                            }
                        }
                    }
                    else
                        map = MapConfig.GetConfig(mapFile);
                }

                if (map.Path == null)
                    return;

                map.Path.ForEach(a =>
                {
                    using (var resource = assembly.GetManifestResourceStream(string.Format("{0}.{1}", projectName, a.Replace("/", "."))))
                    {
                        var xml = "";
                        if (resource != null)
                        {
                            using (var reader = new StreamReader(resource))
                            {
                                xml = reader.ReadToEnd();
                            }
                        }
                        var info = new FileInfo(a);
                        var key = BaseSymmetric.Generate(info.FullName);
                        if (!DbCache.Exists(config.CacheType, key))
                        {
                            var temp = new MapXmlModel();
                            temp.LastWrite = info.LastWriteTime;
                            temp.FileKey = MapXml.ReadXml(info.FullName, config, info.Name.ToLower().Replace(".xml", ""), xml);
                            temp.FileName = info.FullName;
                            if (MapXml.SaveXml(dbKey, key, info, config, db))
                                DbCache.Set<MapXmlModel>(config.CacheType, key, temp);
                        }
                        else if ((DbCache.Get<MapXmlModel>(config.CacheType, key).LastWrite - info.LastWriteTime).Milliseconds != 0)
                        {
                            DbCache.Get<MapXmlModel>(config.CacheType, key).FileKey.ForEach(f => { DbCache.Remove(config.CacheType, f); });

                            var model = new MapXmlModel();
                            model.LastWrite = info.LastWriteTime;
                            model.FileKey = MapXml.ReadXml(info.FullName, config, info.Name.ToLower().Replace(".xml", ""), xml);
                            model.FileName = info.FullName;
                            if (MapXml.SaveXml(dbKey, key, info, config, db))
                                DbCache.Set<MapXmlModel>(config.CacheType, key, model);
                        }
                    }
                });
            }
        }

        /// <summary>
        /// 初始化 XML 映射文件
        /// 从文件系统加载 SQL Map 配置，解析所有 XML 映射文件并缓存 SQL 语句
        /// </summary>
        /// <param name="dbKey">数据库键</param>
        /// <param name="dbFile">配置文件名</param>
        /// <param name="mapFile">Map配置文件名</param>
        /// <param name="aop">AOP接口</param>
        public static void InstanceMap(string dbKey = null, string dbFile = "db.config", string mapFile = "SqlMap.config", IFastAop aop = null)
        {        
            fastAop = aop;
            var list = MapConfig.GetConfig(mapFile);
            var config = DataConfig.GetConfig(dbKey, null, dbFile);
            using (var db = new DataContext(dbKey))
            {
                var query = new DataQuery { Config = config, Key = dbKey };

                if (config.IsMapSave)
                {
                    query.Config.DesignModel = FastData.Base.DesignPatterns.CodeFirst;
                    if (query.Config.DbType == DataDbType.Oracle)
                    {
                        var listInfo = typeof(FastData.DataModel.Oracle.Data_MapFile).GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly).ToList();
                        var listAttribute = typeof(FastData.DataModel.Oracle.Data_MapFile).GetTypeInfo().GetCustomAttributes().ToList();
                        BaseTable.Check(query, "Data_MapFile", listInfo, listAttribute);
                    }

                    if (query.Config.DbType == DataDbType.MySql)
                    {
                        var listInfo = typeof(FastData.DataModel.MySql.Data_MapFile).GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly).ToList();
                        var listAttribute = typeof(FastData.DataModel.MySql.Data_MapFile).GetTypeInfo().GetCustomAttributes().ToList();
                        BaseTable.Check(query, "Data_MapFile", listInfo, listAttribute);
                    }

                    if (query.Config.DbType == DataDbType.SqlServer)
                    {
                        var listInfo = typeof(FastData.DataModel.SqlServer.Data_MapFile).GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly).ToList();
                        var listAttribute = typeof(FastData.DataModel.SqlServer.Data_MapFile).GetTypeInfo().GetCustomAttributes().ToList();
                        BaseTable.Check(query, "Data_MapFile", listInfo, listAttribute);
                    }
                }

                if (list.Path == null)
                    return;

                list.Path.ForEach(p => {
                    var info = new FileInfo(p);
                    var key = BaseSymmetric.md5(32, info.FullName);

                    if (!DbCache.Exists(config.CacheType, key))
                    {
                        var temp = new MapXmlModel();
                        temp.LastWrite = info.LastWriteTime;
                        temp.FileKey = MapXml.ReadXml(p, config, info.Name.ToLower().Replace(".xml", ""));
                        temp.FileName = info.FullName;
                        if (MapXml.SaveXml(dbKey, key, info, config, db))
                            DbCache.Set<MapXmlModel>(config.CacheType, key, temp);
                    }
                    else if ((DbCache.Get<MapXmlModel>(config.CacheType, key).LastWrite - info.LastWriteTime).Milliseconds != 0)
                    {
                        DbCache.Get<MapXmlModel>(config.CacheType, key).FileKey.ForEach(a => { DbCache.Remove(config.CacheType, a); });

                        var model = new MapXmlModel();
                        model.LastWrite = info.LastWriteTime;
                        model.FileKey = MapXml.ReadXml(p, config, info.Name.ToLower().Replace(".xml", ""));
                        model.FileName = info.FullName;
                        if (MapXml.SaveXml(dbKey, key, info, config, db))
                            DbCache.Set<MapXmlModel>(config.CacheType, key, model);
                    }
                });
            }
        }


        /// <summary>
        /// maq 执行返回结果
        /// </summary>
        public static List<T> Query<T>(string name, DbParameter[] param, DataContext db = null, string key = null, bool isOutSql = false) where T : class, new()
        {
            key = key == null ? MapDb(name) : key;
            var config = db == null ? DataConfig.GetConfig(key) : db.config;
            if (config.IsUpdateCache)
                InstanceMap(config.Key);

            if (DbCache.Exists(config.CacheType, name.ToLower()))
            {
                var sql = MapXml.GetMapSql(name, ref param, db, key);
                isOutSql = isOutSql || IsMapLog(name);

                AopMapBefore(name, sql, param, config,AopType.Map_List_Model);

                var result = FastRead.ExecuteSql<T>(sql, param, db, key, isOutSql);
                if (MapXml.MapIsForEach(name, config))
                {
                    if (db == null)
                    {
                        using (var tempDb = new DataContext(key))
                        {
                                for (var i = 1; i <= MapXml.MapForEachCount(name, config); i++)
                                {
                                    result = MapXml.MapForEach<T>(result, name, tempDb, config, i);
                                }
                        }
                    }
                    else
                        result = MapXml.MapForEach<T>(result, name, db, config);
                }

                AopMapAfter(name, sql, param, config, AopType.Map_List_Model, result);
                return result;
            }
            else
            {
                AopMapBefore(name, "", param, config, AopType.Map_List_Model);
                var data = new List<T>();
                AopMapAfter(name, "", param, config, AopType.Map_List_Model, data);

                return data;
            }
        }

        /// <summary>
        /// 执行sql asy
        /// </summary>
        public static async Task<List<T>> QueryAsync<T>(string name, DbParameter[] param, DataContext db = null, string key = null, bool isOutSql = false) where T : class, new()
        {
            key = key == null ? MapDb(name) : key;
            var config = db == null ? DataConfig.GetConfig(key) : db.config;
            if (config.IsUpdateCache)
                InstanceMap(config.Key);

            if (DbCache.Exists(config.CacheType, name.ToLower()))
            {
                var sql = MapXml.GetMapSql(name, ref param, db, key);
                isOutSql = isOutSql || IsMapLog(name);

                AopMapBefore(name, sql, param, config, AopType.Map_List_Model);

                // 走真正的异步 I/O，而不是把同步查询丢进线程池：
                // 原实现用 AsyncHelper.RunAsync 包装同步 Query，
                // 那只是换了个线程阻塞，连接与命令仍在同步等待。
                var result = await FastRead.ExecuteSqlAsync<T>(sql, param, db, key, isOutSql).ConfigureAwait(false);

                if (MapXml.MapIsForEach(name, config))
                {
                    if (db == null)
                    {
                        using (var tempDb = new DataContext(key))
                        {
                            for (var i = 1; i <= MapXml.MapForEachCount(name, config); i++)
                            {
                                result = MapXml.MapForEach<T>(result, name, tempDb, config, i);
                            }
                        }
                    }
                    else
                        result = MapXml.MapForEach<T>(result, name, db, config);
                }

                AopMapAfter(name, sql, param, config, AopType.Map_List_Model, result);
                return result;
            }
            else
            {
                AopMapBefore(name, "", param, config, AopType.Map_List_Model);
                var data = new List<T>();
                AopMapAfter(name, "", param, config, AopType.Map_List_Model, data);
                return data;
            }
        }

        /// <summary>
        /// 延迟加载查询
        /// </summary>
        public static Lazy<List<T>> QueryLazy<T>(string name, DbParameter[] param, DataContext db = null, string key = null, bool isOutSql = false) where T : class, new()
        {
            return new Lazy<List<T>>(() => Query<T>(name, param, db, key, isOutSql));
        }


        /// <summary>
        /// 执行写操作
        /// </summary>
        /// <param name="name">Map名称</param>
        /// <param name="param">数据库参数数组</param>
        /// <param name="db">数据上下文</param>
        /// <param name="key">配置键</param>
        /// <param name="isOutSql">是否输出SQL</param>
        /// <returns>写入返回对象</returns>
        public static WriteReturn Write(string name, DbParameter[] param, DataContext db = null, string key = null, bool isOutSql = false)
        {
            key = key == null ? MapDb(name) : key;
            var config = db == null ? DataConfig.GetConfig(key) : db.config;
            if (config.IsUpdateCache)
                InstanceMap(config.Key);

            if (DbCache.Exists(config.CacheType, name.ToLower()))
            {
                var sql = MapXml.GetMapSql(name, ref param, db, key);
                isOutSql = isOutSql || IsMapLog(name);

                AopMapBefore(name, sql, param, config, AopType.Map_Write);

                var result = FastWrite.ExecuteSql(sql, param, db, key, isOutSql);
                AopMapAfter(name, sql, param, config, AopType.Map_Write, result.IsSuccess);
                return result;
            }
            else
            {
                AopMapBefore(name, "", param, config, AopType.Map_Write);
                var data = new WriteReturn();
                AopMapAfter(name, "", param, config,AopType.Map_Write,data.IsSuccess);
                return data;
            }
        }

        /// <summary>
        ///  maq 执行写操作 asy
        /// </summary>
        /// <param name="name">Map名称</param>
        /// <param name="param">数据库参数数组</param>
        /// <param name="db">数据上下文</param>
        /// <param name="key">配置键</param>
        /// <param name="isOutSql">是否输出SQL</param>
        /// <returns>写入返回对象任务</returns>
        public static async Task<WriteReturn> WriteAsync(string name, DbParameter[] param, DataContext db = null, string key = null, bool isOutSql = false)
        {
            key = key == null ? MapDb(name) : key;
            var config = db == null ? DataConfig.GetConfig(key) : db.config;
            if (config.IsUpdateCache)
                InstanceMap(config.Key);

            if (DbCache.Exists(config.CacheType, name.ToLower()))
            {
                var sql = MapXml.GetMapSql(name, ref param, db, key);
                isOutSql = isOutSql || IsMapLog(name);

                AopMapBefore(name, sql, param, config, AopType.Map_Write);

                // 走真正的异步写入，而不是把同步 Write 丢进线程池
                var result = await FastWrite.ExecuteSqlAsync(sql, param, db, key, isOutSql).ConfigureAwait(false);
                AopMapAfter(name, sql, param, config, AopType.Map_Write, result.IsSuccess);
                return result;
            }
            else
            {
                AopMapBefore(name, "", param, config, AopType.Map_Write);
                var data = new WriteReturn();
                AopMapAfter(name, "", param, config, AopType.Map_Write, data.IsSuccess);
                return data;
            }
        }

        /// <summary>
        /// 延迟加载写入
        /// </summary>
        public static Lazy<WriteReturn> WriteLazy(string name, DbParameter[] param, DataContext db = null, string key = null, bool isOutSql = false)
        {
            return new Lazy<WriteReturn>(() => Write(name, param, db, key, isOutSql));
        }


        /// <summary>
        /// maq 执行返回 List<Dictionary<string, object>>
        /// </summary>
        public static List<Dictionary<string, object>> Query(string name, DbParameter[] param, DataContext db = null, string key = null, bool isOutSql = false)
        {
            key = key == null ? MapDb(name) : key;
            var config = db == null ? DataConfig.GetConfig(key) : db.config;
            if (config.IsUpdateCache)
                InstanceMap(config.Key);

            if (DbCache.Exists(config.CacheType, name.ToLower()))
            {
                var sql = MapXml.GetMapSql(name, ref param, db, key);
                isOutSql = isOutSql || IsMapLog(name);

                AopMapBefore(name, sql, param, config,AopType.Map_List_Dic);

                var result = FastRead.ExecuteSql(sql, param, db, key, isOutSql);

                if (MapXml.MapIsForEach(name, config))
                {
                    if (db == null)
                    {
                        using (var tempDb = new DataContext(key))
                        {
                                for (var i = 1; i <= MapXml.MapForEachCount(name, config); i++)
                                {
                                    result = MapXml.MapForEach(result, name, tempDb, key, config, i);
                                }
                        }
                    }
                    else
                        result = MapXml.MapForEach(result, name, db, key, config);
                }

                AopMapAfter(name, sql, param, config, AopType.Map_List_Dic, result);
                return result;
            }
            else
            {
                AopMapBefore(name, "", param, config, AopType.Map_List_Dic);
                var data = new List<Dictionary<string, object>>();
                AopMapAfter(name, "", param, config, AopType.Map_List_Dic, data);
                return data;
            }
        }

        /// <summary>
        /// 执行sql List<Dictionary<string, object>> asy
        /// </summary>
        public static async Task<List<Dictionary<string, object>>> QueryAsync(string name, DbParameter[] param, DataContext db = null, string key = null, bool isOutSql = false)
        {
            key = key == null ? MapDb(name) : key;
            var config = db == null ? DataConfig.GetConfig(key) : db.config;
            if (config.IsUpdateCache)
                InstanceMap(config.Key);

            if (DbCache.Exists(config.CacheType, name.ToLower()))
            {
                var sql = MapXml.GetMapSql(name, ref param, db, key);
                isOutSql = isOutSql || IsMapLog(name);

                AopMapBefore(name, sql, param, config, AopType.Map_List_Dic);

                // 走真正的异步 I/O
                var result = await FastRead.ExecuteSqlAsync(sql, param, db, key, isOutSql).ConfigureAwait(false);

                if (MapXml.MapIsForEach(name, config))
                {
                    if (db == null)
                    {
                        using (var tempDb = new DataContext(key))
                        {
                            for (var i = 1; i <= MapXml.MapForEachCount(name, config); i++)
                            {
                                result = MapXml.MapForEach(result, name, tempDb, key, config, i);
                            }
                        }
                    }
                    else
                        result = MapXml.MapForEach(result, name, db, key, config);
                }

                AopMapAfter(name, sql, param, config, AopType.Map_List_Dic, result);
                return result;
            }
            else
            {
                AopMapBefore(name, "", param, config, AopType.Map_List_Dic);
                var data = new List<Dictionary<string, object>>();
                AopMapAfter(name, "", param, config, AopType.Map_List_Dic, data);
                return data;
            }
        }

        /// <summary>
        /// 延迟加载查询（字典格式）
        /// </summary>
        public static Lazy<List<Dictionary<string, object>>> QueryLazy(string name, DbParameter[] param, DataContext db = null, string key = null, bool isOutSql = false)
        {
            return new Lazy<List<Dictionary<string, object>>>(() => Query(name, param, db, key, isOutSql));
        }


        /// <summary>
        /// 执行分页查询（字典格式）
        /// </summary>
        /// <param name="pModel">分页模型</param>
        /// <param name="sql">SQL 语句</param>
        /// <param name="param">数据库参数数组</param>
        /// <param name="db">数据上下文</param>
        /// <param name="key">数据库连接键</param>
        /// <param name="isOutSql">是否输出 SQL</param>
        /// <returns>分页查询结果</returns>
        private static PageResult ExecuteSqlPage(PageModel pModel, string sql, DbParameter[] param, DataContext db = null, string key = null, bool isOutSql = false)
        {
            var result = new DataReturn();
            var config = DataConfig.GetConfig(key);
            var stopwatch = new Stopwatch();

            stopwatch.Start();

            if (db == null)
            {
                using (var tempDb = new DataContext(key))
                {
                    result = tempDb.GetPageSql(pModel, sql, param,false);
                }
            }
            else
                result = db.GetPageSql(pModel, sql, param,false);

            stopwatch.Stop();

            config.IsOutSql = config.IsOutSql || isOutSql;
            DbLog.LogSql(config.IsOutSql, result.Sql, config.DbType, stopwatch.Elapsed.TotalMilliseconds);

            return result.PageResult;
        }

        /// <summary>
        /// maq 执行分页
        /// </summary>
        public static PageResult QueryPage(PageModel pModel, string name, DbParameter[] param, DataContext db = null, string key = null, bool isOutSql = false)
        {
            key = key == null ? MapDb(name) : key;
            var config = db == null ? DataConfig.GetConfig(key) : db.config;
            if (config.IsUpdateCache)
                InstanceMap(config.Key);
            if (DbCache.Exists(config.CacheType, name.ToLower()))
            {
                var sql = MapXml.GetMapSql(name, ref param, db, key);
                isOutSql = isOutSql || IsMapLog(name);

                AopMapBefore(name, sql, param, config,AopType.Map_Page_Dic);

                var result = ExecuteSqlPage(pModel, sql, param, db, key, isOutSql);

                if (MapXml.MapIsForEach(name, config))
                {
                    if (db == null)
                    {
                        using (var tempDb = new DataContext(key))
                        {
                                for (var i = 1; i <= MapXml.MapForEachCount(name, config); i++)
                                {
                                    result.list = MapXml.MapForEach(result.list, name, tempDb, key, config, i);
                                }
                            }
                        }
                        else
                            result.list = MapXml.MapForEach(result.list, name, db, key, config);
                    }

                    AopMapAfter(name, sql, param, config, AopType.Map_Page_Dic, result.list);
                return result;
            }
            else
            {
                AopMapBefore(name, "", param, config, AopType.Map_Page_Dic);
                var data = new PageResult();
                AopMapAfter(name, "", param, config, AopType.Map_Page_Dic, data.list);
                return data;
            }
        }

        /// <summary>
        /// 执行分页 asy
        /// </summary>
        public static Task<PageResult> QueryPageAsync(PageModel pModel, string name, DbParameter[] param, DataContext db = null, string key = null, bool isOutSql = false)
        {
            // 注意：QueryPage 目前仍是同步实现 —— DataContext.GetPageSql 没有异步版本，
            // 要真正异步化需要先实现异步分页 SQL 执行器（见 issue #4 的遗留部分）。
            // 这里保留 AsyncHelper 包装，是为了不改变「返回 Task」的公开契约，
            // 但它并不提供 I/O 异步，只是把同步调用移出调用线程。
            return AsyncHelper.RunAsync(() => QueryPage(pModel, name, param, db, key, isOutSql));
        }

        /// <summary>
        /// 延迟加载分页查询（字典格式）
        /// </summary>
        public static Lazy<PageResult> QueryPageLazy(PageModel pModel, string name, DbParameter[] param, DataContext db = null, string key = null, bool isOutSql = false)
        {
            return new Lazy<PageResult>(() => QueryPage(pModel, name, param, db, key, isOutSql));
        }


        /// <summary>
        /// 执行分页查询（实体格式）
        /// </summary>
        /// <typeparam name="T">实体类型</typeparam>
        /// <param name="pModel">分页模型</param>
        /// <param name="sql">SQL 语句</param>
        /// <param name="param">数据库参数数组</param>
        /// <param name="db">数据上下文</param>
        /// <param name="key">数据库连接键</param>
        /// <param name="isOutSql">是否输出 SQL</param>
        /// <returns>分页查询结果</returns>
        private static PageResult<T> ExecuteSqlPage<T>(PageModel pModel, string sql, DbParameter[] param, DataContext db = null, string key = null, bool isOutSql = false) where T : class, new()
        {
            var result = new DataReturn<T>();
            var config = DataConfig.GetConfig(key);
            var stopwatch = new Stopwatch();

            stopwatch.Start();

            if (db == null)
            {
                using (var tempDb = new DataContext(key))
                {
                    result = tempDb.GetPageSql<T>(pModel, sql, param,false);
                }
            }
            else
                result = db.GetPageSql<T>(pModel, sql, param,false);

            stopwatch.Stop();

            config.IsOutSql = config.IsOutSql || isOutSql;
            DbLog.LogSql(config.IsOutSql, result.Sql, config.DbType, stopwatch.Elapsed.TotalMilliseconds);

            return result.PageResult;
        }

        /// <summary>
        /// maq 执行分页
        /// </summary>
        public static PageResult<T> QueryPage<T>(PageModel pModel, string name, DbParameter[] param, DataContext db = null, string key = null, bool isOutSql = false) where T : class, new()
        {
            key = key == null ? MapDb(name) : key;
            var config = db == null ? DataConfig.GetConfig(key) : db.config;
            if (config.IsUpdateCache)
                InstanceMap(config.Key);
            if (DbCache.Exists(config.CacheType, name.ToLower()))
            {
                var sql = MapXml.GetMapSql(name, ref param, db, key);
                isOutSql = isOutSql || IsMapLog(name);

                AopMapBefore(name, sql, param, config,AopType.Map_Page_Model);

                var result = ExecuteSqlPage<T>(pModel, sql, param, db, key, isOutSql);

                if (MapXml.MapIsForEach(name, config))
                {
                    if (db == null)
                    {
                        using (var tempDb = new DataContext(key))
                        {
                                for (var i = 1; i <= MapXml.MapForEachCount(name, config); i++)
                                {
                                    result.list = MapXml.MapForEach<T>(result.list, name, tempDb, config, i);
                                }
                        }
                    }
                    else
                        result.list = MapXml.MapForEach<T>(result.list, name, db, config);
                }
                AopMapAfter(name, sql, param, config, AopType.Map_Page_Model, result.list);
                return result;
            }
            else
            {
                AopMapBefore(name, "", param, config, AopType.Map_Page_Model);
                var data = new PageResult<T>();
                AopMapAfter(name, "", param, config, AopType.Map_Page_Model, data.list);
                return data;
            }
        }

        /// <summary>
        /// 执行分页 asy
        /// </summary>
        public static Task<PageResult<T>> QueryPageAsync<T>(PageModel pModel, string name, DbParameter[] param, DataContext db = null, string key = null, bool isOutSql = false) where T : class, new()
        {
            // 同 QueryPage：底层 GetPageSql<T> 无异步版本，保留包装以维持公开契约。
            return AsyncHelper.RunAsync(() => QueryPage<T>(pModel, name, param, db, key, isOutSql));
        }

        /// <summary>
        /// 延迟加载分页查询（实体格式）
        /// </summary>
        public static Lazy<PageResult<T>> QueryPageLazy<T>(PageModel pModel, string name, DbParameter[] param, DataContext db = null, string key = null, bool isOutSql = false) where T : class, new()
        {
            return new Lazy<PageResult<T>>(() => QueryPage<T>(pModel, name, param, db, key, isOutSql));
        }

        /// <summary>
        /// 验证xml
        /// </summary>
        /// <returns></returns>
        public static bool CheckMap(string xml, string dbKey = null)
        {
            var config = DataConfig.GetConfig(dbKey);
            var info = new FileInfo(xml);
            return MapXml.GetXmlList(info.FullName, "sqlMap", config).isSuccess;
        }

        /// <summary>
        /// map 参数列表
        /// </summary>
        public static List<string> MapParam(string name, ConfigModel config = null)
        {
            var cacheType = config == null ? DataConfig.GetConfig().CacheType : config.CacheType;
            return DbCache.Get<List<string>>(cacheType, MapCacheKey(name, "param"));
        }

        /// <summary>
        /// map db
        /// </summary>
        public static string MapDb(string name)
        {
            return DbCache.Get(DataConfig.GetConfig().CacheType, MapCacheKey(name, "db"));
        }

        /// <summary>
        /// map type
        /// </summary>
        public static string MapType(string name)
        {
            return DbCache.Get(DataConfig.GetConfig().CacheType, MapCacheKey(name, "type"));
        }

        /// <summary>
        /// map view
        /// </summary>
        public static string MapView(string name)
        {
            return DbCache.Get(DataConfig.GetConfig().CacheType, MapCacheKey(name, "view"));
        }

        /// <summary>
        /// 是否存在map id
        /// </summary>
        public static bool IsExists(string name)
        {
            return DbCache.Exists(DataConfig.GetConfig().CacheType, name.ToLower());
        }

        /// <summary>
        /// 获取 Map 的备注说明
        /// </summary>
        public static string MapRemark(string name)
        {
            return DbCache.Get(DataConfig.GetConfig().CacheType, MapCacheKey(name, "remark"));
        }

        /// <summary>
        /// 检查 Map 是否启用日志记录
        /// </summary>
        public static bool IsMapLog(string name)
        {
            return DbCache.Get(DataConfig.GetConfig().CacheType, MapCacheKey(name, "log")).ToStr().ToLower() == "true";
        }

        /// <summary>
        /// 获取 Map 参数的备注说明
        /// </summary>
        public static string MapParamRemark(string name, string param)
        {
            return DbCache.Get(DataConfig.GetConfig().CacheType, MapCacheKey(name, param, "remark"));
        }

        /// <summary>
        /// 获取api接口key
        /// </summary>
        public static Dictionary<string, object> Api
        {
            get => DbCache.Get<Dictionary<string, object>>(DataConfig.GetConfig().CacheType, "FastMap.Api") ?? new Dictionary<string, object>();
        }

        /// <summary>
        /// 获取map验证必填
        /// </summary>
        public static string MapRequired(string name, string param)
        {
            return DbCache.Get(DataConfig.GetConfig().CacheType, MapCacheKey(name, param, "required"));
        }

        /// <summary>
        /// 获取map验证长度
        /// </summary>
        public static string MapMaxlength(string name, string param)
        {
            return DbCache.Get(DataConfig.GetConfig().CacheType, MapCacheKey(name, param, "maxlength"));
        }

        /// <summary>
        /// 获取map验证日期
        /// </summary>
        public static string MapDate(string name, string param)
        {
            return DbCache.Get(DataConfig.GetConfig().CacheType, MapCacheKey(name, param, "date"));
        }

        /// <summary>
        /// 获取map验证map
        /// </summary>
        public static string MapCheckMap(string name, string param)
        {
            return DbCache.Get(DataConfig.GetConfig().CacheType, MapCacheKey(name, param, "checkmap"));
        }

        /// <summary>
        /// 获取map验证map
        /// </summary>
        public static string MapExistsMap(string name, string param)
        {
            return DbCache.Get(DataConfig.GetConfig().CacheType, MapCacheKey(name, param, "existsmap"));
        }

        /// <summary>
        /// 获取db配置文件
        /// </summary>
        public static ConfigModel DbConfig(string name)
        {
            return DataConfig.GetConfig(name);
        }

        #region 私有辅助方法

        /// <summary>
        /// 生成 Map 缓存键（单段后缀）
        /// </summary>
        private static string MapCacheKey(string name, string suffix)
        {
            return string.Format("{0}.{1}", name.ToLower(), suffix);
        }

        /// <summary>
        /// 生成 Map 缓存键（参数+后缀两段）
        /// </summary>
        private static string MapCacheKey(string name, string param, string suffix)
        {
            return string.Format("{0}.{1}.{2}", name.ToLower(), param.ToLower(), suffix);
        }

        /// <summary>
        /// AOP Map 前置事件触发
        /// </summary>
        private static void AopMapBefore(string mapName, string sql, DbParameter[] param, ConfigModel config, AopType type)
        {
            if (fastAop == null)
                return;

            var context = new MapBeforeContext
            {
                mapName = mapName,
                sql = sql,
                type = type,
                dbType = config.DbType
            };

            if (param != null)
                context.param = param.ToList();

            fastAop.MapBefore(context);
        }

        /// <summary>
        /// AOP Map 后置事件触发
        /// </summary>
        private static void AopMapAfter(string mapName, string sql, DbParameter[] param, ConfigModel config, AopType type, object data)
        {
            if (fastAop == null)
                return;

            var context = new MapAfterContext
            {
                mapName = mapName,
                sql = sql,
                type = type,
                dbType = config.DbType,
                result = data
            };

            if (param != null)
                context.param = param.ToList();

            fastAop.MapAfter(context);
        }

        #endregion
    }
}

