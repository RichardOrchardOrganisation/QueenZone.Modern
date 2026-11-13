using QueenZone.Data;

namespace QueenZone.Web.Tests;

// The submission twin has no injectable clock. Only its seed publication dates are replaced;
// publication filtering and all public article behavior still run through the real twins.
internal sealed class FixedPublicationSubmissionSeed : IArticleSubmissionRepository
{
    private readonly InMemoryArticleSubmissionRepository inner = new();
    private readonly Dictionary<Guid, DateTimeOffset> dates = [];
    private readonly Guid authorId = Guid.NewGuid();

    internal async Task<Guid> SeedAsync(string title, DateTimeOffset date, string tags, bool published)
    {
        var saved = await inner.UpsertDraftAsync(new(null, authorId, title, "Excerpt", "Full community body", null, tags));
        dates.Add(saved.Id, date);
        if (published)
        {
            await inner.UpdateStatusAsync(saved.Id, ArticleSubmissionStatus.Published, "editor", null, null);
        }
        return saved.Id;
    }

    public async Task<IReadOnlyList<PublishedArticleSubmission>> GetPublishedAsync(CancellationToken ct = default) =>
        (await inner.GetPublishedAsync(ct)).Select(item => item with { PublishedAt = dates[item.Id] }).ToList();

    public Task<ArticleSubmission> UpsertDraftAsync(ArticleSubmissionDraft draft, CancellationToken ct = default) =>
        inner.UpsertDraftAsync(draft, ct);
    public Task<ArticleSubmission?> SubmitForReviewAsync(Guid id, Guid authorMemberId, CancellationToken ct = default) =>
        inner.SubmitForReviewAsync(id, authorMemberId, ct);
    public Task<SubmissionListPage<ArticleSubmission>> GetDraftsForMemberAsync(Guid memberId, int page = 1, int pageSize = 10, CancellationToken ct = default) =>
        inner.GetDraftsForMemberAsync(memberId, page, pageSize, ct);
    public Task<IReadOnlyList<ArticleSubmissionListItem>> GetPendingAsync(int page, int pageSize, CancellationToken ct = default) =>
        inner.GetPendingAsync(page, pageSize, ct);
    public Task<ArticleSubmission?> GetByIdAsync(Guid id, CancellationToken ct = default) => inner.GetByIdAsync(id, ct);
    public Task<ArticleSubmission?> UpdateStatusAsync(Guid id, string status, string? reviewerEmail, string? notes, string? rejectionReason, ArticlePublicationDetails? publication = null, CancellationToken ct = default) =>
        inner.UpdateStatusAsync(id, status, reviewerEmail, notes, rejectionReason, publication, ct);
    public Task<SubmissionTypeCounts> GetDashboardCountsAsync(DateTimeOffset utcNow, CancellationToken ct = default) => inner.GetDashboardCountsAsync(utcNow, ct);
    public Task<IReadOnlyList<SubmissionContributor>> GetTopContributorsThisMonthAsync(DateTimeOffset monthStart, int maxCount, CancellationToken ct = default) =>
        inner.GetTopContributorsThisMonthAsync(monthStart, maxCount, ct);
}
