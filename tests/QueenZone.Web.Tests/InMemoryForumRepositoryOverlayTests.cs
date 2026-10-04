using QueenZone.Data;

namespace QueenZone.Web.Tests;

public sealed class InMemoryForumRepositoryOverlayTests
{
    [Theory]
    [InlineData(1001)]
    [InlineData(1002)]
    public async Task RepliesOverlaySeedOnceAcrossReadProjectionsAndTotals(int topicId)
    {
        var writes = new InMemoryForumWriteRepository();
        var repository = new InMemoryForumRepository(
            SampleForumData.CreateSeedCategories(), SampleForumData.CreateSeedStats(), writes);
        var before = await repository.GetCategoryTopicsPageAsync(1, 1, 100);
        var seed = Assert.Single(before.Topics, topic => topic.Id == topicId);
        var categoryBefore = (await repository.GetCategoryByIdAsync(1))!;
        var statsBefore = await repository.GetArchiveStatsAsync();
        var sitemapBefore = await repository.GetTopicSitemapCountAsync();
        var now = new DateTimeOffset(2026, 10, 4, 12, 0, 0, TimeSpan.Zero);
        for (var reply = 0; reply < 2; reply++)
        {
            await writes.CreatePostAsync(new NewForumPost(
                topicId, Guid.NewGuid(), "Reply author", "<p>Reply</p>", now.AddMinutes(reply)));
        }

        var category = await repository.GetCategoryTopicsPageAsync(1, 1, 100);
        var topic = Assert.Single(category.Topics, item => item.Id == topicId);
        Assert.Equal(before.TotalCount, category.TotalCount);
        Assert.Equal(seed.Title, topic.Title);
        Assert.Equal(seed.AuthorUsername, topic.AuthorUsername);
        Assert.Equal(seed.IsSticky, topic.IsSticky);
        Assert.Equal(seed.ReplyCount + 2, topic.ReplyCount);
        Assert.Equal(now.AddMinutes(1).UtcDateTime, topic.LastActivityAt);
        var recent = Assert.Single(await repository.GetRecentThreadsAsync(50), item => item.TopicId == topicId);
        Assert.Equal(topic.ReplyCount, recent.ReplyCount);
        Assert.Equal(topic.LastActivityAt, recent.LastActivityAt);
        var search = await repository.SearchForumAsync(seed.Title, 1, 100);
        var hit = Assert.Single(search.Results);
        Assert.Equal(topicId, hit.TopicId);
        Assert.Equal(seed.AuthorUsername, hit.StartedByDisplayName);
        Assert.Equal(topic.ReplyCount, hit.ReplyCount);
        Assert.Equal(topic.LastActivityAt, hit.LastActivityAt);
        Assert.Equal(1, search.TotalCount);
        Assert.Equal(sitemapBefore, await repository.GetTopicSitemapCountAsync());
        var sitemap = Assert.Single(await repository.GetTopicSitemapPageAsync(0, 100), item => item.TopicId == topicId);
        Assert.Equal(topic.LastActivityAt, sitemap.LastActivityAt);
        var stats = await repository.GetArchiveStatsAsync();
        Assert.Equal(statsBefore.ThreadCount, stats.ThreadCount);
        Assert.Equal(statsBefore.PostCount + 2, stats.PostCount);
        var categoryAfter = (await repository.GetCategoryByIdAsync(1))!;
        Assert.Equal(categoryBefore.PostCount + 2, categoryAfter.PostCount);
        Assert.Equal(topic.LastActivityAt, categoryAfter.LastActivityAt);
    }

    [Fact]
    public async Task OlderReplyDoesNotMoveSeedActivityBackwards()
    {
        var writes = new InMemoryForumWriteRepository();
        var repository = new InMemoryForumRepository(
            SampleForumData.CreateSeedCategories(), SampleForumData.CreateSeedStats(), writes);
        var seed = Assert.Single((await repository.GetCategoryTopicsPageAsync(1, 1, 100)).Topics,
            topic => topic.Id == 1002);
        await writes.CreatePostAsync(new NewForumPost(
            1002, Guid.NewGuid(), "Member", "Reply", new DateTimeOffset(seed.LastActivityAt).AddDays(-1)));

        var topic = Assert.Single((await repository.GetCategoryTopicsPageAsync(1, 1, 100)).Topics,
            item => item.Id == 1002);
        Assert.Equal(seed.LastActivityAt, topic.LastActivityAt);
        Assert.Equal(seed.ReplyCount + 1, topic.ReplyCount);
        var sitemap = Assert.Single(await repository.GetTopicSitemapPageAsync(0, 100), item => item.TopicId == 1002);
        Assert.Equal(seed.LastActivityAt, sitemap.LastActivityAt);
    }

    [Fact]
    public async Task NewThreadRemainsDistinctAlongsideSeedOverlay()
    {
        var writes = new InMemoryForumWriteRepository();
        var repository = new InMemoryForumRepository(
            SampleForumData.CreateSeedCategories(), SampleForumData.CreateSeedStats(), writes);
        var before = await repository.GetArchiveStatsAsync();
        var sitemapBefore = await repository.GetTopicSitemapCountAsync();
        var now = new DateTimeOffset(2026, 10, 4, 12, 0, 0, TimeSpan.Zero);
        await writes.CreatePostAsync(new NewForumPost(1002, Guid.NewGuid(), "Member", "Reply", now));
        var created = await writes.CreateThreadAsync(new NewForumThread(
            1, Guid.NewGuid(), "Member", "New ranking discussion", "Starter", now.AddMinutes(1)));
        await writes.CreatePostAsync(new NewForumPost(created.TopicId, Guid.NewGuid(), "Member", "Reply", now.AddMinutes(2)));

        var topics = (await repository.GetCategoryTopicsPageAsync(1, 1, 100)).Topics;
        Assert.Single(topics, item => item.Id == 1002);
        var newTopic = Assert.Single(topics, item => item.Id == created.TopicId);
        Assert.Equal(1, newTopic.ReplyCount);
        Assert.Equal(now.AddMinutes(2).UtcDateTime, newTopic.LastActivityAt);
        Assert.Single((await repository.SearchForumAsync("New ranking discussion", 1, 100)).Results);
        Assert.Single(await repository.GetRecentThreadsAsync(50), item => item.TopicId == created.TopicId);
        Assert.Equal(sitemapBefore + 1, await repository.GetTopicSitemapCountAsync());
        Assert.Single(await repository.GetTopicSitemapPageAsync(0, 100), item => item.TopicId == created.TopicId);
        var stats = await repository.GetArchiveStatsAsync();
        Assert.Equal(before.ThreadCount + 1, stats.ThreadCount);
        Assert.Equal(before.PostCount + 3, stats.PostCount);
    }
}
