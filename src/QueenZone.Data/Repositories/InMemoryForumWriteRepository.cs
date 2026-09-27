namespace QueenZone.Data;

public sealed class InMemoryForumWriteRepository : IForumWriteRepository
{
    private readonly List<ForumWriteThread> threads = [];
    private readonly List<InMemoryForumWritePost> posts = [];
    private readonly List<ForumCategoryItem> createdCategories = [];
    private readonly object sync = new();
    private int nextTopicId = 200_000;
    private int nextPostId = 2_000_000;
    private InMemoryForumPollRepository? pollRepository;

    public void AttachPollRepository(InMemoryForumPollRepository repository) =>
        pollRepository = repository;

    public async Task<ForumThreadCreateResult> CreateThreadAsync(NewForumThread thread, CancellationToken cancellationToken = default)
    {
        int topicId;
        int postId;
        lock (sync)
        {
            topicId = nextTopicId++;
            postId = nextPostId++;
            threads.Add(new ForumWriteThread(
                topicId,
                thread.CategoryId,
                thread.Subject.Trim(),
                thread.CreatedAt,
                thread.CreatedAt,
                1,
                IsLocked: false,
                HasPoll: thread.Poll is not null));
            posts.Add(new InMemoryForumWritePost(
                postId,
                topicId,
                thread.AuthorMemberId,
                thread.AuthorDisplayName,
                thread.Body,
                thread.CreatedAt));
        }

        if (thread.Poll is not null && pollRepository is not null)
        {
            pollRepository.RegisterTopic(topicId);
            await pollRepository.CreatePollAsync(
                topicId,
                thread.Poll with { CreatedByMemberId = thread.AuthorMemberId },
                cancellationToken);
        }

        return new ForumThreadCreateResult(topicId, postId);
    }

    public Task<int> CreatePostAsync(NewForumPost post, CancellationToken cancellationToken = default)
    {
        lock (sync)
        {
            var index = threads.FindIndex(thread => thread.TopicId == post.TopicId);
            if (index < 0)
            {
                var header = SampleForumData.TryGetSeedTopicHeader(post.TopicId)
                    ?? throw new InvalidOperationException("Forum thread not found.");
                threads.Add(new ForumWriteThread(
                    post.TopicId,
                    header.ForumId,
                    header.Title,
                    post.CreatedAt,
                    post.CreatedAt,
                    SampleForumData.CreateSeedPosts(post.TopicId).Count,
                    IsLocked: false));
                index = threads.Count - 1;
            }

            var thread = threads[index];
            if (thread.IsLocked)
            {
                throw new InvalidOperationException("Forum thread is locked.");
            }

            var postId = nextPostId++;
            posts.Add(new InMemoryForumWritePost(
                postId,
                post.TopicId,
                post.AuthorMemberId,
                post.AuthorDisplayName,
                post.Body,
                post.CreatedAt));
            threads[index] = thread with
            {
                LastPostAt = post.CreatedAt,
                PostCount = thread.PostCount + 1,
            };
            return Task.FromResult(postId);
        }
    }

    public Task<ForumEditablePost?> GetPostAsync(int postId, CancellationToken cancellationToken = default)
    {
        lock (sync)
        {
            var post = posts.SingleOrDefault(item => item.PostId == postId);
            if (post is null)
            {
                return Task.FromResult<ForumEditablePost?>(null);
            }

            var subject = threads.SingleOrDefault(thread => thread.TopicId == post.TopicId)?.Subject
                ?? SampleForumData.TryGetSeedTopicHeader(post.TopicId)?.Title
                ?? string.Empty;

            var seedCount = SampleForumData.CreateSeedPosts(post.TopicId).Count;
            var createdBefore = posts.Count(item => item.TopicId == post.TopicId && item.PostId <= postId);
            // Created posts append after seed posts for topics that started from sample data.
            var position = seedCount > 0 && threads.Any(thread => thread.TopicId == post.TopicId)
                ? seedCount + createdBefore
                : createdBefore;

            return Task.FromResult<ForumEditablePost?>(new ForumEditablePost(
                post.PostId,
                post.TopicId,
                subject,
                post.Body,
                post.MemberId,
                post.DisplayName,
                post.CreatedAt,
                post.EditedAt,
                post.EditCount,
                Math.Max(1, position),
                post.UpdatedAt ?? post.CreatedAt));
        }
    }

