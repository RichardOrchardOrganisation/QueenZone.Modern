using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using QueenZone.Data;

namespace QueenZone.Web.Tests;

public abstract class PhotoRepositoryContractTests
{
    protected abstract IPhotoRepository Repository { get; }
    protected abstract Task SeedAsync(IReadOnlyList<PhotoCategorySeed> categories);

    private static readonly DateTime Date = new(1986, 7, 12);
    private static PhotoItemSeed Photo(int id, int width = 1920, int height = 1080, int day = 0) =>
        new(id, $"Photo {id}", $"/gallery/{id}.jpg", $"/gallery/{id}-t.jpg", Date.AddDays(day), width, height);

    private Task SeedAsync() => SeedAsync([
        new(9, "Brian May", [Photo(1, day: -1), Photo(2, 800, 600), Photo(3, 1080, 1920)]),
        new(11, "Roger Taylor", [Photo(4, day: 1)]),
        new(12, "Empty", [])]);

    [Fact]
    public async Task Categories_exclude_empty_and_supply_counts_cover_and_slug_lookup()
    {
        await SeedAsync();
        var categories = await Repository.GetCategoriesAsync();
        Assert.Equal([9, 11], categories.Select(c => c.CatId).Order());
        var brian = Assert.Single(categories, c => c.CatId == 9);
        Assert.Equal(3, brian.ImageCount);
        Assert.Contains(await Repository.GetCategoryAllAsync(9), photo => photo.ThumbnailUrl == brian.CoverThumbnailUrl);
        Assert.Equal(brian, await Repository.GetCategoryBySlugAsync("brian-may"));
        Assert.Equal(brian, await Repository.GetCategoryBySlugAsync("BRIAN-MAY"));
        Assert.Null(await Repository.GetCategoryBySlugAsync("missing"));
    }

    [Fact]
    public async Task Category_pages_sort_by_date_then_id_and_keep_unpaged_total()
    {
        await SeedAsync();
        var first = await Repository.GetCategoryPageAsync(9, 1, 2);
        Assert.Equal("Brian May", first.CategoryName);
        Assert.Equal(3, first.TotalCount);
        Assert.Equal([3, 2], first.Items.Select(p => p.PicId));
        var second = await Repository.GetCategoryPageAsync(9, 2, 2);
        Assert.Equal(3, second.TotalCount);
        Assert.Equal(1, Assert.Single(second.Items).PicId);
        Assert.Empty((await Repository.GetCategoryPageAsync(9, 3, 2)).Items);
        Assert.Equal([3, 2, 1], (await Repository.GetCategoryAllAsync(9)).Select(p => p.PicId));
        var missing = await Repository.GetCategoryPageAsync(999, 1, 2);
        Assert.Equal(string.Empty, missing.CategoryName);
        Assert.Equal(0, missing.TotalCount);
        Assert.Empty(missing.Items);
        Assert.Empty((await Repository.GetCategoryPageAsync(12, 1, 2)).Items);
        Assert.Empty(await Repository.GetCategoryAllAsync(999));
    }

    [Fact]
    public async Task Page_bounds_clamp_to_one_and_two_hundred()
    {
        await SeedAsync([new(9, "Brian May", Enumerable.Range(1, 201).Select(id => Photo(id)).ToList())]);
        var first = await Repository.GetCategoryPageAsync(9, 0, 0);
        Assert.Equal(201, first.TotalCount);
        Assert.Equal(201, Assert.Single(first.Items).PicId);
        var large = await Repository.GetCategoryPageAsync(9, 1, 999);
        Assert.Equal(201, large.TotalCount);
        Assert.Equal(200, large.Items.Count);
        Assert.Equal(1, Assert.Single((await Repository.GetCategoryPageAsync(9, 2, 999)).Items).PicId);
    }

    [Fact]
    public async Task Filters_control_page_totals_and_navigation_with_unfiltered_fallback()
    {
        await SeedAsync();
        var desktop = new PhotoListFilter(PhotoSizePreset.Desktop);
        var landscape = new PhotoListFilter(PhotoSizePreset.Landscape);
        var page = await Repository.GetCategoryPageAsync(9, 1, 10, desktop);
        Assert.Equal(1, page.TotalCount);
        Assert.Equal(1, Assert.Single(page.Items).PicId);
        page = await Repository.GetCategoryPageAsync(9, 1, 1, landscape);
        Assert.Equal(2, page.TotalCount);
        Assert.Equal(2, Assert.Single(page.Items).PicId);
        var nav = await Repository.GetDetailNavigationAsync(9, 2, landscape);
        Assert.NotNull(nav);
        Assert.True(nav.MatchedRequestedFilter);
        Assert.Equal((0, 2, (int?)null, (int?)1), (nav.Index, nav.Count, nav.PreviousPicId, nav.NextPicId));
        var last = await Repository.GetDetailNavigationAsync(9, 1, landscape);
        Assert.NotNull(last);
        Assert.Equal((1, 2, (int?)2, (int?)null), (last.Index, last.Count, last.PreviousPicId, last.NextPicId));
        var fallback = await Repository.GetDetailNavigationAsync(9, 2, desktop);
        Assert.NotNull(fallback);
        Assert.Equal(2, fallback.Photo.PicId);
        Assert.False(fallback.MatchedRequestedFilter);
        Assert.Equal((1, 3, (int?)3, (int?)1), (fallback.Index, fallback.Count, fallback.PreviousPicId, fallback.NextPicId));
        Assert.Null(await Repository.GetDetailNavigationAsync(9, 999, desktop));
        Assert.Null(await Repository.GetDetailNavigationAsync(9, 4));
    }

