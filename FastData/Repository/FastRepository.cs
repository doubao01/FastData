using FastData.Base;
using FastData.Context;
using FastData.Model;
using FastUntility.Base;
using FastUntility.Page;
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Linq;
using FastData.CacheModel;
using FastData.Check;
using FastData.DbTypes;
using System.Reflection;
using System.IO;
using System.Linq.Expressions;
using FastData.Config;
using FastData.Aop;

namespace FastData.Repository
{
    /// <summary>
    /// FastRepository - 仓储模式实现
    ///
    /// 职责：实现 IFastRepository 接口，提供读写分离、Lambda 查询、XML 映射等统一访问入口。
    ///
    /// 主要功能：
    /// 1. 读取操作（Query/QueryAsync）：LINQ 查询、原生 SQL、XML 映射查询
    /// 2. 写入操作（Add/Update/Delete）：单条和批量
    /// 3. 异步支持：所有方法都有对应的 Async 版本
    /// 4. 事务支持：通过 DataContext 统一管理
    /// 5. 缓存支持：自动集成 DbCache
    ///
    /// 注意事项：
    /// - 推荐通过依赖注入（AddFastData）使用此类型
    /// - 读写操作共用同一个 DataContext，便于事务管理
    /// - key 参数用于多数据库场景，未指定时使用默认配置
    /// </summary>
    public class FastRepository : IFastRepository
    {
        internal Query query { get; set; } = new Query();

        /// <summary>
        /// maq 执行返回结果
        /// </summary>
        public List<T> Query<T>(string name, DbParameter[] param, DataContext db = null, string key = null, bool isOutSql = false) where T : class, new()
        {
            key = key == null ? MapDb(name) : key;
            return FastMap.Query<T>(name, param, db, key, isOutSql);
        }

        /// <summary>
        /// 执行sql asy
        /// </summary>
        public Task<List<T>> QueryAsync<T>(string name, DbParameter[] param, DataContext db = null, string key = null, bool isOutSql = false) where T : class, new()
        {
            return AsyncHelper.RunSyncAsAsync(() => Query<T>(name, param, db, key, isOutSql));
        }

        /// <summary>
        /// maq 执行返回结果 lazy
        /// </summary>
        public Lazy<List<T>> QueryLazy<T>(string name, DbParameter[] param, DataContext db = null, string key = null, bool isOutSql = false) where T : class, new()
        {
            return AsyncHelper.ToLazy(() => Query<T>(name, param, db, key, isOutSql));
        }

        /// <summary>
        /// maq 执行返回结果 lazy asy
        /// </summary>
        public Task<Lazy<List<T>>> QueryLazyAsync<T>(string name, DbParameter[] param, DataContext db = null, string key = null, bool isOutSql = false) where T : class, new()
        {
            return AsyncHelper.RunSyncAsLazyAsync(() => Query<T>(name, param, db, key, isOutSql));
        }


        /// <summary>
        /// maq 执行返回 List<Dictionary<string, object>>
        /// </summary>
        /// <param name="name">Map名称</param>
        /// <param name="param">数据库参数数组</param>
        /// <param name="db">数据上下文</param>
        /// <param name="key">配置键</param>
        /// <param name="isOutSql">是否输出SQL</param>
        /// <returns>字典列表</returns>
        public List<Dictionary<string, object>> Query(string name, DbParameter[] param, DataContext db = null, string key = null, bool isOutSql = false)
        {
            key = key == null ? MapDb(name) : key;
            return FastMap.Query(name, param, db, key, isOutSql);
        }

        /// <summary>
        /// 执行sql List<Dictionary<string, object>> asy
        /// </summary>
        /// <param name="name">Map名称</param>
        /// <param name="param">数据库参数数组</param>
        /// <param name="db">数据上下文</param>
        /// <param name="key">配置键</param>
        /// <param name="isOutSql">是否输出SQL</param>
        /// <returns>字典列表任务</returns>
        public Task<List<Dictionary<string, object>>> QueryAsync(string name, DbParameter[] param, DataContext db = null, string key = null, bool isOutSql = false)
        {
            return AsyncHelper.RunSyncAsAsync(() => Query(name, param, db, key, isOutSql));
        }

