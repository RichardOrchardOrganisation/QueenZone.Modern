using QueenZone.Data;
using QueenZone.Tools;

namespace QueenZone.Tools.Tests;

public sealed class PhotoCategoryScanTests
{
    [Fact]
    public async Task LoadPhotosAsync_FiltersByCategoryId()
    {
        var photos = await PhotoCategoryScan.LoadPhotosAsync(
            SampleRepository(),
            categoryId: 9,
            categorySlug: null,
            limit: null,
            CancellationToken.None);

        Assert.Equal(3, photos.Count);
        Assert.All(photos, photo => Assert.Equal(9, photo.CatId));
    }

    [Fact]
    public async Task LoadPhotosAsync_PrefersCategoryIdOverSlug()
    {
        var photos = await PhotoCategoryScan.LoadPhotosAsync(
            SampleRepository(),
            categoryId: 12,
            categorySlug: "brian-may",
            limit: null,
            CancellationToken.None);

        Assert.Equal(4, photos.Count);
        Assert.All(photos, photo => Assert.Equal("queen", photo.CategorySlug, StringComparer.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task LoadPhotosAsync_ReturnsEmpty_WhenCategoryIdIsUnknown()
    {
        var photos = await PhotoCategoryScan.LoadPhotosAsync(
            SampleRepository(),
            categoryId: 999,
            categorySlug: null,
            limit: 5,
            CancellationToken.None);

        Assert.Empty(photos);
    }

    [Fact]
    public async Task LoadPhotosAsync_ReturnsAllPhotos_WhenNoFilterOrLimit()
    {
        var photos = await PhotoCategoryScan.LoadPhotosAsync(
            SampleRepository(),
            categoryId: null,
            categorySlug: null,
            limit: null,
            CancellationToken.None);

        Assert.Equal(11, photos.Count);
    }

    [Fact]
    public async Task LoadPhotosAsync_ReturnsAllMatchingPhotos_WhenLimitIsAboveCount()
    {
        var photos = await PhotoCategoryScan.LoadPhotosAsync(
            SampleRepository(),
            categoryId: null,
            categorySlug: "freddie-mercury",
            limit: 50,
            CancellationToken.None);

        Assert.Equal(4, photos.Count);
        Assert.All(photos, photo => Assert.Equal("freddie-mercury", photo.CategorySlug, StringComparer.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task LoadPhotosAsync_StopsAfterLimitAcrossCategories()
    {
        var photos = await PhotoCategoryScan.LoadPhotosAsync(
            SampleRepository(),
            categoryId: null,
            categorySlug: null,
            limit: 4,
            CancellationToken.None);

        Assert.Equal(4, photos.Count);
        Assert.True(photos.Select(photo => photo.CatId).Distinct().Count() >= 2);
    }

    private static InMemoryPhotoRepository SampleRepository() =>
        new(new SharedPhotoStore(SamplePhotoData.CreateSeedCategories()));
}
