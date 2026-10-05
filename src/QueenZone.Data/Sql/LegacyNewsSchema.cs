using System.Collections.Concurrent;

namespace QueenZone.Data;

/// <summary>
/// Legacy <c>NEWS_T</c> column-availability probes. Latest-row / published projection
/// SQL lives in <see cref="PublishedNewsQuery"/>.
/// </summary>
public static class LegacyNewsSchema
{
    /// <summary>
    /// Process-lifetime cache of COL_LENGTH probes keyed by connection string.
    /// Avoids a SQL round-trip on every scoped news repository construction.
    /// </summary>
    private static readonly ConcurrentDictionary<string, NewsColumnAvailability> ColumnAvailabilityCache =
        new(StringComparer.Ordinal);

    public sealed class NewsColumnAvailability
    {
        public bool HasSourceUrlColumn { get; set; }

        public bool HasSlugColumn { get; set; }

        public bool HasCreatedAtColumn { get; set; }

        public bool HasUpdatedAtColumn { get; set; }

        public bool HasEditorEmailColumn { get; set; }

        public bool HasImageBlobKeyColumn { get; set; }

        public bool HasImageGalleryPicIdColumn { get; set; }

        public bool HasForumTopicIdColumn { get; set; }
    }

    /// <inheritdoc cref="PublishedNewsQuery.BuildPublishedNewsCte"/>
    public static string BuildPublishedNewsCte(
        bool includeSlugColumn,
        bool includeBody = true,
        bool includeImageBlobKeyColumn = false,
        bool includeImageGalleryPicIdColumn = false,
        bool includeForumTopicIdColumn = false) =>
        PublishedNewsQuery.BuildPublishedNewsCte(
            includeSlugColumn,
            includeBody,
            includeImageBlobKeyColumn,
            includeImageGalleryPicIdColumn,
            includeForumTopicIdColumn);

    /// <inheritdoc cref="PublishedNewsQuery.BuildAdminLatestNewsSql"/>
    public static string BuildAdminLatestNewsSql(NewsColumnAvailability columns) =>
        PublishedNewsQuery.BuildAdminLatestNewsSql(columns);

    /// <inheritdoc cref="PublishedNewsQuery.BuildAdminLatestNewsCountSql"/>
    public static string BuildAdminLatestNewsCountSql(NewsColumnAvailability columns) =>
        PublishedNewsQuery.BuildAdminLatestNewsCountSql(columns);

    /// <summary>
    /// Returns whether <c>NEWS_T.SLUG</c> exists. Result is cached for the process lifetime
    /// per connection string (same probe as <see cref="GetNewsColumnAvailability"/>).
    /// </summary>
    internal static bool HasLegacySlugColumn(string connectionString) =>
        GetNewsColumnAvailability(connectionString).HasSlugColumn;

    /// <summary>
    /// Probes legacy <c>NEWS_T</c> column availability once per connection string, then reuses
    /// the result for all subsequent repository constructions in this process.
    /// </summary>
    internal static NewsColumnAvailability GetNewsColumnAvailability(string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        return ColumnAvailabilityCache.GetOrAdd(connectionString, static cs => ProbeNewsColumnAvailability(cs));
    }

    /// <summary>
    /// Probes legacy <c>NEWS_T</c> column availability asynchronously and populates the process
    /// cache, so that <see cref="GetNewsColumnAvailability"/> can return a cached value without
    /// ever touching the database on a request thread. Intended to run once during host startup
    /// (see <c>NewsColumnAvailabilityWarmupService</c>), before the app begins serving requests.
    /// If this fails, the cache is left unpopulated and <see cref="GetNewsColumnAvailability"/>
    /// falls back to probing synchronously on first use — a rare degraded path, not the norm.
    /// </summary>
    internal static async Task WarmupAsync(string connectionString, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        if (ColumnAvailabilityCache.ContainsKey(connectionString))
        {
            return;
        }

        var columns = await ProbeNewsColumnAvailabilityAsync(connectionString, cancellationToken);
        ColumnAvailabilityCache.TryAdd(connectionString, columns);
    }

    /// <summary>Test helper: clears process cache so probes can be re-tested in isolation.</summary>
    internal static void ClearColumnAvailabilityCacheForTests() => ColumnAvailabilityCache.Clear();

    /// <summary>
    /// Test helper: seeds the process cache directly, so <see cref="WarmupAsync"/>'s short-circuit
    /// behaviour (and callers built on top of it) can be exercised without a real SQL Server probe.
    /// </summary>
    internal static void SeedColumnAvailabilityCacheForTests(string connectionString, NewsColumnAvailability columns) =>
        ColumnAvailabilityCache[connectionString] = columns;

    private static NewsColumnAvailability ProbeNewsColumnAvailability(string connectionString) =>
        ProbeNewsColumnAvailabilityAsync(connectionString, CancellationToken.None).GetAwaiter().GetResult();

    private static Task<NewsColumnAvailability> ProbeNewsColumnAvailabilityAsync(
        string connectionString, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT
                CAST(CASE WHEN COL_LENGTH('NEWS_T', 'SOURCE_URL') IS NOT NULL THEN 1 ELSE 0 END AS bit) AS HasSourceUrlColumn,
                CAST(CASE WHEN COL_LENGTH('NEWS_T', 'SLUG') IS NOT NULL THEN 1 ELSE 0 END AS bit) AS HasSlugColumn,
                CAST(CASE WHEN COL_LENGTH('NEWS_T', 'CREATED_AT') IS NOT NULL THEN 1 ELSE 0 END AS bit) AS HasCreatedAtColumn,
                CAST(CASE WHEN COL_LENGTH('NEWS_T', 'UPDATED_AT') IS NOT NULL THEN 1 ELSE 0 END AS bit) AS HasUpdatedAtColumn,
                CAST(CASE WHEN COL_LENGTH('NEWS_T', 'EDITOR_EMAIL') IS NOT NULL THEN 1 ELSE 0 END AS bit) AS HasEditorEmailColumn,
                CAST(CASE WHEN COL_LENGTH('NEWS_T', 'IMAGE_BLOB_KEY') IS NOT NULL THEN 1 ELSE 0 END AS bit) AS HasImageBlobKeyColumn,
                CAST(CASE WHEN COL_LENGTH('NEWS_T', 'IMAGE_GALLERY_PIC_ID') IS NOT NULL THEN 1 ELSE 0 END AS bit) AS HasImageGalleryPicIdColumn,
                CAST(CASE WHEN COL_LENGTH('NEWS_T', 'FORUM_TOPIC_ID') IS NOT NULL THEN 1 ELSE 0 END AS bit) AS HasForumTopicIdColumn
            """;

        return EfSql.QuerySingleSqlAsync<NewsColumnAvailability>(connectionString, sql, cancellationToken);
    }
}
