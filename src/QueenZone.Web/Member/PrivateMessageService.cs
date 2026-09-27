using Microsoft.Extensions.Logging;
using QueenZone.Data;
using QueenZone.Data.Entities;

namespace QueenZone.Web;

public sealed partial class PrivateMessageService(
    IPrivateMessageRepository privateMessageRepository,
    IPrivateMessageModerationRepository privateMessageModerationRepository,
    IMemberAccountRepository memberAccountRepository,
    IMemberFollowRepository memberFollowRepository,
    PrivateMessageRateLimiter privateMessageRateLimiter,
    INotificationDispatcher notificationDispatcher,
    ILogger<PrivateMessageService> logger,
    TimeProvider timeProvider)
{
    public const string BodyRequiredMessage = "Message body is required.";

    public const string CannotMessageYourselfMessage = "You cannot message yourself.";

    public static string BodyTooLongMessage =>
        $"Message body must be {PrivateMessageLimits.MaxBodyLength} characters or fewer.";

    public const string UnableToSendMessage = "Unable to send message.";

    public const string RateLimitedMessage =
        "You're sending messages too quickly. Please wait a bit and try again.";

    public Task<PrivateInboxPage> GetInboxAsync(
        Guid memberId,
        int page = 1,
        int pageSize = PrivateMessageLimits.InboxPageSize,
        CancellationToken cancellationToken = default) =>
        privateMessageRepository.GetInboxAsync(memberId, page, pageSize, cancellationToken);

    public Task<int> CountUnreadConversationsAsync(
        Guid memberId,
        CancellationToken cancellationToken = default) =>
        privateMessageRepository.CountUnreadConversationsAsync(memberId, cancellationToken);

    public async Task<PrivateConversationDetail?> GetConversationAsync(
        Guid conversationId,
        Guid memberId,
        bool markRead = true,
        int? page = null,
        int pageSize = PrivateMessageLimits.ConversationPageSize,
        CancellationToken cancellationToken = default)
    {
        var detail = await privateMessageRepository.GetConversationAsync(
            conversationId,
            memberId,
            page,
            pageSize,
            cancellationToken);
        if (detail is null)
        {
            return null;
        }

        // Advance using the commit-ordered SortKey of the last message actually returned on this page.
        // CreatedAt alone is not unique/commit-ordered and can hide delayed or equal-timestamp messages.
        // Opening the default (latest) page marks through the newest message; older pages only advance
        // as far as that page's last message (and cannot move the cursor backwards).
        if (markRead && detail.Messages.Count > 0)
        {
            var lastReturned = detail.Messages[^1];
            await privateMessageRepository.MarkConversationReadAsync(
                conversationId,
                memberId,
                lastReturned.SortKey,
                lastReturned.CreatedAt,
                cancellationToken);
        }

        return detail;
    }

    /// <summary>
    /// Same sendability rules as website <c>/messages/{id}</c>: blocked or
    /// deleted counterparts cannot receive a reply.
    /// </summary>
    public async Task<bool> CanSendReplyAsync(
        Guid memberId,
        PrivateConversationDetail detail,
        CancellationToken cancellationToken = default)
    {
        var otherIsDeleted = string.Equals(
            detail.OtherParticipantDisplayName,
            MemberAccountDeletionPolicy.DeletedDisplayName,
            StringComparison.Ordinal);
        if (otherIsDeleted)
        {
            return false;
        }

        return !await IsMessagingBlockedAsync(
            memberId,
            detail.OtherParticipantId,
            cancellationToken);
    }

    public async Task<PrivateMessageSendResult> ComposeAsync(
        Guid senderMemberId,
        Guid recipientMemberId,
        string? body,
        CancellationToken cancellationToken = default)
    {
        if (senderMemberId == recipientMemberId)
        {
            return new PrivateMessageSendResult(false, null, CannotMessageYourselfMessage);
        }

        var sender = await memberAccountRepository.FindByIdAsync(senderMemberId, cancellationToken);
        if (sender is null || sender.IsSuspended)
        {
            return new PrivateMessageSendResult(false, null, UnableToSendMessage);
        }

        var recipient = await memberAccountRepository.FindByIdAsync(recipientMemberId, cancellationToken);
        if (recipient is null || recipient.DeletionRequestedAt is not null)
        {
            return new PrivateMessageSendResult(false, null, "Recipient was not found.");
        }

        if (await privateMessageModerationRepository.IsMessagingBlockedAsync(
                senderMemberId,
                recipientMemberId,
                cancellationToken))
        {
            return new PrivateMessageSendResult(false, null, UnableToSendMessage);
        }

        var hasConversation = await privateMessageRepository.HasConversationBetweenAsync(
            senderMemberId,
            recipientMemberId,
            cancellationToken);
        if (!hasConversation
            && !await CanStartNewConversationAsync(senderMemberId, recipient, cancellationToken))
        {
            return new PrivateMessageSendResult(false, null, UnableToSendMessage);
        }

        if (!await privateMessageRateLimiter.IsSendAllowedAsync(
                senderMemberId,
                sender.CreatedAt,
                body ?? string.Empty,
                isNewConversation: !hasConversation,
                cancellationToken))
        {
            return new PrivateMessageSendResult(false, null, RateLimitedMessage);
        }

        if (RejectInvalidBody(body, out var composeBody) is { } invalidCompose)
        {
            return invalidCompose;
        }

        var result = await privateMessageRepository.SendNewOrExistingAsync(
            senderMemberId,
            recipientMemberId,
            composeBody,
            timeProvider.GetUtcNow(),
            cancellationToken);
        if (result.Succeeded && result.ConversationId is Guid conversationId)
        {
            await TryNotifyPrivateMessageAsync(
                conversationId,
                recipientMemberId,
                senderMemberId,
                cancellationToken);
        }

        return result;
    }

    public async Task<PrivateMessageSendResult> ReplyAsync(
        Guid conversationId,
        Guid senderMemberId,
        string? body,
        CancellationToken cancellationToken = default)
    {
        var isParticipant = await privateMessageRepository.IsParticipantAsync(
            conversationId,
            senderMemberId,
            cancellationToken);
        if (!isParticipant)
        {
            return new PrivateMessageSendResult(
                false,
                null,
                "You are not a participant in this conversation.");
        }

        var sender = await memberAccountRepository.FindByIdAsync(senderMemberId, cancellationToken);
        if (sender is null || sender.IsSuspended)
        {
            return new PrivateMessageSendResult(false, null, UnableToSendMessage);
        }

        var otherParticipantId = await privateMessageRepository.GetOtherParticipantIdAsync(
            conversationId,
            senderMemberId,
            cancellationToken);
        if (otherParticipantId is Guid other)
        {
            var otherParticipant = await memberAccountRepository.FindByIdAsync(other, cancellationToken);
            if (otherParticipant?.DeletionRequestedAt is not null
                || await privateMessageModerationRepository.IsMessagingBlockedAsync(
                    senderMemberId,
                    other,
                    cancellationToken))
            {
                return new PrivateMessageSendResult(false, null, UnableToSendMessage);
            }
        }

        if (!await privateMessageRateLimiter.IsSendAllowedAsync(
                senderMemberId,
                sender.CreatedAt,
                body ?? string.Empty,
                isNewConversation: false,
                cancellationToken))
        {
            return new PrivateMessageSendResult(false, null, RateLimitedMessage);
        }

        if (RejectInvalidBody(body, out var replyBody) is { } invalidReply)
        {
            return invalidReply;
        }

        var result = await privateMessageRepository.ReplyAsync(
            conversationId,
            senderMemberId,
            replyBody,
            timeProvider.GetUtcNow(),
            cancellationToken);
        if (result.Succeeded && otherParticipantId is Guid recipientId)
        {
            await TryNotifyPrivateMessageAsync(
                conversationId,
                recipientId,
                senderMemberId,
                cancellationToken);
        }

        return result;
    }

    private static PrivateMessageSendResult? RejectInvalidBody(string? body, out string normalized)
    {
        normalized = string.IsNullOrWhiteSpace(body) ? string.Empty : body.Trim();
        if (normalized.Length == 0)
        {
            return new PrivateMessageSendResult(false, null, BodyRequiredMessage);
        }

        if (normalized.Length > PrivateMessageLimits.MaxBodyLength)
        {
            return new PrivateMessageSendResult(false, null, BodyTooLongMessage);
        }

        return null;
    }

    private async Task TryNotifyPrivateMessageAsync(
        Guid conversationId,
        Guid recipientMemberId,
        Guid senderMemberId,
        CancellationToken cancellationToken)
    {
        try
        {
            await notificationDispatcher.NotifyPrivateMessageAsync(
                conversationId,
                recipientMemberId,
                senderMemberId,
                cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Log.PushDispatchFailedAfterPrivateMessage(
                logger,
                ex,
                recipientMemberId,
                conversationId,
                ex.Message);
        }
    }

    public Task<PrivateInboxPage> GetArchivedInboxAsync(
        Guid memberId,
        int page = 1,
        int pageSize = PrivateMessageLimits.InboxPageSize,
        CancellationToken cancellationToken = default) =>
        privateMessageRepository.GetArchivedInboxAsync(memberId, page, pageSize, cancellationToken);

    public Task<bool> ArchiveConversationAsync(
        Guid conversationId,
        Guid memberId,
        CancellationToken cancellationToken = default) =>
        privateMessageRepository.ArchiveConversationAsync(conversationId, memberId, cancellationToken);

    public Task<bool> UnarchiveConversationAsync(
        Guid conversationId,
        Guid memberId,
        CancellationToken cancellationToken = default) =>
        privateMessageRepository.UnarchiveConversationAsync(conversationId, memberId, cancellationToken);

    public Task<bool> RemoveConversationAsync(
        Guid conversationId,
        Guid memberId,
        CancellationToken cancellationToken = default) =>
        privateMessageRepository.RemoveConversationAsync(conversationId, memberId, cancellationToken);

    public async Task<PrivateMessageBlockResult> BlockAsync(
        Guid blockerMemberId,
        Guid blockedMemberId,
        CancellationToken cancellationToken = default)
    {
        if (blockerMemberId == blockedMemberId)
        {
            return new PrivateMessageBlockResult(false, "You cannot block yourself.");
        }

        var target = await memberAccountRepository.FindByIdAsync(blockedMemberId, cancellationToken);
        if (target is null || target.DeletionRequestedAt is not null)
        {
            return new PrivateMessageBlockResult(false, "Member was not found.");
        }

        await privateMessageModerationRepository.BlockAsync(
            blockerMemberId,
            blockedMemberId,
            timeProvider.GetUtcNow(),
            cancellationToken);
        await memberFollowRepository.UnfollowAsync(
            blockerMemberId,
            blockedMemberId,
            cancellationToken);
        return new PrivateMessageBlockResult(true, null);
    }

    public Task<bool> UnblockAsync(
        Guid blockerMemberId,
        Guid blockedMemberId,
        CancellationToken cancellationToken = default) =>
        privateMessageModerationRepository.UnblockAsync(blockerMemberId, blockedMemberId, cancellationToken);

    public Task<bool> HasBlockedAsync(
        Guid blockerMemberId,
        Guid blockedMemberId,
        CancellationToken cancellationToken = default) =>
        privateMessageModerationRepository.IsBlockedAsync(blockerMemberId, blockedMemberId, cancellationToken);

    public Task<IReadOnlySet<Guid>> ListBlockedMemberIdsAsync(
        Guid blockerMemberId,
        IReadOnlyCollection<Guid> candidateMemberIds,
        CancellationToken cancellationToken = default) =>
        privateMessageModerationRepository.ListBlockedMemberIdsAsync(
            blockerMemberId,
            candidateMemberIds,
            cancellationToken);

    public Task<bool> IsMessagingBlockedAsync(
        Guid memberA,
        Guid memberB,
        CancellationToken cancellationToken = default) =>
        privateMessageModerationRepository.IsMessagingBlockedAsync(memberA, memberB, cancellationToken);

    public Task<IReadOnlyList<MemberRecipientMatch>> SearchRecipientsAsync(
        Guid currentMemberId,
        string? query,
        CancellationToken cancellationToken = default) =>
        memberAccountRepository.SearchByDisplayNameAsync(
            query ?? string.Empty,
            currentMemberId,
            PrivateMessageLimits.MaxRecipientSearchResults,
            cancellationToken);

    public static bool CanMessage(Guid? currentMemberId, Guid? targetMemberId) =>
        currentMemberId is Guid current
        && targetMemberId is Guid target
        && current != target;

    public async Task<bool> CanMessageAsync(
        Guid? currentMemberId,
        Guid? targetMemberId,
        CancellationToken cancellationToken = default)
    {
        if (!CanMessage(currentMemberId, targetMemberId))
        {
            return false;
        }

        var target = await memberAccountRepository.FindByIdAsync(targetMemberId!.Value, cancellationToken);
        if (target?.DeletionRequestedAt is not null)
        {
            return false;
        }

        if (await privateMessageModerationRepository.IsMessagingBlockedAsync(
                currentMemberId!.Value,
                targetMemberId.Value,
                cancellationToken))
        {
            return false;
        }

        if (await privateMessageRepository.HasConversationBetweenAsync(
                currentMemberId.Value,
                targetMemberId.Value,
                cancellationToken))
        {
            return true;
        }

        return target is not null
            && await CanStartNewConversationAsync(currentMemberId.Value, target, cancellationToken);
    }

    private async Task<bool> CanStartNewConversationAsync(
        Guid senderMemberId,
        MemberAccount recipient,
        CancellationToken cancellationToken)
    {
        return recipient.MessagePrivacy switch
        {
            MemberMessagePrivacy.Members => true,
            MemberMessagePrivacy.Nobody => false,
            MemberMessagePrivacy.Followed => await memberFollowRepository.IsFollowingAsync(
                recipient.Id,
                senderMemberId,
                cancellationToken),
            _ => false,
        };
    }

    public async Task<PrivateMessageReportResult> ReportMessageAsync(
        Guid reporterMemberId,
        Guid conversationId,
        Guid messageId,
        string? reason,
        CancellationToken cancellationToken = default)
    {
        var normalizedReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        if (normalizedReason is { Length: > PrivateMessageLimits.MaxReportReasonLength })
        {
            return new PrivateMessageReportResult(false, null, PrivateMessageReportText.ReasonTooLong);
        }

        return await privateMessageModerationRepository.CreateReportAsync(
            reporterMemberId,
            conversationId,
            messageId,
            normalizedReason,
            timeProvider.GetUtcNow(),
            cancellationToken);
    }
}
