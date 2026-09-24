namespace QueenZone.Data;

public interface IPrivateMessageModerationRepository
{
    /// <summary>
    /// True when <paramref name="blockerMemberId"/> has blocked <paramref name="blockedMemberId"/>.
    /// </summary>
    Task<bool> IsBlockedAsync(
        Guid blockerMemberId,
        Guid blockedMemberId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// The subset of <paramref name="candidateMemberIds"/> that <paramref name="blockerMemberId"/>
    /// has blocked. Batch form of <see cref="IsBlockedAsync"/> for callers filtering a list.
    /// </summary>
    Task<IReadOnlySet<Guid>> ListBlockedMemberIdsAsync(
        Guid blockerMemberId,
        IReadOnlyCollection<Guid> candidateMemberIds,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// True when either member has blocked the other for private messaging.
    /// </summary>
    Task<bool> IsMessagingBlockedAsync(
        Guid memberA,
        Guid memberB,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a block from <paramref name="blockerMemberId"/> to <paramref name="blockedMemberId"/>.
    /// Idempotent when the block already exists. Caller must validate self-block and membership.
    /// </summary>
    Task BlockAsync(
        Guid blockerMemberId,
        Guid blockedMemberId,
        DateTimeOffset blockedAt,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes a block. Returns false when no such block existed.
    /// </summary>
    Task<bool> UnblockAsync(
        Guid blockerMemberId,
        Guid blockedMemberId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Records a report for a message in a conversation the reporter participates in.
    /// Snapshots the message body and a little preceding context. Idempotent when the
    /// same reporter already reported the same message. Does not notify the reported member.
    /// </summary>
    Task<PrivateMessageReportResult> CreateReportAsync(
        Guid reporterMemberId,
        Guid conversationId,
        Guid messageId,
        string? reason,
        DateTimeOffset createdAt,
        CancellationToken cancellationToken = default);

    Task<PrivateMessageReport?> GetReportAsync(
        Guid reportId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Message ids in <paramref name="conversationId"/> that <paramref name="reporterMemberId"/>
    /// has already reported. Used to hide the report action on the conversation page.
    /// </summary>
    Task<IReadOnlySet<Guid>> GetReportedMessageIdsAsync(
        Guid conversationId,
        Guid reporterMemberId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Admin moderation queue (issue #470). Pass "all" or null/whitespace for
    /// <paramref name="status"/> to return every status, otherwise a single
    /// <see cref="PrivateMessageReportStatus"/> value. Newest first.
    /// </summary>
    Task<PrivateMessageReportListPage> ListReportsAsync(
        string? status,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    /// <summary>Count of reports in <see cref="PrivateMessageReportStatus.Open"/>, for dashboard tiles.</summary>
    Task<int> CountOpenReportsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Transitions a report's status and records the transition in the report access audit
    /// log (ADR 0015) in the same operation, so a status change can never happen without a
    /// matching audit row. Returns null when no report with <paramref name="reportId"/> exists.
    /// </summary>
    Task<PrivateMessageReport?> UpdateReportStatusAsync(
        Guid reportId,
        string status,
        string actorEmail,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Records that <paramref name="actorEmail"/> viewed a report's snapshotted content
    /// (ADR 0015). Call once per admin review-page load, not from list views that only
    /// show report metadata.
    /// </summary>
    Task AppendReportViewedAuditAsync(
        Guid reportId,
        string actorEmail,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Permanently deletes reports that reached a terminal status (Dismissed/Actioned) at least
    /// <see cref="PrivateMessageLimits.ReportRetentionAfterTerminalStatus"/> before
    /// <paramref name="asOfUtc"/> (ADR 0015 decision 2). "Reached" is the most recent
    /// <c>StatusChanged</c> audit row for the report — a terminal report always has one, since
    /// reports are created Open and only leave Open via <see cref="UpdateReportStatusAsync"/>.
    /// Open/Reviewed reports are never purged. The report's audit log rows are retained
    /// (ADR 0015 decision 3) — only the <see cref="PrivateMessageReportEntity"/> row is removed.
    /// Returns the number of reports purged.
    /// </summary>
    Task<int> PurgeExpiredReportsAsync(DateTimeOffset asOfUtc, CancellationToken cancellationToken = default);
}
