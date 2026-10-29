using System;
using System.Collections.Generic;
using System.Linq;
using FastData.DevTools;
using Xunit;

namespace FastData.Tests.DevTools
{
    /// <summary>
    /// CodeGenerator 的生成契约。
    /// <para>
    /// 背景：原实现的 <c>GetTableColumns</c> 返回写死的 6 列
    /// （注释自认「这是示例实现」），生成任何表都输出同一套字段；
    /// 且生成的是 <c>[TableName]</c>（本库不存在该特性，编译不过）
    /// 与 <c>[Key]</c>（DataAnnotations，ORM 不认，算不出主键）。
    /// </para>
    /// <para>
    /// 这里锁住：不同表生成不同字段、特性用本库真实定义的那两个。
    /// </para>
    /// </summary>
    public class CodeGeneratorTests
    {
        private static IList<CodeGenerator.ColumnMetadata> OrdersColumns()
        {
            return new List<CodeGenerator.ColumnMetadata>
            {
                new CodeGenerator.ColumnMetadata { ColumnName = "order_id", DataType = "int", IsNullable = false, IsPrimaryKey = true, IsIdentity = true },
                new CodeGenerator.ColumnMetadata { ColumnName = "amount", DataType = "decimal", IsNullable = false },
                new CodeGenerator.ColumnMetadata { ColumnName = "memo", DataType = "nvarchar", IsNullable = true, MaxLength = 200 }
            };
        }

        private static IList<CodeGenerator.ColumnMetadata> UsersColumns()
        {
            return new List<CodeGenerator.ColumnMetadata>
            {
                new CodeGenerator.ColumnMetadata { ColumnName = "user_id", DataType = "bigint", IsNullable = false, IsPrimaryKey = true, IsIdentity = true },
                new CodeGenerator.ColumnMetadata { ColumnName = "nick_name", DataType = "varchar", IsNullable = true, MaxLength = 64 }
            };
        }

        /// <summary>
        /// 关键回归：不同表必须生成不同字段。
        /// 原实现对任何表都输出 Id/Name/Email/Age/IsActive/CreateTime。
        /// </summary>
        [Fact]
        public void GenerateEntity_DifferentTables_ShouldProduceDifferentFields()
        {
            var orders = CodeGenerator.GenerateEntity(OrdersColumns(), "orders");
            var users = CodeGenerator.GenerateEntity(UsersColumns(), "users");

            Assert.NotEqual(orders, users);
            Assert.Contains("OrderId", orders);
            Assert.DoesNotContain("OrderId", users);
            Assert.Contains("UserId", users);
            Assert.DoesNotContain("UserId", orders);
        }

        /// <summary>
        /// 不应再出现写死的字段集合。
        /// </summary>
        [Fact]
        public void GenerateEntity_ShouldNotContainHardcodedFields()
        {
            var code = CodeGenerator.GenerateEntity(OrdersColumns(), "orders");

            Assert.DoesNotContain("CreateTime", code);
            Assert.DoesNotContain("IsActive", code);
            Assert.DoesNotContain("Email", code);
        }

        /// <summary>
        /// 表特性必须是本库存在的 [Table(Name = ...)]。
        /// 原来生成的 [TableName(...)] 在本库不存在，编译直接失败。
        /// </summary>
        [Fact]
        public void GenerateEntity_ShouldUseRealTableAttribute()
        {
            var code = CodeGenerator.GenerateEntity(OrdersColumns(), "orders");

            Assert.Contains("[Table(Name = \"orders\")]", code);
            Assert.DoesNotContain("[TableName", code);
        }

        /// <summary>
        /// 主键必须用 [Column(IsKey = true)]，而不是 DataAnnotations 的 [Key]
        /// （后者 ORM 不识别，等于没标主键）。
        /// </summary>
        [Fact]
        public void GenerateEntity_PrimaryKey_ShouldUseColumnAttribute()
        {
            var code = CodeGenerator.GenerateEntity(OrdersColumns(), "orders");

            Assert.Contains("[Column(IsKey = true, IsIdentity = true)]", code);
            Assert.DoesNotContain("[Key]", code);
        }

        /// <summary>
        /// 字符串长度应带进 [Column(Length = n)]。
        /// </summary>
        [Fact]
        public void GenerateEntity_StringLength_ShouldBeMapped()
        {
            var code = CodeGenerator.GenerateEntity(OrdersColumns(), "orders");

            Assert.Contains("[Column(Length = 200)]", code);
        }

        /// <summary>
        /// 类型映射：int→int、bigint→long、decimal→decimal、nvarchar→string。
        /// </summary>
        [Fact]
        public void GenerateEntity_ShouldMapDatabaseTypes()
        {
            var code = CodeGenerator.GenerateEntity(OrdersColumns(), "orders");

            Assert.Contains("public int OrderId", code);
            Assert.Contains("public decimal Amount", code);
            Assert.Contains("public string Memo", code);
        }

        /// <summary>
        /// 可空的值类型加 ?，string 不加。
        /// </summary>
        [Fact]
        public void GenerateEntity_NullableHandling()
        {
            var cols = new List<CodeGenerator.ColumnMetadata>
            {
                new CodeGenerator.ColumnMetadata { ColumnName = "a", DataType = "int", IsNullable = true },
                new CodeGenerator.ColumnMetadata { ColumnName = "b", DataType = "int", IsNullable = false },
                new CodeGenerator.ColumnMetadata { ColumnName = "c", DataType = "nvarchar", IsNullable = true }
            };

            var code = CodeGenerator.GenerateEntity(cols, "t");

            Assert.Contains("public int? A", code);
            Assert.Contains("public int B", code);
            Assert.Contains("public string C", code);
        }

        /// <summary>
        /// 生成结果必须是可编译的 C# 骨架：有命名空间、有类、括号配对。
        /// </summary>
        [Fact]
        public void GenerateEntity_ShouldProduceWellFormedCode()
        {
            var code = CodeGenerator.GenerateEntity(OrdersColumns(), "orders", "MyApp.Models");

            Assert.Contains("namespace MyApp.Models", code);
            Assert.Contains("public class Orders", code);
            Assert.Contains("using FastData.Property;", code);
            Assert.Equal(code.Count(c => c == '{'), code.Count(c => c == '}'));
        }

        /// <summary>
        /// 空参数要明确报错，不能返回空字符串让调用方误以为生成成功。
        /// </summary>
        [Fact]
        public void GenerateEntity_InvalidArgs_ShouldThrow()
        {
            Assert.Throws<ArgumentNullException>(() => CodeGenerator.GenerateEntity(null, "t"));
            Assert.Throws<ArgumentNullException>(() => CodeGenerator.GenerateEntity(OrdersColumns(), " "));
        }

        /// <summary>
        /// 取不到表结构时必须抛异常，而不是返回一份字段全错的骨架。
        /// </summary>
        [Fact]
        public void GenerateEntityFromTable_UnknownTable_ShouldThrow()
        {
            Assert.Throws<InvalidOperationException>(
                () => CodeGenerator.GenerateEntityFromTable("no_such_table_zzz"));
        }

        /// <summary>
        /// 表名含非法字符时不做查询、直接返回空（防标识符拼接注入）。
        /// </summary>
        [Fact]
        public void GetTableColumns_UnsafeIdentifier_ShouldReturnEmpty()
        {
            var cols = CodeGenerator.GetTableColumns("users; DROP TABLE users--");
            Assert.Empty(cols);
        }
    }
}