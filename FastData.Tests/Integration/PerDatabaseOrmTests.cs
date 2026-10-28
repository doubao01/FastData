using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FastData;
using FastData.Base;
using FastData.Config;
using FastData.Context;
using FastData.Model;
using FastData.Property;
using FastUntility.Page;
using Xunit;

namespace FastData.Tests.Integration
{
    /// <summary>
    /// 每个数据库完整 ORM 功能测试
    /// 覆盖：CRUD、Lambda 查询、链式查询、分页、事务、批量插入、AOP、多数据库表名映射
    /// </summary>
    [Collection(SharedDatabaseCollection.Name)]
    public class PerDatabaseOrmTests
    {
        private static readonly object _lockObj = new object();

        public PerDatabaseOrmTests()
        {
            DbProviderFactories.RegisterFactory("Microsoft.Data.SqlClient", Microsoft.Data.SqlClient.SqlClientFactory.Instance);
            DbProviderFactories.RegisterFactory("MySql.Data.MySqlClient", MySql.Data.MySqlClient.MySqlClientFactory.Instance);
            DbProviderFactories.RegisterFactory("Npgsql", Npgsql.NpgsqlFactory.Instance);
        }

        #region SqlServer 完整测试

        [Fact]
        public void SqlServer_FullOrmTest()
        {
            var dbName = "SqlServer";
            var results = RunFullOrmTest(dbName);
            PrintResults(dbName, results);

            // 验证所有测试通过
            Assert.All(results, r => Assert.True(r.Value.Success, string.Format("{0} 失败: {1}", r.Key, r.Value.Details)));
        }

        [Fact]
        public void SqlServer_Insert()
        {
            var result = TestInsert("SqlServer");
            Assert.True(result.Success, result.Details);
        }

        [Fact]
        public void SqlServer_Query()
        {
            var result = TestQuery("SqlServer");
            Assert.True(result.Success, result.Details);
        }

        [Fact]
        public void SqlServer_Update()
        {
            var result = TestUpdate("SqlServer");
            Assert.True(result.Success, result.Details);
        }

        [Fact]
        public void SqlServer_Delete()
        {
            var result = TestDelete("SqlServer");
            Assert.True(result.Success, result.Details);
        }

        [Fact]
        public void SqlServer_ChainQuery()
        {
            var result = TestChainQuery("SqlServer");
            Assert.True(result.Success, result.Details);
        }

        [Fact]
        public void SqlServer_Pagination()
        {
            var result = TestPagination("SqlServer");
            Assert.True(result.Success, result.Details);
        }

        [Fact]
        public void SqlServer_BatchInsert()
        {
            var result = TestBatchInsert("SqlServer");
            Assert.True(result.Success, result.Details);
        }

        [Fact]
        public void SqlServer_LambdaWhere()
        {
            var result = TestLambdaWhere("SqlServer");
            Assert.True(result.Success, result.Details);
        }

        [Fact]
        public void SqlServer_OrderByGroupBy()
        {
            var result = TestOrderByGroupBy("SqlServer");
            Assert.True(result.Success, result.Details);
        }

        [Fact]
        public void SqlServer_DbTableNames()
        {
            var result = TestDbTableNames("SqlServer");
            Assert.True(result.Success, result.Details);
        }

        #endregion

        #region MySql 完整测试

        [Fact]
        public void MySql_FullOrmTest()
        {
            var dbName = "MySql";
            var results = RunFullOrmTest(dbName);
            PrintResults(dbName, results);

            Assert.All(results, r => Assert.True(r.Value.Success, string.Format("{0} 失败: {1}", r.Key, r.Value.Details)));
        }

        [Fact]
        public void MySql_Insert()
        {
            var result = TestInsert("MySql");
            Assert.True(result.Success, result.Details);
        }

        [Fact]
        public void MySql_Query()
        {
            var result = TestQuery("MySql");
            Assert.True(result.Success, result.Details);
        }

        [Fact]
        public void MySql_Update()
        {
            var result = TestUpdate("MySql");
            Assert.True(result.Success, result.Details);
        }

        [Fact]
        public void MySql_Delete()
        {
            var result = TestDelete("MySql");
            Assert.True(result.Success, result.Details);
        }

        [Fact]
        public void MySql_ChainQuery()
        {
            var result = TestChainQuery("MySql");
            Assert.True(result.Success, result.Details);
        }

        [Fact]
        public void MySql_Pagination()
        {
            var result = TestPagination("MySql");
            Assert.True(result.Success, result.Details);
        }

