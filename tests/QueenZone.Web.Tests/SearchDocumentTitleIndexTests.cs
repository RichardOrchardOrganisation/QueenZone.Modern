using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using QueenZone.Data;
using QueenZone.Data.Entities;

namespace QueenZone.Web.Tests;

public sealed class SearchDocumentTitleIndexTests
{
    [Fact]
    public void Title_has_no_explicit_case_sensitive_collation()
    {
        using var dbContext = CreateSqlServerModel();
        var title = dbContext.GetService<IDesignTimeModel>().Model
            .FindEntityType(typeof(SearchDocumentEntity))
            ?.FindProperty(nameof(SearchDocumentEntity.Title));

        Assert.NotNull(title);
        Assert.Null(title.GetCollation());
        Assert.DoesNotContain(
            "UseCollation(",
            File.ReadAllText(RepoPaths.Combine(
                "src",
                "QueenZone.Data",
                "Configurations",
                "Search",
                "SearchDocumentEntityConfiguration.cs")),
            StringComparison.Ordinal);
    }

    [Fact]
    public void Ef_model_has_covering_title_index()
    {
        using var dbContext = CreateSqlServerModel();
        var entity = dbContext.GetService<IDesignTimeModel>().Model
            .FindEntityType(typeof(SearchDocumentEntity));
        Assert.NotNull(entity);

        var index = Assert.Single(entity.GetIndexes(), candidate =>
            candidate.GetDatabaseName() == "IX_SearchDocument_Title");
        Assert.Equal(["Title"], index.Properties.Select(property => property.Name));
        Assert.Equal(["ContentType", "Url"], index.GetIncludeProperties());
    }

    [Fact]
    public void Migration_creates_title_index_online_only_on_azure_sql()
    {
        using var dbContext = CreateSqlServerModel();
        var assembly = dbContext.GetService<IMigrationsAssembly>();
        Assert.True(assembly.Migrations.TryGetValue(
            "20261007134011_AddSearchDocumentTitleIndex",
            out var migrationType));

        var migration = assembly.CreateMigration(migrationType, dbContext.Database.ProviderName!);
        var operation = Assert.Single(migration.UpOperations.OfType<SqlOperation>());

        Assert.Contains("IX_SearchDocument_Title", operation.Sql, StringComparison.Ordinal);
        Assert.Contains("ON dbo.SearchDocument (Title)", operation.Sql, StringComparison.Ordinal);
        Assert.Contains("INCLUDE (ContentType, Url)", operation.Sql, StringComparison.Ordinal);
        Assert.Contains("SERVERPROPERTY('EngineEdition') = 5", operation.Sql, StringComparison.Ordinal);
        Assert.Contains("WITH (ONLINE = ON)", operation.Sql, StringComparison.Ordinal);
        Assert.Contains("sp_executesql", operation.Sql, StringComparison.Ordinal);
        Assert.DoesNotContain("ALTER TABLE", operation.Sql, StringComparison.Ordinal);
        Assert.DoesNotContain("CommandTimeout", operation.Sql, StringComparison.Ordinal);

        var onlineIndex = operation.Sql.IndexOf("WITH (ONLINE = ON)", StringComparison.Ordinal);
        var editionCheck = operation.Sql.LastIndexOf("SERVERPROPERTY('EngineEdition') = 5", onlineIndex);
        Assert.True(editionCheck >= 0, "ONLINE = ON must sit under the Azure SQL EngineEdition = 5 branch.");

        var down = Assert.Single(migration.DownOperations.OfType<SqlOperation>());
        Assert.Contains("DROP INDEX IX_SearchDocument_Title", down.Sql, StringComparison.Ordinal);
        Assert.DoesNotContain("WITH (ONLINE = ON)", down.Sql, StringComparison.Ordinal);
    }

    private static QueenZoneDbContext CreateSqlServerModel()
    {
        var options = new DbContextOptionsBuilder<QueenZoneDbContext>()
            .UseSqlServer("Server=localhost;Database=SearchDocumentTitleIndex;Integrated Security=True;TrustServerCertificate=True")
            .Options;
        return new QueenZoneDbContext(options);
    }
}
