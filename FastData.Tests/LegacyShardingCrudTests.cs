using System;
using System.Reflection;
using FastData.Sharding;
using Xunit;

namespace FastData.Tests
{
    public class LegacyShardingCrudTests
    {
        [Theory]
        [InlineData("QueryFromShard", "ShardQuery", "ShardingReadHelper")]
        [InlineData("InsertToShard", "ShardAdd", "ShardingWriteHelper")]
        [InlineData("UpdateInShard", "ShardUpdate", "ShardingWriteHelper")]
        [InlineData("DeleteFromShard", "ShardDelete", "ShardingWriteHelper")]
        public void LegacyCrud_IsWarningOnlyAndNamesTheUsableReplacement(string methodName, string replacement, string helper)
        {
            var method = GetLegacyMethod(methodName);
            var obsolete = method.GetCustomAttribute<ObsoleteAttribute>();

            Assert.NotNull(obsolete);
            Assert.False(obsolete.IsError);
            Assert.Contains("FastDataClient", obsolete.Message);
            Assert.Contains(replacement, obsolete.Message);
            Assert.Contains(helper, obsolete.Message);
        }

        [Theory]
        [InlineData("QueryFromShard", "ShardQuery")]
        [InlineData("InsertToShard", "ShardAdd")]
        [InlineData("UpdateInShard", "ShardUpdate")]
        [InlineData("DeleteFromShard", "ShardDelete")]
        public void LegacyCrud_StillRejectsExecutionWithReplacementGuidance(string methodName, string replacement)
        {
            var method = GetLegacyMethod(methodName).MakeGenericMethod(typeof(LegacyEntity));
            var arguments = methodName == "QueryFromShard"
                ? new object[] { "legacy_target", null }
                : new object[] { new LegacyEntity { Id = 7 } };

            var invocation = Assert.Throws<TargetInvocationException>(() => method.Invoke(null, arguments));
            var exception = Assert.IsType<NotSupportedException>(invocation.InnerException);
            Assert.Contains("FastDataClient", exception.Message);
            Assert.Contains(replacement, exception.Message);
        }

        private static MethodInfo GetLegacyMethod(string methodName)
        {
            var method = typeof(ShardingManager).GetMethod(methodName, BindingFlags.Public | BindingFlags.Static);
            Assert.NotNull(method);
            Assert.True(method.IsGenericMethodDefinition);
            return method;
        }

        private sealed class LegacyEntity
        {
            public LegacyEntity()
            {
            }

            public int Id { get; set; }
        }
    }
}
