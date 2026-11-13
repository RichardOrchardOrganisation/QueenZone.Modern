using Microsoft.EntityFrameworkCore;
using QueenZone.Data;
using QueenZone.Data.Configurations;
using QueenZone.Data.Entities;

namespace QueenZone.SqlServerTests;

public sealed class AdminPhotoRepositoryContractTests : IAsyncLifetime
{
    private QueenZoneDbContext db = null!;
    private IAdminPhotoRepository Repository { get; set; } = null!;
    private IPhotoRepository PublicRepository { get; set; } = null!;
    private const string Editor = "editor@example.test";
    private static readonly DateTime Date = new(1986, 7, 12);
    private static AdminPhotoCreateRequest Request(string title = "Wembley", int cat = 9, bool visible = true) =>
        new(cat, title, "guitar", 1986, Date, visible, "/gallery/photo.jpg", "/gallery/thumb.jpg", 150, 100, 1920, 1080);
    private static AdminPhotoConcurrencyToken Token(AdminPhotoItem item) =>
        new(item.Title, item.Keywords, item.Year, item.DateTime, item.CatId, item.IsVisible);

    [Fact]
    public async Task Create_update_hide_and_delete_round_trip_through_admin_and_public_reads()
    {
        var id = await Repository.CreateAsync(Request(), Editor);
        Assert.True(id > 0);
        var created = await Repository.GetByIdAsync(id);
        Assert.NotNull(created);
        Assert.Equal("Wembley", created.Title);
        Assert.Equal(9, created.CatId);
        Assert.Equal("/gallery/photo.jpg", created.LegacyUrl);
        Assert.Equal("/gallery/thumb.jpg", created.LegacyThumbUrl);
        Assert.Equal((150, 100, 1920, 1080), (created.ThumbWidth, created.ThumbHeight, created.PictureWidth, created.PictureHeight));
        Assert.Equal(1986, created.Year);
        Assert.True(created.IsVisible);
        Assert.Contains(await PublicRepository.GetCategoryAllAsync(9), p => p.PicId == id);
        await Repository.UpdateAsync(id, new("Updated", "stage", 1985, Date.AddDays(-1), 11), Editor);
        var updated = (await Repository.GetByIdAsync(id))!;
        Assert.Equal(("Updated", "stage", 1985, Date.AddDays(-1), 11),
            (updated.Title, updated.Keywords, updated.Year, updated.DateTime, updated.CatId));
        Assert.DoesNotContain(await PublicRepository.GetCategoryAllAsync(9), p => p.PicId == id);
        Assert.Contains(await PublicRepository.GetCategoryAllAsync(11), p => p.PicId == id);
        await Repository.SetVisibilityAsync(id, false, Editor);
        Assert.False((await Repository.GetByIdAsync(id))!.IsVisible);
        Assert.DoesNotContain(await PublicRepository.GetCategoryAllAsync(11), p => p.PicId == id);
        Assert.Null(await PublicRepository.GetDetailNavigationAsync(11, id));
        Assert.Empty(await PublicRepository.GetPublishedByIdsAsync(11, [id]));
        Assert.DoesNotContain(await PublicRepository.GetLatestPublishedAsync(24), p => p.PicId == id);
        Assert.DoesNotContain((await PublicRepository.GetPublishedSitemapCategoriesAsync()).SelectMany(c => c.Photos), p => p.PicId == id);
        await Repository.DeleteAsync(id, Editor);
        Assert.Null(await Repository.GetByIdAsync(id));
    }

    [Fact]
    public async Task Admin_pages_include_hidden_filter_fields_and_clamp_bounds()
    {
        var first = await Repository.CreateAsync(Request("Red Special"), Editor);
        var hidden = await Repository.CreateAsync(Request("Hidden", 11, false) with { Year = 1975, Keywords = "drums" }, Editor);
        var all = await Repository.GetPageAsync(new(), 1, 10);
        Assert.Equal(2, all.TotalCount);
        Assert.Equal([hidden, first], all.Items.Select(p => p.PicId));
        foreach (var filter in new[] { new AdminPhotoListFilter(CatId: 11), new(IsVisible: false), new(Year: 1975), new(Search: "drums"), new(Search: "Hidden") })
        {
            var page = await Repository.GetPageAsync(filter, 1, 10);
            Assert.Equal(1, page.TotalCount);
            Assert.Equal(hidden, Assert.Single(page.Items).PicId);
        }
        Assert.Equal(first, Assert.Single((await Repository.GetPageAsync(new(IsVisible: true), 1, 10)).Items).PicId);
        Assert.Empty((await Repository.GetPageAsync(new(CatId: 9, IsVisible: false), 1, 10)).Items);
        var clamped = await Repository.GetPageAsync(new(), 0, 0);
        Assert.Equal((1, 1, 2), (clamped.Page, clamped.PageSize, clamped.TotalCount));
        Assert.Equal(hidden, Assert.Single(clamped.Items).PicId);
        Assert.Equal(first, Assert.Single((await Repository.GetPageAsync(new(), 2, 1)).Items).PicId);
        Assert.Equal(200, (await Repository.GetPageAsync(new(), 1, 999)).PageSize);
    }

    [Fact]
    public async Task Category_and_blob_reads_return_referenced_names()
    {
        Assert.Equal(["Brian May", "Roger Taylor"], (await Repository.GetCategoriesAsync()).Select(c => c.Name));
        Assert.Equal(new AdminPhotoCategory(9, "Brian May", "brian-may"), await Repository.GetCategoryByIdAsync(9));
        Assert.Null(await Repository.GetCategoryByIdAsync(999));
        await Repository.CreateAsync(Request(), Editor);
        Assert.Equal(["photo.jpg", "thumb.jpg"], (await Repository.GetReferencedBlobNamesAsync(9)).Order());
        Assert.Empty(await Repository.GetReferencedBlobNamesAsync(11));
    }

