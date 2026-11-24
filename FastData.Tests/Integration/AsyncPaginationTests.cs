using System.Collections.Generic;
using System.Data.Common;
using System.Threading.Tasks;
using FastData;
using FastData.Context;
using FastUntility.Page;
using Xunit;

namespace FastData.Tests.Integration
{
    /// <summary>
    /// 原生 SQL 异步分页回归（issue #20）。
    ///
    /// 覆盖 SQL Server / MySQL / PostgreSQL / SQLite 四种方言：
    /// 同步与异步分页返回相同的 TotalRecord、TotalPage 与每页行数。
    /// Oracle / DB2 没有 CI 连库环境，不在本组用例中冒充已验证。
    /// </summary>
    public class AsyncPaginationTests
    {
        public static IEnumerable<object[]> GetDatabases() => MultiDatabaseTestHelper.GetRelationalAndSqliteDatabases();

        [Theory]
        [MemberData(nameof(GetDatabases))]
        public async Task GetPageSqlAsync_MatchesSyncPagination(string dbName)
        {
            if (!MultiDatabaseTestHelper.CanOpenDatabase(dbName)) return;

            MultiDatabaseTestHelper.RegisterProviders();
            var tag = MultiDatabaseTestHelper.NewTag("asyncpage", 20);
            var entities = new List<PerfUser>();
            for (var i = 0; i < 7; i++)
                entities.Add(MultiDatabaseTestHelper.CreateUser($"{tag}_{i}", 20 + i));

            using (var db = new DataContext(dbName))
            {
                var added = db.AddList(entities);
                Assert.True(added.WriteReturn.IsSuccess, $"{dbName} seed failed: {added.WriteReturn.Message}");
            }

            try
            {
                var sql = $"select * from perf_users where UserName like '{tag}%'";

                var syncPage1 = new PageModel { PageId = 1, PageSize = 3 };
                var asyncPage1 = new PageModel { PageId = 1, PageSize = 3 };
                var syncPage2 = new PageModel { PageId = 2, PageSize = 3 };
                var asyncPage2 = new PageModel { PageId = 2, PageSize = 3 };

                using (var db = new DataContext(dbName))
                {
                    var sync1 = db.GetPageSql(syncPage1, sql, new DbParameter[0], false);
                    var async1 = await db.GetPageSqlAsync(asyncPage1, sql, new DbParameter[0], false);
                    var sync2 = db.GetPageSql(syncPage2, sql, new DbParameter[0], false);
                    var async2 = await db.GetPageSqlAsync(asyncPage2, sql, new DbParameter[0], false);

                    Assert.Equal(7, syncPage1.TotalRecord);
                    Assert.Equal(7, asyncPage1.TotalRecord);
                    Assert.Equal(syncPage1.TotalPage, asyncPage1.TotalPage);
                    Assert.Equal(sync1.PageResult.list.Count, async1.PageResult.list.Count);
                    Assert.Equal(3, async1.PageResult.list.Count);
                    Assert.Equal(sync2.PageResult.list.Count, async2.PageResult.list.Count);
                    Assert.Equal(3, async2.PageResult.list.Count);
                }

                var typedPage = new PageModel { PageId = 3, PageSize = 3 };
                using (var db = new DataContext(dbName))
                {
                    var typed = await db.GetPageSqlAsync<PerfUser>(typedPage, sql, new DbParameter[0], false);
                    Assert.Equal(7, typedPage.TotalRecord);
                    Assert.Single(typed.PageResult.list);
                }
            }
            finally
            {
                FastWrite.Delete<PerfUser>(u => u.UserName.Contains(tag), key: dbName);
            }
        }
    }
}
