using System;
using System.Collections.Generic;
using System.Data.Common;

namespace FastData.Database
{
    /// <summary>
    /// 数据库提供程序映射
    /// 集中管理数据库类型与提供程序名称的映射关系
    /// </summary>
    public static class DatabaseProviderMappings
    {
        /// <summary>
        /// 提供程序名称到显示名称的映射
        /// </summary>
        public static readonly Dictionary<string, string> ProviderDisplayNames = new Dictionary<string, string>
        {
            { "System.Data.SqlClient", "SQL Server" },
            { "Microsoft.Data.SqlClient", "SQL Server" },
            { "MySql.Data.MySqlClient", "MySQL" },
            { "Oracle.ManagedDataAccess.Client", "Oracle" },
            { "System.Data.SQLite", "SQLite" },
            { "IBM.Data.DB2.iSeries", "DB2" },
            { "Npgsql", "PostgreSQL" }
        };

        /// <summary>
        /// 显示名称到提供程序名称的映射
        /// </summary>
        public static readonly Dictionary<string, string> DisplayNameToProvider = new Dictionary<string, string>
        {
            { "SQL Server", "System.Data.SqlClient" },
            { "MySQL", "MySql.Data.MySqlClient" },
            { "Oracle", "Oracle.ManagedDataAccess.Client" },
            { "SQLite", "System.Data.SQLite" },
            { "DB2", "IBM.Data.DB2.iSeries" },
            { "PostgreSQL", "Npgsql" }
        };

        /// <summary>
        /// 获取所有支持的提供程序名称列表
        /// </summary>
        public static string[] AllProviderNames => new[]
        {
            "System.Data.SqlClient",
            "Microsoft.Data.SqlClient",
            "MySql.Data.MySqlClient",
            "Oracle.ManagedDataAccess.Client",
            "System.Data.SQLite",
            "Npgsql"
        };

        /// <summary>
        /// 获取所有支持的显示名称列表
        /// </summary>
        public static string[] AllDisplayNames => new[]
        {
            "SQL Server",
            "MySQL",
            "Oracle",
            "SQLite",
            "PostgreSQL"
        };

        /// <summary>
        /// 根据提供程序名称获取显示名称
        /// </summary>
        public static string GetDisplayName(string providerName)
        {
            return ProviderDisplayNames.TryGetValue(providerName, out var displayName) 
                ? displayName 
                : providerName;
        }

        /// <summary>
        /// 根据显示名称获取提供程序名称
        /// </summary>
        public static string GetProviderName(string displayName)
        {
            return DisplayNameToProvider.TryGetValue(displayName, out var providerName) 
                ? providerName 
                : displayName;
        }

        /// <summary>
        /// 检查是否支持该提供程序
        /// </summary>
        public static bool IsSupported(string providerName)
        {
            return ProviderDisplayNames.ContainsKey(providerName);
        }

        /// <summary>
        /// 将用户输入的 Provider 名称规范化为可直接用于 DbProviderFactories 的 invariant 名称。
        /// 支持三类输入：invariant 名称（原样返回）、显示名称（如 "SQL Server"）、
        /// 以及常见短名（如 "SqlServer"、"MySql"、"PostgreSql"）。
        /// 无法识别时返回去除首尾空白后的原值。
        /// </summary>
        /// <param name="providerName">用户输入的 Provider 名称</param>
        /// <returns>规范化后的 invariant 名称</returns>
        public static string NormalizeProviderName(string providerName)
        {
            if (string.IsNullOrWhiteSpace(providerName))
                return null;

            var trimmed = providerName.Trim();

            foreach (var name in AllProviderNames)
            {
                if (string.Equals(name, trimmed, StringComparison.OrdinalIgnoreCase))
                    return name;
            }

            var key = trimmed.Replace(" ", string.Empty).Replace("_", string.Empty).ToLowerInvariant();
            switch (key)
            {
                case "sqlserver":
                case "mssql":
                    return "Microsoft.Data.SqlClient";
                case "mysql":
                    return "MySql.Data.MySqlClient";
                case "postgresql":
                case "postgres":
                case "pgsql":
                case "npgsql":
                    return "Npgsql";
                case "sqlite":
                    return "System.Data.SQLite";
                case "oracle":
                    return "Oracle.ManagedDataAccess.Client";
                case "db2":
                    return "IBM.Data.DB2.iSeries";
                default:
                    return trimmed;
            }
        }

        /// <summary>
        /// 创建数据库连接
        /// </summary>
        /// <param name="providerName">提供程序名称</param>
        /// <param name="connectionString">连接字符串</param>
        /// <returns>数据库连接</returns>
        public static DbConnection CreateConnection(string providerName, string connectionString)
        {
            if (string.IsNullOrWhiteSpace(providerName))
                throw new ArgumentException("提供程序名称不能为空", nameof(providerName));

            if (string.IsNullOrWhiteSpace(connectionString))
                throw new ArgumentException("连接字符串不能为空", nameof(connectionString));

            var factory = DbProviderFactories.GetFactory(providerName);
            var connection = factory.CreateConnection();
            
            if (connection == null)
                throw new InvalidOperationException(string.Format("无法为提供程序 '{0}' 创建数据库连接", providerName));
            
            connection.ConnectionString = connectionString;
            return connection;
        }
    }
}
