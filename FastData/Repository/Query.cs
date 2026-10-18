using FastData.Base;
using FastData.Config;
using FastData.Context;
using FastData.Model;
using FastUntility.Page;
using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.Linq.Expressions;
using System.Threading.Tasks;

namespace FastData.Repository
{
    internal class Query : IQuery
    {
        internal DataQuery Data { get; set; } = new DataQuery();

        /// <summary>
        /// 查询join
        /// </summary>
        /// <typeparam name="T">第一个表类型</typeparam>
        /// <typeparam name="T1">第二个表类型</typeparam>
        /// <param name="joinType">join类型</param>
        /// <param name="predicate">条件表达式</param>
        /// <param name="field">字段表达式</param>
        /// <param name="isDblink">是否跨库查询</param>
        /// <returns>查询对象</returns>
        private IQuery JoinType<T, T1>(string joinType, Expression<Func<T, T1, bool>> predicate, Expression<Func<T1, object>> field = null, bool isDblink = false) where T : class where T1 : class
        {
            var queryField = BaseField.QueryField<T, T1>(predicate, field, this.Data.Config);
            // queryField.Field 是逗号分隔的字符串，需要分割成列表
            this.Data.Field.AddRange(queryField.Field.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries));
            this.Data.AsName.AddRange(queryField.AsName);

            var condtion = VisitExpression.LambdaWhere<T, T1>(predicate, this.Data.Config);
            this.Data.Predicate.Add(condtion);
            this.Data.Table.Add(string.Format("{2} {0}{3} {1}", typeof(T1).Name, predicate.Parameters[1].Name
            , joinType, isDblink && !string.IsNullOrEmpty(this.Data.Config.DbLinkName) ? string.Format("@", this.Data.Config.DbLinkName) : ""));

            return this;
        }

        /// <summary>
        /// 查询left join
        /// </summary>
        /// <typeparam name="T">第一个表类型</typeparam>
        /// <typeparam name="T1">第二个表类型</typeparam>
        /// <param name="predicate">条件表达式</param>
        /// <param name="field">字段表达式</param>
        /// <param name="isDblink">是否跨库查询</param>
        /// <returns>查询对象</returns>
        public override IQuery LeftJoin<T, T1>(Expression<Func<T, T1, bool>> predicate, Expression<Func<T1, object>> field = null, bool isDblink = false) where T : class where T1 : class
        {
            return JoinType("left join", predicate, field);
        }

        /// <summary>
        /// 查询right join
        /// </summary>
        /// <typeparam name="T">第一个表类型</typeparam>
        /// <typeparam name="T1">第二个表类型</typeparam>
        /// <param name="predicate">条件表达式</param>
        /// <param name="field">字段表达式</param>
        /// <param name="isDblink">是否跨库查询</param>
        /// <returns>查询对象</returns>
        public override IQuery RightJoin<T, T1>(Expression<Func<T, T1, bool>> predicate, Expression<Func<T1, object>> field = null, bool isDblink = false) where T : class where T1 : class
        {
            return JoinType("right join", predicate, field);
        }

        /// <summary>
        /// 查询inner join
        /// </summary>
        /// <typeparam name="T">第一个表类型</typeparam>
        /// <typeparam name="T1">第二个表类型</typeparam>
        /// <param name="predicate">条件表达式</param>
        /// <param name="field">字段表达式</param>
        /// <param name="isDblink">是否跨库查询</param>
        /// <returns>查询对象</returns>
        public override IQuery InnerJoin<T, T1>(Expression<Func<T, T1, bool>> predicate, Expression<Func<T1, object>> field = null, bool isDblink = false) where T : class where T1 : class
        {
            return JoinType("inner join", predicate, field);
        }

        /// <summary>
        /// 查询order by
        /// </summary>
        /// <typeparam name="T">实体类型</typeparam>
        /// <param name="field">排序字段表达式</param>
        /// <param name="isDesc">是否降序</param>
        /// <returns>查询对象</returns>
        public override IQuery OrderBy<T>(Expression<Func<T, object>> field, bool isDesc = true)
        {
            var orderBy = BaseField.OrderBy<T>(field, this.Data.Config, isDesc);
            this.Data.OrderBy.AddRange(orderBy);
            return this;
        }

        /// <summary>
        /// 查询group by
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="item"></param>
        /// <param name="field"></param>
        /// <returns></returns>
        public override IQuery GroupBy<T>(Expression<Func<T, object>> field)
        {
            var groupBy = BaseField.GroupBy<T>(field, this.Data.Config);
            this.Data.GroupBy.AddRange(groupBy);
            return this;
        }

