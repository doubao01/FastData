using System;
using FastData.Context;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace FastData.DevTools
{
    /// <summary>
    /// 代码生成器 - 自动生成实体类和代码
    /// </summary>
    public static class CodeGenerator
    {
        /// <summary>
        /// 从数据库表生成实体类代码。
        /// </summary>
        /// <param name="tableName">表名</param>
        /// <param name="namespace">生成代码所在的命名空间</param>
        /// <exception cref="InvalidOperationException">取不到表结构时抛出。</exception>
        /// <remarks>
        /// 原实现不查数据库，<c>GetTableColumns</c> 直接返回写死的 6 列，
        /// 生成任何表都输出同一套字段。现在改为读取 INFORMATION_SCHEMA。
        /// <para>
        /// 取不到表结构时**抛出异常**而不是返回骨架代码 ——
        /// 生成一份看似正常、实际字段全错的实体，比直接失败更糟。
        /// </para>
        /// <para>
        /// 这个重载保留原有签名（2 参数），用于已编译程序集的二进制兼容；
        /// 需要指定连接时用三参数重载。
        /// </para>
        /// </remarks>
        public static string GenerateEntityFromTable(string tableName, string @namespace = "FastData.Model")
        {
            return GenerateEntityFromTable(tableName, @namespace, null);
        }

        /// <summary>
        /// 从数据库表生成实体类代码（指定连接）。
        /// </summary>
        /// <param name="tableName">表名</param>
        /// <param name="namespace">生成代码所在的命名空间</param>
        /// <param name="dbKey">连接配置的 Key；为空时使用 db.config 里的默认连接。</param>
        /// <exception cref="InvalidOperationException">取不到表结构时抛出。</exception>
        public static string GenerateEntityFromTable(string tableName, string @namespace, string dbKey)
        {
            var columns = GetTableColumns(tableName, dbKey);

            if (columns == null || columns.Count == 0)
            {
                throw new InvalidOperationException(string.Format(
                    "取不到表 {0} 的列信息（连接 Key={1}）。" +
                    "请确认表名正确、连接已配置；若目标数据库没有 INFORMATION_SCHEMA（如 Oracle），" +
                    "请改用 GenerateEntity 直接传入列元数据。",
                    tableName, dbKey ?? "<默认>"));
            }

            return GenerateEntity(columns, tableName, @namespace);
        }

        /// <summary>
        /// 用给定的列元数据生成实体类代码（不访问数据库）。
        /// </summary>
        /// <param name="columns">列元数据</param>
        /// <param name="tableName">表名</param>
        /// <param name="namespace">生成代码所在的命名空间</param>
        /// <returns>实体类源码</returns>
        /// <remarks>
        /// 这个重载是本类的核心：纯函数、无外部依赖，便于测试与在无库环境使用。
        /// </remarks>
        public static string GenerateEntity(IList<ColumnMetadata> columns, string tableName, string @namespace = "FastData.Model")
        {
            if (columns == null)
                throw new ArgumentNullException(nameof(columns));
            if (string.IsNullOrWhiteSpace(tableName))
                throw new ArgumentNullException(nameof(tableName));

            var sb = new System.Text.StringBuilder();

            sb.AppendLine("using System;");
            // FastData 自己的映射特性。原实现生成的是 [TableName] / [Key]：
            // [TableName] 在本库并不存在（编译不过），[Key] 是 DataAnnotations 的，
            // ORM 不认 —— 生成的实体既不能编译，也算不出主键。
            sb.AppendLine("using FastData.Property;");
            sb.AppendLine();
            sb.AppendLine(string.Format("namespace {0}", @namespace));
            sb.AppendLine("{");
            sb.AppendLine("    /// <summary>");
            sb.AppendLine(string.Format("    /// {0} 实体类（由 CodeGenerator 生成）", tableName));
            sb.AppendLine("    /// </summary>");
            sb.AppendLine(string.Format("    [Table(Name = \"{0}\")]", tableName));
            sb.AppendLine(string.Format("    public class {0}", ToPascalCase(tableName)));
            sb.AppendLine("    {");

            foreach (var prop in columns)
            {
                var propType = GetCSharpType(prop.DataType);
                var propName = ToPascalCase(prop.ColumnName);
                // string 本身可空，其它值类型才加 ?
                var nullableSuffix = (prop.IsNullable && propType != "string") ? "?" : "";

                sb.AppendLine("        /// <summary>");
                sb.AppendLine(string.Format("        /// {0}", prop.ColumnName));
                sb.AppendLine("        /// </summary>");

                var attrs = new List<string>();
                if (prop.IsPrimaryKey)
                    attrs.Add(string.Format("IsKey = true{0}", prop.IsIdentity ? ", IsIdentity = true" : ""));
                if (prop.MaxLength.HasValue && prop.MaxLength.Value > 0 && propType == "string")
                    attrs.Add(string.Format("Length = {0}", prop.MaxLength.Value));

                if (attrs.Count > 0)
                    sb.AppendLine(string.Format("        [Column({0})]", string.Join(", ", attrs)));

                sb.AppendLine(string.Format("        public {0}{1} {2} {{ get; set; }}",
                    propType, nullableSuffix, propName));
                sb.AppendLine();
            }

            sb.AppendLine("    }");
            sb.AppendLine("}");

            return sb.ToString();
        }

        /// <summary>
        /// 生成仓储类代码
        /// </summary>
        public static string GenerateRepositoryCode(string entityName, string @namespace = "FastData.Repository")
        {
            var sb = new System.Text.StringBuilder();

            sb.AppendLine("using System;");
            sb.AppendLine("using System.Collections.Generic;");
            sb.AppendLine("using System.Linq;");
            sb.AppendLine("using System.Linq.Expressions;");
            sb.AppendLine("using FastData;");
            sb.AppendLine();

            sb.AppendLine(string.Format("namespace {0}", @namespace));
            sb.AppendLine("{");
            sb.AppendLine("    /// <summary>");
            sb.AppendLine(string.Format("    /// {0} 仓储类", entityName));
            sb.AppendLine("    /// </summary>");
            sb.AppendLine(string.Format("    public class {0}Repository", entityName));
            sb.AppendLine("    {");
            sb.AppendLine("        /// <summary>");
            sb.AppendLine(string.Format("        /// 查询所有 {0}", entityName));
            sb.AppendLine("        /// </summary>");
            sb.AppendLine(string.Format("        public List<{0}> GetAll()", entityName));
            sb.AppendLine("        {");
            sb.AppendLine(string.Format("            return FastRead.Query<{0}>().ToList();", entityName));
            sb.AppendLine("        }");
            sb.AppendLine();
            sb.AppendLine("        /// <summary>");
            sb.AppendLine(string.Format("        /// 根据 ID 查询 {0}", entityName));
            sb.AppendLine("        /// </summary>");
            sb.AppendLine(string.Format("        public {0} GetById(int id)", entityName));
            sb.AppendLine("        {");
            sb.AppendLine(string.Format("            return FastRead.Query<{0}>(e => e.Id == id).ToItem();", entityName));
            sb.AppendLine("        }");
            sb.AppendLine();
            sb.AppendLine("        /// <summary>");
            sb.AppendLine(string.Format("        /// 添加 {0}", entityName));
            sb.AppendLine("        /// </summary>");
            sb.AppendLine(string.Format("        public WriteReturn Add({0} entity)", entityName));
            sb.AppendLine("        {");
            sb.AppendLine("            return FastWrite.Add(entity);");
            sb.AppendLine("        }");
            sb.AppendLine();
            sb.AppendLine("        /// <summary>");
            sb.AppendLine(string.Format("        /// 更新 {0}", entityName));
            sb.AppendLine("        /// </summary>");
            sb.AppendLine(string.Format("        public WriteReturn Update({0} entity)", entityName));
            sb.AppendLine("        {");
            sb.AppendLine("            return FastWrite.Update(entity);");
            sb.AppendLine("        }");
            sb.AppendLine();
            sb.AppendLine("        /// <summary>");
            sb.AppendLine(string.Format("        /// 删除 {0}", entityName));
            sb.AppendLine("        /// </summary>");
            sb.AppendLine("        public WriteReturn Delete(int id)");
            sb.AppendLine("        {");
            sb.AppendLine(string.Format("            return FastWrite.Delete<{0}>(e => e.Id == id);", entityName));
            sb.AppendLine("        }");
            sb.AppendLine();
            sb.AppendLine("        /// <summary>");
            sb.AppendLine(string.Format("        /// 分页查询 {0}", entityName));
            sb.AppendLine("        /// </summary>");
            sb.AppendLine(string.Format("        public (List<{0}> Items, int Total) GetPaged(int page, int pageSize)", entityName));
            sb.AppendLine("        {");
            sb.AppendLine(string.Format("            var query = FastRead.Query<{0}>();", entityName));
            sb.AppendLine("            var pageResult = query.ToPage(new FastUntility.Page.PageModel { PageId = page, PageSize = pageSize });");
            sb.AppendLine("            return (pageResult.list, pageResult.pModel.TotalRecord);");
            sb.AppendLine("        }");
            sb.AppendLine();
            sb.AppendLine("        /// <summary>");
            sb.AppendLine(string.Format("        /// 条件查询 {0}", entityName));
            sb.AppendLine("        /// </summary>");
            sb.AppendLine(string.Format("        public List<{0}> Find(Expression<Func<{0}, bool>> predicate)", entityName));
            sb.AppendLine("        {");
            sb.AppendLine(string.Format("            return FastRead.Query<{0}>(predicate).ToList();", entityName));
            sb.AppendLine("        }");
            sb.AppendLine("    }");
            sb.AppendLine("}");

            return sb.ToString();
        }

        /// <summary>
        /// 生成服务类代码
        /// </summary>
        public static string GenerateServiceCode(string entityName, string @namespace = "FastData.Service")
        {
            var sb = new System.Text.StringBuilder();

            sb.AppendLine("using System;");
            sb.AppendLine("using System.Collections.Generic;");
            sb.AppendLine("using FastData.Repository;");
            sb.AppendLine();

            sb.AppendLine(string.Format("namespace {0}", @namespace));
            sb.AppendLine("{");
            sb.AppendLine("    /// <summary>");
            sb.AppendLine(string.Format("    /// {0} 服务类", entityName));
            sb.AppendLine("    /// </summary>");
            sb.AppendLine(string.Format("    public class {0}Service", entityName));
            sb.AppendLine("    {");
            sb.AppendLine(string.Format("        private readonly {0}Repository _repository;", entityName));
            sb.AppendLine();
            sb.AppendLine(string.Format("        public {0}Service()", entityName));
            sb.AppendLine("        {");
            sb.AppendLine(string.Format("            _repository = new {0}Repository();", entityName));
            sb.AppendLine("        }");
            sb.AppendLine();
            sb.AppendLine("        /// <summary>");
            sb.AppendLine(string.Format("        /// 获取所有 {0}", entityName));
            sb.AppendLine("        /// </summary>");
            sb.AppendLine(string.Format("        public List<{0}> GetAll()", entityName));
            sb.AppendLine("        {");
            sb.AppendLine("            return _repository.GetAll();");
            sb.AppendLine("        }");
            sb.AppendLine();
            sb.AppendLine("        /// <summary>");
            sb.AppendLine(string.Format("        /// 根据 ID 获取 {0}", entityName));
            sb.AppendLine("        /// </summary>");
            sb.AppendLine(string.Format("        public {0} GetById(int id)", entityName));
            sb.AppendLine("        {");
            sb.AppendLine("            return _repository.GetById(id);");
            sb.AppendLine("        }");
            sb.AppendLine();
            sb.AppendLine("        /// <summary>");
            sb.AppendLine(string.Format("        /// 添加 {0}", entityName));
            sb.AppendLine("        /// </summary>");
            sb.AppendLine(string.Format("        public WriteReturn Add({0} entity)", entityName));
            sb.AppendLine("        {");
            sb.AppendLine("            return _repository.Add(entity);");
            sb.AppendLine("        }");
            sb.AppendLine();
            sb.AppendLine("        /// <summary>");
            sb.AppendLine(string.Format("        /// 更新 {0}", entityName));
            sb.AppendLine("        /// </summary>");
            sb.AppendLine(string.Format("        public WriteReturn Update({0} entity)", entityName));
            sb.AppendLine("        {");
            sb.AppendLine("            return _repository.Update(entity);");
            sb.AppendLine("        }");
            sb.AppendLine();
            sb.AppendLine("        /// <summary>");
            sb.AppendLine(string.Format("        /// 删除 {0}", entityName));
            sb.AppendLine("        /// </summary>");
            sb.AppendLine("        public WriteReturn Delete(int id)");
            sb.AppendLine("        {");
            sb.AppendLine("            return _repository.Delete(id);");
            sb.AppendLine("        }");
            sb.AppendLine("    }");
            sb.AppendLine("}");

            return sb.ToString();
        }

        /// <summary>
        /// 转换为 PascalCase
        /// </summary>
        private static string ToPascalCase(string text)
        {
            if (string.IsNullOrEmpty(text))
                return text;

            var words = text.Split(new[] { '_', '-', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            var result = string.Concat(words.Select(word => 
                char.ToUpper(word[0]) + word.Substring(1).ToLower()));

            return result;
        }

        /// <summary>
        /// 获取 C# 类型
        /// </summary>
        private static string GetCSharpType(string dbType)
        {
            return dbType.ToLower() switch
            {
                "int" => "int",
                "bigint" => "long",
                "smallint" => "short",
                "tinyint" => "byte",
                "bit" => "bool",
                "decimal" => "decimal",
                "money" => "decimal",
                "float" => "float",
                "double" => "double",
                "datetime" => "DateTime",
                "date" => "DateTime",
                "time" => "TimeSpan",
                "varchar" => "string",
                "nvarchar" => "string",
                "char" => "string",
                "nchar" => "string",
                "text" => "string",
                "ntext" => "string",
                "uniqueidentifier" => "Guid",
                _ => "object"
            };
        }

        /// <summary>
        /// 从数据库读取表的列信息。
        /// </summary>
        /// <param name="tableName">表名</param>
        /// <param name="dbKey">连接配置 Key；为空时用默认连接</param>
        /// <returns>列元数据；读不到时返回空集合</returns>
        /// <remarks>
        /// 原实现直接返回写死的 6 列（注释自认「这是示例实现」），
        /// 生成任何表都输出同一套字段。现在读 INFORMATION_SCHEMA。
        /// <para>
        /// 表名走白名单校验：它是标识符，无法参数化，只允许字母/数字/下划线，
        /// 挡掉拼接注入。
        /// </para>
        /// </remarks>
        public static IList<ColumnMetadata> GetTableColumns(string tableName, string dbKey = null)
        {
            var result = new List<ColumnMetadata>();

            if (string.IsNullOrWhiteSpace(tableName))
                return result;

            if (!IsSafeIdentifier(tableName))
                return result;

            try
            {
                using (var db = new Context.DataContext(dbKey))
                {
                    db.EnsureConnectionOpen();

                    // PRIMARY KEY / 自增列的信息不在 COLUMNS 视图里，需要 join 别的表。
                    // 各库差异太大，这里先取基础列信息，再用一次查询补主键。
                    db.cmd.Parameters.Clear();
                    db.cmd.CommandText =
                        "SELECT COLUMN_NAME, DATA_TYPE, IS_NULLABLE, CHARACTER_MAXIMUM_LENGTH " +
                        "FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = @p_table " +
                        "ORDER BY ORDINAL_POSITION";

                    var p = db.cmd.CreateParameter();
                    p.ParameterName = "@p_table";
                    p.Value = tableName;
                    db.cmd.Parameters.Add(p);

                    using (var reader = db.cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            var maxLen = reader.IsDBNull(3) ? (int?)null : Convert.ToInt32(reader.GetValue(3));

                            result.Add(new ColumnMetadata
                            {
                                ColumnName = Convert.ToString(reader.GetValue(0)),
                                DataType = Convert.ToString(reader.GetValue(1)),
                                IsNullable = string.Equals(Convert.ToString(reader.GetValue(2)), "YES", StringComparison.OrdinalIgnoreCase),
                                MaxLength = maxLen
                            });
                        }
                    }

                    if (result.Count == 0)
                        return result;

                    MarkPrimaryKeys(db, tableName, result);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.TraceWarning(string.Format(
                    "CodeGenerator.GetTableColumns 读取 {0} 失败：{1}: {2}",
                    tableName, ex.GetType().Name, ex.Message));
                return new List<ColumnMetadata>();
            }

            return result;
        }

        /// <summary>
        /// 标记主键与自增列。
        /// </summary>
        /// <remarks>
        /// 用 KEY_COLUMN_USAGE（标准 SQL，SQL Server / MySQL / PostgreSQL / SQLite 均支持）。
        /// 查询失败不影响列信息本身 —— 只是主键标记缺失，生成的实体少了 [Column(IsKey)]，
        /// 调用方仍可手工补。因此这里吞掉异常，但通过 Trace 留痕。
        /// </remarks>
        private static void MarkPrimaryKeys(Context.DataContext db, string tableName, List<ColumnMetadata> columns)
        {
            try
            {
                db.cmd.Parameters.Clear();
                db.cmd.CommandText =
                    "SELECT k.COLUMN_NAME FROM INFORMATION_SCHEMA.KEY_COLUMN_USAGE k " +
                    "INNER JOIN INFORMATION_SCHEMA.TABLE_CONSTRAINTS c " +
                    "  ON k.CONSTRAINT_NAME = c.CONSTRAINT_NAME AND k.TABLE_NAME = c.TABLE_NAME " +
                    "WHERE c.CONSTRAINT_TYPE = 'PRIMARY KEY' AND k.TABLE_NAME = @p_table";

                var p = db.cmd.CreateParameter();
                p.ParameterName = "@p_table";
                p.Value = tableName;
                db.cmd.Parameters.Add(p);

                var keyNames = new List<string>();
                using (var reader = db.cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        if (!reader.IsDBNull(0))
                            keyNames.Add(Convert.ToString(reader.GetValue(0)));
                    }
                }

                foreach (var col in columns)
                {
                    if (keyNames.Exists(k => string.Equals(k, col.ColumnName, StringComparison.OrdinalIgnoreCase)))
                    {
                        col.IsPrimaryKey = true;
                        // 单列整型主键按惯例视为自增；多列复合主键或非整型不自增。
                        var isWholeNumber = col.DataType != null &&
                            (col.DataType.IndexOf("int", StringComparison.OrdinalIgnoreCase) >= 0 ||
                             col.DataType.IndexOf("serial", StringComparison.OrdinalIgnoreCase) >= 0);
                        col.IsIdentity = keyNames.Count == 1 && isWholeNumber;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.TraceWarning(string.Format(
                    "CodeGenerator 读取主键失败（列信息仍可用）：{0}", ex.Message));
            }
        }

        /// <summary>
        /// 表名/列名等标识符只允许字母、数字、下划线、$。
        /// </summary>
        private static bool IsSafeIdentifier(string name)
        {
            if (string.IsNullOrEmpty(name))
                return false;

            if (!char.IsLetter(name[0]) && name[0] != '_')
                return false;

            foreach (var c in name)
            {
                if (!char.IsLetterOrDigit(c) && c != '_' && c != '$')
                    return false;
            }

            return true;
        }

        /// <summary>
        /// 列元数据。
        /// <para>
        /// 公开是为了让调用方能在没有数据库连接时（如从已有 DDL、设计稿）
        /// 直接构造列信息交给 <see cref="GenerateEntity"/> 生成代码。
        /// </para>
        /// </summary>
        public class ColumnMetadata
        {
            /// <summary>列名（数据库中的原始名称）。</summary>
            public string ColumnName { get; set; }

            /// <summary>数据库类型名（如 int / nvarchar / datetime）。</summary>
            public string DataType { get; set; }

            /// <summary>是否允许 NULL。</summary>
            public bool IsNullable { get; set; }

            /// <summary>字符类型最大长度；非字符类型为 null。</summary>
            public int? MaxLength { get; set; }

            /// <summary>是否主键。原实现没有这个信息，导致生成不出 [Column(IsKey = true)]。</summary>
            public bool IsPrimaryKey { get; set; }

            /// <summary>是否自增（identity / auto_increment / serial）。</summary>
            public bool IsIdentity { get; set; }
        }
    }
}
