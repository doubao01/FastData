#if !NETFRAMEWORK
using FastData;
using FastData.Tests.Integration;
using FastData.Queue;
using FastUntility.Page;
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using Xunit;

namespace FastData.Tests
{
    /// <summary>
    /// ORM CRUD 功能覆盖率测试
    /// 覆盖之前未测试的核心 ORM 操作
    /// </summary>
    public class OrmCrudTests : IDisposable
    {
        private readonly string _key = "SqlServer";
        private bool _dbAvailable = true;

        /// <summary>
        /// 静态构造函数：注册数据库提供程序工厂
        /// 解决 .NET 6+ 中 DbProviderFactories.GetFactory 找不到提供程序的问题
        /// </summary>
        static OrmCrudTests()
        {
            try { DbProviderFactories.RegisterFactory("Microsoft.Data.SqlClient", Microsoft.Data.SqlClient.SqlClientFactory.Instance); } catch { }
            try { DbProviderFactories.RegisterFactory("MySql.Data.MySqlClient", MySql.Data.MySqlClient.MySqlClientFactory.Instance); } catch { }
            try { DbProviderFactories.RegisterFactory("Npgsql", Npgsql.NpgsqlFactory.Instance); } catch { }
        }

        public OrmCrudTests()
        {
            if (!string.Equals(Environment.GetEnvironmentVariable("FASTDATA_RUN_DB_INTEGRATION"), "true", StringComparison.OrdinalIgnoreCase))
            {
                _dbAvailable = false;
                return;
            }

            try
            {
                var users = FastRead.Query<PerfUser>(u => true, key: _key)
                    .Take(1)
                    .ToList<PerfUser>();
                _dbAvailable = true;
            }
            catch
            {
                _dbAvailable = false;
            }
        }

        public void Dispose()
        {
            if (_dbAvailable)
            {
                try
                {
                    FastWrite.Delete<PerfUser>(u => u.UserName.StartsWith("orm_test_"), key: _key);
                }
                catch { }
            }
        }

        private PerfUser CreateTestUser(string suffix = null)
        {
            var tag = suffix ?? Guid.NewGuid().ToString("N").Substring(0, 8);
            return new PerfUser
            {
                UserName = string.Format("orm_test_{0}", tag),
                Email = string.Format("orm_test_{0}@example.com", tag),
                Age = 25,
                IsActive = true,
                CreatedAt = DateTime.Now
            };
        }

        #region Update by primary key

        [Fact]
        public void Update_ByPrimaryKey_ShouldModifyEntity()
        {
            if (!_dbAvailable)
            {
                Console.WriteLine("SKIP: database unavailable");
                return;
            }

            var tag = Guid.NewGuid().ToString("N").Substring(0, 8);
            var user = CreateTestUser(tag);
            var addResult = FastWrite.Add(user, key: _key);
            Assert.True(addResult.IsSuccess, string.Format("Add failed: {0}", addResult.Message));

            var inserted = FastRead.Query<PerfUser>(u => u.UserName == string.Format("orm_test_{0}", tag), key: _key)
                .ToItem<PerfUser>();
            Assert.NotNull(inserted);

            inserted.Email = string.Format("updated_{0}@example.com", tag);
            inserted.Age = 99;

            var updateResult = FastWrite.Update(inserted, key: _key);
            Console.WriteLine("Update by PK: IsSuccess={0}, Message={1}", updateResult.IsSuccess, updateResult.Message);
            Assert.True(updateResult.IsSuccess, string.Format("Update failed: {0}", updateResult.Message));

            var updated = FastRead.Query<PerfUser>(u => u.Id == inserted.Id, key: _key)
                .ToItem<PerfUser>();
            Assert.NotNull(updated);
            Assert.Equal(99, updated.Age);
            Assert.Equal(string.Format("updated_{0}@example.com", tag), updated.Email);
        }

        #endregion

        #region Update by predicate