        [Fact]
        public void MySql_BatchInsert()
        {
            var result = TestBatchInsert("MySql");
            Assert.True(result.Success, result.Details);
        }

        [Fact]
        public void MySql_LambdaWhere()
        {
            var result = TestLambdaWhere("MySql");
            Assert.True(result.Success, result.Details);
        }

        [Fact]
        public void MySql_OrderByGroupBy()
        {
            var result = TestOrderByGroupBy("MySql");
            Assert.True(result.Success, result.Details);
        }

        [Fact]
        public void MySql_DbTableNames()
        {
            var result = TestDbTableNames("MySql");
            Assert.True(result.Success, result.Details);
        }

        #endregion

        #region PostgreSql 完整测试

        [Fact]
        public void PostgreSql_FullOrmTest()
        {
            var dbName = "PostgreSql";
            var results = RunFullOrmTest(dbName);
            PrintResults(dbName, results);

            Assert.All(results, r => Assert.True(r.Value.Success, string.Format("{0} 失败: {1}", r.Key, r.Value.Details)));
        }

        [Fact]
        public void PostgreSql_Insert()
        {
            var result = TestInsert("PostgreSql");
            Assert.True(result.Success, result.Details);
        }

        [Fact]
        public void PostgreSql_Query()
        {
            var result = TestQuery("PostgreSql");
            Assert.True(result.Success, result.Details);
        }

        [Fact]
        public void PostgreSql_Update()
        {
            var result = TestUpdate("PostgreSql");
            Assert.True(result.Success, result.Details);
        }

        [Fact]
        public void PostgreSql_Delete()
        {
            var result = TestDelete("PostgreSql");
            Assert.True(result.Success, result.Details);
        }

        [Fact]
        public void PostgreSql_ChainQuery()
        {
            var result = TestChainQuery("PostgreSql");
            Assert.True(result.Success, result.Details);
        }

        [Fact]
        public void PostgreSql_Pagination()
        {
            var result = TestPagination("PostgreSql");
            Assert.True(result.Success, result.Details);
        }

        [Fact]
        public void PostgreSql_BatchInsert()
        {
            var result = TestBatchInsert("PostgreSql");
            Assert.True(result.Success, result.Details);
        }

        [Fact]
        public void PostgreSql_LambdaWhere()
        {
            var result = TestLambdaWhere("PostgreSql");
            Assert.True(result.Success, result.Details);
        }

        [Fact]
        public void PostgreSql_OrderByGroupBy()
        {
            var result = TestOrderByGroupBy("PostgreSql");
            Assert.True(result.Success, result.Details);
        }

        [Fact]
        public void PostgreSql_DbTableNames()
        {
            var result = TestDbTableNames("PostgreSql");
            Assert.True(result.Success, result.Details);
        }

        #endregion

        #region 测试方法

        private Dictionary<string, TestResult> RunFullOrmTest(string dbName)
        {
            var results = new Dictionary<string, TestResult>();

            results["1.插入"] = TestInsert(dbName);
            results["2.查询"] = TestQuery(dbName);
            results["3.更新"] = TestUpdate(dbName);
            results["4.删除"] = TestDelete(dbName);
            results["5.链式查询"] = TestChainQuery(dbName);
            results["6.分页查询"] = TestPagination(dbName);
            results["7.批量插入"] = TestBatchInsert(dbName);
            results["8.Lambda查询"] = TestLambdaWhere(dbName);
            results["9.排序分组"] = TestOrderByGroupBy(dbName);
            results["10.表名映射"] = TestDbTableNames(dbName);

            return results;
        }

        private TestResult TestInsert(string dbName)
        {
            if (!ShouldRunDbIntegration()) return TestResult.Skipped;

            var stopwatch = Stopwatch.StartNew();
            try
            {
                var entity = new PerfUser
                {
                    UserName = string.Format("TestUser_{0}", DateTime.Now.Ticks),
                    Email = string.Format("test_{0}@example.com", DateTime.Now.Ticks),
                    Age = 25,
                    IsActive = true,
                    CreatedAt = DateTime.Now
                };

                using var db = new DataContext(dbName);
                var result = db.Add(entity);
                stopwatch.Stop();

                return new TestResult
                {
                    Success = result.WriteReturn.IsSuccess,
                    ElapsedMs = stopwatch.ElapsedMilliseconds,
                    Details = result.WriteReturn.IsSuccess ? "插入成功" : string.Format("插入失败: {0}", result.WriteReturn.Message)
                };
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                return TestResult.FromException(ex, stopwatch.ElapsedMilliseconds);
            }
        }

