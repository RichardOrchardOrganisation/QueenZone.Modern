using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.Extensions.Logging.Abstractions;
using QueenZone.Data;
using QueenZone.Data.Entities;
using QueenZone.Data.Migrations;

namespace QueenZone.SqlServerTests;

/// <summary>
/// Covers the SQL Server path of <see cref="EfSiteSearchService"/> excluding Freddie tributes
/// and paging through <c>#Page</c> (#1443). Full-text search is not installed in LocalDB or
/// the CI <c>mssql</c> container, so the test creates <c>dbo.SearchDocument_Search</c> from
/// <see cref="ExcludeTributesFromSiteSearch"/> with only the <c>FREETEXTTABLE</c> sources
/// swapped for a LIKE match. Rank caps, tribute filters, paging, and <c>@TotalRecords</c>
/// run as shipped.
/// </summary>
public sealed class SearchDocumentSearchSqlServerTests : IAsyncLifetime
{
    private const string FreeTextUntyped =
        "FREETEXTTABLE(dbo.SearchDocument, (Title, Body), @Query, @MatchLimit) ft";

    private const string FreeTextTyped =
        "FREETEXTTABLE(dbo.SearchDocument, (Title, Body), @Query, @TypedMatchLimit) ft";

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

    /// <summary>
    /// Previous tail: join the full <c>#Matches</c> window to <c>SearchDocument</c> before
    /// sort/OFFSET, and count through that join. Used to prove the <c>#Page</c> rewrite keeps
    /// typed and untyped results, order, and totals identical.
    /// </summary>
    private const string PreviousTailProcedureSql = """
        CREATE OR ALTER PROCEDURE dbo.SearchDocument_Search
            @Query          NVARCHAR(500),
            @ContentType    NVARCHAR(50) = NULL,
            @Offset         INT,
            @PageSize       INT,
            @TotalRecords   INT OUTPUT,
            @RankLimit      INT = 1000,
            @TypedRankLimit INT = 5000
        AS
        BEGIN
            SET NOCOUNT ON;

            DECLARE @MatchLimit INT = CASE
                WHEN @RankLimit IS NULL OR @RankLimit < 1 THEN 1000
                WHEN @RankLimit > 1000 THEN 1000
                ELSE @RankLimit
            END;

            DECLARE @TypedMatchLimit INT = CASE
                WHEN @TypedRankLimit IS NULL OR @TypedRankLimit < 1 THEN 5000
                WHEN @TypedRankLimit > 5000 THEN 5000
                ELSE @TypedRankLimit
            END;

            CREATE TABLE #Matches
            (
                DocumentId UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
                SearchRank INT NOT NULL
            );

            IF @ContentType IS NULL
            BEGIN
                INSERT INTO #Matches (DocumentId, SearchRank)
                SELECT ft.[KEY], ft.[RANK]
                FROM   FREETEXTTABLE(dbo.SearchDocument, (Title, Body), @Query, @MatchLimit) ft
                INNER JOIN dbo.SearchDocument d ON d.Id = ft.[KEY]
                WHERE  d.ContentType <> N'tribute'
                OPTION (RECOMPILE);
            END
            ELSE
            BEGIN
                INSERT INTO #Matches (DocumentId, SearchRank)
                SELECT TOP (@MatchLimit)
                       d.Id,
                       ft.[RANK]
                FROM   FREETEXTTABLE(dbo.SearchDocument, (Title, Body), @Query, @TypedMatchLimit) ft
                INNER JOIN dbo.SearchDocument d ON d.Id = ft.[KEY]
                WHERE  d.ContentType = @ContentType
                  AND  d.ContentType <> N'tribute'
                ORDER BY ft.[RANK] DESC, d.PublishedAt DESC, d.Id DESC
                OPTION (RECOMPILE);
            END

            SELECT
                d.ContentType,
                d.SourceKey,
                d.Title,
                d.Summary,
                d.Url,
                d.PublishedAt,
                d.ImageUrl,
                d.Category,
                d.AuthorDisplayName
            FROM   dbo.SearchDocument d
            INNER JOIN #Matches fm ON fm.DocumentId = d.Id
            WHERE  @ContentType IS NULL OR d.ContentType = @ContentType
            ORDER BY fm.SearchRank DESC, d.PublishedAt DESC, d.Id DESC
            OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;

            SELECT @TotalRecords = COUNT(*)
            FROM   dbo.SearchDocument d
            INNER JOIN #Matches fm ON fm.DocumentId = d.Id
            WHERE  @ContentType IS NULL OR d.ContentType = @ContentType;
        END;
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
            await schema.Database.ExecuteSqlRawAsync(ApplyLikeStandIn(SearchProcedureSql()));
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
        Assert.Equal(2, sql.Split(SiteSearchExclusion.SqlIsSearchable("d"), StringSplitOptions.None).Length - 1);
        Assert.Contains("@RankLimit      INT = 1000", sql, StringComparison.Ordinal);
        Assert.Contains("@TypedRankLimit INT = 5000", sql, StringComparison.Ordinal);
        Assert.Equal(2, sql.Split("OPTION (RECOMPILE)", StringSplitOptions.None).Length - 1);
        Assert.Contains("CREATE TABLE #Page", sql, StringComparison.Ordinal);
        Assert.Contains("SELECT @TotalRecords = COUNT(*)", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("@ContentType IS NULL OR", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("INNER JOIN #Matches", sql, StringComparison.Ordinal);
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

    [Fact]
    public async Task Search_excludes_alias_and_mismatched_tribute_rows()
    {
        dbContext.SearchDocuments.AddRange(
            Document(
                "freddie-tribute:7",
                SiteSearchContentType.FreddieTribute,
                "Freddie alias tribute",
                "Thank you Freddie."),
            Document(
                SearchDocumentSourceKey.ForTribute(8),
                SiteSearchContentType.News,
                "Freddie mismatched key",
                "News type with a tribute source key."),
            Document(
                "news:44",
                SiteSearchContentType.Tribute,
                "Freddie mismatched type",
                "Tribute type with a news source key."),
            Document(
                SearchDocumentSourceKey.ForNews(3),
                SiteSearchContentType.News,
                "Freddie news article",
                "Published news about Freddie."));
        await dbContext.SaveChangesAsync();

        var all = await search.SearchAsync("Freddie", null, 1, 20);
        Assert.Equal(1, all.TotalCount);
        Assert.Equal(SearchDocumentSourceKey.ForNews(3), Assert.Single(all.Results).SourceKey);

        var typedAlias = await search.SearchAsync("Freddie", SiteSearchContentType.FreddieTribute, 1, 20);
        Assert.Equal(0, typedAlias.TotalCount);
        Assert.Empty(typedAlias.Results);
    }

    [Fact]
    public async Task Page_rewrite_keeps_typed_and_untyped_results_order_and_totals()
    {
        await SeedOrderingDocumentsAsync();

        var rewrittenUntyped = await SnapshotAsync("Live", null);
        var rewrittenTyped = await SnapshotAsync("Live", SiteSearchContentType.News);

        await dbContext.Database.ExecuteSqlRawAsync(ApplyLikeStandIn(PreviousTailProcedureSql));

        var previousUntyped = await SnapshotAsync("Live", null);
        var previousTyped = await SnapshotAsync("Live", SiteSearchContentType.News);

        Assert.Equal(previousUntyped.TotalCount, rewrittenUntyped.TotalCount);
        Assert.Equal(previousUntyped.SourceKeys, rewrittenUntyped.SourceKeys);
        Assert.Equal(previousUntyped.Results, rewrittenUntyped.Results);
        Assert.Equal(previousTyped.TotalCount, rewrittenTyped.TotalCount);
        Assert.Equal(previousTyped.SourceKeys, rewrittenTyped.SourceKeys);
        Assert.Equal(previousTyped.Results, rewrittenTyped.Results);
        Assert.Equal(
            [
                "news:20",
                "forum-thread:5",
                "forum-thread:4",
                "news:10",
                "discography:1",
                "news:25",
            ],
            rewrittenUntyped.SourceKeys);
        Assert.Equal(6, rewrittenUntyped.TotalCount);
        Assert.Equal(
            ["news:20", "news:10", "news:25"],
            rewrittenTyped.SourceKeys);
        Assert.Equal(3, rewrittenTyped.TotalCount);
        Assert.All(rewrittenUntyped.SourceKeys, key => Assert.False(SearchDocumentSourceKey.IsTribute(key)));
        Assert.All(rewrittenTyped.Results, result => Assert.Equal(SiteSearchContentType.News, result.ContentType));
    }

    [Fact]
    public async Task Beyond_max_page_returns_empty_with_true_capped_total()
    {
        await SeedOrderingDocumentsAsync();

        var first = await search.SearchAsync("Live", null, 1, 2);
        var deep = await search.SearchAsync("Live", null, SiteSearchLimits.MaxPage + 1, 2);

        Assert.Equal(6, first.TotalCount);
        Assert.Equal(2, first.Results.Count);
        Assert.Equal(["news:20", "forum-thread:5"], first.Results.Select(result => result.SourceKey));
        Assert.Equal(6, deep.TotalCount);
        Assert.Empty(deep.Results);
        Assert.Equal(11, deep.Page);
        Assert.Equal(2, deep.PageSize);
    }

    private async Task SeedOrderingDocumentsAsync()
    {
        dbContext.SearchDocuments.AddRange(
            Document(
                "news:20",
                SiteSearchContentType.News,
                "Live Aid night",
                "Wembley coverage",
                DateTimeOffset.Parse("2026-09-20T00:00:00Z"),
                Guid.Parse("00000000-0000-0000-0000-000000000001")),
            Document(
                "news:10",
                SiteSearchContentType.News,
                "Live from Wembley",
                "Earlier news",
                DateTimeOffset.Parse("2026-09-10T00:00:00Z"),
                Guid.Parse("00000000-0000-0000-0000-000000000002")),
            Document(
                "news:25",
                SiteSearchContentType.News,
                "Press notes",
                "Body-only Live mention",
                DateTimeOffset.Parse("2026-09-25T00:00:00Z"),
                Guid.Parse("00000000-0000-0000-0000-000000000003")),
            Document(
                "forum-thread:4",
                SiteSearchContentType.Forum,
                "Live Aid memories",
                "Forum body",
                DateTimeOffset.Parse("2026-09-15T00:00:00Z"),
                Guid.Parse("00000000-0000-0000-0000-000000000004")),
            Document(
                "forum-thread:5",
                SiteSearchContentType.Forum,
                "Live Aid memories",
                "Forum body",
                DateTimeOffset.Parse("2026-09-15T00:00:00Z"),
                Guid.Parse("00000000-0000-0000-0000-000000000005")),
            Document(
                "discography:1",
                SiteSearchContentType.Discography,
                "Live Killers",
                "Album notes",
                DateTimeOffset.Parse("2026-08-01T00:00:00Z"),
                Guid.Parse("00000000-0000-0000-0000-000000000006")),
            Document(
                SearchDocumentSourceKey.ForTribute(9),
                SiteSearchContentType.Tribute,
                "Live tribute",
                "Thank you Freddie",
                DateTimeOffset.Parse("2026-09-28T00:00:00Z"),
                Guid.Parse("00000000-0000-0000-0000-000000000009")));
        await dbContext.SaveChangesAsync();
    }

    private async Task<SearchSnapshot> SnapshotAsync(string query, string? contentType)
    {
        var page = await search.SearchAsync(query, contentType, 1, 20);
        return new SearchSnapshot(
            page.TotalCount,
            page.Results.Select(result => result.SourceKey).ToArray(),
            page.Results.ToArray());
    }

    private static SearchDocumentEntity Document(
        string sourceKey,
        string contentType,
        string title,
        string body,
        DateTimeOffset? publishedAt = null,
        Guid? id = null) =>
        new()
        {
            Id = id ?? Guid.NewGuid(),
            SourceKey = sourceKey,
            ContentType = contentType,
            Title = title,
            Body = body,
            Summary = title,
            Url = $"/search-test/{sourceKey}",
            PublishedAt = publishedAt ?? DateTimeOffset.Parse("2026-09-29T00:00:00Z"),
            IndexedAt = DateTimeOffset.Parse("2026-09-29T00:00:00Z"),
        };

    private static string SearchProcedureSql() =>
        new ExcludeTributesFromSiteSearch().UpOperations
            .OfType<SqlOperation>()
            .Single(operation => operation.Sql.Contains("PROCEDURE dbo.SearchDocument_Search", StringComparison.Ordinal))
            .Sql;

    private static string ApplyLikeStandIn(string procedureSql) =>
        procedureSql
            .Replace(FreeTextUntyped, LikeSource, StringComparison.Ordinal)
            .Replace(FreeTextTyped, LikeSource, StringComparison.Ordinal);

    private DbContextOptions<EmptySchemaContext> SchemaOptions() =>
        new DbContextOptionsBuilder<EmptySchemaContext>().UseSqlServer(ConnectionString).Options;

    private sealed class EmptySchemaContext(DbContextOptions<EmptySchemaContext> options) : DbContext(options);

    private sealed record SearchSnapshot(
        int TotalCount,
        IReadOnlyList<string> SourceKeys,
        IReadOnlyList<SiteSearchResult> Results);
}
