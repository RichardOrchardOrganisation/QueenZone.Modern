using QueenZone.Data;

namespace QueenZone.Web.Sitemap;

public sealed class CoreSitemapBuilder(
    INewsRepository newsRepository,
    IArticlesRepository articlesRepository,
    IArticleRepository communityArticleRepository,
    IBiographyRepository biographyRepository,
    IForumRepository forumRepository,
    IPhotoRepository photoRepository,
    IFanPerformanceRepository fanPerformanceRepository,
    IDiscographyRepository discographyRepository)
{
    public Task<IReadOnlyList<SitemapEntry>> BuildAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<SitemapEntry>>(
            [
                new("/"),
                new("/about"),
                new("/contact"),
                new("/links"),
                new("/mobile-apps")
            ]);

    public async Task<IReadOnlyList<SitemapEntry>?> BuildSectionAsync(
        string section,
        CancellationToken cancellationToken = default)
    {
        var entries = section switch
        {
            SitemapSections.News => new List<SitemapEntry>(),
            SitemapSections.Articles => [],
            SitemapSections.Biography => [],
            SitemapSections.ForumCategories => [],
            SitemapSections.Photography => [],
            SitemapSections.FanPerformances => [],
            SitemapSections.Discography => [],
            _ => null
        };

        if (entries is null)
        {
            return null;
        }

        switch (section)
        {
            case SitemapSections.News:
                await AddNewsEntriesAsync(entries, cancellationToken);
                break;
            case SitemapSections.Articles:
                await AddArticleEntriesAsync(entries, cancellationToken);
                await AddCommunityArticleEntriesAsync(entries, cancellationToken);
                break;
            case SitemapSections.Biography:
                await AddBiographyEntriesAsync(entries, cancellationToken);
                break;
            case SitemapSections.ForumCategories:
                await AddForumEntriesAsync(entries, cancellationToken);
                break;
            case SitemapSections.Photography:
                await AddPhotographyEntriesAsync(entries, cancellationToken);
                break;
            case SitemapSections.FanPerformances:
                await AddFanPerformanceEntriesAsync(entries, cancellationToken);
                break;
            case SitemapSections.Discography:
                await AddDiscographyEntriesAsync(entries, cancellationToken);
                break;
        }

        return entries;
    }

    private async Task AddNewsEntriesAsync(List<SitemapEntry> entries, CancellationToken cancellationToken)
    {
        var publishedCount = await newsRepository.GetPublishedCountAsync(cancellationToken: cancellationToken);
        var totalPages = NewsRoutes.GetArchiveTotalPages(publishedCount);
        for (var page = 1; page <= totalPages; page++)
        {
            entries.Add(new(NewsRoutes.GetArchiveCanonicalPath(page)));
        }

        var newsItems = await newsRepository.GetPublishedSitemapEntriesAsync(cancellationToken);
        foreach (var item in newsItems)
        {
            entries.Add(new(
                NewsArticleContent.GetDetailCanonicalPath(item.Id, item.Title, item.Slug),
                item.PublishedAt));
        }
    }

    private async Task AddArticleEntriesAsync(List<SitemapEntry> entries, CancellationToken cancellationToken)
    {
        var publishedCount = await articlesRepository.GetPublishedCountAsync(cancellationToken);
        var totalPages = ArticlesRoutes.GetArchiveTotalPages(publishedCount);
        for (var page = 1; page <= totalPages; page++)
        {
            entries.Add(new(ArticlesRoutes.GetArchiveCanonicalPath(page)));
        }

        var articleItems = await articlesRepository.GetPublishedSitemapEntriesAsync(cancellationToken);
        foreach (var item in articleItems)
        {
            entries.Add(new(
                ArticlesRoutes.GetArticleDetailPath(item.Id, item.Title),
                item.PublishedAt));
        }
    }

    private async Task AddCommunityArticleEntriesAsync(List<SitemapEntry> entries, CancellationToken cancellationToken)
    {
        var articles = await communityArticleRepository.GetSitemapEntriesAsync(cancellationToken);
        foreach (var article in articles)
        {
            entries.Add(new(ArticlesRoutes.GetCommunityArticleDetailPath(article.Slug), article.PublishedAt.UtcDateTime));
        }
    }

    private async Task AddBiographyEntriesAsync(List<SitemapEntry> entries, CancellationToken cancellationToken)
    {
        entries.Add(new(BiographyRoutes.IndexPath));

        var chapters = await biographyRepository.GetChaptersAsync(cancellationToken);
        foreach (var chapter in chapters)
        {
            entries.Add(new(
                BiographyRoutes.GetChapterDetailPath(chapter),
                chapter.CreatedAt == DateTime.MinValue ? null : chapter.CreatedAt));
        }
    }

    private async Task AddForumEntriesAsync(List<SitemapEntry> entries, CancellationToken cancellationToken)
    {
        entries.Add(new("/forum"));

        var categories = await forumRepository.GetCategoriesAsync(cancellationToken);
        foreach (var category in categories)
        {
            entries.Add(new(
                ForumRoutes.GetCategoryCanonicalPath(category.Id, category.Name),
                category.LastActivityAt));
        }
    }

    private async Task AddPhotographyEntriesAsync(List<SitemapEntry> entries, CancellationToken cancellationToken)
    {
        entries.Add(new(PhotoRoutes.GetCategoriesPath()));

        var categories = await photoRepository.GetPublishedSitemapCategoriesAsync(cancellationToken);
        foreach (var category in categories)
        {
            var latestPhotoAt = category.Photos.Count > 0
                ? category.Photos.Max(photo => photo.DateTime)
                : (DateTime?)null;

            entries.Add(new(PhotoRoutes.GetCategoryPath(category.Slug), latestPhotoAt));

            var totalPages = PhotoRoutes.GetCategoryTotalPages(category.Photos.Count);
            for (var page = 2; page <= totalPages; page++)
            {
                entries.Add(new(PhotoRoutes.GetCategoryPagePath(category.Slug, page), latestPhotoAt));
            }

            foreach (var photo in category.Photos)
            {
                entries.Add(new(
                    PhotoRoutes.GetDetailPath(category.Slug, photo.PicId),
                    photo.DateTime));
            }
        }
    }

    private async Task AddFanPerformanceEntriesAsync(List<SitemapEntry> entries, CancellationToken cancellationToken)
    {
        var visibleCount = await fanPerformanceRepository.GetVisibleCountAsync(cancellationToken);
        var totalPages = FanPerformanceRoutes.GetTotalPages(visibleCount);

        for (var page = 1; page <= totalPages; page++)
        {
            entries.Add(new(FanPerformanceRoutes.GetPagePath(page)));
        }

        if (totalPages == 0)
        {
            entries.Add(new(FanPerformanceRoutes.GetIndexPath()));
        }
    }

    private async Task AddDiscographyEntriesAsync(List<SitemapEntry> entries, CancellationToken cancellationToken)
    {
        entries.Add(new(DiscographyRoutes.GetIndexPath()));

        var albums = await discographyRepository.GetAlbumsAsync(cancellationToken);
        foreach (var album in albums)
        {
            entries.Add(new(DiscographyRoutes.GetAlbumPath(album)));
        }
    }
}

