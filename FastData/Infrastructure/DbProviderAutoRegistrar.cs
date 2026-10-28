using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Reflection;
#if NETFRAMEWORK
using FastData.Base;
#endif

namespace FastData.Infrastructure;

/// <summary>
/// 数据库提供程序自动注册器
/// </summary>
public static class DbProviderAutoRegistrar
{
    private static bool _isRegistered = false;
    private static readonly object _lockObj = new();

    /// <summary>
    /// 对字符串执行不区分大小写的包含检查（兼容 .NET 4.5.2）
    /// </summary>
    private static bool ContainsIgnoreCase(string source, string value)
    {
        if (source == null) return false;
#if NETFRAMEWORK
        return source.IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0;
#else
        return source.Contains(value, StringComparison.OrdinalIgnoreCase);
#endif
    }

    private static readonly List<(string AssemblyKeyword, string FactoryTypeName, string InvariantName)> KnownProviders = new()
    {
        ("System.Data.SqlClient", "System.Data.SqlClient.SqlClientFactory", "System.Data.SqlClient"),
        ("Microsoft.Data.SqlClient", "Microsoft.Data.SqlClient.SqlClientFactory", "Microsoft.Data.SqlClient"),
        ("MySql.Data", "MySql.Data.MySqlClient.MySqlClientFactory", "MySql.Data.MySqlClient"),
        ("Pomelo", "MySql.Data.MySqlClient.MySqlClientFactory", "MySql.Data.MySqlClient"),
        ("Npgsql", "Npgsql.NpgsqlFactory", "Npgsql"),
        ("Microsoft.Data.Sqlite", "Microsoft.Data.Sqlite.SqliteFactory", "Microsoft.Data.Sqlite"),
        ("System.Data.SQLite", "System.Data.SQLite.SQLiteFactory", "System.Data.SQLite"),
        ("Oracle.ManagedDataAccess", "Oracle.ManagedDataAccess.Client.OracleClientFactory", "Oracle.ManagedDataAccess.Client"),
    };

    private static readonly Dictionary<string, string> ProviderPackages = new(StringComparer.OrdinalIgnoreCase)
    {
        { "System.Data.SqlClient", "System.Data.SqlClient" },
        { "Microsoft.Data.SqlClient", "Microsoft.Data.SqlClient" },
        { "MySql.Data.MySqlClient", "MySql.Data" },
        { "Npgsql", "Npgsql" },
        { "Microsoft.Data.Sqlite", "Microsoft.Data.Sqlite" },
        { "System.Data.SQLite", "System.Data.SQLite.Core" },
        { "Oracle.ManagedDataAccess.Client", "Oracle.ManagedDataAccess" },
        { "IBM.Data.DB2.Core", "IBM.Data.DB2.Core" },
    };

    /// <summary>
    /// 获取 DbProviderFactory。若驱动缺失，会返回包含 NuGet 安装命令的异常信息。
    /// </summary>
    /// <param name="providerName">ADO.NET Provider invariant name。</param>
    /// <returns>已注册的 DbProviderFactory。</returns>
    public static DbProviderFactory GetFactory(string providerName)
    {
        if (string.IsNullOrWhiteSpace(providerName))
            throw new InvalidOperationException("ProviderName 为空，请检查数据库配置。");

        Register();

        try
        {
            return DbProviderFactories.GetFactory(providerName);
        }
        catch (Exception ex)
        {
            throw CreateMissingProviderException(providerName, ex);
        }
    }

    /// <summary>
    /// 检查指定 Provider 是否可用。驱动已在应用运行目录或依赖图中存在时会自动注册。
    /// </summary>
    /// <param name="providerName">ADO.NET Provider invariant name。</param>
    public static void EnsureProvider(string providerName)
    {
        GetFactory(providerName);
    }

