namespace QueenZone.Data;

public sealed class InMemoryForumRepository(
    IReadOnlyList<ForumCategoryItem> seedCategories,
    ForumArchiveStats seedStats,
    InMemoryForumWriteRepository? writeRepository = null,
    IForumAttachmentRepository? attachmentRepository = null) : IForumRepository
{
    public Task<IReadOnlyList<ForumCategoryItem>> GetCategoriesAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<ForumCategoryItem>>(GetCategories());

    public Task<ForumCategoryItem?> GetCategoryByIdAsync(int id, CancellationToken cancellationToken = default) =>
        Task.FromResult(GetCategories().SingleOrDefault(category => category.Id == id));

    public Task<ForumCategoryTopicsPage> GetCategoryTopicsPageAsync(
        int forumId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var topics = GetTopics(forumId)
            .OrderByDescending(topic => topic.IsSticky)
            .ThenByDescending(topic => topic.LastActivityAt)
            .ToList();

        var skip = Math.Max(page - 1, 0) * pageSize;
        var pageItems = topics.Skip(skip).Take(pageSize).ToList();
        return Task.FromResult(new ForumCategoryTopicsPage(pageItems, topics.Count, page, pageSize));
    }

    public async Task<ForumTopicPostsPage?> GetTopicPostsPageAsync(
        int topicId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var header = SampleForumData.TryGetSeedTopicHeader(topicId)
            ?? TryGetCreatedTopicHeader(topicId);
        if (header is null)
        {
            return null;
        }

        var posts = SampleForumData.CreateSeedPosts(topicId)
            .Concat(GetCreatedPosts(topicId))
            .OrderBy(post => post.PostedAt)
            .ToList();

        if (attachmentRepository is not null)
        {
            posts = await ForumAttachmentMerge.MergeViaRepositoryAsync(
                attachmentRepository,
                posts,
                cancellationToken);
        }

        var skip = Math.Max(page - 1, 0) * pageSize;
        var pageItems = posts.Skip(skip).Take(pageSize).ToList();
        return new ForumTopicPostsPage(header, pageItems, posts.Count, page, pageSize);
    }

    public Task<int> GetTotalThreadCountAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(seedStats.ThreadCount + (writeRepository?.GetCreatedThreads()
            .Count(thread => SampleForumData.TryGetSeedTopicHeader(thread.TopicId) is null) ?? 0));

    public Task<IReadOnlyList<ForumRecentThreadItem>> GetRecentThreadsAsync(
        int count,
        CancellationToken cancellationToken = default)
    {
        var take = Math.Clamp(count, 1, 50);
        var items = seedCategories
            .Where(category => category.Id != ForumRecentThreadsPolicy.WebsiteDiscussionBoardId)
            .SelectMany(category => GetTopics(category.Id)
                .Select(topic => new ForumRecentThreadItem(
                    topic.Id,
                    topic.Title,
                    category.Id,
                    category.Name,
                    topic.ReplyCount,
                    topic.LastActivityAt)))
            .OrderByDescending(item => item.LastActivityAt)
            .ThenByDescending(item => item.TopicId)
            .Take(take)
            .ToList();
        return Task.FromResult<IReadOnlyList<ForumRecentThreadItem>>(items);
    }

    public Task<IReadOnlyList<ForumRecentThreadItem>> GetLegacyDiscographyThreadsAsync(
        CancellationToken cancellationToken = default)
    {
        var rarities = seedCategories.SingleOrDefault(category => category.Name == "Recordings & Rarities")
            ?? seedCategories[0];
        var items = new List<ForumRecentThreadItem>
        {
            new(5001, "1970s studio session tape log", rarities.Id, rarities.Name, 212, new DateTime(2024, 3, 2, 9, 0, 0, DateTimeKind.Utc)),
            new(5002, "A Day at the Races outtakes and rough mixes", rarities.Id, rarities.Name, 158, new DateTime(2024, 2, 18, 11, 0, 0, DateTimeKind.Utc)),
            new(5003, "Complete guide to Queen promo-only pressings", rarities.Id, rarities.Name, 301, new DateTime(2024, 4, 5, 15, 0, 0, DateTimeKind.Utc)),
        };
        return Task.FromResult<IReadOnlyList<ForumRecentThreadItem>>(
            items.OrderBy(item => item.Title, StringComparer.Ordinal).ToList());
    }

    public async Task<ForumArchiveStats> GetArchiveStatsAsync(CancellationToken cancellationToken = default) =>
        ForumArchiveStats.FromCategories(
            await GetCategoriesAsync(cancellationToken),
            await GetTotalThreadCountAsync(cancellationToken));

    public Task<int> GetTopicSitemapCountAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(GetTopicSitemapItems().Count);

    public Task<IReadOnlyList<ForumTopicSitemapItem>> GetTopicSitemapPageAsync(
        int offset,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var page = GetTopicSitemapItems()
            .Skip(Math.Max(offset, 0))
            .Take(pageSize)
            .ToList();
        return Task.FromResult<IReadOnlyList<ForumTopicSitemapItem>>(page);
    }

    public Task<ForumSearchPage> SearchForumAsync(
        string query,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return Task.FromResult(new ForumSearchPage([], 0, page, pageSize));
        }

        var allResults = seedCategories
            .SelectMany(category => GetTopics(category.Id)
                .Where(topic => topic.Title.Contains(query, StringComparison.OrdinalIgnoreCase))
                .Select(topic => new ForumSearchResult(
                    topic.Id,
                    topic.Title,
                    category.Id,
                    category.Name,
                    topic.ReplyCount,
                    topic.LastActivityAt,
                    topic.AuthorUsername)))
            .OrderByDescending(r => r.LastActivityAt)
            .ToList();

        var skip = Math.Max(page - 1, 0) * pageSize;
        var pageItems = allResults.Skip(skip).Take(pageSize).ToList();
        return Task.FromResult(new ForumSearchPage(pageItems, allResults.Count, page, pageSize));
    }

    public Task<ForumLegacyPostLocation?> FindLegacyPostAsync(
        int legacyPostId,
        CancellationToken cancellationToken = default)
    {
        var header = SampleForumData.TryGetSeedTopicHeader(legacyPostId)
            ?? TryGetCreatedTopicHeader(legacyPostId);
        if (header is not null)
        {
            return Task.FromResult<ForumLegacyPostLocation?>(
                new ForumLegacyPostLocation(header.TopicId, header.Title, legacyPostId, 0));
        }

        var topics = GetTopicSitemapItems();
        foreach (var topic in topics)
        {
            var posts = SampleForumData.CreateSeedPosts(topic.TopicId)
                .Concat(GetCreatedPosts(topic.TopicId))
                .OrderBy(post => post.PostedAt)
                .ToList();
            var index = posts.FindIndex(post => post.Id == legacyPostId);
            if (index >= 0)
            {
                return Task.FromResult<ForumLegacyPostLocation?>(
                    new ForumLegacyPostLocation(topic.TopicId, topic.Title, legacyPostId, index));
            }
        }

        return Task.FromResult<ForumLegacyPostLocation?>(null);
    }

    private IReadOnlyList<ForumTopicSitemapItem> GetTopicSitemapItems()
    {
        var created = (writeRepository?.GetCreatedThreads() ?? [])
            .Where(thread => !string.IsNullOrWhiteSpace(thread.Subject))
            .Select(thread => new ForumTopicSitemapItem(
                thread.TopicId,
                thread.Subject,
                thread.LastPostAt.UtcDateTime));

        return SampleForumData.CreateSeedTopicSitemapItems()
            .Concat(created)
            .GroupBy(item => item.TopicId)
            .Select(group => group.First() with
            {
                LastActivityAt = group.Max(item => item.LastActivityAt),
            })
            .OrderBy(item => item.TopicId)
            .ToList();
    }

    private IReadOnlyList<ForumTopicItem> GetTopics(int forumId)
    {
        var topics = SampleForumData.CreateSeedTopics(forumId).ToDictionary(topic => topic.Id);
        var threads = writeRepository?.GetCreatedThreads() ?? [];
        foreach (var thread in threads.Where(thread => thread.CategoryId == forumId))
        {
            if (topics.TryGetValue(thread.TopicId, out var seed))
            {
                topics[thread.TopicId] = seed with
                {
                    LastActivityAt = Max(seed.LastActivityAt, thread.LastPostAt.UtcDateTime)!.Value,
                    ReplyCount = seed.ReplyCount + AddedPostCount(thread),
                };
            }
            else
            {
                topics[thread.TopicId] = new ForumTopicItem(
                    thread.TopicId,
                    thread.Subject,
                    thread.LastPostAt.UtcDateTime,
                    "Member",
                    Math.Max(thread.PostCount - 1, 0),
                    null,
                    IsSticky: false);
            }
        }

        return topics.Values.ToList();
    }

    private static int AddedPostCount(ForumWriteThread thread) =>
        Math.Max(thread.PostCount - SampleForumData.CreateSeedPosts(thread.TopicId).Count, 0);

    private IReadOnlyList<ForumCategoryItem> GetCategories()
    {
        var createdThreads = writeRepository?.GetCreatedThreads() ?? [];
        var createdCategories = writeRepository?.GetCreatedCategories() ?? [];
        var all = seedCategories
            .Concat(createdCategories.Where(created => seedCategories.All(seed => seed.Id != created.Id)))
            .ToList();
        if (createdThreads.Count == 0)
        {
            return all;
        }

        return all
            .Select(category => OverlayCreatedThreadStats(category, createdThreads))
            .ToList();
    }

    private static ForumCategoryItem OverlayCreatedThreadStats(
        ForumCategoryItem category,
        IReadOnlyList<ForumWriteThread> createdThreads)
    {
        var categoryThreads = createdThreads
            .Where(thread => thread.CategoryId == category.Id)
            .OrderByDescending(thread => thread.LastPostAt)
            .ToList();
        if (categoryThreads.Count == 0)
        {
            return category;
        }

        var latestCreatedThread = categoryThreads[0];
        var latestCreatedActivity = latestCreatedThread.LastPostAt.UtcDateTime;
        var latestThreadTitle = category.LastActivityAt.HasValue && category.LastActivityAt.Value > latestCreatedActivity
            ? category.LatestThreadTitle
            : latestCreatedThread.Subject;

        return category with
        {
            PostCount = category.PostCount + categoryThreads.Sum(AddedPostCount),
            LastActivityAt = Max(category.LastActivityAt, latestCreatedActivity),
            LatestThreadTitle = latestThreadTitle,
        };
    }

    private static DateTime? Max(DateTime? left, DateTime right) =>
        !left.HasValue || right > left.Value ? right : left;

    private ForumTopicHeader? TryGetCreatedTopicHeader(int topicId)
    {
        var thread = writeRepository?.GetCreatedThreads()
            .SingleOrDefault(thread => thread.TopicId == topicId);
        if (thread is null)
        {
            return null;
        }

        var category = seedCategories.SingleOrDefault(category => category.Id == thread.CategoryId)
            ?? writeRepository?.GetCreatedCategories().SingleOrDefault(category => category.Id == thread.CategoryId);
        return category is null
            ? new ForumTopicHeader(thread.TopicId, thread.Subject, thread.CategoryId, NewsForumDiscussion.CategoryName, thread.HasPoll)
            : new ForumTopicHeader(thread.TopicId, thread.Subject, category.Id, category.Name, thread.HasPoll);
    }

    private IReadOnlyList<ForumPostItem> GetCreatedPosts(int topicId) =>
        writeRepository?.GetPostsForTopic(topicId)
            .Select(post => new ForumPostItem(
                post.PostId,
                post.Body,
                post.CreatedAt.UtcDateTime,
                post.DisplayName,
                Signature: null,
                AuthorPostCount: 0,
                AuthorMemberSince: null,
                AuthorMemberId: post.MemberId,
                EditedAt: post.EditedAt,
                EditCount: post.EditCount))
            .ToList() ?? [];
}