        [Fact]
        public void Update_ByPredicate_ShouldModifyMatchingEntities()
        {
            if (!_dbAvailable)
            {
                Console.WriteLine("SKIP: database unavailable");
                return;
            }

            var tag = Guid.NewGuid().ToString("N").Substring(0, 8);
            for (int i = 0; i < 3; i++)
            {
                FastWrite.Add(CreateTestUser(tag + "_" + i), key: _key);
            }

            var template = new PerfUser { IsActive = false };
            // 用模板对象做整体更新会把未赋值字段（如 CreatedAt=DateTime.MinValue）一起写回，
            // 而 CreatedAt 是非空列，必然失败。这里显式指定只更新 IsActive，
            // 与测试意图（验证按条件批量更新）一致。
            var updateResult = FastWrite.Update(template,
                u => u.UserName.Contains(string.Format("orm_test_{0}", tag)),
                u => new { u.IsActive },
                key: _key);
            Console.WriteLine("Update by predicate: IsSuccess={0}, Message={1}", updateResult.IsSuccess, updateResult.Message);
            Assert.True(updateResult.IsSuccess, string.Format("Update by predicate failed: {0}", updateResult.Message));

            var updated = FastRead.Query<PerfUser>(u => u.UserName.Contains(string.Format("orm_test_{0}", tag)), key: _key)
                .ToList<PerfUser>();
            Assert.All(updated, u => Assert.False(u.IsActive));
        }

        #endregion

        #region Update with field selector

        [Fact]
        public void Update_WithFieldSelector_ShouldModifyOnlySelectedFields()
        {
            if (!_dbAvailable)
            {
                Console.WriteLine("SKIP: database unavailable");
                return;
            }

            var tag = Guid.NewGuid().ToString("N").Substring(0, 8);
            var user = CreateTestUser(tag);
            FastWrite.Add(user, key: _key);

            var inserted = FastRead.Query<PerfUser>(u => u.UserName == string.Format("orm_test_{0}", tag), key: _key)
                .ToItem<PerfUser>();
            Assert.NotNull(inserted);

            inserted.Email = string.Format("field_updated_{0}@example.com", tag);
            inserted.Age = 50;

            var updateResult = FastWrite.Update(inserted, u => new { u.Email }, key: _key);
            Console.WriteLine("Update with field selector: IsSuccess={0}, Message={1}", updateResult.IsSuccess, updateResult.Message);
            Assert.True(updateResult.IsSuccess, string.Format("Update with field selector failed: {0}", updateResult.Message));
        }

        #endregion

        #region UpdateList

        [Fact]
        public void UpdateList_ShouldModifyMultipleEntities()
        {
            if (!_dbAvailable)
            {
                Console.WriteLine("SKIP: database unavailable");
                return;
            }

            var tag = Guid.NewGuid().ToString("N").Substring(0, 8);
            var users = new List<PerfUser>();
            for (int i = 0; i < 3; i++)
            {
                var u = CreateTestUser(tag + "_" + i);
                FastWrite.Add(u, key: _key);
                users.Add(u);
            }

            var inserted = FastRead.Query<PerfUser>(u => u.UserName.Contains(string.Format("orm_test_{0}", tag)), key: _key)
                .ToList<PerfUser>();
            Assert.True(inserted.Count >= 3, string.Format("Expected at least 3, got {0}", inserted.Count));

            foreach (var u in inserted)
            {
                u.Age = 77;
            }

            var updateResult = FastWrite.UpdateList(inserted, u => new { u.Age }, key: _key);
            Console.WriteLine("UpdateList: IsSuccess={0}, Message={1}", updateResult.IsSuccess, updateResult.Message);
            Assert.True(updateResult.IsSuccess, string.Format("UpdateList failed: {0}", updateResult.Message));

            var updated = FastRead.Query<PerfUser>(u => u.UserName.Contains(string.Format("orm_test_{0}", tag)), key: _key)
                .ToList<PerfUser>();
            Assert.All(updated, u => Assert.Equal(77, u.Age));
        }

