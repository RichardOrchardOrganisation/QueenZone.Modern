using Microsoft.Extensions.Logging;
using QueenZone.Web.Search;

namespace QueenZone.Web;

public sealed partial class ForumPostWriteEffects(
    PublicQueryCacheService publicQueryCache,
    ForumSearchIndexSynchronizer forumSearchIndex,
    INotificationDispatcher notificationDispatcher,
    ILogger<ForumPostWriteService> logger)
{
    public Task UpsertThreadAsync(int topicId, string title, DateTimeOffset createdAt, CancellationToken cancellationToken) =>
        forumSearchIndex.UpsertThreadAsync(topicId, title, createdAt, cancellationToken);

    public void InvalidateForumStatsCache() => publicQueryCache.InvalidateForumStatsCache();

    public async Task NotifyReplyAsync(
        int topicId, int postId, Guid memberId, string title, CancellationToken cancellationToken)
    {
        try
        {
            await notificationDispatcher.NotifyForumReplyAsync(
                topicId,
                postId,
                memberId,
                title,
                cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Log.PushDispatchFailedAfterForumReply(
                logger,
                ex,
                topicId,
                postId,
                memberId,
                ex.Message);
        }
    }
}
