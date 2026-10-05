namespace QueenZone.Data;

public sealed record CrosswordEntryRevealCount(int Number, CrosswordDirection Direction, int Count);
public sealed record CrosswordStatistics(int Starts, int Completions, double CompletionRate, double CleanSolveRate,
    double? MedianSeconds, IReadOnlyList<CrosswordEntryRevealCount> MostRevealed)
{
    public static CrosswordStatistics Calculate(CrosswordCatalogItem puzzle, IReadOnlyList<CrosswordProgress> progress,
        IReadOnlyList<CrosswordCompletion> completed)
    {
        var starts = progress.Count;
        var times = completed.Select(row => row.ElapsedSeconds).Order().ToArray();
        double? median = times.Length switch
        {
            0 => null,
            var count when count % 2 == 1 => times[count / 2],
            var count => ((double)times[count / 2 - 1] + times[count / 2]) / 2,
        };
        var current = progress.Where(row => row.PlayVersion == puzzle.PlayVersion).ToArray();
        var entries = CrosswordGridValidator.Validate(puzzle.Seed.Grid).Runs.Select(run =>
        {
            var cells = Enumerable.Range(0, run.Answer.Length).Select(offset =>
                (run.Row + (run.Direction == CrosswordDirection.Down ? offset : 0)) * puzzle.Seed.Grid.Width +
                run.Column + (run.Direction == CrosswordDirection.Across ? offset : 0)).ToHashSet();
            return new CrosswordEntryRevealCount(run.Number, run.Direction, current.Count(row => row.RevealedCells.Any(cells.Contains)));
        }).Where(entry => entry.Count > 0).OrderByDescending(entry => entry.Count).ThenBy(entry => entry.Number)
            .ThenBy(entry => entry.Direction).Take(5).ToArray();
        return new(starts, completed.Count, starts == 0 ? 0 : (double)completed.Count / starts,
            completed.Count == 0 ? 0 : (double)completed.Count(row => row.Clean) / completed.Count, median, entries);
    }
}
