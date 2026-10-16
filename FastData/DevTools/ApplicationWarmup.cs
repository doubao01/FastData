using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using FastData.Base;
using FastData.Context;
using FastData.Model;

namespace FastData.DevTools
{
    /// <summary>
    /// 应用启动预热工具
    /// 用于预热 ORM 反射缓存和数据库主键缓存，减少首次访问延迟
    /// </summary>
    public static class ApplicationWarmup
    {
        /// <summary>
        /// 预热配置
        /// </summary>
        public class WarmupConfig
        {
            /// <summary>
            /// 需要预热的实体类型列表
            /// </summary>
            public List<Type> EntityTypes { get; set; } = new List<Type>();

            /// <summary>
            /// 需要预热的数据库配置 Key
            /// </summary>
            public string DbKey { get; set; }

            /// <summary>
            /// 是否预热 PropertyCache
            /// </summary>
            public bool WarmupPropertyCache { get; set; } = true;

            /// <summary>
            /// 是否预热主键缓存
            /// </summary>
            public bool WarmupPrimaryKeyCache { get; set; } = true;

            /// <summary>
            /// 是否预热表元数据缓存
            /// </summary>
            public bool WarmupTableMetadata { get; set; } = false;

            /// <summary>
            /// 是否预热表达式解析缓存
            /// </summary>
            public bool WarmupExpressionCache { get; set; } = true;
        }

        /// <summary>
        /// 预热结果
        /// </summary>
        public class WarmupResult
        {
            /// <summary>
            /// 预热流程是否执行成功。
            /// </summary>
            public bool IsSuccess { get; set; }

            /// <summary>
            /// 已预热属性缓存的实体类型数量。
            /// </summary>
            public int CachedEntityCount { get; set; }

            /// <summary>
            /// 已预热主键缓存的表数量。
            /// </summary>
            public int CachedPrimaryKeyCount { get; set; }

            /// <summary>
            /// 预热总耗时。
            /// </summary>
            public TimeSpan Duration { get; set; }

            /// <summary>
            /// 预热过程中的提示和警告消息。
            /// </summary>
            public List<string> Messages { get; set; } = new List<string>();
        }

        /// <summary>
        /// 执行预热
        /// </summary>
        /// <param name="config">预热配置</param>
        /// <returns>预热结果</returns>
        public static WarmupResult Execute(WarmupConfig config)
        {
            var result = new WarmupResult();
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();

            try
            {
                // 预热 PropertyCache
                if (config.WarmupPropertyCache && config.EntityTypes.Count > 0)
                {
                    foreach (var entityType in config.EntityTypes)
                    {
                        try
                        {
                            // 调用 GetPropertiesCached(Type) 触发反射缓存
                            var method = typeof(Property.PropertyCache).GetMethod("GetPropertiesCached", 
                                BindingFlags.Public | BindingFlags.Static, 
                                null, new[] { typeof(Type) }, null);
                            if (method != null)
                            {
                                method.Invoke(null, new object[] { entityType });
                            }

                            // 调用 GetNonIdentityProperties(Type) 触发缓存
                            var nonIdentityMethod = typeof(Property.PropertyCache).GetMethod("GetNonIdentityProperties",
                                BindingFlags.Public | BindingFlags.Static,
                                null, new[] { typeof(Type) }, null);
                            if (nonIdentityMethod != null)
                            {
                                nonIdentityMethod.Invoke(null, new object[] { entityType });
                            }

                            result.CachedEntityCount++;
                        }
                        catch (Exception ex)
                        {
                            result.Messages.Add($"警告：预热 {entityType.Name} 属性缓存失败 - {ex.Message}");
                        }
                    }
                    result.Messages.Add($"已预热 PropertyCache: {result.CachedEntityCount} 个实体类型");
                }

                // 预热主键缓存
                if (config.WarmupPrimaryKeyCache && config.EntityTypes.Count > 0)
                {
                    var dbKey = config.DbKey;
                    if (string.IsNullOrEmpty(dbKey))
                    {
                        result.Messages.Add("跳过主键缓存预热：未指定数据库配置 Key");
                    }
                    else
                    {
                        using (var db = new DataContext(dbKey))
                        {
                            foreach (var entityType in config.EntityTypes)
                            {
                                try
                                {
                                    var tableName = GetTableName(entityType, db.config);
                                    var primaryKeys = BaseModel.GetPrimaryKeys(db.config, db.cmd, tableName);
                                    if (primaryKeys.Count > 0)
                                        result.CachedPrimaryKeyCount++;
                                }
                                catch (Exception ex)
                                {
                                    result.Messages.Add($"警告：预热 {entityType.Name} 主键失败 - {ex.Message}");
                                }
                            }
                        }
                        result.Messages.Add($"已预热主键缓存：{result.CachedPrimaryKeyCount} 个表");
                    }
                }

                // 预热表达式解析器的静态缓存初始化
                if (config.WarmupExpressionCache && config.EntityTypes.Count > 0)
                {
                    try
                    {
                        System.Runtime.CompilerServices.RuntimeHelpers.RunClassConstructor(typeof(VisitExpression).TypeHandle);
                        result.Messages.Add($"已预热表达式解析缓存");
                    }
                    catch (Exception ex)
                    {
                        result.Messages.Add($"警告：预热表达式缓存失败 - {ex.Message}");
                    }
                }

                result.IsSuccess = true;
            }
            catch (Exception ex)
            {
                result.IsSuccess = false;
                result.Messages.Add($"预热失败：{ex.Message}");
            }
            finally
            {
                stopwatch.Stop();
                result.Duration = stopwatch.Elapsed;
            }

            result.Messages.Add($"预热总耗时：{result.Duration.TotalMilliseconds}ms");
            return result;
        }

        /// <summary>
        /// 快速预热指定实体类型
        /// </summary>
        /// <typeparam name="T">实体类型</typeparam>
        /// <param name="dbKey">数据库 Key（可选）</param>
        /// <returns>预热结果</returns>
        public static WarmupResult WarmupType<T>(string dbKey = null) where T : class, new()
        {
            return Execute(new WarmupConfig
            {
                EntityTypes = { typeof(T) },
                DbKey = dbKey,
                WarmupPropertyCache = true,
                WarmupPrimaryKeyCache = true
            });
        }

        /// <summary>
        /// 批量预热多个实体类型
        /// </summary>
        /// <param name="entityTypes">实体类型列表</param>
        /// <param name="dbKey">数据库 Key（可选）</param>
        /// <returns>预热结果</returns>
        public static WarmupResult WarmupTypes(List<Type> entityTypes, string dbKey = null)
        {
            return Execute(new WarmupConfig
            {
                EntityTypes = entityTypes,
                DbKey = dbKey,
                WarmupPropertyCache = true,
                WarmupPrimaryKeyCache = true
            });
        }

        private static string GetTableName(Type entityType, ConfigModel config)
        {
            return TableNameHelper.GetTableName(entityType, config);
        }
    }
}