        /// <summary>
        /// 查询take
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="item"></param>
        /// <param name="field"></param>
        /// <returns></returns>
        public override IQuery Take(int i)
        {
            this.Data.Take = i;
            return this;
        }


        /// <summary>
        /// 返回list
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="item"></param>
        /// <returns></returns>
        public override List<T> ToList<T>(DataContext db = null, bool isOutSql = false)
        {
            var stopwatch = new Stopwatch();
            var result = new DataReturn<T>();

            if (this.Data.Predicate.Exists(a => a.IsSuccess == false))
                return result.List;

            stopwatch.Start();

            if (db == null)
            {
                using (var tempDb = new DataContext(this.Data.Key))
                {
                    result = tempDb.GetList<T>(this.Data);
                }
            }
            else
                result = db.GetList<T>(this.Data);

            stopwatch.Stop();

            this.Data.Config.IsOutSql = this.Data.Config.IsOutSql || isOutSql;
            DbLog.LogSql(this.Data.Config.IsOutSql, result.Sql, this.Data.Config.DbType, stopwatch.Elapsed.TotalMilliseconds);
            return result.List;
        }

        /// <summary>
        /// 返回list asy
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="item"></param>
        /// <returns></returns>
        public override Task<List<T>> ToListAsync<T>(DataContext db = null, bool isOutSql = false)
        {
            return FastRead.ToListAsync<T>(this.Data, db, isOutSql);
        }

        /// <summary>
        /// 返回lazy<list>
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="item"></param>
        /// <returns></returns>
        public override Lazy<List<T>> ToLazyList<T>(DataContext db = null, bool isOutSql = false)
        {
            return new Lazy<List<T>>(() => ToList<T>(db, isOutSql));
        }

        /// <summary>
        /// 返回lazy<list> asy
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="item"></param>
        /// <returns></returns>
        public override Task<Lazy<List<T>>> ToLazyListAsync<T>(DataContext db = null, bool isOutSql = false)
        {
            return Task.FromResult(new Lazy<List<T>>(() => ToList<T>(db, isOutSql)));
        }


        /// <summary>
        /// 返回json
        /// </summary>
        /// <param name="item"></param>
        /// <returns></returns>
        public override string ToJson(DataContext db = null, bool isOutSql = false)
        {
            var result = new DataReturn();
            var stopwatch = new Stopwatch();

            if (this.Data.Predicate.Exists(a => a.IsSuccess == false))
                return result.Json;

            stopwatch.Start();

            if (db == null)
            {
                using (var tempDb = new DataContext(this.Data.Key))
                {
                    result = tempDb.GetJson(this.Data);
                }
            }
            else
                result = db.GetJson(this.Data);

            stopwatch.Stop();

            this.Data.Config.IsOutSql = this.Data.Config.IsOutSql || isOutSql;
            DbLog.LogSql(this.Data.Config.IsOutSql, result.Sql, this.Data.Config.DbType, stopwatch.Elapsed.TotalMilliseconds);
            return result.Json;
        }

        /// <summary>
        /// 返回json asy
        /// </summary>
        /// <param name="item"></param>
        /// <returns></returns>
        public override Task<string> ToJsonAsync(DataContext db = null, bool isOutSql = false)
        {
            return FastRead.ToJsonAsync(this.Data, db, isOutSql);
        }

        /// <summary>
        /// 返回lazy<json>
        /// </summary>
        /// <param name="item"></param>
        /// <returns></returns>
        public override Lazy<string> ToLazyJson(DataContext db = null, bool isOutSql = false)
        {
            return AsyncHelper.ToLazy(() => ToJson(db, isOutSql));
        }

        /// <summary>
        /// 返回lazy<json> asy
        /// </summary>
        /// <param name="item"></param>
        /// <returns></returns>
        public override Task<Lazy<string>> ToLazyJsonAsync(DataContext db = null, bool isOutSql = false)
        {
            return Task.FromResult(AsyncHelper.ToLazy(() => ToJson(db, isOutSql)));
        }


