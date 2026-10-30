using System.Collections.Generic;
using System.IO;
using FastData.DevTools;
using Xunit;

namespace FastData.Tests.DevTools
{
    /// <summary>
    /// DataImporter 的 CSV 解析契约。
    /// <para>
    /// 背景：这个类 320 行、零测试。原实现用 <c>line.Split(',')</c> 解析，
    /// 字段里含逗号时会被拆成两列，导致后续列全部错位 —— 而且不报错，
    /// 静默把数据写错。
    /// </para>
    /// <para>
    /// 这里只测解析这一层（纯函数、不依赖数据库）。
    /// 导入/导出需要真实连接，由集成测试覆盖。
    /// </para>
    /// </summary>
    public class DataImporterCsvTests
    {
        /// <summary>
        /// 基本多列。
        /// </summary>
        [Fact]
        public void ParseCsvLine_SimpleColumns()
        {
            var fields = DataImporter.ParseCsvLine("a,b,c");

            Assert.Equal(3, fields.Count);
            Assert.Equal(new[] { "a", "b", "c" }, fields);
        }

        /// <summary>
        /// 关键回归：引号内的逗号不能被当作分隔符。
        /// 原实现会把 "Smith, John" 拆成两个字段，导致后续列错位。
        /// </summary>
        [Fact]
        public void ParseCsvLine_CommaInsideQuotes()
        {
            var fields = DataImporter.ParseCsvLine("1,\"Smith, John\",30");

            Assert.Equal(3, fields.Count);
            Assert.Equal("1", fields[0]);
            Assert.Equal("Smith, John", fields[1]);
            Assert.Equal("30", fields[2]);
        }

        /// <summary>
        /// 双引号转义："" 表示一个字面引号。
        /// </summary>
        [Fact]
        public void ParseCsvLine_EscapedQuotes()
        {
            var fields = DataImporter.ParseCsvLine("1,\"say \"\"hi\"\"\",3");

            Assert.Equal(3, fields.Count);
            Assert.Equal("say \"hi\"", fields[1]);
        }

        /// <summary>
        /// 空字段应保留为占位，不能丢列（丢列会错位）。
        /// </summary>
        [Fact]
        public void ParseCsvLine_EmptyFieldsPreserved()
        {
            var fields = DataImporter.ParseCsvLine("a,,c,");

            Assert.Equal(4, fields.Count);
            Assert.Equal(string.Empty, fields[1]);
            Assert.Equal(string.Empty, fields[3]);
        }

        /// <summary>
        /// 整行为空时返回一个空字段，而不是空列表。
        /// </summary>
        [Fact]
        public void ParseCsvLine_EmptyLine()
        {
            var fields = DataImporter.ParseCsvLine("");

            Assert.Single(fields);
            Assert.Equal(string.Empty, fields[0]);
        }

        /// <summary>
        /// 单列无逗号。
        /// </summary>
        [Fact]
        public void ParseCsvLine_SingleColumn()
        {
            var fields = DataImporter.ParseCsvLine("onlyone");

            Assert.Single(fields);
            Assert.Equal("onlyone", fields[0]);
        }

        /// <summary>
        /// 引号包裹但内容为空。
        /// </summary>
        [Fact]
        public void ParseCsvLine_QuotedEmpty()
        {
            var fields = DataImporter.ParseCsvLine("a,\"\",c");

            Assert.Equal(3, fields.Count);
            Assert.Equal(string.Empty, fields[1]);
        }

        /// <summary>
        /// 未闭合的引号不应抛异常，按「一直读到行尾」处理。
        /// 畸形数据在导入场景很常见，抛异常会让整批数据都进不去。
        /// </summary>
        [Fact]
        public void ParseCsvLine_UnclosedQuote_ShouldNotThrow()
        {
            var ex = Record.Exception(() => DataImporter.ParseCsvLine("a,\"unclosed,b"));

            Assert.Null(ex);
        }

        /// <summary>
        /// 中文与特殊符号不应被破坏。
        /// </summary>
        [Fact]
        public void ParseCsvLine_UnicodeContent()
        {
            var fields = DataImporter.ParseCsvLine("张三,\"北京市, 朝阳区\",28");

            Assert.Equal(3, fields.Count);
            Assert.Equal("张三", fields[0]);
            Assert.Equal("北京市, 朝阳区", fields[1]);
        }

        /// <summary>
        /// 表头同样要走 ParseCsvLine —— 原实现的表头也用 Split(',')，
        /// 表头若含逗号同样会错位，且会让整批数据的字段映射全错。
        /// </summary>
        [Fact]
        public void ParseCsvLine_UsedForHeaderToo()
        {
            var headers = DataImporter.ParseCsvLine("Id,\"Full, Name\",Age");

            Assert.Equal(3, headers.Count);
            Assert.Equal("Full, Name", headers[1]);
        }
    }
}