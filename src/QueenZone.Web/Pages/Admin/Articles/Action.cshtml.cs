using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Mvc;
using QueenZone.Data;
using QueenZone.Web.Search;

namespace QueenZone.Web.Pages.Admin.Articles;

// See DetailModel for why antiforgery validation is done manually here: the automatic
// Razor Pages filter returns a bare 400 on failure with no way for the admin to retry
// gracefully, whereas a manual check can redirect back to the review page with a message.
[RequestFormLimits(MultipartBodyLengthLimit = 16 * 1024 * 1024, ValueLengthLimit = 16 * 1024 * 1024)]
[RequestSizeLimit(16 * 1024 * 1024)]
[IgnoreAntiforgeryToken]
public sealed class ActionModel(
    IArticleSubmissionRepository articleSubmissionRepository,
    IArticleRepository articleRepository,
    PublicQueryCacheService publicQueryCache,
    ISearchIndexService searchIndexService,
    NewsArticleImageService imageService,
    IAntiforgery antiforgery,
    ILogger<ActionModel> logger) : AdminArticlesPageModel
{
    [BindProperty]
    public string? Slug { get; set; }

    [BindProperty]
    public string? Excerpt { get; set; }

    [BindProperty]
    public string? Tags { get; set; }

    [BindProperty]
    public string? ReviewNotes { get; set; }

    [BindProperty]
    public string? RejectionReason { get; set; }

    [BindProperty]
    public IFormFile? CoverImage { get; set; }

    [BindProperty]
    public bool RemoveCoverImage { get; set; }

    private bool statusApplied;

    public async Task<IActionResult> OnPostAsync(Guid id, string submitAction, CancellationToken cancellationToken)
    {
        try
        {
            await antiforgery.ValidateRequestAsync(HttpContext);
        }
        catch (AntiforgeryValidationException ex)
        {
            logger.LogWarning(ex, "Articles action POST rejected: {Reason}", ex.Message);
            TempData["ArticleMessage"] = "This action could not be verified. Reload the page and try again.";
            TempData["ArticleMessageKind"] = "error";
            return Redirect($"/admin/articles/{id}");
        }

        return submitAction switch
        {
            "approve" => await ApplyWithCoverImageAsync(id, ArticleSubmissionStatus.ApprovedForPublishing,
                "Approved for publishing.", cancellationToken),
            "publish" => await ApplyWithCoverImageAsync(id, ArticleSubmissionStatus.Published,
                "Article published.", cancellationToken),
            "revise" => await ApplyRevisionRequestAsync(id, cancellationToken),
            "reject" => await ApplyRejectAsync(id, cancellationToken),
            "underreview" => await ApplyAsync(id, ArticleSubmissionStatus.UnderReview,
                rejectionReason: null, "Marked under review.", cancellationToken),
            _ => Redirect($"/admin/articles/{id}"),
        };
    }

    /// <summary>
    /// Stores an uploaded cover image (or clears it) before the status change, so the photo
    /// can be added on the same review step that approves or publishes the article.
    /// </summary>
    private async Task<IActionResult> ApplyWithCoverImageAsync(
        Guid id,
        string status,
        string successMessage,
        CancellationToken cancellationToken)
    {
        var existing = await articleSubmissionRepository.GetByIdAsync(id, cancellationToken);
        if (existing is null)
        {
            return NotFound();
        }

        string? coverImage = RemoveCoverImage ? string.Empty : null;
        if (CoverImage is { Length: > 0 })
        {
            var applied = await imageService.TryApplyAsync(
                CoverImage,
                crop: null,
                new AdminNewsDraft(existing.Title, existing.Slug, existing.Excerpt ?? string.Empty, existing.Body, DateTime.UtcNow, null, existing.CoverImageBlobPath, null),
                User,
                persist: true,
                cancellationToken);
            if (applied.Error is not null)
            {
                TempData["ArticleMessage"] = applied.Error;
                TempData["ArticleMessageKind"] = "error";
                return Redirect($"/admin/articles/{id}");
            }

            coverImage = applied.Draft.ImageBlobKey;
        }

        var result = await ApplyAsync(id, status, rejectionReason: null, successMessage, cancellationToken, coverImage);
        if (coverImage is not null && statusApplied)
        {
            await imageService.TryDeletePreviousUgcArticlesAsync(existing.CoverImageBlobPath, coverImage, cancellationToken);
        }
        else if (!statusApplied && CoverImage is { Length: > 0 })
        {
            // The status change failed, so the freshly uploaded blob is unreferenced.
            await imageService.TryDeletePreviousUgcArticlesAsync(coverImage, null, cancellationToken);
        }

        return result;
    }

    private async Task<IActionResult> ApplyAsync(
        Guid id,
        string status,
        string? rejectionReason,
        string successMessage,
        CancellationToken cancellationToken,
        string? coverImage = null)
    {
        try
        {
            var updated = await articleSubmissionRepository.UpdateStatusAsync(
                id,
                status,
                EditorEmail,
                ReviewNotes,
                rejectionReason,
                new ArticlePublicationDetails(Slug, Excerpt, Tags, coverImage),
                cancellationToken);

            if (updated is null)
            {
                return NotFound();
            }

            // Refresh public archive counts when publish visibility may have changed.
            // Cheap process-local remove; also covers future legacy-article count coupling.
            if (status is ArticleSubmissionStatus.Published
                or ArticleSubmissionStatus.ApprovedForPublishing
                or ArticleSubmissionStatus.Rejected
                or ArticleSubmissionStatus.RequiresRevision)
            {
                publicQueryCache.InvalidateArticleCountCache();
            }

            await SyncSearchIndexAsync(updated, cancellationToken);

            statusApplied = true;
            TempData["ArticleMessage"] = successMessage;
            TempData["ArticleMessageKind"] = "success";
        }
        catch (InvalidOperationException ex)
        {
            TempData["ArticleMessage"] = ex.Message;
            TempData["ArticleMessageKind"] = "error";
        }

        return Redirect($"/admin/articles/{id}");
    }

    /// <summary>
    /// Best-effort: keeps the article's search document in step with its new status. The
    /// scheduled batch reindex is the correctness backstop if this fails.
    /// </summary>
    private async Task SyncSearchIndexAsync(ArticleSubmission updated, CancellationToken cancellationToken)
    {
        try
        {
            if (updated.Status == ArticleSubmissionStatus.Published)
            {
                var published = await articleRepository.GetBySlugAsync(updated.Slug, cancellationToken);
                if (published is not null)
                {
                    await searchIndexService.UpsertAsync(SearchReindexBuilder.MapArticle(published), cancellationToken);
                }
            }
            else
            {
                await searchIndexService.RemoveAsync($"article:{updated.Slug}", cancellationToken);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Best-effort search index sync failed for article submission {ArticleId}", updated.Id);
        }
    }

    private async Task<IActionResult> ApplyRevisionRequestAsync(Guid id, CancellationToken cancellationToken)
    {
        return await ApplyAsync(id, ArticleSubmissionStatus.RequiresRevision,
            rejectionReason: RejectionReason, "Revision requested.", cancellationToken);
    }

    private async Task<IActionResult> ApplyRejectAsync(Guid id, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(RejectionReason))
        {
            TempData["ArticleMessage"] = "A rejection reason is required.";
            TempData["ArticleMessageKind"] = "error";
            return Redirect($"/admin/articles/{id}");
        }

        return await ApplyAsync(id, ArticleSubmissionStatus.Rejected,
            rejectionReason: RejectionReason, "Article rejected.", cancellationToken);
    }
}
