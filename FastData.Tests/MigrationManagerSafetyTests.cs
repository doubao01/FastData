using System;
using FastData.Migrations;
using Xunit;

namespace FastData.Tests
{
    public class MigrationManagerSafetyTests
    {
        [Fact]
        public void Migrate_RejectsUnsupportedExecutionBeforeCallingUp()
        {
            var migration = new CountingMigration();
            var manager = CreateManager(migration);

            Assert.Throws<NotSupportedException>(() => manager.Migrate());
            Assert.Equal(0, migration.UpCalls);
            Assert.Equal(0, migration.DownCalls);
        }

        [Fact]
        public void RepeatedMigrate_DoesNotReplayCallbacks()
        {
            var migration = new CountingMigration();
            var manager = CreateManager(migration);

            for (var attemptIndex = 0; attemptIndex < 3; attemptIndex++)
                Assert.Throws<NotSupportedException>(() => manager.Migrate());

            Assert.Equal(0, migration.UpCalls);
        }

        [Fact]
        public void Rollback_RejectsUnsupportedExecutionBeforeCallingDown()
        {
            var migration = new CountingMigration();
            var manager = CreateManager(migration);

            Assert.Throws<NotSupportedException>(() => manager.Rollback("000"));
            Assert.Equal(0, migration.DownCalls);
        }

        [Fact]
        public void MigrationHistory_FailsInsteadOfReturningFabricatedRecords()
        {
            var manager = CreateManager(new CountingMigration());

            Assert.Throws<NotSupportedException>(() => manager.GetMigrationHistory());
        }

        [Fact]
        public void Constructor_PreservesNullConnectionValidation()
        {
            Assert.Throws<ArgumentNullException>(() => new MigrationManager(null));
        }

        [Fact]
        public void AddMigration_PreservesNullMigrationValidation()
        {
            var manager = new MigrationManager("Data Source=:memory:");

            Assert.Throws<ArgumentNullException>(() => manager.AddMigration(null));
        }

        private static MigrationManager CreateManager(IMigration migration)
        {
            var manager = new MigrationManager("Data Source=:memory:");
            manager.AddMigration(migration);
            return manager;
        }

        private sealed class CountingMigration : IMigration
        {
            public string Version => "001";
            public string Description => "Counting migration";
            public int UpCalls { get; private set; }
            public int DownCalls { get; private set; }

            public void Up()
            {
                UpCalls++;
            }

            public void Down()
            {
                DownCalls++;
            }
        }
    }
}
