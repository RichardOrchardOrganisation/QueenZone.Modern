using Microsoft.EntityFrameworkCore;
using QueenZone.Data;

namespace QueenZone.SqlServerTests;

/// <summary>
/// Runs the production legacy article SQL against the Q_ARTICLE_T types in docs/db-schema.txt.
/// The read-only mirror companion is EfArticlesRepositoryLegacyProbeTests in Web.Tests.
/// </summary>
public sealed class ArticlesRepositorySqlServerTests : IAsyncLifetime
{
    private readonly string databaseName = $"QueenZoneLegacyArticles_{Guid.NewGuid():N}";
    private QueenZoneDbContext dbContext = null!;

    private string ConnectionString
    {
        get
        {
            var source = Environment.GetEnvironmentVariable("ConnectionStrings__SqlServerTest")
                ?? "Server=(localdb)\\MSSQLLocalDB;Trusted_Connection=True;TrustServerCertificate=True";
            return new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(source)
            {
                InitialCatalog = databaseName,
            }.ConnectionString;
        }
    }

    public async Task InitializeAsync()
    {
        await using var schema = SchemaContext();
        await schema.Database.EnsureCreatedAsync();
        await schema.Database.ExecuteSqlRawAsync("""
            CREATE TABLE dbo.Q_ARTICLE_CATEGORY_T (
                Q_ARTICLE_CAT_ID smallint IDENTITY(1,1) NOT NULL PRIMARY KEY,
                ARTICLE_CATEGORY varchar(50) NULL);
            CREATE TABLE dbo.Q_ARTICLE_T (
                Q_ARTICLE_ID smallint IDENTITY(1,1) NOT NULL PRIMARY KEY,
                ARTICLE_NAME varchar(150) NOT NULL, SOURCE varchar(150) NOT NULL,
                ARTICLE_TEXT ntext NOT NULL, Q_ARTICLE_CATEGORY_ID tinyint NOT NULL,
                DISPLAY tinyint NOT NULL, USER_ID int NOT NULL,
                DATE_CREATED smalldatetime NOT NULL, THEDATE varchar(60) NULL);
            INSERT INTO dbo.Q_ARTICLE_CATEGORY_T (ARTICLE_CATEGORY) VALUES ('Interviews'), ('Reviews');
            """);
        dbContext = new QueenZoneDbContext(new DbContextOptionsBuilder<QueenZoneDbContext>()
            .UseSqlServer(ConnectionString).Options);
        await InsertAsync("Older", "  Magazine  ", "Older body", 1, true, new DateTime(2020, 1, 1));
        await InsertAsync("Tied first", "Magazine", "First body", 1, true, new DateTime(2021, 1, 1));
        await InsertAsync("Tied second", "", "<p>Beginning &amp; end</p> " + new string('x', 2100), 2, true, new DateTime(2021, 1, 1));
        await InsertAsync("Draft", "Magazine", "Hidden body", 1, false, new DateTime(2022, 1, 1));
    }

    public async Task DisposeAsync()
    {
        if (dbContext is not null)
        {
            await dbContext.DisposeAsync();
        }
        await using var schema = SchemaContext();
        await schema.Database.EnsureDeletedAsync();
    }

    [Fact]
    public async Task Production_queries_materialize_legacy_types_and_page_visible_articles()
    {
        // The overlay dependency is deliberately absent so the assertions isolate legacy SQL.
        var repository = new EfArticlesRepository(dbContext, null!);

        Assert.Equal(3, await repository.GetPublishedCountAsync());
        Assert.Equal([3, 2], (await repository.GetLatestAsync(2)).Select(item => item.Id));
        var first = await repository.GetArchivePageAsync(1, 2);
        var second = await repository.GetArchivePageAsync(2, 2);
        Assert.Equal([3, 2], first.Select(item => item.Id));
        Assert.Equal([1], second.Select(item => item.Id));
        Assert.Equal("Reviews", first[0].CategoryName);
        Assert.Null(first[0].Source);
        Assert.Equal("Magazine", second[0].Source);
        Assert.Equal(string.Empty, first[0].Body);
        Assert.StartsWith("Beginning & end", first[0].Excerpt, StringComparison.Ordinal);

        var detail = await repository.GetByIdAsync(3);
        Assert.NotNull(detail);
        Assert.True(detail.Body.Length > 2000);
        Assert.Null(await repository.GetByIdAsync(4));
        Assert.Null(await repository.GetByIdAsync(999));
        Assert.Equal([3, 2, 1],
            (await repository.GetPublishedSitemapEntriesAsync()).Select(entry => entry.Id));
    }

    private async Task InsertAsync(string title, string source, string body, byte categoryId, bool visible, DateTime createdAt) =>
        await dbContext.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO dbo.Q_ARTICLE_T
                (ARTICLE_NAME, SOURCE, ARTICLE_TEXT, Q_ARTICLE_CATEGORY_ID, DISPLAY, USER_ID, DATE_CREATED)
            VALUES ({title}, {source}, {body}, {categoryId}, {(visible ? (byte)1 : (byte)0)}, {1}, {createdAt})
            """);

    private EmptySchemaContext SchemaContext() => new(new DbContextOptionsBuilder<EmptySchemaContext>()
        .UseSqlServer(ConnectionString).Options);

    private sealed class EmptySchemaContext(DbContextOptions<EmptySchemaContext> options) : DbContext(options);
}
