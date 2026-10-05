using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using QueenZone.Data;

namespace QueenZone.Web;

public sealed class PublicQueryCacheService(
    PublicEditorialQueryCache editorial,
    PublicForumQueryCache forum,
    PublicCatalogQueryCache catalog,
    PublicMediaQueryCache media)
{
    public Task<IReadOnlyList<NewsItem>> GetLatestNewsAsync(int count, CancellationToken cancellationToken = default) =>
        editorial.GetLatestNewsAsync(count, cancellationToken);

    public Task<int> GetNewsPublishedCountAsync(CancellationToken cancellationToken = default) =>
        editorial.GetNewsPublishedCountAsync(cancellationToken);

    public Task<int> GetNewsPublishedCountAsync(
        NewsArchiveFilter filter,
        CancellationToken cancellationToken = default) =>
        editorial.GetNewsPublishedCountAsync(filter, cancellationToken);

    public Task<IReadOnlyList<NewsItem>> GetNewsArchivePageAsync(
        int page,
        int pageSize,
        NewsArchiveFilter filter = default,
        CancellationToken cancellationToken = default) =>
        editorial.GetNewsArchivePageAsync(page, pageSize, filter, cancellationToken);

    public Task<int> GetArticlePublishedCountAsync(CancellationToken cancellationToken = default) =>
        editorial.GetArticlePublishedCountAsync(cancellationToken);

    public Task<IReadOnlyList<ArticleItem>> GetLatestArticlesAsync(int count, CancellationToken cancellationToken = default) =>
        editorial.GetLatestArticlesAsync(count, cancellationToken);

    public Task<IReadOnlyList<PublishedArticleSubmission>> GetLatestCommunityArticlesAsync(
        int count,
        CancellationToken cancellationToken = default) =>
        editorial.GetLatestCommunityArticlesAsync(count, cancellationToken);

    public Task<IReadOnlyList<ArticleItem>> GetArticlesArchivePageAsync(
        int page,
        int pageSize,
        CancellationToken cancellationToken = default) =>
        editorial.GetArticlesArchivePageAsync(page, pageSize, cancellationToken);

    /// <summary>
    /// Cached merged {source, id, date} index for <c>/articles</c>. Community
    /// <see cref="SqlException"/> falls back to archive-only and is not cached.
    /// Tag views are community-only.
    /// </summary>
    public Task<IReadOnlyList<ArticleFeedKey>> GetMergedArticleFeedIndexAsync(
        string? tag = null,
        CancellationToken cancellationToken = default) =>
        editorial.GetMergedArticleFeedIndexAsync(tag, cancellationToken);

    public Task<IReadOnlyList<ArticleArchiveItem>> HydrateArticleFeedAsync(
        IReadOnlyList<ArticleFeedKey> keys,
        CancellationToken cancellationToken = default) =>
        editorial.HydrateArticleFeedAsync(keys, cancellationToken);

    public Task<IReadOnlyList<ForumCategoryItem>> GetForumCategoriesAsync(CancellationToken cancellationToken = default) =>
        forum.GetForumCategoriesAsync(cancellationToken);

    public Task<int> GetForumThreadCountAsync(CancellationToken cancellationToken = default) =>
        forum.GetForumThreadCountAsync(cancellationToken);

    public Task<IReadOnlyList<ForumRecentThreadItem>> GetForumRecentThreadsAsync(
        int count,
        CancellationToken cancellationToken = default) =>
        forum.GetForumRecentThreadsAsync(count, cancellationToken);

    /// <summary>
    /// John S Stuart's rare/discography posts for the "Rare Discography" page. Long-lived cache:
    /// this legacy-flagged set only changes via re-import, not day-to-day forum activity.
    /// </summary>
    public Task<IReadOnlyList<ForumRecentThreadItem>> GetForumLegacyDiscographyThreadsAsync(
        CancellationToken cancellationToken = default) =>
        forum.GetForumLegacyDiscographyThreadsAsync(cancellationToken);

    public Task<IReadOnlyList<QueenHistoryEvent>> GetOnThisDayAsync(
        DateOnly date,
        int count,
        CancellationToken cancellationToken = default) =>
        catalog.GetOnThisDayAsync(date, count, cancellationToken);

    public Task<IReadOnlyList<QueenHistoryEvent>> GetAroundThisDayAsync(
        DateOnly date,
        int dayWindow,
        int count,
        CancellationToken cancellationToken = default) =>
        catalog.GetAroundThisDayAsync(date, dayWindow, count, cancellationToken);

    public Task<IReadOnlyList<QueenHistoryEvent>> GetAllPublishedHistoryEventsAsync(
        CancellationToken cancellationToken = default) =>
        catalog.GetAllPublishedHistoryEventsAsync(cancellationToken);

    /// <summary>
    /// Caches the published quote pool and picks <see cref="Random.Shared"/> per request
    /// so consecutive callers do not freeze on one quote.
    /// </summary>
    public Task<QuoteItem?> GetRandomPublishedQuoteAsync(CancellationToken cancellationToken = default) =>
        catalog.GetRandomPublishedQuoteAsync(cancellationToken);

    /// <summary>
    /// Up to <paramref name="count"/> distinct published quotes in random order, drawn from the
    /// same cached pool as <see cref="GetRandomPublishedQuoteAsync"/>.
    /// </summary>
    public Task<IReadOnlyList<QuoteItem>> GetRandomPublishedQuotesAsync(
        int count,
        CancellationToken cancellationToken = default) =>
        catalog.GetRandomPublishedQuotesAsync(count, cancellationToken);

    /// <summary>
    /// Caches the published trivia pool and picks <see cref="Random.Shared"/> per request
    /// so consecutive callers do not freeze on one fact.
    /// </summary>
    public Task<TriviaFactItem?> GetRandomPublishedTriviaAsync(CancellationToken cancellationToken = default) =>
        catalog.GetRandomPublishedTriviaAsync(cancellationToken);

    public Task<IReadOnlyList<BiographyChapterItem>> GetBiographyChaptersAsync(
        CancellationToken cancellationToken = default) =>
        catalog.GetBiographyChaptersAsync(cancellationToken);

    public Task<IReadOnlyList<AlbumSummary>> GetDiscographyAlbumsAsync(
        CancellationToken cancellationToken = default) =>
        catalog.GetDiscographyAlbumsAsync(cancellationToken);

    public Task<AlbumDetail?> GetDiscographyAlbumByIdAsync(
        int albumId,
        CancellationToken cancellationToken = default) =>
        catalog.GetDiscographyAlbumByIdAsync(albumId, cancellationToken);

    public Task<IReadOnlyList<SongSummary>> GetSongsAsync(
        CancellationToken cancellationToken = default) =>
        catalog.GetSongsAsync(cancellationToken);

    public Task<SongDetail?> GetSongBySlugAsync(
        string slug,
        CancellationToken cancellationToken = default) =>
        catalog.GetSongBySlugAsync(slug, cancellationToken);

    public Task<IReadOnlyList<PhotoCategory>> GetPhotoCategoriesAsync(CancellationToken cancellationToken = default) =>
        media.GetPhotoCategoriesAsync(cancellationToken);

    /// <summary>Newest displayed photos across all categories, for the homepage gallery strip.</summary>
    public Task<IReadOnlyList<PhotoItem>> GetLatestPhotosAsync(int count, CancellationToken cancellationToken = default) =>
        media.GetLatestPhotosAsync(count, cancellationToken);

    public Task<PhotoCategory?> GetPhotoCategoryBySlugAsync(
        string slug,
        CancellationToken cancellationToken = default) =>
        media.GetPhotoCategoryBySlugAsync(slug, cancellationToken);

    /// <summary>
    /// Caches one visible tribute id for a few minutes. A miss seeks an indexed id range;
    /// a hit loads that id. Neither path sorts <c>FREDDIE_T</c> with <c>NEWID()</c>.
    /// </summary>
    public Task<FreddieTribute?> GetFeaturedFreddieTributeAsync(CancellationToken cancellationToken = default) =>
        catalog.GetFeaturedFreddieTributeAsync(cancellationToken);

    /// <summary>
    /// Up to four Freddie-category photo ids, cached for a few minutes, then loaded by id.
    /// Category lookup reuses <see cref="GetPhotoCategoriesAsync"/>.
    /// </summary>
    public Task<IReadOnlyList<PhotoItem>> GetFreddieTributePhotosAsync(
        CancellationToken cancellationToken = default) =>
        media.GetFreddieTributePhotosAsync(cancellationToken);

    public Task<PhotoCategoryPage> GetPhotoCategoryPageAsync(
        int catId,
        int page,
        int pageSize,
        PhotoListFilter? filter = null,
        CancellationToken cancellationToken = default) =>
        media.GetPhotoCategoryPageAsync(catId, page, pageSize, filter, cancellationToken);

    /// <summary>
    /// Count of forum posts made today. Short 45s TTL: no presence-tracking exists, so this
    /// is the only honest "live" signal for the mobile home screen's activity strip.
    /// </summary>
    public Task<int> GetLiveActivityNewForumRepliesTodayAsync(CancellationToken cancellationToken = default) =>
        forum.GetLiveActivityNewForumRepliesTodayAsync(cancellationToken);

    public Task<IReadOnlyList<FanPerformance>> GetFanPerformancePageAsync(
        int page,
        int pageSize,
        CancellationToken cancellationToken = default) =>
        media.GetFanPerformancePageAsync(page, pageSize, cancellationToken);

    public Task<int> GetFanPerformanceVisibleCountAsync(CancellationToken cancellationToken = default) =>
        media.GetFanPerformanceVisibleCountAsync(cancellationToken);

    public Task<FanPerformance?> GetFanPerformanceByIdAsync(int id, CancellationToken cancellationToken = default) =>
        media.GetFanPerformanceByIdAsync(id, cancellationToken);

    /// <summary>
    /// Invalidates all public news cache entries (latest lists, archive pages, and published counts)
    /// by bumping the news cache version. Call after publish, unpublish, delete of published news,
    /// or edit of published news.
    /// </summary>
    public void InvalidateNewsCache() =>
        editorial.InvalidateNewsCache();

    public void InvalidateForumStatsCache() =>
        forum.InvalidateForumStatsCache();

    /// <summary>
    /// Invalidates public article cache entries (latest lists, archive pages, published count)
    /// by bumping the article cache version.
    /// </summary>
    public void InvalidateArticleCountCache() =>
        editorial.InvalidateArticleCountCache();

    public void InvalidateArticlesCache() =>
        editorial.InvalidateArticlesCache();

    public void InvalidateQuotesCache() =>
        catalog.InvalidateQuotesCache();

    public void InvalidateTriviaCache() =>
        catalog.InvalidateTriviaCache();

    public void InvalidateBiographyCache() =>
        catalog.InvalidateBiographyCache();

    /// <summary>
    /// Evicts the public discography album list. No admin write path exists today;
    /// TTL is the freshness fallback until a sync/admin writer is wired.
    /// </summary>
    public void InvalidateDiscographyCache() =>
        catalog.InvalidateDiscographyCache();

    /// <summary>
    /// Bumps the photo cache version so category lists and paged grids refresh after admin writes.
    /// </summary>
    public void InvalidatePhotoCache() =>
        media.InvalidatePhotoCache();

    public void InvalidateHistoryCache() =>
        catalog.InvalidateHistoryCache();

    /// <summary>
    /// Bumps the fan-performance cache version so archive pages and the
    /// <c>/api/v1</c> content projection refresh after admin writes.
    /// </summary>
    public void InvalidateFanPerformanceCache() =>
        media.InvalidateFanPerformanceCache();

}
