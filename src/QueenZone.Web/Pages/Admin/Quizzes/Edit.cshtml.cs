using Microsoft.AspNetCore.Mvc;
using QueenZone.Data;

namespace QueenZone.Web.Pages.Admin.Quizzes;

public sealed class EditModel(IQuizRepository quizRepository) : AdminQuizPageModel
{
    public QuizFormViewModel? Form { get; private set; }

    public QuizAdminDetail? Quiz { get; private set; }

    public string? StatusMessage { get; private set; }

    public string? StatusMessageKind { get; private set; }

    public IReadOnlyList<BreadcrumbItem> Breadcrumbs { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(Guid id, CancellationToken cancellationToken)
    {
        var quiz = await quizRepository.GetByIdAsync(id, cancellationToken);
        if (quiz is null)
        {
            return NotFound();
        }

        Quiz = quiz;
        StatusMessage = TempData[MessageKey] as string;
        StatusMessageKind = TempData[MessageKindKey] as string;
        ViewData["Title"] = "Edit quiz";
        Breadcrumbs = AdminBreadcrumbs.Page("Quizzes", "/admin/quizzes", "Edit quiz");
        Form = BuildForm(quiz, QuizFormViewModel.ToDraft(quiz), null);
        return Page();
    }

    public static QuizFormViewModel BuildForm(
        QuizAdminDetail quiz,
        AdminQuizDraft draft,
        IReadOnlyList<string>? errors) =>
        new(
            "Edit quiz",
            $"/admin/quizzes/{quiz.Id}",
            draft,
            errors,
            QuestionsLocked: quiz.AttemptCount > 0);
}
