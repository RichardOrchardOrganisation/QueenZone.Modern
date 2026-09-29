using Microsoft.EntityFrameworkCore;
using QueenZone.Data;

namespace QueenZone.SqlServerTests;

/// <summary>
/// Runs the production <see cref="EfLinksRepository"/> SQL against scratch
/// <c>Q_LINK_CAT_T</c> / <c>QUEEN_FEATURED_SITE_T</c> / <c>QueenLinkChecks</c>
/// (#1672 / #1889). Column types match the 2026-09-29
/// <c>queenzone_legacy_sync</c> dump: <c>smallint</c> category ids versus
/// <c>tinyint</c> site category ids, <c>varchar</c> legacy strings, and
/// <c>nvarchar</c> check rows. The read-only mirror probe is
/// <c>EfLinksRepositoryLegacyProbeTests</c> in <c>QueenZone.Web.Tests</c>.
/// </summary>
public sealed class LinksRepositorySqlServerTests : IAsyncLifetime
{
    private readonly string databaseName = $"QueenZoneLinksTests_{Guid.NewGuid():N}";
    private QueenZoneDbContext dbContext = null!;
    private EfLinksRepository repository = null!;

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
            await schema.Database.ExecuteSqlRawAsync(LegacyLinksSchema.CreateLegacyTablesSql);
            await schema.Database.ExecuteSqlRawAsync(LegacyLinksSchema.CreateLinkChecksSql);
        }

        dbContext = new QueenZoneDbContext(new DbContextOptionsBuilder<QueenZoneDbContext>()
            .UseSqlServer(ConnectionString).Options);
        repository = new EfLinksRepository(dbContext);

        await dbContext.Database.ExecuteSqlRawAsync(
            """
            SET IDENTITY_INSERT dbo.Q_LINK_CAT_T ON;
            INSERT INTO dbo.Q_LINK_CAT_T (Q_LINK_CAT_ID, CAT_NAME) VALUES
                (1, 'Official Queen Sites'),
                (2, 'Fan Websites'),
                (8, 'Gone Sites'),
                (10, 'Zebra Last');
            SET IDENTITY_INSERT dbo.Q_LINK_CAT_T OFF;

            SET IDENTITY_INSERT dbo.QUEEN_FEATURED_SITE_T ON;
            INSERT INTO dbo.QUEEN_FEATURED_SITE_T
                (QUEEN_FEATURED_SITE_ID, QUEEN_FEATURED_SITE_TITLE, QUEEN_FEATURED_SITE_URL,
                 SITE_COMMENT, Q_LINK_CAT_ID, FEATURED_SITE, DISPLAY)
            VALUES
                (3, '  Official Queen Site  ', 'https://www.queenonline.com/', '  Official.  ', 1, 1, 1),
                (7, 'International Fan Club', 'https://www.queeninternational.com/', NULL, 1, 0, 1),
                (8, 'Hidden', 'https://hidden.example.test/', NULL, 1, 1, 0),
                (9, 'Dead Official', 'https://dead.example.test/', NULL, 1, 1, 1),
                (10, '   ', 'https://blank-title.example.test/', NULL, 1, 1, 1),
                (11, 'Blank Url', '   ', NULL, 1, 1, 1),
                (12, 'Mailto', 'mailto:fan@example.com', NULL, 1, 1, 1),
                (13, 'Brian May', 'www.brianmay.com', 'Official site', 1, 1, 1),
                (20, 'Fan Site', 'https://fan.example.test/', '  Community  ', 2, 0, 1),
                (30, 'Zebra Site', 'https://zebra.example.test/', NULL, 10, 1, 1),
                (40, 'Only Dead', 'https://gone.example.test/', NULL, 8, 1, 1);
            SET IDENTITY_INSERT dbo.QUEEN_FEATURED_SITE_T OFF;

            INSERT INTO dbo.QueenLinkChecks
                (QueenFeaturedSiteId, Url, LastCheckedAtUtc, IsAvailable, IsConfirmedDead,
                 ConsecutiveFailureCount, LastStatusCode, LastError)
            VALUES
                (3, N'https://www.queenonline.com/', '2026-08-03T02:37:03.8790742', 1, 0, 0, 200, NULL),
                (9, N'https://dead.example.test/', '2026-08-03T02:37:03.8790742', 0, 1, 3, 404, N'gone'),
                (13, N'https://www.brianmay.com/', '2026-08-03T02:37:03.8790742', 1, 0, 0, 301, NULL),
                (40, N'https://gone.example.test/', '2026-08-03T02:37:03.8790742', 0, 1, 5, 404, N'dead');
            """);
    }

    public async Task DisposeAsync()
    {
        await using var schema = new EmptySchemaContext(SchemaOptions());
        await schema.Database.EnsureDeletedAsync();
        await dbContext.DisposeAsync();
    }

    [Fact]
    public async Task Scratch_schema_matches_mirror_column_types()
    {
        Assert.Equal("smallint", await TypeNameAsync("dbo.Q_LINK_CAT_T", "Q_LINK_CAT_ID"));
        Assert.Equal("tinyint", await TypeNameAsync("dbo.QUEEN_FEATURED_SITE_T", "Q_LINK_CAT_ID"));
        Assert.Equal("tinyint", await TypeNameAsync("dbo.QUEEN_FEATURED_SITE_T", "FEATURED_SITE"));
        Assert.Equal("tinyint", await TypeNameAsync("dbo.QUEEN_FEATURED_SITE_T", "DISPLAY"));
        Assert.Equal("varchar", await TypeNameAsync("dbo.Q_LINK_CAT_T", "CAT_NAME"));
        Assert.Equal("varchar", await TypeNameAsync("dbo.QUEEN_FEATURED_SITE_T", "QUEEN_FEATURED_SITE_TITLE"));
        Assert.Equal("nvarchar", await TypeNameAsync("dbo.QueenLinkChecks", "Url"));
        Assert.Equal(
            "SQL_Latin1_General_CP1_CI_AS",
            await CollationAsync("dbo.Q_LINK_CAT_T", "CAT_NAME"));
        Assert.Equal(
            "SQL_Latin1_General_CP1_CI_AS",
            await CollationAsync("dbo.QueenLinkChecks", "Url"));

        var indexes = await dbContext.Database
            .SqlQueryRaw<string>(
                """
                SELECT name AS [Value]
                FROM sys.indexes
                WHERE object_id = OBJECT_ID(N'dbo.QueenLinkChecks')
                ORDER BY name
                """)
            .ToListAsync();
        Assert.Equal(
            ["IX_QueenLinkChecks_IsConfirmedDead", "IX_QueenLinkChecks_LastCheckedAtUtc", "PK_QueenLinkChecks"],
            indexes);
    }

    [Fact]
    public async Task GetCategoriesWithLinksAsync_filters_hidden_dead_blank_and_orders()
    {
        var categories = await repository.GetCategoriesWithLinksAsync();

        Assert.Equal(
            ["Fan Websites", "Official Queen Sites", "Zebra Last"],
            categories.Select(category => category.Name));
        Assert.Equal([2, 1, 10], categories.Select(category => category.Id));
        Assert.DoesNotContain(categories, category => category.Name == "Gone Sites");

        var official = categories[1];
        Assert.Equal(
            [13, 3, 7],
            official.Links.Select(link => link.Id));
        Assert.Equal(
            ["Brian May", "Official Queen Site", "International Fan Club"],
            official.Links.Select(link => link.Title));
        Assert.Equal("https://www.brianmay.com/", official.Links[0].Url);
        Assert.True(official.Links[0].IsFeatured);
        Assert.True(official.Links[1].IsFeatured);
        Assert.False(official.Links[2].IsFeatured);
        Assert.Equal("Official.", official.Links[1].Comment);
        Assert.DoesNotContain(official.Links, link => link.Title is "Hidden" or "Dead Official" or "Mailto");

        Assert.Equal("Community", Assert.Single(categories[0].Links).Comment);
        Assert.Equal(30, Assert.Single(categories[2].Links).Id);
    }

    [Fact]
    public async Task GetLinksForValidationAsync_includes_dead_links_and_materializes_checks()
    {
        var items = await repository.GetLinksForValidationAsync();

        Assert.Equal([3, 7, 9, 12, 13, 20, 30, 40], items.Select(item => item.Link.Id));
        Assert.DoesNotContain(items, item => item.Link.Id is 8 or 10 or 11);

        var official = items.Single(item => item.Link.Id == 3);
        Assert.Equal("Official Queen Site", official.Link.Title);
        Assert.False(official.IsConfirmedDead);
        Assert.Equal(0, official.ConsecutiveFailureCount);

        var uncheckedSite = items.Single(item => item.Link.Id == 7);
        Assert.False(uncheckedSite.IsConfirmedDead);
        Assert.Equal(0, uncheckedSite.ConsecutiveFailureCount);

        var dead = items.Single(item => item.Link.Id == 9);
        Assert.True(dead.IsConfirmedDead);
        Assert.Equal(3, dead.ConsecutiveFailureCount);
        Assert.Equal("https://dead.example.test/", dead.Link.Url);

        var mailto = items.Single(item => item.Link.Id == 12);
        Assert.Equal("mailto:fan@example.com", mailto.Link.Url);

        var gone = items.Single(item => item.Link.Id == 40);
        Assert.True(gone.IsConfirmedDead);
        Assert.Equal(5, gone.ConsecutiveFailureCount);
        Assert.Equal(8, gone.Link.CategoryId);
    }

    [Fact]
    public async Task GetCategoriesWithLinksAsync_without_QueenLinkChecks_includes_dead_links()
    {
        await dbContext.Database.ExecuteSqlRawAsync("DROP TABLE dbo.QueenLinkChecks");

        var categories = await repository.GetCategoriesWithLinksAsync();

        Assert.Equal(
            ["Fan Websites", "Gone Sites", "Official Queen Sites", "Zebra Last"],
            categories.Select(category => category.Name));
        var official = categories.Single(category => category.Id == 1);
        Assert.Contains(official.Links, link => link.Id == 9 && link.Title == "Dead Official");
        Assert.Equal(40, Assert.Single(categories.Single(category => category.Id == 8).Links).Id);
    }

    [Fact]
    public async Task GetLinksForValidationAsync_without_QueenLinkChecks_defaults_check_columns()
    {
        await dbContext.Database.ExecuteSqlRawAsync("DROP TABLE dbo.QueenLinkChecks");

        var items = await repository.GetLinksForValidationAsync();

        Assert.Equal([3, 7, 9, 12, 13, 20, 30, 40], items.Select(item => item.Link.Id));
        Assert.All(items, item =>
        {
            Assert.False(item.IsConfirmedDead);
            Assert.Equal(0, item.ConsecutiveFailureCount);
        });
    }

    [Fact]
    public async Task UpsertCheckResultsAsync_inserts_and_hides_confirmed_dead_from_public()
    {
        await repository.UpsertCheckResultsAsync([]);
        Assert.Equal(4, await dbContext.QueenLinkChecks.CountAsync());

        var checkedAt = new DateTime(2026, 9, 29, 12, 0, 0, DateTimeKind.Utc);
        await repository.UpsertCheckResultsAsync(
        [
            new QueenLinkCheckUpdate(20, "https://fan.example.test/", checkedAt, false, true, 4, 404, "down"),
            new QueenLinkCheckUpdate(3, "https://www.queenonline.com/", checkedAt, false, false, 1, 500, "flaky"),
        ]);

        var fan = await dbContext.QueenLinkChecks.SingleAsync(row => row.QueenFeaturedSiteId == 20);
        Assert.True(fan.IsConfirmedDead);
        Assert.Equal(4, fan.ConsecutiveFailureCount);
        Assert.Equal("down", fan.LastError);
        Assert.Equal(checkedAt, fan.LastCheckedAtUtc);

        var official = await dbContext.QueenLinkChecks.SingleAsync(row => row.QueenFeaturedSiteId == 3);
        Assert.False(official.IsConfirmedDead);
        Assert.False(official.IsAvailable);
        Assert.Equal(1, official.ConsecutiveFailureCount);
        Assert.Equal(500, official.LastStatusCode);

        var categories = await repository.GetCategoriesWithLinksAsync();
        Assert.Equal(["Official Queen Sites", "Zebra Last"], categories.Select(category => category.Name));
        Assert.Contains(categories[0].Links, link => link.Id == 3);

        var validation = await repository.GetLinksForValidationAsync();
        var fanValidation = validation.Single(item => item.Link.Id == 20);
        Assert.True(fanValidation.IsConfirmedDead);
        Assert.Equal(4, fanValidation.ConsecutiveFailureCount);
    }

    private async Task<string> TypeNameAsync(string table, string column) =>
        Assert.Single(await dbContext.Database
            .SqlQueryRaw<string>(
                """
                SELECT TYPE_NAME(c.system_type_id) AS [Value]
                FROM sys.columns AS c
                WHERE c.object_id = OBJECT_ID({0}) AND c.name = {1}
                """,
                table, column)
            .ToListAsync());

    private async Task<string> CollationAsync(string table, string column) =>
        Assert.Single(await dbContext.Database
            .SqlQueryRaw<string>(
                """
                SELECT c.collation_name AS [Value]
                FROM sys.columns AS c
                WHERE c.object_id = OBJECT_ID({0}) AND c.name = {1}
                """,
                table, column)
            .ToListAsync());

    private DbContextOptions<EmptySchemaContext> SchemaOptions() =>
        new DbContextOptionsBuilder<EmptySchemaContext>().UseSqlServer(ConnectionString).Options;

    // Creates and drops the scratch database; tables come from LegacyLinksSchema.
    private sealed class EmptySchemaContext(DbContextOptions<EmptySchemaContext> options) : DbContext(options);
}
