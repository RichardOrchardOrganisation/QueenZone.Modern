using QueenZone.Data;

namespace QueenZone.Web.Tests;

public sealed class QuizSprintScoringTests
{
    [Fact]
    public void Scores_one_point_per_correct_answer_below_the_streak_threshold()
    {
        var score = QuizSprintScoring.Score([true, true, false, true]);

        Assert.Equal(new QuizSprintScore(3, 3, 4, 2), score);
    }

    [Fact]
    public void Doubles_every_correct_answer_after_three_in_a_row()
    {
        // 1 + 1 + 1, then the streak is 3 so the next two score 2 each.
        var score = QuizSprintScoring.Score([true, true, true, true, true]);

        Assert.Equal(7, score.Points);
        Assert.Equal(5, score.BestStreak);
    }

    [Fact]
    public void A_wrong_answer_resets_the_streak_and_costs_nothing()
    {
        var score = QuizSprintScoring.Score([true, true, true, false, true]);

        Assert.Equal(4, score.Points);
        Assert.Equal(4, score.Correct);
        Assert.Equal(5, score.Answered);
        Assert.Equal(3, score.BestStreak);
    }

    [Fact]
    public void Unanswered_questions_break_the_streak_and_are_not_counted_as_answered()
    {
        var score = QuizSprintScoring.Score([true, true, null, true, true]);

        Assert.Equal(new QuizSprintScore(4, 4, 4, 2), score);
    }

    [Fact]
    public void Empty_run_scores_zero()
    {
        Assert.Equal(new QuizSprintScore(0, 0, 0, 0), QuizSprintScoring.Score([]));
    }
}
