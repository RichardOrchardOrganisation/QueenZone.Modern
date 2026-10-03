using QueenZone.Data;
using QueenZone.Web.Sitemap;

namespace QueenZone.Web.Tests;

public sealed class ForumSitemapBuilderTests
{
    [Fact]
    public async Task BuildFileAsync_UsesTopicPageOneCanonicalPathsOnly()
    {
        var builder = new ForumSitemapBuilder(new InMemoryForumRepository(
            SampleForumData.CreateSeedCategories(),
            SampleForumData.CreateSeedStats()));

        var entries = await builder.BuildFileAsync(1);

        Assert.NotNull(entries);
        Assert.Contains(
            entries,
            entry => entry.Path == "/forum/topic/1002/ranking-every-studio-album");
        Assert.DoesNotContain(entries, entry => entry.Path.Contains("/page/", StringComparison.Ordinal));
    }

    [Fact]
    public async Task GetFileCountAsync_ReturnsZeroWhenNoTopics()
    {
        var builder = new ForumSitemapBuilder(new EmptyForumRepository());

        var fileCount = await builder.GetFileCountAsync();

        Assert.Equal(0, fileCount);
    }

    [Fact]
    public async Task GetFileCountAsync_SplitsAcrossMultipleFilesWhenTopicCountExceedsLimit()
    {
        var totalTopics = ForumSitemapLimits.MaxUrlsPerFile + 1;
        var builder = new ForumSitemapBuilder(new FixedCountForumRepository(totalTopics));

        var fileCount = await builder.GetFileCountAsync();

        Assert.Equal(2, fileCount);
    }

    [Fact]
    public async Task BuildFileAsync_ReturnsFullPageForFirstFileWhenTopicsSpanMultipleFiles()
    {
        var totalTopics = ForumSitemapLimits.MaxUrlsPerFile + 500;
        var builder = new ForumSitemapBuilder(new FixedCountForumRepository(totalTopics));

        var entries = await builder.BuildFileAsync(1);

        Assert.NotNull(entries);
        Assert.Equal(ForumSitemapLimits.MaxUrlsPerFile, entries.Count);
    }

    [Fact]
    public async Task BuildFileAsync_ReturnsRemainingTopicsInLastFile()
    {
        var overflow = 500;
        var totalTopics = ForumSitemapLimits.MaxUrlsPerFile + overflow;
        var builder = new ForumSitemapBuilder(new FixedCountForumRepository(totalTopics));

        var entries = await builder.BuildFileAsync(2);

        Assert.NotNull(entries);
        Assert.Equal(overflow, entries.Count);
    }

    [Fact]
    public async Task BuildFileAsync_ReturnsNullForFileNumberBeyondCount()
    {
        var builder = new ForumSitemapBuilder(new FixedCountForumRepository(ForumSitemapLimits.MaxUrlsPerFile));

        var entries = await builder.BuildFileAsync(2);

        Assert.Null(entries);
    }

    private sealed class FixedCountForumRepository(int totalCount) : EmptyForumRepository
    {
        public override Task<int> GetTopicSitemapCountAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(totalCount);

        public override Task<IReadOnlyList<ForumTopicSitemapItem>> GetTopicSitemapPageAsync(
            int offset,
            int pageSize,
            CancellationToken cancellationToken = default)
        {
            var items = Enumerable.Range(offset + 1, Math.Min(pageSize, Math.Max(0, totalCount - offset)))
                .Select(id => new ForumTopicSitemapItem(id, $"Topic {id}", DateTime.UtcNow))
                .ToList();
            return Task.FromResult<IReadOnlyList<ForumTopicSitemapItem>>(items);
        }
    }

    private class EmptyForumRepository : IForumRepository
    {
        public Task<IReadOnlyList<ForumCategoryItem>> GetCategoriesAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ForumCategoryItem>>([]);

        public Task<ForumCategoryItem?> GetCategoryByIdAsync(int id, CancellationToken cancellationToken = default) =>
            Task.FromResult<ForumCategoryItem?>(null);

        public Task<ForumCategoryTopicsPage> GetCategoryTopicsPageAsync(
            int forumId,
            int page,
            int pageSize,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ForumCategoryTopicsPage([], 0, page, pageSize));

        public Task<ForumTopicPostsPage?> GetTopicPostsPageAsync(
            int topicId,
            int page,
            int pageSize,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<ForumTopicPostsPage?>(null);

        public Task<int> GetTotalThreadCountAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(0);

        public Task<IReadOnlyList<ForumRecentThreadItem>> GetRecentThreadsAsync(
            int count,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ForumRecentThreadItem>>([]);

        public Task<IReadOnlyList<ForumRecentThreadItem>> GetLegacyDiscographyThreadsAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ForumRecentThreadItem>>([]);

        public Task<ForumArchiveStats> GetArchiveStatsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new ForumArchiveStats(0, 0, 0));

        public virtual Task<int> GetTopicSitemapCountAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(0);

        public virtual Task<IReadOnlyList<ForumTopicSitemapItem>> GetTopicSitemapPageAsync(
            int offset,
            int pageSize,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ForumTopicSitemapItem>>([]);

        public Task<ForumSearchPage> SearchForumAsync(
            string query,
            int page,
            int pageSize,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ForumSearchPage([], 0, page, pageSize));

        public Task<ForumLegacyPostLocation?> FindLegacyPostAsync(
            int legacyPostId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<ForumLegacyPostLocation?>(null);
    }
}
