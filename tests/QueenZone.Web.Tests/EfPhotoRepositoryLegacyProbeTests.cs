using Microsoft.EntityFrameworkCore;
using QueenZone.Data;

namespace QueenZone.Web.Tests;

/// <summary>
/// Read-only probe of the production <see cref="EfPhotoRepository"/> SQL against the real legacy
/// picture tables (#1672). Skips when <c>ConnectionStrings__QueenZoneLegacy</c> is not set; the
/// nightly <c>legacy-read-probes</c> job runs it against the SQL Express mirror. Scratch-schema
/// coverage lives in <c>PhotoRepositorySqlServerTests</c>.
/// </summary>
[Collection(LiveDatabaseProbeCollection.Name)]
public sealed class EfPhotoRepositoryLegacyProbeTests
{
    [Fact]
    public async Task Probe_legacy_photo_gallery_reads_when_connection_configured()
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__QueenZoneLegacy");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return;
        }

        var options = new DbContextOptionsBuilder<QueenZoneDbContext>()
            .UseSqlServer(connectionString)
            .Options;
        await using var dbContext = new QueenZoneDbContext(options);
        var repository = new EfPhotoRepository(dbContext);

        var categories = await repository.GetCategoriesAsync();
        Assert.NotEmpty(categories);
        var category = categories.MaxBy(item => item.ImageCount)!;

        var page = await repository.GetCategoryPageAsync(category.CatId, 1, 5);
        Assert.Equal(category.ImageCount, page.TotalCount);
        Assert.NotEmpty(page.Items);

        var navigation = await repository.GetDetailNavigationAsync(category.CatId, page.Items[0].PicId);
        Assert.NotNull(navigation);
        Assert.Equal(category.ImageCount, navigation.Count);
        Assert.Equal(0, navigation.Index);

        // Exercises the persisted PIC_LONGEST_SIDE filter path.
        var hd = await repository.GetCategoryPageAsync(category.CatId, 1, 5, new PhotoListFilter(PhotoSizePreset.Hd));
        Assert.True(hd.TotalCount <= page.TotalCount);

        var picked = await repository.PickRandomPublishedPhotoIdsAsync(category.CatId, 3);
        var byIds = await repository.GetPublishedByIdsAsync(category.CatId, picked);
        Assert.Equal(picked, byIds.Select(item => item.PicId));

        var latest = await repository.GetLatestPublishedAsync(6);
        Assert.NotEmpty(latest);
        Assert.Equal(latest.OrderByDescending(item => item.PicId).Select(item => item.PicId), latest.Select(item => item.PicId));

        var sitemap = await repository.GetPublishedSitemapCategoriesAsync();
        Assert.Equal(categories.Count, sitemap.Count);
    }
}
