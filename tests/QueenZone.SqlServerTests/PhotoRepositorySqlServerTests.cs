using Microsoft.EntityFrameworkCore;
using QueenZone.Data;

namespace QueenZone.SqlServerTests;

/// <summary>
/// Runs the production <see cref="EfPhotoRepository"/> SQL (<see cref="PhotoSqlQueries.CreateProduction"/>)
/// against a scratch SQL Server database (#1672). The SQLite suite only runs the fixture SQL, so the
/// SQL Server shapes (OFFSET paging, <c>PIC_LONGEST_SIDE</c> size filters, seek-based neighbours and
/// the submitted-by <c>COALESCE</c>) had no automated coverage. The picture tables come from
/// <see cref="LegacyPhotoSchema"/>; <c>USERS_T</c>, <c>MemberAccounts</c> and <c>PhotoSubmissions</c>
/// are created with only the columns the submitted-by lookup reads, typed as on the mirror. The
/// read-only mirror probe is <c>EfPhotoRepositoryLegacyProbeTests</c> in <c>QueenZone.Web.Tests</c>.
/// </summary>
public sealed class PhotoRepositorySqlServerTests : IAsyncLifetime
{
    private const string SubmittedBySchemaSql = """
        CREATE TABLE dbo.USERS_T
        (
            USER_ID int NOT NULL PRIMARY KEY,
            USERNAME char(40) NULL
        );

        CREATE TABLE dbo.MemberAccounts
        (
            Id uniqueidentifier NOT NULL PRIMARY KEY,
            DisplayName nvarchar(100) NOT NULL,
            LinkedLegacyUserId int NULL
        );

        CREATE TABLE dbo.PhotoSubmissions
        (
            Id uniqueidentifier NOT NULL PRIMARY KEY,
            SubmitterMemberId uniqueidentifier NOT NULL,
            ReviewedAt datetimeoffset NULL,
            PromotedPicId int NULL
        );
        """;

    private static readonly DateTime BaseTime = new(2026, 8, 17, 10, 0, 0);

    private readonly string databaseName = $"QueenZonePhotoTests_{Guid.NewGuid():N}";
    private QueenZoneDbContext dbContext = null!;
    private EfPhotoRepository repository = null!;

