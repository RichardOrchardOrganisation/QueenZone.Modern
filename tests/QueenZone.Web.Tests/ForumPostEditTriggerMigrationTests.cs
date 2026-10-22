using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using QueenZone.Data;
using QueenZone.Data.Migrations;

namespace QueenZone.Web.Tests;

public sealed class ForumPostEditTriggerMigrationTests
{
    [Fact]
    public void Migration_is_discovered_and_down_restores_the_original_trigger()
    {
        var options = new DbContextOptionsBuilder<QueenZoneDbContext>()
            .UseSqlServer("Server=localhost;Database=MigrationSqlShape;Integrated Security=True;TrustServerCertificate=True")
            .Options;
        using var context = new QueenZoneDbContext(options);
        var assembly = context.GetService<IMigrationsAssembly>();
        Assert.True(assembly.Migrations.ContainsKey("20261022023000_SkipArchiveAuthorSummaryForPostEdits"));
        var migration = new SkipArchiveAuthorSummaryForPostEdits();
        var up = Assert.Single(migration.UpOperations.OfType<SqlOperation>());
        var down = Assert.Single(migration.DownOperations.OfType<SqlOperation>());
        Assert.True(up.SuppressTransaction);
        Assert.True(down.SuppressTransaction);
        Assert.Contains("UPDATE(AuthorLegacyUserId)", up.Sql, StringComparison.Ordinal);
        Assert.Contains("UPDATE(AuthorDisplayName)", up.Sql, StringComparison.Ordinal);
        Assert.Contains("UPDATE(AuthorJoinedAt)", up.Sql, StringComparison.Ordinal);
        Assert.Contains("UPDATE(IsHidden)", up.Sql, StringComparison.Ordinal);
        Assert.Contains("UPDATE(PostedAt)", up.Sql, StringComparison.Ordinal);
        Assert.Contains("UPDATE(Id)", up.Sql, StringComparison.Ordinal);
        Assert.DoesNotContain("UPDATE(", down.Sql, StringComparison.Ordinal);
        var original = new AddModernForumArchiveAuthorSummary().UpOperations.OfType<SqlOperation>().Last().Sql;
        Assert.Equal(Normalize(original), Normalize(down.Sql));
    }

    private static string Normalize(string sql) =>
        string.Join(' ', sql.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}
