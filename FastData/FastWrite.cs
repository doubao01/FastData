using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using System.Data;
using System.Data.Common;
using FastData.Base;
using FastData.Model;
using FastData.Repository;
using FastData.Property;
using System.Diagnostics;
using FastData.Context;
using FastData.DbTypes;
using System.Reflection;
#if !NETFRAMEWORK
using FastData.Queue;
#endif

namespace FastData
{
    /// <summary>
    /// FastData 写入操作（静态方法）
    /// 
    /// 职责：
    /// 1. 单条/批量数据添加（Add / AddList）
    /// 2. 数据更新（Update / UpdateList）
    /// 3. 数据删除（Delete）
    /// 4. 高性能批量插入（BulkInsert，使用 SqlBulkCopy 等）
    /// 5. 原生 SQL 写入（ExecuteSql）
    /// 6. CodeFirst 建表（根据实体类特性自动创建表结构）
    /// </summary>
    public static class FastWrite
    {
        /// <summary>
        /// Creates a write API instance bound to a database key.
        /// </summary>
        /// <param name="key">FastData connection key.</param>
        /// <returns>A key-bound write API.</returns>
        public static FastWriteDb Use(string key)
        {
            return new FastWriteDb(key);
        }

#if !NETFRAMEWORK
        /// <summary>
        /// 创建链式写入构建器（带消息队列支持）
        /// 支持 Fluent API：FastWrite.QueueBuilder().Add(user).Add(user2).Execute()
        /// </summary>
        /// <param name="databaseKey">数据库 Key（可选）</param>
        /// <returns>链式构建器</returns>
        public static FastWriteQueueBuilder QueueBuilder(string databaseKey = null)
        {
            return new FastWriteQueueBuilder(databaseKey);
        }

        /// <summary>
        /// 配置表级别的消息队列
        /// </summary>
        /// <typeparam name="T">实体类型</typeparam>
        /// <param name="config">队列配置</param>
        public static void ConfigureQueue<T>(WriteBehindConfig config) where T : class
        {
            WriteBehindRegistry.Register<T>(config);
        }

        /// <summary>
        /// 配置表级别的消息队列
        /// </summary>
        /// <param name="tableName">表名</param>
        /// <param name="config">队列配置</param>
        public static void ConfigureQueue(string tableName, WriteBehindConfig config)
        {
            WriteBehindRegistry.Register(tableName, config);
        }

        /// <summary>
        /// 检查表是否启用了消息队列
        /// </summary>
        /// <typeparam name="T">实体类型</typeparam>
        /// <returns>是否启用队列</returns>
        public static bool IsQueueEnabled<T>() where T : class
        {
            return WriteBehindRegistry.IsQueueEnabled<T>();
        }

        /// <summary>
        /// 检查表是否启用了消息队列
        /// </summary>
        /// <param name="tableName">表名</param>
        /// <returns>是否启用队列</returns>
        public static bool IsQueueEnabled(string tableName)
        {
            return WriteBehindRegistry.IsQueueEnabled(tableName);
        }
#endif

        #region 异步写入操作模板

        /// <summary>
        /// 异步写入操作核心模板
        /// </summary>
        private static async Task<WriteReturn> ExecuteWriteTemplateAsync<T>(
            string key, DataContext db, bool isOutSql,
            Func<DataContext, Task<DataReturn<T>>> execute) where T : class, new()
        {
            var config = db?.config;
            DataReturn<T> result;
            var stopwatch = Stopwatch.StartNew();

            if (db != null)
            {
                result = await execute(db).ConfigureAwait(false);
            }
            else
            {
                using (var tempDb = new DataContext(key, FastDb.GetProjectName()))
                {
                    config = tempDb.config;
                    result = await execute(tempDb).ConfigureAwait(false);
                }
            }

            stopwatch.Stop();

            if (config != null)
            {
                config.IsOutSql |= isOutSql;
                DbLog.LogSql(config.IsOutSql, result.Sql, config.DbType, stopwatch.Elapsed.TotalMilliseconds);
            }

            return result.WriteReturn;
        }

        #endregion

        #region 写入操作模板（消除重复代码）

        /// <summary>
        /// 泛型写入操作模板 - 封装 DataContext 生命周期、计时和日志记录
        /// </summary>
        private static WriteReturn ExecuteWriteTemplate<T>(
            string key, DataContext db, bool isOutSql,
            Func<DataContext, DataReturn<T>> execute) where T : class, new()
        {
            return ExecuteWriteTemplateCore(
                key ?? FastDb.CurrentKey, db, isOutSql,
                ctx => execute(ctx),
                r => r.WriteReturn,
                r => r.Sql);
        }

