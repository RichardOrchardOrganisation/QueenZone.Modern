namespace QueenZone.Data;

public interface IPrivateMessageRepository
{
    Task<PrivateInboxPage> GetInboxAsync(
        Guid memberId,
        int page = 1,
        int pageSize = PrivateMessageLimits.InboxPageSize,
        CancellationToken cancellationToken = default);

    Task<int> CountUnreadConversationsAsync(
        Guid memberId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Loads one page of messages for a conversation the member participates in.
    /// When <paramref name="page"/> is null or less than 1, returns the latest page
    /// (newest messages). Messages within the page are ordered oldest-first by SortKey.
    /// </summary>
    Task<PrivateConversationDetail?> GetConversationAsync(
        Guid conversationId,
        Guid memberId,
        int? page = null,
        int pageSize = PrivateMessageLimits.ConversationPageSize,
        CancellationToken cancellationToken = default);

    Task<bool> IsParticipantAsync(
        Guid conversationId,
        Guid memberId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Advances the participant read cursor to <paramref name="lastReadSortKey"/> when that value
    /// is newer than the stored cursor. Uses a conditional database update.
    /// </summary>
    Task MarkConversationReadAsync(
        Guid conversationId,
        Guid memberId,
        long lastReadSortKey,
        DateTimeOffset readAt,
        CancellationToken cancellationToken = default);

    Task<PrivateMessageSendResult> SendNewOrExistingAsync(
        Guid senderMemberId,
        Guid recipientMemberId,
        string body,
        DateTimeOffset sentAt,
        CancellationToken cancellationToken = default);

    Task<PrivateMessageSendResult> ReplyAsync(
        Guid conversationId,
        Guid senderMemberId,
        string body,
        DateTimeOffset sentAt,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Loads one page of the member's archived conversations (mirrors <see cref="GetInboxAsync"/>
    /// but only conversations the member has archived).
    /// </summary>
    Task<PrivateInboxPage> GetArchivedInboxAsync(
        Guid memberId,
        int page = 1,
        int pageSize = PrivateMessageLimits.InboxPageSize,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Archives the conversation for this member only. Returns false when the member is not a
    /// participant in the conversation.
    /// </summary>
    Task<bool> ArchiveConversationAsync(
        Guid conversationId,
        Guid memberId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Unarchives the conversation for this member only. Returns false when the member is not a
    /// participant in the conversation.
    /// </summary>
    Task<bool> UnarchiveConversationAsync(
        Guid conversationId,
        Guid memberId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes the conversation from this member's own inbox. The other participant's copy is
    /// unaffected. A later message from either participant restores visibility for both. Returns
    /// false when the member is not a participant in the conversation.
    /// </summary>
    Task<bool> RemoveConversationAsync(
        Guid conversationId,
        Guid memberId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the other participant in a 1:1 conversation when <paramref name="memberId"/> is a
    /// participant; otherwise null.
    /// </summary>
    Task<Guid?> GetOtherParticipantIdAsync(
        Guid conversationId,
        Guid memberId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// True when a 1:1 conversation already exists between the two members.
    /// </summary>
    Task<bool> HasConversationBetweenAsync(
        Guid memberA,
        Guid memberB,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Counts messages sent by <paramref name="senderMemberId"/> (across any conversation)
    /// since <paramref name="sinceUtc"/>. Used for private-message rate limiting.
    /// </summary>
    Task<int> CountMessagesBySenderSinceAsync(
        Guid senderMemberId,
        DateTimeOffset sinceUtc,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Counts messages sent by <paramref name="senderMemberId"/> with an exact <paramref name="body"/>
    /// match (across any conversation) since <paramref name="sinceUtc"/>. Used for private-message
    /// rate limiting to detect repeated identical sends.
    /// </summary>
    Task<int> CountIdenticalMessagesBySenderSinceAsync(
        Guid senderMemberId,
        string body,
        DateTimeOffset sinceUtc,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Counts distinct conversations <paramref name="senderMemberId"/> has sent a message in,
    /// restricted to conversations that were themselves created since <paramref name="sinceUtc"/>
    /// (i.e. new conversations, not replies in older ones). Used for private-message rate
    /// limiting to detect excessive recipient targeting.
    /// </summary>
    Task<int> CountDistinctNewRecipientsSinceAsync(
        Guid senderMemberId,
        DateTimeOffset sinceUtc,
        CancellationToken cancellationToken = default);

}
