using System;
using FastData.Context;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using FastData.Model;
using FastData.Property;

namespace FastData.DevTools
{
    /// <summary>
    /// 数据导入导出工具
    /// </summary>
    public static class DataImporter
    {
        /// <summary>
        /// 从CSV文件导入数据
        /// </summary>
        public static (int success, int failed) ImportFromCSV<T>(string filePath, string dbKey = null) where T : class, new()
        {
            int successCount = 0;
            int failCount = 0;

            try
            {
                var lines = File.ReadAllLines(filePath);
                if (lines.Length < 2)
                {
                    return (0, 0);
                }

                var headers = lines[0].Split(',');
                var properties = PropertyCache.GetPropertiesCached<T>();

                for (int i = 1; i < lines.Length; i++)
                {
                    try
                    {
                        var values = lines[i].Split(',');
                        var entity = new T();

                        for (int j = 0; j < Math.Min(headers.Length, values.Length); j++)
                        {
                            var property = properties.FirstOrDefault(p => 
                                p.Name.Equals(headers[j].Trim(), StringComparison.OrdinalIgnoreCase));

                            if (property != null && property.CanWrite)
                            {
                                var value = values[j].Trim();
                                if (!string.IsNullOrEmpty(value))
                                {
                                    var convertedValue = Convert.ChangeType(value, property.PropertyType);
                                    property.SetValue(entity, convertedValue);
                                }
                            }
                        }

                        var result = FastData.FastWrite.Add<T>(entity, null, dbKey);
                        if (result.IsSuccess)
                        {
                            successCount++;
                        }
                        else
                        {
                            failCount++;
                        }
                    }
                    catch
                    {
                        failCount++;
                    }
                }
            }
            catch (Exception ex)
            {
                throw new Exception(string.Format("CSV导入失败: {0}", ex.Message), ex);
            }

            return (successCount, failCount);
        }

        /// <summary>
        /// 导出数据到CSV文件
        /// </summary>
        public static void ExportToCSV<T>(string filePath, System.Linq.Expressions.Expression<Func<T, bool>> expression = null, string dbKey = null) where T : class, new()
        {
            try
            {
                var list = expression == null 
                    ? FastData.FastRead.Query<T>(e => true, null, dbKey).ToList()
                    : FastData.FastRead.Query<T>(expression, null, dbKey).ToList();

                if (list == null || list.Count == 0)
                {
                    throw new Exception("没有可导出的数据");
                }

                var properties = PropertyCache.GetPropertiesCached<T>();
                var csv = new StringBuilder();

                // 写入表头
                csv.AppendLine(string.Join(",", properties.Select(p => p.Name)));

                // 写入数据
                foreach (var item in list)
                {
                    var values = properties.Select(p =>
                    {
                        var value = p.GetValue(item);
                        return value?.ToString() ?? "";
                    });
                    csv.AppendLine(string.Join(",", values));
                }

                File.WriteAllText(filePath, csv.ToString(), Encoding.UTF8);
            }
            catch (Exception ex)
            {
                throw new Exception(string.Format("CSV导出失败: {0}", ex.Message), ex);
            }
        }

        /// <summary>
        /// 批量导入数据
        /// </summary>
        public static (int success, int failed) BatchImport<T>(IEnumerable<T> entities, int batchSize = 1000, string dbKey = null) where T : class, new()
        {
            int successCount = 0;
            int failCount = 0;

            var batch = new List<T>(batchSize);

            foreach (var entity in entities)
            {
                batch.Add(entity);

                if (batch.Count >= batchSize)
                {
                    var result = FastData.FastWrite.AddList<T>(batch, dbKey);
                    successCount += result.IsSuccess ? batch.Count : 0;
                    failCount += result.IsSuccess ? 0 : batch.Count;
                    batch.Clear();
                }
            }

            if (batch.Count > 0)
            {
                var result = FastData.FastWrite.AddList<T>(batch, dbKey);
                successCount += result.IsSuccess ? batch.Count : 0;
                failCount += result.IsSuccess ? 0 : batch.Count;
            }

            return (successCount, failCount);
        }

