using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using QueenZone.Data;

namespace QueenZone.Web.Tests;

public sealed class ForumArchiveAuthorIndexMigrationTests
{
    [Fact]
    public void Migration_ReplacesAuthorIndexWithCompletePagingOrder()
    {
        var options = new DbContextOptionsBuilder<QueenZoneDbContext>()
            .UseSqlServer("Server=localhost;Database=MigrationSqlShape;Integrated Security=True;TrustServerCertificate=True")
            .Options;
        using var dbContext = new QueenZoneDbContext(options);
        var assembly = dbContext.GetService<IMigrationsAssembly>();
        Assert.True(assembly.Migrations.TryGetValue(
            "20260926090600_OrderModernForumPostArchiveAuthorIndex",
            out var migrationType));

        var migration = assembly.CreateMigration(migrationType, dbContext.Database.ProviderName!);
        var up = Assert.Single(migration.UpOperations.OfType<SqlOperation>());

        Assert.True(up.SuppressTransaction);
        Assert.Contains("AuthorLegacyUserId, PostedAt DESC, Id DESC", up.Sql, StringComparison.Ordinal);
        Assert.Contains("DROP_EXISTING = ON", up.Sql, StringComparison.Ordinal);
        Assert.Contains("AuthorLegacyUserId IS NOT NULL", up.Sql, StringComparison.Ordinal);
    }
}
