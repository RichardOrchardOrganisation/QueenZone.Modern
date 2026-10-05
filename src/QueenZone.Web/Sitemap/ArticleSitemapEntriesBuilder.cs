using Microsoft.Data.SqlClient;
using QueenZone.Data;

namespace QueenZone.Web.Sitemap;

public sealed class ArticleSitemapEntriesBuilder(
    IArticlesRepository articlesRepository,
    IArticleRepository communityArticleRepository)
{
    public async Task AddEntriesAsync(List<SitemapEntry> entries, CancellationToken cancellationToken)
    {
        await AddArticleEntriesAsync(entries, cancellationToken);
        await AddCommunityArticleEntriesAsync(entries, cancellationToken);
    }

    private async Task AddArticleEntriesAsync(List<SitemapEntry> entries, CancellationToken cancellationToken)
    {
        var archiveKeys = await articlesRepository.GetPublishedFeedKeysAsync(cancellationToken);
        IReadOnlyList<ArticleFeedKey> communityKeys;
        try
        {
            communityKeys = await communityArticleRepository.GetPublishedFeedKeysAsync(null, cancellationToken);
        }
        catch (SqlException)
        {
            communityKeys = [];
        }

        var totalPages = ArticlesRoutes.GetArchiveTotalPages(archiveKeys.Count + communityKeys.Count);
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

}