        /// <summary>
        /// 写入操作模板（非泛型版本，用于 ExecuteSql 等返回 DataReturn 的方法）
        /// </summary>
        private static WriteReturn ExecuteWriteTemplate(
            string key, DataContext db, bool isOutSql,
            Func<DataContext, DataReturn> execute)
        {
            return ExecuteWriteTemplateCore(
                key ?? FastDb.CurrentKey, db, isOutSql,
                ctx => execute(ctx),
                r => r.WriteReturn,
                r => r.Sql);
        }

        /// <summary>
        /// 写入操作核心模板 - 封装 DataContext 生命周期、计时和日志记录
        /// </summary>
        private static WriteReturn ExecuteWriteTemplateCore<TResult>(
            string key, DataContext db, bool isOutSql,
            Func<DataContext, TResult> execute,
            Func<TResult, WriteReturn> resultSelector,
            Func<TResult, string> sqlSelector)
        {
            var config = db?.config;
            TResult result;
            var stopwatch = Stopwatch.StartNew();

            if (db != null)
            {
                result = execute(db);
            }
            else
            {
                using (var tempDb = new DataContext(key, FastDb.GetProjectName()))
                {
                    config = tempDb.config;
                    result = execute(tempDb);
                }
            }

            stopwatch.Stop();

            if (config != null)
            {
                config.IsOutSql |= isOutSql;
                DbLog.LogSql(config.IsOutSql, sqlSelector(result), config.DbType, stopwatch.Elapsed.TotalMilliseconds);
            }

            return resultSelector(result);
        }

        #endregion

        #region 批量增加
        /// <summary>
        /// 批量增加
        /// </summary>
        public static WriteReturn AddList<T>(List<T> list, string key = null, bool IsTrans = false, bool isLog = true) where T : class, new()
        {
            return ExecuteWriteTemplate<T>(key, null, false, ctx => ctx.AddList<T>(list, IsTrans, isLog));
        }
        #endregion

        #region 批量增加 异步
        /// <summary>
        /// Adds a list of entities asynchronously.
        /// </summary>
        /// <typeparam name="T">Entity type.</typeparam>
        /// <param name="list">Entities to add.</param>
        /// <param name="key">Optional FastData connection key.</param>
        /// <param name="IsTrans">Whether to use a transaction.</param>
        /// <param name="isLog">Whether to log write details.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Write result.</returns>
        public static async Task<WriteReturn> AddListAsy<T>(List<T> list, string key = null, bool IsTrans = false, bool isLog = true, CancellationToken cancellationToken = default) where T : class, new()
        {
            return await ExecuteWriteTemplateAsync<T>(key, null, false,
                async (ctx) => await ctx.AddListAsync<T>(list, IsTrans, isLog, cancellationToken).ConfigureAwait(false)).ConfigureAwait(false);
        }
        #endregion

        #region 增加
        /// <summary>
        /// 增加单条记录
        /// </summary>
        public static WriteReturn Add<T>(T model, DataContext db = null, string key = null, bool isOutSql = false) where T : class, new()
        {
            // 审计字段自动填充
            if (FastData.Config.FastDataOptions.Audit.Enabled)
                AutoFillAuditFields(model, isUpdate: false);

            return ExecuteWriteTemplate<T>(key, db, isOutSql, ctx => ctx.Add<T>(model, false));
        }
        #endregion

        #region 增加 异步
        /// <summary>
        /// Adds one entity asynchronously.
        /// </summary>
        /// <typeparam name="T">Entity type.</typeparam>
        /// <param name="model">Entity to add.</param>
        /// <param name="db">Optional existing data context.</param>
        /// <param name="key">Optional FastData connection key.</param>
        /// <param name="isOutSql">Whether to output SQL logging.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Write result.</returns>
        public static async Task<WriteReturn> AddAsy<T>(T model, DataContext db = null, string key = null, bool isOutSql = false, CancellationToken cancellationToken = default) where T : class, new()
        {
            if (FastData.Config.FastDataOptions.Audit.Enabled)
                AutoFillAuditFields(model, isUpdate: false);

            return await ExecuteWriteTemplateAsync<T>(key, db, isOutSql,
                async (ctx) => await ctx.AddAsync<T>(model, false, cancellationToken: cancellationToken).ConfigureAwait(false)).ConfigureAwait(false);
        }
        #endregion

        #region 删除(Lambda表达式)
        /// <summary>
        /// 根据Lambda表达式条件删除记录
        /// </summary>
        public static WriteReturn Delete<T>(Expression<Func<T, bool>> predicate, DataContext db = null, string key = null, bool isOutSql = false) where T : class, new()
        {
            // 软删除支持
            if (Config.FastDataOptions.SoftDelete.Enabled)
                return SoftDelete<T>(predicate, db, key, isOutSql);

            return ExecuteWriteTemplate<T>(key, db, isOutSql, ctx => ctx.Delete<T>(predicate));
        }

