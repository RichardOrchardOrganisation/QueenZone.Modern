namespace QueenZone.Data;

public interface IArticleSubmissionRepository
{
    /// <summary>Creates or updates a draft. Returns the saved submission.</summary>
    Task<ArticleSubmission> UpsertDraftAsync(ArticleSubmissionDraft draft, CancellationToken ct = default);

    /// <summary>
    /// Transitions Draft → Submitted atomically. Returns null when not found or already submitted.
    /// Throws <see cref="InvalidOperationException"/> when the body is too short.
    /// </summary>
    Task<ArticleSubmission?> SubmitForReviewAsync(Guid id, Guid authorMemberId, CancellationToken ct = default);

    Task<SubmissionListPage<ArticleSubmission>> GetDraftsForMemberAsync(
        Guid memberId,
        int page = 1,
        int pageSize = 10,
        CancellationToken ct = default);

    Task<IReadOnlyList<ArticleSubmissionListItem>> GetPendingAsync(int page, int pageSize, CancellationToken ct = default);

    Task<ArticleSubmission?> GetByIdAsync(Guid id, CancellationToken ct = default);

    Task<ArticleSubmission?> UpdateStatusAsync(
        Guid id,
        string status,
        string? reviewerEmail,
        string? notes,
        string? rejectionReason,
        ArticlePublicationDetails? publication = null,
        CancellationToken ct = default);

    Task<IReadOnlyList<PublishedArticleSubmission>> GetPublishedAsync(CancellationToken ct = default);

    Task<SubmissionTypeCounts> GetDashboardCountsAsync(
        DateTimeOffset utcNow,
        CancellationToken ct = default);

    Task<IReadOnlyList<SubmissionContributor>> GetTopContributorsThisMonthAsync(
        DateTimeOffset monthStart,
        int maxCount,
        CancellationToken ct = default);
}

/// <summary>
/// Editorial fields an admin can adjust while reviewing. <paramref name="CoverImageBlobPath"/>
/// is left unchanged when null and cleared when empty.
/// </summary>
public sealed record ArticlePublicationDetails(string? Slug, string? Excerpt, string? Tags, string? CoverImageBlobPath = null);
