using Microsoft.AspNetCore.Mvc;
using QueenZone.Data;

namespace QueenZone.Web.Pages.Admin.QuizQuestionSubmissions;

public sealed class DetailModel(IQuizQuestionSubmissionRepository quizQuestionSubmissionRepository)
    : AdminQuizQuestionSubmissionsPageModel
{
    public QuizQuestionSubmission? Submission { get; private set; }

    public string? StatusMessage { get; private set; }

    public string? StatusMessageKind { get; private set; }

    public IReadOnlyList<BreadcrumbItem> Breadcrumbs { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(Guid id, CancellationToken cancellationToken)
    {
        Submission = await quizQuestionSubmissionRepository.GetByIdAsync(id, cancellationToken);
        if (Submission is null)
        {
            return NotFound();
        }

        StatusMessage = TempData["QuizQuestionSubmissionMessage"] as string;
        StatusMessageKind = TempData["QuizQuestionSubmissionMessageKind"] as string;
        ViewData["Title"] = "Review quiz question suggestion";
        Breadcrumbs = AdminBreadcrumbs.Page(
            "Quiz question submissions",
            "/admin/quiz-question-submissions",
            "Review suggestion");
        return Page();
    }
}