        /// <summary>
        /// 同步数据（增量更新）
        /// </summary>
        public static (int inserted, int updated, int failed) SyncData<T>(
            IEnumerable<T> newData,
            Func<T, object> keySelector,
            string dbKey = null) where T : class, new()
        {
            int inserted = 0;
            int updated = 0;
            int failed = 0;

            var existingData = FastData.FastRead.Query<T>(e => true, null, dbKey).ToList() ?? new List<T>();
            var existingKeys = new HashSet<object>(existingData.Select(keySelector));

            foreach (var item in newData)
            {
                try
                {
                    var key = keySelector(item);

                    if (existingKeys.Contains(key))
                    {
                        var result = FastData.FastWrite.Update<T>(item, null, null, dbKey);
                        if (result.IsSuccess)
                        {
                            updated++;
                        }
                        else
                        {
                            failed++;
                        }
                    }
                    else
                    {
                        var result = FastData.FastWrite.Add<T>(item, null, dbKey);
                        if (result.IsSuccess)
                        {
                            inserted++;
                            existingKeys.Add(key);
                        }
                        else
                        {
                            failed++;
                        }
                    }
                }
                catch
                {
                    failed++;
                }
            }

            return (inserted, updated, failed);
        }
    }

    /// <summary>
    /// JSON数据导入导出工具
    /// </summary>
    public static class JsonImporter
    {
        /// <summary>
        /// 从JSON文件导入数据
        /// </summary>
        public static (int success, int failed) ImportFromJson<T>(string filePath, string dbKey = null) where T : class, new()
        {
            try
            {
                var json = File.ReadAllText(filePath);
                var entities = System.Text.Json.JsonSerializer.Deserialize<List<T>>(json);

                if (entities == null || entities.Count == 0)
                {
                    return (0, 0);
                }

                return DataImporter.BatchImport(entities, dbKey: dbKey);
            }
            catch (Exception ex)
            {
                throw new Exception(string.Format("JSON导入失败: {0}", ex.Message), ex);
            }
        }

        /// <summary>
        /// 导出数据到JSON文件
        /// </summary>
        public static void ExportToJson<T>(string filePath, System.Linq.Expressions.Expression<Func<T, bool>> expression = null, string dbKey = null) where T : class, new()
        {
            try
            {
                var list = expression == null 
                    ? FastData.FastRead.Query<T>(e => true, null, dbKey).ToList()
                    : FastData.FastRead.Query<T>(expression, null, dbKey).ToList();

                if (list == null || list.Count == 0)
                {
                    throw new Exception("没有可导出的数据");
                }

                var options = new System.Text.Json.JsonSerializerOptions
                {
                    WriteIndented = true
                };

                var json = System.Text.Json.JsonSerializer.Serialize(list, options);
                File.WriteAllText(filePath, json, Encoding.UTF8);
            }
            catch (Exception ex)
            {
                throw new Exception(string.Format("JSON导出失败: {0}", ex.Message), ex);
            }
        }
    }

    /// <summary>
    /// Excel数据导入导出工具（简单版）
    /// </summary>
    public static class ExcelImporter
    {
        /// <summary>
        /// 导出数据到Excel（CSV格式）
        /// </summary>
        public static void ExportToExcel<T>(string filePath, System.Linq.Expressions.Expression<Func<T, bool>> expression = null, string dbKey = null) where T : class, new()
        {
            try
            {
                var list = expression == null 
                    ? FastData.FastRead.Query<T>(e => true, null, dbKey).ToList()
                    : FastData.FastRead.Query<T>(expression, null, dbKey).ToList();

                if (list == null || list.Count == 0)
                {
                    throw new Exception("没有可导出的数据");
                }

                var properties = PropertyCache.GetPropertiesCached<T>();
                var csv = new StringBuilder();

                // 写入表头
                csv.AppendLine(string.Join("\t", properties.Select(p => p.Name)));

                // 写入数据
                foreach (var item in list)
                {
                    var values = properties.Select(p =>
                    {
                        var value = p.GetValue(item);
                        return value?.ToString().Replace("\t", " ") ?? "";
                    });
                    csv.AppendLine(string.Join("\t", values));
                }

                File.WriteAllText(filePath, csv.ToString(), Encoding.UTF8);
            }
            catch (Exception ex)
            {
                throw new Exception(string.Format("Excel导出失败: {0}", ex.Message), ex);
            }
        }
    }
}