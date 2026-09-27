using Microsoft.AspNetCore.Mvc;
using QueenZone.Data;

namespace QueenZone.Web.Pages.Admin.QuizQuestionSubmissions;

public sealed class ActionModel(IQuizQuestionSubmissionRepository quizQuestionSubmissionRepository)
    : AdminQuizQuestionSubmissionsPageModel
{
    [BindProperty]
    public string QuestionText { get; set; } = string.Empty;

    [BindProperty]
    public List<string> OptionTexts { get; set; } = [];

    [BindProperty]
    public int CorrectOptionIndex { get; set; }

    [BindProperty]
    public string? ReviewNotes { get; set; }

    [BindProperty]
    public string? RejectionReason { get; set; }

    public async Task<IActionResult> OnPostApproveAsync(Guid id, CancellationToken cancellationToken)
    {
        var options = OptionTexts
            .Select((text, index) => new QuizQuestionSubmissionOptionDraft(text ?? string.Empty, index == CorrectOptionIndex))
            .ToList();
        var edit = new QuizQuestionSubmissionEdit((QuestionText ?? string.Empty).Trim(), options);

        try
        {
            var approved = await quizQuestionSubmissionRepository.ApproveAsync(
                id,
                edit,
                EditorEmail,
                ReviewNotes,
                cancellationToken);
            if (approved is null)
            {
                return NotFound();
            }
        }
        catch (ArgumentException ex)
        {
            return RedirectWithMessage(id, ex.Message, "error");
        }
        catch (InvalidOperationException ex)
        {
            return RedirectWithMessage(id, ex.Message, "error");
        }

        return RedirectWithMessage(id, "Question approved and added to the question bank.", "success");
    }

    public async Task<IActionResult> OnPostRejectAsync(Guid id, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(RejectionReason))
        {
            return RedirectWithMessage(id, "A rejection reason is required.", "error");
        }

        try
        {
            var updated = await quizQuestionSubmissionRepository.RejectAsync(
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

        return RedirectWithMessage(id, "Quiz question suggestion rejected.", "success");
    }

    private IActionResult RedirectWithMessage(Guid id, string message, string kind)
    {
        TempData["QuizQuestionSubmissionMessage"] = message;
        TempData["QuizQuestionSubmissionMessageKind"] = kind;
        return Redirect($"/admin/quiz-question-submissions/{id}");
    }
}
