using System;
using System.Collections.Generic;
using FastData.Sharding;
using FastData.Sharding.Strategies;
using Xunit;

namespace FastData.Tests.Sync
{
    /// <summary>
    /// 分表名称解析器测试
    /// 
    /// ShardingNameResolver 是所有分表策略的统一命名来源，本测试覆盖时间/哈希/列表/组合
    /// 四类分表名称的解析结果，并验证策略类与解析器对同一配置产出完全一致的表名，
    /// 避免“枚举分表”与“写入分表”命名不一致的问题。
    /// </summary>
    public class ShardingNameResolverTests
    {
        #region 时间分表

        [Theory]
        [InlineData(TimeGranularity.Day, "UserLog_20240115")]
        [InlineData(TimeGranularity.Month, "UserLog_202401")]
        [InlineData(TimeGranularity.Quarter, "UserLog_2024Q1")]
        [InlineData(TimeGranularity.Year, "UserLog_2024")]
        public void ResolveTime_ReturnsExpectedName(TimeGranularity granularity, string expected)
        {
            var config = new ShardingConfig
            {
                BaseTableName = "UserLog",
                ShardingType = ShardingType.Time,
                TimeConfig = new TimeShardingConfig { TimeField = "CreateTime", Granularity = granularity }
            };

            var name = ShardingNameResolver.ResolveTime(config, new DateTime(2024, 1, 15, 10, 30, 0));

            Assert.Equal(expected, name);
        }

        [Fact]
        public void ResolveTime_Week_ReturnsIsoWeekFormat()
        {
            var config = new ShardingConfig
            {
                BaseTableName = "UserLog",
                ShardingType = ShardingType.Time,
                TimeConfig = new TimeShardingConfig { TimeField = "CreateTime", Granularity = TimeGranularity.Week }
            };

            var name = ShardingNameResolver.ResolveTime(config, new DateTime(2024, 1, 15));

            Assert.Equal("UserLog_2024W03", name);
        }

        [Fact]
        public void Resolve_TimeType_UsesConfiguredField()
        {
            var config = new ShardingConfig
            {
                BaseTableName = "UserLog",
                ShardingType = ShardingType.Time,
                TimeConfig = new TimeShardingConfig { TimeField = "CreateTime", Granularity = TimeGranularity.Day }
            };

            var name = ShardingNameResolver.Resolve(
                config,
                field => field == "CreateTime" ? (object)new DateTime(2024, 3, 9) : null);

            Assert.Equal("UserLog_20240309", name);
        }

        [Fact]
        public void Resolve_TimeType_MissingValue_Throws()
        {
            var config = new ShardingConfig
            {
                BaseTableName = "UserLog",
                ShardingType = ShardingType.Time,
                TimeConfig = new TimeShardingConfig { TimeField = "CreateTime", Granularity = TimeGranularity.Day }
            };

            Assert.Throws<InvalidOperationException>(() => ShardingNameResolver.Resolve(config, field => null));
        }

        #endregion

        #region 哈希分表

        [Fact]
        public void ResolveHash_ProducesFourDigitIndexWithinShardCount()
        {
            var config = new ShardingConfig
            {
                BaseTableName = "Order",
                ShardingType = ShardingType.Hash,
                HashConfig = new HashShardingConfig { HashField = "OrderNo", ShardCount = 8, Algorithm = HashAlgorithm.Modulo }
            };

            var name = ShardingNameResolver.ResolveHash(config, "A1001");

            Assert.StartsWith("Order_", name);
            var indexPart = name.Substring("Order_".Length);
            Assert.Equal(4, indexPart.Length);
            var index = int.Parse(indexPart);
            Assert.InRange(index, 0, 7);
        }

        [Fact]
        public void ResolveHash_IsDeterministicForSameInput()
        {
            var config = new ShardingConfig
            {
                BaseTableName = "Order",
                ShardingType = ShardingType.Hash,
                HashConfig = new HashShardingConfig { HashField = "OrderNo", ShardCount = 16, Algorithm = HashAlgorithm.Crc32 }
            };

            Assert.Equal(
                ShardingNameResolver.ResolveHash(config, "A1001"),
                ShardingNameResolver.ResolveHash(config, "A1001"));
        }

        [Fact]
        public void ResolveHash_EmptyValue_Throws()
        {
            var config = new ShardingConfig
            {
                BaseTableName = "Order",
                ShardingType = ShardingType.Hash,
                HashConfig = new HashShardingConfig { HashField = "OrderNo" }
            };

            Assert.Throws<InvalidOperationException>(() => ShardingNameResolver.ResolveHash(config, ""));
        }

        [Fact]
        public void ComputeShardIndex_NonPositiveShardCount_TreatedAsSingleShard()
        {
            var config = new ShardingConfig
            {
                BaseTableName = "Order",
                ShardingType = ShardingType.Hash,
                HashConfig = new HashShardingConfig { HashField = "OrderNo", ShardCount = 0 }
            };

            Assert.Equal(0, ShardingNameResolver.ComputeShardIndex(config, "anything"));
        }

        [Theory]
        [InlineData(HashAlgorithm.Modulo)]
        [InlineData(HashAlgorithm.Consistent)]
        [InlineData(HashAlgorithm.Crc32)]
        public void ComputeShardIndex_AllAlgorithms_ReturnNonNegative(HashAlgorithm algorithm)
        {
            var config = new ShardingConfig
            {
                BaseTableName = "Order",
                ShardingType = ShardingType.Hash,
                HashConfig = new HashShardingConfig { HashField = "OrderNo", ShardCount = 64, Algorithm = algorithm }
            };

            foreach (var value in new[] { "A", "B", "C", "", "12345", "中文键" })
            {
                var index = ShardingNameResolver.ComputeShardIndex(config, value);
                Assert.InRange(index, 0, 63);
            }
        }

