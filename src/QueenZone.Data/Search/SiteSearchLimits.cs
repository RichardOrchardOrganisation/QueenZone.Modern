namespace QueenZone.Data;

/// <summary>
/// Caps for <c>dbo.SearchDocument_Search</c>. Common terms such as "queen" or "freddie"
/// otherwise score every <c>SearchDocument</c> row twice (page + count) and hit the 30-second
/// SQL command timeout, which the mobile client surfaces as "Unable to reach QueenZone".
/// </summary>
public static class SiteSearchLimits
{
    /// <summary>
    /// Rank-window size. Untyped (All) search applies this as <c>FREETEXTTABLE</c>
    /// <c>top_n_by_rank</c> so common terms stay inside the command timeout. Typed search
    /// filters <c>ContentType</c> first, then keeps this many hits. Pagination still works
    /// up to this many hits; <c>totalCount</c> is never larger.
    /// </summary>
    public const int MaxRankedMatches = 1000;

    /// <summary>
    /// Maximum full-text candidates scanned for a typed search before the content-type filter
    /// and final ranked-match cap are applied. This keeps rare content types discoverable without
    /// allowing a common term to rank the entire search corpus.
    /// </summary>
    public const int TypedMatchScanLimit = 5000;

    /// <summary>
    /// Minimum trimmed query length that is sent to SQL. Shorter input returns an empty page
    /// without executing <c>dbo.SearchDocument_Search</c>.
    /// </summary>
    public const int MinQueryLength = 2;

    /// <summary>
    /// Deepest result page website and API search will materialize. Deeper pages return an
    /// empty result list with the true rank-capped <c>totalCount</c>, not an error.
    /// </summary>
    public const int MaxPage = 10;

    /// <summary>
    /// Upper bound for a single search page. Website, API, cache keys, and both search
    /// services clamp to this so key and execution stay aligned.
    /// </summary>
    public const int MaxPageSize = 100;

    /// <summary>
    /// True when <paramref name="query"/> is empty, whitespace, or shorter than
    /// <see cref="MinQueryLength"/> after trimming.
    /// </summary>
    public static bool IsBelowMinimumLength(string? query) =>
        string.IsNullOrWhiteSpace(query) || query.Trim().Length < MinQueryLength;

    /// <summary>
    /// True when <paramref name="page"/> is past <see cref="MaxPage"/>. Page numbers below 1
    /// are treated as page 1.
    /// </summary>
    public static bool IsBeyondMaxPage(int page) => NormalizePage(page) > MaxPage;

    public static int NormalizePage(int page) => Math.Max(page, 1);

    public static int NormalizePageSize(int pageSize) => Math.Clamp(pageSize, 1, MaxPageSize);
}
