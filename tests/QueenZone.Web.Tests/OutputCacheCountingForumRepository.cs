using QueenZone.Data;

namespace QueenZone.Web.Tests;

public sealed class OutputCacheCountingForumRepository : IForumRepository
{
    private readonly IForumRepository inner = new InMemoryForumRepository(
        SampleForumData.CreateSeedCategories(), SampleForumData.CreateSeedStats());

    public int Calls { get; private set; }

    public void Reset() => Calls = 0;

    public Task<IReadOnlyList<ForumCategoryItem>> GetCategoriesAsync(CancellationToken cancellationToken = default)
    {
        Calls++;
        return inner.GetCategoriesAsync(cancellationToken);
    }

    public Task<ForumCategoryItem?> GetCategoryByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        Calls++;
        return inner.GetCategoryByIdAsync(id, cancellationToken);
    }

    public Task<ForumCategoryTopicsPage> GetCategoryTopicsPageAsync(int forumId, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        Calls++;
        return inner.GetCategoryTopicsPageAsync(forumId, page, pageSize, cancellationToken);
    }

    public Task<ForumTopicPostsPage?> GetTopicPostsPageAsync(int topicId, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        Calls++;
        return inner.GetTopicPostsPageAsync(topicId, page, pageSize, cancellationToken);
    }

    public Task<int> GetTotalThreadCountAsync(CancellationToken cancellationToken = default)
    {
        Calls++;
        return inner.GetTotalThreadCountAsync(cancellationToken);
    }

    public Task<IReadOnlyList<ForumRecentThreadItem>> GetRecentThreadsAsync(int count, CancellationToken cancellationToken = default)
    {
        Calls++;
        return inner.GetRecentThreadsAsync(count, cancellationToken);
    }

    public Task<IReadOnlyList<ForumRecentThreadItem>> GetLegacyDiscographyThreadsAsync(CancellationToken cancellationToken = default)
    {
        Calls++;
        return inner.GetLegacyDiscographyThreadsAsync(cancellationToken);
    }

    public Task<ForumArchiveStats> GetArchiveStatsAsync(CancellationToken cancellationToken = default)
    {
        Calls++;
        return inner.GetArchiveStatsAsync(cancellationToken);
    }

    public Task<int> GetTopicSitemapCountAsync(CancellationToken cancellationToken = default)
    {
        Calls++;
        return inner.GetTopicSitemapCountAsync(cancellationToken);
    }

    public Task<IReadOnlyList<ForumTopicSitemapItem>> GetTopicSitemapPageAsync(int offset, int pageSize, CancellationToken cancellationToken = default)
    {
        Calls++;
        return inner.GetTopicSitemapPageAsync(offset, pageSize, cancellationToken);
    }

    public Task<ForumSearchPage> SearchForumAsync(string query, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        Calls++;
        return inner.SearchForumAsync(query, page, pageSize, cancellationToken);
    }

    public Task<ForumLegacyPostLocation?> FindLegacyPostAsync(int legacyPostId, CancellationToken cancellationToken = default)
    {
        Calls++;
        return inner.FindLegacyPostAsync(legacyPostId, cancellationToken);
    }
}

public sealed class OutputCacheCountingArchiveAuthorRepository(IForumRepository forum) : IForumArchiveAuthorRepository
{
    private readonly IForumArchiveAuthorRepository inner = new InMemoryForumArchiveAuthorRepository(forum);

    public int Calls { get; private set; }

    public void Reset() => Calls = 0;

    public Task<ForumArchiveAuthorSummary?> GetSummaryAsync(int legacyUserId, CancellationToken cancellationToken = default)
    {
        Calls++;
        return inner.GetSummaryAsync(legacyUserId, cancellationToken);
    }

    public Task<MemberPublicActivityPage> GetPostsPageAsync(
        int legacyUserId, int page, int pageSize, int totalCount, CancellationToken cancellationToken = default)
    {
        Calls++;
        return inner.GetPostsPageAsync(legacyUserId, page, pageSize, totalCount, cancellationToken);
    }
}