        #endregion

        #region 列表分表

        [Fact]
        public void ResolveList_UsesValueMappingWhenPresent()
        {
            var config = new ShardingConfig
            {
                BaseTableName = "Order",
                ShardingType = ShardingType.List,
                ListConfig = new ListShardingConfig
                {
                    ListField = "Status",
                    ValueMapping = new Dictionary<string, string> { { "paid", "P" }, { "unpaid", "U" } }
                }
            };

            Assert.Equal("Order_P", ShardingNameResolver.ResolveList(config, "paid"));
        }

        [Fact]
        public void ResolveList_FallsBackToRawValue()
        {
            var config = new ShardingConfig
            {
                BaseTableName = "Order",
                ShardingType = ShardingType.List,
                ListConfig = new ListShardingConfig { ListField = "Status" }
            };

            Assert.Equal("Order_unknown", ShardingNameResolver.ResolveList(config, "unknown"));
        }

        [Fact]
        public void ResolveList_EmptyValue_Throws()
        {
            var config = new ShardingConfig
            {
                BaseTableName = "Order",
                ShardingType = ShardingType.List,
                ListConfig = new ListShardingConfig { ListField = "Status" }
            };

            Assert.Throws<InvalidOperationException>(() => ShardingNameResolver.ResolveList(config, null));
        }

        #endregion

        #region 组合分表

        [Fact]
        public void ResolveComposite_UseHash_ReturnsHashedName()
        {
            var config = new ShardingConfig
            {
                BaseTableName = "Order",
                ShardingType = ShardingType.Composite,
                CompositeConfig = new CompositeShardingConfig
                {
                    CompositeFields = new List<string> { "Region", "Type" },
                    Separator = "_",
                    UseHash = true,
                    ShardCount = 16
                }
            };

            var name = ShardingNameResolver.ResolveComposite(config, new List<string> { "CN", "VIP" });

            Assert.StartsWith("Order_", name);
            Assert.Equal(4, name.Substring("Order_".Length).Length);
        }

        [Fact]
        public void ResolveComposite_NoHash_JoinsWithSeparator()
        {
            var config = new ShardingConfig
            {
                BaseTableName = "Order",
                ShardingType = ShardingType.Composite,
                CompositeConfig = new CompositeShardingConfig
                {
                    CompositeFields = new List<string> { "Region", "Type" },
                    Separator = "-",
                    UseHash = false
                }
            };

            Assert.Equal("Order_CN-VIP", ShardingNameResolver.ResolveComposite(config, new List<string> { "CN", "VIP" }));
        }

        [Fact]
        public void Resolve_CompositeType_ReadsAllFieldsViaAccessor()
        {
            var values = new Dictionary<string, object> { { "Region", "CN" }, { "Type", "VIP" } };
            var config = new ShardingConfig
            {
                BaseTableName = "Order",
                ShardingType = ShardingType.Composite,
                CompositeConfig = new CompositeShardingConfig
                {
                    CompositeFields = new List<string> { "Region", "Type" },
                    Separator = "-",
                    UseHash = false
                }
            };

            var name = ShardingNameResolver.Resolve(config, field => values.TryGetValue(field, out var v) ? v : null);

            Assert.Equal("Order_CN-VIP", name);
        }

        [Fact]
        public void ResolveComposite_EmptyValues_Throws()
        {
            var config = new ShardingConfig
            {
                BaseTableName = "Order",
                ShardingType = ShardingType.Composite,
                CompositeConfig = new CompositeShardingConfig { CompositeFields = new List<string> { "Region" } }
            };

            Assert.Throws<InvalidOperationException>(() => ShardingNameResolver.ResolveComposite(config, new List<string>()));
        }

        #endregion

        #region 与策略类一致性

        [Fact]
        public void HashStrategy_AndResolver_ProduceSameName()
        {
            var config = new ShardingConfig
            {
                BaseTableName = "Order",
                ShardingType = ShardingType.Hash,
                HashConfig = new HashShardingConfig { HashField = "OrderNo", ShardCount = 8, Algorithm = HashAlgorithm.Modulo }
            };
            var entity = new ResolverOrderEntity { OrderNo = "A1001" };

            var fromStrategy = new HashShardingStrategy().GetTableName(config, entity);
            var fromResolver = ShardingNameResolver.ResolveHash(config, entity.OrderNo);

            Assert.Equal(fromStrategy, fromResolver);
        }

        [Fact]
        public void GetAllTableNames_MatchesResolverIndexFormat()
        {
            var config = new ShardingConfig
            {
                BaseTableName = "Order",
                ShardingType = ShardingType.Hash,
                HashConfig = new HashShardingConfig { HashField = "OrderNo", ShardCount = 4, Algorithm = HashAlgorithm.Modulo }
            };

            var all = new HashShardingStrategy().GetAllTableNames(config);
            var resolved = ShardingNameResolver.ResolveHash(config, "A1001");

            Assert.Equal(new[] { "Order_0000", "Order_0001", "Order_0002", "Order_0003" }, all);
            Assert.Contains(resolved, all);
        }

        #endregion

        [Fact]
        public void Resolve_NullConfig_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => ShardingNameResolver.Resolve(null, field => null));
        }

        [Fact]
        public void Resolve_UnsupportedType_Throws()
        {
            var config = new ShardingConfig { BaseTableName = "Geo", ShardingType = ShardingType.Geo };

            Assert.Throws<NotSupportedException>(() => ShardingNameResolver.Resolve(config, field => "x"));
        }

        private class ResolverOrderEntity
        {
            public string OrderNo { get; set; }
        }
    }
}