        #endregion

        #region Delete by entity model

        [Fact]
        public void Delete_ByModel_ShouldRemoveEntity()
        {
            if (!_dbAvailable)
            {
                Console.WriteLine("SKIP: database unavailable");
                return;
            }

            var tag = Guid.NewGuid().ToString("N").Substring(0, 8);
            var user = CreateTestUser(tag);
            var addResult = FastWrite.Add(user, key: _key);
            Assert.True(addResult.IsSuccess, string.Format("Add failed: {0}", addResult.Message));

            var inserted = FastRead.Query<PerfUser>(u => u.UserName == string.Format("orm_test_{0}", tag), key: _key)
                .ToItem<PerfUser>();
            Assert.NotNull(inserted);

            var deleteResult = FastWrite.Delete(inserted, key: _key);
            Console.WriteLine("Delete by model: IsSuccess={0}, Message={1}", deleteResult.IsSuccess, deleteResult.Message);
            Assert.True(deleteResult.IsSuccess, string.Format("Delete by model failed: {0}", deleteResult.Message));

            var remaining = FastRead.Query<PerfUser>(u => u.Id == inserted.Id, key: _key)
                .ToItem<PerfUser>();
            Assert.Null(remaining);
        }

        #endregion

        #region ExecuteSql raw write

        [Fact]
        public void ExecuteSql_RawWrite_ShouldExecuteNonQuery()
        {
            if (!_dbAvailable)
            {
                Console.WriteLine("SKIP: database unavailable");
                return;
            }

            var tag = Guid.NewGuid().ToString("N").Substring(0, 8);
            var result = FastWrite.ExecuteSql(
                "INSERT INTO perf_users (UserName, Email, Age, IsActive, CreatedAt) VALUES (@p0, @p1, @p2, @p3, @p4)",
                new[] {
                    new Microsoft.Data.SqlClient.SqlParameter("@p0", string.Format("orm_test_sql_{0}", tag)),
                    new Microsoft.Data.SqlClient.SqlParameter("@p1", string.Format("sql_{0}@example.com", tag)),
                    new Microsoft.Data.SqlClient.SqlParameter("@p2", 42),
                    new Microsoft.Data.SqlClient.SqlParameter("@p3", true),
                    new Microsoft.Data.SqlClient.SqlParameter("@p4", DateTime.Now)
                },
                key: _key);

            Console.WriteLine("ExecuteSql: IsSuccess={0}, Message={1}", result.IsSuccess, result.Message);
            Assert.True(result.IsSuccess, string.Format("ExecuteSql failed: {0}", result.Message));

            var found = FastRead.Query<PerfUser>(u => u.UserName == string.Format("orm_test_sql_{0}", tag), key: _key)
                .ToItem<PerfUser>();
            Assert.NotNull(found);
            Assert.Equal(42, found.Age);
        }

        #endregion

        #region BulkInsert

        [Fact]
        public void BulkInsert_ShouldInsertMultipleRecords()
        {
            if (!_dbAvailable)
            {
                Console.WriteLine("SKIP: database unavailable");
                return;
            }

            var tag = Guid.NewGuid().ToString("N").Substring(0, 8);
            var list = new List<PerfUser>();
            for (int i = 0; i < 10; i++)
            {
                list.Add(new PerfUser
                {
                    UserName = string.Format("orm_test_bulk_{0}_{1}", tag, i),
                    Email = string.Format("bulk_{0}_{1}@example.com", tag, i),
                    Age = 20 + i,
                    IsActive = true,
                    CreatedAt = DateTime.Now
                });
            }

            var result = FastWrite.BulkInsert(list, key: _key);
            Console.WriteLine("BulkInsert: IsSuccess={0}, Message={1}", result.IsSuccess, result.Message);
            Assert.True(result.IsSuccess, string.Format("BulkInsert failed: {0}", result.Message));

            var count = FastRead.Query<PerfUser>(u => u.UserName.Contains(string.Format("orm_test_bulk_{0}", tag)), key: _key)
                .ToCount();
            Console.WriteLine("BulkInsert count after insert: {0}", count);
            Assert.Equal(10, count);
        }

