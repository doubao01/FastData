using System;
using FastData.Tooling.Sync;
using Xunit;

namespace FastData.Tests.Sync
{
    /// <summary>
    /// DataRowSerializer 类型还原测试
    /// 
    /// 覆盖列类型名解析（Nullable、常见别名、未知类型回退）与值转换（数值、日期、Guid、
    /// 枚举、Base64 字节数组、转换失败保留原值），验证 JSON 反序列化能可靠还原到目标列类型。
    /// </summary>
    public class DataRowSerializerTypeTests
    {
        [Theory]
        [InlineData("System.String", typeof(string))]
        [InlineData("System.Int32", typeof(int))]
        [InlineData("System.Int64", typeof(long))]
        [InlineData("System.Int64?", typeof(long))]
        [InlineData("System.Decimal", typeof(decimal))]
        [InlineData("System.Boolean", typeof(bool))]
        [InlineData("System.DateTime", typeof(DateTime))]
        [InlineData("System.DateTimeOffset", typeof(DateTimeOffset))]
        [InlineData("System.TimeSpan", typeof(TimeSpan))]
        [InlineData("System.Guid", typeof(Guid))]
        [InlineData("System.Byte[]", typeof(byte[]))]
        public void ResolveColumnType_KnownTypes_ReturnsExpected(string name, Type expected)
        {
            Assert.Equal(expected, DataRowSerializer.ResolveColumnType(name));
        }

        [Fact]
        public void ResolveColumnType_NullableOfValueType_UnwrapsToUnderlying()
        {
            Assert.Equal(typeof(int), DataRowSerializer.ResolveColumnType("System.Nullable`1[System.Int32]"));
        }

        [Theory]
        [InlineData("")]
        [InlineData(null)]
        [InlineData("SomeUnknownType")]
        public void ResolveColumnType_UnknownOrEmpty_FallsBackToString(string name)
        {
            Assert.Equal(typeof(string), DataRowSerializer.ResolveColumnType(name));
        }

        [Fact]
        public void ConvertToColumnType_NullValue_ReturnsDbNull()
        {
            Assert.Equal(DBNull.Value, DataRowSerializer.ConvertToColumnType(null, typeof(int)));
            Assert.Equal(DBNull.Value, DataRowSerializer.ConvertToColumnType(DBNull.Value, typeof(int)));
        }

        [Fact]
        public void ConvertToColumnType_AlreadyAssignable_ReturnsSameInstance()
        {
            var value = new string(new[] { 'o', 'k' });
            var result = DataRowSerializer.ConvertToColumnType(value, typeof(string));
            Assert.Same(value, result);
        }

        [Theory]
        [InlineData("42", typeof(int), 42)]
        [InlineData("42", typeof(long), 42L)]
        public void ConvertToColumnType_NumericString_Converts(string value, Type type, object expected)
        {
            Assert.Equal(expected, DataRowSerializer.ConvertToColumnType(value, type));
        }

        [Fact]
        public void ConvertToColumnType_DecimalString_ConvertsToDecimal()
        {
            Assert.Equal(3.14m, Assert.IsType<decimal>(DataRowSerializer.ConvertToColumnType("3.14", typeof(decimal))));
        }

        [Fact]
        public void ConvertToColumnType_DateString_ConvertsToDateTime()
        {
            var result = DataRowSerializer.ConvertToColumnType("2024-01-15 10:30:00", typeof(DateTime));
            Assert.Equal(new DateTime(2024, 1, 15, 10, 30, 0), Assert.IsType<DateTime>(result));
        }

        [Fact]
        public void ConvertToColumnType_GuidString_ConvertsToGuid()
        {
            var guid = Guid.NewGuid();
            var result = DataRowSerializer.ConvertToColumnType(guid.ToString(), typeof(Guid));
            Assert.Equal(guid, Assert.IsType<Guid>(result));
        }

        [Fact]
        public void ConvertToColumnType_Base64String_ConvertsToBytes()
        {
            var bytes = new byte[] { 1, 2, 3, 4 };
            var result = DataRowSerializer.ConvertToColumnType(Convert.ToBase64String(bytes), typeof(byte[]));
            Assert.Equal(bytes, Assert.IsType<byte[]>(result));
        }

        [Fact]
        public void ConvertToColumnType_EnumString_ConvertsToEnum()
        {
            var result = DataRowSerializer.ConvertToColumnType("hash", typeof(SampleEnum));
            Assert.Equal(SampleEnum.Hash, Assert.IsType<SampleEnum>(result));
        }

        [Fact]
        public void ConvertToColumnType_UnconvertibleValue_KeepsOriginal()
        {
            var result = DataRowSerializer.ConvertToColumnType("not-a-number", typeof(int));
            Assert.Equal("not-a-number", result);
        }

        private enum SampleEnum
        {
            Time = 0,
            Hash = 1,
            List = 2
        }
    }
}
