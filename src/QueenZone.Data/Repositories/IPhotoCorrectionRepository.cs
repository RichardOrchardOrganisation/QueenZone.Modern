namespace QueenZone.Data;

/// <summary>
/// Member proposals about existing archive photographs. Proposals never change the photo
/// themselves: an editor decision (and, for captions, the caller applying it) does.
/// </summary>
public interface IPhotoCorrectionRepository
{
    /// <summary>
    /// Creates a pending proposal. Re-sending the same <see cref="NewPhotoCorrectionProposal.Id"/>
    /// returns the existing row. Throws <see cref="DuplicatePhotoCorrectionException"/> or
    /// <see cref="PhotoCorrectionLimitException"/> when the member already has too much open.
    /// </summary>
    Task<PhotoCorrectionProposal> CreateAsync(
        NewPhotoCorrectionProposal proposal,
        CancellationToken cancellationToken = default);

    Task<PhotoCorrectionProposal?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Open (pending or needs-info) proposals, oldest first so nothing waits forever.</summary>
    Task<IReadOnlyList<PhotoCorrectionListItem>> GetOpenAsync(
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    /// <summary>Every proposal for one photo, newest first, so editors can compare competing claims.</summary>
    Task<IReadOnlyList<PhotoCorrectionProposal>> GetForPhotoAsync(
        int picId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PhotoCorrectionAuditEntry>> GetAuditAsync(
        Guid id,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Approves an open proposal. <paramref name="appliedFromValue"/> is the value that was
    /// replaced when the change was applied to the photo (null when nothing was applied).
    /// </summary>
    Task<PhotoCorrectionProposal?> ApproveAsync(
        Guid id,
        string reviewerEmail,
        string? reviewNotes,
        string? appliedFromValue,
        CancellationToken cancellationToken = default);

    Task<PhotoCorrectionProposal?> RejectAsync(
        Guid id,
        string reviewerEmail,
        string messageToMember,
        string? reviewNotes,
        CancellationToken cancellationToken = default);

    Task<PhotoCorrectionProposal?> RequestInfoAsync(
        Guid id,
        string reviewerEmail,
        string messageToMember,
        string? reviewNotes,
        CancellationToken cancellationToken = default);

    /// <summary>Marks an approved proposal as reverted after the caller restored the old value.</summary>
    Task<PhotoCorrectionProposal?> RevertAsync(
        Guid id,
        string reviewerEmail,
        string? reviewNotes,
        CancellationToken cancellationToken = default);
}