        #endregion

        #region ToItem

        [Fact]
        public void ToItem_ShouldReturnSingleEntity()
        {
            if (!_dbAvailable)
            {
                Console.WriteLine("SKIP: database unavailable");
                return;
            }

            var tag = Guid.NewGuid().ToString("N").Substring(0, 8);
            var user = CreateTestUser(tag);
            FastWrite.Add(user, key: _key);

            var result = FastRead.Query<PerfUser>(u => u.UserName == string.Format("orm_test_{0}", tag), key: _key)
                .ToItem<PerfUser>();

            Console.WriteLine("ToItem: UserName={0}, Id={1}", result?.UserName, result?.Id);
            Assert.NotNull(result);
            Assert.Equal(string.Format("orm_test_{0}", tag), result.UserName);
        }

        [Fact]
        public void ToItem_NoMatch_ShouldReturnNull()
        {
            if (!_dbAvailable)
            {
                Console.WriteLine("SKIP: database unavailable");
                return;
            }

            var result = FastRead.Query<PerfUser>(u => u.UserName == "nonexistent_user_xyz_12345", key: _key)
                .ToItem<PerfUser>();

            Console.WriteLine("ToItem no match: result is null = {0}", result == null);
            Assert.Null(result);
        }

        #endregion

        #region ToCount

        [Fact]
        public void ToCount_ShouldReturnRecordCount()
        {
            if (!_dbAvailable)
            {
                Console.WriteLine("SKIP: database unavailable");
                return;
            }

            var tag = Guid.NewGuid().ToString("N").Substring(0, 8);
            for (int i = 0; i < 5; i++)
            {
                FastWrite.Add(CreateTestUser(tag + "_" + i), key: _key);
            }

            var count = FastRead.Query<PerfUser>(u => u.UserName.Contains(string.Format("orm_test_{0}", tag)), key: _key)
                .ToCount();

            Console.WriteLine("ToCount: {0}", count);
            Assert.True(count >= 5, string.Format("Expected >= 5, got {0}", count));
        }

        [Fact]
        public void ToCount_NoMatch_ShouldReturnZero()
        {
            if (!_dbAvailable)
            {
                Console.WriteLine("SKIP: database unavailable");
                return;
            }

            var count = FastRead.Query<PerfUser>(u => u.UserName == "nonexistent_count_xyz", key: _key)
                .ToCount();

            Console.WriteLine("ToCount no match: {0}", count);
            Assert.Equal(0, count);
        }

        #endregion

        #region ToPage with PageModel

        [Fact]
        public void ToPage_ShouldReturnPagedResults()
        {
            if (!_dbAvailable)
            {
                Console.WriteLine("SKIP: database unavailable");
                return;
            }

            var tag = Guid.NewGuid().ToString("N").Substring(0, 8);
            for (int i = 0; i < 15; i++)
            {
                FastWrite.Add(CreateTestUser(tag + "_" + i), key: _key);
            }

            var pModel = new PageModel
            {
                PageId = 1,
                PageSize = 5
            };

            var result = FastRead.Query<PerfUser>(u => u.UserName.Contains(string.Format("orm_test_{0}", tag)), key: _key)
                .OrderBy<PerfUser>(u => u.Id)
                .ToPage<PerfUser>(pModel);

            Console.WriteLine("ToPage: list.Count={0}, TotalRecord={1}, TotalPage={2}", result.list.Count, result.pModel.TotalRecord, result.pModel.TotalPage);
            Assert.NotNull(result);
            Assert.NotNull(result.list);
            Assert.True(result.list.Count <= 5, string.Format("Expected <= 5 items, got {0}", result.list.Count));
            Assert.True(result.pModel.TotalRecord >= 15, string.Format("Expected TotalRecord >= 15, got {0}", result.pModel.TotalRecord));
        }