        /// <summary>
        /// maq 执行返回 List<Dictionary<string, object>> lazy
        /// </summary>
        /// <param name="name">Map名称</param>
        /// <param name="param">数据库参数数组</param>
        /// <param name="db">数据上下文</param>
        /// <param name="key">配置键</param>
        /// <param name="isOutSql">是否输出SQL</param>
        /// <returns>字典列表懒加载</returns>
        public Lazy<List<Dictionary<string, object>>> QueryLazy(string name, DbParameter[] param, DataContext db = null, string key = null, bool isOutSql = false)
        {
            return AsyncHelper.ToLazy(() => Query(name, param, db, key, isOutSql));
        }

        /// <summary>
        /// maq 执行返回 List<Dictionary<string, object>> lazy asy
        /// </summary>
        /// <param name="name">Map名称</param>
        /// <param name="param">数据库参数数组</param>
        /// <param name="db">数据上下文</param>
        /// <param name="key">配置键</param>
        /// <param name="isOutSql">是否输出SQL</param>
        /// <returns>字典列表懒加载任务</returns>
        public Task<Lazy<List<Dictionary<string, object>>>> QueryLazyAsync(string name, DbParameter[] param, DataContext db = null, string key = null, bool isOutSql = false)
        {
            return AsyncHelper.RunSyncAsLazyAsync(() => Query(name, param, db, key, isOutSql));
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
        public WriteReturn Write(string name, DbParameter[] param, DataContext db = null, string key = null, bool isOutSql = false)
        {
            key = key == null ? MapDb(name) : key;
            return FastMap.Write(name, param, db, key, isOutSql);
        }

        /// <summary>
        ///  maq 执行写操作 asy
        /// </summary>
        public Task<WriteReturn> WriteAsync(string name, DbParameter[] param, DataContext db = null, string key = null, bool isOutSql = false)
        {
            return AsyncHelper.RunSyncAsAsync(() => Write(name, param, db, key, isOutSql));
        }

        /// <summary>
        /// maq 执行写操作 asy lazy
        /// </summary>
        public Lazy<WriteReturn> WriteLazy(string name, DbParameter[] param, DataContext db = null, string key = null, bool isOutSql = false)
        {
            return AsyncHelper.ToLazy(() => Write(name, param, db, key, isOutSql));
        }

        /// <summary>
        /// maq 执行写操作 asy lazy asy
        /// </summary>
        public Task<Lazy<WriteReturn>> WriteLazyAsync(string name, DbParameter[] param, DataContext db = null, string key = null, bool isOutSql = false)
        {
            return AsyncHelper.RunSyncAsLazyAsync(() => Write(name, param, db, key, isOutSql));
        }


        /// <summary>
        /// maq 执行分页
        /// </summary>
        public PageResult QueryPage(PageModel pModel, string name, DbParameter[] param, DataContext db = null, string key = null, bool isOutSql = false)
        {
            key = key == null ? MapDb(name) : key;
            return FastMap.QueryPage(pModel, name, param, db, key, isOutSql);
        }

        /// <summary>
        /// 执行分页 asy
        /// </summary>
        public Task<PageResult> QueryPageAsync(PageModel pModel, string name, DbParameter[] param, DataContext db = null, string key = null, bool isOutSql = false)
        {
            key = key == null ? MapDb(name) : key;
            return FastMap.QueryPageAsync(pModel, name, param, db, key, isOutSql);
        }

        /// <summary>
        /// maq 执行分页 lazy
        /// </summary>
        public Lazy<PageResult> QueryPageLazy(PageModel pModel, string name, DbParameter[] param, DataContext db = null, string key = null, bool isOutSql = false)
        {
            return AsyncHelper.ToLazy(() => QueryPage(pModel, name, param, db, key, isOutSql));
        }

        /// <summary>
        /// maq 执行分页lazy asy
        /// </summary>
        public Task<Lazy<PageResult>> QueryPageLazyAsync(PageModel pModel, string name, DbParameter[] param, DataContext db = null, string key = null, bool isOutSql = false)
        {
            return AsyncHelper.RunSyncAsLazyAsync(() => QueryPage(pModel, name, param, db, key, isOutSql));
        }


        /// <summary>
        /// maq 执行分页
        /// </summary>
        public PageResult<T> QueryPage<T>(PageModel pModel, string name, DbParameter[] param, DataContext db = null, string key = null, bool isOutSql = false) where T : class, new()
        {
            key = key == null ? MapDb(name) : key;
            return FastMap.QueryPage<T>(pModel, name, param, db, key, isOutSql);
        }

        /// <summary>
        /// 执行分页 asy
        /// </summary>
        public Task<PageResult<T>> QueryPageAsync<T>(PageModel pModel, string name, DbParameter[] param, DataContext db = null, string key = null, bool isOutSql = false) where T : class, new()
        {
            key = key == null ? MapDb(name) : key;
            return FastMap.QueryPageAsync<T>(pModel, name, param, db, key, isOutSql);
        }

        /// <summary>
        /// maq 执行分页 lazy
        /// </summary>
        public Lazy<PageResult<T>> QueryPageLazy<T>(PageModel pModel, string name, DbParameter[] param, DataContext db = null, string key = null, bool isOutSql = false) where T : class, new()
        {
            return AsyncHelper.ToLazy(() => QueryPage<T>(pModel, name, param, db, key, isOutSql));
        }

        /// <summary>
        /// maq 执行分页lazy asy
        /// </summary>
        public Task<Lazy<PageResult<T>>> QueryPageLazyAsync<T>(PageModel pModel, string name, DbParameter[] param, DataContext db = null, string key = null, bool isOutSql = false) where T : class, new()
        {
            return AsyncHelper.RunSyncAsLazyAsync(() => QueryPage<T>(pModel, name, param, db, key, isOutSql));
        }


        /// <summary>
        /// map db
        /// </summary>
        /// <param name="name"></param>
        /// <returns></returns>
        public string MapDb(string name, bool isMapDb = false)
        {
            if (string.IsNullOrEmpty(this.query.Data.Key) && isMapDb == false)
                return DbCache.Get(DataConfig.GetConfig().CacheType, string.Format("{0}.db", name.ToLower()));
            else
                return this.query.Data.Key;
        }

        /// <summary>
        /// map 参数列表
        /// </summary>
        /// <param name="name"></param>
        /// <returns></returns>
        public List<string> MapParam(string name)
        {
            return DbCache.Get<List<string>>(DataConfig.GetConfig().CacheType, string.Format("{0}.param", name.ToLower()));
        }

        /// <summary>
        /// 初始化map 3
        /// </summary>
        /// <returns></returns>
        private void InstanceMap(string dbKey = null, string mapFile = "SqlMap.config")
        {
            var list = MapConfig.GetConfig(mapFile);
            var config = DataConfig.GetConfig(dbKey);
            using (var db = new DataContext(dbKey))
            {
                var query = new DataQuery { Config = config, Key = dbKey };

                list.Path.ForEach(p => {
                    var info = new FileInfo(p);
                    var key = BaseSymmetric.md5(32, info.FullName);

                    if (!DbCache.Exists(config.CacheType, key))
                    {
                        var temp = new MapXmlModel();
                        temp.LastWrite = info.LastWriteTime;
                        temp.FileKey = MapXml.ReadXml(info.FullName, config, info.Name.ToLower().Replace(".xml", ""));
                        temp.FileName = info.FullName;
                        if (MapXml.SaveXml(dbKey, key, info, config, db))
                            DbCache.Set<MapXmlModel>(config.CacheType, key, temp);
                    }
                    else if ((DbCache.Get<MapXmlModel>(config.CacheType, key).LastWrite - info.LastWriteTime).Milliseconds != 0)
                    {
                        DbCache.Get<MapXmlModel>(config.CacheType, key).FileKey.ForEach(a => { DbCache.Remove(config.CacheType, a); });

                        var model = new MapXmlModel();
                        model.LastWrite = info.LastWriteTime;
                        model.FileKey = MapXml.ReadXml(info.FullName, config, info.Name.ToLower().Replace(".xml", ""));
                        model.FileName = info.FullName;
                        if (MapXml.SaveXml(dbKey, key, info, config, db))
                            DbCache.Set<MapXmlModel>(config.CacheType, key, model);
                    }
                });

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
            }
        }

        /// <summary>
        /// 获取api接口key
        /// </summary>
        public Dictionary<string, object> Api()
        {
            return DbCache.Get<Dictionary<string, object>>(DataConfig.GetConfig().CacheType, "FastMap.Api") ?? new Dictionary<string, object>();
        }

        /// <summary>
        /// 验证xml
        /// </summary>
        /// <returns></returns>
        public bool CheckMap(string xml, string dbKey = null)
        {
            var config = DataConfig.GetConfig(dbKey);
            var info = new FileInfo(xml);
            return MapXml.GetXmlList(info.FullName, "sqlMap", config).isSuccess;
        }

        /// <summary>
        /// map 参数列表
        /// </summary>
        /// <param name="name"></param>
        /// <returns></returns>
        public List<string> MapParam(string name, ConfigModel config = null)
        {
            var cacheType = config == null ? DataConfig.GetConfig().CacheType : config.CacheType;
            return DbCache.Get<List<string>>(cacheType, string.Format("{0}.param", name.ToLower()));
        }

        /// <summary>
        /// map db
        /// </summary>
        /// <param name="name"></param>
        /// <returns></returns>
        public string MapDb(string name)
        {
            return DbCache.Get(DataConfig.GetConfig().CacheType, string.Format("{0}.db", name.ToLower()));
        }

        /// <summary>
        /// map db
        /// </summary>
        /// <param name="name"></param>
        /// <returns></returns>
        public string MapType(string name)
        {
            return DbCache.Get(DataConfig.GetConfig().CacheType, string.Format("{0}.type", name.ToLower()));
        }

        /// <summary>
        /// map view
        /// </summary>
        /// <param name="name"></param>
        /// <returns></returns>
        public string MapView(string name)
        {
            return DbCache.Get(DataConfig.GetConfig().CacheType, string.Format("{0}.view", name.ToLower()));
        }

        /// <summary>
        /// 是否存在map id
        /// </summary>
        /// <param name="name"></param>
        /// <returns></returns>
        public bool IsExists(string name)
        {
            return DbCache.Exists(DataConfig.GetConfig().CacheType, name.ToLower());
        }

        /// <summary>
        /// 获取Map的备注信息
        /// </summary>
        /// <param name="name">Map名称</param>
        /// <returns>Map备注信息</returns>
        public string MapRemark(string name)
        {
            return DbCache.Get(DataConfig.GetConfig().CacheType, string.Format("{0}.remark", name.ToLower()));
        }

        /// <summary>
        /// 判断Map是否开启日志记录
        /// </summary>
        /// <param name="name">Map名称</param>
        /// <returns>是否开启日志记录</returns>
        public bool IsMapLog(string name)
        {
            return DbCache.Get(DataConfig.GetConfig().CacheType, string.Format("{0}.log", name.ToLower())).ToStr().ToLower() == "true";
        }

        /// <summary>
        /// 获取Map参数的备注信息
        /// </summary>
        /// <param name="name">Map名称</param>
        /// <param name="param">参数名称</param>
        /// <returns>参数备注信息</returns>
        public string MapParamRemark(string name, string param)
        {
            return DbCache.Get(DataConfig.GetConfig().CacheType, string.Format("{0}.{1}.remark", name.ToLower(), param.ToLower()));
        }

        /// <summary>
        /// 获取map验证必填
        /// </summary>
        /// <param name="name"></param>
        /// <param name="param"></param>
        /// <returns></returns>
        public string MapRequired(string name, string param)
        {
            return DbCache.Get(DataConfig.GetConfig().CacheType, string.Format("{0}.{1}.required", name.ToLower(), param.ToLower()));
        }

        /// <summary>
        /// 获取map验证长度
        /// </summary>
        /// <param name="name"></param>
        /// <param name="param"></param>
        /// <returns></returns>
        public string MapMaxlength(string name, string param)
        {
            return DbCache.Get(DataConfig.GetConfig().CacheType, string.Format("{0}.{1}.maxlength", name.ToLower(), param.ToLower()));
        }

        /// <summary>
        /// 获取map验证日期
        /// </summary>
        /// <param name="name"></param>
        /// <param name="param"></param>
        /// <returns></returns>
        public string MapDate(string name, string param)
        {
            return DbCache.Get(DataConfig.GetConfig().CacheType, string.Format("{0}.{1}.date", name.ToLower(), param.ToLower()));
        }

        /// <summary>
        /// 获取map验证map
        /// </summary>
        /// <param name="name"></param>
        /// <param name="param"></param>
        /// <returns></returns>
        public string MapCheckMap(string name, string param)
        {
            return DbCache.Get(DataConfig.GetConfig().CacheType, string.Format("{0}.{1}.checkmap", name.ToLower(), param.ToLower()));
        }

        /// <summary>
        /// 获取map验证map
        /// </summary>
        /// <param name="name"></param>
        /// <param name="param"></param>
        /// <returns></returns>
        public string MapExistsMap(string name, string param)
        {
            return DbCache.Get(DataConfig.GetConfig().CacheType, string.Format("{0}.{1}.existsmap", name.ToLower(), param.ToLower()));
        }

        /// <summary>
        /// 获取指定名称的数据库配置
        /// </summary>
        /// <param name="name">配置名称</param>
        /// <returns>数据库配置模型</returns>
        public ConfigModel DbConfig(string name)
        {
            return DataConfig.GetConfig(name);
        }

        /// <summary>
        /// 批量增加
        /// </summary>
        /// <typeparam name="T">泛型</typeparam>
        /// <param name="model">实体</param>
        /// <param name="IsTrans">是否事务</param>
        /// <returns></returns>
        public WriteReturn AddList<T>(List<T> list, string key = null, bool IsTrans = false, bool isLog = true) where T : class, new()
        {
            ConfigModel config = null;
            var result = new DataReturn<T>();
            var stopwatch = new Stopwatch();

            stopwatch.Start();

            using (var tempDb = new DataContext(key))
            {
                result = tempDb.AddList<T>(list, IsTrans, isLog);
                config = tempDb.config;
            }

            stopwatch.Stop();

            DbLog.LogSql(config.IsOutSql, result.Sql, config.DbType, stopwatch.Elapsed.TotalMilliseconds);

            return result.WriteReturn;
        }

        /// <summary>
        /// 批量增加 asy
        /// </summary>
        public Task<WriteReturn> AddListAsync<T>(List<T> list, string key = null, bool IsTrans = false, bool isLog = true) where T : class, new()
        {
            return AsyncHelper.RunSyncAsAsync(() => AddList<T>(list, key, IsTrans, isLog));
        }

        /// <summary>
        /// 增加
        /// </summary>
        /// <typeparam name="T">泛型</typeparam>
        /// <param name="model">实体</param>
        /// <param name="IsTrans">是否事务</param>
        /// <param name="notAddField">不需要增加的字段</param>
        /// <returns></returns>
        public WriteReturn Add<T>(T model, DataContext db = null, string key = null, bool isOutSql = false) where T : class, new()
        {
            ConfigModel config = null;
            var result = new DataReturn<T>();
            var stopwatch = new Stopwatch();

            stopwatch.Start();

            if (db == null)
            {
                using (var tempDb = new DataContext(key))
                {
                    result = tempDb.Add<T>(model, false);
                    config = tempDb.config;
                }
            }
            else
            {
                result = db.Add<T>(model, false);
                config = db.config;
            }

            stopwatch.Stop();
            config.IsOutSql = config.IsOutSql || isOutSql;
            DbLog.LogSql(config.IsOutSql, result.Sql, config.DbType, stopwatch.Elapsed.TotalMilliseconds);
            return result.WriteReturn;
        }

        /// <summary>
        /// 增加 asy
        /// </summary>
        public Task<WriteReturn> AddAsync<T>(T model, DataContext db = null, string key = null, bool isOutSql = false) where T : class, new()
        {
            return AsyncHelper.RunSyncAsAsync(() => Add<T>(model, db, key, isOutSql));
        }

        /// <summary>
        /// 删除(Lambda表达式)
        /// </summary>
        /// <typeparam name="T">泛型</typeparam>
        /// <param name="predicate">表达式</param>
        /// <param name="IsTrans">是否事务</param>
        /// <returns></returns>
        public WriteReturn Delete<T>(Expression<Func<T, bool>> predicate, DataContext db = null, string key = null, bool isOutSql = false) where T : class, new()
        {
            ConfigModel config = null;
            var result = new DataReturn<T>();
            var stopwatch = new Stopwatch();

            stopwatch.Start();

            if (db == null)
            {
                using (var tempDb = new DataContext(key))
                {
                    result = tempDb.Delete<T>(predicate);
                    config = tempDb.config;
                }
            }
            else
            {
                result = db.Delete<T>(predicate);
                config = db.config;
            }

            stopwatch.Stop();
            config.IsOutSql = config.IsOutSql || isOutSql;
            DbLog.LogSql(config.IsOutSql, result.Sql, config.DbType, stopwatch.Elapsed.TotalMilliseconds);
            return result.WriteReturn;
        }

        /// <summary>
        /// 删除(Lambda表达式)asy
        /// </summary>
        /// <typeparam name="T">泛型</typeparam>
        /// <param name="predicate">表达式</param>
        /// <param name="IsTrans">是否事务</param>
        /// <returns></returns>
        public Task<WriteReturn> DeleteAsync<T>(Expression<Func<T, bool>> predicate, DataContext db = null, string key = null, bool isOutSql = false) where T : class, new()
        {
            return AsyncHelper.RunSyncAsAsync(() => Delete<T>(predicate, db, key, isOutSql));
        }

        /// <summary>
        /// 删除
        /// </summary>
        /// <param name="model"></param>
        /// <returns></returns>
        public WriteReturn Delete<T>(T model, DataContext db = null, string key = null, bool isTrans = false, bool isOutSql = false) where T : class, new()
        {
            ConfigModel config = null;
            var result = new DataReturn<T>();
            var stopwatch = new Stopwatch();

            stopwatch.Start();

            if (db == null)
            {
                using (var tempDb = new DataContext(key))
                {
                    result = tempDb.Delete(model, isTrans);
                    config = tempDb.config;
                }
            }
            else
            {
                result = db.Delete(model, isTrans);
                config = db.config;
            }

            stopwatch.Stop();
            config.IsOutSql = config.IsOutSql || isOutSql;
            DbLog.LogSql(config.IsOutSql, result.Sql, config.DbType, stopwatch.Elapsed.TotalMilliseconds);

            return result.WriteReturn;
        }

        /// <summary>
        /// 删除asy
        /// </summary>
        public Task<WriteReturn> UpdateAsync<T>(T model, DataContext db = null, string key = null, bool isTrans = false, bool isOutSql = false) where T : class, new()
        {
            return AsyncHelper.RunSyncAsAsync(() => Delete<T>(model, db, key, isOutSql));
        }

        /// <summary>
        /// 修改(Lambda表达式)
        /// </summary>
        /// <typeparam name="T">泛型</typeparam>
        /// <param name="model">实体</param>
        /// <param name="predicate">表达式</param>
        /// <param name="IsTrans">是否事务</param>
        /// <param name="field">需要修改的字段</param>
        /// <returns></returns>
        public WriteReturn Update<T>(T model, Expression<Func<T, bool>> predicate, Expression<Func<T, object>> field = null, DataContext db = null, string key = null, bool isOutSql = false) where T : class, new()
        {
            ConfigModel config = null;
            var result = new DataReturn<T>();
            var stopwatch = new Stopwatch();

            stopwatch.Start();

            if (db == null)
            {
                using (var tempDb = new DataContext(key))
                {
                    result = tempDb.Update<T>(model, predicate, field);
                    config = tempDb.config;
                }
            }
            else
            {
                result = db.Update<T>(model, predicate, field);
                config = db.config;
            }

            stopwatch.Stop();
            config.IsOutSql = config.IsOutSql || isOutSql;
            DbLog.LogSql(config.IsOutSql, result.Sql, config.DbType, stopwatch.Elapsed.TotalMilliseconds);

            return result.WriteReturn;
        }

        /// <summary>
        /// 修改(Lambda表达式)asy
        /// </summary>
        public Task<WriteReturn> UpdateAsync<T>(T model, Expression<Func<T, bool>> predicate, Expression<Func<T, object>> field = null, DataContext db = null, string key = null, bool isOutSql = false) where T : class, new()
        {
            return AsyncHelper.RunSyncAsAsync(() => Update<T>(model, predicate, field, db, key, isOutSql));
        }

        /// <summary>
        /// 修改
        /// </summary>
        /// <param name="model"></param>
        /// <returns></returns>
        public WriteReturn Update<T>(T model, Expression<Func<T, object>> field = null, DataContext db = null, string key = null, bool isTrans = false, bool isOutSql = false) where T : class, new()
        {
            ConfigModel config = null;
            var result = new DataReturn<T>();
            var stopwatch = new Stopwatch();

            stopwatch.Start();

            if (db == null)
            {
                using (var tempDb = new DataContext(key))
                {
                    result = tempDb.Update(model, field, isTrans);
                    config = tempDb.config;
                }
            }
            else
            {
                result = db.Update(model, field, isTrans);
                config = db.config;
            }

            stopwatch.Stop();
            config.IsOutSql = config.IsOutSql || isOutSql;
            DbLog.LogSql(config.IsOutSql, result.Sql, config.DbType, stopwatch.Elapsed.TotalMilliseconds);

            return result.WriteReturn;
        }

        /// <summary>
        /// 修改asy
        /// </summary>
        public Task<WriteReturn> UpdateAsync<T>(T model, Expression<Func<T, object>> field = null, DataContext db = null, string key = null, bool isTrans = false, bool isOutSql = false) where T : class, new()
        {
            return AsyncHelper.RunSyncAsAsync(() => Update<T>(model, field, db, key, isTrans, isOutSql));
        }

        /// <summary>
        /// 修改list
        /// </summary>
        /// <param name="model"></param>
        /// <returns></returns>
        public WriteReturn UpdateList<T>(List<T> list, Expression<Func<T, object>> field = null, DataContext db = null, string key = null, bool isOutSql = false) where T : class, new()
        {
            ConfigModel config = null;
            var result = new DataReturn<T>();
            var stopwatch = new Stopwatch();

            stopwatch.Start();

            if (db == null)
            {
                using (var tempDb = new DataContext(key))
                {
                    result = tempDb.UpdateList(list, field);
                    config = tempDb.config;
                }
            }
            else
            {
                result = db.UpdateList(list, field);
                config = db.config;
            }

            stopwatch.Stop();

            config.IsOutSql = config.IsOutSql || isOutSql;
            DbLog.LogSql(config.IsOutSql, result.Sql, config.DbType, stopwatch.Elapsed.TotalMilliseconds);

            return result.WriteReturn;
        }

        /// <summary>
        /// 修改list asy
        /// </summary>
        public Task<WriteReturn> UpdateListAsync<T>(List<T> list, Expression<Func<T, object>> field = null, DataContext db = null, string key = null, bool isOutSql = false) where T : class, new()
        {
            return AsyncHelper.RunSyncAsAsync(() => UpdateList<T>(list, field, db, key, isOutSql));
        }

        /// <summary>
        /// 执行sql
        /// </summary>
        /// <param name="sql"></param>
        /// <param name="param"></param>
        /// <returns></returns>
        public WriteReturn ExecuteSql(string sql, DbParameter[] param, DataContext db = null, string key = null, bool isOutSql = false)
        {
            ConfigModel config = null;
            var result = new DataReturn();
            var stopwatch = new Stopwatch();

            stopwatch.Start();

            if (db == null)
            {
                using (var tempDb = new DataContext(key))
                {
                    config = tempDb.config;
                    config.IsOutSql = config.IsOutSql || isOutSql;
                    result = tempDb.ExecuteSql(sql, param,false,config.IsOutSql);
                }
            }
            else
            {
                config = db.config;
                config.IsOutSql = config.IsOutSql || isOutSql;
                result = db.ExecuteSql(sql, param, false, config.IsOutSql);
            }

            stopwatch.Stop();

            DbLog.LogSql(config.IsOutSql, result.Sql, config.DbType, stopwatch.Elapsed.TotalMilliseconds);

            return result.WriteReturn;
        }

        /// <summary>
        /// 执行sql asy
        /// </summary>
        public Task<WriteReturn> ExecuteSqlAsync(string sql, DbParameter[] param, DataContext db = null, string key = null, bool isOutSql = false)
        {
            return AsyncHelper.RunSyncAsAsync(() => ExecuteSql(sql, param, db, key, isOutSql));
        }


        /// <summary>
        /// 表查询
        /// </summary>
        /// <typeparam name="T">泛型</typeparam>
        /// <param name="predicate">条件</param>
        /// <param name="field">字段</param>
        /// <param name="Key"></param>
        /// <returns></returns>
        public IQuery Query<T>(Expression<Func<T, bool>> predicate, Expression<Func<T, object>> field = null, string key = null, string dbFile = "db.config") where T : class
        {
            var projectName = Assembly.GetCallingAssembly().GetName().Name;
            if (DataConfig.DataType(key, projectName, dbFile) && key == null)
                throw new Exception("数据库查询key不能为空,数据库类型有多个");

            if (this.query.Data.Config != null && this.query.Data.Config.IsChangeDb)
            {
                key = this.query.Data.Key;
                this.query.Data = new DataQuery();
                this.query.Data.Config = DataConfig.GetConfig(key);
                this.query.Data.Key = key;
            }
            else
            {
                this.query.Data = new DataQuery();
                this.query.Data.Config = DataConfig.GetConfig(key);
                this.query.Data.Key = key;
            }

            var queryField = BaseField.QueryField<T>(predicate, field, query.Data.Config);
            // queryField.Field 是逗号分隔的字符串，需要分割成列表
            query.Data.Field.AddRange(queryField.Field.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries));
            query.Data.AsName.AddRange(queryField.AsName);

            var condtion = VisitExpression.LambdaWhere<T>(predicate, query.Data.Config);
            query.Data.Predicate.Add(condtion);
            query.Data.Table.Add(string.Format("{0} {1}", Base.TableNameHelper.GetTableName<T>(), predicate.Parameters[0].Name));

            return query;
        }

