using System;
using System.Collections.Generic;
using System.Data;

#if NETFRAMEWORK
using System.Web.Script.Serialization;
#else
using System.Text.Json;
#endif

namespace FastData.Tooling.Sync
{
    /// <summary>
    /// 数据行 JSON 序列化工具
    /// 用于失败记录的持久化存储
    /// </summary>
    public static class DataRowSerializer
    {
#if NETFRAMEWORK
        private static readonly JavaScriptSerializer Serializer = new JavaScriptSerializer();
#endif

        /// <summary>
        /// 将 DataRow 序列化为 JSON 字符串
        /// </summary>
        /// <param name="table">数据表结构</param>
        /// <param name="row">数据行</param>
        /// <returns>JSON 字符串</returns>
        public static string Serialize(DataTable table, DataRow row)
        {
            var data = new Dictionary<string, object>();

            // 添加表结构信息
            var columns = new List<Dictionary<string, object>>();
            foreach (DataColumn col in table.Columns)
            {
                columns.Add(new Dictionary<string, object>
                {
                    { "ColumnName", col.ColumnName },
                    { "DataType", col.DataType.FullName },
                    { "IsNullable", col.AllowDBNull }
                });
            }
            data["Columns"] = columns;

            // 添加行数据
            var values = new Dictionary<string, object>();
            foreach (DataColumn col in table.Columns)
            {
                var value = row[col];
                values[col.ColumnName] = Convert.IsDBNull(value) ? null : value;
            }
            data["Values"] = values;

#if NETFRAMEWORK
            return Serializer.Serialize(data);
#else
            return JsonSerializer.Serialize(data, new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
#endif
        }

        /// <summary>
        /// 从 JSON 字符串反序列化为 DataTable
        /// </summary>
        /// <param name="json">JSON 字符串</param>
        /// <returns>DataTable</returns>
        public static DataTable Deserialize(string json)
        {
            var table = new DataTable();
            if (string.IsNullOrWhiteSpace(json))
                return table;

            var columnDefs = new List<Tuple<string, string, bool>>();
            var values = new List<KeyValuePair<string, object>>();

#if NETFRAMEWORK
            var data = Serializer.Deserialize<Dictionary<string, object>>(json);
            if (data == null)
                return table;

            if (data.ContainsKey("Columns") && data["Columns"] is System.Collections.IEnumerable columns)
            {
                foreach (var colObj in columns)
                {
                    if (colObj is IDictionary<string, object> colDict &&
                        colDict.ContainsKey("ColumnName") && colDict.ContainsKey("DataType"))
                    {
                        var columnName = Convert.ToString(colDict["ColumnName"]);
                        var dataTypeName = Convert.ToString(colDict["DataType"]);
                        var isNullable = colDict.ContainsKey("IsNullable") && colDict["IsNullable"] != null && Convert.ToBoolean(colDict["IsNullable"]);
                        columnDefs.Add(Tuple.Create(columnName, dataTypeName, isNullable));
                    }
                }
            }

            if (data.ContainsKey("Values") && data["Values"] is IDictionary<string, object> valueMap)
            {
                foreach (var kvp in valueMap)
                {
                    values.Add(new KeyValuePair<string, object>(kvp.Key, kvp.Value));
                }
            }
#else
            using (var doc = JsonDocument.Parse(json))
            {
                var root = doc.RootElement;

                if (root.TryGetProperty("Columns", out var columnsElement) && columnsElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (var colElement in columnsElement.EnumerateArray())
                    {
                        if (colElement.TryGetProperty("ColumnName", out var nameElement) &&
                            colElement.TryGetProperty("DataType", out var typeElement))
                        {
                            var columnName = nameElement.GetString();
                            var dataTypeName = typeElement.GetString();
                            var isNullable = colElement.TryGetProperty("IsNullable", out var nullableElement)
                                && nullableElement.ValueKind == JsonValueKind.True;
                            columnDefs.Add(Tuple.Create(columnName, dataTypeName, isNullable));
                        }
                    }
                }

                if (root.TryGetProperty("Values", out var valuesElement) && valuesElement.ValueKind == JsonValueKind.Object)
                {
                    foreach (var prop in valuesElement.EnumerateObject())
                    {
                        values.Add(new KeyValuePair<string, object>(prop.Name, ConvertJsonValue(prop.Value)));
                    }
                }
            }
#endif

            // 根据列定义解析类型并构建列
            var columnTypes = new Dictionary<string, Type>(StringComparer.Ordinal);
            foreach (var def in columnDefs)
            {
                if (string.IsNullOrEmpty(def.Item1) || table.Columns.Contains(def.Item1))
                    continue;

                var columnType = ResolveColumnType(def.Item2);
                var column = table.Columns.Add(def.Item1, columnType);
                column.AllowDBNull = def.Item3 || !columnType.IsValueType || Nullable.GetUnderlyingType(columnType) != null;
                columnTypes[def.Item1] = columnType;
            }

            if (table.Columns.Count == 0)
                return table;

            var row = table.NewRow();
            foreach (var kvp in values)
            {
                if (!table.Columns.Contains(kvp.Key))
                    continue;

                var targetType = columnTypes[kvp.Key];
                row[kvp.Key] = ConvertToColumnType(kvp.Value, targetType);
            }
            table.Rows.Add(row);

            return table;
        }

        /// <summary>
        /// 解析列的数据类型（兼容 Nullable 与常见类型别名）
        /// </summary>
        internal static Type ResolveColumnType(string dataTypeName)
        {
            if (string.IsNullOrEmpty(dataTypeName))
                return typeof(string);

            var type = Type.GetType(dataTypeName, throwOnError: false)
                ?? Type.GetType(dataTypeName + ", mscorlib", throwOnError: false)
                ?? Type.GetType(dataTypeName + ", System.Private.CoreLib", throwOnError: false);

            var underlying = type != null ? Nullable.GetUnderlyingType(type) : null;
            if (underlying != null)
                type = underlying;

            if (type != null)
                return type;

            var mapped = MapWellKnownType(dataTypeName);
            return mapped ?? typeof(string);
        }

        private static Type MapWellKnownType(string name)
        {
            var normalized = name != null && name.EndsWith("?", StringComparison.Ordinal)
                ? name.Substring(0, name.Length - 1)
                : name;

            switch (normalized)
            {
                case "System.String": return typeof(string);
                case "System.Int32": return typeof(int);
                case "System.Int64": return typeof(long);
                case "System.Int16": return typeof(short);
                case "System.Byte": return typeof(byte);
                case "System.SByte": return typeof(sbyte);
                case "System.UInt32": return typeof(uint);
                case "System.UInt64": return typeof(ulong);
                case "System.Decimal": return typeof(decimal);
                case "System.Double": return typeof(double);
                case "System.Single": return typeof(float);
                case "System.Boolean": return typeof(bool);
                case "System.DateTime": return typeof(DateTime);
                case "System.DateTimeOffset": return typeof(DateTimeOffset);
                case "System.TimeSpan": return typeof(TimeSpan);
                case "System.Guid": return typeof(Guid);
                case "System.Byte[]": return typeof(byte[]);
                default: return null;
            }
        }

        /// <summary>
        /// 将值转换为目标列类型，转换失败时保留原值
        /// </summary>
        internal static object ConvertToColumnType(object value, Type type)
        {
            if (value == null || value == DBNull.Value)
                return DBNull.Value;

            if (type == null || type == typeof(object))
                return value;

            var valueType = value.GetType();
            if (type.IsAssignableFrom(valueType))
                return value;

            try
            {
                if (type == typeof(string))
                    return Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture);

                if (type == typeof(DateTime))
                    return value is string dtStr
                        ? DateTime.Parse(dtStr, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None)
                        : Convert.ToDateTime(value, System.Globalization.CultureInfo.InvariantCulture);

                if (type == typeof(DateTimeOffset))
                    return value is string dtoStr
                        ? (object)DateTimeOffset.Parse(dtoStr, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None)
                        : new DateTimeOffset(Convert.ToDateTime(value, System.Globalization.CultureInfo.InvariantCulture));

                if (type == typeof(TimeSpan))
                    return value is TimeSpan ? value : TimeSpan.Parse(Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture), System.Globalization.CultureInfo.InvariantCulture);

                if (type == typeof(Guid))
                    return value is Guid ? value : Guid.Parse(Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture));

