using QueenZone.Data;

namespace QueenZone.Web;

internal static class CrosswordResultsReader
{
    public static async Task<CrosswordLeaderboardDto> LeaderboardAsync(Guid puzzleId, Guid? viewer,
        ICrosswordProgressRepository progress, IMemberAccountRepository members, CancellationToken cancellationToken)
    {
        var ranked = CrosswordResults.Rank(await progress.GetCompletionsAsync(puzzleId, null, cancellationToken), viewer);
        var names = await QuizLeaderboardNameReader.LoadAsync(members, ranked.Top.Select(row => row.MemberId), ranked.Viewer?.MemberId, cancellationToken);
        CrosswordLeaderboardEntryDto Project(CrosswordRankedSolve row) => new(row.Rank,
            QuizLeaderboardNameReader.DisplayName(names, row.MemberId), row.ElapsedSeconds, row.CompletedAt);
        return new(ranked.Top.Select(Project).ToArray(), ranked.Viewer is { } mine ? Project(mine) : null, ranked.TotalMembers);
    }

    public static async Task<CrosswordHistoryDto> HistoryAsync(Guid member, ICrosswordCatalogRepository catalog,
        ICrosswordProgressRepository progress, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var completed = (await progress.GetCompletionsAsync(null, member, cancellationToken)).Where(row => row.CompletedAt <= now)
            .GroupBy(row => row.CrosswordId).Select(rows => rows.OrderBy(row => row.CompletedAt).First()).ToArray();
        var puzzles = (await catalog.GetAllAsync(cancellationToken)).ToDictionary(row => row.Id);
        var items = completed.OrderByDescending(row => row.CompletedAt).Select(row =>
        {
            puzzles.TryGetValue(row.CrosswordId, out var puzzle);
            return new CrosswordHistoryEntryDto(row.CrosswordId, puzzle?.Seed.Slug, puzzle?.Seed.Title ?? "Unavailable crossword",
                row.ElapsedSeconds, row.Clean, row.CompletedAt, puzzle is not null && CrosswordVisibility.IsPlayable(puzzle, now));
        }).ToArray();
        return new(items, completed.Length, CrosswordResults.WeeklyStreak(completed, now), "UTC (Monday start)");
    }
}
