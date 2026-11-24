using System;
using FastData.Sharding;
using Xunit;

namespace FastData.Tests
{
    [Collection("Sharding")]
    public class ShardingTableCreationTests : IDisposable
    {
        public ShardingTableCreationTests()
        {
            ShardingManager.Clear();
        }

        public void Dispose()
        {
            ShardingManager.Clear();
        }

        [Fact]
        public void AutomaticTableCreation_IsDisabledByDefault()
        {
            Assert.False(new ShardingConfig().AutoCreateTable);
        }

        [Fact]
        public void CreateTable_WithConfigurationFailsBeforeDatabaseAccess()
        {
            var exception = Assert.Throws<NotSupportedException>(() => ShardingManager.CreateTable(CreateConfig(true), "guarded_creation"));

            Assert.Contains("docs/SHARDING.md", exception.Message);
        }

        [Fact]
        public void CreateTable_UnregisteredEntityFailsExplicitly()
        {
            Assert.Throws<NotSupportedException>(() => ShardingManager.CreateTable<CreationEntity>("guarded_creation"));
        }

        [Fact]
        public void GenerateCreateTableSql_RejectsPlaceholderSql()
        {
            Assert.Throws<NotSupportedException>(() => ShardingManager.GenerateCreateTableSql(CreateConfig(true), "guarded_creation"));
        }

        [Fact]
        public void AutomaticTableCreation_WhenDisabledDoesNotResolveEntityOrDatabase()
        {
            var config = CreateConfig(false);
            ShardingManager.Configure<CreationEntity>(config);

            ShardingManager.AutoCreateTable<CreationEntity>(null);

            Assert.Same(config, ShardingManager.GetConfig<CreationEntity>());
        }

        [Fact]
        public void AutomaticTableCreation_WhenEnabledFailsBeforeResolvingEntity()
        {
            ShardingManager.Configure<CreationEntity>(CreateConfig(true));

            var exception = Assert.Throws<NotSupportedException>(() => ShardingManager.AutoCreateTable<CreationEntity>(null));

            Assert.Contains("docs/SHARDING.md", exception.Message);
        }

        [Fact]
        public void AutomaticTableCreation_UnregisteredEntityRemainsANoOp()
        {
            ShardingManager.AutoCreateTable<CreationEntity>(null);
        }

        [Theory]
        [InlineData(ShardingType.Time)]
        [InlineData(ShardingType.Hash)]
        [InlineData(ShardingType.List)]
        [InlineData(ShardingType.Composite)]
        [InlineData(ShardingType.QueryFrequency)]
        public void BuiltInStrategy_TableCreationFailsExplicitly(ShardingType shardingType)
        {
            var strategy = ShardingManager.GetStrategy(shardingType);

            Assert.Throws<NotSupportedException>(() => strategy.CreateTable(CreateConfig(true), "guarded_creation"));
        }

        private static ShardingConfig CreateConfig(bool enabled)
        {
            return new ShardingConfig
            {
                DatabaseKey = "UnregisteredCreationDatabase",
                BaseTableName = "guarded_creation",
                ShardingType = ShardingType.Time,
                AutoCreateTable = enabled
            };
        }

        private sealed class CreationEntity
        {
            public int Id { get; set; }
        }
    }
}
