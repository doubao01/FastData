using System;
using System.Collections.Generic;
using FastData.Sharding;
using FastData.Sharding.Strategies;
using Xunit;

namespace FastData.Tests
{
    public class ShardingConfigurationTests
    {
        [Theory]
        [InlineData(TimeGranularity.Day, "logs_20260115")]
        [InlineData(TimeGranularity.Week, "logs_2026W03")]
        [InlineData(TimeGranularity.Month, "logs_202601")]
        [InlineData(TimeGranularity.Quarter, "logs_2026Q1")]
        [InlineData(TimeGranularity.Year, "logs_2026")]
        public void TimeSuffix_DefaultFormatRemainsCompatible(TimeGranularity granularity, string expected)
        {
            var config = CreateTimeConfig();
            config.TimeConfig.Granularity = granularity;

            Assert.Equal(expected, ShardingNameResolver.ResolveTime(config, new DateTime(2026, 1, 15)));
        }

        [Fact]
        public void TimeSuffix_CustomFormatIsUsedForRoutingAndEnumeration()
        {
            var config = CreateTimeConfig();
            config.SuffixFormat = "{0:yyyy_MM}";

            Assert.Equal("logs_2026_01", ShardingNameResolver.ResolveTime(config, new DateTime(2026, 1, 15)));
            Assert.Equal(new[] { "logs_2026_01", "logs_2026_02" }, new TimeShardingStrategy().GetAllTableNames(config));
        }

        [Fact]
        public void HashSuffix_CustomFormatIsUsedForRoutingAndEnumeration()
        {
            var config = new ShardingConfig
            {
                BaseTableName = "logs",
                ShardingType = ShardingType.Hash,
                SuffixFormat = "shard{0:D2}",
                HashConfig = new HashShardingConfig { HashField = "Id", ShardCount = 4 }
            };
            var shardIndex = ShardingNameResolver.ComputeShardIndex(config, "42");

            Assert.Equal(string.Format("logs_shard{0:D2}", shardIndex), ShardingNameResolver.ResolveHash(config, "42"));
            Assert.Equal(new[] { "logs_shard00", "logs_shard01", "logs_shard02", "logs_shard03" }, new HashShardingStrategy().GetAllTableNames(config));
        }

        [Fact]
        public void ListSuffix_CustomFormatUsesMappedValuesForRoutingAndEnumeration()
        {
            var config = new ShardingConfig
            {
                BaseTableName = "logs",
                ShardingType = ShardingType.List,
                SuffixFormat = "{0}_archive",
                ListConfig = new ListShardingConfig
                {
                    ListField = "Status",
                    ValueMapping = new Dictionary<string, string> { { "Pending", "pending" } }
                }
            };

            Assert.Equal("logs_pending_archive", ShardingNameResolver.ResolveList(config, "Pending"));
            Assert.Equal("logs_unknown_archive", ShardingNameResolver.ResolveList(config, "unknown"));
            Assert.Equal(new[] { "logs_pending_archive" }, new ListShardingStrategy().GetAllTableNames(config));
        }

        [Fact]
        public void CompositeSuffix_CustomFormatMatchesEnumeratedHashTables()
        {
            var config = new ShardingConfig
            {
                BaseTableName = "logs",
                ShardingType = ShardingType.Composite,
                SuffixFormat = "shard{0:D2}",
                CompositeConfig = new CompositeShardingConfig { ShardCount = 4 }
            };

            Assert.Equal(new[] { "logs_shard00", "logs_shard01", "logs_shard02", "logs_shard03" }, new CompositeShardingStrategy().GetAllTableNames(config));
            Assert.Contains(ShardingNameResolver.ResolveComposite(config, new[] { "42", "eu" }), new CompositeShardingStrategy().GetAllTableNames(config));
        }

        [Fact]
        public void InvalidSuffixFormatFailsInsteadOfBeingIgnored()
        {
            var config = CreateTimeConfig();
            config.SuffixFormat = "{2}";

            Assert.Throws<FormatException>(() => ShardingNameResolver.ResolveTime(config, new DateTime(2026, 1, 15)));
        }

        private static ShardingConfig CreateTimeConfig()
        {
            return new ShardingConfig
            {
                BaseTableName = "logs",
                ShardingType = ShardingType.Time,
                TimeConfig = new TimeShardingConfig
                {
                    TimeField = "CreatedAt",
                    Granularity = TimeGranularity.Month,
                    StartTime = new DateTime(2026, 1, 1),
                    EndTime = new DateTime(2026, 2, 28)
                }
            };
        }
    }
}
