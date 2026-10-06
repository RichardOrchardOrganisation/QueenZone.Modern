using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using QueenZone.Data;

namespace QueenZone.Web;

public sealed class PublicCatalogQueryCache(
    PublicQueryCacheStore store,
    IQueenHistoryRepository queenHistoryRepository,
    IQuoteRepository quoteRepository,
    ITriviaRepository triviaRepository,
    IBiographyRepository biographyRepository,
    IDiscographyRepository discographyRepository,
    IFreddieTributeRepository freddieTributeRepository)
{
    private IMemoryCache cache => store.Cache;
    private IOptions<PublicQueryCacheOptions> options => store.Options;

    public Task<IReadOnlyList<QueenHistoryEvent>> GetOnThisDayAsync(
        DateOnly date,
        int count,
        CancellationToken cancellationToken = default)
    {
        var version = store.GetHistoryCacheVersion();
        return store.GetOrCreateAsync(
            PublicQueryCacheKeys.OnThisDay(version, date, count),
            options.Value.OnThisDayCacheDuration,
            () => queenHistoryRepository.GetOnThisDayAsync(date, count, cancellationToken),
            cancellationToken);
    }

    public Task<IReadOnlyList<QueenHistoryEvent>> GetAroundThisDayAsync(
        DateOnly date,
        int dayWindow,
        int count,
        CancellationToken cancellationToken = default)
    {
        var version = store.GetHistoryCacheVersion();
        return store.GetOrCreateAsync(
            PublicQueryCacheKeys.AroundThisDay(version, date, dayWindow, count),
            options.Value.OnThisDayCacheDuration,
            () => queenHistoryRepository.GetAroundThisDayAsync(date, dayWindow, count, cancellationToken),
            cancellationToken);
    }

    public Task<IReadOnlyList<QueenHistoryEvent>> GetAllPublishedHistoryEventsAsync(
        CancellationToken cancellationToken = default)
    {
        var version = store.GetHistoryCacheVersion();
        return store.GetOrCreateAsync(
            PublicQueryCacheKeys.AllPublishedHistory(version),
            options.Value.OnThisDayCacheDuration,
            () => queenHistoryRepository.GetAllPublishedAsync(cancellationToken),
            cancellationToken);
    }

    /// <summary>
    /// Caches the published quote pool and picks <see cref="Random.Shared"/> per request
    /// so consecutive callers do not freeze on one quote.
    /// </summary>
    public async Task<QuoteItem?> GetRandomPublishedQuoteAsync(CancellationToken cancellationToken = default)
    {
        var published = await GetPublishedQuotesAsync(cancellationToken);
        if (published.Count == 0)
        {
            return null;
        }

        return published[Random.Shared.Next(published.Count)];
    }

    /// <summary>
    /// Up to <paramref name="count"/> distinct published quotes in random order, drawn from the
    /// same cached pool as <see cref="GetRandomPublishedQuoteAsync"/>.
    /// </summary>
    public async Task<IReadOnlyList<QuoteItem>> GetRandomPublishedQuotesAsync(
        int count,
        CancellationToken cancellationToken = default)
    {
        var published = await GetPublishedQuotesAsync(cancellationToken);
        var shuffled = published.ToArray();
        Random.Shared.Shuffle(shuffled);
        return shuffled.Take(Math.Max(count, 0)).ToList();
    }

    /// <summary>
    /// Caches the published trivia pool and picks <see cref="Random.Shared"/> per request
    /// so consecutive callers do not freeze on one fact.
    /// </summary>
    public async Task<TriviaFactItem?> GetRandomPublishedTriviaAsync(CancellationToken cancellationToken = default)
    {
        var published = await GetPublishedTriviaAsync(cancellationToken);
        if (published.Count == 0)
        {
            return null;
        }

        return published[Random.Shared.Next(published.Count)];
    }

    public Task<IReadOnlyList<BiographyChapterItem>> GetBiographyChaptersAsync(
        CancellationToken cancellationToken = default) =>
        store.GetOrCreateAsync(
            PublicQueryCacheKeys.BiographyChapters,
            options.Value.CatalogCacheDuration,
            () => biographyRepository.GetChaptersAsync(cancellationToken),
            cancellationToken);

    public Task<IReadOnlyList<AlbumSummary>> GetDiscographyAlbumsAsync(
        CancellationToken cancellationToken = default) =>
        store.GetOrCreateAsync(
            PublicQueryCacheKeys.DiscographyAlbums(store.GetDiscographyCacheVersion()),
            options.Value.CatalogCacheDuration,
            () => discographyRepository.GetAlbumsAsync(cancellationToken),
            cancellationToken);

    // The album template renders notes and lyrics for every track, including collapsed details.
    // Cache the complete archive read so repeat views do not fetch every track LOB again.

    public Task<AlbumDetail?> GetDiscographyAlbumByIdAsync(
        int albumId,
        CancellationToken cancellationToken = default) =>
        store.GetOrCreateAsync(
            PublicQueryCacheKeys.DiscographyAlbum(store.GetDiscographyCacheVersion(), albumId),
            options.Value.CatalogCacheDuration,
            () => discographyRepository.GetAlbumByIdAsync(albumId, cancellationToken),
            cancellationToken);

    public Task<IReadOnlyList<SongSummary>> GetSongsAsync(
        CancellationToken cancellationToken = default) =>
        store.GetOrCreateAsync(
            PublicQueryCacheKeys.Songs(store.GetDiscographyCacheVersion()),
            options.Value.CatalogCacheDuration,
            () => discographyRepository.GetSongsAsync(cancellationToken),
            cancellationToken);

    public Task<SongDetail?> GetSongBySlugAsync(
        string slug,
        CancellationToken cancellationToken = default) =>
        store.GetOrCreateAsync(
            PublicQueryCacheKeys.Song(store.GetDiscographyCacheVersion(), slug.Trim().ToLowerInvariant()),
            options.Value.CatalogCacheDuration,
            () => discographyRepository.GetSongBySlugAsync(slug, cancellationToken),
            cancellationToken);

    /// <summary>
    /// Caches one visible tribute id for a few minutes. A miss seeks an indexed id range;
    /// a hit loads that id. Neither path sorts <c>FREDDIE_T</c> with <c>NEWID()</c>.
    /// </summary>
    public async Task<FreddieTribute?> GetFeaturedFreddieTributeAsync(CancellationToken cancellationToken = default)
    {
        var pick = await store.GetOrCreateAsync(
            PublicQueryCacheKeys.FreddieFeaturedTributeId,
            options.Value.FreddieSampleCacheDuration,
            async () =>
            {
                var id = await freddieTributeRepository.PickRandomVisibleIdAsync(cancellationToken);
                return new CachedId(id);
            },
            cancellationToken);
        if (pick.Id is not int idValue)
        {
            return null;
        }

        return await freddieTributeRepository.GetVisibleByIdAsync(idValue, cancellationToken);
    }

    public void InvalidateQuotesCache() => cache.Remove(PublicQueryCacheKeys.PublishedQuotes);

    public void InvalidateTriviaCache() => cache.Remove(PublicQueryCacheKeys.PublishedTrivia);

    public void InvalidateBiographyCache() => cache.Remove(PublicQueryCacheKeys.BiographyChapters);

    /// <summary>
    /// Bumps the discography version so album lists, album details, and song pages all
    /// reload after an admin edit. Old versioned entries age out on their TTL.
    /// </summary>
    public void InvalidateDiscographyCache() =>
        cache.Set(PublicQueryCacheKeys.DiscographyVersion, PublicQueryCacheStore.CreateCacheVersion(), PublicQueryCacheStore.VersionEntryOptions);

    public void InvalidateHistoryCache()
    {
        cache.Set(PublicQueryCacheKeys.HistoryVersion, PublicQueryCacheStore.CreateCacheVersion(), PublicQueryCacheStore.VersionEntryOptions);
    }

    private Task<IReadOnlyList<QuoteItem>> GetPublishedQuotesAsync(CancellationToken cancellationToken) =>
        store.GetOrCreateAsync(
            PublicQueryCacheKeys.PublishedQuotes,
            options.Value.CatalogCacheDuration,
            async () =>
            {
                var all = await quoteRepository.GetAllAsync(cancellationToken);
                IReadOnlyList<QuoteItem> published = all.Where(quote => quote.IsPublished).ToList();
                return published;
            },
            cancellationToken);

    private Task<IReadOnlyList<TriviaFactItem>> GetPublishedTriviaAsync(CancellationToken cancellationToken) =>
        store.GetOrCreateAsync(
            PublicQueryCacheKeys.PublishedTrivia,
            options.Value.CatalogCacheDuration,
            async () =>
            {
                var all = await triviaRepository.GetAllAsync(cancellationToken);
                IReadOnlyList<TriviaFactItem> published = all.Where(fact => fact.IsPublished).ToList();
                return published;
            },
            cancellationToken);

    private sealed record CachedId(int? Id);

}