                if (type == typeof(byte[]))
                    return value is string base64 ? (object)Convert.FromBase64String(base64) : value;

                if (type.IsEnum)
                    return value is string enumStr ? Enum.Parse(type, enumStr, true) : Enum.ToObject(type, value);

                return Convert.ChangeType(value, type, System.Globalization.CultureInfo.InvariantCulture);
            }
            catch
            {
                // 转换失败时保留原值，交由写入目标库时抛出更明确的错误
                return value;
            }
        }

#if !NETFRAMEWORK
        private static object ConvertJsonValue(JsonElement element)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.Null:
                    return DBNull.Value;
                case JsonValueKind.Number:
                    if (element.TryGetInt64(out var longValue))
                        return longValue;
                    return element.GetDouble();
                case JsonValueKind.True:
                    return true;
                case JsonValueKind.False:
                    return false;
                default:
                    return element.GetString();
            }
        }
#endif

        /// <summary>
        /// 将 DataTable 批量序列化为 JSON 数组
        /// </summary>
        /// <param name="table">数据表</param>
        /// <returns>JSON 数组字符串</returns>
        public static string SerializeBatch(DataTable table)
        {
            var rows = new List<Dictionary<string, object>>();

            foreach (DataRow row in table.Rows)
            {
                var rowData = new Dictionary<string, object>();
                foreach (DataColumn col in table.Columns)
                {
                    var value = row[col];
                    rowData[col.ColumnName] = Convert.IsDBNull(value) ? null : value;
                }
                rows.Add(rowData);
            }

#if NETFRAMEWORK
            return Serializer.Serialize(rows);
#else
            return JsonSerializer.Serialize(rows, new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
#endif
        }

        /// <summary>
        /// 从 JSON 数组反序列化为 DataTable
        /// </summary>
        /// <param name="json">JSON 字符串</param>
        /// <returns>DataTable</returns>
        public static DataTable DeserializeBatch(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return new DataTable();

            var table = new DataTable();

#if NETFRAMEWORK
            var rows = Serializer.Deserialize<object[]>(json);
            if (rows == null || rows.Length == 0)
                return table;

            var firstRow = rows[0] as IDictionary<string, object>;
            if (firstRow == null)
                return table;

            foreach (var kvp in firstRow)
            {
                table.Columns.Add(kvp.Key, typeof(object));
            }

            foreach (var rowData in rows)
            {
                var rowDict = rowData as IDictionary<string, object>;
                if (rowDict != null)
                {
                    var row = table.NewRow();
                    foreach (var kvp in rowDict)
                    {
                        row[kvp.Key] = kvp.Value ?? DBNull.Value;
                    }
                    table.Rows.Add(row);
                }
            }
#else
            using (var doc = JsonDocument.Parse(json))
            {
                var root = doc.RootElement;
                if (root.GetArrayLength() == 0)
                    return table;

                var firstRow = root[0];
                foreach (var prop in firstRow.EnumerateObject())
                {
                    table.Columns.Add(prop.Name, typeof(object));
                }

                    foreach (var prop in root.EnumerateArray())
                    {
                        var row = table.NewRow();
                        foreach (var p in prop.EnumerateObject())
                        {
                            row[p.Name] = ConvertJsonValue(p.Value);
                        }
                        table.Rows.Add(row);
                    }
            }
#endif

            return table;
        }
    }
}
