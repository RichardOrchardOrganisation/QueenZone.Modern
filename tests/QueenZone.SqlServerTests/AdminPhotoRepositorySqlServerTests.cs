using Microsoft.EntityFrameworkCore;
using QueenZone.Data;
using QueenZone.Data.Configurations;
using QueenZone.Data.Entities;

namespace QueenZone.SqlServerTests;

/// <summary>
/// Runs <see cref="EfAdminPhotoRepository"/> against real SQL Server tables shaped like the legacy
/// <c>PIC_FILES_T</c> / <c>PIC_CAT_T</c> (#1672). The column types below were read from the
/// <c>queenzone_legacy_sync</c> mirror, including the <c>smallint</c> <c>PIC_WIDTH</c>,
/// <c>PIC_HEIGHT</c> and <c>PICTURE_YEAR</c> columns that the in-memory admin tests cannot reach.
/// The read-only mirror probe is <c>EfAdminPhotoRepositoryLegacyProbeTests</c> in
/// <c>QueenZone.Web.Tests</c>.
/// </summary>
public sealed class AdminPhotoRepositorySqlServerTests : IAsyncLifetime
{
    private const string Editor = "editor@example.com";

    private const string LegacySchemaSql = """
        CREATE TABLE dbo.PIC_CAT_T
        (
            PIC_CAT_ID tinyint IDENTITY(1,1) NOT NULL PRIMARY KEY,
            Cat_ID int NOT NULL,
            Paths nvarchar(100) NULL,
            BaseUrl nvarchar(100) NULL,
            Name nvarchar(100) NULL,
            thedate datetime NULL
        );

        CREATE TABLE dbo.PIC_FILES_T
        (
            PIC_ID int IDENTITY(1,1) NOT NULL PRIMARY KEY,
            Name varchar(150) NULL,
            Cat_ID int NULL,
            Date_time datetime NOT NULL,
            Url varchar(400) NULL,
            Thumb_URL varchar(255) NULL,
            t_height int NULL,
            t_width int NULL,
            user_id int NULL,
            DISPLAY int NULL,
            PIC_HEIGHT smallint NOT NULL,
            PIC_WIDTH smallint NOT NULL,
            KEYWORDS varchar(1000) NULL,
            PICTURE_YEAR smallint NULL
        );
        """;

    private static readonly DateTime BaseTime = new(2026, 8, 17, 10, 0, 0);

    private readonly string databaseName = $"QueenZoneAdminPhotoTests_{Guid.NewGuid():N}";
    private QueenZoneDbContext dbContext = null!;
    private EfAdminPhotoRepository repository = null!;

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
        var schemaOptions = new DbContextOptionsBuilder<PhotoSchemaContext>()
            .UseSqlServer(ConnectionString).Options;
        await using (var schema = new PhotoSchemaContext(schemaOptions))
        {
            await schema.Database.EnsureCreatedAsync();
            await schema.Database.ExecuteSqlRawAsync(LegacySchemaSql);
            await schema.Database.ExecuteSqlRawAsync("""
                INSERT INTO dbo.PIC_CAT_T (Cat_ID, Name) VALUES (9, N'Brian May'), (11, N'Roger Taylor');
                """);
        }

