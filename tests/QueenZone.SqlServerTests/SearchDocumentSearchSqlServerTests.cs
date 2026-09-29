using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.Extensions.Logging.Abstractions;
using QueenZone.Data;
using QueenZone.Data.Entities;
using QueenZone.Data.Migrations;

namespace QueenZone.SqlServerTests;

/// <summary>
/// Covers the SQL Server path of <see cref="EfSiteSearchService"/> excluding Freddie tributes
/// (#1443). Full-text search is not installed in LocalDB or the CI <c>mssql</c> container, so
/// the test creates <c>dbo.SearchDocument_Search</c> from
/// <see cref="ExcludeTributesFromSiteSearch"/> with only the <c>FREETEXTTABLE</c> sources
/// swapped for a LIKE match. Rank caps, tribute filters, paging, and <c>@TotalRecords</c>
/// run as shipped.
/// </summary>
public sealed class SearchDocumentSearchSqlServerTests : IAsyncLifetime
{
    private const string FreeTextUntyped =
        "FREETEXTTABLE(dbo.SearchDocument, (Title, Body), @Query, @MatchLimit)";

    private const string FreeTextTyped =
        "FREETEXTTABLE(dbo.SearchDocument, (Title, Body), @Query, @TypedMatchLimit)";

    private const string LikeSource = """
        (
            SELECT Id AS [KEY],
                   MAX(CASE WHEN Title LIKE N'%' + @Query + N'%' THEN 2 ELSE 1 END) AS [RANK]
            FROM dbo.SearchDocument
            WHERE Title LIKE N'%' + @Query + N'%'
               OR Body LIKE N'%' + @Query + N'%'
            GROUP BY Id
        ) ft
        """;

    private const string SearchDocumentTableSql = """
        CREATE TABLE dbo.SearchDocument
        (
            Id uniqueidentifier NOT NULL PRIMARY KEY,
            SourceKey nvarchar(200) NOT NULL,
            ContentType nvarchar(50) NOT NULL,
            Title nvarchar(300) NOT NULL,
            Body nvarchar(max) NOT NULL,
            Summary nvarchar(500) NULL,
            Url nvarchar(500) NOT NULL,
            PublishedAt datetimeoffset NULL,
            ImageUrl nvarchar(512) NULL,
            Category nvarchar(200) NULL,
            AuthorDisplayName nvarchar(256) NULL,
            IndexedAt datetimeoffset NOT NULL
        );
        """;

    private readonly string databaseName = $"QueenZoneSiteSearchTests_{Guid.NewGuid():N}";
    private QueenZoneDbContext dbContext = null!;
    private EfSiteSearchService search = null!;

    private string ConnectionString
    {
        get
        {
            var source = Environment.GetEnvironmentVariable("ConnectionStrings__SqlServerTest")
                ?? "Server=(localdb)\\MSSQLLocalDB;Trusted_Connection=True;TrustServerCertificate=True";
            var builder = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(source)
            {
                InitialCatalog = databaseName,
            };
            return builder.ConnectionString;
        }
    }

    public async Task InitializeAsync()
    {
        await using (var schema = new EmptySchemaContext(SchemaOptions()))
        {
            await schema.Database.EnsureCreatedAsync();
            await schema.Database.ExecuteSqlRawAsync(SearchDocumentTableSql);
            var procedureSql = SearchProcedureSql()
                .Replace(FreeTextUntyped, LikeSource, StringComparison.Ordinal)
                .Replace(FreeTextTyped, LikeSource, StringComparison.Ordinal);
            await schema.Database.ExecuteSqlRawAsync(procedureSql);
        }

        dbContext = new QueenZoneDbContext(new DbContextOptionsBuilder<QueenZoneDbContext>()
            .UseSqlServer(ConnectionString).Options);
        search = new EfSiteSearchService(dbContext, NullLogger<EfSiteSearchService>.Instance);
    }

    public async Task DisposeAsync()
    {
        await using var schema = new EmptySchemaContext(SchemaOptions());
        await schema.Database.EnsureDeletedAsync();
        await dbContext.DisposeAsync();
    }

    [Fact]
    public void Migration_procedure_filters_tributes_at_matches_insert()
    {
        var sql = SearchProcedureSql();

        Assert.Contains(FreeTextUntyped, sql, StringComparison.Ordinal);
        Assert.Contains(FreeTextTyped, sql, StringComparison.Ordinal);
        Assert.Equal(2, sql.Split("d.ContentType <> N'tribute'", StringSplitOptions.None).Length - 1);
        Assert.Contains("@RankLimit      INT = 1000", sql, StringComparison.Ordinal);
        Assert.Contains("@TypedRankLimit INT = 5000", sql, StringComparison.Ordinal);
        Assert.Equal(2, sql.Split("OPTION (RECOMPILE)", StringSplitOptions.None).Length - 1);
    }

    [Fact]
    public async Task Search_does_not_return_seeded_tribute_documents()
    {
        dbContext.SearchDocuments.AddRange(
            Document(
                SearchDocumentSourceKey.ForTribute(1012),
                SiteSearchContentType.Tribute,
                "Freddie tribute thought",
                "Thank you for teaching us to be fearless."),
            Document(
                SearchDocumentSourceKey.ForNews(3),
                SiteSearchContentType.News,
                "Freddie news article",
                "Published news about Freddie."));
        await dbContext.SaveChangesAsync();

        var all = await search.SearchAsync("Freddie", null, 1, 20);
        Assert.Equal(1, all.TotalCount);
        var hit = Assert.Single(all.Results);
        Assert.Equal(SiteSearchContentType.News, hit.ContentType);
        Assert.Equal(SearchDocumentSourceKey.ForNews(3), hit.SourceKey);
        Assert.DoesNotContain(all.Results, result => SiteSearchContentType.IsExcludedFromSiteSearch(result.ContentType));
        Assert.DoesNotContain(all.Results, result => SearchDocumentSourceKey.IsTribute(result.SourceKey));

        var typedTribute = await search.SearchAsync("Freddie", SiteSearchContentType.Tribute, 1, 20);
        Assert.Equal(0, typedTribute.TotalCount);
        Assert.Empty(typedTribute.Results);

        var typedNews = await search.SearchAsync("Freddie", SiteSearchContentType.News, 1, 20);
        Assert.Equal(1, typedNews.TotalCount);
        Assert.Equal(SiteSearchContentType.News, Assert.Single(typedNews.Results).ContentType);
    }

    private static SearchDocumentEntity Document(
        string sourceKey,
        string contentType,
        string title,
        string body) =>
        new()
        {
            Id = Guid.NewGuid(),
            SourceKey = sourceKey,
            ContentType = contentType,
            Title = title,
            Body = body,
            Summary = title,
            Url = $"/search-test/{sourceKey}",
            PublishedAt = DateTimeOffset.Parse("2026-09-29T00:00:00Z"),
            IndexedAt = DateTimeOffset.Parse("2026-09-29T00:00:00Z"),
        };

    private static string SearchProcedureSql() =>
        new ExcludeTributesFromSiteSearch().UpOperations
            .OfType<SqlOperation>()
            .Single(operation => operation.Sql.Contains("PROCEDURE dbo.SearchDocument_Search", StringComparison.Ordinal))
            .Sql;

    private DbContextOptions<EmptySchemaContext> SchemaOptions() =>
        new DbContextOptionsBuilder<EmptySchemaContext>().UseSqlServer(ConnectionString).Options;

    private sealed class EmptySchemaContext(DbContextOptions<EmptySchemaContext> options) : DbContext(options);
}
