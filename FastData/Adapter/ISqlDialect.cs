namespace FastData.Adapter
{
    /// <summary>
    /// SQL 方言接口（当前未被框架内部调用，仅供宿主自行接线）
    /// 
    /// 定义不同数据库的 SQL 语法差异，用于生成特定数据库的 SQL 语句。
    /// 框架内部的分页与参数逻辑目前硬编码在 BaseExecute 中，不经过本接口；
    /// 这里仅作为公开扩展点保留，后续若接入请先补齐实现与契约测试。
    /// </summary>
    public interface ISqlDialect
    {
        /// <summary>
        /// 应用行数限制子句（TOP/LIMIT/FETCH FIRST/ROWNUM）
        /// </summary>
        /// <param name="sql">原始 SQL 语句</param>
        /// <param name="take">获取的记录数</param>
        /// <returns>添加了行数限制的 SQL 语句</returns>
        string ApplyTake(string sql, int take);

        /// <summary>
        /// 构建参数名称
        /// </summary>
        /// <param name="name">参数名（不含前缀）</param>
        /// <returns>带前缀的参数名（如 @name、:name）</returns>
        string BuildParameterName(string name);
    }
}
