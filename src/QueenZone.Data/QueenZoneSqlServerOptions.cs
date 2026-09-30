namespace QueenZone.Data;

/// <summary>
/// Shared SQL Server EF options for runtime and design-time.
/// Default command timeout is short for public request paths; long-running
/// forum/admin commands still raise the timeout on specific operations.
/// </summary>
/// <remarks>
/// <para>
/// Transient Azure SQL faults are retried via EF <c>EnableRetryOnFailure</c>
/// (configured in <see cref="QueenZoneDataServiceCollectionExtensions"/> and
/// <see cref="QueenZoneDbContextFactory"/>). Prefer idempotent writes, or wrap
/// multi-statement work in an explicit execution strategy when adding new
/// non-idempotent batches. Command timeouts (SqlException -2) are intentionally
/// omitted from that global list so public search can fail-soft via
/// <see cref="SiteSearchSqlTimeout"/>; the admin search-reindex job retries
/// -2 and deadlock 1205 at job grain in <see cref="SearchReindexSqlRetry"/>.
/// </para>
/// <para>
/// Default was historically 300s for all commands, which held connections too long
/// on runaway public queries. Public default is now <see cref="DefaultCommandTimeoutSeconds"/>.
/// Design-time migrations and heavy tools may use <see cref="LongRunningCommandTimeoutSeconds"/>.
/// </para>
/// </remarks>
public static class QueenZoneSqlServerOptions
{
    /// <summary>Default EF command timeout for web request paths (seconds).</summary>
    public const int DefaultCommandTimeoutSeconds = 30;

    /// <summary>
    /// Design-time migrations and rare long admin/import operations (seconds).
    /// 300s was not enough for the ModernForumPost BodyHtml varchar→nvarchar(max)
    /// size-of-data ALTER (~1M posts); the CI <c>dotnet ef database update</c> job
    /// uses <see cref="QueenZoneDbContextFactory"/> and this value.
    /// </summary>
    public const int LongRunningCommandTimeoutSeconds = 1800;

    /// <summary>Max transient retries for Azure SQL.</summary>
    public const int MaxRetryCount = 5;

    /// <summary>Cap between retry attempts (seconds).</summary>
    public const int MaxRetryDelaySeconds = 20;

    public static TimeSpan MaxRetryDelay => TimeSpan.FromSeconds(MaxRetryDelaySeconds);
}
