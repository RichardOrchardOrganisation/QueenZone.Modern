using QueenZone.Data.Entities;

namespace QueenZone.Data;

public sealed class InMemoryCrosswordProgressRepository : ICrosswordProgressRepository
{
    private readonly object gate = new();
    private readonly Dictionary<(Guid Puzzle, Guid Member), CrosswordProgressEntity> progress = [];
    private readonly Dictionary<(Guid Puzzle, Guid Member), CrosswordCompletionEntity> completions = [];

    private readonly InMemoryCrosswordCatalogRepository catalog;
    private readonly TimeProvider clock;
    private readonly CrosswordRankingOptions options;

    public InMemoryCrosswordProgressRepository(InMemoryCrosswordCatalogRepository catalog, TimeProvider clock, CrosswordRankingOptions options)
    {
        this.catalog = catalog;
        this.clock = clock;
        this.options = options;
        catalog.ResetInProgress += Reset;
    }

    private void Reset(CrosswordCatalogItem puzzle)
    {
        lock (gate)
        {
            foreach (var row in progress.Values.Where(row => row.CrosswordId == puzzle.Id))
                CrosswordProgressMapping.Reset(row, puzzle);
        }
    }

    public Task<CrosswordProgress?> GetAsync(Guid crosswordId, Guid memberId, CancellationToken cancellationToken = default)
    {
        lock (gate)
        {
            return Task.FromResult(progress.TryGetValue((crosswordId, memberId), out var entity)
                ? CrosswordProgressMapping.Read(entity) : null);
        }
    }

    public Task<IReadOnlyList<CrosswordProgress>> GetForPuzzleAsync(Guid crosswordId, CancellationToken cancellationToken = default)
    {
        lock (gate)
        {
            IReadOnlyList<CrosswordProgress> rows = progress.Values.Where(row => row.CrosswordId == crosswordId)
                .Select(CrosswordProgressMapping.Read).ToArray();
            return Task.FromResult(rows);
        }
    }

    public Task<IReadOnlyList<CrosswordProgress>> GetForMemberAsync(Guid memberId, CancellationToken cancellationToken = default)
    {
        lock (gate)
        {
            IReadOnlyList<CrosswordProgress> result = progress.Values.Where(row => row.MemberId == memberId)
                .Select(CrosswordProgressMapping.Read).ToArray();
            return Task.FromResult(result);
        }
    }

    public Task<IReadOnlyList<CrosswordCompletion>> GetCompletionsAsync(Guid? crosswordId, Guid? memberId,
        CancellationToken cancellationToken = default)
    {
        lock (gate)
        {
            IReadOnlyList<CrosswordCompletion> result = completions.Values
                .Where(row => (crosswordId is null || row.CrosswordId == crosswordId) && (memberId is null || row.MemberId == memberId))
                .Select(CrosswordProgressMapping.Read).ToArray();
            return Task.FromResult(result);
        }
    }

    public Task<CrosswordProgress> SaveAsync(Guid crosswordId, Guid memberId, CrosswordProgressWrite write,
        CancellationToken cancellationToken = default) =>
        MutateAsync(crosswordId, memberId, state => state.Save(write), cancellationToken);

    public async Task MarkAssistanceAsync(Guid crosswordId, Guid memberId, IReadOnlyList<int> revealedCells,
        bool autoCheckUsed, Guid playVersion, CancellationToken cancellationToken = default) =>
        await MutateAsync(crosswordId, memberId, state => { state.MarkAssistance(revealedCells, autoCheckUsed, playVersion); return true; }, cancellationToken);

    public Task<CrosswordCompletionResult> CompleteAsync(Guid crosswordId, Guid memberId, CrosswordProgressWrite write,
        CancellationToken cancellationToken = default) =>
        MutateAsync(crosswordId, memberId, state => state.Complete(write, options), cancellationToken);

    private Task<T> MutateAsync<T>(Guid crosswordId, Guid memberId, Func<CrosswordProgressState, T> action,
        CancellationToken cancellationToken) => Task.FromResult(catalog.WithPlayablePuzzle(crosswordId, clock.GetUtcNow(), puzzle =>
    {
        lock (gate)
        {
            var key = (crosswordId, memberId);
            progress.TryGetValue(key, out var current);
            completions.TryGetValue(key, out var completed);
            var state = new CrosswordProgressState(puzzle, memberId, clock.GetUtcNow(), current, completed);
            var result = action(state);
            progress[key] = state.Progress;
            if (state.Completion is not null)
            {
                completions[key] = state.Completion;
            }
            return result;
        }
    }));
}
