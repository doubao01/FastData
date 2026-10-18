// 手动添加 InternalsVisibleTo 特性
// 原因：FastData.Untility.csproj 中 <GenerateAssemblyInfo>false</GenerateAssemblyInfo>，
// SDK 不会自动生成 AssemblyInfo.cs，因此 csproj 中的 <InternalsVisibleTo> 项不会被处理。
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("FastData.Tests")]
