using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;
using QueenZone.Data;
using QueenZone.Web.Search;
using QueenZone.Web.Sitemap;

namespace QueenZone.Web.Pages.Admin.Articles;

public sealed class StatusModel(
    IEditorialArticleRepository articles,
    IArticleSubmissionRepository submissions,
    IArticleRepository articleRepository,
    IArticlesRepository legacyArticles,
    ISearchIndexService searchIndexService,
    PublicQueryCacheService cache,
    IOutputCacheStore outputCache,
    CoreSitemapService sitemap,
    NewsArticleImageService imageService,
    ILogger<StatusModel> logger) : AdminArticlesPageModel
{
    public async Task<IActionResult> OnPostAsync(Guid id, string status, CancellationToken ct)
    {
        if (status is not (EditorialArticleStatus.Published or EditorialArticleStatus.Unpublished)) return BadRequest();
        var existing = await articles.GetAsync(id, ct);
        if (existing is null) return NotFound();
        var liveStandalone = existing.LegacyArticleId is null
            ? (await articles.GetPublishedStandaloneAsync(ct)).FirstOrDefault(x => x.Id == id)
            : null;
        var updated = await articles.SetStatusAsync(id, status, EditorEmail, ct);
        if (updated is null) return NotFound();
        if (updated.SourceSubmissionId is Guid submissionId)
        {
            await submissions.UpdateStatusAsync(submissionId,
                status == EditorialArticleStatus.Published ? ArticleSubmissionStatus.Published : ArticleSubmissionStatus.ApprovedForPublishing,
                EditorEmail, null, null, new ArticlePublicationDetails(updated.Slug, updated.Excerpt, updated.Tags), ct);
        }
        if (status == EditorialArticleStatus.Published)
        {
            await imageService.TryDeletePreviousUgcArticlesAsync(existing.PublishedImageBlobKey, updated.ImageBlobKey, ct);
        }
        cache.InvalidateArticlesCache();
        await sitemap.InvalidateAsync(ct);
        await outputCache.EvictByTagAsync(PublicOutputCachePolicies.PublicHtmlTag, ct);
        await SyncSearchIndexAsync(existing, updated, liveStandalone, status, ct);
        return Redirect($"/admin/articles/editor/{id}");
    }

    /// <summary>
    /// Best-effort: keeps the article's search document in step with publish visibility. The
    /// scheduled batch reindex is the correctness backstop if this fails.
    /// </summary>
    private async Task SyncSearchIndexAsync(
        EditorialArticle existing,
        EditorialArticle updated,
        EditorialArticle? liveStandalone,
        string status,
        CancellationToken cancellationToken)
    {
        try
        {
            if (updated.LegacyArticleId is int legacyId)
            {
                await SyncLegacyArticleAsync(legacyId, status, cancellationToken);

                return;
            }

            if (status == EditorialArticleStatus.Published)
            {
                var published = await articleRepository.GetBySlugAsync(updated.Slug, cancellationToken);
                if (published is not null)
                {
                    await searchIndexService.UpsertAsync(SearchReindexBuilder.MapArticle(published), cancellationToken);
                }

                if (liveStandalone is not null
                    && !string.Equals(liveStandalone.Slug, updated.Slug, StringComparison.Ordinal))
                {
                    await searchIndexService.RemoveAsync($"article:{liveStandalone.Slug}", cancellationToken);
                }
            }
            else
            {
                var slug = liveStandalone?.Slug ?? existing.Slug;
                await searchIndexService.RemoveAsync($"article:{slug}", cancellationToken);
                if (!string.Equals(slug, existing.Slug, StringComparison.Ordinal))
                {
                    await searchIndexService.RemoveAsync($"article:{existing.Slug}", cancellationToken);
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Best-effort search index sync failed for editorial article {ArticleId}", updated.Id);
        }
    }
    private async Task SyncLegacyArticleAsync(int legacyId, string status, CancellationToken cancellationToken)
    {
        if (status == EditorialArticleStatus.Published)
        {
            var item = await legacyArticles.GetByIdAsync(legacyId, cancellationToken);
            if (item is not null)
            {
                await searchIndexService.UpsertAsync(SearchReindexBuilder.MapLegacyArticle(item), cancellationToken);
            }
        }
        else
        {
            await searchIndexService.RemoveAsync($"legacy-article:{legacyId}", cancellationToken);
        }
    }

}
