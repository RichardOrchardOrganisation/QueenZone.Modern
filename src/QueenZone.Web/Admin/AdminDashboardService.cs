using Microsoft.Extensions.Options;
using QueenZone.Data;
using QueenZone.Web.Pages.Admin;

namespace QueenZone.Web;

/// <summary>
/// Loads admin dashboard tiles in parallel without concurrent use of one
/// <see cref="QueenZone.Data.QueenZoneDbContext"/> (issues #322, #335).
/// Uses independent DI scopes so each tile resolves its own scoped repositories
/// and DbContext. For code that works with <c>QueenZoneDbContext</c> directly,
/// prefer <c>IDbContextFactory&lt;QueenZoneDbContext&gt;</c> (registered when SQL-backed).
/// </summary>
public sealed class AdminDashboardService(IServiceScopeFactory scopeFactory)
{
    public async Task<AdminDashboardSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default)
    {
        var utcNow = DateTime.UtcNow;
        var utcNowOffset = DateTimeOffset.UtcNow;
        var monthStart = new DateTimeOffset(utcNow.Year, utcNow.Month, 1, 0, 0, 0, TimeSpan.Zero);
        var registrationsFrom = DateOnly.FromDateTime(utcNow).AddDays(-29);

        var memberStatsTask = RunAsync(
            sp => sp.GetRequiredService<IMemberAccountRepository>().GetStatsAsync(utcNow, cancellationToken));
        var recentLoginsTask = RunAsync(
            sp => sp.GetRequiredService<IMemberAccountRepository>().GetRecentLoginsAsync(5, cancellationToken));
        var dailyRegistrationsTask = RunAsync(
            sp => sp.GetRequiredService<IMemberAccountRepository>()
                .GetDailyRegistrationsAsync(registrationsFrom, cancellationToken));

        var photoCountsTask = RunAsync(
            sp => sp.GetRequiredService<IPhotoSubmissionRepository>()
                .GetDashboardCountsAsync(utcNowOffset, cancellationToken));
        var newsCountsTask = RunAsync(
            sp => sp.GetRequiredService<INewsSuggestionRepository>()
                .GetDashboardCountsAsync(utcNowOffset, cancellationToken));
        var articleCountsTask = RunAsync(
            sp => sp.GetRequiredService<IArticleSubmissionRepository>()
                .GetDashboardCountsAsync(utcNowOffset, cancellationToken));
        var fanPerformanceCountsTask = RunAsync(
            sp =>
            {
                var staleAfterDays = sp.GetService<IOptions<FanPerformanceSubmissionOptions>>()
                    ?.Value.StaleAfterDays
                    ?? FanPerformanceDashboardCounts.DefaultStaleAfterDays;
                return sp.GetRequiredService<IFanPerformanceSubmissionRepository>()
                    .GetDashboardCountsAsync(utcNowOffset, staleAfterDays, cancellationToken);
            });
        var openHelpRequestsTask = RunAsync(
            sp => sp.GetRequiredService<IHelpRequestRepository>()
                .CountOpenAsync(cancellationToken));
        var openPrivateMessageReportsTask = RunAsync(
            sp => sp.GetRequiredService<IPrivateMessageRepository>()
                .CountOpenReportsAsync(cancellationToken));
        var openForumPostReportsTask = RunAsync(
            sp => sp.GetRequiredService<IForumPostReportRepository>()
                .CountOpenAsync(cancellationToken));
        var openFanPerformanceReportsTask = RunAsync(
            sp => sp.GetRequiredService<IFanPerformanceReportRepository>()
                .CountOpenAsync(cancellationToken));

        var photoContributorsTask = RunAsync(
            sp => sp.GetRequiredService<IPhotoSubmissionRepository>()
                .GetTopContributorsThisMonthAsync(monthStart, 10, cancellationToken));
        var newsContributorsTask = RunAsync(
            sp => sp.GetRequiredService<INewsSuggestionRepository>()
                .GetTopContributorsThisMonthAsync(monthStart, 10, cancellationToken));
        var articleContributorsTask = RunAsync(
            sp => sp.GetRequiredService<IArticleSubmissionRepository>()
                .GetTopContributorsThisMonthAsync(monthStart, 10, cancellationToken));
        var fanPerformanceContributorsTask = RunAsync(
            sp => sp.GetRequiredService<IFanPerformanceSubmissionRepository>()
                .GetTopContributorsThisMonthAsync(monthStart, 10, cancellationToken));

        var trafficTask = RunAsync(
            sp => sp.GetRequiredService<IGoogleAnalyticsTrafficService>()
                .GetDashboardTrafficAsync(cancellationToken));

        await Task.WhenAll(
            memberStatsTask,
            recentLoginsTask,
            dailyRegistrationsTask,
            photoCountsTask,
            newsCountsTask,
            articleCountsTask,
            fanPerformanceCountsTask,
            openHelpRequestsTask,
            openPrivateMessageReportsTask,
            openForumPostReportsTask,
            openFanPerformanceReportsTask,
            photoContributorsTask,
            newsContributorsTask,
            articleContributorsTask,
            fanPerformanceContributorsTask,
            trafficTask).ConfigureAwait(false);

        var submissionQueue = new SubmissionQueueStats(
            await photoCountsTask.ConfigureAwait(false),
            await newsCountsTask.ConfigureAwait(false),
            await articleCountsTask.ConfigureAwait(false),
            await fanPerformanceCountsTask.ConfigureAwait(false),
            CombineTopContributors(
                await photoContributorsTask.ConfigureAwait(false),
                await newsContributorsTask.ConfigureAwait(false),
                await articleContributorsTask.ConfigureAwait(false),
                await fanPerformanceContributorsTask.ConfigureAwait(false),
                maxCount: 5));

        return new AdminDashboardSnapshot(
            await memberStatsTask.ConfigureAwait(false),
            await recentLoginsTask.ConfigureAwait(false),
            await dailyRegistrationsTask.ConfigureAwait(false),
            submissionQueue,
            await openHelpRequestsTask.ConfigureAwait(false),
            await openPrivateMessageReportsTask.ConfigureAwait(false),
            await openForumPostReportsTask.ConfigureAwait(false),
            await openFanPerformanceReportsTask.ConfigureAwait(false),
            await trafficTask.ConfigureAwait(false));
    }

    private async Task<T> RunAsync<T>(Func<IServiceProvider, Task<T>> work)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        return await work(scope.ServiceProvider).ConfigureAwait(false);
    }

    internal static IReadOnlyList<SubmissionContributor> CombineTopContributors(
        IReadOnlyList<SubmissionContributor> photos,
        IReadOnlyList<SubmissionContributor> news,
        IReadOnlyList<SubmissionContributor> articles,
        IReadOnlyList<SubmissionContributor> fanPerformances,
        int maxCount)
    {
        return photos.Concat(news).Concat(articles).Concat(fanPerformances)
            .GroupBy(c => c.MemberId)
            .Select(g => new SubmissionContributor(
                g.Key,
                g.Select(c => c.DisplayName).FirstOrDefault(name => !string.IsNullOrWhiteSpace(name))
                    ?? "Unknown member",
                g.Sum(c => c.Count)))
            .OrderByDescending(c => c.Count)
            .Take(maxCount)
            .ToList();
    }
}

public sealed record AdminDashboardSnapshot(
    MemberStats MemberStats,
    IReadOnlyList<RecentLogin> RecentLogins,
    IReadOnlyList<DailyRegistration> DailyRegistrations,
    SubmissionQueueStats SubmissionQueue,
    int OpenHelpRequestCount,
    int OpenPrivateMessageReportCount,
    int OpenForumPostReportCount,
    int OpenFanPerformanceReportCount,
    GoogleAnalyticsTrafficSnapshot Traffic);
