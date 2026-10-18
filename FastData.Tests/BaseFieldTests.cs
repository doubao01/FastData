using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using FastData.Base;
using FastData.DbTypes;
using FastData.Model;
using Xunit;

namespace FastData.Tests
{
    /// <summary>
    /// BaseField 字段表达式解析测试
    /// 覆盖单字段与匿名对象两种写法
    /// </summary>
    public class BaseFieldTests
    {
        private class TestUser
        {
            public int Id { get; set; }
            public string Name { get; set; }
            public string Role { get; set; }
            public int Age { get; set; }
        }

        private static ConfigModel CreateConfig()
        {
            return new ConfigModel
            {
                DbType = DataDbType.SqlServer,
                Flag = "@",
                ProviderName = "Microsoft.Data.SqlClient",
                SqlErrorType = "file"
            };
        }

        [Fact]
        public void GroupBy_SingleField_ShouldReturnQualifiedField()
        {
            Expression<Func<TestUser, object>> field = u => u.Role;

            var result = BaseField.GroupBy<TestUser>(field, CreateConfig());

            Assert.Equal(new List<string> { "u.Role" }, result);
        }

        [Fact]
        public void GroupBy_AnonymousObject_ShouldReturnAllFields()
        {
            Expression<Func<TestUser, object>> field = u => new { u.Role, u.Name };

            var result = BaseField.GroupBy<TestUser>(field, CreateConfig());

            Assert.Equal(new List<string> { "u.Role", "u.Name" }, result);
        }

        [Fact]
        public void OrderBy_SingleField_ShouldApplyDirection()
        {
            Expression<Func<TestUser, object>> field = u => u.Age;

            var asc = BaseField.OrderBy<TestUser>(field, CreateConfig(), false);
            var desc = BaseField.OrderBy<TestUser>(field, CreateConfig(), true);

            Assert.Equal(new List<string> { "u.Age asc" }, asc);
            Assert.Equal(new List<string> { "u.Age desc" }, desc);
        }

        [Fact]
        public void OrderBy_AnonymousObject_ShouldApplyDirectionToEveryField()
        {
            Expression<Func<TestUser, object>> field = u => new { u.Name, u.Age };

            var result = BaseField.OrderBy<TestUser>(field, CreateConfig(), false);

            Assert.Equal(new List<string> { "u.Name asc", "u.Age asc" }, result);
        }

        [Fact]
        public void QueryField_SingleField_ShouldNotFallBackToAllFields()
        {
            Expression<Func<TestUser, bool>> predicate = u => u.Id > 0;
            Expression<Func<TestUser, object>> field = u => u.Name;

            var result = BaseField.QueryField<TestUser>(predicate, field, CreateConfig());

            Assert.Equal("u.Name", result.Field);
            Assert.Equal(new List<string> { "Name" }, result.AsName);
        }

        [Fact]
        public void QueryField_AnonymousObject_ShouldReturnSelectedFields()
        {
            Expression<Func<TestUser, bool>> predicate = u => u.Id > 0;
            Expression<Func<TestUser, object>> field = u => new { u.Id, u.Name };

            var result = BaseField.QueryField<TestUser>(predicate, field, CreateConfig());

            Assert.Equal("u.Id,u.Name", result.Field);
            Assert.Equal(new List<string> { "Id", "Name" }, result.AsName);
        }

        [Fact]
        public void GroupBy_UnsupportedExpression_ShouldReturnEmptyList()
        {
            Expression<Func<TestUser, object>> field = u => u.Age + 1;

            var result = BaseField.GroupBy<TestUser>(field, CreateConfig());

            Assert.Empty(result);
        }
    }
}