    public Task<ForumPostUpdateResult> UpdatePostAsync(
        int postId,
        Guid editorMemberId,
        string sanitisedBody,
        bool isAdmin,
        int editWindowMinutes,
        DateTimeOffset? expectedUpdatedAt = null,
        CancellationToken cancellationToken = default)
    {
        lock (sync)
        {
            var index = posts.FindIndex(item => item.PostId == postId);
            if (index < 0)
            {
                return Task.FromResult(new ForumPostUpdateResult(ForumPostUpdateStatus.NotFound));
            }

            var post = posts[index];
            var subject = threads.SingleOrDefault(thread => thread.TopicId == post.TopicId)?.Subject
                ?? SampleForumData.TryGetSeedTopicHeader(post.TopicId)?.Title
                ?? string.Empty;
            var utcNow = DateTimeOffset.UtcNow;
            var canEdit = ForumPostEditRules.CanEdit(
                post.MemberId,
                editorMemberId,
                isAdmin,
                post.CreatedAt,
                editWindowMinutes,
                utcNow);

            if (!canEdit)
            {
                if (!isAdmin && post.MemberId == editorMemberId && editWindowMinutes == 0)
                {
                    return Task.FromResult(new ForumPostUpdateResult(ForumPostUpdateStatus.EditingDisabled, post.TopicId, subject));
                }

                if (!isAdmin
                    && post.MemberId == editorMemberId
                    && editWindowMinutes > 0
                    && utcNow > post.CreatedAt.AddMinutes(editWindowMinutes))
                {
                    return Task.FromResult(new ForumPostUpdateResult(ForumPostUpdateStatus.EditWindowExpired, post.TopicId, subject));
                }

                return Task.FromResult(new ForumPostUpdateResult(ForumPostUpdateStatus.Forbidden, post.TopicId, subject));
            }

            if (expectedUpdatedAt is DateTimeOffset expected
                && (post.UpdatedAt ?? post.CreatedAt) != expected)
            {
                return Task.FromResult(new ForumPostUpdateResult(
                    ForumPostUpdateStatus.ConcurrencyConflict,
                    post.TopicId,
                    subject));
            }

            posts[index] = post with
            {
                Body = sanitisedBody,
                EditedAt = utcNow,
                EditCount = post.EditCount + 1,
                UpdatedAt = utcNow,
            };

            return Task.FromResult(new ForumPostUpdateResult(ForumPostUpdateStatus.Success, post.TopicId, subject));
        }
    }

    public Task<ForumWriteThread?> GetThreadAsync(int topicId, CancellationToken cancellationToken = default)
    {
        lock (sync)
        {
            var thread = threads.SingleOrDefault(thread => thread.TopicId == topicId);
            if (thread is not null)
            {
                return Task.FromResult<ForumWriteThread?>(thread);
            }

            var header = SampleForumData.TryGetSeedTopicHeader(topicId);
            return Task.FromResult<ForumWriteThread?>(header is null
                ? null
                : new ForumWriteThread(
                    topicId,
                    header.ForumId,
                    header.Title,
                    DateTimeOffset.MinValue,
                    DateTimeOffset.MinValue,
                    SampleForumData.CreateSeedPosts(topicId).Count,
                    IsLocked: false));
        }
    }

    public Task<int> CountPostsByMemberSinceAsync(Guid memberId, DateTimeOffset since, CancellationToken cancellationToken = default)
    {
        lock (sync)
        {
            return Task.FromResult(posts.Count(post =>
                post.MemberId == memberId && post.CreatedAt >= since && !post.IsHidden));
        }
    }

    public Task<int> CountApprovedPostsByMemberAsync(Guid memberId, CancellationToken cancellationToken = default)
    {
        lock (sync)
        {
            return Task.FromResult(posts.Count(post => post.MemberId == memberId && !post.IsHidden));
        }
    }

    public Task<ForumAuthorContentSummary> GetAuthorForumContentSummaryAsync(
        Guid? memberId, string displayName, CancellationToken cancellationToken = default)
    {
        lock (sync)
        {
            var matching = MatchingPosts(memberId, displayName).ToList();
            var started = StartedTopicIds(matching);
            var matchingThreads = threads.Where(thread => started.Contains(thread.TopicId)).ToList();
            return Task.FromResult(new ForumAuthorContentSummary(
                memberId, displayName.Trim(), matching.Count, matchingThreads.Count,
                matching.Count + matchingThreads.Count > 0
                    && matching.All(post => post.IsHidden)
                    && matchingThreads.All(thread => thread.IsHidden)));
        }
    }

