using System.Collections.Generic;
using System.Linq;
using FastData.Context;
using Xunit;

namespace FastData.Tests.Integration
{
    /// <summary>
    /// 多数据库事务测试
    /// </summary>
    [Collection(SharedDatabaseCollection.Name)]
    public class MultiDatabaseTransactionTests
    {
        static MultiDatabaseTransactionTests()
        {
            MultiDatabaseTestHelper.RegisterProviders();
        }

        public static IEnumerable<object[]> GetDatabases() => MultiDatabaseTestHelper.GetRelationalAndSqliteDatabases();

        [Theory]
        [MemberData(nameof(GetDatabases))]
        public void Transaction_Commit_Persists(string dbName)
        {
            if (!MultiDatabaseTestHelper.CanOpenDatabase(dbName)) return;
            var tag = MultiDatabaseTestHelper.NewTag("commit");

            using (var db = new DataContext(dbName))
            {
                db.BeginTrans();
                var result = db.Add(MultiDatabaseTestHelper.CreateUser(tag, 25));
                Assert.True(result.WriteReturn.IsSuccess, result.WriteReturn.Message);
                db.SubmitTrans();
            }

            Assert.Equal(1, FastRead.Use(dbName).Query<PerfUser>(u => u.UserName == tag).ToCount());
            FastWrite.Delete<PerfUser>(u => u.UserName == tag, key: dbName);
        }

        [Theory]
        [MemberData(nameof(GetDatabases))]
        public void Transaction_Rollback_Discards(string dbName)
        {
            if (!MultiDatabaseTestHelper.CanOpenDatabase(dbName)) return;
            var tag = MultiDatabaseTestHelper.NewTag("rollback");

            using (var db = new DataContext(dbName))
            {
                db.BeginTrans();
                var result = db.Add(MultiDatabaseTestHelper.CreateUser(tag, 25));
                Assert.True(result.WriteReturn.IsSuccess, result.WriteReturn.Message);
                db.RollbackTrans();
            }

            Assert.Equal(0, FastRead.Use(dbName).Query<PerfUser>(u => u.UserName == tag).ToCount());
        }

        [Theory]
        [MemberData(nameof(GetDatabases))]
        public void Transaction_BatchInsert(string dbName)
        {
            if (!MultiDatabaseTestHelper.CanOpenDatabase(dbName)) return;
            var tag = MultiDatabaseTestHelper.NewTag("batch");
            var users = Enumerable.Range(0, 5).Select(i => MultiDatabaseTestHelper.CreateUser($"{tag}_{i}", 20 + i)).ToList();

            using (var db = new DataContext(dbName))
            {
                db.BeginTrans();
                foreach (var u in users)
                {
                    var r = db.Add(u);
                    Assert.True(r.WriteReturn.IsSuccess, r.WriteReturn.Message);
                }
                db.SubmitTrans();
            }

            Assert.Equal(5, FastRead.Use(dbName).Query<PerfUser>(u => u.UserName.Contains(tag)).ToCount());
            FastWrite.Delete<PerfUser>(u => u.UserName.Contains(tag), key: dbName);
        }

        [Theory]
        [MemberData(nameof(GetDatabases))]
        public void Transaction_UpdateWithin(string dbName)
        {
            if (!MultiDatabaseTestHelper.CanOpenDatabase(dbName)) return;
            var tag = MultiDatabaseTestHelper.NewTag("update");

            using (var db = new DataContext(dbName))
            {
                db.BeginTrans();
                var addResult = db.Add(MultiDatabaseTestHelper.CreateUser(tag, 25));
                Assert.True(addResult.WriteReturn.IsSuccess, addResult.WriteReturn.Message);
                var updateResult = db.Update(new PerfUser { Age = 30 }, u => u.UserName == tag, u => new { u.Age });
                Assert.True(updateResult.WriteReturn.IsSuccess, updateResult.WriteReturn.Message);
                db.SubmitTrans();
            }

            var user = FastRead.Use(dbName).Query<PerfUser>(u => u.UserName == tag).ToItem<PerfUser>();
            Assert.NotNull(user);
            Assert.Equal(30, user.Age);
            FastWrite.Delete<PerfUser>(u => u.UserName == tag, key: dbName);
        }
    }
}
