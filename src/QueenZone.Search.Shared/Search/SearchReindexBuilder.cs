using QueenZone.Data;
using QueenZone.Data.Entities;
using QueenZone.Routing;

namespace QueenZone.Search.Shared;

/// <summary>
/// Repeatable batch job that rebuilds <c>SearchDocument</c> rows for one or more content types by
/// reading the same public repositories the archive pages already use — so the visibility rules
/// applied there (published/display flags) are exactly the rules search inherits. Lives in
/// QueenZone.Search.Shared (not QueenZone.Data) because it needs the same canonical-URL route helpers
/// (<see cref="NewsRoutes"/>, <see cref="ForumRoutes"/>, <see cref="ArticlesRoutes"/>) that
/// the public sitemap builders use for the same reason.
/// </summary>
/// <remarks>
/// Covers News, Forum threads, Community Articles, legacy Articles, Biography, Discography,
/// Songs, Timeline, and Fan Performances. Photography is not yet wired in — it needs the same
/// three-step treatment (paginate the existing public repository, map to
/// <see cref="SearchDocumentEntity"/>, call <see cref="ISearchIndexService.ReplaceContentTypeAsync"/>)
/// and can be added as an additional <c>Reindex*Async</c> method following the pattern below.
/// Freddie Tribute content is deliberately not indexed — <see cref="SiteSearchExclusion"/>
/// excludes tribute content types (including <c>freddie-tribute</c>) and tribute source-key
/// prefixes from <c>SearchDocument</c> writes, leftover cleanup, and
/// <c>dbo.SearchDocument_Search</c>.
/// </remarks>
public sealed class SearchReindexBuilder(
    ISearchIndexService searchIndexService,
    INewsRepository newsRepository,
    IForumRepository forumRepository,
    IArticleRepository articleRepository,
    IArticlesRepository articlesRepository,
    IBiographyRepository biographyRepository,
    IDiscographyRepository discographyRepository,
    IQueenHistoryRepository queenHistoryRepository,
    IFanPerformanceRepository fanPerformanceRepository)
{
    private const int BatchSize = 200;

    /// <param name="onContentTypeStarted">
    /// Optional progress callback with the <see cref="SiteSearchContentType"/> key about to be replaced.
    /// Used by the admin background job UI; ignored by startup seed and unit tests.
    /// </param>
    public async Task ReindexAllAsync(
        CancellationToken cancellationToken = default,
        Action<string>? onContentTypeStarted = null)
    {
        await RunContentTypeAsync(SiteSearchContentType.News, () => ReindexNewsAsync(cancellationToken), onContentTypeStarted);
        await RunContentTypeAsync(SiteSearchContentType.Forum, () => ReindexForumAsync(cancellationToken), onContentTypeStarted);
        await RunContentTypeAsync(SiteSearchContentType.Article, () => ReindexArticlesAsync(cancellationToken), onContentTypeStarted);
        await RunContentTypeAsync(SiteSearchContentType.LegacyArticle, () => ReindexLegacyArticlesAsync(cancellationToken), onContentTypeStarted);
        await RunContentTypeAsync(SiteSearchContentType.Biography, () => ReindexBiographyAsync(cancellationToken), onContentTypeStarted);
        await RunContentTypeAsync(SiteSearchContentType.Discography, () => ReindexDiscographyAsync(cancellationToken), onContentTypeStarted);
        await RunContentTypeAsync(SiteSearchContentType.Song, () => ReindexSongsAsync(cancellationToken), onContentTypeStarted);
        await RunContentTypeAsync(SiteSearchContentType.Timeline, () => ReindexTimelineAsync(cancellationToken), onContentTypeStarted);
        await RunContentTypeAsync(SiteSearchContentType.FanPerformance, () => ReindexFanPerformancesAsync(cancellationToken), onContentTypeStarted);
        await searchIndexService.ReplaceContentTypeAsync(SiteSearchContentType.Tribute, [], cancellationToken);
    }

    private static async Task RunContentTypeAsync(
        string contentType,
        Func<Task> reindex,
        Action<string>? onContentTypeStarted)
    {
        onContentTypeStarted?.Invoke(contentType);
        await reindex();
    }

    public async Task ReindexNewsAsync(CancellationToken cancellationToken = default)
    {
        var totalCount = await newsRepository.GetPublishedCountAsync(cancellationToken: cancellationToken);
        var documents = new List<SearchDocumentEntity>();

        for (var page = 1; (page - 1) * BatchSize < totalCount; page++)
        {
            var items = await newsRepository.GetArchivePageAsync(page, BatchSize, cancellationToken: cancellationToken);
            documents.AddRange(items.Select(MapNews));
        }

        await searchIndexService.ReplaceContentTypeAsync(SiteSearchContentType.News, documents, cancellationToken);
    }

    public async Task ReindexForumAsync(CancellationToken cancellationToken = default)
    {
        var totalCount = await forumRepository.GetTopicSitemapCountAsync(cancellationToken);
        var documents = new List<SearchDocumentEntity>();

        for (var offset = 0; offset < totalCount; offset += BatchSize)
        {
            var items = await forumRepository.GetTopicSitemapPageAsync(offset, BatchSize, cancellationToken);
            documents.AddRange(items.Select(MapForumThread));
        }

        await searchIndexService.ReplaceContentTypeAsync(SiteSearchContentType.Forum, documents, cancellationToken);
    }

    public async Task ReindexArticlesAsync(CancellationToken cancellationToken = default)
    {
        var totalCount = await articleRepository.GetCountAsync(ct: cancellationToken);
        var documents = new List<SearchDocumentEntity>();

        for (var page = 1; (page - 1) * BatchSize < totalCount; page++)
        {
            var items = await articleRepository.GetPageAsync(page, BatchSize, ct: cancellationToken);
            documents.AddRange(items.Select(MapArticle));
        }

        await searchIndexService.ReplaceContentTypeAsync(SiteSearchContentType.Article, documents, cancellationToken);
    }

    public async Task ReindexLegacyArticlesAsync(CancellationToken cancellationToken = default)
    {
        var totalCount = await articlesRepository.GetPublishedCountAsync(cancellationToken);
        var documents = new List<SearchDocumentEntity>();

        for (var page = 1; (page - 1) * BatchSize < totalCount; page++)
        {
            var items = await articlesRepository.GetArchivePageAsync(page, BatchSize, cancellationToken);
            documents.AddRange(items.Select(MapLegacyArticle));
        }

        await searchIndexService.ReplaceContentTypeAsync(SiteSearchContentType.LegacyArticle, documents, cancellationToken);
    }

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
            documents.AddRange(items.Select(MapFanPerformance));
        }

        await searchIndexService.ReplaceContentTypeAsync(SiteSearchContentType.FanPerformance, documents, cancellationToken);
    }

    /// <summary>
    /// Maps one published legacy archive article to its <see cref="SearchDocumentEntity"/> shape.
    /// Exposed so admin publish/unpublish handlers can upsert a single document immediately rather
    /// than waiting for the next full reindex — see <c>Admin/Articles/Status.cshtml.cs</c>.
    /// </summary>
    public static SearchDocumentEntity MapLegacyArticle(ArticleItem item)
    {
        var plainBody = SearchDocumentText.ToPlainText(item.Body);
        return new SearchDocumentEntity
        {
            SourceKey = $"legacy-article:{item.Id}",
            ContentType = SiteSearchContentType.LegacyArticle,
            Title = item.Title,
            Body = plainBody,
            Summary = SearchDocumentText.Summarize(
                string.IsNullOrWhiteSpace(item.Excerpt) ? plainBody : item.Excerpt),
            Url = ArticlesRoutes.GetArticleDetailPath(item.Id, item.Title),
            PublishedAt = item.PublishedAt,
            Category = item.CategoryName,
        };
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

    /// <summary>Stable search index identity for a published fan performance, e.g. <c>fan-performance:187</c>.</summary>
    public static string FanPerformanceSourceKey(int id) => $"fan-performance:{id}";

    /// <summary>
    /// Maps one visible fan performance to its <see cref="SearchDocumentEntity"/> shape. Exposed so
    /// admin publish/update/hide handlers can upsert or remove a single document immediately rather
    /// than waiting for the next full reindex.
    /// </summary>
    public static SearchDocumentEntity MapFanPerformance(FanPerformance performance) =>
        new()
        {
            SourceKey = FanPerformanceSourceKey(performance.Id),
            ContentType = SiteSearchContentType.FanPerformance,
            Title = performance.Title,
            Body = performance.Description,
            Summary = SearchDocumentText.Summarize(performance.Description),
            Url = FanPerformanceRoutes.GetIndexPath(),
            PublishedAt = performance.DateAdded,
            AuthorDisplayName = performance.PerformedBy,
        };

    /// <summary>Stable search index identity for a modern forum thread, e.g. <c>forum-thread:4521</c>.</summary>
    public static string ForumThreadSourceKey(int topicId) => $"forum-thread:{topicId}";

    /// <summary>
    /// Maps one published news item to its <see cref="SearchDocumentEntity"/> shape. Exposed so
    /// admin publish/unpublish handlers can upsert a single document immediately rather than
    /// waiting for the next full reindex — see <c>Admin/News/Action.cshtml.cs</c>.
    /// </summary>
    public static SearchDocumentEntity MapNews(NewsItem item)
    {
        var plainBody = SearchDocumentText.ToPlainText(item.Body);
        return new SearchDocumentEntity
        {
            SourceKey = $"news:{item.Id}",
            ContentType = SiteSearchContentType.News,
            Title = item.Title,
            Body = plainBody,
            Summary = SearchDocumentText.Summarize(
                string.IsNullOrWhiteSpace(item.Excerpt) ? plainBody : item.Excerpt),
            Url = NewsRoutes.GetNewsDetailPath(item),
            PublishedAt = item.PublishedAt,
        };
    }

    /// <summary>
    /// Maps one public forum thread to its title-oriented <see cref="SearchDocumentEntity"/> shape.
    /// Exposed so create/reply handlers can upsert a single document immediately rather than
    /// waiting for the next full reindex — see <c>ForumSearchIndexSynchronizer</c>.
    /// </summary>
    public static SearchDocumentEntity MapForumThread(ForumTopicSitemapItem item) =>
        new()
        {
            SourceKey = ForumThreadSourceKey(item.TopicId),
            ContentType = SiteSearchContentType.Forum,
            Title = item.Title,
            // Sitemap projection only carries the thread title, not post bodies — full post
            // text would need a dedicated bulk read from ModernForumPost; title-only indexing
            // is a deliberate scope trade-off for the initial reindex source (see remarks above).
            Body = item.Title,
            Summary = item.Title,
            Url = ForumRoutes.GetTopicCanonicalPath(item.TopicId, item.Title),
            PublishedAt = item.LastActivityAt,
        };

    /// <summary>
    /// Maps one published community article to its <see cref="SearchDocumentEntity"/> shape.
    /// Exposed for the same immediate-upsert reason as <see cref="MapNews"/> — see
    /// <c>Admin/Articles/Action.cshtml.cs</c>.
    /// </summary>
    public static SearchDocumentEntity MapArticle(PublishedArticleSubmission item)
    {
        var plainBody = SearchDocumentText.ToPlainText(item.Body);
        return new SearchDocumentEntity
        {
            SourceKey = $"article:{item.Slug}",
            ContentType = SiteSearchContentType.Article,
            Title = item.Title,
            Body = plainBody,
            Summary = SearchDocumentText.Summarize(
                string.IsNullOrWhiteSpace(item.Excerpt) ? plainBody : item.Excerpt),
            Url = ArticlesRoutes.GetCommunityArticleDetailPath(item.Slug),
            PublishedAt = item.PublishedAt,
            AuthorDisplayName = item.AuthorDisplayName,
        };
    }
}
