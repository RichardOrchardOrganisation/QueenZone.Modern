namespace QueenZone.Data;

public interface IForumPostReportRepository
{
    Task<ForumReportablePost?> GetVisiblePostAsync(int postId, CancellationToken cancellationToken = default);
    Task<ForumPostReportResult> CreateAsync(Guid reporterMemberId, int postId, string category, string? details, DateTimeOffset createdAt, CancellationToken cancellationToken = default);
    Task<IReadOnlySet<int>> GetReportedPostIdsAsync(Guid reporterMemberId, IReadOnlyCollection<int> postIds, CancellationToken cancellationToken = default);
    Task<ForumPostReport?> GetAsync(Guid reportId, CancellationToken cancellationToken = default);
    Task<ForumPostReportListPage> ListAsync(string? status, int page, int pageSize, CancellationToken cancellationToken = default);
    Task<int> CountOpenAsync(CancellationToken cancellationToken = default);
    Task<ForumPostReport?> UpdateStatusAsync(Guid reportId, string status, string actorEmail, CancellationToken cancellationToken = default);
    Task AppendViewedAuditAsync(Guid reportId, string actorEmail, CancellationToken cancellationToken = default);
}
