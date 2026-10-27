using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace FastUntility.Base
{
    /// <summary>
    /// Oracle 反射方法缓存（避免每次 GetMethods() 扫描）
    /// </summary>
    internal static class OracleJsonMethodCache
    {
        public static readonly System.Reflection.MethodInfo GetOracleClob;
        public static readonly System.Reflection.MethodInfo GetClobValue;
        public static readonly System.Reflection.MethodInfo GetOracleBlob;
        public static readonly System.Reflection.MethodInfo GetBlobValue;

        static OracleJsonMethodCache()
        {
            try
            {
                var readerType = typeof(DbDataReader).Assembly
                    .GetType("System.Data.OracleClient.OracleDataReader");
                if (readerType == null) return;

                foreach (var m in readerType.GetMethods())
                {
                    if (m.Name == "GetOracleClob") GetOracleClob = m;
                    else if (m.Name == "GetOracleBlob") GetOracleBlob = m;
                }

                if (GetOracleClob != null)
                    foreach (var m in GetOracleClob.ReturnType.GetMethods())
                        if (m.Name == "get_Value") GetClobValue = m;

                if (GetOracleBlob != null)
                    foreach (var m in GetOracleBlob.ReturnType.GetMethods())
                        if (m.Name == "get_Value") GetBlobValue = m;
            }
            catch (Exception ex)
            {
                // 降级合理：未安装 Oracle 驱动时探测失败属于预期情况，
                // 后续 GetOracleClob/GetOracleBlob 为 null 时按普通值处理。
                // 但不能无痕迹，至少留下警告便于排查“CLOB 变成空串”类问题。
                System.Diagnostics.Trace.TraceWarning(
                    "BaseJson: 反射探测 OracleDataReader 方法失败，CLOB/BLOB 将按通用类型处理。" + ex);
            }
        }
    }

    /// <summary>
    /// JSON 操作类
    /// 提供 JSON 序列化、反序列化和转换功能
    /// </summary>
    public static class BaseJson
    {
        #region JSON 键值操作

        /// <summary>
        /// 检查 JSON 键是否存在或为空值
        /// </summary>
        /// <param name="key">JSON 键名</param>
        /// <param name="jo">JSON 对象</param>
        /// <returns>键不存在或值为空时返回 true</returns>
        public static bool IsJsonKeyNull(string key, JObject jo)
        {
            if (jo == null || key == null)
                return true;

            var property = jo.Property(key);
            if (property == null)
                return true;

            var value = jo[key];
            return value == null || string.IsNullOrEmpty(value.ToString());
        }

        /// <summary>
        /// 获取 JSON 键对应的值
        /// </summary>
        /// <param name="key">JSON 键名</param>
        /// <param name="defaultValue">键不存在时的默认值</param>
        /// <param name="jo">JSON 对象</param>
        /// <returns>JSON 键的值，不存在时返回 defaultValue</returns>
        public static string GetJsonValue(string key, string defaultValue, JObject jo)
        {
            if (jo == null || key == null)
                return defaultValue;

            var property = jo.Property(key);
            if (property == null)
                return defaultValue;

            var value = jo[key];
            if (value == null || string.IsNullOrEmpty(value.ToString()))
                return defaultValue;

            return value.ToString();
        }

        #endregion

        #region 对象与 JSON 转换

        /// <summary>
        /// 将实体对象转换为 JSON 字符串
        /// </summary>
        /// <param name="obj">待序列化的对象</param>
        /// <returns>JSON 字符串，序列化失败时返回空字符串</returns>
        public static string ObjectToJson(object obj)
        {
            if (obj == null)
                return string.Empty;

            try
            {
                return JsonConvert.SerializeObject(obj);
            }
            catch (Exception ex)
            {
                // 数据错误：序列化失败却返回空串，调用方会把“序列化失败”当成“内容为空”。
                // 签名不能改（仍返回 string），因此用日志让失败可见。
                BaseLog.SaveLog(string.Format("ObjectToJson 序列化失败，类型：{0}，异常：{1}",
                    obj.GetType().FullName, ex), "BaseJson_exp");
                return string.Empty;
            }
        }

        /// <summary>
        /// 将 JSON 字符串转换为实体对象
        /// </summary>
        /// <typeparam name="T">目标类型</typeparam>
        /// <param name="jsonString">JSON 字符串</param>
        /// <returns>反序列化后的对象，失败时返回 new T()</returns>
        public static T JsonToObject<T>(string jsonString) where T : class, new()
        {
            if (string.IsNullOrEmpty(jsonString))
                return new T();

            try
            {
                var result = JsonConvert.DeserializeObject<T>(jsonString);
                return result ?? new T();
            }
            catch (Exception ex)
            {
                // 数据错误：反序列化失败返回 new T() 会让调用方拿到“看起来正常的空对象”，
                // 这是最典型的静默数据错误。签名不能改，必须用日志把失败暴露出来。
                BaseLog.SaveLog(string.Format("JsonToObject<{0}> 反序列化失败，原始内容：{1}，异常：{2}",
                    typeof(T).FullName, jsonString, ex), "BaseJson_exp");
                return new T();
            }
        }

        /// <summary>
        /// 将 List 转换为 JSON 数组字符串
        /// </summary>
        /// <typeparam name="T">列表元素类型</typeparam>
        /// <param name="list">待转换的列表</param>
        /// <returns>JSON 数组字符串，失败时返回 "[]"</returns>
        public static string ListToJson<T>(List<T> list)
        {
            if (list == null)
                return "[]";

            try
            {
                return JsonConvert.SerializeObject(list);
            }
            catch (Exception ex)
            {
                // 数据错误：序列化失败返回 "[]" 与“空列表”无法区分，调用方会丢掉整批数据。
                // 签名不能改，用日志让失败可见。
                BaseLog.SaveLog(string.Format("ListToJson<{0}> 序列化失败，条数：{1}，异常：{2}",
                    typeof(T).FullName, list.Count, ex), "BaseJson_exp");
                return "[]";
            }
        }

        /// <summary>
        /// 将 JSON 数组字符串转换为 List
        /// </summary>
        /// <typeparam name="T">列表元素类型</typeparam>
        /// <param name="jsonString">JSON 数组字符串</param>
        /// <returns>反序列化后的列表，失败时返回空列表</returns>
        public static List<T> JsonToList<T>(string jsonString) where T : class, new()
        {
            if (string.IsNullOrEmpty(jsonString))
                return new List<T>();

            try
            {
                var array = JArray.Parse(jsonString);
                var list = new List<T>(array.Count);

                foreach (var item in array)
                {
                    var obj = JsonToObject<T>(item.ToString());
                    if (obj != null)
                        list.Add(obj);
                }

                return list;
            }
            catch (Exception ex)
            {
                // 数据错误：解析失败返回空列表会被当成“确实没有数据”，属于静默数据错误。
                // 签名不能改，用日志让失败可见。
                BaseLog.SaveLog(string.Format("JsonToList<{0}> 解析失败，原始内容：{1}，异常：{2}",
                    typeof(T).FullName, jsonString, ex), "BaseJson_exp");
                return new List<T>();
            }
        }

        #endregion

        #region JSON 与 Dictionary 转换

        /// <summary>
        /// 将 JSON 对象字符串转换为 Dictionary
        /// </summary>
        /// <param name="jsonString">JSON 对象字符串</param>
        /// <returns>字典对象，解析失败时返回空字典</returns>
        public static Dictionary<string, object> JsonToDictionary(string jsonString)
        {
            if (string.IsNullOrEmpty(jsonString))
                return new Dictionary<string, object>();

            try
            {
                var jo = JObject.Parse(jsonString);
                var dict = new Dictionary<string, object>(jo.Count);

                foreach (var property in jo.Properties())
                {
                    dict.Add(property.Name, property.Value);
                }

                return dict;
            }
            catch (Exception ex)
            {
                // 数据错误：解析失败返回空字典与“空对象”无法区分，属于静默数据错误。
                // 签名不能改，用日志让失败可见。
                BaseLog.SaveLog(string.Format("JsonToDictionary 解析失败，原始内容：{0}，异常：{1}",
                    jsonString, ex), "BaseJson_exp");
                return new Dictionary<string, object>();
            }
        }

        /// <summary>
        /// 将 JSON 数组字符串转换为 Dictionary 列表
        /// </summary>
        /// <param name="jsonString">JSON 数组字符串</param>
        /// <returns>字典列表，解析失败时返回空列表</returns>
        public static List<Dictionary<string, object>> JsonToDictionaryList(string jsonString)
        {
            if (string.IsNullOrEmpty(jsonString))
                return new List<Dictionary<string, object>>();

            try
            {
                var array = JArray.Parse(jsonString);
                var list = new List<Dictionary<string, object>>(array.Count);

                foreach (var item in array)
                {
                    list.Add(JsonToDictionary(item.ToString()));
                }

                return list;
            }
            catch (Exception ex)
            {
                // 数据错误：解析失败返回空列表会被当成“确实没有数据”，属于静默数据错误。
                // 签名不能改，用日志让失败可见。
                BaseLog.SaveLog(string.Format("JsonToDictionaryList 解析失败，原始内容：{0}，异常：{1}",
                    jsonString, ex), "BaseJson_exp");
                return new List<Dictionary<string, object>>();
            }
        }

        #endregion

        #region DataReader 转换

        /// <summary>
        /// 将 DbDataReader 转换为 JSON 字符串
        /// </summary>
        /// <param name="reader">数据库读取器</param>
        /// <param name="isOracle">是否为 Oracle 数据库（处理特殊类型）</param>
        /// <returns>JSON 数组字符串</returns>
        public static string DataReaderToJson(DbDataReader reader, bool isOracle = false)
        {
            if (reader == null)
                return "[]";

            var result = new List<Dictionary<string, object>>();
            var columnNames = new List<string>(reader.FieldCount);

            // 获取列名
            for (var i = 0; i < reader.FieldCount; i++)
            {
                columnNames.Add(reader.GetName(i));
            }

            while (reader.Read())
            {
                var row = new Dictionary<string, object>(columnNames.Count);

                foreach (var columnName in columnNames)
                {
                    var value = reader[columnName];

                    if (value is DBNull)
                    {
                        row.Add(columnName.ToLower(), string.Empty);
                    }
                    else if (isOracle)
                    {
                        var oracleValue = HandleOracleValue(reader, columnName, value);
                        row.Add(columnName.ToLower(), oracleValue);
                    }
                    else
                    {
                        row.Add(columnName.ToLower(), value);
                    }
                }

                result.Add(row);
            }

            return JsonConvert.SerializeObject(result, Formatting.None);
        }

        /// <summary>
        /// 将 DbDataReader 转换为 Dictionary 列表
        /// </summary>
        /// <param name="reader">数据库读取器</param>
        /// <param name="isOracle">是否为 Oracle 数据库（处理特殊类型）</param>
        /// <returns>数据字典列表</returns>
        public static List<Dictionary<string, object>> DataReaderToDictionaryList(DbDataReader reader, bool isOracle = false)
        {
            if (reader == null)
                return new List<Dictionary<string, object>>();

            var result = new List<Dictionary<string, object>>();
            var columnNames = new List<string>(reader.FieldCount);

            // 获取不重复的列名
            for (var i = 0; i < reader.FieldCount; i++)
            {
                var name = reader.GetName(i);
                if (!columnNames.Exists(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase)))
                {
                    columnNames.Add(name);
                }
            }

            while (reader.Read())
            {
                var row = new Dictionary<string, object>(columnNames.Count);

                foreach (var columnName in columnNames)
                {
                    var value = reader[columnName];

                    if (value is DBNull)
                    {
                        row.Add(columnName.ToLower(), string.Empty);
                    }
                    else if (isOracle)
                    {
                        var oracleValue = HandleOracleValue(reader, columnName, value);
                        row.Add(columnName.ToLower(), oracleValue);
                    }
                    else
                    {
                        row.Add(columnName.ToLower(), value);
                    }
                }

                result.Add(row);
            }

            return result;
        }

        /// <summary>
        /// 将 DbDataReader 转换为 Dictionary 列表（旧方法名，为了向后兼容）
        /// </summary>
        /// <param name="reader">数据库读取器</param>
        /// <param name="isOracle">是否为 Oracle 数据库（处理特殊类型）</param>
        /// <returns>数据字典列表</returns>
        public static List<Dictionary<string, object>> DataReaderToDic(DbDataReader reader, bool isOracle = false)
        {
            return DataReaderToDictionaryList(reader, isOracle);
        }

        /// <summary>
        /// 处理 Oracle 特殊数据类型（CLOB、BLOB）
        /// </summary>
        /// <param name="reader">数据库读取器</param>
        /// <param name="columnName">列名</param>
        /// <param name="value">原始值</param>
        /// <returns>处理后的值</returns>
        private static object HandleOracleValue(DbDataReader reader, string columnName, object value)
        {
            if (value == null)
                return string.Empty;

            var ordinal = reader.GetOrdinal(columnName.ToUpper());
            var dataTypeName = reader.GetDataTypeName(ordinal).ToLower();

            // 处理 CLOB 类型
            if (dataTypeName == "clob" || dataTypeName == "nclob")
            {
                return GetOracleClobValue(reader, ordinal);
            }

            // 处理 BLOB 类型
            if (dataTypeName == "blob")
            {
                return GetOracleBlobValue(reader, ordinal);
            }

            return value;
        }

        /// <summary>
        /// 获取 Oracle CLOB 字段值
        /// </summary>
        private static object GetOracleClobValue(DbDataReader reader, int ordinal)
        {
            if (reader.IsDBNull(ordinal))
                return string.Empty;

            try
            {
                if (OracleJsonMethodCache.GetOracleClob != null)
                {
                    var clobObject = OracleJsonMethodCache.GetOracleClob.Invoke(reader, new object[] { ordinal });
                    if (clobObject != null && OracleJsonMethodCache.GetClobValue != null)
                        return OracleJsonMethodCache.GetClobValue.Invoke(clobObject, null);
                }
            }
            catch (Exception ex)
            {
                // 数据错误：CLOB 反射读取失败却返回空串，会让调用方以为字段本来就为空。
                // 签名不能改，用日志让失败可见。
                BaseLog.SaveLog(string.Format("读取 Oracle CLOB 字段失败，序号：{0}，异常：{1}",
                    ordinal, ex), "BaseJson_exp");
            }

            return string.Empty;
        }

        /// <summary>
        /// 获取 Oracle BLOB 字段值
        /// </summary>
        private static object GetOracleBlobValue(DbDataReader reader, int ordinal)
        {
            if (reader.IsDBNull(ordinal))
                return null;

            try
            {
                if (OracleJsonMethodCache.GetOracleBlob != null)
                {
                    var blobObject = OracleJsonMethodCache.GetOracleBlob.Invoke(reader, new object[] { ordinal });
                    if (blobObject != null && OracleJsonMethodCache.GetBlobValue != null)
                        return OracleJsonMethodCache.GetBlobValue.Invoke(blobObject, null);
                }
            }
            catch (Exception ex)
            {
                // 数据错误：BLOB 反射读取失败却返回 null，会让调用方以为字段本来就不存在。
                // 签名不能改，用日志让失败可见。
                BaseLog.SaveLog(string.Format("读取 Oracle BLOB 字段失败，序号：{0}，异常：{1}",
                    ordinal, ex), "BaseJson_exp");
            }

            return null;
        }

        #endregion
    }
}
