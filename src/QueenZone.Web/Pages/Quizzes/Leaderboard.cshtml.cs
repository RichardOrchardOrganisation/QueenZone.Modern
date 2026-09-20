using Microsoft.AspNetCore.Mvc.RazorPages;

namespace QueenZone.Web.Pages.Quizzes;

public sealed class LeaderboardModel(QuizSprintService sprintService) : PageModel
{
    private const int TopCount = 20;

    public SprintBoard Board { get; private set; } = new([], null, 0);

    public bool SignedIn { get; private set; }

    public IReadOnlyList<BreadcrumbItem> Breadcrumbs { get; } =
    [
        BreadcrumbItem.Home,
        new BreadcrumbItem("Quiz Sprint", "/quizzes/sprint"),
        new BreadcrumbItem("Leaderboard", "/quizzes/leaderboard"),
    ];

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        var memberAuth = await HttpContext.AuthenticateMemberAsync();
        var viewerId = ForumMember.GetMemberId(memberAuth.Principal);
        SignedIn = viewerId is not null;
        Board = await sprintService.GetBoardAsync(viewerId, TopCount, cancellationToken);

        ViewData["Title"] = "Quiz Sprint leaderboard | QueenZone";
        ViewData["CanonicalPath"] = "/quizzes/leaderboard";
        ViewData["Description"] = "Today's Quiz Sprint standings for the Queenzone community.";
    }
}
