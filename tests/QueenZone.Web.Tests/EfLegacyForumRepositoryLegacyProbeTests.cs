using Microsoft.EntityFrameworkCore;
using QueenZone.Data;

namespace QueenZone.Web.Tests;

/// <summary>
/// Read-only probe of <see cref="LegacyForumRepository"/> against the real
/// legacy forum tables and <c>Q_FORUM_VIEW_PAGE_SP</c> /
/// <c>Q_FORUM_TOPIC_NEW_SP</c> (#1672 / #1891). Skips when
/// <c>ConnectionStrings__QueenZoneLegacy</c> is not set; the nightly
/// <c>legacy-read-probes</c> job runs it against the SQL Express mirror.
/// Scratch-schema coverage lives in <c>LegacyForumRepositorySqlServerTests</c>.
/// <c>Q_FORUM_TOPIC_T</c> and its views are present on QueenZoneLocal but
/// historically missing on <c>queenzone_legacy_sync</c>; topic/procedure
/// surfaces run only when that table exists.
/// </summary>
[Collection(LiveDatabaseProbeCollection.Name)]
public sealed class EfLegacyForumRepositoryLegacyProbeTests
{
    [Fact]
    public async Task Probe_legacy_forum_reads_when_connection_configured()
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__QueenZoneLegacy");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return;
        }

        var options = new DbContextOptionsBuilder<QueenZoneDbContext>()
            .UseSqlServer(connectionString)
            .Options;
        await using var dbContext = new QueenZoneDbContext(options);
        var repository = new LegacyForumRepository(dbContext);

        // Q_FORUM_ID is int on Q_FORUM_T and tinyint on Q_FORUM_TOPIC_T; FORUM_ORDER is tinyint.
        var categories = await repository.GetCategoriesAsync();
        Assert.NotEmpty(categories);

        if (!await LiveProbeSchema.TableExistsAsync(dbContext, "Q_FORUM_TOPIC_T"))
        {
            return;
        }

        var category = await repository.GetCategoryByIdAsync(categories[0].Id);
        Assert.NotNull(category);

        var topics = await repository.GetCategoryTopicsPageAsync(categories[0].Id, 1, 5);
        Assert.True(topics.TotalCount >= topics.Topics.Count);

        var sitemapCount = await repository.GetTopicSitemapCountAsync();
        var sitemap = await repository.GetTopicSitemapPageAsync(0, 5);
        Assert.True(sitemapCount >= sitemap.Count);
        Assert.True(sitemap.Count <= 5);

        if (topics.Topics.Count > 0)
        {
            var posts = await repository.GetTopicPostsPageAsync(topics.Topics[0].Id, 1, 5);
            Assert.NotNull(posts);
            Assert.True(posts.TotalCount >= posts.Posts.Count);
        }

        var threadCount = await repository.GetTotalThreadCountAsync();
        Assert.True(threadCount >= 0);

        var recent = await repository.GetRecentThreadsAsync(5);
        Assert.True(recent.Count <= 5);

        var discography = await repository.GetLegacyDiscographyThreadsAsync();
        Assert.NotNull(discography);

        var stats = await repository.GetArchiveStatsAsync();
        Assert.Equal(categories.Count, stats.ForumCount);
        Assert.Equal(threadCount, stats.ThreadCount);
    }
}
