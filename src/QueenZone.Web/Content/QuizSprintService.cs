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
        if (string.IsNullOrEmpty(rawTicket))
        {
            return new SprintFinishOutcome(SprintFinishStatus.Invalid);
        }

        SprintTicket? ticket;
        try
        {
            ticket = JsonSerializer.Deserialize<SprintTicket>(protector.Unprotect(rawTicket), TicketJsonOptions);
        }
        catch (Exception exception) when (exception is CryptographicException or FormatException or JsonException)
        {
            return new SprintFinishOutcome(SprintFinishStatus.Invalid);
        }

        if (ticket is null || ticket.Questions.Count is < 1 or > QuestionLimit
            || ticket.Questions.Select(question => question.Id).Distinct().Count() != ticket.Questions.Count)
        {
            return new SprintFinishOutcome(SprintFinishStatus.Invalid);
        }

        var now = timeProvider.GetUtcNow();
        var startedAt = DateTimeOffset.FromUnixTimeMilliseconds(ticket.StartedAtUnixMilliseconds);
        if (startedAt > now.AddSeconds(5))
        {
            return new SprintFinishOutcome(SprintFinishStatus.Invalid);
        }

        // A small transit allowance lets an automatic submission at zero reach the server.
        if (now > startedAt.AddSeconds(DurationSeconds + 5))
        {
            return new SprintFinishOutcome(SprintFinishStatus.Expired);
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
