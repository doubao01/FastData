using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;

namespace FastData.Tools.ApiBaseline
{
    /// <summary>
    /// 导出程序集的公开 API 清单，用于版本间比对，防止无意破坏兼容性。
    /// <para>
    /// 为什么不直接用 Microsoft.CodeAnalysis.PublicApiAnalyzers：
    /// 它的初始 PublicAPI.Shipped.txt 需要在 IDE 里用 code fix 批量生成，
    /// 命令行环境无法一次成型。这里用反射导出一份等价的快照，
    /// CI 比对 diff —— 语义上达到同样目的：公开 API 的任何变更都必须显式可见。
    /// </para>
    /// <para>
    /// 输出格式（每行一个符号，已排序，便于 diff）：
    /// T:命名空间.类型
    /// M:命名空间.类型.方法(参数类型,...)
    /// P:命名空间.类型.属性
    /// F:命名空间.类型.字段
    /// E:命名空间.类型.事件
    /// </para>
    /// </summary>
    public static class Program
    {
        public static int Main(string[] args)
        {
            var assemblies = new[]
            {
                typeof(FastData.FastRead).Assembly,              // FastData
                typeof(FastUntility.Base.BaseSymmetric).Assembly // FastData.Untility
            };

            var lines = new SortedSet<string>(StringComparer.Ordinal);

            foreach (var assembly in assemblies)
            {
                foreach (var type in SafeGetTypes(assembly))
                {
                    if (!IsVisible(type))
                        continue;

                    lines.Add("T:" + TypeName(type));

                    const BindingFlags flags = BindingFlags.Public
                                             | BindingFlags.Instance
                                             | BindingFlags.Static
                                             | BindingFlags.DeclaredOnly;

                    foreach (var ctor in type.GetConstructors(flags))
                        lines.Add(Signature("M", type, ".ctor", ctor.GetParameters(), null, ctor.IsStatic));

                    foreach (var method in type.GetMethods(flags))
                    {
                        if (method.IsSpecialName) continue;          // 属性/事件的访问器不单独记录
                        if (IsCompilerGenerated(method)) continue;

                        lines.Add(Signature("M", type, "." + method.Name,
                            method.GetParameters(), method.ReturnType, method.IsStatic));
                    }

                    foreach (var property in type.GetProperties(flags))
                        lines.Add(string.Format("P:{0}.{1}{2}", TypeName(type), property.Name, property.GetIndexParameters().Length > 0 ? "[indexed]" : ""));

                    foreach (var field in type.GetFields(flags))
                    {
                        if (IsCompilerGenerated(field)) continue;
                        lines.Add(string.Format("F:{0}.{1}", TypeName(type), field.Name));
                    }

                    foreach (var evt in type.GetEvents(flags))
                        lines.Add(string.Format("E:{0}.{1}", TypeName(type), evt.Name));
                }
            }

            var output = new StringBuilder();
            output.AppendLine("# FastData 公开 API 基线（自动生成，请勿手工编辑）");
            output.AppendLine("# 本文件由 tools/ApiBaseline 导出，CI 会比对快照差异。");
            output.AppendLine("# 如果变更是有意的：");
            output.AppendLine("#   dotnet run --project tools/ApiBaseline > FastData/docs/PublicAPI.txt");
            output.AppendLine("# 如果变更是无意的：说明破坏了向后兼容，请回退。");
            output.AppendLine("#");
            output.AppendLine("# 注意：仅覆盖 net8.0。net452 目标在 Linux 上无法加载，未纳入。");

            foreach (var line in lines)
                output.AppendLine(line);

            Console.Write(output.ToString());
            return 0;
        }

        private static IEnumerable<Type> SafeGetTypes(Assembly assembly)
        {
            try { return assembly.GetExportedTypes(); }
            catch (ReflectionTypeLoadException ex) { return ex.Types.Where(t => t != null); }
        }

        private static bool IsVisible(Type type)
        {
            return type.IsPublic || type.IsNestedPublic;
        }

        private static bool IsCompilerGenerated(MemberInfo member)
        {
            return member.Name.Contains("<")
                || member.IsDefined(typeof(System.Runtime.CompilerServices.CompilerGeneratedAttribute), false);
        }

        private static string Signature(string prefix, Type declaring, string member, ParameterInfo[] parameters, Type returnType, bool isStatic)
        {
            var args = string.Join(",", parameters.Select(p => TypeName(p.ParameterType)));
            var sb = new StringBuilder();
            sb.Append(prefix).Append(':').Append(TypeName(declaring)).Append(member);
            if (prefix == "M")
                sb.Append('(').Append(args).Append(')');
            if (isStatic)
                sb.Append(" [static]");
            if (returnType != null)
                sb.Append(" -> ").Append(TypeName(returnType));
            return sb.ToString();
        }

        /// <summary>
        /// 类型名带命名空间，泛型参数统一写成 &lt;T&gt;，避免不同语句顺序造成假差异。
        /// </summary>
        private static string TypeName(Type type)
        {
            if (type == null) return "";

            if (type.IsByRef) return TypeName(type.GetElementType()) + "&";
            if (type.IsArray) return TypeName(type.GetElementType()) + "[]";

            if (type.IsGenericParameter) return type.Name;

            if (type.IsGenericType && !type.IsGenericTypeDefinition)
            {
                var def = type.GetGenericTypeDefinition();
                var baseName = (def.FullName ?? def.Name);
                var tick = baseName.IndexOf('`');
                if (tick > 0) baseName = baseName.Substring(0, tick);
                return baseName + "<" + string.Join(",", type.GetGenericArguments().Select(TypeName)) + ">";
            }

            if (type.IsGenericTypeDefinition)
            {
                var name = type.FullName ?? type.Name;
                var tick = name.IndexOf('`');
                return tick > 0 ? name.Substring(0, tick) : name;
            }

            return type.FullName ?? type.Name;
        }
    }
}