    [Fact]
    public async Task By_ids_preserves_requested_order_and_omits_missing_and_other_categories()
    {
        await SeedAsync();
        Assert.Equal([1, 3, 2], (await Repository.GetPublishedByIdsAsync(9, [1, 999, 3, 4, 2])).Select(p => p.PicId));
        Assert.Empty(await Repository.GetPublishedByIdsAsync(9, []));
    }

    [Fact]
    public async Task Latest_is_newest_id_first_across_categories_and_clamps_take()
    {
        await SeedAsync([
            new(9, "Brian May", Enumerable.Range(1, 25).Select(id => Photo(id)).ToList()),
            new(11, "Roger Taylor", [Photo(26)])]);
        Assert.Equal(26, Assert.Single(await Repository.GetLatestPublishedAsync(0)).PicId);
        Assert.Equal(Enumerable.Range(3, 24).Reverse(), (await Repository.GetLatestPublishedAsync(999)).Select(p => p.PicId));
    }

    [Fact]
    public async Task Sitemap_contains_only_nonempty_categories_and_photo_dates()
    {
        await SeedAsync();
        var sitemap = await Repository.GetPublishedSitemapCategoriesAsync();
        Assert.Equal([9, 11], sitemap.Select(c => c.CatId).Order());
        var brian = Assert.Single(sitemap, c => c.CatId == 9);
        Assert.Equal("brian-may", brian.Slug);
        Assert.Equal([1, 2, 3], brian.Photos.Select(p => p.PicId).Order());
        Assert.Equal(Date.AddDays(-1), Assert.Single(brian.Photos, p => p.PicId == 1).DateTime);
    }

    [Fact]
    public async Task Random_samples_are_distinct_bounded_and_category_scoped()
    {
        await SeedAsync([
            new(9, "Brian May", Enumerable.Range(1, 12).Select(id => Photo(id)).ToList()),
            new(11, "Roger Taylor", [Photo(20)]), new(12, "Empty", [])]);
        foreach (var take in new[] { 0, 3, 999 })
        {
            var ids = await Repository.PickRandomPublishedPhotoIdsAsync(9, take);
            Assert.InRange(ids.Count, 1, Math.Clamp(take, 1, 8));
            Assert.Equal(ids.Count, ids.Distinct().Count());
            Assert.All(ids, id => Assert.InRange(id, 1, 12));
            var photos = await Repository.GetRandomPublishedInCategoryAsync(9, take);
            Assert.InRange(photos.Count, 1, Math.Clamp(take, 1, 8));
            Assert.Equal(photos.Count, photos.Select(p => p.PicId).Distinct().Count());
            Assert.All(photos, p => { Assert.Equal(9, p.CatId); Assert.InRange(p.PicId, 1, 12); });
        }
        Assert.Equal(20, Assert.Single(await Repository.PickRandomPublishedPhotoIdsAsync(11, 8)));
        Assert.Equal(20, Assert.Single(await Repository.GetRandomPublishedInCategoryAsync(11, 8)).PicId);
        foreach (var id in new[] { 12, 999 })
        {
            Assert.Empty(await Repository.PickRandomPublishedPhotoIdsAsync(id, 8));
            Assert.Empty(await Repository.GetRandomPublishedInCategoryAsync(id, 8));
        }
    }
}

public sealed class InMemoryPhotoRepositoryContractTests : PhotoRepositoryContractTests
{
    private IPhotoRepository repository = null!;
    protected override IPhotoRepository Repository => repository;
    protected override Task SeedAsync(IReadOnlyList<PhotoCategorySeed> categories)
    {
        repository = new InMemoryPhotoRepository(new SharedPhotoStore(categories));
        return Task.CompletedTask;
    }
}

public sealed class EfPhotoRepositoryContractTests : PhotoRepositoryContractTests, IAsyncDisposable
{
    private readonly SqliteConnection connection;
    private readonly QueenZoneDbContext db;
    protected override IPhotoRepository Repository { get; }

    public EfPhotoRepositoryContractTests()
    {
        connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        db = new QueenZoneDbContext(new DbContextOptionsBuilder<QueenZoneDbContext>().UseSqlite(connection).Options);
        db.Database.ExecuteSqlRaw("""
            CREATE TABLE PhotoCategories (cat_id INTEGER NOT NULL, name TEXT NOT NULL);
            CREATE TABLE PhotoItems (
                NAME TEXT NOT NULL, DATE_TIME TEXT NOT NULL, URL TEXT NOT NULL, THUMB_URL TEXT NOT NULL,
                T_HEIGHT INTEGER NOT NULL, T_WIDTH INTEGER NOT NULL, PIC_WIDTH INTEGER NOT NULL,
                PIC_HEIGHT INTEGER NOT NULL, pic_id INTEGER NOT NULL, category_name TEXT,
                cat_id INTEGER NOT NULL, submitted_by_display_name TEXT);
            """);
        Repository = new EfPhotoRepository(db, PhotoSqlQueries.CreateSqliteFixture());
    }

    protected override async Task SeedAsync(IReadOnlyList<PhotoCategorySeed> categories)
    {
        foreach (var category in categories)
        {
            await db.Database.ExecuteSqlRawAsync("INSERT INTO PhotoCategories VALUES ({0}, {1})", category.CatId, category.Name);
            foreach (var photo in category.Items)
            {
                await db.Database.ExecuteSqlRawAsync("""
                    INSERT INTO PhotoItems VALUES ({0}, {1}, {2}, {3}, 100, 150, {4}, {5}, {6}, {7}, {8}, NULL)
                    """, photo.Title, photo.DateTime, photo.Url, photo.ThumbUrl, photo.PictureWidth,
                    photo.PictureHeight, photo.PicId, category.Name, category.CatId);
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        await db.DisposeAsync();
        await connection.DisposeAsync();
    }
}