    /// <summary>
    /// 获取指定 Provider 对应的 NuGet 安装命令。
    /// </summary>
    /// <param name="providerName">ADO.NET Provider invariant name。</param>
    /// <returns>dotnet add package 命令。</returns>
    public static string GetInstallCommand(string providerName)
    {
        var packageId = GetPackageId(providerName);
        return string.Format("dotnet add package {0}", packageId);
    }

    /// <summary>
    /// 扫描并注册当前应用中已存在的数据库驱动程序集。
    /// </summary>
    public static void Register()
    {
        if (_isRegistered) return;

        lock (_lockObj)
        {
            if (_isRegistered) return;

            try
            {
                var registeredCount = 0;

                // 第一遍：扫描已加载的程序集
                var assemblies = AppDomain.CurrentDomain.GetAssemblies();
                foreach (var (assemblyKeyword, factoryTypeName, invariantName) in KnownProviders)
                {
                    if (!IsProviderRegistered(invariantName) && TryRegisterFromAssemblies(assemblies, assemblyKeyword, factoryTypeName, invariantName))
                        registeredCount++;
                }

                // 第二遍：对未注册的提供程序，显式加载程序集
                foreach (var (assemblyKeyword, factoryTypeName, invariantName) in KnownProviders)
                {
                    if (!IsProviderRegistered(invariantName) && TryRegisterByAssemblyLoad(assemblyKeyword, factoryTypeName, invariantName))
                        registeredCount++;
                }

                _isRegistered = true;

                if (Environment.GetEnvironmentVariable("FASTDATA_DEBUG") == "true")
                    Console.WriteLine(string.Format("[FastData] Auto-registered {0} database provider(s)", registeredCount));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(string.Format("[FastData] Auto-registration warning: {0}", ex.Message));
            }
        }
    }

    private static InvalidOperationException CreateMissingProviderException(string providerName, Exception innerException)
    {
        var packageId = GetPackageId(providerName);
        var message = string.Format(
            "DbProviderFactory 未找到或驱动程序集未加载：{0}。" +
            "FastData 已尝试自动注册当前 AppDomain 中存在的数据库驱动，但没有找到匹配程序集。" +
            "请在应用项目中引用对应 NuGet 包，或在裸 DLL 部署时把匹配版本的驱动 DLL 一起复制到运行目录。" +
            "建议命令：dotnet add package {1}",
            providerName,
            packageId);

        return new InvalidOperationException(message, innerException);
    }

    private static string GetPackageId(string providerName)
    {
        if (!string.IsNullOrWhiteSpace(providerName) && ProviderPackages.TryGetValue(providerName, out var packageId))
            return packageId;

        return providerName;
    }

    private static bool TryRegisterFromAssemblies(IEnumerable<Assembly> assemblies, string assemblyKeyword, string factoryTypeName, string invariantName)
    {
        try
        {
            foreach (var assembly in assemblies)
            {
                if (!ContainsIgnoreCase(assembly.GetName().Name, assemblyKeyword))
                    continue;

                if (TryRegisterFromAssembly(assembly, factoryTypeName, invariantName))
                    return true;
            }
        }
        catch (Exception ex)
        {
            // 内层的 Assembly.Load 探测失败是预期情况，保持静默；
            // 但走到这里的异常不是「找不到程序集」，而是注册过程中出了别的问题，
            // 完全吞掉会让调用方只看到「注册失败」而无法排查。
            System.Diagnostics.Trace.TraceWarning(string.Format(
                "DbProviderAutoRegistrar 按已加载程序集注册 {0} 失败：{1}", invariantName, ex));
        }

        return false;
    }

