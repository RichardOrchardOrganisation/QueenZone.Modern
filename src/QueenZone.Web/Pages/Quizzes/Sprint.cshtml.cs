using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace QueenZone.Web.Pages.Quizzes;

public sealed class SprintModel(QuizSprintService sprintService) : PageModel
{
    private const int IntroBoardRows = 5;
    private const int ResultsBoardRows = 8;

    private const string StartNoticeKey = "QuizSprintStartNotice";

    private const string StartNoticeText = "Press Start to begin a 60-second sprint.";

    public IReadOnlyList<SprintQuestionView> Questions { get; private set; } = [];

    public SprintResult? Result { get; private set; }

    public SprintBoard Board { get; private set; } = new([], null, 0);

    public string? Ticket { get; private set; }

    public long ServerNowUnixMilliseconds { get; private set; }

    public long ExpiresAtUnixMilliseconds { get; private set; }

    public bool EmptyPool { get; private set; }

    public bool Expired { get; private set; }

    public bool SignedIn { get; private set; }

    public string? StartNotice { get; private set; }

    /// <summary>Outcome of saving a guest's earlier score after sign-in (<c>?claim=</c>).</summary>
    public string? ClaimMessage { get; private set; }

    public IReadOnlyList<BreadcrumbItem> Breadcrumbs { get; } =
    [
        BreadcrumbItem.Home,
        new BreadcrumbItem("Quiz Sprint", "/quizzes/sprint"),
    ];

    public async Task OnGetAsync(string? claim, CancellationToken cancellationToken)
    {
        SetViewData();
        StartNotice = TempData[StartNoticeKey] as string;
        EmptyPool = !await sprintService.HasQuestionsAsync(cancellationToken);
        var memberId = await GetCurrentMemberIdAsync();
        SignedIn = memberId is not null;
        if (!string.IsNullOrEmpty(claim))
        {
            ClaimMessage = memberId is Guid member
                ? DescribeClaim(await sprintService.ClaimAsync(claim, member, cancellationToken))
                : "Sign in to save your score to the leaderboard.";
        }

        Board = await sprintService.GetBoardAsync(memberId, IntroBoardRows, cancellationToken);
    }

    public IActionResult OnGetStartAsync()
    {
        TempData[StartNoticeKey] = StartNoticeText;
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostStartAsync(CancellationToken cancellationToken)
    {
        SetViewData();
        SignedIn = await GetCurrentMemberIdAsync() is not null;
        var round = await sprintService.StartAsync(cancellationToken, QuizSprintSeenQuestions.Read(Request));
        if (round is null)
        {
            EmptyPool = true;
            return Page();
        }

        Ticket = round.Ticket;
        ServerNowUnixMilliseconds = round.ServerNowUnixMilliseconds;
        ExpiresAtUnixMilliseconds = round.ExpiresAtUnixMilliseconds;
        Questions = round.Questions;
        return Page();
    }

    public async Task<IActionResult> OnPostFinishAsync(CancellationToken cancellationToken)
    {
        SetViewData();
        if (!Request.Form.TryGetValue("ticket", out var rawTicket) || rawTicket.Count != 1)
        {
            return BadRequest();
        }

        var selections = new Dictionary<Guid, Guid>();
        foreach (var (key, value) in Request.Form)
        {
            if (key.StartsWith("answer_", StringComparison.Ordinal)
                && Guid.TryParse(key["answer_".Length..], out var questionId)
                && Guid.TryParse(value.ToString(), out var optionId))
            {
                selections[questionId] = optionId;
            }
        }

        var memberId = await GetCurrentMemberIdAsync();
        SignedIn = memberId is not null;
        var outcome = await sprintService.FinishAsync(rawTicket.ToString(), selections, memberId, cancellationToken);
        switch (outcome.Status)
        {
            case SprintFinishStatus.Invalid:
                return BadRequest();
            case SprintFinishStatus.Expired:
                Expired = true;
                return Page();
            default:
                QuizSprintSeenQuestions.Remember(HttpContext, outcome.AnsweredQuestionIds ?? []);
                Result = outcome.Result;
                Board = await sprintService.GetBoardAsync(memberId, ResultsBoardRows, cancellationToken);
                return Page();
        }
    }

    private static string DescribeClaim(SprintClaimOutcome outcome) => outcome.Status switch
    {
        SprintClaimStatus.Claimed => $"Your score of {outcome.Points} was added to the leaderboard{(outcome.Rank is { } rank ? $" (rank #{rank} today)" : "")}.",
        SprintClaimStatus.AlreadyClaimed => "That score is already on the leaderboard.",
        SprintClaimStatus.Expired => "That run finished too long ago to add to the leaderboard. Play again to be ranked.",
        _ => "We couldn't save that score. Play again to be ranked.",
    };

    public static string Verdict(int points) => points switch
    {
        >= 20 => "A collector's run. That belongs at the top of the board.",
        >= 12 => "Strong. A steadier streak and the top ten is yours.",
        >= 6 => "Respectable. The archive rewards a second run.",
        _ => "The clock wins this one. Try again — the questions reshuffle.",
    };

    private async Task<Guid?> GetCurrentMemberIdAsync()
    {
        var authResult = await HttpContext.AuthenticateMemberAsync();
        if (!authResult.Succeeded || authResult.Principal is null)
        {
            return null;
        }

        return Guid.TryParse(authResult.Principal.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;
    }

    private void SetViewData()
    {
        ViewData["Title"] = "Quiz Sprint | QueenZone";
        ViewData["CanonicalPath"] = "/quizzes/sprint";
        ViewData["Description"] = "Sixty seconds on the clock. Answer as many Queen questions as you can and take your place on today's leaderboard.";
    }
}