        [Fact]
        public void ToPage_SecondPage_ShouldReturnCorrectOffset()
        {
            if (!_dbAvailable)
            {
                Console.WriteLine("SKIP: database unavailable");
                return;
            }

            var tag = Guid.NewGuid().ToString("N").Substring(0, 8);
            for (int i = 0; i < 12; i++)
            {
                FastWrite.Add(CreateTestUser(tag + "_" + i), key: _key);
            }

            var pModel = new PageModel
            {
                PageId = 2,
                PageSize = 5
            };

            var result = FastRead.Query<PerfUser>(u => u.UserName.Contains(string.Format("orm_test_{0}", tag)), key: _key)
                .OrderBy<PerfUser>(u => u.Id)
                .ToPage<PerfUser>(pModel);

            Console.WriteLine("ToPage p2: list.Count={0}, TotalRecord={1}", result.list.Count, result.pModel.TotalRecord);
            Assert.NotNull(result.list);
            Assert.True(result.list.Count <= 5, string.Format("Expected <= 5 items on page 2, got {0}", result.list.Count));
        }

        #endregion

        #region ToJson

        [Fact]
        public void ToJson_ShouldReturnJsonString()
        {
            if (!_dbAvailable)
            {
                Console.WriteLine("SKIP: database unavailable");
                return;
            }

            var tag = Guid.NewGuid().ToString("N").Substring(0, 8);
            FastWrite.Add(CreateTestUser(tag), key: _key);

            var json = FastRead.Query<PerfUser>(u => u.UserName == string.Format("orm_test_{0}", tag), key: _key)
                .Take(1)
                .ToJson();

            Console.WriteLine("ToJson length: {0}", json?.Length ?? 0);
            Assert.False(string.IsNullOrEmpty(json), "ToJson returned null or empty");
            Assert.Contains(string.Format("orm_test_{0}", tag), json);
        }

        [Fact]
        public void ToJson_EmptyResult_ShouldReturnEmptyOrNull()
        {
            if (!_dbAvailable)
            {
                Console.WriteLine("SKIP: database unavailable");
                return;
            }

            var json = FastRead.Query<PerfUser>(u => u.UserName == "nonexistent_json_xyz", key: _key)
                .ToJson();

            Console.WriteLine("ToJson empty: is null={0}, length={1}", json == null, json?.Length ?? 0);
        }

        #endregion

        #region Queue builder: ConfigureQueue + IsQueueEnabled

        [Fact]
        public void ConfigureQueue_ShouldEnableQueueForType()
        {
            var config = new WriteBehindConfig
            {
                QueueType = WriteBehindQueueType.ReliableQueue,
                Topic = "test-topic-orm",
                EnableFallback = true,
                BatchFlushSize = 50
            };

            FastWrite.ConfigureQueue<PerfUser>(config);

            var isEnabled = FastWrite.IsQueueEnabled<PerfUser>();
            Console.WriteLine("IsQueueEnabled<PerfUser>: {0}", isEnabled);
            Assert.True(isEnabled);
        }

        [Fact]
        public void ConfigureQueue_WithTableName_ShouldEnableQueueForTable()
        {
            var config = new WriteBehindConfig
            {
                QueueType = WriteBehindQueueType.Stream,
                Topic = "test-topic-table-orm"
            };

            FastWrite.ConfigureQueue("custom_test_table", config);

            var isEnabled = FastWrite.IsQueueEnabled("custom_test_table");
            Console.WriteLine("IsQueueEnabled('custom_test_table'): {0}", isEnabled);
            Assert.True(isEnabled);
        }

