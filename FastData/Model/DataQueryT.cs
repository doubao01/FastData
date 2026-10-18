using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Linq.Expressions;
using FastData.Base;
using FastData.Sharding;
using FastUntility.Page;

namespace FastData.Model
{
    #region 泛型查询
    /// <summary>
    /// 泛型查询类 - 支持链式调用且只需指定一次类型
    /// </summary>
    /// <typeparam name="T">实体类型</typeparam>
    public sealed class DataQuery<T> : DataQuery where T : class, new()
    {
        /// <summary>
        /// 链式 Where 条件（AND）
        /// </summary>
        public DataQuery<T> Where(Expression<Func<T, bool>> predicate)
        {
            if (predicate == null)
                return this;

            var visitModel = VisitExpression.LambdaWhere<T>(predicate, Config);
            if (visitModel.IsSuccess)
            {
                ChainedConditions.Add(new ChainedCondition
                {
                    Operator = "AND",
                    Where = visitModel.Where,
                    Param = visitModel.Param
                });
            }

            return this;
        }

        /// <summary>
        /// 使用 Where&lt;T&gt; 条件构建器
        /// </summary>
        public DataQuery<T> Where(Where<T> where)
        {
            if (where == null)
                return this;

            where.SetConfig(Config);
            ChainedConditions.AddRange(where.Conditions);

            return this;
        }

        /// <summary>
        /// 链式 And 条件（Where 的别名）
        /// </summary>
        public DataQuery<T> And(Expression<Func<T, bool>> predicate)
        {
            return Where(predicate);
        }

        /// <summary>
        /// 链式 Or 条件
        /// </summary>
        public DataQuery<T> Or(Expression<Func<T, bool>> predicate)
        {
            if (predicate == null)
                return this;

            var visitModel = VisitExpression.LambdaWhere<T>(predicate, Config);
            if (visitModel.IsSuccess)
            {
                ChainedConditions.Add(new ChainedCondition
                {
                    Operator = "OR",
                    Where = visitModel.Where,
                    Param = visitModel.Param
                });
            }

            return this;
        }

        /// <summary>
        /// 链式 Like 条件（参数化查询，防 SQL 注入）
        /// </summary>
        public DataQuery<T> Like(Expression<Func<T, object>> field, string value)
        {
            if (field == null || string.IsNullOrEmpty(value))
                return this;

            var fieldName = GetMemberName(field);
            ChainedConditions.Add(new ChainedCondition
            {
                Operator = "AND",
                Where = string.Empty,
                Param = new List<DbParameter>(),
                Conditions = new List<FastData.Base.Condition>
                {
                    new FastData.Base.Condition(fieldName, FastData.Base.ConditionOperator.Like, value, FastData.Base.ConditionLogic.And)
                }
            });

            return this;
        }

        /// <summary>
        /// 链式 NotLike 条件（参数化查询）
        /// </summary>
        public DataQuery<T> NotLike(Expression<Func<T, object>> field, string value)
        {
            if (field == null || string.IsNullOrEmpty(value))
                return this;

            var fieldName = GetMemberName(field);
            ChainedConditions.Add(new ChainedCondition
            {
                Operator = "AND",
                Where = string.Empty,
                Param = new List<DbParameter>(),
                Conditions = new List<FastData.Base.Condition>
                {
                    new FastData.Base.Condition(fieldName, FastData.Base.ConditionOperator.NotLike, value, FastData.Base.ConditionLogic.And)
                }
            });
            return this;
        }

        /// <summary>
        /// 链式 Contains 条件（LIKE '%value%'，参数化）
        /// </summary>
        public DataQuery<T> Contains(Expression<Func<T, object>> field, string value)
        {
            if (field == null || value == null) return this;
            var fieldName = GetMemberName(field);
            ChainedConditions.Add(new ChainedCondition
            {
                Operator = "AND",
                Where = string.Empty,
                Param = new List<DbParameter>(),
                Conditions = new List<FastData.Base.Condition>
                {
                    new FastData.Base.Condition(fieldName, FastData.Base.ConditionOperator.Contains, value, FastData.Base.ConditionLogic.And)
                }
            });
            return this;
        }

