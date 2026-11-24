using FastData.Base;
using FastData.DbTypes;

namespace FastData.Adapter
{
    /// <summary>
    /// 数据库适配器工厂
    /// 
    /// 根据数据库类型创建对应的适配器实例。
    /// 
    /// 当前框架内部未调用本工厂，分页与参数逻辑硬编码在 BaseExecute 中；
    /// 本工厂仅作为公开扩展点保留。
    /// 
    /// 使用示例：
    /// <code>
    /// // 创建 SQL Server 适配器
    /// var adapter = DatabaseAdapterFactory.Create(DataDbType.SqlServer);
    /// 
    /// // 创建 MySQL 适配器
    /// var adapter = DatabaseAdapterFactory.Create(DataDbType.MySql);
    /// </code>
    /// </summary>
    public static class DatabaseAdapterFactory
    {
        /// <summary>
        /// 创建数据库适配器
        /// </summary>
        /// <param name="dbType">数据库类型</param>
        /// <returns>数据库适配器实例</returns>
        public static IDatabaseAdapter Create(DataDbType dbType)
        {
            switch (dbType)
            {
                case DataDbType.Oracle:
                    return new DatabaseAdapter(DataDbType.Oracle, Provider.Oracle, new OracleDialect());
                case DataDbType.MySql:
                    return new DatabaseAdapter(DataDbType.MySql, Provider.MySql, new LimitDialect());
                case DataDbType.DB2:
                    return new DatabaseAdapter(DataDbType.DB2, Provider.DB2, new Db2Dialect());
                case DataDbType.SQLite:
                    return new DatabaseAdapter(DataDbType.SQLite, Provider.SQLite, new SqliteDialect());
                case DataDbType.PostgreSql:
                    return new DatabaseAdapter(DataDbType.PostgreSql, Provider.PostgreSql, new LimitDialect());
                default:
                    return new DatabaseAdapter(DataDbType.SqlServer, Provider.SqlServer, new SqlServerDialect());
            }
        }
    }
}
