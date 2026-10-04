using System.Data;
using Microsoft.EntityFrameworkCore;

namespace QueenZone.Data;

public sealed class EfCrosswordProgressRepository(QueenZoneDbContext db, TimeProvider clock,
    CrosswordRankingOptions options) : ICrosswordProgressRepository
{
    public async Task<CrosswordProgress?> GetAsync(Guid crosswordId, Guid memberId, CancellationToken cancellationToken = default)
    {
        var entity = await db.CrosswordProgress.AsNoTracking().SingleOrDefaultAsync(row =>
            row.CrosswordId == crosswordId && row.MemberId == memberId, cancellationToken);
        return entity is null ? null : CrosswordProgressMapping.Read(entity);
    }

    public async Task<IReadOnlyList<CrosswordProgress>> GetForPuzzleAsync(Guid crosswordId, CancellationToken cancellationToken = default)
    {
        var rows = await db.CrosswordProgress.AsNoTracking().Where(row => row.CrosswordId == crosswordId).ToListAsync(cancellationToken);
        return rows.Select(CrosswordProgressMapping.Read).ToArray();
    }

    public async Task<IReadOnlyList<CrosswordProgress>> GetForMemberAsync(Guid memberId, CancellationToken cancellationToken = default)
    {
        var rows = await db.CrosswordProgress.AsNoTracking().Where(row => row.MemberId == memberId).ToListAsync(cancellationToken);
        return rows.Select(CrosswordProgressMapping.Read).ToArray();
    }

    public async Task<IReadOnlyList<CrosswordCompletion>> GetCompletionsAsync(Guid? crosswordId, Guid? memberId,
        CancellationToken cancellationToken = default)
    {
        var rows = await db.CrosswordCompletions.AsNoTracking().Where(row =>
            (crosswordId == null || row.CrosswordId == crosswordId) && (memberId == null || row.MemberId == memberId))
            .ToListAsync(cancellationToken);
        return rows.Select(CrosswordProgressMapping.Read).ToArray();
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
        CancellationToken cancellationToken) => QueenZoneDbTransactions.ExecuteAsync(db, IsolationLevel.Serializable, async token =>
        {
            var entity = await db.Crosswords.AsNoTracking().Include(puzzle => puzzle.Entries)
                .SingleOrDefaultAsync(puzzle => puzzle.Id == crosswordId, token);
            var puzzle = entity is null ? null : CrosswordCatalogMapping.Read(entity);
            if (puzzle is null || !CrosswordVisibility.IsPlayable(puzzle, clock.GetUtcNow()))
            {
                throw new KeyNotFoundException("No playable crossword with that id.");
            }
            var current = await db.CrosswordProgress.SingleOrDefaultAsync(row =>
                row.CrosswordId == crosswordId && row.MemberId == memberId, token);
            var completed = await db.CrosswordCompletions.SingleOrDefaultAsync(row =>
                row.CrosswordId == crosswordId && row.MemberId == memberId, token);
            var state = new CrosswordProgressState(puzzle, memberId, clock.GetUtcNow(), current, completed);
            var result = action(state);
            if (current is null)
            {
                db.CrosswordProgress.Add(state.Progress);
            }
            if (completed is null && state.Completion is not null)
            {
                db.CrosswordCompletions.Add(state.Completion);
            }
            if (!db.Database.IsSqlServer())
            {
                state.Progress.RowVersion = QueenZoneConcurrency.NewClientRowVersion();
            }
            await QueenZoneConcurrency.SaveChangesAsync(db, token);
            return result;
        }, cancellationToken);
}
