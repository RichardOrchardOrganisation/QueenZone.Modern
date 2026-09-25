using Microsoft.EntityFrameworkCore;
using QueenZone.Data.Entities;

namespace QueenZone.Data;

public sealed class EfFanPerformanceSubmissionRepository(QueenZoneDbContext dbContext)
    : IFanPerformanceSubmissionRepository
{
    public async Task<FanPerformanceSubmission> CreateAsync(
        NewFanPerformanceSubmission submission,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(submission);

        var entity = new FanPerformanceSubmissionEntity
        {
            Id = submission.Id is { } preferredId && preferredId != Guid.Empty
                ? preferredId
                : Guid.NewGuid(),
            SubmitterMemberId = submission.SubmitterMemberId,
            Title = submission.Title.Trim(),
            CoveredSong = submission.CoveredSong.Trim(),
            PerformedBy = submission.PerformedBy.Trim(),
            Description = NormalizeOptional(submission.Description, 2000),
            BlobPath = submission.BlobPath.Trim(),
            OriginalFileName = submission.OriginalFileName.Trim(),
            FileSizeBytes = submission.FileSizeBytes,
            MimeType = submission.MimeType.Trim(),
            DurationSeconds = submission.DurationSeconds,
            Status = FanPerformanceSubmissionStatus.Pending,
            SubmittedAt = DateTimeOffset.UtcNow,
            RightsDeclaredAt = submission.RightsDeclaredAt,
            RightsDeclarationVersion = submission.RightsDeclarationVersion.Trim(),
        };

        entity.AuditLogs.Add(new FanPerformanceSubmissionAuditLogEntity
        {
            FanPerformanceSubmissionId = entity.Id,
            Action = "Submitted",
            ActorEmail = string.Empty,
            OccurredAt = entity.SubmittedAt,
            Details = "Member submitted a fan performance for review.",
        });

        dbContext.FanPerformanceSubmissions.Add(entity);
        await dbContext.SaveChangesAsync(cancellationToken);
        return Map(entity);
    }

    public async Task<FanPerformanceSubmission?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var entity = await dbContext.FanPerformanceSubmissions
            .AsNoTracking()
            .Include(row => row.Submitter)
            .SingleOrDefaultAsync(row => row.Id == id, cancellationToken);

        return entity is null ? null : Map(entity);
    }

    public async Task<IReadOnlyList<FanPerformanceSubmissionListItem>> GetPendingAsync(
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var skip = (page - 1) * pageSize;

        if (dbContext.Database.IsSqliteProvider())
        {
            var rows = await dbContext.FanPerformanceSubmissions
                .AsNoTracking()
                .Where(row =>
                    row.Status == FanPerformanceSubmissionStatus.Pending
                    || row.Status == FanPerformanceSubmissionStatus.UnderReview
                    || row.Status == FanPerformanceSubmissionStatus.NeedsInfo)
                .Select(row => new
                {
                    row.Id,
                    row.Title,
                    row.CoveredSong,
                    row.PerformedBy,
                    row.SubmitterMemberId,
                    DisplayName = row.Submitter != null ? row.Submitter.DisplayName : string.Empty,
                    row.SubmittedAt,
                    row.DurationSeconds,
                    row.FileSizeBytes,
                    row.Status,
                })
                .ToListAsync(cancellationToken);

            return rows
                .OrderByDescending(row => row.SubmittedAt)
                .ThenBy(row => row.Id)
                .Skip(skip)
                .Take(pageSize)
                .Select(row => new FanPerformanceSubmissionListItem(
                    row.Id,
                    row.Title,
                    row.CoveredSong,
                    row.PerformedBy,
                    row.SubmitterMemberId,
                    string.IsNullOrWhiteSpace(row.DisplayName) ? "Unknown member" : row.DisplayName,
                    row.SubmittedAt,
                    row.DurationSeconds,
                    row.FileSizeBytes,
                    row.Status))
                .ToList();
        }

        return await PendingQueueQuery(skip, pageSize).ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<FanPerformanceSubmissionAuditEntry>> GetAuditLogsAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var query = dbContext.FanPerformanceSubmissionAuditLogs
            .AsNoTracking()
            .Where(log => log.FanPerformanceSubmissionId == id);

        if (dbContext.Database.IsSqliteProvider())
        {
            var sqliteRows = await query
                .Select(log => new FanPerformanceSubmissionAuditEntry(
                    log.Id,
                    log.Action,
                    log.ActorEmail,
                    log.OccurredAt,
                    log.Details))
                .ToListAsync(cancellationToken);

            return sqliteRows
                .OrderByDescending(log => log.OccurredAt)
                .ThenByDescending(log => log.Id)
                .ToList();
        }

        return await query
            .OrderByDescending(log => log.OccurredAt)
            .ThenByDescending(log => log.Id)
            .Select(log => new FanPerformanceSubmissionAuditEntry(
                log.Id,
                log.Action,
                log.ActorEmail,
                log.OccurredAt,
                log.Details))
            .ToListAsync(cancellationToken);
    }

    public async Task<SubmissionListPage<FanPerformanceSubmission>> GetBySubmitterAsync(
        Guid submitterMemberId,
        int page = 1,
        int pageSize = 10,
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var query = dbContext.FanPerformanceSubmissions
            .AsNoTracking()
            .Where(row => row.SubmitterMemberId == submitterMemberId);

        var totalCount = await query.CountAsync(cancellationToken);
        var skip = (page - 1) * pageSize;

        if (dbContext.Database.IsSqliteProvider())
        {
            var sqliteRows = await query
                .Select(row => new
                {
                    row.Id,
                    row.SubmitterMemberId,
                    row.Title,
                    row.CoveredSong,
                    row.PerformedBy,
                    row.Description,
                    row.BlobPath,
                    row.OriginalFileName,
                    row.FileSizeBytes,
                    row.MimeType,
                    row.DurationSeconds,
                    row.Status,
                    row.SubmittedAt,
                    row.ReviewedAt,
                    row.ReviewerEmail,
                    row.ReviewNotes,
                    row.RejectionReason,
                    row.RightsDeclaredAt,
                    row.RightsDeclarationVersion,
                    row.PromotedStageId,
                    DisplayName = row.Submitter != null ? row.Submitter.DisplayName : null,
                    Email = row.Submitter != null ? row.Submitter.Email : null,
                })
                .ToListAsync(cancellationToken);

            var sqliteItems = sqliteRows
                .OrderByDescending(row => row.SubmittedAt)
                .ThenBy(row => row.Id)
                .Skip(skip)
                .Take(pageSize)
                .Select(row => new FanPerformanceSubmission(
                    row.Id,
                    row.SubmitterMemberId,
                    row.Title,
                    row.CoveredSong,
                    row.PerformedBy,
                    row.Description,
                    row.BlobPath,
                    row.OriginalFileName,
                    row.FileSizeBytes,
                    row.MimeType,
                    row.DurationSeconds,
                    row.Status,
                    row.SubmittedAt,
                    row.ReviewedAt,
                    row.ReviewerEmail,
                    row.ReviewNotes,
                    row.RejectionReason,
                    row.RightsDeclaredAt,
                    row.RightsDeclarationVersion,
                    row.PromotedStageId,
                    row.DisplayName,
                    row.Email))
                .ToList();
            return new SubmissionListPage<FanPerformanceSubmission>(sqliteItems, totalCount);
        }

        var items = await MemberQueueQuery(submitterMemberId, skip, pageSize).ToListAsync(cancellationToken);
        return new SubmissionListPage<FanPerformanceSubmission>(items, totalCount);
    }

    public async Task<FanPerformanceSubmission?> UpdateStatusAsync(
        Guid id,
        string status,
        string? actorEmail,
        string? reviewNotes,
        string? rejectionReason,
        string? auditDetails = null,
        CancellationToken cancellationToken = default)
    {
        var entity = await dbContext.FanPerformanceSubmissions
            .SingleOrDefaultAsync(row => row.Id == id, cancellationToken);
        if (entity is null)
        {
            return null;
        }

        if (!FanPerformanceSubmissionWorkflow.TryValidateStatusChange(entity.Status, status, out var error))
        {
            throw new InvalidOperationException(error);
        }

        var next = FanPerformanceSubmissionStatus.Normalize(status);
        var normalizedRejection = NormalizeOptional(rejectionReason, 500);
        if (next == FanPerformanceSubmissionStatus.Rejected && normalizedRejection is null)
        {
            throw new InvalidOperationException("A rejection reason is required.");
        }

        if (next == FanPerformanceSubmissionStatus.NeedsInfo && NormalizeOptional(reviewNotes, 500) is null)
        {
            throw new InvalidOperationException("Review notes are required when requesting more information.");
        }

        entity.Status = next;
        entity.ReviewedAt = DateTimeOffset.UtcNow;
        if (!string.IsNullOrWhiteSpace(actorEmail))
        {
            entity.ReviewerEmail = NormalizeOptional(actorEmail, 256);
        }

        if (reviewNotes is not null)
        {
            entity.ReviewNotes = NormalizeOptional(reviewNotes, 500);
        }

        if (next == FanPerformanceSubmissionStatus.Rejected)
        {
            entity.RejectionReason = normalizedRejection;
        }
        else if (normalizedRejection is not null)
        {
            entity.RejectionReason = normalizedRejection;
        }

        dbContext.FanPerformanceSubmissionAuditLogs.Add(new FanPerformanceSubmissionAuditLogEntity
        {
            FanPerformanceSubmissionId = entity.Id,
            Action = next,
            ActorEmail = entity.ReviewerEmail ?? string.Empty,
            OccurredAt = entity.ReviewedAt.Value,
            Details = auditDetails ?? BuildAuditDetails(next, entity),
        });

        await dbContext.SaveChangesAsync(cancellationToken);
        return Map(entity);
    }

    public async Task<FanPerformanceSubmission?> UpdateReviewMetadataAsync(
        Guid id,
        FanPerformanceReviewEdits edits,
        string editorEmail,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(edits);

        var entity = await dbContext.FanPerformanceSubmissions
            .SingleOrDefaultAsync(row => row.Id == id, cancellationToken);
        if (entity is null)
        {
            return null;
        }

        InMemoryFanPerformanceSubmissionRepository.ApplyReviewEdits(entity, edits);
        dbContext.FanPerformanceSubmissionAuditLogs.Add(new FanPerformanceSubmissionAuditLogEntity
        {
            FanPerformanceSubmissionId = entity.Id,
            Action = "Edited",
            ActorEmail = NormalizeOptional(editorEmail, 256) ?? string.Empty,
            OccurredAt = DateTimeOffset.UtcNow,
            Details = "Updated title, performer, or description before publish.",
        });

        await dbContext.SaveChangesAsync(cancellationToken);
        return Map(entity);
    }

    public async Task<FanPerformanceSubmission?> PromoteAsync(
        Guid id,
        int promotedStageId,
        string reviewerEmail,
        string? reviewNotes,
        CancellationToken cancellationToken = default)
    {
        var entity = await dbContext.FanPerformanceSubmissions
            .SingleOrDefaultAsync(row => row.Id == id, cancellationToken);
        if (entity is null)
        {
            return null;
        }

        if (!FanPerformanceSubmissionWorkflow.TryValidateStatusChange(
                entity.Status,
                FanPerformanceSubmissionStatus.Approved,
                out var error))
        {
            throw new InvalidOperationException(error);
        }

        entity.Status = FanPerformanceSubmissionStatus.Approved;
        entity.PromotedStageId = promotedStageId;
        entity.ReviewedAt = DateTimeOffset.UtcNow;
        entity.ReviewerEmail = NormalizeOptional(reviewerEmail, 256);
        entity.ReviewNotes = NormalizeOptional(reviewNotes, 500);

        dbContext.FanPerformanceSubmissionAuditLogs.Add(new FanPerformanceSubmissionAuditLogEntity
        {
            FanPerformanceSubmissionId = entity.Id,
            Action = FanPerformanceSubmissionStatus.Approved,
            ActorEmail = entity.ReviewerEmail ?? string.Empty,
            OccurredAt = entity.ReviewedAt.Value,
            Details = $"Approved and published as fan performance #{promotedStageId}. Notes: {entity.ReviewNotes ?? "(none)"}",
        });

        await dbContext.SaveChangesAsync(cancellationToken);
        return Map(entity);
    }

    public Task<FanPerformanceDashboardCounts> GetDashboardCountsAsync(
        DateTimeOffset utcNow,
        int staleAfterDays = FanPerformanceDashboardCounts.DefaultStaleAfterDays,
        CancellationToken cancellationToken = default) =>
        dbContext.Database.IsSqliteProvider()
            ? GetDashboardCountsInMemoryAsync(utcNow, staleAfterDays, cancellationToken)
            : GetDashboardCountsViaSqlAggregateAsync(utcNow, staleAfterDays, cancellationToken);

    public Task<IReadOnlyList<SubmissionContributor>> GetTopContributorsThisMonthAsync(
        DateTimeOffset monthStart,
        int maxCount,
        CancellationToken cancellationToken = default) =>
        dbContext.FanPerformanceSubmissions
            .AsNoTracking()
            .Select(row => new SubmissionContributorRow
            {
                MemberId = row.SubmitterMemberId,
                DisplayName = row.Submitter != null ? row.Submitter.DisplayName : null,
                SubmittedAt = row.SubmittedAt,
            })
            .ToTopContributorsAsync(monthStart, maxCount, aggregateInSql: !dbContext.Database.IsSqliteProvider(), cancellationToken);

    public async Task<IReadOnlyList<FanPerformanceSubmission>> GetEligibleForPendingBlobPurgeAsync(
        DateTimeOffset cutoffUtc,
        CancellationToken cancellationToken = default)
    {
        var rows = await dbContext.FanPerformanceSubmissions
            .AsNoTracking()
            .Include(row => row.Submitter)
            .Where(row =>
                (row.Status == FanPerformanceSubmissionStatus.Rejected
                    || row.Status == FanPerformanceSubmissionStatus.Withdrawn)
                && row.BlobPath != string.Empty)
            .ToListAsync(cancellationToken);

        return rows
            .Where(row =>
                !string.IsNullOrWhiteSpace(row.BlobPath)
                && (row.ReviewedAt ?? row.SubmittedAt) <= cutoffUtc)
            .Select(Map)
            .ToList();
    }

    public async Task ClearPendingBlobPathAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var entity = await dbContext.FanPerformanceSubmissions
            .SingleOrDefaultAsync(row => row.Id == id, cancellationToken);
        if (entity is null)
        {
            return;
        }

        entity.BlobPath = string.Empty;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyDictionary<int, FanPerformanceContributorCredit>> GetApprovedContributorCreditsAsync(
        IReadOnlyCollection<int> stageIds,
        CancellationToken cancellationToken = default)
    {
        if (stageIds is not { Count: > 0 })
        {
            return new Dictionary<int, FanPerformanceContributorCredit>();
        }

        var ids = stageIds.Distinct().ToArray();
        var rows = await dbContext.FanPerformanceSubmissions
            .AsNoTracking()
            .Where(row =>
                row.Status == FanPerformanceSubmissionStatus.Approved
                && row.PromotedStageId != null
                && ids.Contains(row.PromotedStageId.Value))
            .Select(row => new
            {
                StageId = row.PromotedStageId!.Value,
                row.SubmitterMemberId,
                DisplayName = row.Submitter != null ? row.Submitter.DisplayName : null,
                row.SubmittedAt,
            })
            .ToListAsync(cancellationToken);

        return rows
            .GroupBy(row => row.StageId)
            .ToDictionary(
                group => group.Key,
                group =>
                {
                    var newest = group.OrderByDescending(row => row.SubmittedAt).First();
                    var displayName = string.IsNullOrWhiteSpace(newest.DisplayName)
                        ? "Member"
                        : newest.DisplayName.Trim();
                    return new FanPerformanceContributorCredit(newest.SubmitterMemberId, displayName);
                });
    }

    internal IQueryable<FanPerformanceSubmissionListItem> PendingQueueQuery(int skip, int take) =>
        dbContext.FanPerformanceSubmissions
            .AsNoTracking()
            .Where(row =>
                row.Status == FanPerformanceSubmissionStatus.Pending
                || row.Status == FanPerformanceSubmissionStatus.UnderReview
                || row.Status == FanPerformanceSubmissionStatus.NeedsInfo)
            .OrderByDescending(row => row.SubmittedAt)
            .ThenBy(row => row.Id)
            .Skip(skip)
            .Take(take)
            .Select(row => new FanPerformanceSubmissionListItem(
                row.Id,
                row.Title,
                row.CoveredSong,
                row.PerformedBy,
                row.SubmitterMemberId,
                row.Submitter != null ? row.Submitter.DisplayName : "Unknown member",
                row.SubmittedAt,
                row.DurationSeconds,
                row.FileSizeBytes,
                row.Status));

    internal IQueryable<FanPerformanceSubmission> MemberQueueQuery(Guid submitterMemberId, int skip, int take) =>
        dbContext.FanPerformanceSubmissions
            .AsNoTracking()
            .Where(row => row.SubmitterMemberId == submitterMemberId)
            .OrderByDescending(row => row.SubmittedAt)
            .ThenBy(row => row.Id)
            .Skip(skip)
            .Take(take)
            .Select(row => new FanPerformanceSubmission(
                row.Id,
                row.SubmitterMemberId,
                row.Title,
                row.CoveredSong,
                row.PerformedBy,
                row.Description,
                row.BlobPath,
                row.OriginalFileName,
                row.FileSizeBytes,
                row.MimeType,
                row.DurationSeconds,
                row.Status,
                row.SubmittedAt,
                row.ReviewedAt,
                row.ReviewerEmail,
                row.ReviewNotes,
                row.RejectionReason,
                row.RightsDeclaredAt,
                row.RightsDeclarationVersion,
                row.PromotedStageId,
                row.Submitter != null ? row.Submitter.DisplayName : null,
                row.Submitter != null ? row.Submitter.Email : null));

    private async Task<FanPerformanceDashboardCounts> GetDashboardCountsInMemoryAsync(
        DateTimeOffset utcNow,
        int staleAfterDays,
        CancellationToken cancellationToken)
    {
        var rows = await dbContext.FanPerformanceSubmissions
            .AsNoTracking()
            .Select(row => new { row.Status, row.SubmittedAt })
            .ToListAsync(cancellationToken);

        return FanPerformanceDashboardCountCalculator.FromRows(
            rows.Select(row => (row.Status, row.SubmittedAt)).ToList(),
            utcNow,
            staleAfterDays);
    }

    private async Task<FanPerformanceDashboardCounts> GetDashboardCountsViaSqlAggregateAsync(
        DateTimeOffset utcNow,
        int staleAfterDays,
        CancellationToken cancellationToken)
    {
        staleAfterDays = FanPerformanceDashboardCountCalculator.NormalizeStaleAfterDays(staleAfterDays);
        var monthAgo = utcNow.AddDays(-30);
        var todayUtc = new DateTimeOffset(utcNow.UtcDateTime.Date, TimeSpan.Zero);
        var weekAgoUtc = todayUtc.AddDays(-6);
        var staleCutoff = utcNow.AddDays(-staleAfterDays);

        var counts = await dbContext.FanPerformanceSubmissions
            .AsNoTracking()
            .GroupBy(_ => 1)
            .Select(group => new
            {
                Pending = group.Count(row => row.Status == FanPerformanceSubmissionStatus.Pending
                    || row.Status == FanPerformanceSubmissionStatus.UnderReview
                    || row.Status == FanPerformanceSubmissionStatus.NeedsInfo),
                ReceivedToday = group.Count(row => row.SubmittedAt >= todayUtc),
                ReceivedThisWeek = group.Count(row => row.SubmittedAt >= weekAgoUtc),
                ApprovedLast30Days = group.Count(row =>
                    row.SubmittedAt >= monthAgo && row.Status == FanPerformanceSubmissionStatus.Approved),
                RejectedLast30Days = group.Count(row =>
                    row.SubmittedAt >= monthAgo && row.Status == FanPerformanceSubmissionStatus.Rejected),
                StillPendingFromLast30Days = group.Count(row =>
                    row.SubmittedAt >= monthAgo
                    && (row.Status == FanPerformanceSubmissionStatus.Pending
                        || row.Status == FanPerformanceSubmissionStatus.UnderReview
                        || row.Status == FanPerformanceSubmissionStatus.NeedsInfo)),
                StalePendingCount = group.Count(row =>
                    (row.Status == FanPerformanceSubmissionStatus.Pending
                        || row.Status == FanPerformanceSubmissionStatus.UnderReview
                        || row.Status == FanPerformanceSubmissionStatus.NeedsInfo)
                    && row.SubmittedAt <= staleCutoff),
                OldestOpenSubmittedAt = group
                    .Where(row => row.Status == FanPerformanceSubmissionStatus.Pending
                        || row.Status == FanPerformanceSubmissionStatus.UnderReview
                        || row.Status == FanPerformanceSubmissionStatus.NeedsInfo)
                    .Select(row => (DateTimeOffset?)row.SubmittedAt)
                    .Min(),
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (counts is null)
        {
            return FanPerformanceDashboardCounts.Empty;
        }

        return new FanPerformanceDashboardCounts(
            new SubmissionTypeCounts(
                counts.Pending,
                counts.ReceivedToday,
                counts.ReceivedThisWeek,
                counts.ApprovedLast30Days,
                counts.RejectedLast30Days,
                counts.StillPendingFromLast30Days),
            counts.StalePendingCount,
            FanPerformanceDashboardCountCalculator.ToOldestOpenAgeDays(utcNow, counts.OldestOpenSubmittedAt));
    }

    private static FanPerformanceSubmission Map(FanPerformanceSubmissionEntity entity) =>
        new(
            entity.Id,
            entity.SubmitterMemberId,
            entity.Title,
            entity.CoveredSong,
            entity.PerformedBy,
            entity.Description,
            entity.BlobPath,
            entity.OriginalFileName,
            entity.FileSizeBytes,
            entity.MimeType,
            entity.DurationSeconds,
            entity.Status,
            entity.SubmittedAt,
            entity.ReviewedAt,
            entity.ReviewerEmail,
            entity.ReviewNotes,
            entity.RejectionReason,
            entity.RightsDeclaredAt,
            entity.RightsDeclarationVersion,
            entity.PromotedStageId,
            entity.Submitter?.DisplayName,
            entity.Submitter?.Email);

    private static string? BuildAuditDetails(string status, FanPerformanceSubmissionEntity entity) =>
        status switch
        {
            FanPerformanceSubmissionStatus.Approved =>
                $"Approved. Notes: {entity.ReviewNotes ?? "(none)"}",
            FanPerformanceSubmissionStatus.Rejected =>
                $"Rejected. Reason: {entity.RejectionReason}. Notes: {entity.ReviewNotes ?? "(none)"}",
            FanPerformanceSubmissionStatus.NeedsInfo =>
                $"Needs info. Notes: {entity.ReviewNotes ?? "(none)"}",
            FanPerformanceSubmissionStatus.Withdrawn =>
                "Member withdrew the submission.",
            _ => entity.ReviewNotes,
        };

    private static string? NormalizeOptional(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
    }
}
