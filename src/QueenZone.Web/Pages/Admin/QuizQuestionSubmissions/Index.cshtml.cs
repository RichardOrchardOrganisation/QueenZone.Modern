using QueenZone.Data;

namespace QueenZone.Web.Pages.Admin.QuizQuestionSubmissions;

public sealed class IndexModel(IQuizQuestionSubmissionRepository quizQuestionSubmissionRepository)
    : AdminQuizQuestionSubmissionsPageModel
{
    public IReadOnlyList<QuizQuestionSubmissionListItem> Submissions { get; private set; } = [];

    public IReadOnlyList<BreadcrumbItem> Breadcrumbs { get; private set; } = [];

    public async Task OnGetAsync(int pageNumber = 1, CancellationToken cancellationToken = default)
    {
        Submissions = await quizQuestionSubmissionRepository.GetPendingAsync(
            Math.Max(1, pageNumber),
            50,
            cancellationToken);
        ViewData["Title"] = "Quiz question submissions";
        Breadcrumbs = AdminBreadcrumbs.Section("Quiz question submissions", "/admin/quiz-question-submissions");
    }
}
