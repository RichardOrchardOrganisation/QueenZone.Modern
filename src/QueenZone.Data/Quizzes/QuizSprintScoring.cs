namespace QueenZone.Data;

/// <summary>Final tally for a Quiz Sprint run.</summary>
public sealed record QuizSprintScore(int Points, int Correct, int Answered, int BestStreak);

public static class QuizSprintScoring
{
    /// <summary>A streak of this many correct answers doubles every further correct answer.</summary>
    public const int StreakBonusThreshold = 3;

    /// <summary>
    /// Scores answers in the order they were given: +1 per correct answer, or +2 once the current
    /// streak is <see cref="StreakBonusThreshold"/> or more. A wrong answer resets the streak.
    /// <c>null</c> marks an unanswered question, which breaks the streak but is not counted as answered.
    /// </summary>
    public static QuizSprintScore Score(IEnumerable<bool?> answers)
    {
        int points = 0, correct = 0, answered = 0, streak = 0, bestStreak = 0;
        foreach (var answer in answers)
        {
            if (answer is null)
            {
                streak = 0;
                continue;
            }

            answered++;
            if (answer.Value)
            {
                points += streak >= StreakBonusThreshold ? 2 : 1;
                correct++;
                streak++;
                bestStreak = Math.Max(bestStreak, streak);
            }
            else
            {
                streak = 0;
            }
        }

        return new QuizSprintScore(points, correct, answered, bestStreak);
    }
}
