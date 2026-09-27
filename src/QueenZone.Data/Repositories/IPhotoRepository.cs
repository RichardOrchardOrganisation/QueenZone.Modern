namespace QueenZone.Data;

public interface IPhotoRepository
{
    Task<IReadOnlyList<PhotoCategory>> GetCategoriesAsync(CancellationToken cancellationToken = default);

    Task<PhotoCategory?> GetCategoryBySlugAsync(string slug, CancellationToken cancellationToken = default);

    Task<PhotoCategoryPage> GetCategoryPageAsync(
        int catId,
        int page,
        int pageSize,
        PhotoListFilter? filter = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Loads one photo plus prev/next ids and position without materializing the category.
    /// When <paramref name="filter"/> is active and the photo matches, totals and neighbors
    /// are restricted to matches. When the photo does not match, one unfiltered navigation
    /// query is used and <see cref="PhotoDetailNavigation.MatchedRequestedFilter"/> is false.
    /// Returns null when the photo is not a displayed member of the category.
    /// </summary>
    Task<PhotoDetailNavigation?> GetDetailNavigationAsync(
        int catId,
        int picId,
        PhotoListFilter? filter = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Full visible collection for tools/inventory. Prefer
    /// <see cref="GetDetailNavigationAsync"/> for public detail pages.
    /// </summary>
    Task<IReadOnlyList<PhotoItem>> GetCategoryAllAsync(int catId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns up to <paramref name="take"/> published photos from a category.
    /// The sample is an indexed id pick, not a sort of the category.
    /// </summary>
    Task<IReadOnlyList<PhotoItem>> GetRandomPublishedInCategoryAsync(
        int catId,
        int take,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Picks up to <paramref name="take"/> published pic ids (capped at 8) by seeking
    /// a random id in the category's min/max range.
    /// </summary>
    Task<IReadOnlyList<int>> PickRandomPublishedPhotoIdsAsync(
        int catId,
        int take,
        CancellationToken cancellationToken = default);

    /// <summary>Loads the published photos for the given ids, in that order.</summary>
    Task<IReadOnlyList<PhotoItem>> GetPublishedByIdsAsync(
        int catId,
        IReadOnlyList<int> picIds,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns visible categories and photo detail ids/dates in one repository pass
    /// for sitemap generation (avoids a second full category reload in the builder).
    /// </summary>
    Task<IReadOnlyList<PhotoSitemapCategory>> GetPublishedSitemapCategoriesAsync(
        CancellationToken cancellationToken = default);
}
