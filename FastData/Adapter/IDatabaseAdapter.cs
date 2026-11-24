using FastData.DbTypes;

namespace FastData.Adapter
{
    /// <summary>
    /// 数据库适配器接口（当前未被框架内部调用，仅供宿主自行接线）
    /// 
    /// 定义数据库适配器的基本属性，用于统一不同数据库的访问方式。
    /// 框架内部的分页与参数逻辑目前硬编码在 BaseExecute 中，不经过本接口；
    /// 这里仅作为公开扩展点保留，后续若接入请先补齐实现与契约测试。
    /// </summary>
    public interface IDatabaseAdapter
    {
        /// <summary>
        /// 数据库类型
        /// </summary>
        DataDbType DbType { get; }

        /// <summary>
        /// 数据库提供程序名称（如 System.Data.SqlClient）
        /// </summary>
        string ProviderName { get; }

        /// <summary>
        /// SQL 方言实现（用于生成特定数据库的 SQL 语句）
        /// </summary>
        ISqlDialect Dialect { get; }
    }
}
