namespace QueenZone.Data;

/// <summary>
/// Process-local search-index generation. Readers keep their starting revision so a write
/// invalidates both completed entries and in-flight reads. Other worker processes and the
/// asynchronous full-text crawl remain bounded by the search cache's short absolute TTL.
/// </summary>
public sealed class SearchIndexRevision
{
    private long value;

    public long Value => Interlocked.Read(ref value);

    public void Advance() => Interlocked.Increment(ref value);
}