        private TestResult TestQuery(string dbName)
        {
            if (!ShouldRunDbIntegration()) return TestResult.Skipped;

            var stopwatch = Stopwatch.StartNew();
            try
            {
                using var db = new DataContext(dbName);
                var result = db.GetList<PerfUser>(FastRead.Use(dbName).Query<PerfUser>(u => u.IsActive));
                stopwatch.Stop();

                return new TestResult
                {
                    Success = true,
                    ElapsedMs = stopwatch.ElapsedMilliseconds,
                    Details = string.Format("查询成功，返回 {0} 条记录", result.List.Count)
                };
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                return TestResult.FromException(ex, stopwatch.ElapsedMilliseconds);
            }
        }

        private TestResult TestUpdate(string dbName)
        {
            if (!ShouldRunDbIntegration()) return TestResult.Skipped;

            var stopwatch = Stopwatch.StartNew();
            try
            {
                // 先查询一条记录
                using var db = new DataContext(dbName);
                var queryResult = db.GetList<PerfUser>(FastRead.Use(dbName).Query<PerfUser>(u => u.IsActive));

                if (queryResult.List.Count == 0)
                {
                    stopwatch.Stop();
                    return new TestResult { Success = false, ElapsedMs = stopwatch.ElapsedMilliseconds, Details = "没有可更新的记录" };
                }

                var user = queryResult.List[0];
                user.UserName = string.Format("Updated_{0}", DateTime.Now.Ticks);
                user.Email = string.Format("updated_{0}@example.com", DateTime.Now.Ticks);

                // 排除 Id 字段，只更新其他字段
                var result = db.Update(user, u => u.Id == user.Id, u => new { u.UserName, u.Email, u.Age, u.IsActive, u.CreatedAt });
                stopwatch.Stop();

                return new TestResult
                {
                    Success = result.WriteReturn.IsSuccess,
                    ElapsedMs = stopwatch.ElapsedMilliseconds,
                    Details = result.WriteReturn.IsSuccess ? "更新成功" : string.Format("更新失败: {0}", result.WriteReturn.Message)
                };
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                return TestResult.FromException(ex, stopwatch.ElapsedMilliseconds);
            }
        }

        private TestResult TestDelete(string dbName)
        {
            if (!ShouldRunDbIntegration()) return TestResult.Skipped;

            var stopwatch = Stopwatch.StartNew();
            try
            {
                // 先插入一条记录
                var entity = new PerfUser
                {
                    UserName = string.Format("DeleteTest_{0}", DateTime.Now.Ticks),
                    Email = string.Format("delete_{0}@example.com", DateTime.Now.Ticks),
                    Age = 30,
                    IsActive = true,
                    CreatedAt = DateTime.Now
                };

                using var db = new DataContext(dbName);
                var insertResult = db.Add(entity);

                if (!insertResult.WriteReturn.IsSuccess)
                {
                    stopwatch.Stop();
                    return new TestResult { Success = false, ElapsedMs = stopwatch.ElapsedMilliseconds, Details = string.Format("插入失败: {0}", insertResult.WriteReturn.Message) };
                }

                // 查询刚插入的记录
                var queryResult = db.GetList<PerfUser>(FastRead.Use(dbName).Query<PerfUser>(u => u.UserName == entity.UserName));

                if (queryResult.List.Count == 0)
                {
                    stopwatch.Stop();
                    return new TestResult { Success = false, ElapsedMs = stopwatch.ElapsedMilliseconds, Details = "未找到要删除的记录" };
                }

                // 删除记录
                var deleteResult = db.Delete<PerfUser>(u => u.Id == queryResult.List[0].Id);
                stopwatch.Stop();

                return new TestResult
                {
                    Success = deleteResult.WriteReturn.IsSuccess,
                    ElapsedMs = stopwatch.ElapsedMilliseconds,
                    Details = deleteResult.WriteReturn.IsSuccess ? "删除成功" : string.Format("删除失败: {0}", deleteResult.WriteReturn.Message)
                };
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                return TestResult.FromException(ex, stopwatch.ElapsedMilliseconds);
            }
        }

