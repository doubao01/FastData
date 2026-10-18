using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using System.Data.Common;
using FastUntility.Page;
using FastData.Base;
using FastData.Config;
using FastData.Model;
using FastData.Repository;
using System.Diagnostics;
using FastData.Context;
using System.Data;
#if !NETFRAMEWORK
using FastData.Queue;
#endif

namespace FastData
{
    /// <summary>
    /// FastData 读取操作（静态方法）
    /// 
    /// 职责：
    /// 1. LINQ 查询构建（Query / Where / And / Or / Like / In / Between）
    /// 2. 多表联查（LeftJoin / RightJoin / InnerJoin）
    /// 3. 查询结果转换（ToList / ToItem / ToCount / ToPage / ToJson）
    /// 4. 原生 SQL 查询（ExecuteSql）
    /// 5. 延迟加载查询（ToLazyList / ToLazyItem）
    /// </summary>
    public static class FastRead
    {
        /// <summary>
        /// Creates a read API instance bound to a database key.
        /// </summary>
        /// <param name="key">FastData connection key.</param>
        /// <returns>A key-bound read API.</returns>
        public static FastReadDb Use(string key)
        {
            return new FastReadDb(key);
        }

#if !NETFRAMEWORK
        /// <summary>
        /// 创建链式读取构建器（带消息队列支持）
        /// </summary>
        public static FastReadQueueBuilder<T> QueueBuilder<T>(string databaseKey = null) where T : class, new()
        {
            return new FastReadQueueBuilder<T>(databaseKey);
        }

        /// <summary>
        /// 配置表级别的消息队列（泛型版本）
        /// </summary>
        /// <typeparam name="T">实体类型</typeparam>
        /// <param name="config">队列配置</param>
        public static void ConfigureQueue<T>(WriteBehindConfig config) where T : class
        {
            FastWrite.ConfigureQueue<T>(config);
        }

        /// <summary>
        /// 配置表级别的消息队列（表名版本）
        /// </summary>
        /// <param name="tableName">表名</param>
        /// <param name="config">队列配置</param>
        public static void ConfigureQueue(string tableName, WriteBehindConfig config)
        {
            FastWrite.ConfigureQueue(tableName, config);
        }

        /// <summary>
        /// 检查表是否启用了消息队列（泛型版本）
        /// </summary>
        /// <typeparam name="T">实体类型</typeparam>
        /// <returns>是否启用队列</returns>
        public static bool IsQueueEnabled<T>() where T : class
        {
            return FastWrite.IsQueueEnabled<T>();
        }

        /// <summary>
        /// 检查表是否启用了消息队列（表名版本）
        /// </summary>
        /// <param name="tableName">表名</param>
        /// <returns>是否启用队列</returns>
        public static bool IsQueueEnabled(string tableName)
        {
            return FastWrite.IsQueueEnabled(tableName);
        }
#endif

        /// <summary>
        /// 内部 Join 查询
        /// </summary>
        private static DataQuery JoinType<T, T1>(string joinType, DataQuery item, Expression<Func<T, T1, bool>> predicate, Expression<Func<T1, object>> field = null, bool isDblink = false) where T : class where T1 : class
        {
            var queryField = BaseField.QueryField<T, T1>(predicate, field, item.Config);
            // queryField.Field 是逗号分隔的字符串，需要分割成列表
            item.Field.AddRange(queryField.Field.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries));
            item.AsName.AddRange(queryField.AsName);

            var condtion = VisitExpression.LambdaWhere<T, T1>(predicate, item.Config);
            item.Predicate.Add(condtion);
            item.Table.Add(string.Format("{2} {0}{3} {1}", typeof(T1).Name, predicate.Parameters[1].Name
            , joinType, isDblink && !string.IsNullOrEmpty(item.Config.DbLinkName) ? string.Format("@", item.Config.DbLinkName) : ""));

            return item;
        }

        /// <summary>
        /// 创建表查询
        /// </summary>
        public static DataQuery<T> Query<T>(Expression<Func<T, bool>> predicate, Expression<Func<T, object>> field = null, string key = null, string dbFile = "db.config") where T : class, new()
        {
            key = key ?? FastDb.CurrentKey;
            var projectName = FastDb.GetProjectName();
            var result = new DataQuery<T>();
            result.Config = DataConfig.GetConfig(key, projectName, dbFile);
            result.Key = key;

            var queryField = BaseField.QueryField<T>(predicate, field, result.Config);
            // queryField.Field 是逗号分隔的字符串，需要分割成列表
            result.Field.AddRange(queryField.Field.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries));
            result.AsName.AddRange(queryField.AsName);

            var condtion = VisitExpression.LambdaWhere<T>(predicate, result.Config);
            result.Predicate.Add(condtion);
            result.Table.Add(string.Format("{0} {1}", TableNameHelper.GetTableName<T>(result.Config), predicate.Parameters[0].Name));

