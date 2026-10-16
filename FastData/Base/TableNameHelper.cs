using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;
using FastData.Config;
using FastData.DbTypes;
using FastData.Model;

namespace FastData.Base
{
    internal static class TableNameHelper
    {
        private static readonly ConcurrentDictionary<string, string> _tableNameCache =
            new ConcurrentDictionary<string, string>();

        public static string GetTableName<T>(string dbKey = null)
        {
            return GetTableName(typeof(T), dbKey);
        }

        public static string GetTableName<T>(ConfigModel config)
        {
            return GetTableName(typeof(T), config?.Key);
        }

        public static string GetTableName(Type type, ConfigModel config)
        {
            return GetTableName(type, config?.Key);
        }

        public static string GetTableName(Type type, string dbKey = null)
        {
            if (type == null)
                throw new ArgumentNullException(nameof(type));

            var mappingKey = dbKey ?? FastDb.CurrentKey;
            var cacheKey = string.Format("{0}|{1}|{2}", type.FullName, dbKey ?? string.Empty, mappingKey ?? string.Empty);

            if (_tableNameCache.TryGetValue(cacheKey, out var cachedTableName))
                return cachedTableName;

            var attr = type.GetCustomAttribute<Property.TableAttribute>();
            if (attr != null)
            {
                if (!string.IsNullOrEmpty(attr.DbTableNames) && !string.IsNullOrEmpty(mappingKey))
                {
                    var tableName = GetTableNameFromMapping(attr.DbTableNames, mappingKey);
                    if (!string.IsNullOrEmpty(tableName))
                        return _tableNameCache.GetOrAdd(cacheKey, QuoteTableName(tableName, mappingKey));
                }

                if (!string.IsNullOrEmpty(attr.Name))
                    return _tableNameCache.GetOrAdd(cacheKey, QuoteTableName(attr.Name, dbKey));
            }

            var trackingAttr = type.GetCustomAttribute<ChangeTracking.TableNameAttribute>();
            if (trackingAttr != null && !string.IsNullOrEmpty(trackingAttr.Name))
            {
                return _tableNameCache.GetOrAdd(cacheKey, QuoteTableName(trackingAttr.Name, dbKey));
            }

            return _tableNameCache.GetOrAdd(cacheKey, QuoteTableName(type.Name, dbKey));
        }

        private static string QuoteTableName(string tableName, DataDbType dbType)
        {
            if (string.IsNullOrEmpty(tableName))
                return tableName;

            switch (dbType)
            {
                case DataDbType.PostgreSql:
                    return tableName.ToLowerInvariant();

                case DataDbType.MySql:
                    return $"`{tableName}`";

                default:
                    return tableName;
            }
        }

        private static string QuoteTableName(string tableName, string dbKey)
        {
            if (string.IsNullOrEmpty(tableName) || string.IsNullOrEmpty(dbKey))
                return tableName;

            try
            {
                var dbConfig = FastDataConfig.GetConfig(dbKey);
                if (dbConfig != null)
                    return QuoteTableName(tableName, dbConfig.DbType);
            }
            catch { }

            return tableName;
        }

        private static string GetTableNameFromMapping(string dbTableNames, string dbKey)
        {
            if (string.IsNullOrEmpty(dbTableNames) || string.IsNullOrEmpty(dbKey))
                return null;

            var pairs = dbTableNames.Split(',');
            foreach (var pair in pairs)
            {
                var parts = pair.Trim().Split('.');
                if (parts.Length == 2 && string.Equals(parts[0].Trim(), dbKey, StringComparison.OrdinalIgnoreCase))
                    return parts[1].Trim();
            }

            return null;
        }
    }
}