        /// <summary>
        /// 链式 StartsWith 条件（LIKE 'value%'，参数化）
        /// </summary>
        public DataQuery<T> StartsWith(Expression<Func<T, object>> field, string value)
        {
            if (field == null || value == null) return this;
            var fieldName = GetMemberName(field);
            ChainedConditions.Add(new ChainedCondition
            {
                Operator = "AND",
                Where = string.Empty,
                Param = new List<DbParameter>(),
                Conditions = new List<FastData.Base.Condition>
                {
                    new FastData.Base.Condition(fieldName, FastData.Base.ConditionOperator.StartsWith, value, FastData.Base.ConditionLogic.And)
                }
            });
            return this;
        }

        /// <summary>
        /// 链式 EndsWith 条件（LIKE '%value'，参数化）
        /// </summary>
        public DataQuery<T> EndsWith(Expression<Func<T, object>> field, string value)
        {
            if (field == null || value == null) return this;
            var fieldName = GetMemberName(field);
            ChainedConditions.Add(new ChainedCondition
            {
                Operator = "AND",
                Where = string.Empty,
                Param = new List<DbParameter>(),
                Conditions = new List<FastData.Base.Condition>
                {
                    new FastData.Base.Condition(fieldName, FastData.Base.ConditionOperator.EndsWith, value, FastData.Base.ConditionLogic.And)
                }
            });
            return this;
        }

        /// <summary>
        /// 链式 In 条件（参数化查询）
        /// </summary>
        public DataQuery<T> In(Expression<Func<T, object>> field, IEnumerable<object> values)
        {
            if (field == null || values == null) return this;

            var fieldName = GetMemberName(field);
            var list = new List<object>();
            foreach (var v in values) list.Add(v);
            if (list.Count == 0) return this;

            ChainedConditions.Add(new ChainedCondition
            {
                Operator = "AND",
                Where = string.Empty,
                Param = new List<DbParameter>(),
                Conditions = new List<FastData.Base.Condition>
                {
                    new FastData.Base.Condition(fieldName, FastData.Base.ConditionOperator.In, list, FastData.Base.ConditionLogic.And)
                }
            });
            return this;
        }

        /// <summary>
        /// 链式 NotIn 条件（参数化查询）
        /// </summary>
        public DataQuery<T> NotIn(Expression<Func<T, object>> field, IEnumerable<object> values)
        {
            if (field == null || values == null) return this;

            var fieldName = GetMemberName(field);
            var list = new List<object>();
            foreach (var v in values) list.Add(v);
            if (list.Count == 0) return this;

            ChainedConditions.Add(new ChainedCondition
            {
                Operator = "AND",
                Where = string.Empty,
                Param = new List<DbParameter>(),
                Conditions = new List<FastData.Base.Condition>
                {
                    new FastData.Base.Condition(fieldName, FastData.Base.ConditionOperator.NotIn, list, FastData.Base.ConditionLogic.And)
                }
            });
            return this;
        }

        /// <summary>
        /// 链式 Between 条件（参数化查询）
        /// </summary>
        /// <param name="field">字段表达式</param>
        /// <param name="start">起始值</param>
        /// <param name="end">结束值</param>
        /// <returns>DataQuery泛型对象</returns>
        public DataQuery<T> Between(Expression<Func<T, object>> field, object start, object end)
        {
            if (field == null) return this;

            var fieldName = GetMemberName(field);
            ChainedConditions.Add(new ChainedCondition
            {
                Operator = "AND",
                Where = string.Empty,
                Param = new List<DbParameter>(),
                Conditions = new List<FastData.Base.Condition>
                {
                    new FastData.Base.Condition(fieldName, FastData.Base.ConditionOperator.Between, new List<object> { start, end }, FastData.Base.ConditionLogic.And)
                }
            });
            return this;
        }

        /// <summary>链式 NotBetween 条件</summary>
        public DataQuery<T> NotBetween(Expression<Func<T, object>> field, object start, object end)
        {
            if (field == null) return this;
            var fieldName = GetMemberName(field);
            ChainedConditions.Add(new ChainedCondition
            {
                Operator = "AND",
                Where = string.Empty,
                Param = new List<DbParameter>(),
                Conditions = new List<FastData.Base.Condition>
                {
                    new FastData.Base.Condition(fieldName, FastData.Base.ConditionOperator.NotBetween, new List<object> { start, end }, FastData.Base.ConditionLogic.And)
                }
            });
            return this;
        }

