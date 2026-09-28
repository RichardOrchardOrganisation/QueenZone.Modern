using Microsoft.EntityFrameworkCore;
using QueenZone.Data;

namespace QueenZone.SqlServerTests;

/// <summary>Runs the server-side DateTimeOffset ordering and paging paths in EfArticleRepository.</summary>
public sealed class ArticleOrderingSqlServerTests : IAsyncLifetime
{
    private static readonly Guid AuthorId = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid OldId = Guid.Parse("00000000-0000-0000-0000-000000000011");
    private static readonly Guid TieFirstId = Guid.Parse("00000000-0000-0000-0000-000000000012");
    private static readonly Guid TieSecondId = Guid.Parse("00000000-0000-0000-0000-000000000013");
    private static readonly Guid NewId = Guid.Parse("00000000-0000-0000-0000-000000000014");
    private static readonly Guid DraftId = Guid.Parse("00000000-0000-0000-0000-000000000015");
    private readonly string databaseName = $"QueenZoneArticleOrdering_{Guid.NewGuid():N}";
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
            CREATE TABLE dbo.MemberAccounts (Id uniqueidentifier NOT NULL PRIMARY KEY, DisplayName nvarchar(100) NOT NULL);
            CREATE TABLE dbo.ArticleSubmissions (
                Id uniqueidentifier NOT NULL PRIMARY KEY, AuthorMemberId uniqueidentifier NOT NULL,
                Title nvarchar(300) NOT NULL, Slug nvarchar(300) NOT NULL, Excerpt nvarchar(max) NULL,
                Body nvarchar(max) NOT NULL, WordCount int NOT NULL, CoverImageBlobPath nvarchar(max) NULL,
                Tags nvarchar(max) NULL, Status nvarchar(50) NOT NULL, PublishedAt datetimeoffset NULL);
            CREATE TABLE dbo.EditorialArticles (
                Id uniqueidentifier NOT NULL PRIMARY KEY, LegacyArticleId int NULL, SourceSubmissionId uniqueidentifier NULL,
                Status nvarchar(50) NOT NULL, LiveTitle nvarchar(max) NULL, LiveSlug nvarchar(max) NULL,
                LiveExcerpt nvarchar(max) NULL, LiveImageBlobKey nvarchar(max) NULL, LiveTags nvarchar(max) NULL,
                LivePublishedAt datetimeoffset NULL, LiveAuthorName nvarchar(max) NULL, LiveWordCount int NOT NULL,
                LiveCategory nvarchar(max) NULL, LiveSource nvarchar(max) NULL);
            """);
        dbContext = new QueenZoneDbContext(new DbContextOptionsBuilder<QueenZoneDbContext>()
            .UseSqlServer(ConnectionString).Options);
        await dbContext.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO dbo.MemberAccounts (Id, DisplayName) VALUES ({AuthorId}, {"Article Author"})");
        await InsertArticleAsync(OldId, "old", new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        await InsertArticleAsync(TieFirstId, "tie-first", new DateTimeOffset(2026, 2, 1, 0, 0, 0, TimeSpan.Zero));
        await InsertArticleAsync(TieSecondId, "tie-second", new DateTimeOffset(2026, 2, 1, 0, 0, 0, TimeSpan.Zero));
        await InsertArticleAsync(NewId, "new", new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero));
        await dbContext.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO dbo.ArticleSubmissions
                (Id, AuthorMemberId, Title, Slug, Body, WordCount, Tags, Status, PublishedAt)
            VALUES ({DraftId}, {AuthorId}, {"draft"}, {"draft"}, {"body"}, {1}, {"queen"}, {ArticleSubmissionStatus.Draft},
                {new DateTimeOffset(2026, 4, 1, 0, 0, 0, TimeSpan.Zero)})
            """);
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
    public async Task Published_list_pages_on_sql_server_with_stable_tie_order_and_tag_filter()
    {
        var repository = new EfArticleRepository(dbContext);
        var first = await repository.GetPageAsync(1, 2);
        var second = await repository.GetPageAsync(2, 2);

        Assert.Equal(4, await repository.GetCountAsync());
        Assert.Equal([NewId, TieFirstId], first.Select(article => article.Id));
        Assert.Equal([TieSecondId, OldId], second.Select(article => article.Id));
        Assert.Equal([NewId, TieFirstId],
            (await repository.GetPageAsync(1, 2, "queen")).Select(article => article.Id));
        Assert.All(first, article => Assert.Equal("Article Author", article.AuthorDisplayName));
    }

    [Fact]
    public async Task Adjacent_and_sitemap_order_on_sql_server()
    {
        var repository = new EfArticleRepository(dbContext);
        var (previous, next) = await repository.GetAdjacentAsync(
            new DateTimeOffset(2026, 2, 1, 0, 0, 0, TimeSpan.Zero));
        var sitemap = await repository.GetSitemapEntriesAsync();

        Assert.Equal(OldId, previous?.Id);
        Assert.Equal(NewId, next?.Id);
        Assert.Equal([NewId, TieFirstId, TieSecondId, OldId], sitemap.Select(article => article.Id));
    }

    private async Task InsertArticleAsync(Guid id, string slug, DateTimeOffset publishedAt) =>
        await dbContext.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO dbo.ArticleSubmissions
                (Id, AuthorMemberId, Title, Slug, Body, WordCount, Tags, Status, PublishedAt)
            VALUES ({id}, {AuthorId}, {slug}, {slug}, {"body"}, {1}, {"queen"}, {ArticleSubmissionStatus.Published},
                {publishedAt})
            """);

    private EmptySchemaContext SchemaContext() => new(new DbContextOptionsBuilder<EmptySchemaContext>()
        .UseSqlServer(ConnectionString).Options);

    private sealed class EmptySchemaContext(DbContextOptions<EmptySchemaContext> options) : DbContext(options);
}
