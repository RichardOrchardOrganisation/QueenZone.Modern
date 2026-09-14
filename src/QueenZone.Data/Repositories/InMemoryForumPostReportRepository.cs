using QueenZone.Data.Entities;

namespace QueenZone.Data;

public sealed class InMemoryForumPostReportRepository(
    IForumWriteRepository forumWriteRepository,
    Func<Guid, MemberAccount?>? resolveMember = null) : IForumPostReportRepository
{
    private readonly List<ForumPostReport> reports = [];
    private readonly object sync = new();

    public async Task<ForumReportablePost?> GetVisiblePostAsync(int postId, CancellationToken cancellationToken = default)
    {
        var editable = await forumWriteRepository.GetPostAsync(postId, cancellationToken);
        if (editable is not null)
        {
            return new ForumReportablePost(editable.PostId, editable.TopicId, editable.TopicSubject,
                editable.Body, editable.AuthorMemberId, editable.AuthorDisplayName, editable.PostedAt);
        }

        foreach (var topicId in new[] { 1001, 1002 })
        {
            var item = SampleForumData.CreateSeedPosts(topicId).SingleOrDefault(post => post.Id == postId);
            if (item is not null)
            {
                var title = SampleForumData.TryGetSeedTopicHeader(topicId)?.Title ?? string.Empty;
                return new ForumReportablePost(item.Id, topicId, title, item.Body, item.AuthorMemberId,
                    item.AuthorUsername, new DateTimeOffset(DateTime.SpecifyKind(item.PostedAt, DateTimeKind.Utc)));
            }
        }

        return null;
    }

    public async Task<ForumPostReportResult> CreateAsync(Guid reporterMemberId, int postId, string category, string? details, DateTimeOffset createdAt, CancellationToken cancellationToken = default)
    {
        lock (sync)
        {
            var existing = reports.SingleOrDefault(item => item.ReporterMemberId == reporterMemberId && item.PostId == postId);
            if (existing is not null)
            {
                return new ForumPostReportResult(true, existing.Id, null, AlreadyReported: true);
            }
        }

        var post = await GetVisiblePostAsync(postId, cancellationToken);
        if (post is null)
        {
            return new ForumPostReportResult(false, null, ForumPostReportText.PostNotFound);
        }

        if (post.AuthorMemberId == reporterMemberId)
        {
            return new ForumPostReportResult(false, null, ForumPostReportText.CannotReportOwn);
        }

        var report = new ForumPostReport(Guid.NewGuid(), post.PostId, post.TopicId, reporterMemberId,
            post.AuthorMemberId, category, details, createdAt, PrivateMessageReportStatus.Open, post.Body,
            post.AuthorDisplayName, post.PostedAt, post.ThreadTitle, []);
        lock (sync)
        {
            var existing = reports.SingleOrDefault(item => item.ReporterMemberId == reporterMemberId && item.PostId == postId);
            if (existing is not null)
            {
                return new ForumPostReportResult(true, existing.Id, null, AlreadyReported: true);
            }
            reports.Add(report);
        }
        return new ForumPostReportResult(true, report.Id, null);
    }

    public Task<IReadOnlySet<int>> GetReportedPostIdsAsync(Guid reporterMemberId, IReadOnlyCollection<int> postIds, CancellationToken cancellationToken = default)
    {
        lock (sync)
        {
            return Task.FromResult<IReadOnlySet<int>>(reports
                .Where(item => item.ReporterMemberId == reporterMemberId && postIds.Contains(item.PostId))
                .Select(item => item.PostId).ToHashSet());
        }
    }

    public Task<ForumPostReport?> GetAsync(Guid reportId, CancellationToken cancellationToken = default)
    {
        lock (sync)
        {
            var report = reports.SingleOrDefault(item => item.Id == reportId);
            if (report is null)
            {
                return Task.FromResult<ForumPostReport?>(null);
            }
            var previous = reports.Count(item => item.Id != reportId && (report.ReportedMemberId is Guid memberId
                ? item.ReportedMemberId == memberId
                : item.AuthorDisplayNameSnapshot == report.AuthorDisplayNameSnapshot));
            return Task.FromResult<ForumPostReport?>(report with { PreviousAuthorReportCount = previous });
        }
    }

    public Task<ForumPostReportListPage> ListAsync(string? status, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        var normalized = string.IsNullOrWhiteSpace(status) || status.Equals("all", StringComparison.OrdinalIgnoreCase)
            ? null : PrivateMessageReportStatus.Normalize(status);
        lock (sync)
        {
            var query = reports.Where(item => normalized is null || item.Status == normalized).OrderByDescending(item => item.CreatedAt).ToList();
            var items = query.Skip((Math.Max(1, page) - 1) * pageSize).Take(pageSize).Select(item =>
                new ForumPostReportListItem(item.Id, item.PostId, item.TopicId, item.ReporterMemberId,
                    resolveMember?.Invoke(item.ReporterMemberId)?.DisplayName ?? "Unknown member", item.ReportedMemberId,
                    item.ReportedMemberId is Guid id ? resolveMember?.Invoke(id)?.DisplayName ?? item.AuthorDisplayNameSnapshot : item.AuthorDisplayNameSnapshot,
                    item.Category, item.Status, item.CreatedAt)).ToList();
            return Task.FromResult(new ForumPostReportListPage(items, query.Count, normalized));
        }
    }

    public Task<int> CountOpenAsync(CancellationToken cancellationToken = default)
    {
        lock (sync)
        {
            return Task.FromResult(reports.Count(item => item.Status == PrivateMessageReportStatus.Open));
        }
    }

    public Task<ForumPostReport?> UpdateStatusAsync(Guid reportId, string status, string actorEmail, CancellationToken cancellationToken = default)
    {
        status = PrivateMessageReportStatus.Normalize(status);
        lock (sync)
        {
            var index = reports.FindIndex(item => item.Id == reportId);
            if (index < 0)
            {
                return Task.FromResult<ForumPostReport?>(null);
            }
            reports[index] = reports[index] with { Status = status };
            return Task.FromResult<ForumPostReport?>(reports[index]);
        }
    }

    public Task AppendViewedAuditAsync(Guid reportId, string actorEmail, CancellationToken cancellationToken = default) => Task.CompletedTask;
}