    public async Task<ForumAuthorContentSummary?> FindNoAccountForumAuthorAsync(
        string displayName, CancellationToken cancellationToken = default)
    {
        var summary = await GetAuthorForumContentSummaryAsync(null, displayName, cancellationToken);
        return summary.PostCount == 0 ? null : summary;
    }

    public Task HideAuthorForumContentAsync(
        Guid? memberId, string displayName, CancellationToken cancellationToken = default)
    {
        lock (sync)
        {
            var matching = MatchingPosts(memberId, displayName).ToList();
            var startedTopicIds = StartedTopicIds(matching);
            for (var i = 0; i < threads.Count; i++)
            {
                if (startedTopicIds.Contains(threads[i].TopicId))
                {
                    threads[i] = threads[i] with { IsHidden = true };
                }
            }

            for (var i = 0; i < posts.Count; i++)
            {
                if (matching.Contains(posts[i]))
                {
                    posts[i] = posts[i] with { IsHidden = true };
                }
            }

            return Task.CompletedTask;
        }
    }

    public Task UnhideAuthorForumContentAsync(
        Guid? memberId, string displayName, CancellationToken cancellationToken = default)
    {
        lock (sync)
        {
            var matching = MatchingPosts(memberId, displayName).ToList();
            var startedTopicIds = StartedTopicIds(matching);
            for (var i = 0; i < threads.Count; i++)
            {
                if (startedTopicIds.Contains(threads[i].TopicId))
                {
                    threads[i] = threads[i] with { IsHidden = false };
                }
            }

            for (var i = 0; i < posts.Count; i++)
            {
                if (matching.Contains(posts[i]))
                {
                    posts[i] = posts[i] with { IsHidden = false };
                }
            }

            return Task.CompletedTask;
        }
    }

    private IEnumerable<InMemoryForumWritePost> MatchingPosts(Guid? memberId, string displayName)
    {
        var name = displayName.Trim();
        return posts.Where(post => memberId.HasValue
            ? post.MemberId == memberId.Value
            : string.Equals(post.DisplayName.Trim(), name, StringComparison.OrdinalIgnoreCase));
    }

    private HashSet<int> StartedTopicIds(IEnumerable<InMemoryForumWritePost> matching)
    {
        var matchingSet = matching.ToHashSet();
        return posts.GroupBy(post => post.TopicId)
            .Where(group => matchingSet.Contains(group.OrderBy(post => post.PostId).First()))
            .Select(group => group.Key)
            .ToHashSet();
    }

    public Task<int> EnsureCategoryAsync(
        string slug,
        string name,
        CancellationToken cancellationToken = default)
    {
        lock (sync)
        {
            var existing = NewsForumDiscussion.FindExistingCategory(
                createdCategories.Concat(SampleForumData.CreateSeedCategories()),
                category => category.Name,
                slug,
                name);
            if (existing is not null)
            {
                return Task.FromResult(existing.Id);
            }

            var nextId = Math.Max(
                createdCategories.Select(category => category.Id).DefaultIfEmpty(0).Max(),
                SampleForumData.CreateSeedCategories().Select(category => category.Id).DefaultIfEmpty(0).Max()) + 1;
            if (NewsForumDiscussion.IsTheMusic(name) || nextId == 1)
            {
                nextId = Math.Max(nextId, 2);
            }

            var created = new ForumCategoryItem(
                nextId,
                name,
                "Discussion of published QueenZone news articles.",
                0,
                null,
                null,
                nextId * 10);
            createdCategories.Add(created);
            return Task.FromResult(nextId);
        }
    }

    public IReadOnlyList<ForumCategoryItem> GetCreatedCategories()
    {
        lock (sync)
        {
            return createdCategories.ToList();
        }
    }

    internal void Clear()
    {
        lock (sync)
        {
            threads.Clear();
            posts.Clear();
            createdCategories.Clear();
        }
    }

    public IReadOnlyList<ForumWriteThread> GetCreatedThreads()
    {
        lock (sync)
        {
            return threads.Where(thread => !thread.IsHidden).ToList();
        }
    }

    public IReadOnlyList<InMemoryForumWritePost> GetPostsForTopic(int topicId)
    {
        lock (sync)
        {
            return posts.Where(post => post.TopicId == topicId && !post.IsHidden).ToList();
        }
    }
}

public sealed record InMemoryForumWritePost(
    int PostId,
    int TopicId,
    Guid MemberId,
    string DisplayName,
    string Body,
    DateTimeOffset CreatedAt,
    DateTimeOffset? EditedAt = null,
    int EditCount = 0,
    bool IsHidden = false,
    DateTimeOffset? UpdatedAt = null);
