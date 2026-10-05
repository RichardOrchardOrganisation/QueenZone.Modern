using QueenZone.Data;
using QueenZone.Data.Entities;
using QueenZone.Routing;

namespace QueenZone.Search.Shared;

public sealed class CatalogSearchReindexer(
    ISearchIndexService searchIndexService,
    IBiographyRepository biographyRepository,
    IDiscographyRepository discographyRepository,
    IQueenHistoryRepository queenHistoryRepository,
    IFanPerformanceRepository fanPerformanceRepository)
{
    private const int BatchSize = 200;

    public async Task ReindexBiographyAsync(CancellationToken cancellationToken = default)
    {
        var listedChapters = await biographyRepository.GetChaptersAsync(cancellationToken);
        var documents = new List<SearchDocumentEntity>(listedChapters.Count);

        // The public list projection (legacy Q_BIO list SP / EfBiographyRepository.MapListRow)
        // leaves Body empty. Detail reads carry BIO_TEXT, which is what members search.
        foreach (var listed in listedChapters)
        {
            var detail = await biographyRepository.GetByIdAsync(listed.Id, cancellationToken);
            var chapter = detail is null || string.IsNullOrWhiteSpace(detail.Body)
                ? listed
                : listed with { Body = detail.Body };
            documents.Add(MapBiographyChapter(chapter));
        }

        await searchIndexService.ReplaceContentTypeAsync(SiteSearchContentType.Biography, documents, cancellationToken);
    }

    public async Task ReindexDiscographyAsync(CancellationToken cancellationToken = default)
    {
        var albums = await discographyRepository.GetAlbumsAsync(cancellationToken);
        var documents = new List<SearchDocumentEntity>(albums.Count);

        // Album list rows are name-only. Track titles live on the album detail so a song
        // query can hit the album document without adding a second content type.
        foreach (var album in albums)
        {
            var detail = await discographyRepository.GetAlbumByIdAsync(album.AlbumId, cancellationToken);
            documents.Add(MapAlbum(album, detail));
        }

        await searchIndexService.ReplaceContentTypeAsync(SiteSearchContentType.Discography, documents, cancellationToken);
    }

    public async Task ReindexSongsAsync(CancellationToken cancellationToken = default)
    {
        var songs = await discographyRepository.GetSongsAsync(cancellationToken);
        var documents = songs.Select(MapSong).ToList();
        await searchIndexService.ReplaceContentTypeAsync(SiteSearchContentType.Song, documents, cancellationToken);
    }

    public async Task ReindexTimelineAsync(CancellationToken cancellationToken = default)
    {
        var events = await queenHistoryRepository.GetAllPublishedAsync(cancellationToken);
        var documents = events.Select(MapTimelineEvent).ToList();

        await searchIndexService.ReplaceContentTypeAsync(SiteSearchContentType.Timeline, documents, cancellationToken);
    }

    public async Task ReindexFanPerformancesAsync(CancellationToken cancellationToken = default)
    {
        var totalCount = await fanPerformanceRepository.GetVisibleCountAsync(cancellationToken);
        var documents = new List<SearchDocumentEntity>();

        for (var page = 1; (page - 1) * BatchSize < totalCount; page++)
        {
            var items = await fanPerformanceRepository.GetPageAsync(page, BatchSize, cancellationToken);
            documents.AddRange(items.Select(SearchReindexBuilder.MapFanPerformance));
        }

        await searchIndexService.ReplaceContentTypeAsync(SiteSearchContentType.FanPerformance, documents, cancellationToken);
    }

    private static SearchDocumentEntity MapBiographyChapter(BiographyChapterItem chapter)
    {
        var plainBody = SearchDocumentText.ToPlainText(chapter.Body);
        return new SearchDocumentEntity
        {
            SourceKey = $"biography:{chapter.Id}",
            ContentType = SiteSearchContentType.Biography,
            Title = chapter.Title,
            Body = plainBody,
            Summary = SearchDocumentText.Summarize(
                string.IsNullOrWhiteSpace(chapter.Summary) ? plainBody : chapter.Summary),
            Url = BiographyRoutes.GetChapterDetailPath(chapter),
            PublishedAt = chapter.CreatedAt == DateTime.MinValue ? null : chapter.CreatedAt,
        };
    }

    private static SearchDocumentEntity MapAlbum(AlbumSummary album, AlbumDetail? detail)
    {
        var trackTitles = detail?.Songs
            .Select(song => song.Title)
            .Where(title => !string.IsNullOrWhiteSpace(title))
            ?? [];
        var body = string.Join('\n', new[] { album.Name }.Concat(trackTitles));

        return new SearchDocumentEntity
        {
            SourceKey = $"discography:{album.AlbumId}",
            ContentType = SiteSearchContentType.Discography,
            Title = album.Name,
            Body = body,
            Summary = album.Name,
            Url = DiscographyRoutes.GetAlbumPath(album),
            PublishedAt = album.ReleaseYear.HasValue
                ? new DateTimeOffset(album.ReleaseYear.Value, 1, 1, 0, 0, 0, TimeSpan.Zero)
                : null,
        };
    }

    private static SearchDocumentEntity MapSong(SongSummary song)
    {
        var body = string.Join('\n', new[] { song.Title }.Concat(song.AlbumNames));
        return new SearchDocumentEntity
        {
            SourceKey = SearchDocumentSourceKey.ForSong(song.Slug),
            ContentType = SiteSearchContentType.Song,
            Title = song.Title,
            Body = body,
            Summary = song.Title,
            Url = SongRoutes.GetSongPath(song.Slug),
            PublishedAt = song.EarliestReleaseYear.HasValue
                ? new DateTimeOffset(song.EarliestReleaseYear.Value, 1, 1, 0, 0, 0, TimeSpan.Zero)
                : null,
        };
    }

    private static SearchDocumentEntity MapTimelineEvent(QueenHistoryEvent historyEvent) =>
        new()
        {
            SourceKey = $"timeline:{historyEvent.Id}",
            ContentType = SiteSearchContentType.Timeline,
            Title = historyEvent.Title,
            Body = historyEvent.Summary,
            Summary = SearchDocumentText.Summarize(historyEvent.Summary),
            Url = $"/timeline#event-{historyEvent.Id}",
            PublishedAt = historyEvent.EventDate,
            Category = historyEvent.Category.ToString(),
        };

}