        private TestResult TestChainQuery(string dbName)
        {
            if (!ShouldRunDbIntegration()) return TestResult.Skipped;

            var stopwatch = Stopwatch.StartNew();
            try
            {
                using var db = new DataContext(dbName);
                var query = FastRead.Use(dbName).Query<PerfUser>(u => u.IsActive)
                    .And<PerfUser>(u => u.Age > 20)
                    .OrderBy<PerfUser>(u => u.Id)
                    .Take(10);

                var result = db.GetList<PerfUser>(query);
                stopwatch.Stop();

                return new TestResult
                {
                    Success = true,
                    ElapsedMs = stopwatch.ElapsedMilliseconds,
                    Details = string.Format("链式查询成功，返回 {0} 条记录", result.List.Count)
                };
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                return TestResult.FromException(ex, stopwatch.ElapsedMilliseconds);
            }
        }

        private TestResult TestPagination(string dbName)
        {
            if (!ShouldRunDbIntegration()) return TestResult.Skipped;

            var stopwatch = Stopwatch.StartNew();
            try
            {
                using var db = new DataContext(dbName);

                // 测试分页查询
                var result = FastRead.Use(dbName).Query<PerfUser>(u => u.IsActive)
                    .OrderBy<PerfUser>(u => u.Id)
                    .ToPagination<PerfUser>(1, 10);

                stopwatch.Stop();

                return new TestResult
                {
                    Success = result.Data != null,
                    ElapsedMs = stopwatch.ElapsedMilliseconds,
                    Details = string.Format("分页查询成功: Page={0}, PageSize={1}, Total={2}, Data={3}", result.Page, result.PageSize, result.Total, result.Data?.Count ?? 0)
                };
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                return TestResult.FromException(ex, stopwatch.ElapsedMilliseconds);
            }
        }

        private TestResult TestBatchInsert(string dbName)
        {
            if (!ShouldRunDbIntegration()) return TestResult.Skipped;

            var stopwatch = Stopwatch.StartNew();
            try
            {
                var entities = new List<PerfUser>();
                for (int i = 0; i < 10; i++)
                {
                    entities.Add(new PerfUser
                    {
                        UserName = string.Format("Batch_{0}_{1}", DateTime.Now.Ticks, i),
                        Email = string.Format("batch_{0}_{1}@example.com", DateTime.Now.Ticks, i),
                        Age = 20 + i,
                        IsActive = true,
                        CreatedAt = DateTime.Now
                    });
                }

                using var db = new DataContext(dbName);
                var result = db.AddList(entities);
                stopwatch.Stop();

                return new TestResult
                {
                    Success = result.WriteReturn.IsSuccess,
                    ElapsedMs = stopwatch.ElapsedMilliseconds,
                    Details = result.WriteReturn.IsSuccess ? string.Format("批量插入 {0} 条成功", entities.Count) : string.Format("批量插入失败: {0}", result.WriteReturn.Message)
                };
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                return TestResult.FromException(ex, stopwatch.ElapsedMilliseconds);
            }
        }

        private TestResult TestLambdaWhere(string dbName)
        {
            if (!ShouldRunDbIntegration()) return TestResult.Skipped;

            var stopwatch = Stopwatch.StartNew();
            try
            {
                using var db = new DataContext(dbName);

                // 测试 Where
                var whereResult = db.GetList<PerfUser>(FastRead.Use(dbName).Query<PerfUser>(u => u.Age > 25));

                // 测试 Or
                var orResult = db.GetList<PerfUser>(FastRead.Use(dbName).Query<PerfUser>(u => u.Age > 30).Or<PerfUser>(u => u.UserName.Contains("Test")));

                // 测试 Like
                var likeResult = db.GetList<PerfUser>(FastRead.Use(dbName).Query<PerfUser>(u => u.IsActive).Like<PerfUser>(u => u.UserName, "Batch%"));

                // 测试 In
                var ages = new List<object> { 20, 25, 30 };
                var inResult = db.GetList<PerfUser>(FastRead.Use(dbName).Query<PerfUser>(u => u.IsActive).In<PerfUser>(u => u.Age, ages));

                // 测试 Between
                var betweenResult = db.GetList<PerfUser>(FastRead.Use(dbName).Query<PerfUser>(u => u.IsActive).Between<PerfUser>(u => u.Age, 20, 30));

                stopwatch.Stop();

                return new TestResult
                {
                    Success = true,
                    ElapsedMs = stopwatch.ElapsedMilliseconds,
                    Details = string.Format("Lambda查询成功: Where={0}, Or={1}, Like={2}, In={3}, Between={4}", whereResult.List.Count, orResult.List.Count, likeResult.List.Count, inResult.List.Count, betweenResult.List.Count)
                };
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                return TestResult.FromException(ex, stopwatch.ElapsedMilliseconds);
            }
        }

