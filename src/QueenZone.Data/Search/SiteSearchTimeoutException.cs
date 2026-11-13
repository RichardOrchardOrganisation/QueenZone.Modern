namespace QueenZone.Data;

/// <summary>
/// SQL command timeout while running <c>dbo.SearchDocument_Search</c>.
/// Callers must fail-soft (API 504, in-page unavailable) rather than 500 or an empty 200.
/// </summary>
public sealed class SiteSearchTimeoutException : Exception
{
    public SiteSearchTimeoutException(int queryLength, int termCount, TimeSpan duration, Exception innerException)
        : base("Site search SQL command timed out.", innerException)
    {
        QueryLength = queryLength;
        TermCount = termCount;
        Duration = duration;
    }

    public int QueryLength { get; }

    public int TermCount { get; }

    public TimeSpan Duration { get; }
}
