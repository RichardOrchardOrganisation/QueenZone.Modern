using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using QueenZone.Data;

namespace QueenZone.Web.Pages.Submit;

[RequestSizeLimit(8 * 1024 * 1024)]
[Authorize(Policy = MemberAuthenticationSchemes.MemberPolicy, AuthenticationSchemes = MemberAuthenticationSchemes.MembersCookie)]
public sealed class ArticleModel(
    IArticleSubmissionRepository articleSubmissionRepository,
    UgcHtml ugcHtml,
    NewsArticleImageService imageService) : PageModel
{
    [BindProperty]
    public Guid? DraftId { get; set; }

    [BindProperty]
    [Required(ErrorMessage = "Title is required.")]
    [StringLength(300, ErrorMessage = "Title must be 300 characters or fewer.")]
    public string Title { get; set; } = string.Empty;

    [BindProperty]
    [StringLength(500, ErrorMessage = "Excerpt must be 500 characters or fewer.")]
    public string? Excerpt { get; set; }

    [BindProperty]
    public string? Body { get; set; }

    [BindProperty]
    [StringLength(500, ErrorMessage = "Tags must be 500 characters or fewer.")]
    public string? Tags { get; set; }

    [BindProperty]
    public IFormFile? CoverImage { get; set; }

    [BindProperty]
    public bool RemoveCoverImage { get; set; }

    public string? CoverImageBlobPath { get; private set; }

    // Previous cover blob, deleted only once the replacement is persisted on the draft.
    private string? replacedCoverBlob;

    public string? StatusMessage { get; private set; }

    public string StatusMessageKind { get; private set; } = "success";

    public async Task<IActionResult> OnGetAsync(Guid? id, CancellationToken cancellationToken)
    {
        if (id is Guid editId)
        {
            return await LoadEditableDraftAsync(editId, cancellationToken);
        }

        if (await HttpContext.AuthenticateMemberIdAsync() is null)
        {
            return Redirect("/account/login");
        }

        ViewData["Title"] = "Write an article";
        return Page();
    }

    /// <summary>Legacy edit URL: <c>/submit/article?handler=Edit&amp;id={guid}</c>.</summary>
    public Task<IActionResult> OnGetEditAsync(Guid id, CancellationToken cancellationToken) =>
        LoadEditableDraftAsync(id, cancellationToken);

    private async Task<IActionResult> LoadEditableDraftAsync(Guid id, CancellationToken cancellationToken)
    {
        var memberId = await HttpContext.AuthenticateMemberIdAsync();
        if (memberId is null)
        {
            return Redirect("/account/login");
        }

        var submission = await articleSubmissionRepository.GetByIdAsync(id, cancellationToken);
        if (submission is null || submission.AuthorMemberId != memberId.Value)
        {
            return NotFound();
        }

        if (submission.Status is not (ArticleSubmissionStatus.Draft or ArticleSubmissionStatus.RequiresRevision))
        {
            return Redirect("/account/my-submissions?tab=articles");
        }

        DraftId = submission.Id;
        Title = submission.Title;
        Excerpt = submission.Excerpt;
        Body = submission.Body;
        Tags = submission.Tags;
        CoverImageBlobPath = submission.CoverImageBlobPath;

        ViewData["Title"] = submission.Status == ArticleSubmissionStatus.RequiresRevision
            ? "Revise article"
            : "Edit draft";
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(string? action, CancellationToken cancellationToken)
    {
        var memberId = await HttpContext.AuthenticateMemberIdAsync();
        if (memberId is null)
        {
            return Redirect("/account/login");
        }

        ViewData["Title"] = "Write an article";

        if (action == "submit")
        {
            if (!ModelState.IsValid)
            {
                await LoadStoredCoverAsync(memberId.Value, cancellationToken);
                return Page();
            }

            var sanitizedBody = ugcHtml.Sanitize(Body);

            var cover = await ApplyCoverImageAsync(memberId.Value, sanitizedBody, cancellationToken);
            if (!ModelState.IsValid)
            {
                return Page();
            }

            var draft = await articleSubmissionRepository.UpsertDraftAsync(
                new ArticleSubmissionDraft(DraftId, memberId.Value, Title, Excerpt, sanitizedBody, cover, Tags),
                cancellationToken);
            await DeleteReplacedCoverAsync(cancellationToken);

            try
            {
                var submitted = await articleSubmissionRepository.SubmitForReviewAsync(
                    draft.Id,
                    memberId.Value,
                    cancellationToken);

                if (submitted is null)
                {
                    StatusMessage = "Could not submit — the article may already be submitted.";
                    StatusMessageKind = "error";
                    return Page();
                }

                return Redirect($"/submit/article/confirmation/{submitted.Id:D}");
            }
            catch (InvalidOperationException ex)
            {
                ModelState.AddModelError(nameof(Body), ex.Message);
                return Page();
            }
        }

        // Save draft
        if (string.IsNullOrWhiteSpace(Title))
        {
            ModelState.AddModelError(nameof(Title), "Title is required to save a draft.");
            return Page();
        }

        var savedBody = ugcHtml.Sanitize(Body);

        var savedCover = await ApplyCoverImageAsync(memberId.Value, savedBody, cancellationToken);
        if (!ModelState.IsValid)
        {
            return Page();
        }

        try
        {
            var saved = await articleSubmissionRepository.UpsertDraftAsync(
                new ArticleSubmissionDraft(DraftId, memberId.Value, Title, Excerpt, savedBody, savedCover, Tags),
                cancellationToken);

            await DeleteReplacedCoverAsync(cancellationToken);
            DraftId = saved.Id;
            CoverImageBlobPath = saved.CoverImageBlobPath;
            StatusMessage = "Draft saved.";
            StatusMessageKind = "success";
        }
        catch (InvalidOperationException ex)
        {
            StatusMessage = ex.Message;
            StatusMessageKind = "error";
        }

        return Page();
    }

    /// <summary>
    /// Returns the cover image value for the draft upsert: a new blob name, an empty string to clear,
    /// or null to keep what is stored. Adds a model error and returns null when the upload is rejected.
    /// </summary>
    private async Task<string?> ApplyCoverImageAsync(Guid memberId, string body, CancellationToken cancellationToken)
    {
        await LoadStoredCoverAsync(memberId, cancellationToken);

        if (CoverImage is not { Length: > 0 })
        {
            if (RemoveCoverImage && CoverImageBlobPath is not null)
            {
                replacedCoverBlob = CoverImageBlobPath;
                CoverImageBlobPath = null;
                return string.Empty;
            }

            return null;
        }

        var applied = await imageService.TryApplyAsync(
            CoverImage,
            crop: null,
            new AdminNewsDraft(Title, null, Excerpt ?? string.Empty, body, DateTime.UtcNow, null, CoverImageBlobPath, null),
            User,
            persist: true,
            cancellationToken);
        if (applied.Error is not null)
        {
            ModelState.AddModelError(nameof(CoverImage), applied.Error);
            return null;
        }

        replacedCoverBlob = CoverImageBlobPath;
        CoverImageBlobPath = applied.Draft.ImageBlobKey;
        return CoverImageBlobPath;
    }

    private async Task DeleteReplacedCoverAsync(CancellationToken cancellationToken)
    {
        await imageService.TryDeletePreviousUgcArticlesAsync(replacedCoverBlob, CoverImageBlobPath, cancellationToken);
        replacedCoverBlob = null;
    }

    private async Task LoadStoredCoverAsync(Guid memberId, CancellationToken cancellationToken)
    {
        if (DraftId is not Guid draftId)
        {
            return;
        }

        var stored = await articleSubmissionRepository.GetByIdAsync(draftId, cancellationToken);
        if (stored is not null && stored.AuthorMemberId == memberId)
        {
            CoverImageBlobPath = stored.CoverImageBlobPath;
        }
    }
}
