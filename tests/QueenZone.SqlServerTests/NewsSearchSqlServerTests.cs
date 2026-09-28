using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using QueenZone.Data;
using QueenZone.Data.Migrations;

namespace QueenZone.SqlServerTests;

/// <summary>
/// Covers the SQL Server search path of <see cref="EfNewsRepository.SearchAsync"/> (#1672).
/// Full-text search is not installed in LocalDB or the CI <c>mssql</c> container, so the test
/// creates <c>dbo.NEWS_T_SearchPublished</c> from the <see cref="AddNewsFullTextSearch"/> migration
/// with only the <c>FREETEXTTABLE</c> source swapped for a LIKE match. Everything else in the
/// procedure (dedup, <c>DISPLAY</c> filter, ordering, paging, <c>@TotalRecords</c>) runs as shipped.
/// Real full-text matching is covered by the <c>EfNewsFullTextSearchLiveProbeTests</c> mirror probe.
/// </summary>
public sealed class NewsSearchSqlServerTests : IAsyncLifetime
{
    private const string FreeTextSource = "FREETEXTTABLE(dbo.NEWS_T, (TITLE, EXCERPT, ARTICLE), @Query) ft";

    // Title matches rank above excerpt/article matches, like FREETEXTTABLE's RANK would.
    private const string LikeSource = """
        (
            SELECT NEWS_ID AS [KEY],
                   MAX(CASE WHEN TITLE LIKE N'%' + @Query + N'%' THEN 2 ELSE 1 END) AS [RANK]
            FROM dbo.NEWS_T
            WHERE TITLE LIKE N'%' + @Query + N'%'
               OR EXCERPT LIKE N'%' + @Query + N'%'
               OR ARTICLE LIKE N'%' + @Query + N'%'
            GROUP BY NEWS_ID
        ) ft
        """;

    // NEWS_T column types from the queenzone_legacy_sync mirror. No primary key, because legacy
    // data has duplicate NEWS_ID rows that the procedure deduplicates.
    private const string NewsTableSql = """
        CREATE TABLE dbo.NEWS_T
        (
            NEWS_ID int IDENTITY(1,1) NOT NULL,
            TITLE varchar(150) NULL,
            EXCERPT varchar(800) NULL,
            ARTICLE varchar(max) NULL,
            [DATE] smalldatetime NOT NULL,
            DISPLAY tinyint NOT NULL,
            SOURCE_URL varchar(500) NULL,
            SLUG nvarchar(200) NULL
        );
        """;

    private readonly string databaseName = $"QueenZoneNewsSearchTests_{Guid.NewGuid():N}";
    private QueenZoneDbContext dbContext = null!;
    private EfNewsRepository repository = null!;

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
            await schema.Database.ExecuteSqlRawAsync(NewsTableSql);
            await schema.Database.ExecuteSqlRawAsync(
                SearchProcedureSql().Replace(FreeTextSource, LikeSource, StringComparison.Ordinal));
            await schema.Database.ExecuteSqlRawAsync("""
                SET IDENTITY_INSERT dbo.NEWS_T ON;
                INSERT INTO dbo.NEWS_T (NEWS_ID, TITLE, EXCERPT, ARTICLE, [DATE], DISPLAY, SOURCE_URL, SLUG) VALUES
                    (1, 'Freddie tribute concert', 'Wembley 1992', 'Long body', '2020-04-20', 1, 'https://example.com/1', N'freddie-tribute'),
                    (1, 'Freddie tribute (old copy)', '', '', '2019-01-01', 1, NULL, NULL),
                    (2, 'Box set', 'Liner notes about Freddie', '', '2024-05-01', 1, NULL, N'box-set'),
                    (3, 'Tour dates', '', 'Freddie appears in the article only', '2019-06-01', 1, NULL, NULL),
                    (4, 'Freddie draft', '', '', '2025-01-01', 0, NULL, NULL),
                    (5, 'Brian solo album', 'Guitar', 'Nothing relevant', '2025-02-01', 1, NULL, NULL);
                SET IDENTITY_INSERT dbo.NEWS_T OFF;
                """);
        }

        dbContext = new QueenZoneDbContext(new DbContextOptionsBuilder<QueenZoneDbContext>()
            .UseSqlServer(ConnectionString).Options);
        repository = new EfNewsRepository(dbContext, "", "", "", "", "");
    }

    public async Task DisposeAsync()
    {
        await using var schema = new EmptySchemaContext(SchemaOptions());
        await schema.Database.EnsureDeletedAsync();
        await dbContext.DisposeAsync();
    }

    [Fact]
    public void Migration_procedure_searches_title_excerpt_and_article_with_freetext()
    {
        var sql = SearchProcedureSql();

        Assert.Equal(2, sql.Split(FreeTextSource).Length - 1);
        Assert.Contains("@TotalRecords INT OUTPUT", sql, StringComparison.Ordinal);
        Assert.Contains("WHERE  n.DISPLAY = 1", sql, StringComparison.Ordinal);
        Assert.Contains("ORDER BY SearchRank DESC, PublishedAt DESC, Id DESC", sql, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Search_runs_procedure_dedupes_filters_unpublished_ranks_and_pages()
    {
        var first = await repository.SearchAsync("  freddie ", 1, 2);
        Assert.Equal(3, first.TotalCount);
        Assert.Equal((1, 2), (first.Page, first.PageSize));
        Assert.Equal([1, 2], first.Items.Select(item => item.Id));

        var top = first.Items[0];
        Assert.Equal(("Freddie tribute concert", "Wembley 1992", string.Empty), (top.Title, top.Excerpt, top.Body));
        Assert.Equal(new DateTime(2020, 4, 20), top.PublishedAt);
        Assert.Equal(("https://example.com/1", "freddie-tribute"), (top.SourceUrl, top.Slug));
        Assert.True(top.IsPublished);

        var second = await repository.SearchAsync("freddie", 2, 2);
        Assert.Equal(3, second.TotalCount);
        Assert.Equal([3], second.Items.Select(item => item.Id));

        var clamped = await repository.SearchAsync("freddie", 0, 0);
        Assert.Equal((1, 1, 3), (clamped.Page, clamped.PageSize, clamped.TotalCount));
        Assert.Equal([1], clamped.Items.Select(item => item.Id));

        var none = await repository.SearchAsync("zeppelin", 1, 10);
        Assert.Equal(0, none.TotalCount);
        Assert.Empty(none.Items);
    }

    private static string SearchProcedureSql() =>
        new AddNewsFullTextSearch().UpOperations
            .OfType<SqlOperation>()
            .Single(operation => operation.Sql.Contains("PROCEDURE dbo.NEWS_T_SearchPublished", StringComparison.Ordinal))
            .Sql;

    private DbContextOptions<EmptySchemaContext> SchemaOptions() =>
        new DbContextOptionsBuilder<EmptySchemaContext>().UseSqlServer(ConnectionString).Options;

    // Creates and drops the scratch database; NEWS_T and the procedure come from raw DDL.
    private sealed class EmptySchemaContext(DbContextOptions<EmptySchemaContext> options) : DbContext(options);
}
