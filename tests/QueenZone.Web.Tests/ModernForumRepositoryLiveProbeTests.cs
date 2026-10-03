using Microsoft.Extensions.DependencyInjection;
using QueenZone.Data;

namespace QueenZone.Web.Tests;

/// <summary>
/// Read-only SQL Express mirror probe for <see cref="ModernForumRepository"/>
/// procedures, including real full-text search (#1892).
/// </summary>
[Collection(LiveDatabaseProbeCollection.Name)]
public sealed class ModernForumRepositoryLiveProbeTests
{
    [Fact]
    public async Task Forum_reads_and_fulltext_search_materialize_when_connection_configured()
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__QueenZoneLegacy");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return;
        }

        var services = new ServiceCollection();
        services.AddQueenZoneLegacyData(connectionString, new ForumDataOptions { UseModernForumReads = true });
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<IForumRepository>();
        Assert.IsType<ModernForumRepository>(repository);

        var categories = await repository.GetCategoriesAsync();
        Assert.NotEmpty(categories);
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

            var topicLocation = await repository.FindLegacyPostAsync(topics.Topics[0].Id);
            Assert.NotNull(topicLocation);
            Assert.Equal(topics.Topics[0].Id, topicLocation.TopicId);
            Assert.Equal(0, topicLocation.PostIndex);

            if (posts.Posts.Count > 1)
            {
                var replyLocation = await repository.FindLegacyPostAsync(posts.Posts[^1].Id);
                Assert.NotNull(replyLocation);
                Assert.Equal(posts.Posts[^1].Id, replyLocation.PostId);
            }
        }

        var recent = await repository.GetRecentThreadsAsync(5);
        Assert.True(recent.Count <= 5);
        Assert.All(recent, item => Assert.NotEqual(7, item.CategoryId));

        var first = await repository.SearchForumAsync("Queen", 1, 5);
        Assert.True(first.TotalCount >= first.Results.Count);
        Assert.True(first.Results.Count <= 5);

        var missing = await repository.SearchForumAsync("queenzoneunlikelytermzzzxqv", 1, 5);
        Assert.Equal(0, missing.TotalCount);
        Assert.Empty(missing.Results);
    }
}
