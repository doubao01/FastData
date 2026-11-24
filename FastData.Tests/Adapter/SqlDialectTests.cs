using System;
using FastData.Adapter;
using FastData.Base;
using FastData.DbTypes;
using Xunit;

namespace FastData.Tests.Adapter
{
    /// <summary>
    /// SQL 方言与适配器工厂测试
    /// 
    /// issue #23 回归：方言层此前零测试覆盖。这里直接验证工厂映射、
    /// 五种方言的行数限制 SQL 以及参数名前缀，不连接数据库。
    /// </summary>
    public class SqlDialectTests
    {
        /// <summary>
        /// 验证工厂为每种数据库类型创建正确的适配器与方言。
        /// </summary>
        [Fact]
        public void Factory_MapsDbTypeToAdapterAndDialect()
        {
            AssertAdapter(DataDbType.Oracle, Provider.Oracle, typeof(OracleDialect));
            AssertAdapter(DataDbType.MySql, Provider.MySql, typeof(LimitDialect));
            AssertAdapter(DataDbType.SqlServer, Provider.SqlServer, typeof(SqlServerDialect));
            AssertAdapter(DataDbType.DB2, Provider.DB2, typeof(Db2Dialect));
            AssertAdapter(DataDbType.SQLite, Provider.SQLite, typeof(SqliteDialect));
            AssertAdapter(DataDbType.PostgreSql, Provider.PostgreSql, typeof(LimitDialect));
        }

        /// <summary>
        /// 验证未知数据库类型回退到 SQL Server。
        /// </summary>
        [Fact]
        public void Factory_UnknownDbType_FallsBackToSqlServer()
        {
            AssertAdapter((DataDbType)0, Provider.SqlServer, typeof(SqlServerDialect));
            AssertAdapter((DataDbType)999, Provider.SqlServer, typeof(SqlServerDialect));
        }

        /// <summary>
        /// LIMIT 方言（MySQL/PostgreSQL）在查询末尾追加 LIMIT。
        /// </summary>
        [Fact]
        public void LimitDialect_ApplyTake_AppendsLimit()
        {
            var dialect = new LimitDialect();

            Assert.Equal("select * from t limit 5", dialect.ApplyTake("select * from t", 5));
            Assert.Equal("select * from t", dialect.ApplyTake("select * from t", 0));
            Assert.Equal("select * from t", dialect.ApplyTake("select * from t", -1));
        }

        /// <summary>
        /// SQLite 方言在查询末尾追加 LIMIT。
        /// </summary>
        [Fact]
        public void SqliteDialect_ApplyTake_AppendsLimit()
        {
            var dialect = new SqliteDialect();

            Assert.Equal("select * from t limit 5", dialect.ApplyTake("select * from t", 5));
            Assert.Equal("select * from t", dialect.ApplyTake("select * from t", 0));
        }

        /// <summary>
        /// DB2 方言在查询末尾追加 FETCH FIRST。
        /// </summary>
        [Fact]
        public void Db2Dialect_ApplyTake_AppendsFetchFirst()
        {
            var dialect = new Db2Dialect();

            Assert.Equal(
                "select * from t order by id fetch first 5 rows only",
                dialect.ApplyTake("select * from t order by id", 5));
            Assert.Equal("select * from t", dialect.ApplyTake("select * from t", 0));
        }

        /// <summary>
        /// SQL Server 方言把 TOP 插到 SELECT 之后，并兼容 DISTINCT。
        /// </summary>
        [Fact]
        public void SqlServerDialect_ApplyTake_InsertsTop()
        {
            var dialect = new SqlServerDialect();

            Assert.Equal("select top 5 id from t", dialect.ApplyTake("select id from t", 5));
            Assert.Equal("select top 5 distinct id from t", dialect.ApplyTake("select distinct id from t", 5));
        }

        /// <summary>
        /// SQL Server 方言保留已有 TOP，不改写非 SELECT 语句。
        /// </summary>
        [Fact]
        public void SqlServerDialect_ApplyTake_LeavesExistingTopAndNonSelectUnchanged()
        {
            var dialect = new SqlServerDialect();

            Assert.Equal("select top 10 id from t", dialect.ApplyTake("select top 10 id from t", 5));
            Assert.Equal("update t set a = 1", dialect.ApplyTake("update t set a = 1", 5));
        }

        /// <summary>
        /// Oracle 方言对没有 WHERE 的查询也必须生成合法 SQL。
        /// </summary>
        [Fact]
        public void OracleDialect_ApplyTake_WrapsSelectWithoutWhere()
        {
            var dialect = new OracleDialect();

            Assert.Equal(
                "select * from (select * from t) where rownum <= 5",
                dialect.ApplyTake("select * from t", 5));
        }

        /// <summary>
        /// Oracle 方言对带 WHERE / ORDER BY / 结尾分号的查询统一包装。
        /// </summary>
        [Fact]
        public void OracleDialect_ApplyTake_WrapsFilteredOrderedAndTerminatedQueries()
        {
            var dialect = new OracleDialect();

            Assert.Equal(
                "select * from (select * from t where a = 1) where rownum <= 5",
                dialect.ApplyTake("select * from t where a = 1", 5));
            Assert.Equal(
                "select * from (select * from t order by id) where rownum <= 5",
                dialect.ApplyTake("select * from t order by id", 5));
            Assert.Equal(
                "select * from (select * from t) where rownum <= 5",
                dialect.ApplyTake("select * from t;", 5));
        }

        /// <summary>
        /// Oracle 方言对非查询语句与非正数 take 保持原样。
        /// </summary>
        [Fact]
        public void OracleDialect_ApplyTake_LeavesNonSelectAndNonPositiveTakeUnchanged()
        {
            var dialect = new OracleDialect();

            Assert.Equal("delete from t", dialect.ApplyTake("delete from t", 5));
            Assert.Equal("select * from t", dialect.ApplyTake("select * from t", 0));
            Assert.Null(dialect.ApplyTake(null, 5));
        }

        /// <summary>
        /// 各方言把常见参数前缀统一替换为目标数据库的前缀。
        /// </summary>
        [Theory]
        [InlineData(typeof(SqlServerDialect), "@")]
        [InlineData(typeof(LimitDialect), "@")]
        [InlineData(typeof(SqliteDialect), "@")]
        [InlineData(typeof(Db2Dialect), "@")]
        [InlineData(typeof(OracleDialect), ":")]
        public void BuildParameterName_NormalizesKnownPrefixes(Type dialectType, string expectedPrefix)
        {
            var dialect = (ISqlDialect)Activator.CreateInstance(dialectType, true);

            Assert.Equal(expectedPrefix + "id", dialect.BuildParameterName("id"));
            Assert.Equal(expectedPrefix + "id", dialect.BuildParameterName("@id"));
            Assert.Equal(expectedPrefix + "id", dialect.BuildParameterName(":id"));
            Assert.Equal(expectedPrefix + "id", dialect.BuildParameterName("?id"));
        }

        private static void AssertAdapter(DataDbType dbType, string expectedProvider, Type expectedDialectType)
        {
            var adapter = DatabaseAdapterFactory.Create(dbType);

            Assert.Equal(dbType, adapter.DbType);
            Assert.Equal(expectedProvider, adapter.ProviderName);
            Assert.Equal(expectedDialectType, adapter.Dialect.GetType());
        }
    }
}