        /// <summary>
        /// 返回item
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="item"></param>
        /// <returns></returns>
        public override T ToItem<T>(DataContext db = null, bool isOutSql = false)
        {
            var result = new DataReturn<T>();
            var stopwatch = new Stopwatch();

            if (this.Data.Predicate.Exists(a => a.IsSuccess == false))
                return result.Item;

            stopwatch.Start();

            this.Data.Take = 1;

            if (db == null)
            {
                using (var tempDb = new DataContext(this.Data.Key))
                {
                    result = tempDb.GetList<T>(this.Data);
                }
            }
            else
                result = db.GetList<T>(this.Data);

            stopwatch.Stop();

            this.Data.Config.IsOutSql = this.Data.Config.IsOutSql || isOutSql;
            DbLog.LogSql(this.Data.Config.IsOutSql, result.Sql, this.Data.Config.DbType, stopwatch.Elapsed.TotalMilliseconds);
            return result.Item;
        }

        /// <summary>
        /// 返回item asy
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="item"></param>
        /// <returns></returns>
        public override Task<T> ToItemAsync<T>(DataContext db = null, bool isOutSql = false)
        {
            return FastRead.ToItemAsync<T>(this.Data, db, isOutSql);
        }

        /// <summary>
        /// 返回Lazy<item>
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="item"></param>
        /// <returns></returns>
        public override Lazy<T> ToLazyItem<T>(DataContext db = null, bool isOutSql = false)
        {
            return AsyncHelper.ToLazy(() => ToItem<T>(db, isOutSql));
        }

        /// <summary>
        /// 返回Lazy<item> asy
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="item"></param>
        /// <returns></returns>
        public override Task<Lazy<T>> ToLazyItemAsync<T>(DataContext db = null, bool isOutSql = false)
        {
            return Task.FromResult(AsyncHelper.ToLazy(() => ToItem<T>(db, isOutSql)));
        }


        /// <summary>
        /// 返回条数
        /// </summary>
        /// <param name="item"></param>
        /// <returns></returns>
        public override int ToCount(DataContext db = null, bool isOutSql = false)
        {
            var result = new DataReturn();
            var stopwatch = new Stopwatch();

            if (this.Data.Predicate.Exists(a => a.IsSuccess == false))
                return result.Count;

            stopwatch.Start();

            if (db == null)
            {
                using (var tempDb = new DataContext(this.Data.Key))
                {
                    result = tempDb.GetCount(this.Data);
                }
            }
            else
                result = db.GetCount(this.Data);

            stopwatch.Stop();

            this.Data.Config.IsOutSql = this.Data.Config.IsOutSql || isOutSql;
            DbLog.LogSql(this.Data.Config.IsOutSql, result.Sql, this.Data.Config.DbType, stopwatch.Elapsed.TotalMilliseconds);

            return result.Count;
        }

        /// <summary>
        /// 返回条数 asy
        /// </summary>
        /// <param name="item"></param>
        /// <returns></returns>
        public override Task<int> ToCountAsync<T, T1>(DataContext db = null, bool isOutSql = false)
        {
            return FastRead.ToCountAsync(this.Data, db, isOutSql);
        }


        /// <summary>
        /// 返回分页
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="item"></param>
        /// <param name="pModel"></param>
        /// <returns></returns>
        public override PageResult<T> ToPage<T>(PageModel pModel, DataContext db = null, bool isOutSql = false)
        {
            var result = new DataReturn<T>();
            var stopwatch = new Stopwatch();

            if (this.Data.Predicate.Exists(a => a.IsSuccess == false))
                return result.PageResult;

            stopwatch.Start();

            if (db == null)
            {
                using (var tempDb = new DataContext(this.Data.Key))
                {
                    result = tempDb.GetPage<T>(this.Data, pModel);
                }
            }
            else
                result = db.GetPage<T>(this.Data, pModel);

            stopwatch.Stop();

            this.Data.Config.IsOutSql = this.Data.Config.IsOutSql || isOutSql;
            DbLog.LogSql(this.Data.Config.IsOutSql, result.Sql, this.Data.Config.DbType, stopwatch.Elapsed.TotalMilliseconds);
            return result.PageResult;
        }

        /// <summary>
        /// 返回分页 asy
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="item"></param>
        /// <param name="pModel"></param>
        /// <returns></returns>
        public override Task<PageResult<T>> ToPageAsync<T>(PageModel pModel, DataContext db = null, bool isOutSql = false)
        {
            return FastRead.ToPageAsync<T>(this.Data, pModel, db, isOutSql);
        }

        /// <summary>
        /// 返回分页lazy
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="item"></param>
        /// <param name="pModel"></param>
        /// <returns></returns>
        public override Lazy<PageResult<T>> ToLazyPage<T>(PageModel pModel, DataContext db = null, bool isOutSql = false)
        {
            return new Lazy<PageResult<T>>(() => ToPage<T>(pModel, db, isOutSql));
        }

