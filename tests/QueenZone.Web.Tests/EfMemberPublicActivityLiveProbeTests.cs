using Microsoft.EntityFrameworkCore;
using QueenZone.Data;

namespace QueenZone.Web.Tests;

/// <summary>
/// Opt-in SQL Express mirror probe for the member public activity feed. Skipped unless
/// ConnectionStrings__QueenZoneLegacy and RUN_MEMBER_ACTIVITY_PROBE=true are set.
/// </summary>
/// <remarks>
/// Read-only: it seeds nothing and writes nothing. It exists because the unit tests for this
/// repository run on SQLite, which cannot ORDER BY a DateTimeOffset and therefore takes a
/// client-side sort branch. SQL Server takes the server-side branch, so the production ordering
/// and the UNION ALL over ArticleSubmissions / NewsSuggestions / PhotoSubmissions are otherwise
/// never exercised. See issue #1478 for why forum posts remain a separate query.
/// </remarks>
[Collection(LiveDatabaseProbeCollection.Name)]
public sealed class EfMemberPublicActivityLiveProbeTests
{
    [Fact]
    public async Task Feed_page_translates_and_orders_on_sql_server_when_enabled()
    {
        if (!IsProbeEnabled(out var connectionString))
        {
            return;
        }

        await using var dbContext = CreateContext(connectionString);
        var repository = new EfMemberPublicActivityRepository(dbContext, new EfForumArchiveAuthorRepository(dbContext));

        // Every member that actually has linked forum activity on the mirror, so the union and
        // the forum branch both have rows to order.
        var authorIds = await dbContext.ModernForumPosts
            .AsNoTracking()
            .Where(post => post.AuthorMemberId != null && !post.IsHidden && post.Thread != null)
            .Select(post => post.AuthorMemberId!.Value)
            .Distinct()
            .ToListAsync();

        Assert.True(
            authorIds.Count > 0,
            "Mirror has no member-linked forum posts; re-sync before running this probe.");

        var page = await repository.GetFeedPageAsync(authorIds, page: 1, pageSize: 20);

        // Translation is the point of the probe: an untranslatable set operation or ORDER BY
        // throws before it reaches these assertions.
        Assert.True(page.TotalCount >= page.Items.Count);
        Assert.True(page.Items.Count <= 20);
        Assert.All(page.Items, item => Assert.False(string.IsNullOrWhiteSpace(item.Type)));

        // Newest first, across all four sources.
        var publishedAt = page.Items.Select(item => item.PublishedAt).ToList();
        Assert.Equal(publishedAt.OrderByDescending(value => value).ToList(), publishedAt);

        // Paging must not repeat a row from page 1.
        if (page.TotalCount > 20)
        {
            var second = await repository.GetFeedPageAsync(authorIds, page: 2, pageSize: 20);
            var firstKeys = page.Items.Select(KeyOf).ToHashSet();
            Assert.DoesNotContain(second.Items.Select(KeyOf), key => firstKeys.Contains(key));
        }

        // Single-member reads go through the same path.
        var single = await repository.GetPageAsync(authorIds[0], linkedLegacyUserId: null, page: 1, pageSize: 5);
        Assert.True(single.Items.Count <= 5);

        // A linked legacy account merges its archive posts through the summary count and the
        // deferred body load; page 2 exercises the Take beyond one page.
        var legacyUserId = await dbContext.ModernForumPosts
            .AsNoTracking()
            .Where(post => post.AuthorLegacyUserId != null && !post.IsHidden && post.Thread != null)
            .Select(post => post.AuthorLegacyUserId!.Value)
            .FirstAsync();
        var linked = await repository.GetPageAsync(authorIds[0], legacyUserId, page: 2, pageSize: 5);
        Assert.True(linked.TotalCount > single.TotalCount);
        Assert.All(
            linked.Items.Where(item => item.Type == MemberPublicActivityType.ForumPost),
            item => Assert.Equal(authorIds[0], item.AuthorId));
    }

    private static string KeyOf(MemberPublicActivityItem item) =>
        $"{item.Type}|{item.ContentId}|{item.Title}|{item.PublishedAt:O}";

    private static QueenZoneDbContext CreateContext(string connectionString)
    {
        var options = new DbContextOptionsBuilder<QueenZoneDbContext>()
            .UseSqlServer(
                connectionString,
                sql =>
                {
                    sql.CommandTimeout(QueenZoneSqlServerOptions.DefaultCommandTimeoutSeconds);
                    sql.EnableRetryOnFailure(
                        maxRetryCount: QueenZoneSqlServerOptions.MaxRetryCount,
                        maxRetryDelay: QueenZoneSqlServerOptions.MaxRetryDelay,
                        errorNumbersToAdd: null);
                })
            .Options;
        return new QueenZoneDbContext(options);
    }

    private static bool IsProbeEnabled(out string connectionString)
    {
        connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__QueenZoneLegacy") ?? string.Empty;
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return false;
        }

        return string.Equals(
            Environment.GetEnvironmentVariable("RUN_MEMBER_ACTIVITY_PROBE"),
            "true",
            StringComparison.OrdinalIgnoreCase);
    }
}
