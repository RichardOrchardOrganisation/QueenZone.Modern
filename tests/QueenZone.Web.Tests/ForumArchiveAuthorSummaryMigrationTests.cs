using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using QueenZone.Data;

namespace QueenZone.Web.Tests;

public sealed class ForumArchiveAuthorSummaryMigrationTests
{
    [Fact]
    public void Migration_CreatesBackfillsAndMaintainsSummary()
    {
        var options = new DbContextOptionsBuilder<QueenZoneDbContext>()
            .UseSqlServer("Server=localhost;Database=MigrationSqlShape;Integrated Security=True;TrustServerCertificate=True")
            .Options;
        using var dbContext = new QueenZoneDbContext(options);
        var assembly = dbContext.GetService<IMigrationsAssembly>();
        Assert.True(assembly.Migrations.TryGetValue(
            "20260914070000_AddModernForumArchiveAuthorSummary",
            out var migrationType));

        var migration = assembly.CreateMigration(migrationType, dbContext.Database.ProviderName!);
        var up = migration.UpOperations.OfType<SqlOperation>().ToList();

        Assert.Equal(2, up.Count);
        Assert.All(up, operation => Assert.True(operation.SuppressTransaction));
        Assert.Contains("CREATE TABLE dbo.ModernForumArchiveAuthorSummary", up[0].Sql, StringComparison.Ordinal);
        Assert.Contains("COUNT_BIG(*) AS PostCount", up[0].Sql, StringComparison.Ordinal);
        Assert.Contains("ORDER BY post.PostedAt DESC, post.Id DESC", up[0].Sql, StringComparison.Ordinal);
        Assert.Contains("CREATE OR ALTER TRIGGER", up[1].Sql, StringComparison.Ordinal);
        Assert.Contains("AFTER INSERT, UPDATE, DELETE", up[1].Sql, StringComparison.Ordinal);
        Assert.Contains("INNER JOIN @Affected", up[1].Sql, StringComparison.Ordinal);
        Assert.Contains("WHERE counts.PostCount > 0", up[1].Sql, StringComparison.Ordinal);
    }
}
