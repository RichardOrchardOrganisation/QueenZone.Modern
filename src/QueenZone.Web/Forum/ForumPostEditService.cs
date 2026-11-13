using Microsoft.Extensions.Options;
using QueenZone.Data;
using QueenZone.Storage;

namespace QueenZone.Web;

public sealed record ForumPostEditActor(Guid MemberId, bool IsAdmin);

public sealed record ForumPostEditResult(
    ForumPostUpdateStatus Status,
    string SanitizedBody,
    int TopicId = 0,
    string TopicSubject = "",
    int PositionInThread = 1);

public sealed class ForumPostEditService(
    IForumWriteRepository forumWriteRepository,
    UgcHtml ugcHtml,
    ForumPostWriteEffects writeEffects,
    IOptions<ForumOptions> forumOptions)
{
    public async Task<ForumPostEditResult> EditAsync(
        int topicId,
        int postId,
        ForumPostEditActor actor,
        string? body,
        DateTimeOffset? expectedUpdatedAt,
        CancellationToken cancellationToken)
    {
        var sanitizedBody = ugcHtml.Sanitize(body ?? string.Empty);
        if (string.IsNullOrWhiteSpace(sanitizedBody))
        {
            return new(ForumPostUpdateStatus.ValidationFailed, sanitizedBody);
        }

        var post = await forumWriteRepository.GetPostAsync(postId, cancellationToken);
        if (post is null || post.TopicId != topicId)
        {
            return new(ForumPostUpdateStatus.NotFound, sanitizedBody);
        }

        var result = await forumWriteRepository.UpdatePostAsync(
            postId, actor.MemberId, sanitizedBody, actor.IsAdmin,
            forumOptions.Value.PostEditWindowMinutes, expectedUpdatedAt, cancellationToken);
        if (result.Status == ForumPostUpdateStatus.Success)
        {
            writeEffects.InvalidateForumStatsCache();
        }

        return new(result.Status, sanitizedBody, post.TopicId, post.TopicSubject, post.PositionInThread);
    }
}
