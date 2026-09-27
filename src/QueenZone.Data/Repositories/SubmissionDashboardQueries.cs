using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace QueenZone.Data;

/// <summary>
/// Admin dashboard aggregation shared by the EF submission repositories. Each repository
/// projects its entity into <see cref="SubmissionCountRow"/> or <see cref="SubmissionContributorRow"/>
/// (the per-type status rules live in that projection); this class owns the date windows and the
/// SQLite / SQL Server split.
/// </summary>
/// <remarks>
/// The EF Core SQLite provider cannot translate <see cref="DateTimeOffset"/> comparisons, so on
/// SQLite (the default <c>QueenZone.Web.Tests</c> suite) the rows are materialised and counted in
/// memory. Other providers aggregate in SQL; that path is covered by
/// <c>tests/QueenZone.SqlServerTests</c> against a real SQL Server — see
/// docs/architecture/testing-policy.md.
/// </remarks>
internal static class SubmissionDashboardQueries
{
    private const string UnknownMember = "Unknown member";

    internal static bool IsSqliteProvider(this DatabaseFacade database) =>
        string.Equals(
            database.ProviderName,
            "Microsoft.EntityFrameworkCore.Sqlite",
            StringComparison.Ordinal);

    internal static Task<SubmissionTypeCounts> ToDashboardCountsAsync(
        this IQueryable<SubmissionCountRow> rows,
        DateTimeOffset utcNow,
        bool aggregateInSql,
        CancellationToken cancellationToken) =>
        aggregateInSql
            ? CountViaSqlAggregateAsync(rows, utcNow, cancellationToken)
            : CountInMemoryAsync(rows, utcNow, cancellationToken);

    internal static Task<IReadOnlyList<SubmissionContributor>> ToTopContributorsAsync(
        this IQueryable<SubmissionContributorRow> rows,
        DateTimeOffset monthStart,
        int maxCount,
        bool aggregateInSql,
        CancellationToken cancellationToken) =>
        aggregateInSql
            ? TopContributorsViaSqlAggregateAsync(rows, monthStart, maxCount, cancellationToken)
            : TopContributorsInMemoryAsync(rows, monthStart, maxCount, cancellationToken);

    private static async Task<SubmissionTypeCounts> CountInMemoryAsync(
        IQueryable<SubmissionCountRow> rows,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken)
    {
        var monthAgo = utcNow.AddDays(-30);
        var today = utcNow.UtcDateTime.Date;
        var weekAgo = today.AddDays(-6);

        var materialised = await rows.ToListAsync(cancellationToken);

        var pending = materialised.Count(row => row.IsOpen);

        var submitted = materialised.Where(row => row.SubmittedAt.HasValue).ToList();
        var receivedToday = submitted.Count(row => row.SubmittedAt!.Value.UtcDateTime.Date >= today);
        var receivedThisWeek = submitted.Count(row => row.SubmittedAt!.Value.UtcDateTime.Date >= weekAgo);

        var last30 = submitted.Where(row => row.SubmittedAt!.Value >= monthAgo).ToList();
        var approvedLast30 = last30.Count(row => row.IsApproved);
        var rejectedLast30 = last30.Count(row => row.IsRejected);
        var pendingLast30 = last30.Count(row => row.IsStillPending);

        return new SubmissionTypeCounts(
            pending, receivedToday, receivedThisWeek, approvedLast30, rejectedLast30, pendingLast30);
    }

    private static async Task<SubmissionTypeCounts> CountViaSqlAggregateAsync(
        IQueryable<SubmissionCountRow> rows,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken)
    {
        var monthAgo = utcNow.AddDays(-30);
        var todayUtc = new DateTimeOffset(utcNow.UtcDateTime.Date, TimeSpan.Zero);
        var weekAgoUtc = todayUtc.AddDays(-6);

        var counts = await rows
            .GroupBy(_ => 1)
            .Select(g => new SubmissionTypeCounts(
                g.Count(row => row.IsOpen),
                g.Count(row => row.SubmittedAt >= todayUtc),
                g.Count(row => row.SubmittedAt >= weekAgoUtc),
                g.Count(row => row.SubmittedAt >= monthAgo && row.IsApproved),
                g.Count(row => row.SubmittedAt >= monthAgo && row.IsRejected),
                g.Count(row => row.SubmittedAt >= monthAgo && row.IsStillPending)))
            .SingleOrDefaultAsync(cancellationToken);

        return counts ?? SubmissionTypeCounts.Empty;
    }

    private static async Task<IReadOnlyList<SubmissionContributor>> TopContributorsInMemoryAsync(
        IQueryable<SubmissionContributorRow> rows,
        DateTimeOffset monthStart,
        int maxCount,
        CancellationToken cancellationToken)
    {
        var materialised = await rows.ToListAsync(cancellationToken);

        return materialised
            .Where(row => row.SubmittedAt.HasValue && row.SubmittedAt.Value >= monthStart)
            .GroupBy(row => row.MemberId)
            .Select(g => new SubmissionContributor(
                g.Key,
                g.FirstOrDefault(row => !string.IsNullOrWhiteSpace(row.DisplayName))?.DisplayName ?? UnknownMember,
                g.Count()))
            .OrderByDescending(contributor => contributor.Count)
            .Take(maxCount)
            .ToList();
    }

    private static async Task<IReadOnlyList<SubmissionContributor>> TopContributorsViaSqlAggregateAsync(
        IQueryable<SubmissionContributorRow> rows,
        DateTimeOffset monthStart,
        int maxCount,
        CancellationToken cancellationToken)
    {
        var aggregated = await rows
            .Where(row => row.SubmittedAt >= monthStart)
            .GroupBy(row => row.MemberId)
            .Select(g => new
            {
                MemberId = g.Key,
                DisplayName = g.Max(row => row.DisplayName),
                Count = g.Count(),
            })
            .OrderByDescending(contributor => contributor.Count)
            .Take(maxCount)
            .ToListAsync(cancellationToken);

        return aggregated
            .Select(contributor => new SubmissionContributor(
                contributor.MemberId,
                string.IsNullOrWhiteSpace(contributor.DisplayName) ? UnknownMember : contributor.DisplayName,
                contributor.Count))
            .ToList();
    }
}

/// <summary>
/// One submission reduced to what the dashboard counts need. The status flags carry each
/// submission type's own rules (for example, articles count <c>ApprovedForPublishing</c> as both
/// open and approved), so they must be set in an EF-translatable projection.
/// </summary>
internal sealed class SubmissionCountRow
{
    public DateTimeOffset? SubmittedAt { get; init; }

    /// <summary>Counted in the current queue (<see cref="SubmissionTypeCounts.Pending"/>).</summary>
    public bool IsOpen { get; init; }

    public bool IsApproved { get; init; }

    public bool IsRejected { get; init; }

    /// <summary>Counted in <see cref="SubmissionTypeCounts.StillPendingFromLast30Days"/>.</summary>
    public bool IsStillPending { get; init; }
}

/// <summary>One submission reduced to what the top-contributor ranking needs.</summary>
internal sealed class SubmissionContributorRow
{
    public Guid MemberId { get; init; }

    public string? DisplayName { get; init; }

    public DateTimeOffset? SubmittedAt { get; init; }
}
