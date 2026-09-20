using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using QueenZone.Data;

namespace QueenZone.Web;

public sealed record SprintOptionView(Guid Id, string Text);

public sealed record SprintQuestionView(Guid Id, string Text, IReadOnlyList<SprintOptionView> Options);

public sealed record SprintRound(
    string Ticket,
    long ServerNowUnixMilliseconds,
    long ExpiresAtUnixMilliseconds,
    IReadOnlyList<SprintQuestionView> Questions);

public sealed record SprintReviewItem(Guid QuestionId, string QuestionText, bool IsCorrect, string CorrectAnswer);

/// <param name="Recorded">True when the run was saved to the daily leaderboard (signed-in members only).</param>
/// <param name="Rank">The member's rank on today's board after this run, when recorded.</param>
public sealed record SprintResult(
    int Attempted,
    int Correct,
    int Points,
    int BestStreak,
    bool Recorded,
    int? Rank,
    IReadOnlyList<SprintReviewItem> Answers);

public sealed record SprintBoardRow(int Rank, string DisplayName, int Score, int BestStreak, bool IsViewer);

/// <summary>Today's standings with display names resolved; <c>Viewer</c> is set even outside <c>Rows</c>.</summary>
public sealed record SprintBoard(IReadOnlyList<SprintBoardRow> Rows, SprintBoardRow? Viewer, int PlayersToday);

/// <summary>Whether a picked option was right, plus the right option so the client can reveal it.</summary>
public sealed record SprintAnswerCheck(bool IsCorrect, Guid CorrectOptionId);

public enum SprintFinishStatus
{
    Completed,
    Expired,
    Invalid,
}

public sealed record SprintFinishOutcome(SprintFinishStatus Status, SprintResult? Result = null);

