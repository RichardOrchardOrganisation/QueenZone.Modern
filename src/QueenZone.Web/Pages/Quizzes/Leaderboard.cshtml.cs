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
        Scope = string.Equals(scope, "all", StringComparison.OrdinalIgnoreCase)
            ? QuizSprintBoardScope.AllTime
            : QuizSprintBoardScope.Daily;
        var memberAuth = await HttpContext.AuthenticateMemberAsync();
        var viewerId = ForumMember.GetMemberId(memberAuth.Principal);
        SignedIn = viewerId is not null;
        Board = await sprintService.GetBoardAsync(viewerId, TopCount, cancellationToken, Scope);

        var allTime = Scope == QuizSprintBoardScope.AllTime;
        ViewData["Title"] = (allTime ? "Quiz Sprint all-time leaderboard" : "Quiz Sprint leaderboard") + " | QueenZone";
        ViewData["CanonicalPath"] = allTime ? "/quizzes/leaderboard?scope=all" : "/quizzes/leaderboard";
        ViewData["Description"] = allTime
            ? "The best Quiz Sprint runs of all time for the Queenzone community."
            : "Today's Quiz Sprint standings for the Queenzone community.";
    }
}
