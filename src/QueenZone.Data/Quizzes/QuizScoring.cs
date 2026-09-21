using QueenZone.Data.Entities;

namespace QueenZone.Data;

internal static class QuizScoring
{
    public static QuizSubmissionResult Score(
        QuizEntity quiz,
        IReadOnlyList<QuizAnswerSubmission> answers,
        bool recorded)
    {
        var answersByQuestion = answers.ToDictionary(answer => answer.QuestionId, answer => answer.SelectedOptionId);
        var results = new List<QuizAnswerResult>();
        var score = 0;
        var maxScore = 0;
        var correctCount = 0;

        foreach (var question in quiz.Questions.OrderBy(question => question.DisplayOrder))
        {
            var correctOption = question.Options.Single(option => option.IsCorrect);
            answersByQuestion.TryGetValue(question.Id, out var selectedOptionId);
            var selectedOption = selectedOptionId is Guid selectedId
                ? question.Options.SingleOrDefault(option => option.Id == selectedId)
                : null;
            var isCorrect = selectedOption is not null && selectedOption.Id == correctOption.Id;
            var pointsAwarded = isCorrect ? question.Points : 0;

            maxScore += question.Points;
            score += pointsAwarded;
            if (isCorrect)
            {
                correctCount++;
            }

            results.Add(new QuizAnswerResult(
                question.Id,
                question.QuestionText,
                selectedOption?.Id,
                selectedOption?.OptionText,
                correctOption.Id,
                correctOption.OptionText,
                isCorrect,
                pointsAwarded));
        }

        return new QuizSubmissionResult(
            quiz.Id,
            quiz.Title,
            score,
            maxScore,
            correctCount,
            quiz.Questions.Count,
            recorded,
            results);
    }

    public static DateTimeOffset GetCurrentDayStartUtc(DateTimeOffset now) =>
        new(now.UtcDateTime.Date, TimeSpan.Zero);

    public static QuizSprintBoardResult BuildSprintBoard(
        IEnumerable<QuizSprintRunEntity> runs,
        Guid? viewerMemberId,
        int top)
    {
        var ranked = runs
            .GroupBy(run => run.MemberAccountId)
            .Select(group => (
                Best: group.OrderByDescending(run => run.Score).ThenBy(run => run.CompletedAt).First(),
                Runs: group.Count()))
            .OrderByDescending(row => row.Best.Score)
            .ThenBy(row => row.Best.CompletedAt)
            .ThenBy(row => row.Best.MemberAccountId)
            .Select((row, index) => new QuizSprintLeaderboardEntry(
                index + 1,
                row.Best.MemberAccountId,
                row.Best.Score,
                row.Best.BestStreak,
                row.Best.AnsweredCount,
                row.Best.CompletedAt,
                row.Runs))
            .ToList();

        var viewer = viewerMemberId is Guid memberId
            ? ranked.SingleOrDefault(entry => entry.MemberAccountId == memberId)
            : null;

        return new QuizSprintBoardResult(ranked.Take(top).ToList(), viewer, ranked.Count);
    }

    /// <summary>Ranks members by points summed over all their runs; ties fall back to member id.</summary>
    public static QuizSprintBoardResult BuildSprintTotalBoard(
        IEnumerable<QuizSprintRunEntity> runs,
        Guid? viewerMemberId,
        int top)
    {
        var ranked = runs
            .GroupBy(run => run.MemberAccountId)
            .Select(group => (
                MemberAccountId: group.Key,
                Points: group.Sum(run => run.Score),
                BestStreak: group.Max(run => run.BestStreak),
                Answered: group.Sum(run => run.AnsweredCount),
                Runs: group.Count()))
            .OrderByDescending(row => row.Points)
            .ThenBy(row => row.MemberAccountId)
            .Select((row, index) => new QuizSprintLeaderboardEntry(
                index + 1,
                row.MemberAccountId,
                row.Points,
                row.BestStreak,
                row.Answered,
                default,
                row.Runs))
            .ToList();

        var viewer = viewerMemberId is Guid memberId
            ? ranked.SingleOrDefault(entry => entry.MemberAccountId == memberId)
            : null;

        return new QuizSprintBoardResult(ranked.Take(top).ToList(), viewer, ranked.Count);
    }

    public static DateTimeOffset GetCurrentWeekStartUtc(DateTimeOffset now)
    {
        var today = now.UtcDateTime.Date;
        var daysSinceMonday = ((int)today.DayOfWeek + 6) % 7;
        return new DateTimeOffset(today.AddDays(-daysSinceMonday), TimeSpan.Zero);
    }

    public static QuizLeaderboardResult BuildLeaderboard(
        IEnumerable<QuizAttemptEntity> attempts,
        Guid? viewerMemberId,
        int top)
    {
        var ranked = attempts
            .GroupBy(attempt => attempt.MemberAccountId)
            .Select(group => new
            {
                MemberAccountId = group.Key,
                Score = group.Sum(attempt => attempt.Score),
                AttemptCount = group.Count(),
            })
            .OrderByDescending(row => row.Score)
            .ThenBy(row => row.MemberAccountId)
            .Select((row, index) => new QuizLeaderboardEntry(index + 1, row.MemberAccountId, row.Score, row.AttemptCount))
            .ToList();

        var viewer = viewerMemberId is Guid memberId
            ? ranked.SingleOrDefault(entry => entry.MemberAccountId == memberId)
            : null;

        return new QuizLeaderboardResult(ranked.Take(top).ToList(), viewer, ranked.Count);
    }
}
