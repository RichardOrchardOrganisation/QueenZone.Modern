namespace QueenZone.Data;

public interface IHelpRequestRepository
{
    Task<HelpRequest> CreateAsync(HelpRequest request, CancellationToken cancellationToken = default);

    Task<HelpRequest?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<HelpRequestListPage> ListAsync(
        string? status,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    Task<HelpRequest?> UpdateStatusAsync(
        Guid id,
        string status,
        string? reviewerEmail,
        string? notes,
        CancellationToken cancellationToken = default);

    /// <summary>Logs a reply sent to the requester; null when the request no longer exists.</summary>
    Task<HelpRequestReply?> AddReplyAsync(HelpRequestReply reply, CancellationToken cancellationToken = default);

    /// <summary>Replies for a request, oldest first.</summary>
    Task<IReadOnlyList<HelpRequestReply>> ListRepliesAsync(
        Guid helpRequestId,
        CancellationToken cancellationToken = default);

    Task<int> CountByEmailSinceAsync(
        string normalizedEmail,
        DateTimeOffset sinceUtc,
        CancellationToken cancellationToken = default);

    Task<int> CountByMemberSinceAsync(
        Guid memberId,
        DateTimeOffset sinceUtc,
        CancellationToken cancellationToken = default);

    Task<int> CountOpenAsync(CancellationToken cancellationToken = default);
}
