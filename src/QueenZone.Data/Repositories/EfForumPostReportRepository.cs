using Microsoft.EntityFrameworkCore;
using QueenZone.Data.Entities;

namespace QueenZone.Data;

public sealed class EfForumPostReportRepository(QueenZoneDbContext dbContext) : IForumPostReportRepository
{
    public async Task<ForumReportablePost?> GetVisiblePostAsync(int postId, CancellationToken cancellationToken = default)
    {
        var row = await VisiblePosts()
            .Where(post => post.LegacyPostId == postId)
            .Select(post => new
            {
                post.LegacyPostId,
                post.LegacyThreadTopicId,
                ThreadTitle = post.Thread!.Title,
                post.BodyHtml,
                post.AuthorLegacyUserId,
                post.AuthorMemberId,
                post.AuthorDisplayName,
                post.PostedAt,
            })
            .SingleOrDefaultAsync(cancellationToken);

        var authorMemberId = row is null
            ? null
            : await ResolveAuthorMemberIdAsync(row.AuthorMemberId, row.AuthorLegacyUserId, cancellationToken);

        return row is null ? null : new ForumReportablePost(
            row.LegacyPostId,
            row.LegacyThreadTopicId,
            row.ThreadTitle,
            row.BodyHtml,
            authorMemberId,
            row.AuthorDisplayName,
            ToOffset(row.PostedAt));
    }