        /// <summary>链式 IsNull 条件</summary>
        public DataQuery<T> IsNull(Expression<Func<T, object>> field)
        {
            if (field == null) return this;
            var fieldName = GetMemberName(field);
            ChainedConditions.Add(new ChainedCondition
            {
                Operator = "AND",
                Where = string.Empty,
                Param = new List<DbParameter>(),
                Conditions = new List<FastData.Base.Condition>
                {
                    new FastData.Base.Condition(fieldName, FastData.Base.ConditionOperator.IsNull, null, FastData.Base.ConditionLogic.And)
                }
            });
            return this;
        }

        /// <summary>链式 IsNotNull 条件</summary>
        public DataQuery<T> IsNotNull(Expression<Func<T, object>> field)
        {
            if (field == null) return this;
            var fieldName = GetMemberName(field);
            ChainedConditions.Add(new ChainedCondition
            {
                Operator = "AND",
                Where = string.Empty,
                Param = new List<DbParameter>(),
                Conditions = new List<FastData.Base.Condition>
                {
                    new FastData.Base.Condition(fieldName, FastData.Base.ConditionOperator.IsNotNull, null, FastData.Base.ConditionLogic.And)
                }
            });
            return this;
        }

        /// <summary>
        /// 排序（升序）
        /// </summary>
        public new DataQuery<T> OrderBy(Expression<Func<T, object>> field)
        {
            if (field == null)
                return this;

            var fieldName = GetMemberName(field);
            base.OrderBy.Add(string.Format("{0} ASC", fieldName));

            return this;
        }

        /// <summary>
        /// 排序（降序）
        /// </summary>
        public DataQuery<T> OrderByDescending(Expression<Func<T, object>> field)
        {
            if (field == null)
                return this;

            var fieldName = GetMemberName(field);
            base.OrderBy.Add(string.Format("{0} DESC", fieldName));

            return this;
        }

        /// <summary>
        /// 分组
        /// </summary>
        public new DataQuery<T> GroupBy(Expression<Func<T, object>> field)
        {
            if (field == null)
                return this;

            var fieldName = GetMemberName(field);
            base.GroupBy.Add(fieldName);

            return this;
        }

        /// <summary>
        /// 投影查询（支持匿名类型）
        /// </summary>
        public ProjectedQuery<T, TResult> Select<TResult>(Expression<Func<T, TResult>> selector)
        {
            return new ProjectedQuery<T, TResult>(this, selector);
        }

        #region 分表支持

        /// <summary>
        /// 启用分表查询
        /// </summary>
        /// <param name="queryParams">分表查询参数（如时间范围、哈希字段值等）</param>
        /// <returns>当前查询对象</returns>
        public DataQuery<T> UseSharding(Dictionary<string, object> queryParams = null)
        {
            EnableSharding = true;
            if (queryParams != null)
            {
                foreach (var param in queryParams)
                {
                    ShardingQueryParams[param.Key] = param.Value;
                }
            }
            return this;
        }

        /// <summary>
        /// 添加分表查询参数
        /// </summary>
        /// <param name="key">参数名</param>
        /// <param name="value">参数值</param>
        /// <returns>当前查询对象</returns>
        public DataQuery<T> WithShardingParam(string key, object value)
        {
            EnableSharding = true;
            ShardingQueryParams[key] = value;
            return this;
        }

        /// <summary>
        /// 使用时间范围作为分表参数
        /// </summary>
        /// <param name="timeField">时间字段名</param>
        /// <param name="startTime">开始时间</param>
        /// <param name="endTime">结束时间</param>
        /// <returns>当前查询对象</returns>
        public DataQuery<T> WithTimeRange(string timeField, DateTime startTime, DateTime endTime)
        {
            EnableSharding = true;
            ShardingQueryParams[string.Format("{0}_Start", timeField)] = startTime;
            ShardingQueryParams[string.Format("{0}_End", timeField)] = endTime;
            return this;
        }

