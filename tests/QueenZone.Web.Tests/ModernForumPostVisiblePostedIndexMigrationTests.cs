using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using QueenZone.Data;
using QueenZone.Data.Entities;

namespace QueenZone.Web.Tests;

public sealed class ModernForumPostVisiblePostedIndexMigrationTests
{
    [Fact]
    public void Migration_creates_filtered_posted_at_index_without_a_transaction()
    {
        using var dbContext = CreateDbContext();
        var assembly = dbContext.GetService<IMigrationsAssembly>();
        Assert.True(assembly.Migrations.TryGetValue(
            "20260922150000_AddModernForumPostVisiblePostedIndex",
            out var migrationType));

        var migration = assembly.CreateMigration(migrationType, dbContext.Database.ProviderName!);
        var operation = Assert.Single(migration.UpOperations.OfType<SqlOperation>());

        Assert.True(operation.SuppressTransaction);
        Assert.Contains("CREATE INDEX IX_ModernForumPost_PostedAt_Visible", operation.Sql, StringComparison.Ordinal);
        Assert.Contains("ON dbo.ModernForumPost (PostedAt)", operation.Sql, StringComparison.Ordinal);
        Assert.Contains("WHERE IsHidden = 0", operation.Sql, StringComparison.Ordinal);
        Assert.Contains("NOT EXISTS", operation.Sql, StringComparison.Ordinal);
    }

    [Fact]
    public void Ef_model_mirrors_the_filtered_sql_server_index()
    {
        using var dbContext = CreateDbContext();
        var entity = dbContext.Model.FindEntityType(typeof(ModernForumPostEntity));
        Assert.NotNull(entity);

        var index = Assert.Single(entity.GetIndexes(), candidate =>
            candidate.GetDatabaseName() == "IX_ModernForumPost_PostedAt_Visible");
        Assert.Equal(["PostedAt"], index.Properties.Select(property => property.Name));
        Assert.Equal("[IsHidden] = 0", index.GetFilter());
    }

    private static QueenZoneDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<QueenZoneDbContext>()
            .UseSqlServer("Server=localhost;Database=MigrationSqlShape;Integrated Security=True;TrustServerCertificate=True")
            .Options;
        return new QueenZoneDbContext(options);
    }
}
