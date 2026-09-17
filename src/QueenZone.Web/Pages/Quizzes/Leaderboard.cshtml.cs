using Microsoft.AspNetCore.Mvc.RazorPages;
using QueenZone.Data;

namespace QueenZone.Web.Pages.Quizzes;

public sealed record QuizLeaderboardRow(int Rank, string DisplayName, int Score, int AttemptCount);

public sealed class LeaderboardModel(
    IQuizRepository quizRepository,
    IMemberAccountRepository memberAccountRepository) : PageModel
{
    private const int TopCount = 20;

    public QuizLeaderboardScope Scope { get; private set; }

    public IReadOnlyList<QuizLeaderboardRow> Rows { get; private set; } = [];

    public QuizLeaderboardRow? ViewerRow { get; private set; }

    public int TotalMembers { get; private set; }

    public IReadOnlyList<BreadcrumbItem> Breadcrumbs { get; } =
    [
        BreadcrumbItem.Home,
        new BreadcrumbItem("Quiz", "/quizzes"),
        new BreadcrumbItem("Leaderboard", "/quizzes/leaderboard"),
    ];

    public async Task OnGetAsync(string? scope, CancellationToken cancellationToken)
    {
        Scope = string.Equals(scope, "all", StringComparison.OrdinalIgnoreCase)
            ? QuizLeaderboardScope.AllTime
            : QuizLeaderboardScope.Week;

        var memberAuth = await HttpContext.AuthenticateMemberAsync();
        var viewerId = ForumMember.GetMemberId(memberAuth.Principal);

        var result = await quizRepository.GetLeaderboardAsync(Scope, viewerId, TopCount, cancellationToken);
        Rows = await ToRowsAsync(result.Top, cancellationToken);
        ViewerRow = result.Viewer is null
            ? null
            : (await ToRowsAsync([result.Viewer], cancellationToken)).Single();
        TotalMembers = result.TotalMembers;

        ViewData["Title"] = "Quiz leaderboard | QueenZone";
        ViewData["CanonicalPath"] = "/quizzes/leaderboard";
        ViewData["Description"] = "Weekly and all-time quiz standings for the Queenzone community.";
    }

    private async Task<IReadOnlyList<QuizLeaderboardRow>> ToRowsAsync(
        IReadOnlyList<QuizLeaderboardEntry> entries,
        CancellationToken cancellationToken)
    {
        var rows = new List<QuizLeaderboardRow>(entries.Count);
        foreach (var entry in entries)
        {
            var account = await memberAccountRepository.FindByIdAsync(entry.MemberAccountId, cancellationToken);
            rows.Add(new QuizLeaderboardRow(entry.Rank, account?.DisplayName ?? "Member", entry.Score, entry.AttemptCount));
        }

        return rows;
    }
}