        /// <summary>
        /// 使用哈希字段作为分表参数
        /// </summary>
        /// <param name="hashField">哈希字段名</param>
        /// <param name="value">字段值</param>
        /// <returns>当前查询对象</returns>
        public DataQuery<T> WithHashField(string hashField, object value)
        {
            EnableSharding = true;
            ShardingQueryParams[hashField] = value;
            return this;
        }

        /// <summary>
        /// 使用列表字段作为分表参数
        /// </summary>
        /// <param name="listField">列表字段名</param>
        /// <param name="value">字段值</param>
        /// <returns>当前查询对象</returns>
        public DataQuery<T> WithListField(string listField, object value)
        {
            EnableSharding = true;
            ShardingQueryParams[listField] = value;
            return this;
        }

        /// <summary>
        /// 覆盖全局分表配置
        /// </summary>
        /// <param name="config">分表配置</param>
        /// <returns>当前查询对象</returns>
        public DataQuery<T> WithShardingConfig(ShardingConfig config)
        {
            ShardingConfigOverride = config;
            return this;
        }

        /// <summary>
        /// 启用当前查询的SQL日志（覆盖全局设置）
        /// </summary>
        /// <returns>当前查询对象</returns>
        public DataQuery<T> EnableSqlLog()
        {
            this.IsSqlLogEnabled = true;
            return this;
        }

        #endregion

        /// <summary>
        /// 查询列表
        /// </summary>
        public List<T> ToList()
        {
            if (EnableSharding && ShardingManager.IsShardingEnabled<T>())
            {
                return ShardingReadHelper.Query<T>(
                    BuildPredicate(),
                    ShardingQueryParams,
                    Key
                );
            }
            return FastRead.ToList<T>(this);
        }

        /// <summary>
        /// 查询单条
        /// </summary>
        public T ToItem()
        {
            if (EnableSharding && ShardingManager.IsShardingEnabled<T>())
            {
                var results = ShardingReadHelper.Query<T>(
                    BuildPredicate(),
                    ShardingQueryParams,
                    Key
                );
                return results.FirstOrDefault();
            }
            return FastRead.ToItem<T>(this);
        }

        /// <summary>
        /// 查询条数
        /// </summary>
        public int ToCount()
        {
            if (EnableSharding && ShardingManager.IsShardingEnabled<T>())
            {
                var results = ShardingReadHelper.Query<T>(
                    BuildPredicate(),
                    ShardingQueryParams,
                    Key
                );
                return results.Count;
            }
            return FastRead.ToCount(this);
        }

        /// <summary>
        /// 分页查询
        /// </summary>
        /// <param name="page">页码</param>
        /// <param name="pageSize">每页条数</param>
        /// <returns>分页结果</returns>
        public PaginationResult<T> ToPagination(int page, int pageSize)
        {
            if (EnableSharding && ShardingManager.IsShardingEnabled<T>())
            {
                var pageResult = ShardingReadHelper.QueryPage<T>(
                    BuildPredicate(),
                    ShardingQueryParams,
                    page,
                    pageSize,
                    Key
                );

                return new PaginationResult<T>
                {
                    Data = pageResult.list,
                    Page = page,
                    PageSize = pageSize,
                    Total = pageResult.pModel.TotalRecord,
                    TotalPages = pageResult.pModel.TotalPage
                };
            }
            return FastRead.ToPagination<T>(this, page, pageSize);
        }

        /// <summary>
        /// 构建查询谓词
        /// </summary>
        private Expression<Func<T, bool>> BuildPredicate()
        {
            // 分片查询当前只支持按分片参数路由，无法把链式条件转换为表达式。
            // 存在条件时直接失败，避免把全部分片数据当作查询结果返回。
            if (ChainedConditions != null && ChainedConditions.Count > 0)
                throw new NotSupportedException("分片查询暂不支持链式 Where/Like 条件，请改用分片参数或关闭分片后查询。");

            return t => true;
        }

        /// <summary>
        /// 从表达式中获取成员名称
        /// </summary>
        private static string GetMemberName(Expression<Func<T, object>> expression)
        {
            if (expression.Body is MemberExpression member)
                return member.Member.Name;

            if (expression.Body is UnaryExpression unary && unary.Operand is MemberExpression unaryMember)
                return unaryMember.Member.Name;

            throw new ArgumentException("表达式必须是成员访问表达式");
        }
    }
    #endregion
}
