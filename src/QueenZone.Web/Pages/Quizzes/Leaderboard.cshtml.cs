using Microsoft.AspNetCore.Mvc.RazorPages;
using QueenZone.Data;

namespace QueenZone.Web.Pages.Quizzes;

public sealed class LeaderboardModel(QuizSprintService sprintService) : PageModel
{
    private const int TopCount = 20;

    public SprintBoard Board { get; private set; } = new([], null, 0);

    public QuizSprintBoardScope Scope { get; private set; }

    public bool SignedIn { get; private set; }

    public IReadOnlyList<BreadcrumbItem> Breadcrumbs { get; } =
    [
        BreadcrumbItem.Home,
        new BreadcrumbItem("Quiz Sprint", "/quizzes/sprint"),
        new BreadcrumbItem("Leaderboard", "/quizzes/leaderboard"),
    ];

    public async Task OnGetAsync(string? scope, CancellationToken cancellationToken)
    {
        Scope = scope?.ToLowerInvariant() switch
        {
            "all" => QuizSprintBoardScope.AllTime,
            "total" => QuizSprintBoardScope.Total,
            _ => QuizSprintBoardScope.Daily,
        };
        var memberAuth = await HttpContext.AuthenticateMemberAsync();
        var viewerId = ForumMember.GetMemberId(memberAuth.Principal);
        SignedIn = viewerId is not null;
        Board = await sprintService.GetBoardAsync(viewerId, TopCount, cancellationToken, Scope);

        var (title, path, description) = Scope switch
        {
            QuizSprintBoardScope.AllTime => ("Quiz Sprint best runs", "/quizzes/leaderboard?scope=all", "The best single Quiz Sprint runs of all time for the Queenzone community."),
            QuizSprintBoardScope.Total => ("Quiz Sprint total points", "/quizzes/leaderboard?scope=total", "Total Quiz Sprint points across every run for the Queenzone community."),
            _ => ("Quiz Sprint leaderboard", "/quizzes/leaderboard", "Today's Quiz Sprint standings for the Queenzone community."),
        };
        ViewData["Title"] = title + " | QueenZone";
        ViewData["CanonicalPath"] = path;
        ViewData["Description"] = description;
    }
}