        /// <summary>
        /// 多种数据库类型切换
        /// </summary>
        /// <param name="key"></param>
        /// <returns></returns>
        public IFastRepository SetKey(string key)
        {
            query.Data.Config = DataConfig.GetConfig(key);
            query.Data.Key = key;
            query.Data.Config.IsChangeDb = true;
            return this;
        }



        /// <summary>
        /// Aop Map Before
        /// </summary>
        /// <param name="sql"></param>
        /// <param name="name"></param>
        /// <param name="param"></param>
        /// <param name="config"></param>
        private void AopMapBefore(string mapName, string sql, DbParameter[] param, ConfigModel config, AopType type)
        {
            if (FastMap.fastAop != null)
            {
                var context = new MapBeforeContext();
                context.mapName = mapName;
                context.sql = sql;
                context.type = type;

                if (param != null)
                    context.param = param.ToList();

                context.dbType = config.DbType;

                FastMap.fastAop.MapBefore(context);
            }
        }

        /// <summary>
        /// Aop Map After
        /// </summary>
        /// <param name="sql"></param>
        /// <param name="name"></param>
        /// <param name="param"></param>
        /// <param name="config"></param>
        private void AopMapAfter(string mapName, string sql, DbParameter[] param, ConfigModel config, AopType type, object data)
        {
            if (FastMap.fastAop != null)
            {
                var context = new MapAfterContext();
                context.mapName = mapName;
                context.sql = sql;
                context.type = type;

                if (param != null)
                    context.param = param.ToList();

                context.dbType = config.DbType;
                context.result = data;

                FastMap.fastAop.MapAfter(context);
            }
        }
    }
}