        /// <summary>
        /// 软删除实现
        /// </summary>
        private static WriteReturn SoftDelete<T>(Expression<Func<T, bool>> predicate, DataContext db = null, string key = null, bool isOutSql = false) where T : class, new()
        {
            key = key ?? FastDb.CurrentKey;
            try
            {
                var deleteProperty = FastData.Config.FastDataOptions.SoftDelete.PropertyName;
                var property = typeof(T).GetProperty(deleteProperty);
                if (property == null) throw new Exception(string.Format("字段{0}不存在", deleteProperty));

                var parameter = Expression.Parameter(typeof(T), "x");
                var updateField = Expression.Lambda<Func<T, object>>(
                    Expression.Convert(Expression.Property(parameter, property), typeof(object)), parameter);

                var example = new T();
                var deletedValue = Config.FastDataOptions.SoftDelete.DeletedValue;
                if (deletedValue != null && !property.PropertyType.IsInstanceOfType(deletedValue))
                {
                    var targetType = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
                    deletedValue = Convert.ChangeType(deletedValue, targetType);
                }
                property.SetValue(example, deletedValue, null);

                return ExecuteWriteTemplate<T>(key, db, isOutSql, ctx => ctx.Update(example, predicate, updateField));
            }
            catch (Exception ex)
            {
                DbLog.LogException<T>(true, DataDbType.SqlServer, ex, "SoftDelete", "");
                return new WriteReturn { IsSuccess = false, Message = ex.Message };
            }
        }
        #endregion

        #region 删除(Lambda表达式)异步
        /// <summary>
        /// Deletes entities matching a predicate asynchronously.
        /// </summary>
        /// <typeparam name="T">Entity type.</typeparam>
        /// <param name="predicate">Delete predicate.</param>
        /// <param name="db">Optional existing data context.</param>
        /// <param name="key">Optional FastData connection key.</param>
        /// <param name="isOutSql">Whether to output SQL logging.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Write result.</returns>
        public static async Task<WriteReturn> DeleteAsy<T>(Expression<Func<T, bool>> predicate, DataContext db = null, string key = null, bool isOutSql = false, CancellationToken cancellationToken = default) where T : class, new()
        {
            if (Config.FastDataOptions.SoftDelete.Enabled)
                return await ExecuteWriteTemplateAsync<T>(key, db, isOutSql,
                    async (ctx) => await SoftDeleteAsync<T>(predicate, ctx, cancellationToken).ConfigureAwait(false)).ConfigureAwait(false);

            return await ExecuteWriteTemplateAsync<T>(key, db, isOutSql,
                async (ctx) => await ctx.DeleteAsync<T>(predicate, cancellationToken: cancellationToken).ConfigureAwait(false)).ConfigureAwait(false);
        }

