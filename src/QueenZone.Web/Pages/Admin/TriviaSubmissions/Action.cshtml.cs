using Microsoft.AspNetCore.Mvc;
using QueenZone.Data;

namespace QueenZone.Web.Pages.Admin.TriviaSubmissions;

public sealed class ActionModel(
    ITriviaFactSubmissionRepository triviaFactSubmissionRepository,
    ITriviaRepository triviaRepository,
    PublicQueryCacheService publicQueryCache) : AdminTriviaSubmissionsPageModel
{
    [BindProperty]
    public string Text { get; set; } = string.Empty;

    [BindProperty]
    public string? Category { get; set; }

    [BindProperty]
    public string? Difficulty { get; set; }

    [BindProperty]
    public string? Source { get; set; }

    [BindProperty]
    public string? ReviewNotes { get; set; }

    [BindProperty]
    public string? RejectionReason { get; set; }

    public async Task<IActionResult> OnPostApproveAsync(Guid id, CancellationToken cancellationToken)
    {
        var submission = await triviaFactSubmissionRepository.GetByIdAsync(id, cancellationToken);
        if (submission is null)
        {
            return NotFound();
        }

        if (!TriviaFactSubmissionStatus.IsPendingReview(submission.Status))
        {
            return RedirectWithMessage(id, "Only pending suggestions can be approved.", "error");
        }

        var draft = new AdminTriviaDraft(
            (Text ?? string.Empty).Trim(),
            true,
            TriviaValidation.NormalizeOptional(Category),
            TriviaValidation.NormalizeDifficulty(Difficulty),
            TriviaValidation.NormalizeOptional(Source));

        var errors = TriviaValidation.ValidateDraft(draft);
        if (errors.Count > 0)
        {
            return RedirectWithMessage(id, string.Join(" ", errors), "error");
        }

        var factId = await triviaRepository.CreateAsync(draft, cancellationToken);
        publicQueryCache.InvalidateTriviaCache();
        try
        {
            var approved = await triviaFactSubmissionRepository.ApproveAsync(
                id,
                factId,
                EditorEmail,
                ReviewNotes,
                cancellationToken);
            if (approved is null)
            {
                return NotFound();
            }
        }
        catch (InvalidOperationException ex)
        {
            return RedirectWithMessage(id, ex.Message, "error");
        }

        return RedirectWithMessage(id, "Trivia fact approved and published.", "success");
    }

    public async Task<IActionResult> OnPostRejectAsync(Guid id, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(RejectionReason))
        {
            return RedirectWithMessage(id, "A rejection reason is required.", "error");
        }

        try
        {
            var updated = await triviaFactSubmissionRepository.RejectAsync(
                id,
                EditorEmail,
                RejectionReason,
                ReviewNotes,
                cancellationToken);
            if (updated is null)
            {
                return NotFound();
            }
        }
        catch (InvalidOperationException ex)
        {
            return RedirectWithMessage(id, ex.Message, "error");
        }

        return RedirectWithMessage(id, "Trivia suggestion rejected.", "success");
    }

    private IActionResult RedirectWithMessage(Guid id, string message, string kind)
    {
        TempData["TriviaSubmissionMessage"] = message;
        TempData["TriviaSubmissionMessageKind"] = kind;
        return Redirect($"/admin/trivia-submissions/{id}");
    }
}
