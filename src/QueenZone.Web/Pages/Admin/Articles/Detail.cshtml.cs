using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Mvc;
using QueenZone.Data;

namespace QueenZone.Web.Pages.Admin.Articles;

// Automatic Razor Pages antiforgery validation returns a bare 400 with no error page
// or redisplay on failure. Prepare is a low-traffic handler that can be reached long
// after the review page was loaded (a stale/expired antiforgery cookie), so validation
// is done manually here and failures redisplay the review page with a message instead
// of surfacing a raw 400 to the admin.
[IgnoreAntiforgeryToken]
public sealed class DetailModel(
    IArticleSubmissionRepository articleSubmissionRepository,
    IEditorialArticleRepository editorialArticles,
    UgcHtml ugcHtml,
    IAntiforgery antiforgery,
    ILogger<DetailModel> logger) : AdminArticlesPageModel
{
    public ArticleSubmission? Submission { get; private set; }

    public string FormattedBody { get; private set; } = string.Empty;

    public string AuthorLine => Submission is null ? string.Empty
        : $"{Submission.AuthorDisplayName ?? "Unknown"} {(string.IsNullOrWhiteSpace(Submission.AuthorEmail) ? "" : $"({Submission.AuthorEmail})")}";

    public string SubmittedText => Submission?.SubmittedAt?.ToString("u") ?? "—";

    public string ExcerptText => Submission?.Excerpt ?? "—";

    public string TagsText => Submission?.Tags ?? "—";

    public string? StatusMessage { get; private set; }

    public string StatusMessageKind { get; private set; } = "success";

    public IReadOnlyList<BreadcrumbItem> Breadcrumbs { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(Guid id, CancellationToken cancellationToken)
    {
        var submission = await articleSubmissionRepository.GetByIdAsync(id, cancellationToken);
        if (submission is null)
        {
            return NotFound();
        }

        Submission = submission;
        FormattedBody = ugcHtml.FormatForDisplay(submission.Body);

        StatusMessage = TempData["ArticleMessage"] as string;
        StatusMessageKind = TempData["ArticleMessageKind"] as string ?? "success";

        ViewData["Title"] = $"Review: {submission.Title}";
        Breadcrumbs = AdminBreadcrumbs.Page("Articles", "/admin/articles", "Review article submission");
        return Page();
    }

    public async Task<IActionResult> OnPostPrepareAsync(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            await antiforgery.ValidateRequestAsync(HttpContext);
        }
        catch (AntiforgeryValidationException ex)
        {
            logger.LogWarning(ex, "Articles Prepare POST rejected: {Reason}", ex.Message);
            TempData["ArticleMessage"] = "This action could not be verified. Reload the page and try again.";
            TempData["ArticleMessageKind"] = "error";
            return Redirect($"/admin/articles/{id}");
        }

        var submission = await articleSubmissionRepository.GetByIdAsync(id, cancellationToken);
        if (submission is null) return NotFound();
        var existing = (await editorialArticles.GetAllAsync(cancellationToken)).SingleOrDefault(x => x.SourceSubmissionId == id);
        if (existing is not null) return Redirect($"/admin/articles/editor/{existing.Id}");
        try
        {
            var saved = await editorialArticles.SaveDraftAsync(new EditorialArticleDraft(
                null, null, submission.Id, submission.Title, submission.Slug, submission.Excerpt ?? string.Empty,
                submission.Body, submission.AuthorDisplayName ?? "QueenZone contributor", "Feature", submission.Tags, null,
                submission.CoverImageBlobPath, submission.PublishedAt ?? DateTimeOffset.UtcNow), EditorEmail, cancellationToken);
            return Redirect($"/admin/articles/editor/{saved.Id}");
        }
        catch (InvalidOperationException ex)
        {
            logger.LogWarning(ex, "Articles Prepare POST failed for submission {SubmissionId}: {Reason}", id, ex.Message);
            TempData["ArticleMessage"] = ex.Message;
            TempData["ArticleMessageKind"] = "error";
            return Redirect($"/admin/articles/{id}");
        }
    }
}
