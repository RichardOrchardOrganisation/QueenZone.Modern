using QueenZone.Data;

namespace QueenZone.Web.Pages.Admin.Quizzes;

public sealed class NewModel : AdminQuizPageModel
{
    public QuizFormViewModel Form { get; private set; } = BuildForm(QuizFormViewModel.EmptyDraft(), null);

    public IReadOnlyList<BreadcrumbItem> Breadcrumbs { get; private set; } = [];

    public void OnGet()
    {
        ViewData["Title"] = "Add quiz";
        Breadcrumbs = AdminBreadcrumbs.Page("Quizzes", "/admin/quizzes", "Add quiz");
    }

    public static QuizFormViewModel BuildForm(AdminQuizDraft draft, IReadOnlyList<string>? errors) =>
        new("Add quiz", "/admin/quizzes", draft, errors);
}
