namespace QueenZone.Data;

public sealed class InMemoryNewsForumDiscussionLookup(InMemoryForumWriteRepository writeRepository)
    : INewsForumDiscussionLookup
{
    public async Task<IReadOnlyDictionary<int, int>> GetReplyCountsAsync(
        IReadOnlyList<int> topicIds,
        CancellationToken cancellationToken = default)
    {
        var counts = new Dictionary<int, int>();
        foreach (var topicId in topicIds.Distinct())
        {
            var thread = await writeRepository.GetThreadAsync(topicId, cancellationToken);
            if (thread is not null)
            {
                counts[topicId] = Math.Max(0, thread.PostCount - 1);
            }
        }

        return counts;
    }

    public async Task<NewsForumDiscussionLookupResult> GetDiscussionAsync(
        int topicId,
        int previewCount,
        CancellationToken cancellationToken = default)
    {
        var thread = await writeRepository.GetThreadAsync(topicId, cancellationToken);
        if (thread is null)
        {
            return NewsForumDiscussionLookupResult.Missing;
        }

        var created = writeRepository.GetPostsForTopic(topicId);
        IReadOnlyList<NewsDiscussionPreview> chronological = created.Count > 0
            ? created
                .Select(post => new NewsDiscussionPreview(
                    post.DisplayName,
                    post.CreatedAt.UtcDateTime,
                    NewsForumDiscussion.TruncatePlain(post.Body, NewsForumDiscussion.PreviewExcerptMaxLength)))
                .ToList()
            : SampleForumData.CreateSeedPosts(topicId)
                .Select(post => new NewsDiscussionPreview(
                    post.AuthorUsername,
                    post.PostedAt,
                    NewsForumDiscussion.TruncatePlain(post.Body, NewsForumDiscussion.PreviewExcerptMaxLength)))
                .ToList();
        var replies = chronological.Skip(1).ToList();
        var take = Math.Max(previewCount, 0);
        var preview = replies.Count <= take
            ? replies
            : replies.Skip(replies.Count - take).ToList();
        var replyCount = Math.Max(0, thread.PostCount - 1);
        return NewsForumDiscussionLookupResult.Found(replyCount, preview);
    }
}
