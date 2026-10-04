using QueenZone.Data.Entities;

namespace QueenZone.Data;

internal sealed class CrosswordProgressState(CrosswordCatalogItem puzzle, Guid memberId, DateTimeOffset now,
    CrosswordProgressEntity? progress, CrosswordCompletionEntity? completion)
{
    public CrosswordProgressEntity Progress { get; } = progress ?? CrosswordProgressMapping.Create(puzzle, memberId, now);
    public CrosswordCompletionEntity? Completion { get; private set; } = completion;

    public CrosswordProgress Save(CrosswordProgressWrite write)
    {
        write = CrosswordProgressMapping.Normalize(puzzle.Seed.Grid, write);
        CrosswordProgressMapping.EnsureGrid(Progress, puzzle);
        if (Completion is null)
        {
            CrosswordProgressMapping.Apply(Progress, write);
        }
        return CrosswordProgressMapping.Read(Progress);
    }

    public void MarkAssistance(IReadOnlyList<int> cells, bool autoCheckUsed)
    {
        cells = CrosswordProgressMapping.NormalizeReveals(puzzle.Seed.Grid, cells);
        CrosswordProgressMapping.EnsureGrid(Progress, puzzle);
        if (Completion is null)
        {
            CrosswordProgressMapping.MarkAssistance(Progress, cells, autoCheckUsed);
        }
    }

    public CrosswordCompletionResult Complete(CrosswordProgressWrite write, CrosswordRankingOptions options)
    {
        write = CrosswordProgressMapping.Normalize(puzzle.Seed.Grid, write);
        if (Completion is not null)
        {
            var correct = CrosswordPlayRules.IsComplete(puzzle.Seed.Grid, write.Letters);
            return new(correct, CrosswordProgressMapping.Read(Completion));
        }
        Save(write);
        if (!CrosswordPlayRules.IsComplete(puzzle.Seed.Grid, Progress.Letters))
        {
            return new(false, null);
        }
        Completion = CrosswordProgressMapping.Complete(Progress, puzzle, now, options);
        return new(true, CrosswordProgressMapping.Read(Completion));
    }
}
