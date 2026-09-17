using Microsoft.AspNetCore.Mvc.RazorPages;
using QueenZone.Data;

namespace QueenZone.Web.Pages.Quizzes;

public sealed class IndexModel(IQuizRepository quizRepository) : PageModel
{
    public IReadOnlyList<QuizListItem> Quizzes { get; private set; } = [];

    public IReadOnlyList<BreadcrumbItem> Breadcrumbs { get; } =
    [
        BreadcrumbItem.Home,
        new BreadcrumbItem("Quiz", "/quizzes"),
    ];

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        ViewData["Title"] = "Quiz | QueenZone";
        ViewData["CanonicalPath"] = "/quizzes";
        ViewData["Description"] = "Test your Queen knowledge with multiple-choice quizzes from the Queenzone archive.";
        Quizzes = await quizRepository.GetPublishedAsync(cancellationToken);
    }
}
