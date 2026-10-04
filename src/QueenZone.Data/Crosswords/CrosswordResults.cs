namespace QueenZone.Data;

public sealed record CrosswordRankedSolve(int Rank, Guid MemberId, int ElapsedSeconds, DateTimeOffset CompletedAt);
public sealed record CrosswordRanking(IReadOnlyList<CrosswordRankedSolve> Top, CrosswordRankedSolve? Viewer, int TotalMembers);

public static class CrosswordResults
{
    public static CrosswordRanking Rank(IEnumerable<CrosswordCompletion> completions, Guid? viewer)
    {
        var ranked = completions.GroupBy(row => row.MemberId).Select(rows => rows.OrderBy(row => row.CompletedAt).First())
            .Where(row => row.Clean && row.RankingEligible)
            .OrderBy(row => row.ElapsedSeconds).ThenBy(row => row.CompletedAt).ThenBy(row => row.MemberId)
            .Select((row, index) => new CrosswordRankedSolve(index + 1, row.MemberId, row.ElapsedSeconds, row.CompletedAt)).ToArray();
        return new(ranked.Take(50).ToArray(), ranked.SingleOrDefault(row => row.MemberId == viewer), ranked.Length);
    }

    /// <summary>Monday UTC weeks match quiz leaderboards. An unfinished current week does not break last week's streak.</summary>
    public static int WeeklyStreak(IEnumerable<CrosswordCompletion> completions, DateTimeOffset now)
    {
        var weeks = completions.Where(row => row.CompletedAt <= now)
            .Select(row => QuizScoring.GetCurrentWeekStartUtc(row.CompletedAt)).ToHashSet();
        var current = QuizScoring.GetCurrentWeekStartUtc(now);
        if (!weeks.Contains(current)) current = current.AddDays(-7);
        var count = 0;
        while (weeks.Contains(current)) { count++; current = current.AddDays(-7); }
        return count;
    }
}
