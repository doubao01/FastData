using System;

namespace FastData.ChangeTracking
{
    /// <summary>
    /// 表名帮助类（已统一转发至 FastData.Base.TableNameHelper）
    /// </summary>
    internal static class TableNameHelper
    {
        /// <summary>
        /// 获取实体对应的表名
        /// </summary>
        public static string GetTableName(Type type)
        {
            return Base.TableNameHelper.GetTableName(type);
        }
    }

    /// <summary>
    /// 表名特性
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
    public class TableNameAttribute : Attribute
    {
        /// <summary>
        /// Gets the mapped table name.
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// Initializes a table-name mapping attribute.
        /// </summary>
        /// <param name="name">Mapped table name.</param>
        public TableNameAttribute(string name)
        {
            Name = name;
        }
    }
}