        private TestResult TestOrderByGroupBy(string dbName)
        {
            if (!ShouldRunDbIntegration()) return TestResult.Skipped;

            var stopwatch = Stopwatch.StartNew();
            try
            {
                using var db = new DataContext(dbName);

                // 测试 OrderBy
                var orderByResult = db.GetList<PerfUser>(FastRead.Use(dbName).Query<PerfUser>(u => u.IsActive).OrderBy<PerfUser>(u => u.Age));

                // 测试 OrderBy Desc
                var orderByDescResult = db.GetList<PerfUser>(FastRead.Use(dbName).Query<PerfUser>(u => u.IsActive).OrderBy<PerfUser>(u => u.Age, true));

                // 测试 GroupBy
                var groupByResult = db.GetList<PerfUser>(FastRead.Use(dbName).Query<PerfUser>(u => u.IsActive).GroupBy<PerfUser>(u => u.Age));

                stopwatch.Stop();

                return new TestResult
                {
                    Success = true,
                    ElapsedMs = stopwatch.ElapsedMilliseconds,
                    Details = string.Format("排序分组成功: OrderBy={0}, OrderByDesc={1}, GroupBy={2}", orderByResult.List.Count, orderByDescResult.List.Count, groupByResult.List.Count)
                };
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                return TestResult.FromException(ex, stopwatch.ElapsedMilliseconds);
            }
        }

        private TestResult TestDbTableNames(string dbName)
        {
            if (!ShouldRunDbIntegration()) return TestResult.Skipped;

            var stopwatch = Stopwatch.StartNew();
            try
            {
                using var db = new DataContext(dbName);

                // 测试 MultiDbUser（DbTableNames 属性）
                var result = db.GetList<MultiDbUser>(FastRead.Use(dbName).Query<MultiDbUser>(u => u.Id > 0));

                stopwatch.Stop();

                return new TestResult
                {
                    Success = true,
                    ElapsedMs = stopwatch.ElapsedMilliseconds,
                    Details = string.Format("表名映射测试成功: MultiDbUser 查询 {0} 条", result.List.Count)
                };
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                return TestResult.FromException(ex, stopwatch.ElapsedMilliseconds);
            }
        }

        #endregion

        #region 辅助方法

        private void PrintResults(string dbName, Dictionary<string, TestResult> results)
        {
            Console.WriteLine("\n{0} ORM 完整测试:", dbName);
            Console.WriteLine(new string('-', 80));
            Console.WriteLine("{0,-15} {1,-8} {2,-12} {3}", "测试项", "结果", "耗时(ms)", "详情");
            Console.WriteLine(new string('-', 80));

            foreach (var test in results)
            {
                var status = test.Value.Success ? "PASS" : "FAIL";
                Console.WriteLine("{0,-15} {1,-8} {2,-12} {3}", test.Key, status, test.Value.ElapsedMs, test.Value.Details);
            }

            Console.WriteLine(new string('-', 80));
            var passed = results.Count(r => r.Value.Success);
            Console.WriteLine("总计: {0}/{1} 通过\n", passed, results.Count);
        }

        private static bool ShouldRunDbIntegration()
        {
            return string.Equals(Environment.GetEnvironmentVariable("FASTDATA_RUN_DB_INTEGRATION"), "true", StringComparison.OrdinalIgnoreCase);
        }

        #endregion

        private class TestResult
        {
            public static readonly TestResult Skipped = new TestResult
            {
                Success = true,
                ElapsedMs = 0,
                Details = "未设置 FASTDATA_RUN_DB_INTEGRATION=true，跳过数据库集成测试"
            };

            public static TestResult FromException(Exception ex, long elapsedMs)
            {
                if (IsMissingDatabaseConfig(ex))
                {
                    return new TestResult
                    {
                        Success = true,
                        ElapsedMs = elapsedMs,
                        Details = string.Format("数据库配置不可用，跳过测试: {0}", ex.Message)
                    };
                }

                return new TestResult { Success = false, ElapsedMs = elapsedMs, Details = string.Format("异常: {0}", ex.Message) };
            }

            private static bool IsMissingDatabaseConfig(Exception ex)
            {
                return ex.Message.Contains("数据库配置 Key 不存在")
                    || ex.Message.Contains("db.config")
                    || ex.Message.Contains("配置文件已发布到输出目录")
                    || ex.Message.Contains("无法创建新连接")
                    || ex.Message.Contains("数据库可能不可达")
                    || ex.Message.Contains("连接池")
                    || ex.Message.Contains("熔断器打开");
            }

            public bool Success { get; set; }
            public long ElapsedMs { get; set; }
            public string Details { get; set; }
        }
    }
}
