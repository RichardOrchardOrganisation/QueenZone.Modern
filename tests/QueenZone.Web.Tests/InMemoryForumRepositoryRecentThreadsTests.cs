using QueenZone.Data;

namespace QueenZone.Web.Tests;

public sealed class InMemoryForumRepositoryRecentThreadsTests
{
    [Fact]
    public async Task GetRecentThreadsAsync_ReturnsCrossBoardThreadsNewestFirst()
    {
        var repository = new InMemoryForumRepository(
            SampleForumData.CreateSeedCategories(),
            SampleForumData.CreateSeedStats());

        var recent = await repository.GetRecentThreadsAsync(5);

        Assert.Equal(5, recent.Count);
        Assert.Equal(1001, recent[0].TopicId);
        Assert.Equal("Forum Guidelines", recent[0].Title);
        Assert.Equal("The Music", recent[0].CategoryName);
        Assert.True(recent[0].LastActivityAt >= recent[^1].LastActivityAt);
    }
    [Fact]
    public async Task GetRecentThreadsAsync_ExcludesWebsiteBoardButPreservesDirectReadsAndTestPressings()
    {
        var categories = SampleForumData.CreateSeedCategories().Append(
            new ForumCategoryItem(7, "Queenzone.com", "Website support", 0, null, null, 70)).ToArray();
        var writes = new InMemoryForumWriteRepository();
        var now = new DateTimeOffset(2026, 8, 3, 0, 0, 0, TimeSpan.Zero);
        var website = await writes.CreateThreadAsync(new NewForumThread(
            7, Guid.NewGuid(), "Member", "Reviewer Test Message", "App check", now));
        var music = await writes.CreateThreadAsync(new NewForumThread(
            1, Guid.NewGuid(), "Member", "Test pressings of Queen II", "A genuine discussion", now.AddHours(-1)));
        var repository = new InMemoryForumRepository(categories, SampleForumData.CreateSeedStats(), writes);

        var recent = await repository.GetRecentThreadsAsync(2);

        Assert.Equal(2, recent.Count);
        Assert.Equal(music.TopicId, recent[0].TopicId);
        Assert.All(recent, item => Assert.NotEqual(7, item.CategoryId));
        Assert.NotNull(await repository.GetCategoryByIdAsync(7));
        Assert.Contains((await repository.GetCategoryTopicsPageAsync(7, 1, 20)).Topics, item => item.Id == website.TopicId);
        Assert.NotNull(await repository.GetTopicPostsPageAsync(website.TopicId, 1, 20));
    }
}