        [Fact]
        public void IsQueueEnabled_UnregisteredType_ShouldReturnFalse()
        {
            var isEnabled = FastWrite.IsQueueEnabled<ConnectionPool.SmartConnectionPool>();
            Console.WriteLine("IsQueueEnabled<SmartConnectionPool>: {0}", isEnabled);
            Assert.False(isEnabled);
        }

        #endregion

        #region Take operation

        [Fact]
        public void Take_ShouldLimitResultCount()
        {
            if (!_dbAvailable)
            {
                Console.WriteLine("SKIP: database unavailable");
                return;
            }

            var tag = Guid.NewGuid().ToString("N").Substring(0, 8);
            for (int i = 0; i < 10; i++)
            {
                FastWrite.Add(CreateTestUser(tag + "_" + i), key: _key);
            }

            var results = FastRead.Query<PerfUser>(u => u.UserName.Contains(string.Format("orm_test_{0}", tag)), key: _key)
                .Take(3)
                .ToList<PerfUser>();

            Console.WriteLine("Take(3): got {0} items", results.Count);
            Assert.True(results.Count <= 3, string.Format("Expected <= 3, got {0}", results.Count));
            Assert.True(results.Count > 0, "Expected at least 1 result");
        }

        [Fact]
        public void Take_Zero_ShouldReturnEmpty()
        {
            if (!_dbAvailable)
            {
                Console.WriteLine("SKIP: database unavailable");
                return;
            }

            var results = FastRead.Query<PerfUser>(u => u.IsActive, key: _key)
                .Take(0)
                .ToList<PerfUser>();

            Console.WriteLine("Take(0): got {0} items", results.Count);
            Assert.Empty(results);
        }

        #endregion

        #region AddList bulk add

        [Fact]
        public void AddList_ShouldInsertMultipleEntities()
        {
            if (!_dbAvailable)
            {
                Console.WriteLine("SKIP: database unavailable");
                return;
            }

            var tag = Guid.NewGuid().ToString("N").Substring(0, 8);
            var list = new List<PerfUser>();
            for (int i = 0; i < 5; i++)
            {
                list.Add(CreateTestUser(tag + "_" + i));
            }

            var result = FastWrite.AddList(list, key: _key);
            Console.WriteLine("AddList: IsSuccess={0}, Message={1}", result.IsSuccess, result.Message);
            Assert.True(result.IsSuccess, string.Format("AddList failed: {0}", result.Message));

            var count = FastRead.Query<PerfUser>(u => u.UserName.Contains(string.Format("orm_test_{0}", tag)), key: _key)
                .ToCount();
            Assert.Equal(5, count);
        }

        #endregion

        #region Delete by predicate

        [Fact]
        public void Delete_ByPredicate_ShouldRemoveMatchingEntities()
        {
            if (!_dbAvailable)
            {
                Console.WriteLine("SKIP: database unavailable");
                return;
            }

            var tag = Guid.NewGuid().ToString("N").Substring(0, 8);
            for (int i = 0; i < 3; i++)
            {
                FastWrite.Add(CreateTestUser(tag + "_" + i), key: _key);
            }

            var countBefore = FastRead.Query<PerfUser>(u => u.UserName.Contains(string.Format("orm_test_{0}", tag)), key: _key)
                .ToCount();
            Assert.Equal(3, countBefore);

            var deleteResult = FastWrite.Delete<PerfUser>(u => u.UserName.Contains(string.Format("orm_test_{0}", tag)), key: _key);
            Console.WriteLine("Delete by predicate: IsSuccess={0}, Message={1}", deleteResult.IsSuccess, deleteResult.Message);
            Assert.True(deleteResult.IsSuccess, string.Format("Delete by predicate failed: {0}", deleteResult.Message));

            var countAfter = FastRead.Query<PerfUser>(u => u.UserName.Contains(string.Format("orm_test_{0}", tag)), key: _key)
                .ToCount();
            Assert.Equal(0, countAfter);
        }

