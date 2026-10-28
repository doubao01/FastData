using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
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
    /// 性能测试用户实体
    /// </summary>
    [Table(Name = "perf_users")]
    [Collection(SharedDatabaseCollection.Name)]
    public class PerfUser
    {
        /// <summary>
        /// 主键
        /// </summary>
        [Column(IsKey = true, IsIdentity = true)]
        public int Id { get; set; }

        /// <summary>
        /// 用户名
        /// </summary>
        public string UserName { get; set; }

        /// <summary>
        /// 邮箱
        /// </summary>
        public string Email { get; set; }

        /// <summary>
        /// 年龄
        /// </summary>
        public int Age { get; set; }

        /// <summary>
        /// 是否激活
        /// </summary>
        public bool IsActive { get; set; }

        /// <summary>
        /// 创建时间
        /// </summary>
        public DateTime CreatedAt { get; set; }
    }

    /// <summary>
    /// 多数据库用户实体
    /// 
    /// 使用 DbTableNames 特性指定不同数据库的表名映射。
    /// </summary>
    [Table(DbTableNames = "SqlServer.Users,MySql.user_info,PostgreSQL.tb_users")]
    public class MultiDbUser
    {
        /// <summary>
        /// 主键
        /// </summary>
        [Column(IsIdentity = true)]
        public int Id { get; set; }

        /// <summary>
        /// 用户名
        /// </summary>
        public string UserName { get; set; }

        /// <summary>
        /// 邮箱
        /// </summary>
        public string Email { get; set; }
    }

    /// <summary>
    /// 混合数据库用户实体
    /// 
    /// 同时使用 Name 和 DbTableNames 特性。
    /// </summary>
    [Table(Name = "default_users", DbTableNames = "SqlServer.Users,MySql.user_info")]
    public class MixedDbUser
    {
        /// <summary>
        /// 主键
        /// </summary>
        [Column(IsIdentity = true)]
        public int Id { get; set; }

        /// <summary>
        /// 用户名
        /// </summary>
        public string UserName { get; set; }
    }

    /// <summary>
    /// 测试结果
    /// </summary>
    public class TestResult
    {
        /// <summary>
        /// 是否成功
        /// </summary>
        public bool Success { get; set; }

        /// <summary>
        /// 耗时（毫秒）
        /// </summary>
        public long ElapsedMs { get; set; }

        /// <summary>
        /// 每秒操作数
        /// </summary>
        public double OpsPerSecond { get; set; }

        /// <summary>
        /// 详细信息
        /// </summary>
        public string Details { get; set; }
    }

    /// <summary>
    /// 数据库集成测试
    /// 
    /// 测试多种数据库的 CRUD 操作、并发读写、分页查询等功能。
    /// </summary>
    public class DatabaseIntegrationTests
    {
        public DatabaseIntegrationTests()
        {
            // 注册数据库工厂
            DbProviderFactories.RegisterFactory("Microsoft.Data.SqlClient", Microsoft.Data.SqlClient.SqlClientFactory.Instance);
            DbProviderFactories.RegisterFactory("MySql.Data.MySqlClient", MySql.Data.MySqlClient.MySqlClientFactory.Instance);
            DbProviderFactories.RegisterFactory("Npgsql", Npgsql.NpgsqlFactory.Instance);
        }

        /// <summary>
        /// 完整集成测试
        /// </summary>
        /// <param name="dbName">数据库名称</param>
        [Theory]
        [InlineData("SqlServer")]
        [InlineData("MySql")]
        [InlineData("PostgreSql")]
        public void FullIntegrationTest(string dbName)
        {
            if (!IsDatabaseAvailable(dbName))
            {
                Console.WriteLine("{0} 不可用，跳过测试", dbName);
                return;
            }

            var results = new Dictionary<string, TestResult>();

            // 单线程测试
            results["单条插入"] = TestSingleInsert(dbName, 10);
            results["查询"] = TestQuery(dbName);
            results["删除"] = TestDelete(dbName);
            results["链式查询"] = TestChainQuery(dbName);
            results["分页查询"] = TestPagination(dbName);

            // 30线程并发测试
            results["并发读写"] = TestConcurrentReadWrite(dbName, 30, 10);

            // 打印汇总
            PrintSummary(dbName, results);
        }

        /// <summary>
        /// 检查数据库是否可用
        /// </summary>
        /// <param name="dbName">数据库名称</param>
        /// <returns>是否可用</returns>
        private bool IsDatabaseAvailable(string dbName)
        {
            try
            {
                var connStr = GetConnectionString(dbName);
                if (string.IsNullOrEmpty(connStr))
                    return false;

                using var conn = DbProviderFactories.GetFactory(GetProviderName(dbName)).CreateConnection();
                conn.ConnectionString = connStr;
                conn.Open();
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 获取连接字符串
        /// </summary>
        /// <param name="dbName">数据库名称</param>
        /// <returns>连接字符串</returns>
        private string GetConnectionString(string dbName)
        {
            return dbName switch
            {
                "SqlServer" => Environment.GetEnvironmentVariable("FASTDATA_SQLSERVER_CONNSTR"),
                "MySql" => Environment.GetEnvironmentVariable("FASTDATA_MYSQL_CONNSTR"),
                "PostgreSql" => Environment.GetEnvironmentVariable("FASTDATA_POSTGRESQL_CONNSTR"),
                _ => null
            };
        }

        /// <summary>
        /// 获取提供程序名称
        /// </summary>
        /// <param name="dbName">数据库名称</param>
        /// <returns>提供程序名称</returns>
        private string GetProviderName(string dbName)
        {
            return dbName switch
            {
                "SqlServer" => "Microsoft.Data.SqlClient",
                "MySql" => "MySql.Data.MySqlClient",
                "PostgreSql" => "Npgsql",
                _ => throw new ArgumentException(string.Format("不支持的数据库: {0}", dbName))
            };
        }

        /// <summary>
        /// 测试单条插入
        /// </summary>
        /// <param name="dbName">数据库名称</param>
        /// <param name="count">插入记录数</param>
        /// <returns>测试结果</returns>
        private TestResult TestSingleInsert(string dbName, int count)
        {
            var sw = Stopwatch.StartNew();
            var success = 0;
            var errors = new List<string>();

            try
            {
                for (int i = 0; i < count; i++)
                {
                    var user = new PerfUser
                    {
                        UserName = string.Format("test_user_{0:N}", Guid.NewGuid()),
                        Email = string.Format("test_{0:N}@example.com", Guid.NewGuid()),
                        Age = 20 + i % 50,
                        IsActive = true,
                        CreatedAt = DateTime.Now
                    };

                    var result = FastWrite.Add(user);
                    if (result.IsSuccess)
                        success++;
                    else
                        errors.Add(string.Format("插入失败: {0}", result.Message));
                }
            }
            catch (Exception ex)
            {
                errors.Add(ex.Message);
            }

            sw.Stop();

            return new TestResult
            {
                Success = success == count,
                ElapsedMs = sw.ElapsedMilliseconds,
                OpsPerSecond = success / sw.Elapsed.TotalSeconds,
                Details = string.Format("成功: {0}/{1}, 错误: {2}", success, count, errors.Count)
            };
        }

        /// <summary>
        /// 测试查询
        /// </summary>
        /// <param name="dbName">数据库名称</param>
        /// <returns>测试结果</returns>
        private TestResult TestQuery(string dbName)
        {
            var sw = Stopwatch.StartNew();

            try
            {
                var users = FastRead.Query<PerfUser>(u => u.IsActive)
                    .Take(100)
                    .ToList<PerfUser>();

                sw.Stop();

                return new TestResult
                {
                    Success = true,
                    ElapsedMs = sw.ElapsedMilliseconds,
                    Details = string.Format("查询到 {0} 条记录", users.Count)
                };
            }
            catch (Exception ex)
            {
                sw.Stop();
                return new TestResult
                {
                    Success = false,
                    ElapsedMs = sw.ElapsedMilliseconds,
                    Details = ex.Message
                };
            }
        }

        /// <summary>
        /// 测试删除
        /// </summary>
        /// <param name="dbName">数据库名称</param>
        /// <returns>测试结果</returns>
        private TestResult TestDelete(string dbName)
        {
            var sw = Stopwatch.StartNew();

            try
            {
                var result = FastWrite.Delete<PerfUser>(u => u.UserName.StartsWith("test_user_"));
                sw.Stop();

                return new TestResult
                {
                    Success = result.IsSuccess,
                    ElapsedMs = sw.ElapsedMilliseconds,
                    Details = "删除成功"
                };
            }
            catch (Exception ex)
            {
                sw.Stop();
                return new TestResult
                {
                    Success = false,
                    ElapsedMs = sw.ElapsedMilliseconds,
                    Details = ex.Message
                };
            }
        }

        /// <summary>
        /// 测试链式查询
        /// </summary>
        /// <param name="dbName">数据库名称</param>
        /// <returns>测试结果</returns>
        private TestResult TestChainQuery(string dbName)
        {
            var sw = Stopwatch.StartNew();

            try
            {
                var users = FastRead.Query<PerfUser>(u => u.IsActive)
                    .Where(u => u.Age > 25)
                    .OrderByDescending(u => u.CreatedAt)
                    .Take(10)
                    .ToList<PerfUser>();

                sw.Stop();

                return new TestResult
                {
                    Success = true,
                    ElapsedMs = sw.ElapsedMilliseconds,
                    Details = string.Format("查询到 {0} 条记录", users.Count)
                };
            }
            catch (Exception ex)
            {
                sw.Stop();
                return new TestResult
                {
                    Success = false,
                    ElapsedMs = sw.ElapsedMilliseconds,
                    Details = ex.Message
                };
            }
        }

        /// <summary>
        /// 测试分页查询
        /// </summary>
        /// <param name="dbName">数据库名称</param>
        /// <returns>测试结果</returns>
        private TestResult TestPagination(string dbName)
        {
            var sw = Stopwatch.StartNew();

            try
            {
                var pagination = FastRead.Query<PerfUser>(u => u.IsActive)
                    .OrderBy<PerfUser>(u => u.Id)
                    .ToPagination<PerfUser>(1, 10);

                sw.Stop();

                return new TestResult
                {
                    Success = true,
                    ElapsedMs = sw.ElapsedMilliseconds,
                    Details = string.Format("总记录数: {0}, 当前页: {1}", pagination.Total, pagination.Data.Count)
                };
            }
            catch (Exception ex)
            {
                sw.Stop();
                return new TestResult
                {
                    Success = false,
                    ElapsedMs = sw.ElapsedMilliseconds,
                    Details = ex.Message
                };
            }
        }

        /// <summary>
        /// 测试并发读写
        /// </summary>
        /// <param name="dbName">数据库名称</param>
        /// <param name="threadCount">线程数</param>
        /// <param name="opsPerThread">每线程操作数</param>
        /// <returns>测试结果</returns>
        private TestResult TestConcurrentReadWrite(string dbName, int threadCount, int opsPerThread)
        {
            var sw = Stopwatch.StartNew();
            var tasks = new List<Task>();
            var successCount = 0;
            var errorCount = 0;

            for (int i = 0; i < threadCount; i++)
            {
                var threadId = i;
                tasks.Add(Task.Run(() =>
                {
                    for (int j = 0; j < opsPerThread; j++)
                    {
                        try
                        {
                            // 插入
                            var user = new PerfUser
                            {
                                UserName = string.Format("concurrent_user_{0}_{1}", threadId, j),
                                Email = string.Format("concurrent_{0}_{1}@example.com", threadId, j),
                                Age = 20 + j % 50,
                                IsActive = true,
                                CreatedAt = DateTime.Now
                            };

                            var addResult = FastWrite.Add(user);
                            if (addResult.IsSuccess)
                            {
                                Interlocked.Increment(ref successCount);

                                // 查询
                                var queryResult = FastRead.Query<PerfUser>(u => u.UserName == user.UserName)
                                    .ToItem<PerfUser>();

                                if (queryResult != null)
                                {
                                    // 更新
                                    queryResult.Age = 30;
                                    FastWrite.Update(queryResult);
                                }
                            }
                            else
                            {
                                Interlocked.Increment(ref errorCount);
                            }
                        }
                        catch
                        {
                            Interlocked.Increment(ref errorCount);
                        }
                    }
                }));
            }

            Task.WaitAll(tasks.ToArray());
            sw.Stop();

            var totalOps = threadCount * opsPerThread;
            return new TestResult
            {
                Success = errorCount == 0,
                ElapsedMs = sw.ElapsedMilliseconds,
                OpsPerSecond = successCount / sw.Elapsed.TotalSeconds,
                Details = string.Format("总操作: {0}, 成功: {1}, 失败: {2}, 线程数: {3}", totalOps, successCount, errorCount, threadCount)
            };
        }

        /// <summary>
        /// 打印测试汇总
        /// </summary>
        /// <param name="dbName">数据库名称</param>
        /// <param name="results">测试结果字典</param>
        private void PrintSummary(string dbName, Dictionary<string, TestResult> results)
        {
            Console.WriteLine("\n=== {0} 测试结果汇总 ===", dbName);
            Console.WriteLine("{0,-15} {1,-8} {2,-12} {3,-12} {4}", "测试项", "结果", "耗时(ms)", "OPS", "详情");
            Console.WriteLine(new string('-', 80));

            foreach (var kvp in results)
            {
                var result = kvp.Value;
                Console.WriteLine("{0,-15} {1,-8} {2,-12} {3:F2,-12} {4}", kvp.Key, (result.Success ? "通过" : "失败"), result.ElapsedMs, result.OpsPerSecond, result.Details);
            }

            var allPassed = results.Values.All(r => r.Success);
            Console.WriteLine("\n总结: {0}", (allPassed ? "全部通过" : "存在失败"));
        }
    }
}
