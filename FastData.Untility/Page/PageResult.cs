using System.Collections.Generic;

namespace FastUntility.Page
{
    /// <summary>
    /// 分页返回实体
    /// </summary>
    public sealed class PageResult<T> where T : class,new()
    {
        /// <summary>
        /// 分页model
        /// </summary>
        public PageModel pModel = new PageModel();

        /// <summary>
        /// 分页列表
        /// </summary>
        public List<T> list = new List<T>();

        /// <summary>
        /// 实际执行的 SQL（含参数化后的显示形式）。
        /// <para>
        /// 分页返回空列表时，仅看 list 无法区分「条件本来就匹配 0 行」与「SQL 有问题」，
        /// 这个字段用于把两者分开。
        /// </para>
        /// </summary>
        public string Sql { get; set; }
    }


    /// <summary>
    /// 分页返回实体
    /// </summary>
    public class PageResult
    {
        /// <summary>
        /// 分页model
        /// </summary>
        public PageModel pModel = new PageModel();

        /// <summary>
        /// 分页列表
        /// </summary>
        public List<Dictionary<string, object>> list { get; set; }

        /// <summary>
        /// 实际执行的 SQL（含参数化后的显示形式）
        /// </summary>
        public string Sql { get; set; }
    }
}