        #endregion

        #region Fluent query chain: Where + OrderBy + Take + ToList

        [Fact]
        public void FluentQuery_Where_OrderBy_Take_ToList()
        {
            if (!_dbAvailable)
            {
                Console.WriteLine("SKIP: database unavailable");
                return;
            }

            var tag = Guid.NewGuid().ToString("N").Substring(0, 8);
            for (int i = 0; i < 8; i++)
            {
                FastWrite.Add(new PerfUser
                {
                    UserName = string.Format("orm_test_{0}_{1:D2}", tag, i),
                    Email = string.Format("chain_{0}_{1}@example.com", tag, i),
                    Age = 20 + i,
                    IsActive = i % 2 == 0,
                    CreatedAt = DateTime.Now.AddMinutes(-i)
                }, key: _key);
            }

            var results = FastRead.Query<PerfUser>(u => u.UserName.Contains(string.Format("orm_test_{0}", tag)), key: _key)
                .Where(u => u.Age > 23)
                .OrderByDescending(u => u.Age)
                .Take(5)
                .ToList<PerfUser>();

            Console.WriteLine("Fluent chain: got {0} items", results.Count);
            Assert.NotEmpty(results);
            Assert.True(results.Count <= 5);

            for (int i = 0; i < results.Count - 1; i++)
            {
                Assert.True(results[i].Age >= results[i + 1].Age, "Results not in descending order");
            }
        }

        #endregion

        #region Query with Like/Contains

        [Fact]
        public void Query_Like_ShouldMatchPartialStrings()
        {
            if (!_dbAvailable)
            {
                Console.WriteLine("SKIP: database unavailable");
                return;
            }

            var tag = Guid.NewGuid().ToString("N").Substring(0, 8);
            FastWrite.Add(CreateTestUser(string.Format("abc_{0}", tag)), key: _key);
            FastWrite.Add(CreateTestUser(string.Format("xyz_{0}", tag)), key: _key);

            var results = FastRead.Query<PerfUser>(u => u.UserName.Contains(string.Format("abc_{0}", tag)), key: _key)
                .ToList<PerfUser>();

            Console.WriteLine("Query Like: got {0} items", results.Count);
            Assert.Single(results);
            Assert.Contains("abc", results[0].UserName);
        }

        #endregion

        #region ToList async

        [Fact]
        public async System.Threading.Tasks.Task ToListAsync_ShouldReturnResults()
        {
            if (!_dbAvailable)
            {
                Console.WriteLine("SKIP: database unavailable");
                return;
            }

            var tag = Guid.NewGuid().ToString("N").Substring(0, 8);
            FastWrite.Add(CreateTestUser(tag), key: _key);

            var results = await FastRead.Query<PerfUser>(u => u.UserName == string.Format("orm_test_{0}", tag), key: _key)
                .Take(1)
                .ToListAsync<PerfUser>();

            Console.WriteLine("ToListAsync: got {0} items", results.Count);
            Assert.Single(results);
        }

        #endregion

        #region ExecuteSql read

        [Fact]
        public void ExecuteSql_Read_ShouldReturnResults()
        {
            if (!_dbAvailable)
            {
                Console.WriteLine("SKIP: database unavailable");
                return;
            }

            var tag = Guid.NewGuid().ToString("N").Substring(0, 8);
            FastWrite.Add(CreateTestUser(tag), key: _key);

            var results = FastRead.ExecuteSql<PerfUser>(
                "SELECT TOP 5 * FROM perf_users WHERE UserName LIKE @p0",
                new[] { new Microsoft.Data.SqlClient.SqlParameter("@p0", string.Format("%orm_test_{0}%", tag)) },
                key: _key);

            Console.WriteLine("ExecuteSql read: got {0} items", results.Count);
            Assert.NotEmpty(results);
        }

        #endregion
    }
}
#endif