    public async Task<ForumPostReportResult> CreateAsync(
        Guid reporterMemberId,
        int postId,
        string category,
        string? details,
        DateTimeOffset createdAt,
        CancellationToken cancellationToken = default)
    {
        var existing = await dbContext.ForumPostReports
            .AsNoTracking()
            .Where(report => report.ReporterMemberId == reporterMemberId && report.PostId == postId)
            .Select(report => report.Id)
            .SingleOrDefaultAsync(cancellationToken);
        if (existing != Guid.Empty)
        {
            return new ForumPostReportResult(true, existing, null, AlreadyReported: true);
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

        var contextRows = await VisiblePosts()
            .Where(item => item.LegacyThreadTopicId == post.TopicId && item.LegacyPostId < postId)
            .OrderByDescending(item => item.LegacyPostId)
            .Take(ForumPostReportLimits.ContextPostCount)
            .OrderBy(item => item.LegacyPostId)
            .Select(item => new { item.LegacyPostId, item.AuthorDisplayName, item.BodyHtml, item.PostedAt })
            .ToListAsync(cancellationToken);
        var context = contextRows.Select(item => new ForumPostReportContextItem(
            item.LegacyPostId, item.AuthorDisplayName, item.BodyHtml, ToOffset(item.PostedAt))).ToList();

        var entity = new ForumPostReportEntity
        {
            Id = Guid.NewGuid(),
            PostId = post.PostId,
            TopicId = post.TopicId,
            ReporterMemberId = reporterMemberId,
            ReportedMemberId = post.AuthorMemberId,
            Category = category,
            Details = details,
            CreatedAt = createdAt,
            PostBodySnapshot = post.Body,
            AuthorDisplayNameSnapshot = post.AuthorDisplayName,
            PostCreatedAtSnapshot = post.PostedAt,
            ThreadTitleSnapshot = post.ThreadTitle,
            ContextJson = ForumPostReportContextSerializer.Serialize(context),
        };
        dbContext.ForumPostReports.Add(entity);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (EfPrivateMessageRepository.IsUniqueConstraintViolation(ex))
        {
            dbContext.Entry(entity).State = EntityState.Detached;
            existing = await dbContext.ForumPostReports
                .AsNoTracking()
                .Where(report => report.ReporterMemberId == reporterMemberId && report.PostId == postId)
                .Select(report => report.Id)
                .SingleAsync(cancellationToken);
            return new ForumPostReportResult(true, existing, null, AlreadyReported: true);
        }

        return new ForumPostReportResult(true, entity.Id, null);
    }

    public async Task<IReadOnlySet<int>> GetReportedPostIdsAsync(Guid reporterMemberId, IReadOnlyCollection<int> postIds, CancellationToken cancellationToken = default)
    {
        if (postIds.Count == 0)
        {
            return new HashSet<int>();
        }

        return (await dbContext.ForumPostReports.AsNoTracking()
            .Where(report => report.ReporterMemberId == reporterMemberId && postIds.Contains(report.PostId))
            .Select(report => report.PostId)
            .ToListAsync(cancellationToken)).ToHashSet();
    }

    public async Task<ForumPostReport?> GetAsync(Guid reportId, CancellationToken cancellationToken = default)
    {
        var report = await dbContext.ForumPostReports.AsNoTracking().SingleOrDefaultAsync(item => item.Id == reportId, cancellationToken);
        if (report is null)
        {
            return null;
        }

        var reportedMemberId = report.ReportedMemberId;
        if (reportedMemberId is null)
        {
            var author = await dbContext.ModernForumPosts.AsNoTracking()
                .Where(post => post.LegacyPostId == report.PostId)
                .Select(post => new { post.AuthorMemberId, post.AuthorLegacyUserId })
                .SingleOrDefaultAsync(cancellationToken);
            if (author is not null)
            {
                reportedMemberId = await ResolveAuthorMemberIdAsync(
                    author.AuthorMemberId, author.AuthorLegacyUserId, cancellationToken);
            }
        }

        var previous = reportedMemberId is Guid memberId
            ? await dbContext.ForumPostReports.CountAsync(item => item.ReportedMemberId == memberId && item.Id != reportId, cancellationToken)
            : await dbContext.ForumPostReports.CountAsync(item => item.AuthorDisplayNameSnapshot == report.AuthorDisplayNameSnapshot && item.Id != reportId, cancellationToken);
        return Map(report, reportedMemberId, previous);
    }

    public async Task<ForumPostReportListPage> ListAsync(string? status, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var normalized = string.IsNullOrWhiteSpace(status) || status.Equals("all", StringComparison.OrdinalIgnoreCase) ? null : PrivateMessageReportStatus.Normalize(status);
        var query = dbContext.ForumPostReports.AsNoTracking().AsQueryable();
        if (normalized is not null)
        {
            query = query.Where(report => report.Status == normalized);
        }

        if (string.Equals(
            dbContext.Database.ProviderName,
            "Microsoft.EntityFrameworkCore.Sqlite",
            StringComparison.Ordinal))
        {
            var allRows = await ProjectListItems(query).ToListAsync(cancellationToken);
            var ordered = allRows.OrderByDescending(report => report.CreatedAt).ThenBy(report => report.Id).ToList();
            return new ForumPostReportListPage(
                ordered.Skip((page - 1) * pageSize).Take(pageSize).ToList(),
                ordered.Count,
                normalized);
        }

        var total = await query.CountAsync(cancellationToken);
        var rows = query.OrderByDescending(report => report.CreatedAt)
            .ThenBy(report => report.Id)
            .Skip((page - 1) * pageSize).Take(pageSize);
        var items = await ProjectListItems(rows)
            .ToListAsync(cancellationToken);
        return new ForumPostReportListPage(items, total, normalized);
    }

    public Task<int> CountOpenAsync(CancellationToken cancellationToken = default) =>
        dbContext.ForumPostReports.CountAsync(report => report.Status == PrivateMessageReportStatus.Open, cancellationToken);

    public async Task<ForumPostReport?> UpdateStatusAsync(Guid reportId, string status, string actorEmail, CancellationToken cancellationToken = default)
    {
        status = PrivateMessageReportStatus.Normalize(status);
        var report = await dbContext.ForumPostReports.SingleOrDefaultAsync(item => item.Id == reportId, cancellationToken);
        if (report is null)
        {
            return null;
        }

        var previous = report.Status;
        report.Status = status;
        dbContext.ForumPostReportAuditLogs.Add(new ForumPostReportAuditLogEntity
        {
            ReportId = reportId,
            Action = PrivateMessageReportAuditAction.StatusChanged,
            ActorEmail = actorEmail,
            OccurredAt = DateTimeOffset.UtcNow,
            Details = $"{previous} -> {status}",
        });
        await dbContext.SaveChangesAsync(cancellationToken);
        return Map(report);
    }

    public async Task AppendViewedAuditAsync(Guid reportId, string actorEmail, CancellationToken cancellationToken = default)
    {
        dbContext.ForumPostReportAuditLogs.Add(new ForumPostReportAuditLogEntity
        {
            ReportId = reportId,
            Action = PrivateMessageReportAuditAction.Viewed,
            ActorEmail = actorEmail,
            OccurredAt = DateTimeOffset.UtcNow,
        });
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private IQueryable<ModernForumPostEntity> VisiblePosts() => dbContext.ModernForumPosts.AsNoTracking()
        .Where(post => !post.IsHidden && post.Thread != null && !post.Thread.IsHidden
            && post.Thread.IsLegacyTopicStarter && post.Thread.StartedByUserValidated == true
            && post.Thread.Category != null && !post.Thread.Category.IsSynthetic);

    private static IQueryable<ForumPostReportListItem> ProjectListItems(IQueryable<ForumPostReportEntity> query) =>
        query.Select(report => new ForumPostReportListItem(
            report.Id, report.PostId, report.TopicId, report.ReporterMemberId,
            report.Reporter != null ? report.Reporter.DisplayName : "Unknown member",
            report.ReportedMemberId,
            report.Reported != null ? report.Reported.DisplayName : report.AuthorDisplayNameSnapshot,
            report.Category, report.Status, report.CreatedAt));

    private static ForumPostReport Map(ForumPostReportEntity report, int previous = 0) => new(
        report.Id, report.PostId, report.TopicId, report.ReporterMemberId, report.ReportedMemberId,
        report.Category, report.Details, report.CreatedAt, report.Status, report.PostBodySnapshot,
        report.AuthorDisplayNameSnapshot, report.PostCreatedAtSnapshot, report.ThreadTitleSnapshot,
        ForumPostReportContextSerializer.Deserialize(report.ContextJson), previous);

    private static ForumPostReport Map(ForumPostReportEntity report, Guid? reportedMemberId, int previous) => new(
        report.Id, report.PostId, report.TopicId, report.ReporterMemberId, reportedMemberId,
        report.Category, report.Details, report.CreatedAt, report.Status, report.PostBodySnapshot,
        report.AuthorDisplayNameSnapshot, report.PostCreatedAtSnapshot, report.ThreadTitleSnapshot,
        ForumPostReportContextSerializer.Deserialize(report.ContextJson), previous);

    private async Task<Guid?> ResolveAuthorMemberIdAsync(
        Guid? authorMemberId,
        int? authorLegacyUserId,
        CancellationToken cancellationToken)
    {
        if (authorMemberId is not null || authorLegacyUserId is null)
        {
            return authorMemberId;
        }

        return await dbContext.MemberAccounts.AsNoTracking()
            .Where(account => account.LinkedLegacyUserId == authorLegacyUserId)
            .Select(account => (Guid?)account.Id)
            .SingleOrDefaultAsync(cancellationToken);
    }

    private static DateTimeOffset ToOffset(DateTime? value) =>
        new(DateTime.SpecifyKind(value ?? DateTime.MinValue, DateTimeKind.Utc));
}
