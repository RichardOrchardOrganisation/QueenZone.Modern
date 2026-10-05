using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using QueenZone.Data;

namespace QueenZone.Web;

public sealed class PublicMediaQueryCache(
    PublicQueryCacheStore store,
    IPhotoRepository photoRepository,
    IFanPerformanceRepository fanPerformanceRepository)
{
    private IMemoryCache cache => store.Cache;
    private IOptions<PublicQueryCacheOptions> options => store.Options;

    public Task<IReadOnlyList<PhotoCategory>> GetPhotoCategoriesAsync(CancellationToken cancellationToken = default)
    {
        var version = store.GetPhotoCacheVersion();
        return store.GetOrCreateAsync(
            PublicQueryCacheKeys.PhotoCategories(version),
            options.Value.PhotoCacheDuration,
            () => photoRepository.GetCategoriesAsync(cancellationToken),
            cancellationToken);
    }

    /// <summary>Newest displayed photos across all categories, for the homepage gallery strip.</summary>
    public Task<IReadOnlyList<PhotoItem>> GetLatestPhotosAsync(int count, CancellationToken cancellationToken = default)
    {
        var version = store.GetPhotoCacheVersion();
        return store.GetOrCreateAsync(
            PublicQueryCacheKeys.LatestPhotos(version, count),
            options.Value.PhotoCacheDuration,
            () => photoRepository.GetLatestPublishedAsync(count, cancellationToken),
            cancellationToken);
    }

    public async Task<PhotoCategory?> GetPhotoCategoryBySlugAsync(
        string slug,
        CancellationToken cancellationToken = default)
    {
        var categories = await GetPhotoCategoriesAsync(cancellationToken);
        return categories.FirstOrDefault(category =>
            string.Equals(category.Slug, slug, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Up to four Freddie-category photo ids, cached for a few minutes, then loaded by id.
    /// Category lookup reuses <see cref="GetPhotoCategoriesAsync"/>.
    /// </summary>
    public async Task<IReadOnlyList<PhotoItem>> GetFreddieTributePhotosAsync(
        CancellationToken cancellationToken = default)
    {
        var categories = await GetPhotoCategoriesAsync(cancellationToken);
        var category = categories.FirstOrDefault(item =>
            item.Slug.Contains("freddie", StringComparison.OrdinalIgnoreCase));
        if (category is null)
        {
            return [];
        }

        var version = store.GetPhotoCacheVersion();
        var ids = await store.GetOrCreateAsync(
            PublicQueryCacheKeys.FreddiePhotoSample(version, category.CatId),
            options.Value.FreddieSampleCacheDuration,
            () => photoRepository.PickRandomPublishedPhotoIdsAsync(category.CatId, 4, cancellationToken),
            cancellationToken);
        if (ids.Count == 0)
        {
            return [];
        }

        return await photoRepository.GetPublishedByIdsAsync(category.CatId, ids, cancellationToken);
    }

    public Task<PhotoCategoryPage> GetPhotoCategoryPageAsync(
        int catId,
        int page,
        int pageSize,
        PhotoListFilter? filter = null,
        CancellationToken cancellationToken = default)
    {
        var activeFilter = filter ?? PhotoListFilter.None;
        var version = store.GetPhotoCacheVersion();
        return store.GetOrCreateAsync(
            PublicQueryCacheKeys.PhotoCategoryPage(version, catId, page, pageSize, activeFilter.QueryValue),
            options.Value.PhotoCacheDuration,
            () => photoRepository.GetCategoryPageAsync(catId, page, pageSize, activeFilter, cancellationToken),
            cancellationToken);
    }

    public Task<IReadOnlyList<FanPerformance>> GetFanPerformancePageAsync(
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var version = store.GetFanPerformanceCacheVersion();
        return store.GetOrCreateAsync(
            PublicQueryCacheKeys.FanPerformancePage(version, page, pageSize),
            options.Value.FanPerformanceCacheDuration,
            () => fanPerformanceRepository.GetPageAsync(page, pageSize, cancellationToken),
            cancellationToken);
    }

    public Task<int> GetFanPerformanceVisibleCountAsync(CancellationToken cancellationToken = default)
    {
        var version = store.GetFanPerformanceCacheVersion();
        return store.GetOrCreateAsync(
            PublicQueryCacheKeys.FanPerformanceVisibleCount(version),
            options.Value.FanPerformanceCacheDuration,
            () => fanPerformanceRepository.GetVisibleCountAsync(cancellationToken),
            cancellationToken);
    }

    public Task<FanPerformance?> GetFanPerformanceByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        var version = store.GetFanPerformanceCacheVersion();
        return store.GetOrCreateAsync(
            PublicQueryCacheKeys.FanPerformanceById(version, id),
            options.Value.FanPerformanceCacheDuration,
            () => fanPerformanceRepository.GetByIdAsync(id, cancellationToken),
            cancellationToken);
    }

    /// <summary>
    /// Bumps the photo cache version so category lists and paged grids refresh after admin writes.
    /// </summary>
    public void InvalidatePhotoCache()
    {
        cache.Set(PublicQueryCacheKeys.PhotoVersion, PublicQueryCacheStore.CreateCacheVersion(), PublicQueryCacheStore.VersionEntryOptions);
    }

    /// <summary>
    /// Bumps the fan-performance cache version so archive pages and the
    /// <c>/api/v1</c> content projection refresh after admin writes.
    /// </summary>
    public void InvalidateFanPerformanceCache()
    {
        cache.Set(PublicQueryCacheKeys.FanPerformanceVersion, PublicQueryCacheStore.CreateCacheVersion(), PublicQueryCacheStore.VersionEntryOptions);
    }

}
