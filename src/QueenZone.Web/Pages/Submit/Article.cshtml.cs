using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using QueenZone.Data;

namespace QueenZone.Web.Pages.Submit;

[Authorize(Policy = MemberAuthenticationSchemes.MemberPolicy, AuthenticationSchemes = MemberAuthenticationSchemes.MembersCookie)]
public sealed class ArticleModel(
    IArticleSubmissionRepository articleSubmissionRepository,
    UgcHtml ugcHtml) : PageModel
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
                return Page();
            }

            var sanitizedBody = ugcHtml.Sanitize(Body);

            var draft = await articleSubmissionRepository.UpsertDraftAsync(
                new ArticleSubmissionDraft(DraftId, memberId.Value, Title, Excerpt, sanitizedBody, null, Tags),
                cancellationToken);

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

        try
        {
            var saved = await articleSubmissionRepository.UpsertDraftAsync(
                new ArticleSubmissionDraft(DraftId, memberId.Value, Title, Excerpt, savedBody, null, Tags),
                cancellationToken);

            DraftId = saved.Id;
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
}
