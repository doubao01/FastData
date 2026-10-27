using System;
using System.Collections.Generic;
using System.Linq;
using FastUntility.Page;
using Xunit;

namespace FastData.Tests.Integration
{
    /// <summary>
    /// 多数据库边界测试
    /// </summary>
    public class MultiDatabaseEdgeTests
    {
        static MultiDatabaseEdgeTests()
        {
            MultiDatabaseTestHelper.RegisterProviders();
        }

        public static IEnumerable<object[]> GetDatabases() => MultiDatabaseTestHelper.GetRelationalAndSqliteDatabases();

        [Theory]
        [MemberData(nameof(GetDatabases))]
        public void Edge_EmptyResult(string dbName)
        {
            if (!MultiDatabaseTestHelper.CanOpenDatabase(dbName)) return;
            var tag = MultiDatabaseTestHelper.NewTag("none");
            var list = FastRead.Use(dbName).Query<PerfUser>(u => u.UserName == tag).ToList<PerfUser>();
            Assert.NotNull(list);
            Assert.Empty(list);
        }

        [Theory]
        [MemberData(nameof(GetDatabases))]
        public void Edge_SpecialChars(string dbName)
        {
            if (!MultiDatabaseTestHelper.CanOpenDatabase(dbName)) return;
            var tag = MultiDatabaseTestHelper.NewTag("special");
            var email = "test+filter@example.com&_q='\"";
            var r = FastWrite.Add(new PerfUser { UserName = tag, Email = email, Age = 25, IsActive = true, CreatedAt = DateTime.Now }, key: dbName);
            Assert.True(r.IsSuccess, r.Message);

            var u = FastRead.Use(dbName).Query<PerfUser>(u => u.UserName == tag).ToItem<PerfUser>();
            Assert.NotNull(u);
            Assert.Equal(email, u.Email);
            FastWrite.Delete<PerfUser>(u => u.UserName == tag, key: dbName);
        }

        [Theory]
        [MemberData(nameof(GetDatabases))]
        public void Edge_Unicode(string dbName)
        {
            if (!MultiDatabaseTestHelper.CanOpenDatabase(dbName)) return;
            foreach (var name in new[] { "中文", "日本語", "한국어", "Ελληνικά" })
            {
                var tag = MultiDatabaseTestHelper.NewTag(name);
                var r = FastWrite.Add(new PerfUser { UserName = tag, Email = $"{tag}@ex.com", Age = 25, IsActive = true, CreatedAt = DateTime.Now }, key: dbName);
                Assert.True(r.IsSuccess, $"{name}: {r.Message}");

                var u = FastRead.Use(dbName).Query<PerfUser>(u => u.UserName == tag).ToItem<PerfUser>();
                Assert.NotNull(u);
                FastWrite.Delete<PerfUser>(u => u.UserName == tag, key: dbName);
            }
        }

        [Theory]
        [MemberData(nameof(GetDatabases))]
        public void Edge_LongString(string dbName)
        {
            if (!MultiDatabaseTestHelper.CanOpenDatabase(dbName)) return;
            var tag = MultiDatabaseTestHelper.NewTag("long");
            var longEmail = new string('x', 100) + "@example.com";
            var r = FastWrite.Add(new PerfUser { UserName = tag, Email = longEmail, Age = 25, IsActive = true, CreatedAt = DateTime.Now }, key: dbName);
            Assert.True(r.IsSuccess, r.Message);

            var u = FastRead.Use(dbName).Query<PerfUser>(u => u.UserName == tag).ToItem<PerfUser>();
            Assert.NotNull(u);
            Assert.Equal(longEmail.Length, u.Email.Length);
            FastWrite.Delete<PerfUser>(u => u.UserName == tag, key: dbName);
        }

        [Theory]
        [MemberData(nameof(GetDatabases))]
        public void Edge_BulkInsert(string dbName)
        {
            if (!MultiDatabaseTestHelper.CanOpenDatabase(dbName)) return;
            var tag = MultiDatabaseTestHelper.NewTag("bulk");
            var users = Enumerable.Range(0, 100).Select(i => MultiDatabaseTestHelper.CreateUser($"{tag}_{i}", 20 + i)).ToList();
            var r = FastWrite.AddList(users, key: dbName);
            Assert.True(r.IsSuccess, r.Message);

            Assert.Equal(100, FastRead.Use(dbName).Query<PerfUser>(u => u.UserName.Contains(tag)).ToCount());
            FastWrite.Delete<PerfUser>(u => u.UserName.Contains(tag), key: dbName);
        }

        [Theory]
        [MemberData(nameof(GetDatabases))]
        public void Edge_Paging(string dbName)
        {
            if (!MultiDatabaseTestHelper.CanOpenDatabase(dbName)) return;
            var tag = MultiDatabaseTestHelper.NewTag("page");
            for (int i = 0; i < 15; i++)
            {
                var addResult = FastWrite.Add(MultiDatabaseTestHelper.CreateUser($"{tag}_{i}", 20 + i), key: dbName);
                Assert.True(addResult.IsSuccess, addResult.Message);
            }

            var page = FastRead.Use(dbName)
                .Query<PerfUser>(u => u.UserName.Contains(tag))
                .OrderBy<PerfUser>(u => u.Id)
                .ToPage<PerfUser>(new PageModel { PageId = 1, PageSize = 5 });

            Assert.NotNull(page);
            // 失败时把实际 SQL 与总数一起报出来：分页返回 0 行既可能是条件无匹配，
            // 也可能是 SQL 生成错误（如 LIMIT 位置、row_number 派生表别名问题）。
            Assert.True(page.list.Count == 5,
                string.Format("{0} 分页应返回 5 行，实际 {1} 行；TotalRecord={2}；SQL: {3}",
                    dbName, page.list.Count, page.pModel.TotalRecord, page.Sql));
            Assert.Equal(15, page.pModel.TotalRecord);
            Assert.Equal(3, page.pModel.TotalPage);

            FastWrite.Delete<PerfUser>(u => u.UserName.Contains(tag), key: dbName);
        }

        [Theory]
        [MemberData(nameof(GetDatabases))]
        public void Edge_Boolean(string dbName)
        {
            if (!MultiDatabaseTestHelper.CanOpenDatabase(dbName)) return;
            var tagT = MultiDatabaseTestHelper.NewTag("boolt");
            var tagF = MultiDatabaseTestHelper.NewTag("boolf");

            var trueResult = FastWrite.Add(new PerfUser { UserName = tagT, Email = $"{tagT}@ex.com", Age = 25, IsActive = true, CreatedAt = DateTime.Now }, key: dbName);
            Assert.True(trueResult.IsSuccess, trueResult.Message);
            var falseResult = FastWrite.Add(new PerfUser { UserName = tagF, Email = $"{tagF}@ex.com", Age = 25, IsActive = false, CreatedAt = DateTime.Now }, key: dbName);
            Assert.True(falseResult.IsSuccess, falseResult.Message);

            var active = FastRead.Use(dbName).Query<PerfUser>(u => u.IsActive).ToCount();
            var inactive = FastRead.Use(dbName).Query<PerfUser>(u => !u.IsActive).ToCount();

            Assert.True(active >= 1);
            Assert.True(inactive >= 1);

            FastWrite.Delete<PerfUser>(u => u.UserName == tagT || u.UserName == tagF, key: dbName);
        }
    }
}
