using Microsoft.EntityFrameworkCore;
using QueenZone.Data.Entities;

namespace QueenZone.Data;

public sealed class EfNewsForumDiscussionLookup(QueenZoneDbContext dbContext) : INewsForumDiscussionLookup
{
    public async Task<IReadOnlyDictionary<int, int>> GetReplyCountsAsync(
        IReadOnlyList<int> topicIds,
        CancellationToken cancellationToken = default)
    {
        if (topicIds.Count == 0)
        {
            return new Dictionary<int, int>();
        }

        var ids = topicIds.Distinct().ToArray();
        return await dbContext.ModernForumThreads
            .AsNoTracking()
            .Where(thread => ids.Contains(thread.LegacyTopicId) && !thread.IsHidden)
            .ToDictionaryAsync(thread => thread.LegacyTopicId, thread => thread.ReplyCount, cancellationToken);
    }

    public async Task<NewsForumDiscussionLookupResult> GetDiscussionAsync(
        int topicId,
        int previewCount,
        CancellationToken cancellationToken = default)
    {
        var replyCount = await dbContext.ModernForumThreads
            .AsNoTracking()
            .Where(thread => thread.LegacyTopicId == topicId && !thread.IsHidden)
            .Select(thread => (int?)thread.ReplyCount)
            .FirstOrDefaultAsync(cancellationToken);
        if (replyCount is null)
        {
            return NewsForumDiscussionLookupResult.Missing;
        }

        var take = Math.Max(previewCount, 0);
        if (take == 0)
        {
            return NewsForumDiscussionLookupResult.Found(replyCount.Value, []);
        }

        var starterId = await dbContext.ModernForumPosts
            .AsNoTracking()
            .Where(post => post.LegacyThreadTopicId == topicId && !post.IsHidden)
            .OrderBy(post => post.PostedAt)
            .ThenBy(post => post.LegacyPostId)
            .Select(post => (int?)post.LegacyPostId)
            .FirstOrDefaultAsync(cancellationToken);

        if (starterId is null)
        {
            return NewsForumDiscussionLookupResult.Found(replyCount.Value, []);
        }

        var replies = await dbContext.ModernForumPosts
            .AsNoTracking()
            .Where(post =>
                post.LegacyThreadTopicId == topicId
                && !post.IsHidden
                && post.LegacyPostId != starterId.Value)
            .OrderByDescending(post => post.PostedAt)
            .ThenByDescending(post => post.LegacyPostId)
            .Take(take)
            .Select(post => new
            {
                post.AuthorDisplayName,
                post.PostedAt,
                post.BodyHtml,
            })
            .ToListAsync(cancellationToken);

        replies.Reverse();
        var preview = replies
            .Select(post => new NewsDiscussionPreview(
                post.AuthorDisplayName,
                post.PostedAt ?? DateTime.MinValue,
                NewsForumDiscussion.TruncatePlain(post.BodyHtml, NewsForumDiscussion.PreviewExcerptMaxLength)))
            .ToList();
        return NewsForumDiscussionLookupResult.Found(replyCount.Value, preview);
    }
}