        dbContext = new QueenZoneDbContext(new DbContextOptionsBuilder<QueenZoneDbContext>()
            .UseSqlServer(ConnectionString).Options);
        repository = new EfAdminPhotoRepository(dbContext);
    }

    public async Task DisposeAsync()
    {
        var schemaOptions = new DbContextOptionsBuilder<PhotoSchemaContext>()
            .UseSqlServer(ConnectionString).Options;
        await using var schema = new PhotoSchemaContext(schemaOptions);
        await schema.Database.EnsureDeletedAsync();
        await dbContext.DisposeAsync();
    }

    [Fact]
    public async Task GetPage_filters_orders_pages_and_materializes_legacy_smallint_columns()
    {
        var tieLow = await CreateAsync("Red Special", catId: 9, year: 1975, dateTime: BaseTime, keywords: "guitar");
        var tieHigh = await CreateAsync("Brian at Wembley", catId: 9, year: 1986, dateTime: BaseTime);
        var hidden = await CreateAsync("Hidden drum kit", catId: 11, year: 1977, dateTime: BaseTime.AddDays(-1),
            isVisible: false);
        // Legacy rows can have NULL year, thumb size and DISPLAY; PIC_WIDTH/PIC_HEIGHT are smallint.
        var legacy = await InsertLegacyRowAsync("Old scan", catId: 11, BaseTime.AddYears(-20));

        var all = await repository.GetPageAsync(new AdminPhotoListFilter(), 1, 10);
        Assert.Equal(4, all.TotalCount);
        Assert.Equal([tieHigh, tieLow, hidden, legacy], all.Items.Select(item => item.PicId));

        var legacyItem = all.Items[^1];
        Assert.Equal(BaseTime.AddYears(-20).Year, legacyItem.Year);
        Assert.Equal(0, legacyItem.ThumbWidth);
        Assert.Equal(0, legacyItem.ThumbHeight);
        Assert.Equal(32000, legacyItem.PictureWidth);
        Assert.Equal(1200, legacyItem.PictureHeight);
        Assert.False(legacyItem.IsVisible);
        Assert.Equal("Roger Taylor", legacyItem.CategoryName);
        Assert.Equal("roger-taylor", legacyItem.CategorySlug);

        var second = await repository.GetPageAsync(new AdminPhotoListFilter(), 2, 1);
        Assert.Equal(4, second.TotalCount);
        Assert.Equal(tieLow, Assert.Single(second.Items).PicId);

        var brian = await repository.GetPageAsync(new AdminPhotoListFilter(CatId: 9), 1, 10);
        Assert.Equal([tieHigh, tieLow], brian.Items.Select(item => item.PicId));

        var visible = await repository.GetPageAsync(new AdminPhotoListFilter(IsVisible: true), 1, 10);
        Assert.Equal(2, visible.TotalCount);
        var notVisible = await repository.GetPageAsync(new AdminPhotoListFilter(IsVisible: false), 1, 10);
        Assert.Equal([hidden], notVisible.Items.Select(item => item.PicId));

        var byYear = await repository.GetPageAsync(new AdminPhotoListFilter(Year: 1977), 1, 10);
        Assert.Equal([hidden], byYear.Items.Select(item => item.PicId));

        var byKeyword = await repository.GetPageAsync(new AdminPhotoListFilter(Search: " guitar "), 1, 10);
        Assert.Equal([tieLow], byKeyword.Items.Select(item => item.PicId));
        var byTitle = await repository.GetPageAsync(new AdminPhotoListFilter(Search: "wembley"), 1, 10);
        Assert.Equal([tieHigh], byTitle.Items.Select(item => item.PicId));

        var clamped = await repository.GetPageAsync(new AdminPhotoListFilter(), 0, 0);
        Assert.Equal(1, clamped.Page);
        Assert.Equal(1, clamped.PageSize);
    }

    [Fact]
    public async Task Category_and_blob_reference_reads_use_legacy_tables()
    {
        var categories = await repository.GetCategoriesAsync();
        Assert.Equal(["Brian May", "Roger Taylor"], categories.Select(category => category.Name));
        Assert.Equal("brian-may", categories[0].Slug);

        var roger = await repository.GetCategoryByIdAsync(11);
        Assert.Equal(new AdminPhotoCategory(11, "Roger Taylor", "roger-taylor"), roger);
        Assert.Null(await repository.GetCategoryByIdAsync(404));

        await CreateAsync("One", catId: 9, year: 1980, dateTime: BaseTime,
            url: "/gallery/photos/a.jpg", thumbUrl: "/gallery/photos/thumbs/a.jpg");
        await CreateAsync("Two", catId: 9, year: 1981, dateTime: BaseTime,
            url: "/gallery/photos/A.JPG", thumbUrl: "/gallery/photos/thumbs/b.jpg");
        await CreateAsync("Other category", catId: 11, year: 1982, dateTime: BaseTime,
            url: "/gallery/photos/c.jpg", thumbUrl: "/gallery/photos/thumbs/c.jpg");

        // Duplicates collapse case-insensitively; the query has no ORDER BY, so compare sorted.
        var blobNames = await repository.GetReferencedBlobNamesAsync(9);
        Assert.Equal(["a.jpg", "b.jpg"], blobNames.Select(name => name.ToLowerInvariant()).Order());
    }

    [Fact]
    public async Task Writes_round_trip_enforce_concurrency_and_append_audit_rows()
    {
        var picId = await CreateAsync("  Live Aid  ", catId: 9, year: 1985, dateTime: BaseTime, keywords: " stage ");
        var created = await repository.GetByIdAsync(picId);
        Assert.NotNull(created);
        Assert.Equal("Live Aid", created.Title);
        Assert.Equal("stage", created.Keywords);
        Assert.Equal(1985, created.Year);
        Assert.True(created.IsVisible);

        var token = new AdminPhotoConcurrencyToken(created.Title, created.Keywords, created.Year,
            created.DateTime, created.CatId, created.IsVisible);
        await repository.UpdateAsync(picId,
            new AdminPhotoUpdateRequest("Live Aid 1985", null, 1985, BaseTime.AddHours(1), 11), Editor, token);

        var updated = await repository.GetByIdAsync(picId);
        Assert.NotNull(updated);
        Assert.Equal("Live Aid 1985", updated.Title);
        Assert.Null(updated.Keywords);
        Assert.Equal(11, updated.CatId);

        // The original token no longer matches the row.
        await Assert.ThrowsAsync<OptimisticConcurrencyException>(() => repository.UpdateAsync(picId,
            new AdminPhotoUpdateRequest("Stale", null, 1985, BaseTime, 9), Editor, token));
        await Assert.ThrowsAsync<InvalidOperationException>(() => repository.UpdateAsync(404,
            new AdminPhotoUpdateRequest("Missing", null, 1985, BaseTime, 9), Editor));

        await repository.SetVisibilityAsync(picId, false, Editor, expectedIsVisible: true);
        await Assert.ThrowsAsync<OptimisticConcurrencyException>(() =>
            repository.SetVisibilityAsync(picId, true, Editor, expectedIsVisible: true));
        Assert.False((await repository.GetByIdAsync(picId))!.IsVisible);

        await repository.UpdateAssetsAsync(picId,
            new AdminPhotoAssetUpdate("/gallery/photos/new.jpg", "/gallery/photos/thumbs/new.jpg", 150, 100, 3000, 2000),
            Editor);
        await repository.UpdateThumbnailAsync(picId, "/gallery/photos/thumbs/regen.jpg", 160, 120, Editor);
        var assets = await repository.GetByIdAsync(picId);
        Assert.NotNull(assets);
        Assert.Equal("/gallery/photos/new.jpg", assets.LegacyUrl);
        Assert.Equal("/gallery/photos/thumbs/regen.jpg", assets.LegacyThumbUrl);
        Assert.Equal((160, 120, 3000, 2000),
            (assets.ThumbWidth, assets.ThumbHeight, assets.PictureWidth, assets.PictureHeight));
        await Assert.ThrowsAsync<InvalidOperationException>(() => repository.UpdateAssetsAsync(404,
            new AdminPhotoAssetUpdate("a", "b", 1, 1, 1, 1), Editor));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            repository.UpdateThumbnailAsync(404, "b", 1, 1, Editor));

        await repository.DeleteAsync(picId, Editor);
        Assert.Null(await repository.GetByIdAsync(picId));
        await Assert.ThrowsAsync<InvalidOperationException>(() => repository.DeleteAsync(picId, Editor));

        var actions = await dbContext.PhotoAdminAuditLogs
            .Where(log => log.PicId == picId)
            .OrderBy(log => log.Id)
            .Select(log => log.Action)
            .ToListAsync();
        Assert.Equal(["create", "edit", "hide", "replace", "regenerate-thumb", "delete"], actions);
    }

    private Task<int> CreateAsync(
        string title,
        int catId,
        int year,
        DateTime dateTime,
        string? keywords = null,
        bool isVisible = true,
        string url = "/gallery/photos/x.jpg",
        string thumbUrl = "/gallery/photos/thumbs/x.jpg") =>
        repository.CreateAsync(
            new AdminPhotoCreateRequest(catId, title, keywords, year, dateTime, isVisible, url, thumbUrl,
                ThumbWidth: 150, ThumbHeight: 100, PictureWidth: 1500, PictureHeight: 1000),
            Editor);

    private async Task<int> InsertLegacyRowAsync(string title, int catId, DateTime dateTime)
    {
        var ids = await dbContext.Database.SqlQueryRaw<int>(
            """
            INSERT INTO dbo.PIC_FILES_T (Name, Cat_ID, Date_time, Url, Thumb_URL, PIC_HEIGHT, PIC_WIDTH)
            OUTPUT CAST(INSERTED.PIC_ID AS int) AS Value
            VALUES ({0}, {1}, {2}, '/gallery/photos/old.jpg', NULL, 1200, 32000)
            """,
            title, catId, dateTime).ToListAsync();
        return ids.Single();
    }

    // The production DbContext includes legacy tables that cannot be created in a blank database,
    // so only the modern audit table comes from EF; the legacy tables use LegacySchemaSql.
    private sealed class PhotoSchemaContext(DbContextOptions<PhotoSchemaContext> options) : DbContext(options)
    {
        public DbSet<PhotoAdminAuditLogEntity> PhotoAdminAuditLogs => Set<PhotoAdminAuditLogEntity>();

        protected override void OnModelCreating(ModelBuilder modelBuilder) =>
            modelBuilder.ApplyConfiguration(new PhotoAdminAuditLogEntityConfiguration());
    }
}
