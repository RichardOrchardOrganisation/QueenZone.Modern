using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using QueenZone.Data;
using QueenZone.Data.Migrations;

namespace QueenZone.Web.Tests;

public sealed class ForumPostEditStatisticsMigrationTests
{
    [Fact]
    public void SqlServer_discovers_migration_and_runs_sampling_outside_transaction()
    {
        var options = new DbContextOptionsBuilder<QueenZoneDbContext>()
            .UseSqlServer("Server=localhost;Database=MigrationDiscovery;Integrated Security=True")
            .Options;
        using var context = new QueenZoneDbContext(options);
        var assembly = context.GetService<IMigrationsAssembly>();
        var migrationType = assembly.Migrations["20261022130000_PrecreateForumPostUpdatedAtStatistics"];
        var migration = assembly.CreateMigration(migrationType, "Microsoft.EntityFrameworkCore.SqlServer");
        Assert.True(Assert.IsType<SqlOperation>(Assert.Single(migration.UpOperations)).SuppressTransaction);
        Assert.Empty(migration.DownOperations);
    }

    [Theory]
    [InlineData("Microsoft.EntityFrameworkCore.Sqlite")]
    [InlineData("Microsoft.EntityFrameworkCore.InMemory")]
    public void Other_providers_do_not_receive_SQL_Server_catalog_operations(string provider)
    {
        var migration = new PrecreateForumPostUpdatedAtStatistics { ActiveProvider = provider };
        Assert.Empty(migration.UpOperations);
        Assert.Empty(migration.DownOperations);
    }
}