            return result;
        }

        /// <summary>
        /// 投影查询（支持匿名类型）
        /// 用法：FastRead.Query&lt;User&gt;(u => u.IsActive).Select(p => new { p.Id, p.UserName }).ToList()
        /// </summary>
        /// <typeparam name="T">源实体类型</typeparam>
        /// <typeparam name="TResult">投影结果类型（可以是匿名类型）</typeparam>
        /// <param name="query">查询条件</param>
        /// <param name="selector">投影表达式</param>
        /// <returns>投影查询对象</returns>
        public static ProjectedQuery<T, TResult> Select<T, TResult>(this DataQuery query, Expression<Func<T, TResult>> selector) where T : class, new()
        {
            return new ProjectedQuery<T, TResult>(query, selector);
        }

        /// <summary>链式追加 WHERE 条件（AND）</summary>
        public static DataQuery Where<T>(this DataQuery query, Expression<Func<T, bool>> predicate) where T : class, new()
        {
            if (predicate == null)
                return query;

            query.EntityType = typeof(T);
            var visitModel = VisitExpression.LambdaWhere<T>(predicate, query.Config);
            if (visitModel.IsSuccess)
            {
                query.ChainedConditions.Add(new ChainedCondition
                {
                    Operator = "AND",
                    Where = visitModel.Where,
                    Param = visitModel.Param
                });
            }

            return query;
        }

        /// <summary>
        /// 链式追加 WHERE 条件（AND）- And 别名
        /// </summary>
        public static DataQuery And<T>(this DataQuery query, Expression<Func<T, bool>> predicate) where T : class, new()
        {
            return Where<T>(query, predicate);
        }

        /// <summary>链式追加 WHERE 条件（OR）</summary>
        public static DataQuery Or<T>(this DataQuery query, Expression<Func<T, bool>> predicate) where T : class, new()
        {
            if (predicate == null)
                return query;

            query.EntityType = typeof(T);
            var visitModel = VisitExpression.LambdaWhere<T>(predicate, query.Config);
            if (visitModel.IsSuccess)
            {
                query.ChainedConditions.Add(new ChainedCondition
                {
                    Operator = "OR",
                    Where = visitModel.Where,
                    Param = visitModel.Param
                });
            }

            return query;
        }

        /// <summary>
        /// 链式追加 LIKE 条件（参数化查询，天然防 SQL 注入）
        /// </summary>
        public static DataQuery Like<T>(this DataQuery query, Expression<Func<T, object>> field, string value) where T : class, new()
        {
            if (field == null || string.IsNullOrEmpty(value))
                return query;
            return AppendLikeCondition(query, ConditionOperator.Like, field, value);
        }

        /// <summary>
        /// 链式追加 NOT LIKE 条件
        /// </summary>
        public static DataQuery NotLike<T>(this DataQuery query, Expression<Func<T, object>> field, string value) where T : class, new()
        {
            if (field == null || string.IsNullOrEmpty(value))
                return query;
            return AppendLikeCondition(query, ConditionOperator.NotLike, field, value);
        }

        /// <summary>
        /// 链式追加 LIKE 条件（包含 - LIKE '%value%'）
        /// </summary>
        public static DataQuery Contains<T>(this DataQuery query, Expression<Func<T, object>> field, string value) where T : class, new()
        {
            if (field == null || value == null)
                return query;
            return AppendLikeCondition(query, ConditionOperator.Contains, field, value);
        }

        /// <summary>
        /// 链式追加 LIKE 条件（开头 - LIKE 'value%'）
        /// </summary>
        public static DataQuery StartsWith<T>(this DataQuery query, Expression<Func<T, object>> field, string value) where T : class, new()
        {
            if (field == null || value == null)
                return query;
            return AppendLikeCondition(query, ConditionOperator.StartsWith, field, value);
        }

        /// <summary>
        /// 链式追加 LIKE 条件（结尾 - LIKE '%value'）
        /// </summary>
        public static DataQuery EndsWith<T>(this DataQuery query, Expression<Func<T, object>> field, string value) where T : class, new()
        {
            if (field == null || value == null)
                return query;
            return AppendLikeCondition(query, ConditionOperator.EndsWith, field, value);
        }

        private static DataQuery AppendLikeCondition<T>(DataQuery query, ConditionOperator op, Expression<Func<T, object>> field, string value) where T : class, new()
        {
            query.EntityType = typeof(T);
            AppendChainedCondition(query, op, GetMemberName(field), value);
            return query;
        }

        /// <summary>
        /// 链式追加 IN 条件（参数化查询，天然防 SQL 注入）
        /// </summary>
        public static DataQuery In<T>(this DataQuery query, Expression<Func<T, object>> field, IEnumerable<object> values) where T : class, new()
        {
            if (field == null || values == null)
                return query;

            query.EntityType = typeof(T);
            var list = new List<object>(values);
            if (list.Count == 0)
                return query;
            AppendChainedCondition(query, ConditionOperator.In, GetMemberName(field), list);
            return query;
        }

        /// <summary>
        /// 链式追加 NOT IN 条件（参数化查询）
        /// </summary>
        public static DataQuery NotIn<T>(this DataQuery query, Expression<Func<T, object>> field, IEnumerable<object> values) where T : class, new()
        {
            if (field == null || values == null)
                return query;

            query.EntityType = typeof(T);
            var list = new List<object>(values);
            if (list.Count == 0)
                return query;
            AppendChainedCondition(query, ConditionOperator.NotIn, GetMemberName(field), list);
            return query;
        }

        /// <summary>
        /// 链式追加 BETWEEN 条件（参数化查询）
        /// 用法：FastRead.Query&lt;User&gt;().Between&lt;User&gt;(u => u.Age, 18, 65)
        /// </summary>
        public static DataQuery Between<T>(this DataQuery query, Expression<Func<T, object>> field, object start, object end) where T : class, new()
        {
            if (field == null)
                return query;

            query.EntityType = typeof(T);
            var fieldName = GetMemberName(field);
            // 包装为 List<object> 以保证 AppendBetween 能通过 IEnumerable 路径获取两个边界值
            AppendChainedCondition(query, ConditionOperator.Between, fieldName, new List<object> { start, end });
            return query;
        }

        /// <summary>
        /// 链式追加 NOT BETWEEN 条件
        /// </summary>
        public static DataQuery NotBetween<T>(this DataQuery query, Expression<Func<T, object>> field, object start, object end) where T : class, new()
        {
            if (field == null)
                return query;

            query.EntityType = typeof(T);
            var fieldName = GetMemberName(field);
            AppendChainedCondition(query, ConditionOperator.NotBetween, fieldName, new List<object> { start, end });
            return query;
        }

        /// <summary>链式追加 IS NULL 条件</summary>
        public static DataQuery IsNull<T>(this DataQuery query, Expression<Func<T, object>> field) where T : class, new()
        {
            if (field == null) return query;
            return AppendValueCondition(query, ConditionOperator.IsNull, field, null);
        }

        /// <summary>链式追加 IS NOT NULL 条件</summary>
        public static DataQuery IsNotNull<T>(this DataQuery query, Expression<Func<T, object>> field) where T : class, new()
        {
            if (field == null) return query;
            return AppendValueCondition(query, ConditionOperator.IsNotNull, field, null);
        }

        /// <summary>链式追加大于条件</summary>
        public static DataQuery GreaterThan<T>(this DataQuery query, Expression<Func<T, object>> field, object value) where T : class, new()
        {
            if (field == null) return query;
            return AppendValueCondition(query, ConditionOperator.GreaterThan, field, value);
        }

        /// <summary>链式追加大于等于条件</summary>
        public static DataQuery GreaterThanOrEqual<T>(this DataQuery query, Expression<Func<T, object>> field, object value) where T : class, new()
        {
            if (field == null) return query;
            return AppendValueCondition(query, ConditionOperator.GreaterThanOrEqual, field, value);
        }

        /// <summary>链式追加小于条件</summary>
        public static DataQuery LessThan<T>(this DataQuery query, Expression<Func<T, object>> field, object value) where T : class, new()
        {
            if (field == null) return query;
            return AppendValueCondition(query, ConditionOperator.LessThan, field, value);
        }

        /// <summary>链式追加小于等于条件</summary>
        public static DataQuery LessThanOrEqual<T>(this DataQuery query, Expression<Func<T, object>> field, object value) where T : class, new()
        {
            if (field == null) return query;
            return AppendValueCondition(query, ConditionOperator.LessThanOrEqual, field, value);
        }

        /// <summary>链式追加等于条件</summary>
        public static DataQuery Equal<T>(this DataQuery query, Expression<Func<T, object>> field, object value) where T : class, new()
        {
            if (field == null) return query;
            return AppendValueCondition(query, ConditionOperator.Equal, field, value);
        }

        /// <summary>链式追加不等于条件</summary>
        public static DataQuery NotEqual<T>(this DataQuery query, Expression<Func<T, object>> field, object value) where T : class, new()
        {
            if (field == null) return query;
            return AppendValueCondition(query, ConditionOperator.NotEqual, field, value);
        }

        private static DataQuery AppendValueCondition<T>(DataQuery query, ConditionOperator op, Expression<Func<T, object>> field, object value) where T : class, new()
        {
            query.EntityType = typeof(T);
            AppendChainedCondition(query, op, GetMemberName(field), value);
            return query;
        }

        /// <summary>
        /// 内部统一追加链式条件：使用新 <see cref="FastData.Base.Condition"/> 机制，参数化安全
        /// </summary>
        private static void AppendChainedCondition(DataQuery query, ConditionOperator op, string fieldName, object value)
        {
            query.ChainedConditions.Add(new ChainedCondition
            {
                Operator = "AND",
                Where = string.Empty,
                Param = new List<DbParameter>(),
                Conditions = new List<FastData.Base.Condition>
                {
                    new FastData.Base.Condition(fieldName, op, value, ConditionLogic.And)
                }
            });
        }

        /// <summary>
        /// 将 <see cref="ConditionBuilder"/> 的条件一次性追加到查询的链式条件列表
        /// </summary>
        public static DataQuery Where(this DataQuery query, ConditionBuilder builder)
        {
            if (query == null) throw new ArgumentNullException(nameof(query));
            if (builder == null || builder.Count == 0) return query;

            builder.AppendTo(query.ChainedConditions);
            return query;
        }

        private static DbParameter GetDbParameter(DataQuery query, string name, object value)
        {
            var dbParam = DbProviderFactories.GetFactory(query.Config.ProviderName).CreateParameter();
            dbParam.ParameterName = name;
            dbParam.Value = value ?? DBNull.Value;
            return dbParam;
        }

        /// <summary>从表达式中获取成员名称</summary>
        private static string GetMemberName<T>(Expression<Func<T, object>> expression)
        {
            if (expression.Body is MemberExpression member)
                return member.Member.Name;

            if (expression.Body is UnaryExpression unary && unary.Operand is MemberExpression unaryMember)
                return unaryMember.Member.Name;

            throw new ArgumentException("表达式必须是成员访问表达式");
        }

        /// <summary>Left Join 查询</summary>
        public static DataQuery LeftJoin<T, T1>(this DataQuery item, Expression<Func<T, T1, bool>> predicate, Expression<Func<T1, object>> field = null, bool isDblink = false) where T : class where T1 : class
        {
            return JoinType("left join", item, predicate, field);
        }

        /// <summary>Right Join 查询</summary>
        public static DataQuery RightJoin<T, T1>(this DataQuery item, Expression<Func<T, T1, bool>> predicate, Expression<Func<T1, object>> field = null, bool isDblink = false) where T : class where T1 : class
        {
            return JoinType("right join", item, predicate, field);
        }

        /// <summary>Inner Join 查询</summary>
        public static DataQuery InnerJoin<T, T1>(this DataQuery item, Expression<Func<T, T1, bool>> predicate, Expression<Func<T1, object>> field = null, bool isDblink = false) where T : class where T1 : class
        {
            return JoinType("inner join", item, predicate, field);
        }

        /// <summary>排序</summary>
        public static DataQuery OrderBy<T>(this DataQuery item, Expression<Func<T, object>> field, bool isDesc = true)
        {
            var orderBy = BaseField.OrderBy<T>(field, item.Config, isDesc);
            item.OrderBy.AddRange(orderBy);
            return item;
        }

        /// <summary>分组</summary>
        public static DataQuery GroupBy<T>(this DataQuery item, Expression<Func<T, object>> field)
        {
            var groupBy = BaseField.GroupBy<T>(field, item.Config);
            item.GroupBy.AddRange(groupBy);
            return item;
        }

        /// <summary>限制返回记录数</summary>
        public static DataQuery Take(this DataQuery item, int i)
        {
            item.Take = i;
            return item;
        }


        /// <summary>返回实体列表</summary>
        public static List<T> ToList<T>(this DataQuery item, DataContext db = null, bool isOutSql = false) where T : class, new()
        {
            return ExecuteQueryTemplate<List<T>, DataReturn<T>>(
                item, db, isOutSql,
                (ctx, q) => ctx.GetList<T>(q),
                () => item.Predicate.Exists(a => a.IsSuccess == false),
                r => r.List,
                r => r.Sql);
        }

        /// <summary>返回实体列表（异步）</summary>
        public static async Task<List<T>> ToListAsync<T>(this DataQuery item, DataContext db = null, bool isOutSql = false, CancellationToken cancellationToken = default) where T : class, new()
        {
            return await ExecuteQueryTemplateAsync<List<T>, DataReturn<T>>(
                item, db, isOutSql,
                async (ctx, q, ct) => await ctx.GetListAsync<T>(q, ct).ConfigureAwait(false),
                () => item.Predicate.Exists(a => a.IsSuccess == false),
                r => r.List,
                r => r.Sql,
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }

        /// <summary>返回实体列表（延迟加载）</summary>
        public static Lazy<List<T>> ToLazyList<T>(this DataQuery item, DataContext db = null, bool isOutSql = false) where T : class, new()
        {
            return new Lazy<List<T>>(() => ToList<T>(item, db, isOutSql));
        }


        /// <summary>返回 JSON 字符串</summary>
        public static string ToJson(this DataQuery item, DataContext db = null, bool isOutSql = false)
        {
            return ExecuteQueryTemplate<string, DataReturn>(
                item, db, isOutSql,
                (ctx, q) => ctx.GetJson(q),
                () => item.Predicate.Exists(a => a.IsSuccess == false),
                r => r.Json,
                r => r.Sql);
        }

        /// <summary>返回 JSON 字符串（异步）</summary>
        public static async Task<string> ToJsonAsync(this DataQuery item, DataContext db = null, bool isOutSql = false, CancellationToken cancellationToken = default)
        {
            return await ExecuteQueryTemplateAsync<string, DataReturn>(
                item, db, isOutSql,
                async (ctx, q, ct) => await ctx.GetJsonAsync(q, ct).ConfigureAwait(false),
                () => item.Predicate.Exists(a => a.IsSuccess == false),
                r => r.Json,
                r => r.Sql,
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }

        /// <summary>返回 JSON 字符串（延迟加载）</summary>
        public static Lazy<string> ToLazyJson(this DataQuery item, DataContext db = null, bool isOutSql = false)
        {
            return new Lazy<string>(() => ToJson(item, db, isOutSql));
        }


        /// <summary>返回单个实体</summary>
        public static T ToItem<T>(this DataQuery item, DataContext db = null, bool isOutSql = false) where T : class, new()
        {
            return ExecuteQueryTemplate<T, DataReturn<T>>(
                item, db, isOutSql,
                (ctx, q) => ctx.GetList<T>(q),
                () => item.Predicate.Exists(a => a.IsSuccess == false),
                r => r.Item,
                r => r.Sql,
                () => item.Take = 1);
        }

        /// <summary>返回单个实体（异步）</summary>
        public static async Task<T> ToItemAsync<T>(this DataQuery item, DataContext db = null, bool isOutSql = false, CancellationToken cancellationToken = default) where T : class, new()
        {
            return await ExecuteQueryTemplateAsync<T, DataReturn<T>>(
                item, db, isOutSql,
                async (ctx, q, ct) => await ctx.GetListAsync<T>(q, ct).ConfigureAwait(false),
                () => item.Predicate.Exists(a => a.IsSuccess == false),
                r => r.Item,
                r => r.Sql,
                () => item.Take = 1,
                cancellationToken).ConfigureAwait(false);
        }

        /// <summary>返回单个实体（延迟加载）</summary>
        public static Lazy<T> ToLazyItem<T>(this DataQuery item, DataContext db = null, bool isOutSql = false) where T : class, new()
        {
            return new Lazy<T>(() => ToItem<T>(item, db, isOutSql));
        }


        /// <summary>返回记录总数</summary>
        public static int ToCount(this DataQuery item, DataContext db = null, bool isOutSql = false)
        {
            return ExecuteQueryTemplate<int, DataReturn>(
                item, db, isOutSql,
                (ctx, q) => ctx.GetCount(q),
                () => item.Predicate.Exists(a => a.IsSuccess == false),
                r => r.Count,
                r => r.Sql);
        }

        /// <summary>返回记录总数（异步）</summary>
        public static async Task<int> ToCountAsync(this DataQuery item, DataContext db = null, bool isOutSql = false, CancellationToken cancellationToken = default)
        {
            return await ExecuteQueryTemplateAsync<int, DataReturn>(
                item, db, isOutSql,
                async (ctx, q, ct) => await ctx.GetCountAsync(q, ct).ConfigureAwait(false),
                () => item.Predicate.Exists(a => a.IsSuccess == false),
                r => r.Count,
                r => r.Sql,
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }


        /// <summary>返回分页结果（实体）</summary>
        public static PageResult<T> ToPage<T>(this DataQuery item, PageModel pModel, DataContext db = null, bool isOutSql = false) where T : class, new()
        {
            return ExecutePageTemplate<T, DataReturn<T>>(
                item, db, isOutSql,
                (ctx, q) => ctx.GetPage<T>(q, pModel),
                () => item.Predicate.Exists(a => a.IsSuccess == false),
                r => r.PageResult);
        }

        /// <summary>返回分页结果（实体，异步）</summary>
        public static async Task<PageResult<T>> ToPageAsync<T>(this DataQuery item, PageModel pModel, DataContext db = null, bool isOutSql = false, CancellationToken cancellationToken = default) where T : class, new()
        {
            return await ExecutePageTemplateAsync<T, DataReturn<T>>(
                item, db, isOutSql,
                async (ctx, q, ct) => await ctx.GetPageAsync<T>(q, pModel, ct).ConfigureAwait(false),
                () => item.Predicate.Exists(a => a.IsSuccess == false),
                r => r.PageResult,
                cancellationToken).ConfigureAwait(false);
        }

        /// <summary>返回分页结果（实体，延迟加载）</summary>
        public static Lazy<PageResult<T>> ToLazyPage<T>(this DataQuery item, PageModel pModel, DataContext db = null, bool isOutSql = false) where T : class, new()
        {
            return new Lazy<PageResult<T>>(() => ToPage<T>(item, pModel, db, isOutSql));
        }


        /// <summary>返回分页结果（字典）</summary>
        public static PageResult ToPage(this DataQuery item, PageModel pModel, DataContext db = null, bool isOutSql = false)
        {
            return ExecutePageTemplate<DataReturn>(
                item, db, isOutSql,
                (ctx, q) => ctx.GetPage(q, pModel),
                () => item.Predicate.Exists(a => a.IsSuccess == false),
                r => r.PageResult);
        }

        /// <summary>返回分页结果（字典，异步）</summary>
        public static async Task<PageResult> ToPageAsync(this DataQuery item, PageModel pModel, DataContext db = null, bool isOutSql = false, CancellationToken cancellationToken = default)
        {
            return await ExecutePageTemplateAsync<DataReturn>(
                item, db, isOutSql,
                async (ctx, q, ct) => await ctx.GetPageAsync(q, pModel, ct).ConfigureAwait(false),
                () => item.Predicate.Exists(a => a.IsSuccess == false),
                r => r.PageResult,
                cancellationToken).ConfigureAwait(false);
        }

        /// <summary>返回分页结果（字典，延迟加载）</summary>
        public static Lazy<PageResult> ToLazyPage(this DataQuery item, PageModel pModel, DataContext db = null, bool isOutSql = false)
        {
            return new Lazy<PageResult>(() => ToPage(item, pModel, db, isOutSql));
        }


        /// <summary>分页查询（简化API）返回格式：{ Total, TotalPages, Page, PageSize, Data }</summary>
        public static PaginationResult<T> ToPagination<T>(this DataQuery item, int page, int pageSize, DataContext db = null, bool isOutSql = false) where T : class, new()
        {
            page = page < 1 ? 1 : page;
            pageSize = pageSize < 1 ? 10 : pageSize;

            var pModel = new PageModel
            {
                PageId = page,
                PageSize = pageSize
            };

            var pageResult = ToPage<T>(item, pModel, db, isOutSql);
            var total = pageResult.pModel.TotalRecord;

            return new PaginationResult<T>
            {
                Total = total,
                TotalPages = (int)Math.Ceiling(total / (double)pageSize),
                Page = page,
                PageSize = pageSize,
                Data = pageResult.list
            };
        }

        /// <summary>分页查询（简化API，异步）</summary>
        public static async Task<PaginationResult<T>> ToPaginationAsync<T>(this DataQuery item, int page, int pageSize, DataContext db = null, bool isOutSql = false, CancellationToken cancellationToken = default) where T : class, new()
        {
            page = page < 1 ? 1 : page;
            pageSize = pageSize < 1 ? 10 : pageSize;

            var pModel = new PageModel
            {
                PageId = page,
                PageSize = pageSize
            };

            var pageResult = await ToPageAsync<T>(item, pModel, db, isOutSql, cancellationToken).ConfigureAwait(false);
            var total = pageResult.pModel.TotalRecord;

            return new PaginationResult<T>
            {
                Total = total,
                TotalPages = (int)Math.Ceiling(total / (double)pageSize),
                Page = page,
                PageSize = pageSize,
                Data = pageResult.list
            };
        }

        /// <summary>分页查询（使用 PaginationRequest）</summary>
        public static PaginationResult<T> ToPagination<T>(this DataQuery item, PaginationRequest request, DataContext db = null, bool isOutSql = false) where T : class, new()
        {
            return ToPagination<T>(item, request.Page, request.PageSize, db, isOutSql);
        }

        /// <summary>分页查询（使用 PaginationRequest，异步）</summary>
        public static Task<PaginationResult<T>> ToPaginationAsync<T>(this DataQuery item, PaginationRequest request, DataContext db = null, bool isOutSql = false, CancellationToken cancellationToken = default) where T : class, new()
        {
            return ToPaginationAsync<T>(item, request.Page, request.PageSize, db, isOutSql, cancellationToken);
        }

        /// <summary>分页查询（字典格式）</summary>
        public static PaginationResult ToPagination(this DataQuery item, int page, int pageSize, DataContext db = null, bool isOutSql = false)
        {
            page = page < 1 ? 1 : page;
            pageSize = pageSize < 1 ? 10 : pageSize;

            var pModel = new PageModel
            {
                PageId = page,
                PageSize = pageSize
            };

            var pageResult = ToPage(item, pModel, db, isOutSql);
            return PaginationResult.FromPageResult(pageResult, page, pageSize);
        }

        /// <summary>分页查询（字典格式，异步）</summary>
        public static async Task<PaginationResult> ToPaginationAsync(this DataQuery item, int page, int pageSize, DataContext db = null, bool isOutSql = false, CancellationToken cancellationToken = default)
        {
            page = page < 1 ? 1 : page;
            pageSize = pageSize < 1 ? 10 : pageSize;

            var pModel = new PageModel
            {
                PageId = page,
                PageSize = pageSize
            };

            var pageResult = await ToPageAsync(item, pModel, db, isOutSql, cancellationToken).ConfigureAwait(false);
            return PaginationResult.FromPageResult(pageResult, page, pageSize);
        }


        /// <summary>执行 SQL 查询（实体）</summary>
        public static List<T> ExecuteSql<T>(string sql, DbParameter[] param, DataContext db = null, string key = null, bool isOutSql = false) where T : class, new()
        {
            key = key ?? FastDb.CurrentKey;
            return ExecuteSqlTemplate<List<T>, DataReturn<T>>(
                key, db, isOutSql,
                (ctx) => ctx.ExecuteSql<T>(sql, param),
                r => r.List);
        }

        /// <summary>执行 SQL 查询（实体，异步）</summary>
        public static async Task<List<T>> ExecuteSqlAsync<T>(string sql, DbParameter[] param, DataContext db = null, string key = null, bool isOutSql = false) where T : class, new()
        {
            key = key ?? FastDb.CurrentKey;
            return await ExecuteSqlTemplateAsync<List<T>, DataReturn<T>>(
                key, db, isOutSql,
                async (ctx, ct) => await ctx.ExecuteSqlAsync<T>(sql, param, ct).ConfigureAwait(false),
                r => r.List).ConfigureAwait(false);
        }


        /// <summary>返回字典列表</summary>
        public static List<Dictionary<string, object>> ToDics(this DataQuery item, DataContext db = null, bool isOutSql = false)
        {
            return ExecuteQueryTemplate<List<Dictionary<string, object>>, DataReturn>(
                item, db, isOutSql,
                (ctx, q) => ctx.GetDic(q),
                () => item.Predicate.Exists(a => a.IsSuccess == false),
                r => r.DicList,
                r => r.Sql);
        }

        /// <summary>返回字典列表（异步）</summary>
        public static async Task<List<Dictionary<string, object>>> ToDicsAsync(this DataQuery item, DataContext db = null, bool isOutSql = false, CancellationToken cancellationToken = default)
        {
            return await ExecuteQueryTemplateAsync<List<Dictionary<string, object>>, DataReturn>(
                item, db, isOutSql,
                async (ctx, q, ct) => await ctx.GetDicAsync(q, ct).ConfigureAwait(false),
                () => item.Predicate.Exists(a => a.IsSuccess == false),
                r => r.DicList,
                r => r.Sql,
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }

        /// <summary>返回字典列表（延迟加载）</summary>
        public static Lazy<List<Dictionary<string, object>>> ToLazyDics(this DataQuery item, DataContext db = null, bool isOutSql = false)
        {
            return new Lazy<List<Dictionary<string, object>>>(() => ToDics(item, db, isOutSql));
        }


        /// <summary>返回单个字典</summary>
        public static Dictionary<string, object> ToDic(this DataQuery item, DataContext db = null, bool isOutSql = false)
        {
            return ExecuteQueryTemplate<Dictionary<string, object>, DataReturn>(
                item, db, isOutSql,
                (ctx, q) => ctx.GetDic(q),
                () => item.Predicate.Exists(a => a.IsSuccess == false),
                r => r.Dic,
                r => r.Sql,
                () => item.Take = 1);
        }

        /// <summary>返回单个字典（异步）</summary>
        public static async Task<Dictionary<string, object>> ToDicAsync(this DataQuery item, DataContext db = null, bool isOutSql = false, CancellationToken cancellationToken = default)
        {
            return await ExecuteQueryTemplateAsync<Dictionary<string, object>, DataReturn>(
                item, db, isOutSql,
                async (ctx, q, ct) => await ctx.GetDicAsync(q, ct).ConfigureAwait(false),
                () => item.Predicate.Exists(a => a.IsSuccess == false),
                r => r.Dic,
                r => r.Sql,
                () => item.Take = 1,
                cancellationToken).ConfigureAwait(false);
        }

        /// <summary>返回单个字典（延迟加载）</summary>
        public static Lazy<Dictionary<string, object>> ToLazyDic(this DataQuery item, DataContext db = null, bool isOutSql = false)
        {
            return new Lazy<Dictionary<string, object>>(() => ToDic(item, db, isOutSql));
        }


        /// <summary>返回 DataTable</summary>
        public static DataTable ToDataTable(this DataQuery item, DataContext db = null, bool isOutSql = false)
        {
            return ExecuteQueryTemplate<DataTable, DataReturn>(
                item, db, isOutSql,
                (ctx, q) => ctx.GetDataTable(q),
                () => item.Predicate.Exists(a => a.IsSuccess == false),
                r => r.Table,
                r => r.Sql);
        }

        /// <summary>返回 DataTable（异步）</summary>
        public static async Task<DataTable> ToDataTableAsync(this DataQuery item, DataContext db = null, bool isOutSql = false, CancellationToken cancellationToken = default)
        {
            return await ExecuteQueryTemplateAsync<DataTable, DataReturn>(
                item, db, isOutSql,
                async (ctx, q, ct) => await ctx.GetDataTableAsync(q, ct).ConfigureAwait(false),
                () => item.Predicate.Exists(a => a.IsSuccess == false),
                r => r.Table,
                r => r.Sql,
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }

        /// <summary>返回 DataTable（延迟加载）</summary>
        public static Lazy<DataTable> ToLazyDataTable(this DataQuery item, DataContext db = null, bool isOutSql = false)
        {
            return new Lazy<DataTable>(() => ToDataTable(item, db, isOutSql));
        }


        /// <summary>执行 SQL 查询（字典）</summary>
        public static List<Dictionary<string, object>> ExecuteSql(string sql, DbParameter[] param, DataContext db = null, string key = null, bool isOutSql = false)
        {
            key = key ?? FastDb.CurrentKey;
            return ExecuteSqlTemplate<List<Dictionary<string, object>>, DataReturn>(
                key, db, isOutSql,
                (ctx) => ctx.ExecuteSqlList(sql, param, false),
                r => r.DicList);
        }

        /// <summary>执行 SQL 查询（字典，异步）</summary>
        public static async Task<List<Dictionary<string, object>>> ExecuteSqlAsync(string sql, DbParameter[] param, DataContext db = null, string key = null, bool isOutSql = false)
        {
            key = key ?? FastDb.CurrentKey;
            return await ExecuteSqlTemplateAsync<List<Dictionary<string, object>>, DataReturn>(
                key, db, isOutSql,
                async (ctx, ct) => await ctx.ExecuteSqlListAsync(sql, param, false, false, ct).ConfigureAwait(false),
                r => r.DicList).ConfigureAwait(false);
        }

        #region 私有模板方法

        /// <summary>
        /// 异步查询执行模板
        /// </summary>
        private static async Task<TResult> ExecuteQueryTemplateAsync<TResult, TReturn>(
            DataQuery item,
            DataContext db,
            bool isOutSql,
            Func<DataContext, DataQuery, CancellationToken, Task<TReturn>> execute,
            Func<bool> predicateFailed,
            Func<TReturn, TResult> defaultResult,
            Func<TReturn, string> getSql,
            Action preExecute = null,
            CancellationToken cancellationToken = default)
        {
            if (predicateFailed())
                return default(TResult);

            preExecute?.Invoke();

            var stopwatch = new Stopwatch();
            TReturn result;

            stopwatch.Start();

            if (db == null)
            {
                using (var tempDb = new DataContext(item.Key))
                {
                    result = await execute(tempDb, item, cancellationToken).ConfigureAwait(false);
                }
            }
            else
                result = await execute(db, item, cancellationToken).ConfigureAwait(false);

            stopwatch.Stop();

            var shouldLog = item.IsSqlLogEnabled || FastDb.EnableSqlLog || item.Config.IsOutSql || isOutSql;
            DbLog.LogSql(shouldLog, getSql(result), item.Config.DbType, stopwatch.Elapsed.TotalMilliseconds);

            if (result == null)
                return default(TResult);

            return defaultResult(result);
        }

        /// <summary>
        /// 异步原生SQL执行模板
        /// </summary>
        private static async Task<TResult> ExecuteSqlTemplateAsync<TResult, TReturn>(
            string key, DataContext db, bool isOutSql,
            Func<DataContext, CancellationToken, Task<TReturn>> execute,
            Func<TReturn, TResult> resultSelector,
            CancellationToken cancellationToken = default)
            where TReturn : class
        {
            ConfigModel config = null;
            TReturn result;
            var stopwatch = new Stopwatch();

            stopwatch.Start();

            if (db == null)
            {
                using (var tempDb = new DataContext(key))
                {
                    result = await execute(tempDb, cancellationToken).ConfigureAwait(false);
                    config = tempDb.config;
                }
            }
            else
            {
                result = await execute(db, cancellationToken).ConfigureAwait(false);
                config = db.config;
            }

            stopwatch.Stop();

            config.IsOutSql = config.IsOutSql || isOutSql;
            DbLog.LogSql(config.IsOutSql, GetSqlFromResult(result), config.DbType, stopwatch.Elapsed.TotalMilliseconds);

            if (result == null)
                return default(TResult);

            return resultSelector(result);
        }

        /// <summary>
        /// 异步分页查询执行模板
        /// </summary>
        private static async Task<PageResult> ExecutePageTemplateAsync<TReturn>(
            DataQuery item, DataContext db, bool isOutSql,
            Func<DataContext, DataQuery, CancellationToken, Task<TReturn>> execute,
            Func<bool> predicateFailed,
            Func<TReturn, PageResult> resultSelector,
            CancellationToken cancellationToken = default)
        {
            if (predicateFailed())
                return new PageResult();

            var stopwatch = new Stopwatch();
            TReturn result;

            stopwatch.Start();

            if (db == null)
            {
                using (var tempDb = new DataContext(item.Key))
                {
                    result = await execute(tempDb, item, cancellationToken).ConfigureAwait(false);
                }
            }
            else
                result = await execute(db, item, cancellationToken).ConfigureAwait(false);

            stopwatch.Stop();

            var shouldLog = item.IsSqlLogEnabled || FastDb.EnableSqlLog || item.Config.IsOutSql || isOutSql;
            DbLog.LogSql(shouldLog, GetSqlFromResult(result), item.Config.DbType, stopwatch.Elapsed.TotalMilliseconds);

            if (result == null)
                return new PageResult();

            return resultSelector(result);
        }

        /// <summary>
        /// 异步分页查询执行模板（泛型版本）
        /// </summary>
        private static async Task<PageResult<T>> ExecutePageTemplateAsync<T, TReturn>(
            DataQuery item, DataContext db, bool isOutSql,
            Func<DataContext, DataQuery, CancellationToken, Task<TReturn>> execute,
            Func<bool> predicateFailed,
            Func<TReturn, PageResult<T>> resultSelector,
            CancellationToken cancellationToken = default) where T : class, new()
        {
            if (predicateFailed())
                return new PageResult<T>();

            var stopwatch = new Stopwatch();
            TReturn result;

            stopwatch.Start();

            if (db == null)
            {
                using (var tempDb = new DataContext(item.Key))
                {
                    result = await execute(tempDb, item, cancellationToken).ConfigureAwait(false);
                }
            }
            else
                result = await execute(db, item, cancellationToken).ConfigureAwait(false);

            stopwatch.Stop();

            var shouldLog = item.IsSqlLogEnabled || FastDb.EnableSqlLog || item.Config.IsOutSql || isOutSql;
            DbLog.LogSql(shouldLog, GetSqlFromResult(result), item.Config.DbType, stopwatch.Elapsed.TotalMilliseconds);

            if (result == null)
                return new PageResult<T>();

            return resultSelector(result);
        }

        /// <summary>
        /// 查询执行模板 - 提取公共逻辑
        /// predicateFailed 在查询执行前验证 Predicate 列表，避免无效查询
        /// </summary>
        private static TResult ExecuteQueryTemplate<TResult, TReturn>(
            DataQuery item,
            DataContext db,
            bool isOutSql,
            Func<DataContext, DataQuery, TReturn> execute,
            Func<bool> predicateFailed,
            Func<TReturn, TResult> defaultResult,
            Func<TReturn, string> getSql,
            Action preExecute = null)
        {
            if (predicateFailed())
                return default(TResult);

            preExecute?.Invoke();

            var stopwatch = new Stopwatch();
            TReturn result;

            stopwatch.Start();

            if (db == null)
            {
                using (var tempDb = new DataContext(item.Key))
                {
                    result = execute(tempDb, item);
                }
            }
            else
                result = execute(db, item);

            stopwatch.Stop();

            var shouldLog = item.IsSqlLogEnabled || FastDb.EnableSqlLog || item.Config.IsOutSql || isOutSql;
            DbLog.LogSql(shouldLog, getSql(result), item.Config.DbType, stopwatch.Elapsed.TotalMilliseconds);

            if (result == null)
                return default(TResult);

            return defaultResult(result);
        }

        /// <summary>
        /// 原生SQL执行模板 - 封装 DataContext 生命周期、计时和日志记录
        /// </summary>
        private static TResult ExecuteSqlTemplate<TResult, TReturn>(
            string key, DataContext db, bool isOutSql,
            Func<DataContext, TReturn> execute,
            Func<TReturn, TResult> resultSelector)
            where TReturn : class
        {
            ConfigModel config = null;
            TReturn result;
            var stopwatch = new Stopwatch();

            stopwatch.Start();

            if (db == null)
            {
                using (var tempDb = new DataContext(key))
                {
                    result = execute(tempDb);
                    config = tempDb.config;
                }
            }
            else
            {
                result = execute(db);
                config = db.config;
            }

            stopwatch.Stop();

            config.IsOutSql = config.IsOutSql || isOutSql;
            DbLog.LogSql(config.IsOutSql, GetSqlFromResult(result), config.DbType, stopwatch.Elapsed.TotalMilliseconds);

            if (result == null)
                return default(TResult);

            return resultSelector(result);
        }

        /// <summary>
        /// 分页查询执行模板 - 封装 DataContext 生命周期、计时和日志记录
        /// </summary>
        private static PageResult ExecutePageTemplate<TReturn>(
            DataQuery item, DataContext db, bool isOutSql,
            Func<DataContext, DataQuery, TReturn> execute,
            Func<bool> predicateFailed,
            Func<TReturn, PageResult> resultSelector)
        {
            if (predicateFailed())
                return new PageResult();

            var stopwatch = new Stopwatch();
            TReturn result;

            stopwatch.Start();

            if (db == null)
            {
                using (var tempDb = new DataContext(item.Key))
                {
                    result = execute(tempDb, item);
                }
            }
            else
                result = execute(db, item);

            stopwatch.Stop();

            var shouldLog = item.IsSqlLogEnabled || FastDb.EnableSqlLog || item.Config.IsOutSql || isOutSql;
            DbLog.LogSql(shouldLog, GetSqlFromResult(result), item.Config.DbType, stopwatch.Elapsed.TotalMilliseconds);

            if (result == null)
                return new PageResult();

            return resultSelector(result);
        }

        /// <summary>
        /// 分页查询执行模板（泛型版本）
        /// </summary>
        private static PageResult<T> ExecutePageTemplate<T, TReturn>(
            DataQuery item, DataContext db, bool isOutSql,
            Func<DataContext, DataQuery, TReturn> execute,
            Func<bool> predicateFailed,
            Func<TReturn, PageResult<T>> resultSelector) where T : class, new()
        {
            if (predicateFailed())
                return new PageResult<T>();

            var stopwatch = new Stopwatch();
            TReturn result;

            stopwatch.Start();

            if (db == null)
            {
                using (var tempDb = new DataContext(item.Key))
                {
                    result = execute(tempDb, item);
                }
            }
            else
                result = execute(db, item);

            stopwatch.Stop();

            var shouldLog = item.IsSqlLogEnabled || FastDb.EnableSqlLog || item.Config.IsOutSql || isOutSql;
            DbLog.LogSql(shouldLog, GetSqlFromResult(result), item.Config.DbType, stopwatch.Elapsed.TotalMilliseconds);

            if (result == null)
                return new PageResult<T>();

            return resultSelector(result);
        }

        /// <summary>
        /// 从结果对象中提取SQL字符串（通过反射获取Sql属性）
        /// </summary>
        private static string GetSqlFromResult<T>(T result)
        {
            if (result == null)
                return string.Empty;

            var sqlProperty = typeof(T).GetProperty("Sql");
            return sqlProperty?.GetValue(result)?.ToString() ?? string.Empty;
        }

        #endregion
    }
}
