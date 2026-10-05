using Microsoft.EntityFrameworkCore;
using QueenZone.Data;

namespace QueenZone.SqlServerTests;

/// <summary>
/// Runs the production <see cref="EfNewsRepository"/> constructor (legacy <c>NEWS_T</c>
/// COL_LENGTH probes plus archive/count/page/by-id/sitemap/decade SQL) against a scratch
/// SQL Server database (#1672 / #1882). Search stays in <see cref="NewsSearchSqlServerTests"/>.
/// The read-only mirror probe is <c>EfNewsRepositoryLegacyProbeTests</c> in
/// <c>QueenZone.Web.Tests</c>.
/// </summary>
public sealed class NewsRepositorySqlServerTests : IAsyncLifetime
{
    private readonly string databaseName = $"QueenZoneNewsArchiveTests_{Guid.NewGuid():N}";
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
        LegacyNewsSchema.ClearColumnAvailabilityCacheForTests();

        await using (var schema = new EmptySchemaContext(SchemaOptions()))
        {
            await schema.Database.EnsureCreatedAsync();
            await schema.Database.ExecuteSqlRawAsync(LegacyNewsArchiveSchema.CreateTableSql);
            await schema.Database.ExecuteSqlRawAsync("""
                SET IDENTITY_INSERT dbo.NEWS_T ON;
                INSERT INTO dbo.NEWS_T (
                    NEWS_ID, TITLE, EXCERPT, ARTICLE, [DATE], DISPLAY, SOURCE_URL, SLUG,
                    IMAGE_BLOB_KEY, IMAGE_GALLERY_PIC_ID, FORUM_TOPIC_ID)
                VALUES
                    (1, 'Freddie tribute (old copy)', '', '', '2019-01-01', 1, NULL, NULL, NULL, NULL, NULL),
                    (1, 'Freddie tribute concert', 'Wembley 1992', 'Long body about the concert', '2020-04-20', 1,
                        'https://example.com/1', N'freddie-tribute', N'ugc-articles/freddie.webp', 42, 99),
                    (2, 'Box set', 'Liner notes', '', '2024-05-01', 1, NULL, N'box-set', NULL, NULL, NULL),
                    (3, 'Old article', 'From the archive', 'Body from 2008', '2008-03-04', 1, NULL, NULL, NULL, NULL, NULL),
                    (4, 'Draft', '', '', '2025-01-01', 0, NULL, NULL, NULL, NULL, NULL),
                    (5, 'Brian solo album', 'Guitar', '', '2025-02-01', 1, NULL, N'brian-solo', NULL, NULL, NULL),
                    (6, '2010s piece', 'Mid decade', '', '2015-06-01', 1, NULL, NULL, NULL, NULL, NULL);
                SET IDENTITY_INSERT dbo.NEWS_T OFF;
                """);
        }

