using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using QueenZone.Data;

namespace QueenZone.Web.Pages.Admin.Quizzes;

/// <summary>
/// Approved member question suggestions (#1109) an admin can add into this quiz. Approval alone
/// never publishes a suggestion into a live quiz — an admin must explicitly add it here.
/// </summary>
public sealed class QuestionBankModel(
    IQuizRepository quizRepository,
    IQuizQuestionSubmissionRepository quizQuestionSubmissionRepository) : AdminQuizPageModel
{
    public QuizAdminDetail? Quiz { get; private set; }

    public IReadOnlyList<QuizQuestionSubmissionListItem> AvailableSubmissions { get; private set; } = [];

    public string? StatusMessage { get; private set; }

    public string? StatusMessageKind { get; private set; }

    public IReadOnlyList<BreadcrumbItem> Breadcrumbs { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(Guid id, CancellationToken cancellationToken)
    {
        Quiz = await quizRepository.GetByIdAsync(id, cancellationToken);
        if (Quiz is null)
        {
            return NotFound();
        }

        AvailableSubmissions = await quizQuestionSubmissionRepository.GetApprovedAndAvailableAsync(cancellationToken);
        StatusMessage = TempData["QuizQuestionBankMessage"] as string;
        StatusMessageKind = TempData["QuizQuestionBankMessageKind"] as string;
        ViewData["Title"] = "Question bank";
        Breadcrumbs = AdminBreadcrumbs.Page(
            "Quizzes",
            "/admin/quizzes",
            "Edit quiz",
            $"/admin/quizzes/{id}/edit",
            "Question bank",
            $"/admin/quizzes/{id}/question-bank");
        return Page();
    }

    public async Task<IActionResult> OnPostAddAsync(Guid id, Guid submissionId, CancellationToken cancellationToken)
    {
        var quiz = await quizRepository.GetByIdAsync(id, cancellationToken);
        if (quiz is null)
        {
            return NotFound();
        }

        if (quiz.AttemptCount > 0)
        {
            return RedirectWithMessage(id, "This quiz has results and cannot accept new questions.", "error");
        }

        var submission = await quizQuestionSubmissionRepository.GetByIdAsync(submissionId, cancellationToken);
        if (submission is null
            || !string.Equals(submission.Status, QuizQuestionSubmissionStatus.Approved, StringComparison.Ordinal)
            || submission.AddedToQuizId is not null)
        {
            return RedirectWithMessage(id, "That suggestion is no longer available to add.", "error");
        }

        var questions = quiz.Questions
            .Select(question => new QuizQuestionDraft(
                question.Text,
                question.Points,
                question.Options.Select(option => new QuizOptionDraft(option.Text, option.IsCorrect)).ToList()))
            .ToList();
        questions.Add(new QuizQuestionDraft(
            submission.QuestionText,
            QuizValidation.DefaultPoints,
            submission.Options.Select(option => new QuizOptionDraft(option.Text, option.IsCorrect)).ToList()));

        var draft = new AdminQuizDraft(quiz.Title, quiz.Description, questions);
        var errors = QuizValidation.ValidateDraft(draft);
        if (errors.Count > 0)
        {
            return RedirectWithMessage(id, string.Join(" ", errors), "error");
        }

        try
        {
            await quizRepository.UpdateAsync(id, draft, cancellationToken);
        }
        catch (QuizException ex)
        {
            return RedirectWithMessage(id, ex.Message, "error");
        }

        await quizQuestionSubmissionRepository.MarkAddedToQuizAsync(submissionId, id, EditorEmail, cancellationToken);
        return RedirectWithMessage(id, "Question added to the quiz.", "success");
    }

    private string EditorEmail =>
        User.FindFirstValue(ClaimTypes.Email)
        ?? User.FindFirstValue("preferred_username")
        ?? User.Identity?.Name
        ?? "unknown";

    private IActionResult RedirectWithMessage(Guid id, string message, string kind)
    {
        TempData["QuizQuestionBankMessage"] = message;
        TempData["QuizQuestionBankMessageKind"] = kind;
        return Redirect($"/admin/quizzes/{id}/question-bank");
    }
}
