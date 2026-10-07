using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.Caching.Memory;
using QueenZone.Data;
using QueenZone.Data.Entities;
using QueenZone.Web.Pages.Songs;

namespace QueenZone.Web.Tests;

public sealed class SongPageQueryCountTests
{
    [Fact]
    public async Task Song_page_loads_catalogue_once_per_discography_version()
    {
        using var memoryCache = new MemoryCache(new MemoryCacheOptions());
        var discography = new CountingCatalogDiscographyRepository();
        var search = new CountingSearchIndexService
        {
            Documents =
            [
                new SearchDocumentEntity
                {
                    ContentType = SiteSearchContentType.News,
                    Title = "Bohemian Rhapsody",
                    Url = "/news/1003/queenzone-modernisation-begins",
                },
            ],
        };
        var cache = PublicQueryCacheServiceTests.CreateService(
            memoryCache,
            discographyRepository: discography);

        var first = await GetAsync(cache, search, "bohemian-rhapsody");
        Assert.IsType<PageResult>(first.Result);
        Assert.Equal("Bohemian Rhapsody", first.Model.Song.Title);
        Assert.Contains(
            first.Model.Related.SelectMany(section => section.Links),
            link => link.Url == "/news/1003/queenzone-modernisation-begins");
        Assert.True(discography.CatalogueLoads <= 1);
        Assert.Equal(1, search.ExactTitleLookups);
        Assert.Equal(0, discography.AlbumDetailLoads);

        var second = await GetAsync(cache, search, "keep-yourself-alive");
        Assert.IsType<PageResult>(second.Result);
        Assert.Equal("Keep Yourself Alive", second.Model.Song.Title);
        Assert.Equal(1, discography.CatalogueLoads);
        Assert.Equal(2, search.ExactTitleLookups);

        var missing = await GetAsync(cache, search, "not-a-queen-song");
        var missingAgain = await GetAsync(cache, search, "not-a-queen-song");
        Assert.IsType<NotFoundResult>(missing.Result);
        Assert.IsType<NotFoundResult>(missingAgain.Result);
        Assert.Equal(1, discography.CatalogueLoads);
        Assert.Equal(2, search.ExactTitleLookups);
    }

    [Fact]
    public async Task Content_song_detail_api_uses_the_same_catalogue_cache()
    {
        using var memoryCache = new MemoryCache(new MemoryCacheOptions());
        var discography = new CountingCatalogDiscographyRepository();
        var search = new CountingSearchIndexService();
        var cache = PublicQueryCacheServiceTests.CreateService(
            memoryCache,
            discographyRepository: discography);

        var first = await ContentSongsApiEndpoints.GetSongDetailAsync(
            cache,
            search,
            "seven-seas-of-rhye",
            CancellationToken.None);
        var second = await ContentSongsApiEndpoints.GetSongDetailAsync(
            cache,
            search,
            "bohemian-rhapsody",
            CancellationToken.None);
        var missing = await ContentSongsApiEndpoints.GetSongDetailAsync(
            cache,
            search,
            "not-a-queen-song",
            CancellationToken.None);
        var missingAgain = await ContentSongsApiEndpoints.GetSongDetailAsync(
            cache,
            search,
            "not-a-queen-song",
            CancellationToken.None);

        Assert.IsAssignableFrom<IStatusCodeHttpResult>(first);
        Assert.Equal(200, ((IStatusCodeHttpResult)first).StatusCode);
        Assert.Equal(200, ((IStatusCodeHttpResult)second).StatusCode);
        Assert.Equal(404, ((IStatusCodeHttpResult)missing).StatusCode);
        Assert.Equal(404, ((IStatusCodeHttpResult)missingAgain).StatusCode);
        Assert.Equal(1, discography.CatalogueLoads);
        Assert.Equal(2, search.ExactTitleLookups);
    }

    private static async Task<(IActionResult Result, DetailModel Model)> GetAsync(
        PublicQueryCacheService cache,
        ISearchIndexService search,
        string slug)
    {
        var model = new DetailModel(cache, search)
        {
            PageContext = new PageContext
            {
                ViewData = new ViewDataDictionary(new EmptyModelMetadataProvider(), new ModelStateDictionary()),
            },
        };
        var result = await model.OnGetAsync(slug, CancellationToken.None);
        return (result, model);
    }

    private sealed class CountingCatalogDiscographyRepository : IDiscographyRepository
    {
        private readonly InMemoryDiscographyRepository inner = new(SampleDiscographyData.CreateSeedAlbums());

        public int CatalogueLoads { get; private set; }

        public int AlbumDetailLoads { get; private set; }

        public Task<IReadOnlyList<AlbumSummary>> GetAlbumsAsync(CancellationToken cancellationToken = default) =>
            inner.GetAlbumsAsync(cancellationToken);

        public Task<AlbumDetail?> GetAlbumByIdAsync(int albumId, CancellationToken cancellationToken = default)
        {
            AlbumDetailLoads++;
            return inner.GetAlbumByIdAsync(albumId, cancellationToken);
        }

        public Task<IReadOnlyList<SongTrackSource>> GetActiveAlbumTracksAsync(
            CancellationToken cancellationToken = default)
        {
            CatalogueLoads++;
            return inner.GetActiveAlbumTracksAsync(cancellationToken);
        }

        public Task<IReadOnlyList<SongSummary>> GetSongsAsync(CancellationToken cancellationToken = default) =>
            inner.GetSongsAsync(cancellationToken);

        public Task<SongDetail?> GetSongBySlugAsync(string slug, CancellationToken cancellationToken = default) =>
            inner.GetSongBySlugAsync(slug, cancellationToken);
    }

    private sealed class CountingSearchIndexService : ISearchIndexService
    {
        public int ExactTitleLookups { get; private set; }

        public IReadOnlyList<SearchDocumentEntity> Documents { get; init; } = [];

        public Task UpsertAsync(SearchDocumentEntity document, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task RemoveAsync(string sourceKey, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task ReplaceContentTypeAsync(
            string contentType,
            IReadOnlyList<SearchDocumentEntity> documents,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<IReadOnlyDictionary<string, int>> GetContentTypeCountsAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyDictionary<string, int>>(new Dictionary<string, int>());

        public Task<IReadOnlyList<SearchDocumentEntity>> FindByExactTitleAsync(
            string title,
            CancellationToken cancellationToken = default)
        {
            ExactTitleLookups++;
            return Task.FromResult(Documents);
        }
    }
}