        dbContext = new QueenZoneDbContext(new DbContextOptionsBuilder<QueenZoneDbContext>()
            .UseSqlServer(ConnectionString).Options);
        repository = new EfNewsRepository(dbContext, new InMemoryNewsSuggestionRepository());
    }

    public async Task DisposeAsync()
    {
        await using var schema = new EmptySchemaContext(SchemaOptions());
        await schema.Database.EnsureDeletedAsync();
        if (dbContext is not null)
        {
            await dbContext.DisposeAsync();
        }

        LegacyNewsSchema.ClearColumnAvailabilityCacheForTests();
    }

    [Fact]
    public void LegacyNewsSchema_probes_all_eight_optional_columns_when_present()
    {
        var columns = LegacyNewsSchema.GetNewsColumnAvailability(ConnectionString);

        Assert.True(columns.HasSourceUrlColumn);
        Assert.True(columns.HasSlugColumn);
        Assert.True(columns.HasCreatedAtColumn);
        Assert.True(columns.HasUpdatedAtColumn);
        Assert.True(columns.HasEditorEmailColumn);
        Assert.True(columns.HasImageBlobKeyColumn);
        Assert.True(columns.HasImageGalleryPicIdColumn);
        Assert.True(columns.HasForumTopicIdColumn);
        Assert.True(LegacyNewsSchema.HasLegacySlugColumn(ConnectionString));
    }

    [Fact]
    public async Task LegacyNewsSchema_reports_missing_optional_columns()
    {
        var missingDatabase = $"QueenZoneNewsMinimal_{Guid.NewGuid():N}";
        var source = Environment.GetEnvironmentVariable("ConnectionStrings__SqlServerTest")
            ?? "Server=(localdb)\\MSSQLLocalDB;Trusted_Connection=True;TrustServerCertificate=True";
        var missingConnection = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(source)
        {
            InitialCatalog = missingDatabase,
        }.ConnectionString;
        var options = new DbContextOptionsBuilder<EmptySchemaContext>().UseSqlServer(missingConnection).Options;

        await using (var schema = new EmptySchemaContext(options))
        {
            await schema.Database.EnsureCreatedAsync();
            await schema.Database.ExecuteSqlRawAsync(LegacyNewsArchiveSchema.CreateMinimalTableSql);
        }

        try
        {
            LegacyNewsSchema.ClearColumnAvailabilityCacheForTests();
            var columns = LegacyNewsSchema.GetNewsColumnAvailability(missingConnection);

            Assert.False(columns.HasSourceUrlColumn);
            Assert.False(columns.HasSlugColumn);
            Assert.False(columns.HasCreatedAtColumn);
            Assert.False(columns.HasUpdatedAtColumn);
            Assert.False(columns.HasEditorEmailColumn);
            Assert.False(columns.HasImageBlobKeyColumn);
            Assert.False(columns.HasImageGalleryPicIdColumn);
            Assert.False(columns.HasForumTopicIdColumn);
            Assert.False(LegacyNewsSchema.HasLegacySlugColumn(missingConnection));
        }
        finally
        {
            await using var schema = new EmptySchemaContext(options);
            await schema.Database.EnsureDeletedAsync();
            LegacyNewsSchema.ClearColumnAvailabilityCacheForTests();
        }
    }

    [Fact]
    public async Task Archive_reads_dedupe_filter_unpublished_page_count_latest_and_sitemap()
    {
        Assert.Equal(5, await repository.GetPublishedCountAsync());

        var latest = await repository.GetLatestAsync(2);
        Assert.Equal([5, 2], latest.Select(item => item.Id));
        Assert.All(latest, item =>
        {
            Assert.True(item.IsPublished);
            Assert.Equal(string.Empty, item.Body);
        });

        var pageOne = await repository.GetArchivePageAsync(1, 2);
        var pageTwo = await repository.GetArchivePageAsync(2, 2);
        var pageThree = await repository.GetArchivePageAsync(3, 2);
        Assert.Equal([5, 2], pageOne.Select(item => item.Id));
        Assert.Equal([1, 6], pageTwo.Select(item => item.Id));
        Assert.Equal([3], pageThree.Select(item => item.Id));
        Assert.Equal("Freddie tribute concert", pageTwo[0].Title);
        Assert.Equal("Wembley 1992", pageTwo[0].Excerpt);
        Assert.Equal(string.Empty, pageTwo[0].Body);

        var clamped = await repository.GetArchivePageAsync(0, 0);
        Assert.Equal([5], clamped.Select(item => item.Id));

        var sitemap = await repository.GetPublishedSitemapEntriesAsync();
        Assert.Equal([5, 2, 1, 6, 3], sitemap.Select(entry => entry.Id));
        Assert.Equal("Freddie tribute concert", sitemap[2].Title);
        Assert.Equal("freddie-tribute", sitemap[2].Slug);
        Assert.DoesNotContain(sitemap, entry => entry.Id == 4);
    }

    [Fact]
    public async Task GetById_and_GetByIds_project_detail_body_image_columns_and_skip_hidden()
    {
        var detail = await repository.GetByIdAsync(1);
        Assert.NotNull(detail);
        Assert.Equal("Freddie tribute concert", detail.Title);
        Assert.Equal("Long body about the concert", detail.Body);
        Assert.Equal("https://example.com/1", detail.SourceUrl);
        Assert.Equal("freddie-tribute", detail.Slug);
        Assert.Equal("ugc-articles/freddie.webp", detail.ImageBlobKey);
        Assert.Equal(42, detail.ImageGalleryPicId);
        Assert.Equal(99, detail.ForumTopicId);
        Assert.True(detail.IsPublished);

        Assert.Null(await repository.GetByIdAsync(4));
        Assert.Null(await repository.GetByIdAsync(404));

        var batched = await repository.GetByIdsAsync([1, 5, 4, 1]);
        Assert.Equal([1, 5], batched.Select(item => item.Id).OrderBy(id => id));
        Assert.Empty(await repository.GetByIdsAsync([]));
    }

    [Fact]
    public async Task Decade_year_and_year_range_filter_counts_and_pages()
    {
        var twenties = new NewsArchiveFilter(2020);
        Assert.Equal(3, await repository.GetPublishedCountAsync(twenties));
        Assert.Equal([5, 2, 1], (await repository.GetArchivePageAsync(1, 10, twenties)).Select(item => item.Id));

        var tens = new NewsArchiveFilter(2010);
        Assert.Equal(1, await repository.GetPublishedCountAsync(tens));
        Assert.Equal([6], (await repository.GetArchivePageAsync(1, 10, tens)).Select(item => item.Id));

        var aughts = new NewsArchiveFilter(2000);
        Assert.Equal(1, await repository.GetPublishedCountAsync(aughts));
        Assert.Equal([3], (await repository.GetArchivePageAsync(1, 10, aughts)).Select(item => item.Id));

        var year2020 = new NewsArchiveFilter(null, 2020);
        Assert.Equal(1, await repository.GetPublishedCountAsync(year2020));
        Assert.Equal([1], (await repository.GetArchivePageAsync(1, 10, year2020)).Select(item => item.Id));

        Assert.Equal(0, await repository.GetPublishedCountAsync(new NewsArchiveFilter(1980)));
        Assert.Empty(await repository.GetArchivePageAsync(1, 10, new NewsArchiveFilter(1980)));

        var range = await repository.GetArchiveYearRangeAsync();
        Assert.Equal(2008, range.MinYear);
        Assert.Equal(2025, range.MaxYear);
    }

    private DbContextOptions<EmptySchemaContext> SchemaOptions() =>
        new DbContextOptionsBuilder<EmptySchemaContext>().UseSqlServer(ConnectionString).Options;

    private sealed class EmptySchemaContext(DbContextOptions<EmptySchemaContext> options) : DbContext(options);
}