    // Brian May (cat 9), newest first: tieHigh, tieLow, portrait, noSize; hidden is not displayed.
    private int tieLow;
    private int tieHigh;
    private int portrait;
    private int hidden;
    private int noSize;
    private int roger;

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
            await schema.Database.ExecuteSqlRawAsync(LegacyPhotoSchema.CreateTablesSql);
            await schema.Database.ExecuteSqlRawAsync(SubmittedBySchemaSql);
        }

        dbContext = new QueenZoneDbContext(new DbContextOptionsBuilder<QueenZoneDbContext>()
            .UseSqlServer(ConnectionString).Options);
        repository = new EfPhotoRepository(dbContext);

        tieLow = await InsertPhotoAsync("Red Special", 9, BaseTime, 2000, 1000, userId: 5);
        tieHigh = await InsertPhotoAsync("Wembley", 9, BaseTime, 800, 600);
        portrait = await InsertPhotoAsync("Tall", 9, BaseTime.AddDays(-1), 1080, 2400, userId: 6);
        hidden = await InsertPhotoAsync("Hidden", 9, BaseTime.AddDays(1), 3000, 2000, display: 0);
        noSize = await InsertPhotoAsync("Unsized", 9, BaseTime.AddDays(-2), 0, 0, thumbUrl: null);
        roger = await InsertPhotoAsync("Drums", 11, BaseTime, 640, 480);

        var linkedMember = Guid.NewGuid();
        var submitter = Guid.NewGuid();
        await dbContext.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO dbo.USERS_T (USER_ID, USERNAME) VALUES (5, 'legacy_five'), (6, 'oldtimer');
            INSERT INTO dbo.MemberAccounts (Id, DisplayName, LinkedLegacyUserId)
            VALUES ({0}, N' Linked Member ', 5), ({1}, N'Submitter', NULL);
            INSERT INTO dbo.PhotoSubmissions (Id, SubmitterMemberId, ReviewedAt, PromotedPicId)
            VALUES (NEWID(), {1}, SYSDATETIMEOFFSET(), {2});
            """,
            linkedMember, submitter, tieHigh);
    }

    public async Task DisposeAsync()
    {
        await using var schema = new EmptySchemaContext(SchemaOptions());
        await schema.Database.EnsureDeletedAsync();
        await dbContext.DisposeAsync();
    }

    [Fact]
    public async Task Categories_count_visible_photos_and_pick_newest_cover()
    {
        var categories = await repository.GetCategoriesAsync();

        Assert.Equal(
            [
                new PhotoCategory(9, "Brian May", "brian-may", 4, PhotoImageUrl.Build(Thumb("Wembley"))),
                new PhotoCategory(11, "Roger Taylor", "roger-taylor", 1, PhotoImageUrl.Build(Thumb("Drums"))),
            ],
            categories);
        Assert.Equal(11, (await repository.GetCategoryBySlugAsync("ROGER-TAYLOR"))?.CatId);
        Assert.Null(await repository.GetCategoryBySlugAsync("freddie-mercury"));
    }

    [Fact]
    public async Task Category_page_pages_visible_photos_and_applies_size_filters()
    {
        var first = await repository.GetCategoryPageAsync(9, 1, 2);
        Assert.Equal("Brian May", first.CategoryName);
        Assert.Equal(4, first.TotalCount);
        Assert.Equal([tieHigh, tieLow], first.Items.Select(item => item.PicId));

        var red = first.Items[1];
        Assert.Equal((2000, 1000, 150, 100), (red.PictureWidth, red.PictureHeight, red.ThumbWidth, red.ThumbHeight));
        Assert.Equal(("brian-may", 2026), (red.CategorySlug, red.Year));

        var second = await repository.GetCategoryPageAsync(9, 2, 2);
        Assert.Equal([portrait, noSize], second.Items.Select(item => item.PicId));
        // NULL Thumb_URL and zero dimensions come back through the ISNULL/CAST projections.
        var unsized = second.Items[1];
        Assert.Equal(PhotoImageUrl.Build(string.Empty), unsized.ThumbnailUrl);
        Assert.False(unsized.HasPictureDimensions);

        await AssertFilterAsync(PhotoSizePreset.Desktop, tieLow);
        await AssertFilterAsync(PhotoSizePreset.Phone, portrait);
        await AssertFilterAsync(PhotoSizePreset.Large, tieLow, portrait);
        await AssertFilterAsync(PhotoSizePreset.Hd, tieLow, portrait);
        await AssertFilterAsync(PhotoSizePreset.Landscape, tieHigh, tieLow);
        await AssertFilterAsync(PhotoSizePreset.Portrait, portrait);

        var all = await repository.GetCategoryAllAsync(9);
        Assert.Equal([tieHigh, tieLow, portrait, noSize], all.Select(item => item.PicId));
    }

    [Fact]
    public async Task Detail_navigation_resolves_neighbours_totals_and_submitted_by()
    {
        var middle = await repository.GetDetailNavigationAsync(9, tieLow);
        Assert.NotNull(middle);
        Assert.Equal((1, 4, tieHigh, portrait), (middle.Index, middle.Count, middle.PreviousPicId, middle.NextPicId));
        Assert.True(middle.MatchedRequestedFilter);
        Assert.Equal("Linked Member", middle.Photo.SubmittedByDisplayName);
        Assert.Equal(new PhotoNeighborMedia(Url("Wembley"), 800, 600), middle.PreviousMedia);
        Assert.Equal(new PhotoNeighborMedia(Url("Tall"), 1080, 2400), middle.NextMedia);

        var newest = await repository.GetDetailNavigationAsync(9, tieHigh);
        Assert.Equal((0, (int?)null, tieLow), (newest!.Index, newest.PreviousPicId, newest.NextPicId));
        Assert.Null(newest.PreviousMedia);
        Assert.Equal("Submitter", newest.Photo.SubmittedByDisplayName);

        var legacyUser = await repository.GetDetailNavigationAsync(9, portrait);
        Assert.Equal("oldtimer", legacyUser!.Photo.SubmittedByDisplayName);
        var oldest = await repository.GetDetailNavigationAsync(9, noSize);
        Assert.Equal((3, portrait, (int?)null), (oldest!.Index, oldest.PreviousPicId, oldest.NextPicId));
        Assert.Null(oldest.Photo.SubmittedByDisplayName);

        // A matching filter narrows totals and neighbours to the filtered set.
        var large = await repository.GetDetailNavigationAsync(9, portrait, new PhotoListFilter(PhotoSizePreset.Large));
        Assert.Equal((1, 2, tieLow, (int?)null, true),
            (large!.Index, large.Count, large.PreviousPicId, large.NextPicId, large.MatchedRequestedFilter));

        // A non-matching filter falls back to unfiltered navigation.
        var fallback = await repository.GetDetailNavigationAsync(9, tieHigh, new PhotoListFilter(PhotoSizePreset.Large));
        Assert.Equal((4, false), (fallback!.Count, fallback.MatchedRequestedFilter));

        Assert.Null(await repository.GetDetailNavigationAsync(9, hidden));
        Assert.Null(await repository.GetDetailNavigationAsync(11, tieLow));
        Assert.Null(await repository.GetDetailNavigationAsync(9, hidden, new PhotoListFilter(PhotoSizePreset.Large)));
    }

    [Fact]
    public async Task Id_lookups_random_picks_and_sitemap_skip_hidden_photos()
    {
        var byIds = await repository.GetPublishedByIdsAsync(9, [portrait, hidden, tieLow, portrait, roger]);
        Assert.Equal([portrait, tieLow], byIds.Select(item => item.PicId));
        Assert.Empty(await repository.GetPublishedByIdsAsync(9, []));

        int[] visible = [tieLow, tieHigh, portrait, noSize];
        var picked = await repository.PickRandomPublishedPhotoIdsAsync(9, 3);
        Assert.InRange(picked.Count, 1, 3);
        Assert.Equal(picked.Count, picked.Distinct().Count());
        Assert.All(picked, id => Assert.Contains(id, visible));

        var random = await repository.GetRandomPublishedInCategoryAsync(11, 5);
        Assert.Equal([roger], random.Select(item => item.PicId));
        Assert.Empty(await repository.PickRandomPublishedPhotoIdsAsync(404, 3));

        var sitemap = await repository.GetPublishedSitemapCategoriesAsync();
        Assert.Equal(["brian-may", "roger-taylor"], sitemap.Select(category => category.Slug));
        Assert.Equal([tieHigh, tieLow, portrait, noSize], sitemap[0].Photos.Select(photo => photo.PicId));
        Assert.Equal(BaseTime, sitemap[0].Photos[0].DateTime);
    }

    private async Task AssertFilterAsync(PhotoSizePreset preset, params int[] expected)
    {
        var page = await repository.GetCategoryPageAsync(9, 1, 10, new PhotoListFilter(preset));
        Assert.Equal(expected.Length, page.TotalCount);
        Assert.Equal(expected, page.Items.Select(item => item.PicId));
    }

    private async Task<int> InsertPhotoAsync(
        string title,
        int catId,
        DateTime dateTime,
        short width,
        short height,
        int? userId = null,
        int display = 1,
        string? thumbUrl = "")
    {
        var ids = await dbContext.Database.SqlQueryRaw<int>(
            """
            INSERT INTO dbo.PIC_FILES_T
                (Name, Cat_ID, Date_time, Url, Thumb_URL, t_height, t_width, user_id, DISPLAY, PIC_HEIGHT, PIC_WIDTH)
            OUTPUT INSERTED.PIC_ID AS Value
            VALUES ({0}, {1}, {2}, {3}, {4}, 100, 150, {5}, {6}, {7}, {8})
            """,
            title, catId, dateTime, Url(title), thumbUrl is null ? DBNull.Value : Thumb(title),
            userId is int id ? id : DBNull.Value, display, height, width).ToListAsync();
        return ids.Single();
    }

    private static string Url(string title) => $"/gallery/photos/{title.ToLowerInvariant()}.jpg";

    private static string Thumb(string title) => $"/gallery/photos/thumbs/{title.ToLowerInvariant()}.jpg";

    private DbContextOptions<EmptySchemaContext> SchemaOptions() =>
        new DbContextOptionsBuilder<EmptySchemaContext>().UseSqlServer(ConnectionString).Options;

    // Creates and drops the scratch database; the legacy objects come from raw DDL.
    private sealed class EmptySchemaContext(DbContextOptions<EmptySchemaContext> options) : DbContext(options);
}
