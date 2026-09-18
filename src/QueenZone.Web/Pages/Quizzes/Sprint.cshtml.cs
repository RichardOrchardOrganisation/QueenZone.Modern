using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using QueenZone.Data;

namespace QueenZone.Web.Pages.Quizzes;

public sealed record SprintOptionView(Guid Id, string Text);

public sealed record SprintQuestionView(Guid Id, string Text, IReadOnlyList<SprintOptionView> Options);

public sealed record SprintReviewItem(string QuestionText, bool IsCorrect, string CorrectAnswer);

public sealed record SprintResult(int Attempted, int Correct, IReadOnlyList<SprintReviewItem> Answers);

public sealed class SprintModel(
    IQuizRepository quizRepository,
    IDataProtectionProvider dataProtectionProvider,
    TimeProvider timeProvider) : PageModel
{
    private const int DurationSeconds = 60;
    private const int QuestionLimit = 60;
    private static readonly JsonSerializerOptions TicketJsonOptions = new(JsonSerializerDefaults.Web);
    private readonly IDataProtector protector = dataProtectionProvider.CreateProtector("QueenZone.QuizSprint.v1");

    public IReadOnlyList<SprintQuestionView> Questions { get; private set; } = [];

    public SprintResult? Result { get; private set; }

    public string? Ticket { get; private set; }

    public long ServerNowUnixMilliseconds { get; private set; }

    public long ExpiresAtUnixMilliseconds { get; private set; }

    public bool EmptyPool { get; private set; }

    public bool Expired { get; private set; }

    public IReadOnlyList<BreadcrumbItem> Breadcrumbs { get; } =
    [
        BreadcrumbItem.Home,
        new BreadcrumbItem("Quiz", "/quizzes"),
        new BreadcrumbItem("Quiz Sprint", "/quizzes/sprint"),
    ];

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        SetViewData();
        EmptyPool = (await quizRepository.GetPublishedSprintQuestionsAsync(cancellationToken)).Count == 0;
    }

    public async Task<IActionResult> OnPostStartAsync(CancellationToken cancellationToken)
    {
        SetViewData();
        var pool = (await quizRepository.GetPublishedSprintQuestionsAsync(cancellationToken)).ToList();
        if (pool.Count == 0)
        {
            EmptyPool = true;
            return Page();
        }

        Shuffle(pool);
        var selected = pool.Take(QuestionLimit).ToList();
        var ticketQuestions = new List<TicketQuestion>(selected.Count);
        var viewQuestions = new List<SprintQuestionView>(selected.Count);
        foreach (var question in selected)
        {
            var correct = question.Options.Single(option => option.IsCorrect);
            var options = question.Options.ToList();
            Shuffle(options);
            ticketQuestions.Add(new TicketQuestion(
                question.Id,
                question.Text,
                correct.Id,
                correct.Text,
                options.Select(option => option.Id).ToArray()));
            viewQuestions.Add(new SprintQuestionView(
                question.Id,
                question.Text,
                options.Select(option => new SprintOptionView(option.Id, option.Text)).ToList()));
        }

        var now = timeProvider.GetUtcNow();
        ServerNowUnixMilliseconds = now.ToUnixTimeMilliseconds();
        ExpiresAtUnixMilliseconds = now.AddSeconds(DurationSeconds).ToUnixTimeMilliseconds();
        Ticket = protector.Protect(JsonSerializer.Serialize(
            new SprintTicket(ServerNowUnixMilliseconds, ticketQuestions), TicketJsonOptions));
        Questions = viewQuestions;
        return Page();
    }

    public IActionResult OnPostFinish()
    {
        SetViewData();
        if (!Request.Form.TryGetValue("ticket", out var rawTicket) || rawTicket.Count != 1)
        {
            return BadRequest();
        }

        SprintTicket? ticket;
        try
        {
            ticket = JsonSerializer.Deserialize<SprintTicket>(
                protector.Unprotect(rawTicket.ToString()), TicketJsonOptions);
        }
        catch (Exception exception) when (exception is CryptographicException or FormatException or JsonException)
        {
            return BadRequest();
        }

        if (ticket is null || ticket.Questions.Count is < 1 or > QuestionLimit
            || ticket.Questions.Select(question => question.Id).Distinct().Count() != ticket.Questions.Count)
        {
            return BadRequest();
        }

        var now = timeProvider.GetUtcNow();
        var startedAt = DateTimeOffset.FromUnixTimeMilliseconds(ticket.StartedAtUnixMilliseconds);
        if (startedAt > now.AddSeconds(5))
        {
            return BadRequest();
        }

        // A small transit allowance lets an automatic submission at zero reach the server.
        if (now > startedAt.AddSeconds(DurationSeconds + 5))
        {
            Expired = true;
            return Page();
        }

        var answers = new List<SprintReviewItem>();
        foreach (var question in ticket.Questions)
        {
            if (!Request.Form.TryGetValue($"answer_{question.Id:N}", out var rawAnswer)
                || !Guid.TryParse(rawAnswer, out var selectedId)
                || !question.OptionIds.Contains(selectedId))
            {
                continue;
            }

            answers.Add(new SprintReviewItem(
                question.Text,
                selectedId == question.CorrectOptionId,
                question.CorrectOptionText));
        }

        Result = new SprintResult(answers.Count, answers.Count(answer => answer.IsCorrect), answers);
        return Page();
    }

    private void SetViewData()
    {
        ViewData["Title"] = "Quiz Sprint | QueenZone";
        ViewData["CanonicalPath"] = "/quizzes/sprint";
        ViewData["Description"] = "Answer as many Queen questions as you can in 60 seconds.";
    }

    private static void Shuffle<T>(IList<T> items)
    {
        for (var index = items.Count - 1; index > 0; index--)
        {
            var swapIndex = RandomNumberGenerator.GetInt32(index + 1);
            (items[index], items[swapIndex]) = (items[swapIndex], items[index]);
        }
    }

    private sealed record SprintTicket(long StartedAtUnixMilliseconds, IReadOnlyList<TicketQuestion> Questions);

    private sealed record TicketQuestion(
        Guid Id,
        string Text,
        Guid CorrectOptionId,
        string CorrectOptionText,
        IReadOnlyList<Guid> OptionIds);
}