    [Fact]
    public async Task Missing_photos_throw_for_writes_and_read_as_null()
    {
        Assert.Null(await Repository.GetByIdAsync(999));
        var update = await Assert.ThrowsAsync<InvalidOperationException>(() => Repository.UpdateAsync(999, new("Missing", null, 1986, Date, 9), Editor));
        var visibility = await Assert.ThrowsAsync<InvalidOperationException>(() => Repository.SetVisibilityAsync(999, false, Editor));
        var delete = await Assert.ThrowsAsync<InvalidOperationException>(() => Repository.DeleteAsync(999, Editor));
        Assert.Equal("Photo 999 was not found.", update.Message);
        Assert.Equal(update.Message, visibility.Message);
        Assert.Equal(update.Message, delete.Message);
    }

    [Fact]
    public async Task Stale_edit_and_visibility_tokens_preserve_first_writer_state()
    {
        var id = await Repository.CreateAsync(Request(), Editor);
        var original = Token((await Repository.GetByIdAsync(id))!);
        await Repository.UpdateAsync(id, new("First writer", "stage", 1985, Date, 9), Editor, original);
        await Assert.ThrowsAsync<OptimisticConcurrencyException>(() => Repository.UpdateAsync(id, new("Stale", null, 1980, Date, 11), Editor, original));
        var saved = (await Repository.GetByIdAsync(id))!;
        Assert.Equal("First writer", saved.Title);
        Assert.Equal("stage", saved.Keywords);
        Assert.Equal(9, saved.CatId);
        await Repository.UpdateAsync(id, new("Fresh writer", "stage", 1985, Date, 9), Editor, Token(saved));
        await Repository.SetVisibilityAsync(id, false, Editor, expectedIsVisible: true);
        await Assert.ThrowsAsync<OptimisticConcurrencyException>(() => Repository.SetVisibilityAsync(id, true, Editor, expectedIsVisible: true));
        Assert.False((await Repository.GetByIdAsync(id))!.IsVisible);
        Assert.Equal("Fresh writer", (await Repository.GetByIdAsync(id))!.Title);
        await Repository.SetVisibilityAsync(id, true, Editor, expectedIsVisible: false);
        Assert.True((await Repository.GetByIdAsync(id))!.IsVisible);
    }

    [Fact]
    public async Task Two_contexts_reject_stale_edit_and_visibility_without_losing_first_writer()
    {
        var id = await Repository.CreateAsync(Request(), Editor);
        await using var secondContext = CreateContext();
        IAdminPhotoRepository second = new EfAdminPhotoRepository(secondContext);
        var original = Token((await second.GetByIdAsync(id))!);
        await Repository.UpdateAsync(id, new("First writer", "stage", 1985, Date, 9), Editor, original);
        await Assert.ThrowsAsync<OptimisticConcurrencyException>(() => second.UpdateAsync(id,
            new("Stale", null, 1980, Date, 11), Editor, original));
        Assert.Equal("First writer", (await second.GetByIdAsync(id))!.Title);
        var visible = (await second.GetByIdAsync(id))!.IsVisible;
        await Repository.SetVisibilityAsync(id, false, Editor, expectedIsVisible: visible);
        await Assert.ThrowsAsync<OptimisticConcurrencyException>(() => second.SetVisibilityAsync(id, true, Editor, expectedIsVisible: visible));
        var saved = (await second.GetByIdAsync(id))!;
        Assert.Equal("First writer", saved.Title);
        Assert.False(saved.IsVisible);
    }

    private readonly string databaseName = $"QueenZonePhotoContract_{Guid.NewGuid():N}";
    private string ConnectionString => new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(
        Environment.GetEnvironmentVariable("ConnectionStrings__SqlServerTest")
        ?? "Server=(localdb)\\MSSQLLocalDB;Trusted_Connection=True;TrustServerCertificate=True")
    {
        InitialCatalog = databaseName,
    }.ConnectionString;

    private QueenZoneDbContext CreateContext() => new(new DbContextOptionsBuilder<QueenZoneDbContext>()
        .UseSqlServer(ConnectionString).Options);

    public async Task InitializeAsync()
    {
        await using var schema = new PhotoSchemaContext(new DbContextOptionsBuilder<PhotoSchemaContext>()
            .UseSqlServer(ConnectionString).Options);
        await schema.Database.EnsureCreatedAsync();
        await schema.Database.ExecuteSqlRawAsync(LegacyPhotoSchema.CreateTablesSql);
        // Production EfPhotoRepository detail/sitemap SQL joins USERS_T (and optional
        // MemberAccounts / PhotoSubmissions). Same minimal schema as PhotoRepositorySqlServerTests.
        await schema.Database.ExecuteSqlRawAsync("""
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
            """);
        db = CreateContext();
        Repository = new EfAdminPhotoRepository(db);
        PublicRepository = new EfPhotoRepository(db);
    }

    public async Task DisposeAsync()
    {
        await using var schema = new PhotoSchemaContext(new DbContextOptionsBuilder<PhotoSchemaContext>()
            .UseSqlServer(ConnectionString).Options);
        await schema.Database.EnsureDeletedAsync();
        await db.DisposeAsync();
    }

    private sealed class PhotoSchemaContext(DbContextOptions<PhotoSchemaContext> options) : DbContext(options)
    {
        public DbSet<PhotoAdminAuditLogEntity> PhotoAdminAuditLogs => Set<PhotoAdminAuditLogEntity>();
        protected override void OnModelCreating(ModelBuilder builder) =>
            builder.ApplyConfiguration(new PhotoAdminAuditLogEntityConfiguration());
    }
}
