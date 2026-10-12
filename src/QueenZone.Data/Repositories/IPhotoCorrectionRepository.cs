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

    /// <summary>
    /// Open (pending or needs-info) proposals, oldest first so nothing waits forever. Corrections and
    /// fan memories are moderated separately: <paramref name="memories"/> picks which queue.
    /// </summary>
    Task<IReadOnlyList<PhotoCorrectionListItem>> GetOpenAsync(
        int page,
        int pageSize,
        bool memories = false,
        CancellationToken cancellationToken = default);

    /// <summary>Every proposal for one photo, newest first, so editors can compare competing claims.</summary>
    Task<IReadOnlyList<PhotoCorrectionProposal>> GetForPhotoAsync(
        int picId,
        CancellationToken cancellationToken = default);

    /// <summary>A member's own proposals, newest first, for the My submissions page.</summary>
    Task<SubmissionListPage<PhotoCorrectionProposal>> GetBySubmitterAsync(
        Guid submitterMemberId,
        int page = 1,
        int pageSize = 10,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Latest approved event-date and photographer/source value for a photo (captions are applied
    /// to the photo itself). Reverted, rejected and open proposals never appear.
    /// </summary>
    Task<IReadOnlyList<PublicPhotoCorrection>> GetApprovedPublicAsync(
        int picId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Approved fan memories for a photo, newest first (at most <see cref="PhotoCorrectionValidation.MaxPublicMemories"/>).
    /// Shown as labelled recollections, never as established fact.
    /// </summary>
    Task<IReadOnlyList<PublicPhotoCorrection>> GetApprovedMemoriesAsync(
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

    /// <summary>
    /// The submitter answers an editor's request for more information; the proposal returns to
    /// pending. Returns null when the proposal does not exist or belongs to someone else.
    /// </summary>
    Task<PhotoCorrectionProposal?> RespondAsync(
        Guid id,
        Guid submitterMemberId,
        string reply,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// The submitter withdraws an open proposal. Returns null when it does not exist or belongs
    /// to someone else; throws when it is no longer open.
    /// </summary>
    Task<PhotoCorrectionProposal?> WithdrawAsync(
        Guid id,
        Guid submitterMemberId,
        CancellationToken cancellationToken = default);

    /// <summary>Marks an approved proposal as reverted after the caller restored the old value.</summary>
    Task<PhotoCorrectionProposal?> RevertAsync(
        Guid id,
        string reviewerEmail,
        string? reviewNotes,
        CancellationToken cancellationToken = default);
}
