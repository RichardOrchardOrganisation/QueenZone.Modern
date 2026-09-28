using Microsoft.EntityFrameworkCore;
using QueenZone.Data;

namespace QueenZone.Web.Tests;

/// <summary>
/// Read-only probe of <see cref="EfAdminPhotoRepository"/> against the real legacy picture tables
/// (#1672). Skips when <c>ConnectionStrings__QueenZoneLegacy</c> is not set; the nightly
/// <c>legacy-read-probes</c> job runs it against the SQL Express mirror. Scratch-schema coverage
/// lives in <c>AdminPhotoRepositorySqlServerTests</c>.
/// </summary>
[Collection(LiveDatabaseProbeCollection.Name)]
public sealed class EfAdminPhotoRepositoryLegacyProbeTests
{
    [Fact]
    public async Task Probe_legacy_admin_photo_reads_when_connection_configured()
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
        var repository = new EfAdminPhotoRepository(dbContext);

        var categories = await repository.GetCategoriesAsync();
        Assert.NotEmpty(categories);

        var page = await repository.GetPageAsync(new AdminPhotoListFilter(), 1, 5);
        Assert.NotEmpty(page.Items);
        Assert.True(page.TotalCount >= page.Items.Count);

        // smallint PIC_WIDTH / PIC_HEIGHT / PICTURE_YEAR must materialize into the int row model.
        var photo = page.Items[0];
        var loaded = await repository.GetByIdAsync(photo.PicId);
        Assert.Equal(photo, loaded);

        var category = await repository.GetCategoryByIdAsync(photo.CatId);
        Assert.NotNull(category);
        Assert.Equal(photo.CategoryName, category.Name);

        // The newest photo overall is also the newest in its category.
        var filtered = await repository.GetPageAsync(new AdminPhotoListFilter(CatId: photo.CatId), 1, 1);
        Assert.Equal(photo.PicId, Assert.Single(filtered.Items).PicId);

        var search = photo.Title.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        if (search is not null)
        {
            var searched = await repository.GetPageAsync(new AdminPhotoListFilter(Search: search), 1, 1);
            Assert.True(searched.TotalCount >= 1);
        }

        Assert.NotNull(await repository.GetReferencedBlobNamesAsync(photo.CatId));
    }
}
