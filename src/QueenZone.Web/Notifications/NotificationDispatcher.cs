using Microsoft.Extensions.Logging;
using QueenZone.Data;
using QueenZone.Data.Entities;

namespace QueenZone.Web;

public sealed partial class NotificationDispatcher(
    INotificationPreferenceRepository preferenceRepository,
    IDeviceTokenRepository deviceTokenRepository,
    ITopicWatchLookup topicWatchLookup,
    IPushTransport pushTransport,
    ILogger<NotificationDispatcher> logger) : INotificationDispatcher
{
    public Task NotifyForumReplyAsync(
        int topicId,
        int postId,
        Guid authorMemberId,
        string topicTitle,
        CancellationToken cancellationToken = default) =>
        DispatchSafelyAsync(
            NotificationCategory.ForumReply,
            () => ResolveForumAudienceAsync(topicId, authorMemberId, cancellationToken),
            PushNotificationPayload.ForumReply(topicId, postId, topicTitle),
            cancellationToken);

    public Task NotifyPrivateMessageAsync(
        Guid conversationId,
        Guid recipientMemberId,
        Guid senderMemberId,
        CancellationToken cancellationToken = default) =>
        DispatchSafelyAsync(
            NotificationCategory.PrivateMessage,
            () => ResolvePrivateMessageAudienceAsync(recipientMemberId, senderMemberId, cancellationToken),
            PushNotificationPayload.PrivateMessage(conversationId),
            cancellationToken);

    public Task NotifyNewsPublishedAsync(
        int articleId,
        string title,
        CancellationToken cancellationToken = default) =>
        DispatchSafelyAsync(
            NotificationCategory.News,
            () => preferenceRepository.ListEnabledAsync(NotificationCategory.News, cancellationToken),
            PushNotificationPayload.News(articleId, title),
            cancellationToken);

    private async Task DispatchSafelyAsync(
        NotificationCategory category,
        Func<Task<IReadOnlyList<Guid>>> resolveAudience,
        PushNotificationPayload payload,
        CancellationToken cancellationToken)
    {
        try
        {
            var memberIds = await resolveAudience();
            if (memberIds.Count == 0)
            {
                return;
            }

            var tokens = await deviceTokenRepository.ListByMemberIdsAsync(memberIds, cancellationToken);
            if (tokens.Count == 0)
            {
                return;
            }

            var unregistered = await pushTransport.SendAsync(
                DeviceTokenMapper.ToPushTokens(tokens), payload, cancellationToken);
            foreach (var token in unregistered.Distinct())
            {
                var platform = DeviceTokenMapper.ToEntityPlatform(token.Platform);
                var matchingRegistrations = tokens.Where(row =>
                    row.MemberAccountId == token.MemberAccountId
                    && row.Platform == platform
                    && string.Equals(row.Token, token.Token, StringComparison.Ordinal));
                foreach (var registration in matchingRegistrations)
                {
                    await deviceTokenRepository.DeleteIfTokenMatchesAsync(
                        registration.Id,
                        registration.UpdatedAt,
                        token.MemberAccountId,
                        platform,
                        token.Token,
                        cancellationToken);
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Log.PushDispatchFailedForCategory(
                logger,
                ex,
                category.ToPayloadValue(),
                ex.Message);
        }
    }

    private async Task<IReadOnlyList<Guid>> ResolveForumAudienceAsync(
        int topicId,
        Guid authorMemberId,
        CancellationToken cancellationToken)
    {
        var watchers = await topicWatchLookup.ListMemberIdsAsync(topicId, cancellationToken);
        var candidates = watchers.Where(id => id != authorMemberId).ToArray();
        if (candidates.Length == 0)
        {
            return [];
        }

        return await preferenceRepository.FilterEnabledAsync(
            candidates,
            NotificationCategory.ForumReply,
            cancellationToken);
    }

    private async Task<IReadOnlyList<Guid>> ResolvePrivateMessageAudienceAsync(
        Guid recipientMemberId,
        Guid senderMemberId,
        CancellationToken cancellationToken)
    {
        if (recipientMemberId == senderMemberId)
        {
            return [];
        }

        return await preferenceRepository.FilterEnabledAsync(
            [recipientMemberId],
            NotificationCategory.PrivateMessage,
            cancellationToken);
    }
}
