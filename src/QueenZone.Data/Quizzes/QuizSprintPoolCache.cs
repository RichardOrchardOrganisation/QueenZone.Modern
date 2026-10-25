namespace QueenZone.Data;

/// <summary>
/// Process-wide snapshot of the published Quiz Sprint question pool. Starting a sprint otherwise
/// reloads every published question and option from SQL, and that pool only changes when an admin
/// publishes, unpublishes, edits or deletes a quiz. <see cref="EfQuizRepository"/> calls
/// <see cref="Invalidate"/> on each of those writes; the TTL is the fallback for writers outside
/// the repository (for example member account deletion removing authored quizzes).
/// </summary>
/// <remarks>
/// The cached list and its options are shared between requests and must be treated as immutable:
/// callers copy before shuffling.
/// </remarks>
public sealed class QuizSprintPoolCache(TimeProvider timeProvider)
{
    public static readonly TimeSpan Ttl = TimeSpan.FromMinutes(2);

    private readonly SemaphoreSlim loadGate = new(1, 1);
    private Snapshot? snapshot;
    private int version;

    public async Task<IReadOnlyList<QuizSprintQuestion>> GetAsync(
        Func<CancellationToken, Task<IReadOnlyList<QuizSprintQuestion>>> load,
        CancellationToken cancellationToken = default)
    {
        if (TryGetFresh(out var cached))
        {
            return cached;
        }

        // One caller reloads; concurrent callers wait and reuse its result.
        await loadGate.WaitAsync(cancellationToken);
        try
        {
            if (TryGetFresh(out cached))
            {
                return cached;
            }

            var versionBeforeLoad = Volatile.Read(ref version);
            var pool = await load(cancellationToken);
            if (versionBeforeLoad == Volatile.Read(ref version))
            {
                Volatile.Write(ref snapshot, new Snapshot(pool, timeProvider.GetUtcNow().Add(Ttl)));
            }

            return pool;
        }
        finally
        {
            loadGate.Release();
        }
    }

    /// <summary>Drops the snapshot; a load already in flight will not store its (now stale) result.</summary>
    public void Invalidate()
    {
        Interlocked.Increment(ref version);
        Volatile.Write(ref snapshot, null);
    }

    private bool TryGetFresh(out IReadOnlyList<QuizSprintQuestion> pool)
    {
        var current = Volatile.Read(ref snapshot);
        if (current is not null && timeProvider.GetUtcNow() < current.ExpiresAt)
        {
            pool = current.Pool;
            return true;
        }

        pool = [];
        return false;
    }

    private sealed record Snapshot(IReadOnlyList<QuizSprintQuestion> Pool, DateTimeOffset ExpiresAt);
}