        /// <summary>
        /// 返回分页lazy asy
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="item"></param>
        /// <param name="pModel"></param>
        /// <returns></returns>
        public override Task<Lazy<PageResult<T>>> ToLazyPageAsync<T>(PageModel pModel, DataContext db = null, bool isOutSql = false)
        {
            return Task.FromResult(new Lazy<PageResult<T>>(() => ToPage<T>(pModel, db, isOutSql)));
        }


        /// <summary>
        /// 返回分页Dictionary<string, object>
        /// </summary>
        /// <param name="item"></param>
        /// <param name="pModel"></param>
        /// <returns></returns>
        public override PageResult ToPage(PageModel pModel, DataContext db = null, bool isOutSql = false)
        {
            var result = new DataReturn();
            var stopwatch = new Stopwatch();

            if (this.Data.Predicate.Exists(a => a.IsSuccess == false))
                return result.PageResult;

            stopwatch.Start();

            if (db == null)
            {
                using (var tempDb = new DataContext(this.Data.Key))
                {
                    result = tempDb.GetPage(this.Data, pModel);
                }
            }
            else
                result = db.GetPage(this.Data, pModel);

            stopwatch.Stop();

            this.Data.Config.IsOutSql = this.Data.Config.IsOutSql || isOutSql;
            DbLog.LogSql(this.Data.Config.IsOutSql, result.Sql, this.Data.Config.DbType, stopwatch.Elapsed.TotalMilliseconds);
            return result.PageResult;
        }

        /// <summary>
        /// 返回分页Dictionary<string, object> asy
        /// </summary>
        /// <param name="item"></param>
        /// <param name="pModel"></param>
        /// <returns></returns>
        public override Task<PageResult> ToPageAsync(PageModel pModel, DataContext db = null, bool isOutSql = false)
        {
            return FastRead.ToPageAsync(this.Data, pModel, db, isOutSql);
        }

        /// <summary>
        /// 返回分页Dictionary<string, object> lazy
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="item"></param>
        /// <param name="pModel"></param>
        /// <returns></returns>
        public override Lazy<PageResult> ToLazyPage(PageModel pModel, DataContext db = null, bool isOutSql = false)
        {
            return AsyncHelper.ToLazy(() => ToPage(pModel, db, isOutSql));
        }

        /// <summary>
        /// 返回分页Dictionary<string, object> lazy asy
        /// </summary>
        /// <param name="item"></param>
        /// <param name="pModel"></param>
        /// <returns></returns>
        public override Task<Lazy<PageResult>> ToLazyPageAsync(PageModel pModel, DataContext db = null, bool isOutSql = false)
        {
            return Task.FromResult(AsyncHelper.ToLazy(() => ToPage(pModel, db, isOutSql)));
        }


        /// <summary>
        /// DataTable
        /// </summary>
        /// <param name="item"></param>
        /// <returns></returns>
        public override DataTable ToDataTable(DataContext db = null, bool isOutSql = false)
        {
            var result = new DataReturn();
            var stopwatch = new Stopwatch();

            if (this.Data.Predicate.Exists(a => a.IsSuccess == false))
                return result.Table;

            stopwatch.Start();

            if (db == null)
            {
                using (var tempDb = new DataContext(this.Data.Key))
                {
                    result = tempDb.GetDataTable(this.Data);
                }
            }
            else
                result = db.GetDataTable(this.Data);

            stopwatch.Stop();

            this.Data.Config.IsOutSql = this.Data.Config.IsOutSql || isOutSql;
            DbLog.LogSql(this.Data.Config.IsOutSql, result.Sql, this.Data.Config.DbType, stopwatch.Elapsed.TotalMilliseconds);
            return result.Table;
        }

        /// <summary>
        /// DataTable asy
        /// </summary>
        /// <param name="item"></param>
        /// <returns></returns>
        public override Task<DataTable> ToDataTableAsync(DataContext db = null, bool isOutSql = false)
        {
            return FastRead.ToDataTableAsync(this.Data, db, isOutSql);
        }

        /// <summary>
        /// DataTable lazy
        /// </summary>
        /// <param name="item"></param>
        /// <returns></returns>
        public override Lazy<DataTable> ToLazyDataTable(DataContext db = null, bool isOutSql = false)
        {
            return AsyncHelper.ToLazy(() => ToDataTable(db, isOutSql));
        }

