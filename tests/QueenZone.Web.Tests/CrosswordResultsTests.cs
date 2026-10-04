using QueenZone.Data;

namespace QueenZone.Web.Tests;

public sealed class CrosswordResultsTests
{
    [Fact]
    public void Ranking_uses_first_solve_only_excludes_assists_and_short_times_and_keeps_viewer_outside_top_fifty()
    {
        var puzzle = Guid.NewGuid(); var now = DateTimeOffset.Parse("2026-10-04T12:00:00Z");
        var clean = Enumerable.Range(0, 61).Select(index => new CrosswordCompletion(puzzle, Guid.NewGuid(), 100 + index, true, true, now.AddMinutes(index))).ToArray();
        var assisted = new CrosswordCompletion(puzzle, Guid.NewGuid(), 30, false, false, now);
        var laterClean = assisted with { ElapsedSeconds = 40, Clean = true, RankingEligible = true, CompletedAt = now.AddMinutes(1) };
        var tooFast = new CrosswordCompletion(puzzle, Guid.NewGuid(), 1, true, false, now);
        var result = CrosswordResults.Rank(clean.Concat([assisted, laterClean, tooFast]), clean[60].MemberId);
        Assert.Equal(50, result.Top.Count); Assert.Equal(61, result.TotalMembers); Assert.Equal(61, result.Viewer!.Rank);
        Assert.Equal(clean[0].MemberId, result.Top[0].MemberId); Assert.Equal(100, result.Top[0].ElapsedSeconds);
        Assert.Null(CrosswordResults.Rank([assisted, laterClean, tooFast], assisted.MemberId).Viewer);
        Assert.Empty(CrosswordResults.Rank([], null).Top);
    }

    [Fact]
    public void Weekly_streak_observes_Monday_UTC_boundaries_and_current_week_grace_period()
    {
        var now = DateTimeOffset.Parse("2026-10-05T00:01:00Z");
        CrosswordCompletion Solve(string date) => new(Guid.NewGuid(), Guid.NewGuid(), 100, true, true, DateTimeOffset.Parse(date));
        var solves = new[] { Solve("2026-09-21T12:00:00Z"), Solve("2026-09-27T23:59:00Z"), Solve("2026-10-04T23:59:00Z") };
        Assert.Equal(2, CrosswordResults.WeeklyStreak(solves, now));
        Assert.Equal(3, CrosswordResults.WeeklyStreak(solves.Append(Solve("2026-10-05T00:00:00Z")), now));
        Assert.Equal(0, CrosswordResults.WeeklyStreak(solves, now.AddDays(7)));
        Assert.Equal(0, CrosswordResults.WeeklyStreak([Solve("2026-10-12T00:00:00Z")], now));
        Assert.Equal(0, CrosswordResults.WeeklyStreak([], now));
        Assert.Equal(2, CrosswordResults.WeeklyStreak(solves, DateTimeOffset.Parse("2026-10-04T20:01:00-04:00")));
    }
}