/// <summary>
/// Runs a 60-second Quiz Sprint. The round is a signed ticket carrying the answer key, so the
/// client never sees correct answers and cannot alter the start time; scoring (with the streak
/// bonus) and leaderboard recording happen here, server-side.
/// </summary>
public sealed class QuizSprintService(
    IQuizRepository quizRepository,
    IMemberAccountRepository memberAccountRepository,
    IDataProtectionProvider dataProtectionProvider,
    TimeProvider timeProvider)
{
    public const int DurationSeconds = 60;
    private const int QuestionLimit = 60;
    private static readonly JsonSerializerOptions TicketJsonOptions = new(JsonSerializerDefaults.Web);
    private readonly IDataProtector protector = dataProtectionProvider.CreateProtector("QueenZone.QuizSprint.v1");

    public async Task<bool> HasQuestionsAsync(CancellationToken cancellationToken) =>
        (await quizRepository.GetPublishedSprintQuestionsAsync(cancellationToken)).Count > 0;

    /// <summary>Starts a round, or returns null when no published questions exist.</summary>
    public async Task<SprintRound?> StartAsync(CancellationToken cancellationToken)
    {
        var pool = (await quizRepository.GetPublishedSprintQuestionsAsync(cancellationToken)).ToList();
        if (pool.Count == 0)
        {
            return null;
        }

        Shuffle(pool);
        var ticketQuestions = new List<TicketQuestion>();
        var viewQuestions = new List<SprintQuestionView>();
        foreach (var question in pool.Take(QuestionLimit))
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
        var startedAt = now.ToUnixTimeMilliseconds();
        var ticket = protector.Protect(JsonSerializer.Serialize(
            new SprintTicket(startedAt, ticketQuestions), TicketJsonOptions));
        return new SprintRound(ticket, startedAt, now.AddSeconds(DurationSeconds).ToUnixTimeMilliseconds(), viewQuestions);
    }

    /// <summary>
    /// Verifies the ticket and deadline, scores <paramref name="selections"/> (question id to option id)
    /// in the order the questions were served, and records the run when <paramref name="memberId"/> is set.
    /// </summary>
    public async Task<SprintFinishOutcome> FinishAsync(
        string? rawTicket,
        IReadOnlyDictionary<Guid, Guid> selections,
        Guid? memberId,
        CancellationToken cancellationToken)
    {
        var (status, ticket) = ReadTicket(rawTicket);
        if (ticket is null)
        {
            return new SprintFinishOutcome(status);
        }

        var outcomes = new List<bool?>(ticket.Questions.Count);
        var review = new List<SprintReviewItem>();
        foreach (var question in ticket.Questions)
        {
            if (!selections.TryGetValue(question.Id, out var selectedId) || !question.OptionIds.Contains(selectedId))
            {
                outcomes.Add(null);
                continue;
            }

            var isCorrect = selectedId == question.CorrectOptionId;
            outcomes.Add(isCorrect);
            review.Add(new SprintReviewItem(question.Id, question.Text, isCorrect, question.CorrectOptionText));
        }

        var score = QuizSprintScoring.Score(outcomes);
        int? rank = null;
        if (memberId is Guid member && score.Answered > 0)
        {
            await quizRepository.RecordSprintRunAsync(member, score, cancellationToken);
            rank = (await quizRepository.GetSprintDailyBoardAsync(member, 1, cancellationToken)).Viewer?.Rank;
        }

        return new SprintFinishOutcome(
            SprintFinishStatus.Completed,
            new SprintResult(score.Answered, score.Correct, score.Points, score.BestStreak, rank is not null, rank, review));
    }

    /// <summary>
    /// Reveals whether one pick was right while a round is still live, so the client can show
    /// per-answer feedback without ever holding the answer key. Null for a bad or expired ticket.
    /// </summary>
    public SprintAnswerCheck? CheckAnswer(string? rawTicket, Guid questionId, Guid optionId)
    {
        var (_, ticket) = ReadTicket(rawTicket);
        var question = ticket?.Questions.FirstOrDefault(item => item.Id == questionId);
        if (question is null || !question.OptionIds.Contains(optionId))
        {
            return null;
        }

        return new SprintAnswerCheck(optionId == question.CorrectOptionId, question.CorrectOptionId);
    }

    /// <summary>Today's standings with display names, marking the viewer's own row.</summary>
    public async Task<SprintBoard> GetBoardAsync(Guid? viewerMemberId, int top, CancellationToken cancellationToken)
    {
        var board = await quizRepository.GetSprintDailyBoardAsync(viewerMemberId, top, cancellationToken);

        async Task<SprintBoardRow> ToRowAsync(QuizSprintLeaderboardEntry entry)
        {
            var account = await memberAccountRepository.FindByIdAsync(entry.MemberAccountId, cancellationToken);
            return new SprintBoardRow(
                entry.Rank,
                account?.DisplayName ?? "Member",
                entry.Score,
                entry.BestStreak,
                entry.MemberAccountId == viewerMemberId);
        }

        var rows = new List<SprintBoardRow>(board.Top.Count);
        foreach (var entry in board.Top)
        {
            rows.Add(await ToRowAsync(entry));
        }

        var viewer = board.Viewer is null ? null : await ToRowAsync(board.Viewer);
        return new SprintBoard(rows, viewer, board.PlayersToday);
    }

    private (SprintFinishStatus Status, SprintTicket? Ticket) ReadTicket(string? rawTicket)
    {
        if (string.IsNullOrEmpty(rawTicket))
        {
            return (SprintFinishStatus.Invalid, null);
        }

        SprintTicket? ticket;
        try
        {
            ticket = JsonSerializer.Deserialize<SprintTicket>(protector.Unprotect(rawTicket), TicketJsonOptions);
        }
        catch (Exception exception) when (exception is CryptographicException or FormatException or JsonException)
        {
            return (SprintFinishStatus.Invalid, null);
        }

        if (ticket is null || ticket.Questions.Count is < 1 or > QuestionLimit
            || ticket.Questions.Select(question => question.Id).Distinct().Count() != ticket.Questions.Count)
        {
            return (SprintFinishStatus.Invalid, null);
        }

        var now = timeProvider.GetUtcNow();
        var startedAt = DateTimeOffset.FromUnixTimeMilliseconds(ticket.StartedAtUnixMilliseconds);
        if (startedAt > now.AddSeconds(5))
        {
            return (SprintFinishStatus.Invalid, null);
        }

        // A small transit allowance lets an automatic submission at zero reach the server.
        if (now > startedAt.AddSeconds(DurationSeconds + 5))
        {
            return (SprintFinishStatus.Expired, null);
        }

        return (SprintFinishStatus.Completed, ticket);
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