        /// <summary>
        /// DataTable lazy asy
        /// </summary>
        /// <param name="item"></param>
        /// <returns></returns>
        public override Task<Lazy<DataTable>> ToLazyDataTableAsync(DataContext db = null, bool isOutSql = false)
        {
            return Task.FromResult(AsyncHelper.ToLazy(() => ToDataTable(db, isOutSql)));
        }


        /// <summary>
        /// 返回List<Dictionary<string, object>>
        /// </summary>
        /// <param name="item"></param>
        /// <returns></returns>
        public override List<Dictionary<string, object>> ToDics(DataContext db = null, bool isOutSql = false)
        {
            var result = new DataReturn();
            var stopwatch = new Stopwatch();

            if (this.Data.Predicate.Exists(a => a.IsSuccess == false))
                return result.DicList;

            stopwatch.Start();

            if (db == null)
            {
                using (var tempDb = new DataContext(this.Data.Key))
                {
                    result = tempDb.GetDic(this.Data);
                }
            }
            else
                result = db.GetDic(this.Data);

            stopwatch.Stop();

            this.Data.Config.IsOutSql = this.Data.Config.IsOutSql || isOutSql;
            DbLog.LogSql(this.Data.Config.IsOutSql, result.Sql, this.Data.Config.DbType, stopwatch.Elapsed.TotalMilliseconds);
            return result.DicList;
        }

        /// <summary>
        /// 返回List<Dictionary<string, object>> asy
        /// </summary>
        /// <param name="item"></param>
        /// <returns></returns>
        public override Task<List<Dictionary<string, object>>> ToDicsAsync(DataContext db = null, bool isOutSql = false)
        {
            return FastRead.ToDicsAsync(this.Data, db, isOutSql);
        }

        /// <summary>
        /// 返回lazy<List<Dictionary<string, object>>>
        /// </summary>
        /// <param name="item"></param>
        /// <returns></returns>
        public override Lazy<List<Dictionary<string, object>>> ToLazyDics(DataContext db = null, bool isOutSql = false)
        {
            return new Lazy<List<Dictionary<string, object>>>(() => ToDics(db, isOutSql));
        }

        /// <summary>
        /// 返回lazy<List<Dictionary<string, object>>> asy
        /// </summary>
        /// <param name="item"></param>
        /// <returns></returns>
        public override Task<Lazy<List<Dictionary<string, object>>>> ToLazyDicsAsync(DataContext db = null, bool isOutSql = false)
        {
            return Task.FromResult(new Lazy<List<Dictionary<string, object>>>(() => ToDics(db, isOutSql)));
        }


        /// <summary>
        /// Dictionary<string, object>
        /// </summary>
        /// <param name="item"></param>
        /// <returns></returns>
        public override Dictionary<string, object> ToDic(DataContext db = null, bool isOutSql = false)
        {
            var result = new DataReturn();
            var stopwatch = new Stopwatch();

            if (this.Data.Predicate.Exists(a => a.IsSuccess == false))
                return result.Dic;

            stopwatch.Start();
            this.Data.Take = 1;

            if (db == null)
            {
                using (var tempDb = new DataContext(this.Data.Key))
                {
                    result = tempDb.GetDic(this.Data);
                }
            }
            else
                result = db.GetDic(this.Data);

            stopwatch.Stop();

            this.Data.Config.IsOutSql = this.Data.Config.IsOutSql || isOutSql;
            DbLog.LogSql(this.Data.Config.IsOutSql, result.Sql, this.Data.Config.DbType, stopwatch.Elapsed.TotalMilliseconds);
            return result.Dic;
        }

        /// <summary>
        /// Dictionary<string, object> asy
        /// </summary>
        /// <param name="item"></param>
        /// <returns></returns>
        public override Task<Dictionary<string, object>> ToDicAsync(DataContext db = null, bool isOutSql = false)
        {
            return FastRead.ToDicAsync(this.Data, db, isOutSql);
        }

        /// <summary>
        /// Dictionary<string, object>>
        /// </summary>
        /// <param name="item"></param>
        /// <returns></returns>
        public override Lazy<Dictionary<string, object>> ToLazyDic(DataContext db = null, bool isOutSql = false)
        {
            return new Lazy<Dictionary<string, object>>(() => ToDic(db, isOutSql));
        }

        /// <summary>
        /// Dictionary<string, object> asy
        /// </summary>
        /// <param name="item"></param>
        /// <returns></returns>
        public override Task<Lazy<Dictionary<string, object>>> ToLazyDicAsync(DataContext db = null, bool isOutSql = false)
        {
            return Task.FromResult(new Lazy<Dictionary<string, object>>(() => ToDic(db, isOutSql)));
        }
    }
}
