using QueenZone.Data;

namespace QueenZone.Web.Tests;

public sealed class InMemoryForumRepositoryTopicOverlayTests
{
    [Fact]
    public async Task Reply_to_sample_topic_updates_one_row_in_category_recent_and_search_lists()
    {
        var writes = new InMemoryForumWriteRepository();
        var now = new DateTimeOffset(2026, 10, 4, 0, 0, 0, TimeSpan.Zero);
        await writes.CreatePostAsync(new NewForumPost(1002, Guid.NewGuid(), "Test member", "A reply", now));
        var created = await writes.CreateThreadAsync(new NewForumThread(1, Guid.NewGuid(),
            "Test member", "A new forum fixture topic", "First post", now.AddMinutes(1)));
        var repository = new InMemoryForumRepository(SampleForumData.CreateSeedCategories(),
            SampleForumData.CreateSeedStats(), writes);

        var category = await repository.GetCategoryTopicsPageAsync(1, 1, 100);
        Assert.Equal(SampleForumData.CreateSeedTopics(1).Count + 1, category.TotalCount);
        var updated = Assert.Single(category.Topics, topic => topic.Id == 1002);
        Assert.Equal(now.UtcDateTime, updated.LastActivityAt);
        Assert.Equal("brightonrock", updated.AuthorUsername);
        Assert.Equal((await writes.GetThreadAsync(1002))!.PostCount - 1, updated.ReplyCount);
        Assert.Single(category.Topics, topic => topic.Id == created.TopicId);
        Assert.True(Assert.Single(category.Topics, topic => topic.Id == 1001).IsSticky);

        var recent = await repository.GetRecentThreadsAsync(50);
        Assert.Single(recent, topic => topic.TopicId == 1002);
        Assert.Single(recent, topic => topic.TopicId == created.TopicId);
        var search = await repository.SearchForumAsync("Ranking every studio album", 1, 100);
        Assert.Equal(1, search.TotalCount);
        Assert.Single(search.Results);
        var newTopicSearch = await repository.SearchForumAsync("A new forum fixture topic", 1, 100);
        Assert.Single(newTopicSearch.Results);
    }
}