        /// <summary>
        /// 异步软删除实现
        /// </summary>
        private static async Task<DataReturn<T>> SoftDeleteAsync<T>(Expression<Func<T, bool>> predicate, DataContext db, CancellationToken cancellationToken = default) where T : class, new()
        {
            try
            {
                var deleteProperty = FastData.Config.FastDataOptions.SoftDelete.PropertyName;
                var property = typeof(T).GetProperty(deleteProperty);
                if (property == null) throw new Exception(string.Format("字段{0}不存在", deleteProperty));

                var parameter = Expression.Parameter(typeof(T), "x");
                var updateField = Expression.Lambda<Func<T, object>>(
                    Expression.Convert(Expression.Property(parameter, property), typeof(object)), parameter);

                var example = new T();
                var deletedValue = Config.FastDataOptions.SoftDelete.DeletedValue;
                if (deletedValue != null && !property.PropertyType.IsInstanceOfType(deletedValue))
                {
                    var targetType = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
                    deletedValue = Convert.ChangeType(deletedValue, targetType);
                }
                property.SetValue(example, deletedValue, null);

                return await db.UpdateAsync<T>(example, predicate, updateField, cancellationToken: cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                DbLog.LogException<T>(true, DataDbType.SqlServer, ex, "SoftDeleteAsync", "");
                return new DataReturn<T> { WriteReturn = new WriteReturn { IsSuccess = false, Message = ex.Message } };
            }
        }
        #endregion

        #region 删除
        /// <summary>
        /// 根据实体对象删除记录
        /// </summary>
        public static WriteReturn Delete<T>(T model, DataContext db = null, string key = null, bool isTrans = false, bool isOutSql = false) where T : class, new()
        {
            return ExecuteWriteTemplate<T>(key, db, isOutSql, ctx => ctx.Delete(model, isTrans));
        }
        #endregion

        #region 删除 异步
        /// <summary>
        /// Deletes an entity asynchronously.
        /// </summary>
        /// <typeparam name="T">Entity type.</typeparam>
        /// <param name="model">Entity identifying the row to delete.</param>
        /// <param name="db">Optional existing data context.</param>
        /// <param name="key">Optional FastData connection key.</param>
        /// <param name="isTrans">Whether to use a transaction.</param>
        /// <param name="isOutSql">Whether to output SQL logging.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Write result.</returns>
        public static async Task<WriteReturn> DeleteAsy<T>(T model, DataContext db = null, string key = null, bool isTrans = false, bool isOutSql = false, CancellationToken cancellationToken = default) where T : class, new()
        {
            return await ExecuteWriteTemplateAsync<T>(key, db, isOutSql,
                async (ctx) => await ctx.DeleteAsync<T>(model, isTrans, cancellationToken).ConfigureAwait(false)).ConfigureAwait(false);
        }
        #endregion

        #region 修改(Lambda表达式)
        /// <summary>
        /// 根据Lambda表达式条件更新记录
        /// </summary>
        public static WriteReturn Update<T>(T model, Expression<Func<T, bool>> predicate, Expression<Func<T, object>> field = null, DataContext db = null, string key = null, bool isOutSql = false) where T : class, new()
        {
            // 审计字段自动填充
            if (FastData.Config.FastDataOptions.Audit.Enabled)
                AutoFillAuditFields(model, isUpdate: true);

            return ExecuteWriteTemplate<T>(key, db, isOutSql, ctx => ctx.Update<T>(model, predicate, field));
        }
        #endregion

        #region 修改(Lambda表达式)异步
        /// <summary>
        /// Updates entities matching a predicate asynchronously.
        /// </summary>
        /// <typeparam name="T">Entity type.</typeparam>
        /// <param name="model">Entity containing updated values.</param>
        /// <param name="predicate">Update predicate.</param>
        /// <param name="field">Optional fields to update.</param>
        /// <param name="db">Optional existing data context.</param>
        /// <param name="key">Optional FastData connection key.</param>
        /// <param name="isOutSql">Whether to output SQL logging.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Write result.</returns>
        public static async Task<WriteReturn> UpdateAsy<T>(T model, Expression<Func<T, bool>> predicate, Expression<Func<T, object>> field = null, DataContext db = null, string key = null, bool isOutSql = false, CancellationToken cancellationToken = default) where T : class, new()
        {
            if (FastData.Config.FastDataOptions.Audit.Enabled)
                AutoFillAuditFields(model, isUpdate: true);

            return await ExecuteWriteTemplateAsync<T>(key, db, isOutSql,
                async (ctx) => await ctx.UpdateAsync<T>(model, predicate, field, cancellationToken: cancellationToken).ConfigureAwait(false)).ConfigureAwait(false);
        }
        #endregion

        #region 修改
        /// <summary>
        /// 根据实体主键更新记录
        /// </summary>
        public static WriteReturn Update<T>(T model, Expression<Func<T, object>> field = null, DataContext db = null, string key = null, bool isOutSql = false) where T : class, new()
        {
            return ExecuteWriteTemplate<T>(key, db, isOutSql, ctx => ctx.Update(model, field));
        }
        #endregion

        #region 修改 异步
        /// <summary>
        /// Updates an entity by primary key asynchronously.
        /// </summary>
        /// <typeparam name="T">Entity type.</typeparam>
        /// <param name="model">Entity containing key and updated values.</param>
        /// <param name="field">Optional fields to update.</param>
        /// <param name="db">Optional existing data context.</param>
        /// <param name="key">Optional FastData connection key.</param>
        /// <param name="isOutSql">Whether to output SQL logging.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Write result.</returns>
        public static async Task<WriteReturn> UpdateAsy<T>(T model, Expression<Func<T, object>> field = null, DataContext db = null, string key = null, bool isOutSql = false, CancellationToken cancellationToken = default) where T : class, new()
        {
            return await ExecuteWriteTemplateAsync<T>(key, db, isOutSql,
                async (ctx) => await ctx.UpdateAsync<T>(model, field, cancellationToken: cancellationToken).ConfigureAwait(false)).ConfigureAwait(false);
        }
        #endregion

        #region 修改列表
        /// <summary>
        /// 批量更新实体列表
        /// </summary>
        public static WriteReturn UpdateList<T>(List<T> list, Expression<Func<T, object>> field = null, DataContext db = null, string key = null, bool isOutSql = false) where T : class, new()
        {
            return ExecuteWriteTemplate<T>(key, db, isOutSql, ctx => ctx.UpdateList(list, field));
        }
        #endregion

        #region 修改列表 异步
        /// <summary>
        /// Updates a list of entities asynchronously.
        /// </summary>
        /// <typeparam name="T">Entity type.</typeparam>
        /// <param name="list">Entities to update.</param>
        /// <param name="field">Optional fields to update.</param>
        /// <param name="db">Optional existing data context.</param>
        /// <param name="key">Optional FastData connection key.</param>
        /// <param name="isOutSql">Whether to output SQL logging.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Write result.</returns>
        public static async Task<WriteReturn> UpdateListAsy<T>(List<T> list, Expression<Func<T, object>> field = null, DataContext db = null, string key = null, bool isOutSql = false, CancellationToken cancellationToken = default) where T : class, new()
        {
            return await ExecuteWriteTemplateAsync<T>(key, db, isOutSql,
                async (ctx) => await Task.FromResult(ctx.UpdateList(list, field)).ConfigureAwait(false)).ConfigureAwait(false);
        }
        #endregion

        #region 执行sql
        /// <summary>
        /// 执行sql
        /// </summary>
        public static WriteReturn ExecuteSql(string sql, DbParameter[] param, DataContext db = null, string key = null, bool isOutSql = false)
        {
            return ExecuteWriteTemplate(key, db, isOutSql, ctx =>
            {
                ctx.config.IsOutSql = ctx.config.IsOutSql || isOutSql;
                return ctx.ExecuteSql(sql, param, false, ctx.config.IsOutSql);
            });
        }
        #endregion

        #region 执行sql 异步
        /// <summary>
        /// Executes a SQL write command asynchronously.
        /// </summary>
        /// <param name="sql">SQL text.</param>
        /// <param name="param">SQL parameters.</param>
        /// <param name="db">Optional existing data context.</param>
        /// <param name="key">Optional FastData connection key.</param>
        /// <param name="isOutSql">Whether to output SQL logging.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Write result.</returns>
        public static async Task<WriteReturn> ExecuteSqlAsync(string sql, DbParameter[] param, DataContext db = null, string key = null, bool isOutSql = false, CancellationToken cancellationToken = default)
        {
            return await ExecuteSqlWriteTemplateAsync(key, db, isOutSql, async ctx =>
            {
                ctx.config.IsOutSql = ctx.config.IsOutSql || isOutSql;
                return await ctx.ExecuteSqlAsync(sql, param, isTrans: false, isAop: false, cancellationToken: cancellationToken).ConfigureAwait(false);
            }).ConfigureAwait(false);
        }

        private static async Task<WriteReturn> ExecuteSqlWriteTemplateAsync(
            string key, DataContext db, bool isOutSql,
            Func<DataContext, Task<DataReturn>> execute)
        {
            var config = db?.config;
            DataReturn result;
            var stopwatch = Stopwatch.StartNew();

            if (db != null)
            {
                result = await execute(db).ConfigureAwait(false);
            }
            else
            {
                using (var tempDb = new DataContext(key ?? FastDb.CurrentKey, FastDb.GetProjectName()))
                {
                    config = tempDb.config;
                    result = await execute(tempDb).ConfigureAwait(false);
                }
            }

            stopwatch.Stop();

            if (config != null)
            {
                config.IsOutSql |= isOutSql;
                DbLog.LogSql(config.IsOutSql, result.Sql, config.DbType, stopwatch.Elapsed.TotalMilliseconds);
            }

            return result.WriteReturn;
        }
        #endregion

        #region 批量插入
        /// <summary>
        /// 批量插入（高性能）
        /// </summary>
        public static WriteReturn BulkInsert<T>(List<T> list, DataContext db = null, string key = null) where T : class, new()
        {
            if (list == null || list.Count == 0)
                return new WriteReturn { IsSuccess = true };

            return BulkExecute(db, key, ctx => ctx.BulkInsert(list, ctx.config.IsOutSql));
        }

        /// <summary>
        /// 批量插入异步（高性能）
        /// </summary>
        public static async Task<WriteReturn> BulkInsertAsync<T>(List<T> list, DataContext db = null, string key = null, CancellationToken cancellationToken = default) where T : class, new()
        {
            return await BulkExecuteAsync(db, key, async ctx => await ctx.BulkInsertAsync(list, cancellationToken: cancellationToken).ConfigureAwait(false)).ConfigureAwait(false);
        }
        #endregion

        #region Code First

        /// <summary>
        /// 批量操作执行模板 - 封装 DataContext 生命周期、异常处理和日志记录
        /// </summary>
        private static WriteReturn BulkExecute(DataContext db, string key, Func<DataContext, DataReturn> execute)
        {
            key = key ?? FastDb.CurrentKey;
            var config = db?.config;
            DataReturn result;
            var stopwatch = Stopwatch.StartNew();

            try
            {
                if (db != null)
                {
                    result = execute(db);
                }
                else
                {
                    using (var tempDb = new DataContext(key, FastDb.GetProjectName()))
                    {
                        config = tempDb.config;
                        result = execute(tempDb);
                    }
                }
            }
            catch (Exception ex)
            {
                DbLog.LogException(config?.IsOutSql ?? false, config?.DbType ?? DataDbType.SqlServer, ex, "BulkExecute", "");
                result = new DataReturn { WriteReturn = new WriteReturn { IsSuccess = false, Message = ex.Message } };
            }

            stopwatch.Stop();
            DbLog.LogSql(config?.IsOutSql ?? false, result.Sql, config?.DbType ?? DataDbType.SqlServer, stopwatch.Elapsed.TotalMilliseconds);

            return result.WriteReturn;
        }

        /// <summary>
        /// 异步批量操作执行模板
        /// </summary>
        private static async Task<WriteReturn> BulkExecuteAsync(DataContext db, string key, Func<DataContext, Task<DataReturn>> execute)
        {
            key = key ?? FastDb.CurrentKey;
            var config = db?.config;
            DataReturn result;
            var stopwatch = Stopwatch.StartNew();

            try
            {
                if (db != null)
                {
                    result = await execute(db).ConfigureAwait(false);
                }
                else
                {
                    using (var tempDb = new DataContext(key, FastDb.GetProjectName()))
                    {
                        config = tempDb.config;
                        result = await execute(tempDb).ConfigureAwait(false);
                    }
                }
            }
            catch (Exception ex)
            {
                DbLog.LogException(config?.IsOutSql ?? false, config?.DbType ?? DataDbType.SqlServer, ex, "BulkExecuteAsync", "");
                result = new DataReturn { WriteReturn = new WriteReturn { IsSuccess = false, Message = ex.Message } };
            }

            stopwatch.Stop();
            DbLog.LogSql(config?.IsOutSql ?? false, result.Sql, config?.DbType ?? DataDbType.SqlServer, stopwatch.Elapsed.TotalMilliseconds);

            return result.WriteReturn;
        }

        /// <summary>
        /// Code First：根据 Model 创建数据库表
        /// </summary>
        public static WriteReturn CodeFirst<T>(string key = null, bool isDropExists = false) where T : class, new()
        {
            try
            {
                var tableName = typeof(T).GetCustomAttributes(typeof(Property.TableAttribute), false)
                    .Cast<Property.TableAttribute>().FirstOrDefault()?.Name ?? typeof(T).Name;

                using (var db = new DataContext(key))
                {
                    var sql = BuildCreateTableSql<T>(tableName, db.config.DbType);

                    if (isDropExists)
                    {
                        try { db.ExecuteSql(BuildDropTableSql(tableName, db.config.DbType), null, false, false); }
                        catch (Exception dropEx) { DbLog.LogException(true, db.config.DbType, dropEx, "CodeFirst_DropTable", ""); }
                    }

                    db.ExecuteSql(sql, null, false, true);
                }
                return new WriteReturn { IsSuccess = true, Message = string.Format("Table {0} created", tableName) };
            }
            catch (Exception ex)
            {
                DbLog.LogException(true, DataDbType.SqlServer, ex, "CodeFirst", "");
                return new WriteReturn { IsSuccess = false, Message = ex.Message };
            }
        }

        private static string BuildCreateTableSql<T>(string tableName, DataDbType dbType) where T : class, new()
        {
            var cols = GetModelColumns<T>(dbType);
            var defs = new List<string>();

            foreach (var c in cols)
            {
                var def = string.Format("{0} {1}", QuoteIdentifier(c.Name, dbType), c.Type);
                if (!c.IsNull || c.IsPrimary) def += " NOT NULL";
                defs.Add(def);
            }

            var pks = cols.Where(x => x.IsPrimary).Select(x => x.Name).ToList();
            if (pks.Count > 0) defs.Add(string.Format("PRIMARY KEY ({0})", string.Join(", ", pks.Select(n => QuoteIdentifier(n, dbType)))));

            return string.Format("CREATE TABLE {0} ({1})", QuoteIdentifier(tableName, dbType), string.Join(", ", defs));
        }

        private static string BuildDropTableSql(string tableName, DataDbType dbType)
        {
            return string.Format("DROP TABLE {0}", QuoteIdentifier(tableName, dbType));
        }

        private static string QuoteIdentifier(string name, DataDbType dbType)
        {
            switch (dbType)
            {
                case DataDbType.MySql:
                    return string.Format("`{0}`", name);
                case DataDbType.PostgreSql:
                case DataDbType.Oracle:
                case DataDbType.SQLite:
                    return string.Format("\"{0}\"", name);
                default:
                    return string.Format("[{0}]", name);
            }
        }

        private static List<(string Name, string Type, bool IsPrimary, bool IsIdentity, bool IsNull)> GetModelColumns<T>(DataDbType dbType) where T : class, new()
        {
            var list = new List<(string, string, bool, bool, bool)>();
            foreach (var p in PropertyCache.GetPropertiesCached<T>())
            {
                var col = p.GetCustomAttribute<Property.ColumnAttribute>();
                var pri = p.GetCustomAttribute<Property.PrimaryAttribute>() != null;
                var isIdentity = col?.IsIdentity ?? false;
                var type = GetClrSqlType(p.PropertyType, col?.Length ?? 0, dbType, pri && isIdentity);
                list.Add((p.Name, type, pri, col?.IsIdentity ?? false, col?.IsNull ?? true));
            }
            return list;
        }

        private static string GetClrSqlType(Type t, int len, DataDbType dbType, bool isIdentity)
        {
            t = Nullable.GetUnderlyingType(t) ?? t;

            if (isIdentity)
            {
                if (dbType == DataDbType.PostgreSql) return t == typeof(long) ? "BIGSERIAL" : "SERIAL";
                if (dbType == DataDbType.SQLite) return "INTEGER";
                if (dbType == DataDbType.MySql) return t == typeof(long) ? "BIGINT AUTO_INCREMENT" : "INT AUTO_INCREMENT";
                if (dbType == DataDbType.Oracle) return t == typeof(long) ? "NUMBER(19) GENERATED BY DEFAULT AS IDENTITY" : "NUMBER(10) GENERATED BY DEFAULT AS IDENTITY";
                return t == typeof(long) ? "BIGINT IDENTITY(1,1)" : "INT IDENTITY(1,1)";
            }

            if (t == typeof(string))
            {
                if (len > 0) return string.Format("VARCHAR({0})", len);
                return dbType == DataDbType.SqlServer ? "VARCHAR(MAX)" : "TEXT";
            }
            if (t == typeof(int)) return dbType == DataDbType.Oracle ? "NUMBER(10)" : "INT";
            if (t == typeof(long)) return dbType == DataDbType.Oracle ? "NUMBER(19)" : "BIGINT";
            if (t == typeof(decimal) || t == typeof(double)) return "DECIMAL(18,2)";
            if (t == typeof(bool)) return dbType == DataDbType.PostgreSql || dbType == DataDbType.SQLite ? "BOOLEAN" : dbType == DataDbType.Oracle ? "NUMBER(1)" : "BIT";
            if (t == typeof(DateTime)) return dbType == DataDbType.PostgreSql || dbType == DataDbType.Oracle ? "TIMESTAMP" : "DATETIME";
            if (t == typeof(Guid)) return dbType == DataDbType.PostgreSql ? "UUID" : dbType == DataDbType.SqlServer ? "UNIQUEIDENTIFIER" : "CHAR(36)";
            return dbType == DataDbType.SqlServer ? "NVARCHAR(MAX)" : "TEXT";
        }

        /// <summary>
        /// Creates a table for the entity asynchronously using Code First metadata.
        /// </summary>
        /// <typeparam name="T">Entity type.</typeparam>
        /// <param name="key">Optional FastData connection key.</param>
        /// <param name="isDropExists">Whether to drop an existing table first.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Write result.</returns>
        public static async Task<WriteReturn> CodeFirstAsync<T>(string key = null, bool isDropExists = false, CancellationToken cancellationToken = default) where T : class, new()
        {
            return await Task.Run(() => CodeFirst<T>(key, isDropExists), cancellationToken).ConfigureAwait(false);
        }


        /// <summary>
        /// 自动填充审计字段
        /// </summary>
        private static void AutoFillAuditFields<T>(T model, bool isUpdate) where T : class, new()
        {
            var audit = FastData.Config.FastDataOptions.Audit;
            var now = DateTime.Now;
            var currentUser = audit.GetCurrentUser?.Invoke() ?? "System";

            if (!isUpdate)
                SetIfWritable(model, audit.CreatedTimeProperty, now);

            if (!isUpdate)
                SetIfWritable(model, audit.CreatedByProperty, currentUser);

            SetIfWritable(model, audit.UpdatedTimeProperty, now);
            SetIfWritable(model, audit.UpdatedByProperty, currentUser);
        }

        /// <summary>
        /// 如果属性可写则设置值
        /// </summary>
        private static void SetIfWritable<T>(T model, string propertyName, object value) where T : class, new()
        {
            var prop = typeof(T).GetProperty(propertyName);
            if (prop != null && prop.CanWrite)
                prop.SetValue(model, value);
        }
        #endregion

        #region SqlBulkCopy 辅助方法

        /// <summary>
        /// 获取实体可读写属性列表（过滤自增列）
        /// </summary>
        private static IEnumerable<PropertyInfo> GetBulkCopyProperties<T>(bool includeIdentity) where T : class, new()
        {
            return PropertyCache.GetPropertiesCached<T>()
                .Where(p => p.CanRead && p.CanWrite && (includeIdentity || !IsIdentityProperty(p)));
        }

        private static bool IsIdentityProperty(System.Reflection.PropertyInfo prop)
        {
            return prop.GetCustomAttributes(typeof(Property.ColumnAttribute), false)
                .FirstOrDefault() is Property.ColumnAttribute attr && attr.IsIdentity;
        }

        /// <summary>
        /// 创建用于 SqlBulkCopy 的 DataTable，自动包含实体所有属性对应的列
        /// </summary>
        public static DataTable CreateBulkCopyDataTable<T>(bool includeIdentity = true) where T : class, new()
        {
            var dataTable = new DataTable();
            foreach (var prop in GetBulkCopyProperties<T>(includeIdentity))
            {
                dataTable.Columns.Add(prop.Name, Nullable.GetUnderlyingType(prop.PropertyType) ?? prop.PropertyType);
            }
            return dataTable;
        }

        /// <summary>
        /// 将实体列表填充到 DataTable（用于 SqlBulkCopy）
        /// </summary>
        public static void FillBulkCopyDataTable<T>(List<T> list, DataTable dataTable, bool includeIdentity = true) where T : class, new()
        {
            if (list == null || list.Count == 0)
                return;

            var properties = GetBulkCopyProperties<T>(includeIdentity).ToList();
            foreach (var item in list)
            {
                var row = dataTable.NewRow();
                foreach (var prop in properties)
                {
                    if (dataTable.Columns.Contains(prop.Name))
                        row[prop.Name] = prop.GetValue(item) ?? DBNull.Value;
                }
                dataTable.Rows.Add(row);
            }
        }

        #endregion

        #region 批量更新/删除

        /// <summary>
        /// 批量更新实体（使用 SQL UPDATE ... WHERE IN）
        /// </summary>
        public static WriteReturn BulkUpdate<T>(List<T> list, Expression<Func<T, bool>> predicate, DataContext db = null, string key = null) where T : class, new()
        {
            if (list == null || list.Count == 0)
                return new WriteReturn { IsSuccess = true };

            return BulkExecute(db, key, ctx => ctx.BulkUpdate(list, predicate, ctx.config.IsOutSql));
        }

        /// <summary>
        /// 批量删除实体（使用 SQL DELETE FROM ... WHERE IN）
        /// </summary>
        public static WriteReturn BulkDelete<T>(Expression<Func<T, bool>> predicate, DataContext db = null, string key = null) where T : class, new()
        {
            if (predicate == null)
                throw new ArgumentNullException(nameof(predicate), "删除条件不能为空");

            return BulkExecute(db, key, ctx => ctx.BulkDelete(predicate, ctx.config.IsOutSql));
        }

        /// <summary>
        /// 批量更新/删除异步
        /// </summary>
        public static async Task<WriteReturn> BulkUpdateAsync<T>(List<T> list, Expression<Func<T, bool>> predicate, DataContext db = null, string key = null, CancellationToken cancellationToken = default) where T : class, new()
        {
            return await BulkExecuteAsync(db, key, async ctx => await ctx.BulkUpdateAsync(list, predicate, cancellationToken: cancellationToken).ConfigureAwait(false)).ConfigureAwait(false);
        }

        /// <summary>
        /// Deletes entities in bulk asynchronously.
        /// </summary>
        /// <typeparam name="T">Entity type.</typeparam>
        /// <param name="predicate">Delete predicate.</param>
        /// <param name="db">Optional existing data context.</param>
        /// <param name="key">Optional FastData connection key.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Write result.</returns>
        public static async Task<WriteReturn> BulkDeleteAsync<T>(Expression<Func<T, bool>> predicate, DataContext db = null, string key = null, CancellationToken cancellationToken = default) where T : class, new()
        {
            return await BulkExecuteAsync(db, key, async ctx => await ctx.BulkDeleteAsync(predicate, cancellationToken: cancellationToken).ConfigureAwait(false)).ConfigureAwait(false);
        }

        #endregion
    }
}
