using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace QueenZone.Web.Pages.Quizzes;

public sealed class SprintModel(QuizSprintService sprintService) : PageModel
{
    public IReadOnlyList<SprintQuestionView> Questions { get; private set; } = [];

    public SprintResult? Result { get; private set; }

    public string? Ticket { get; private set; }

    public long ServerNowUnixMilliseconds { get; private set; }

    public long ExpiresAtUnixMilliseconds { get; private set; }

    public bool EmptyPool { get; private set; }

    public bool Expired { get; private set; }

    public bool SignedIn { get; private set; }

    public string? StartNotice { get; private set; }

    private const string StartNoticeKey = "QuizSprintStartNotice";

    private const string StartNoticeText = "Press Start to begin a 60-second sprint.";

    public IReadOnlyList<BreadcrumbItem> Breadcrumbs { get; } =
    [
        BreadcrumbItem.Home,
        new BreadcrumbItem("Quiz", "/quizzes"),
        new BreadcrumbItem("Quiz Sprint", "/quizzes/sprint"),
    ];

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        SetViewData();
        StartNotice = TempData[StartNoticeKey] as string;
        EmptyPool = !await sprintService.HasQuestionsAsync(cancellationToken);
        SignedIn = await GetCurrentMemberIdAsync() is not null;
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
        var round = await sprintService.StartAsync(cancellationToken);
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
                Result = outcome.Result;
                return Page();
        }
    }

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
        ViewData["Description"] = "Answer as many Queen questions as you can in 60 seconds.";
    }
}