    private static bool TryRegisterByAssemblyLoad(string assemblyKeyword, string factoryTypeName, string invariantName)
    {
        try
        {
            if (IsProviderRegistered(invariantName))
                return true;

            // 尝试按关键词加载程序集
            Assembly assembly = null;
            try { assembly = Assembly.Load(assemblyKeyword); }
            catch { }

            // 如果关键词加载失败，尝试按完整程序集名加载
            if (assembly == null)
            {
                var fallbackNames = GetFallbackAssemblyNames(assemblyKeyword);
                foreach (var name in fallbackNames)
                {
                    try { assembly = Assembly.Load(name); if (assembly != null) break; }
                    catch { }
                }
            }

            if (assembly == null) return false;

            return TryRegisterFromAssembly(assembly, factoryTypeName, invariantName);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.TraceWarning(string.Format(
                "DbProviderAutoRegistrar 按程序集名注册 {0} 失败：{1}", invariantName, ex));
        }

        return false;
    }

    private static bool TryRegisterFromAssembly(Assembly assembly, string factoryTypeName, string invariantName)
    {
        try
        {
            var factoryType = assembly.GetType(factoryTypeName);
            if (factoryType == null) return false;

            // Try to get factory instance - some providers use property, others use field
            DbProviderFactory factoryInstance = null;
            
            // Try property first (MySql, Npgsql, etc.)
            var instanceProperty = factoryType.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static);
            if (instanceProperty != null)
            {
                factoryInstance = instanceProperty.GetValue(null) as DbProviderFactory;
            }
            
            // Try field if property not found (Microsoft.Data.SqlClient uses field)
            if (factoryInstance == null)
            {
                var instanceField = factoryType.GetField("Instance", BindingFlags.Public | BindingFlags.Static);
                if (instanceField != null)
                {
                    factoryInstance = instanceField.GetValue(null) as DbProviderFactory;
                }
            }
            
            if (factoryInstance == null) return false;

            if (IsProviderRegistered(invariantName))
                return true;

#if !NETFRAMEWORK
            // .NET Core/5+ 支持运行时注册
            DbProviderFactories.RegisterFactory(invariantName, factoryInstance);
#else
            // .NET Framework 需在 app.config 中配置 provider，
            // 或通过反射注册到私有字段
            RegisterFactoryNetFramework(invariantName, factoryInstance);
#endif
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string[] GetFallbackAssemblyNames(string keyword)
    {
        return keyword switch
        {
            "Microsoft.Data.SqlClient" => new[] { "Microsoft.Data.SqlClient" },
            "System.Data.SqlClient" => new[] { "System.Data.SqlClient" },
            "MySql.Data" => new[] { "MySql.Data", "MySqlConnector" },
            "Pomelo" => new[] { "MySqlConnector", "MySql.Data" },
            "Npgsql" => new[] { "Npgsql" },
            "Microsoft.Data.Sqlite" => new[] { "Microsoft.Data.Sqlite" },
            "System.Data.SQLite" => new[] { "System.Data.SQLite", "System.Data.SQLite.Core" },
            "Oracle.ManagedDataAccess" => new[] { "Oracle.ManagedDataAccess", "Oracle.ManagedDataAccess.Core" },
            _ => new[] { keyword }
        };
    }

    private static bool IsProviderRegistered(string invariantName)
    {
        try
        {
            return DbProviderFactories.GetFactory(invariantName) != null;
        }
        catch
        {
            return false;
        }
    }

#if NETFRAMEWORK
    /// <summary>
    /// .NET Framework 下通过反射注册 DbProviderFactory
    /// </summary>
    private static void RegisterFactoryNetFramework(string invariantName, DbProviderFactory factory)
    {
        try
        {
            var factoriesField = typeof(DbProviderFactories)
                .GetField("_configTable", BindingFlags.Static | BindingFlags.NonPublic);
            if (factoriesField == null)
            {
                // 部分实现使用不同字段名
                factoriesField = typeof(DbProviderFactories)
                    .GetField("_registeredProviders", BindingFlags.Static | BindingFlags.NonPublic);
            }
            if (factoriesField != null)
            {
                var table = factoriesField.GetValue(null) as System.Collections.Hashtable;
                if (table != null)
                {
                    table[invariantName] = factory;
                }
            }
        }
        catch { }
    }
#endif
}
