using QueenZone.Data.Entities;

namespace QueenZone.Data;

public sealed class InMemoryFanPerformanceSubmissionRepository : IFanPerformanceSubmissionRepository
{
    private readonly object sync = new();
    private readonly List<FanPerformanceSubmissionEntity> submissions = [];
    private readonly List<FanPerformanceSubmissionAuditLogEntity> auditLogs = [];
    private readonly Func<Guid, MemberAccount?>? resolveMember;
    private long nextAuditId = 1;

    public InMemoryFanPerformanceSubmissionRepository(Func<Guid, MemberAccount?>? resolveMember = null)
    {
        this.resolveMember = resolveMember;
    }

    public Task<FanPerformanceSubmission> CreateAsync(
        NewFanPerformanceSubmission submission,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(submission);

        lock (sync)
        {
            var entity = CreateEntity(submission);
            submissions.Add(entity);
            auditLogs.Add(new FanPerformanceSubmissionAuditLogEntity
            {
                Id = nextAuditId++,
                FanPerformanceSubmissionId = entity.Id,
                Action = "Submitted",
                ActorEmail = string.Empty,
                OccurredAt = entity.SubmittedAt,
                Details = "Member submitted a fan performance for review.",
            });

            return Task.FromResult(Map(entity));
        }
    }

    public Task<FanPerformanceSubmission?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        lock (sync)
        {
            var entity = submissions.SingleOrDefault(row => row.Id == id);
            return Task.FromResult(entity is null ? null : Map(entity));
        }
    }

    public Task<IReadOnlyList<FanPerformanceSubmissionListItem>> GetPendingAsync(
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        lock (sync)
        {
            IReadOnlyList<FanPerformanceSubmissionListItem> result = submissions
                .Where(row => FanPerformanceSubmissionWorkflow.CanAdminAct(row.Status))
                .OrderByDescending(row => row.SubmittedAt)
                .ThenBy(row => row.Id)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(row =>
                {
                    var member = resolveMember?.Invoke(row.SubmitterMemberId);
                    return new FanPerformanceSubmissionListItem(
                        row.Id,
                        row.Title,
                        row.CoveredSong,
                        row.PerformedBy,
                        row.SubmitterMemberId,
                        member?.DisplayName ?? "Unknown member",
                        row.SubmittedAt,
                        row.DurationSeconds,
                        row.FileSizeBytes,
                        row.Status);
                })
                .ToList();

            return Task.FromResult(result);
        }
    }

    public Task<IReadOnlyList<FanPerformanceSubmissionAuditEntry>> GetAuditLogsAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        lock (sync)
        {
            IReadOnlyList<FanPerformanceSubmissionAuditEntry> result = auditLogs
                .Where(log => log.FanPerformanceSubmissionId == id)
                .OrderByDescending(log => log.OccurredAt)
                .ThenByDescending(log => log.Id)
                .Select(log => new FanPerformanceSubmissionAuditEntry(
                    log.Id,
                    log.Action,
                    log.ActorEmail,
                    log.OccurredAt,
                    log.Details))
                .ToList();
            return Task.FromResult(result);
        }
    }

    public Task<SubmissionListPage<FanPerformanceSubmission>> GetBySubmitterAsync(
        Guid submitterMemberId,
        int page = 1,
        int pageSize = 10,
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        lock (sync)
        {
            var owned = submissions
                .Where(row => row.SubmitterMemberId == submitterMemberId)
                .OrderByDescending(row => row.SubmittedAt)
                .ToList();

            IReadOnlyList<FanPerformanceSubmission> items = owned
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(Map)
                .ToList();

            return Task.FromResult(new SubmissionListPage<FanPerformanceSubmission>(items, owned.Count));
        }
    }

    public Task<FanPerformanceSubmission?> UpdateStatusAsync(
        Guid id,
        string status,
        string? actorEmail,
        string? reviewNotes,
        string? rejectionReason,
        string? auditDetails = null,
        CancellationToken cancellationToken = default)
    {
        lock (sync)
        {
            var entity = submissions.SingleOrDefault(row => row.Id == id);
            if (entity is null)
            {
                return Task.FromResult<FanPerformanceSubmission?>(null);
            }

            ApplyStatusChange(entity, status, actorEmail, reviewNotes, rejectionReason, requireNeedsInfoNotes: true);
            auditLogs.Add(new FanPerformanceSubmissionAuditLogEntity
            {
                Id = nextAuditId++,
                FanPerformanceSubmissionId = entity.Id,
                Action = entity.Status,
                ActorEmail = entity.ReviewerEmail ?? string.Empty,
                OccurredAt = entity.ReviewedAt ?? DateTimeOffset.UtcNow,
                Details = auditDetails ?? BuildAuditDetails(entity.Status, entity),
            });

            return Task.FromResult<FanPerformanceSubmission?>(Map(entity));
        }
    }

    public Task<FanPerformanceSubmission?> UpdateReviewMetadataAsync(
        Guid id,
        FanPerformanceReviewEdits edits,
        string editorEmail,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(edits);

        lock (sync)
        {
            var entity = submissions.SingleOrDefault(row => row.Id == id);
            if (entity is null)
            {
                return Task.FromResult<FanPerformanceSubmission?>(null);
            }

            ApplyReviewEdits(entity, edits);
            auditLogs.Add(new FanPerformanceSubmissionAuditLogEntity
            {
                Id = nextAuditId++,
                FanPerformanceSubmissionId = entity.Id,
                Action = "Edited",
                ActorEmail = SubmissionInput.NormalizeOptional(editorEmail, 256) ?? string.Empty,
                OccurredAt = DateTimeOffset.UtcNow,
                Details = "Updated title, performer, or description before publish.",
            });

            return Task.FromResult<FanPerformanceSubmission?>(Map(entity));
        }
    }

    public Task<FanPerformanceSubmission?> PromoteAsync(
        Guid id,
        int promotedStageId,
        string reviewerEmail,
        string? reviewNotes,
        CancellationToken cancellationToken = default)
    {
        lock (sync)
        {
            var entity = submissions.SingleOrDefault(row => row.Id == id);
            if (entity is null)
            {
                return Task.FromResult<FanPerformanceSubmission?>(null);
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
            entity.ReviewerEmail = SubmissionInput.NormalizeOptional(reviewerEmail, 256);
            entity.ReviewNotes = SubmissionInput.NormalizeOptional(reviewNotes, 500);

            auditLogs.Add(new FanPerformanceSubmissionAuditLogEntity
            {
                Id = nextAuditId++,
                FanPerformanceSubmissionId = entity.Id,
                Action = FanPerformanceSubmissionStatus.Approved,
                ActorEmail = entity.ReviewerEmail ?? string.Empty,
                OccurredAt = entity.ReviewedAt.Value,
                Details = $"Approved and published as fan performance #{promotedStageId}. Notes: {entity.ReviewNotes ?? "(none)"}",
            });

            return Task.FromResult<FanPerformanceSubmission?>(Map(entity));
        }
    }

    public Task<FanPerformanceDashboardCounts> GetDashboardCountsAsync(
        DateTimeOffset utcNow,
        int staleAfterDays = FanPerformanceDashboardCounts.DefaultStaleAfterDays,
        CancellationToken cancellationToken = default)
    {
        lock (sync)
        {
            var rows = submissions
                .Select(row => (row.Status, row.SubmittedAt))
                .ToList();
            return Task.FromResult(
                FanPerformanceDashboardCountCalculator.FromRows(rows, utcNow, staleAfterDays));
        }
    }

    public Task<IReadOnlyList<SubmissionContributor>> GetTopContributorsThisMonthAsync(
        DateTimeOffset monthStart,
        int maxCount,
        CancellationToken cancellationToken = default)
    {
        lock (sync)
        {
            IReadOnlyList<SubmissionContributor> result = submissions
                .Where(row => row.SubmittedAt >= monthStart)
                .GroupBy(row => row.SubmitterMemberId)
                .Select(group =>
                {
                    var member = resolveMember?.Invoke(group.Key);
                    return new SubmissionContributor(group.Key, member?.DisplayName ?? "Unknown member", group.Count());
                })
                .OrderByDescending(contributor => contributor.Count)
                .Take(maxCount)
                .ToList();

            return Task.FromResult(result);
        }
    }

    public Task<IReadOnlyList<FanPerformanceSubmission>> GetEligibleForPendingBlobPurgeAsync(
        DateTimeOffset cutoffUtc,
        CancellationToken cancellationToken = default)
    {
        lock (sync)
        {
            IReadOnlyList<FanPerformanceSubmission> result = submissions
                .Where(row =>
                    (row.Status == FanPerformanceSubmissionStatus.Rejected
                        || row.Status == FanPerformanceSubmissionStatus.Withdrawn)
                    && !string.IsNullOrWhiteSpace(row.BlobPath)
                    && (row.ReviewedAt ?? row.SubmittedAt) <= cutoffUtc)
                .Select(Map)
                .ToList();
            return Task.FromResult(result);
        }
    }

    public Task ClearPendingBlobPathAsync(Guid id, CancellationToken cancellationToken = default)
    {
        lock (sync)
        {
            var entity = submissions.SingleOrDefault(row => row.Id == id);
            if (entity is not null)
            {
                entity.BlobPath = string.Empty;
            }

            return Task.CompletedTask;
        }
    }

    public Task<IReadOnlyDictionary<int, FanPerformanceContributorCredit>> GetApprovedContributorCreditsAsync(
        IReadOnlyCollection<int> stageIds,
        CancellationToken cancellationToken = default)
    {
        if (stageIds is not { Count: > 0 })
        {
            return Task.FromResult<IReadOnlyDictionary<int, FanPerformanceContributorCredit>>(
                new Dictionary<int, FanPerformanceContributorCredit>());
        }

        var ids = stageIds.ToHashSet();
        lock (sync)
        {
            IReadOnlyDictionary<int, FanPerformanceContributorCredit> result = submissions
                .Where(row =>
                    row.Status == FanPerformanceSubmissionStatus.Approved
                    && row.PromotedStageId is int stageId
                    && ids.Contains(stageId))
                .GroupBy(row => row.PromotedStageId!.Value)
                .ToDictionary(
                    group => group.Key,
                    group =>
                    {
                        var newest = group.OrderByDescending(row => row.SubmittedAt).First();
                        var member = resolveMember?.Invoke(newest.SubmitterMemberId);
                        var displayName = string.IsNullOrWhiteSpace(member?.DisplayName)
                            ? "Member"
                            : member.DisplayName.Trim();
                        return new FanPerformanceContributorCredit(newest.SubmitterMemberId, displayName);
                    });

            return Task.FromResult(result);
        }
    }

    /// <summary>Test helper: audit entries written for a submission.</summary>
    public IReadOnlyList<FanPerformanceSubmissionAuditLogEntity> GetAuditLogs(Guid submissionId)
    {
        lock (sync)
        {
            return auditLogs.Where(log => log.FanPerformanceSubmissionId == submissionId).ToList();
        }
    }

    /// <summary>Test helper: backdate submitted/reviewed timestamps for purge eligibility.</summary>
    public void SetTimestamps(Guid id, DateTimeOffset submittedAt, DateTimeOffset? reviewedAt)
    {
        lock (sync)
        {
            var entity = submissions.Single(row => row.Id == id);
            entity.SubmittedAt = submittedAt;
            entity.ReviewedAt = reviewedAt;
        }
    }

    /// <summary>Test helper: simulate a partial promote that recorded <c>PromotedStageId</c> without approving.</summary>
    public void ForcePromotedStageId(Guid id, int promotedStageId)
    {
        lock (sync)
        {
            var entity = submissions.Single(row => row.Id == id);
            entity.PromotedStageId = promotedStageId;
        }
    }

    private FanPerformanceSubmission Map(FanPerformanceSubmissionEntity entity)
    {
        var member = resolveMember?.Invoke(entity.SubmitterMemberId);
        return Map(entity, member?.DisplayName, member?.Email);
    }

    internal static FanPerformanceSubmissionEntity CreateEntity(NewFanPerformanceSubmission submission) =>
        new()
        {
            Id = SubmissionInput.IdOrNew(submission.Id),
            SubmitterMemberId = submission.SubmitterMemberId,
            Title = submission.Title.Trim(),
            CoveredSong = submission.CoveredSong.Trim(),
            PerformedBy = submission.PerformedBy.Trim(),
            Description = SubmissionInput.NormalizeOptional(submission.Description, 2000),
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

    internal static void ApplyReviewEdits(FanPerformanceSubmissionEntity entity, FanPerformanceReviewEdits edits)
    {
        if (edits.Title is not null)
        {
            var title = edits.Title.Trim();
            if (title.Length == 0)
            {
                throw new InvalidOperationException("Title is required.");
            }

            entity.Title = title.Length <= 200 ? title : title[..200];
        }

        if (edits.PerformedBy is not null)
        {
            var performedBy = edits.PerformedBy.Trim();
            if (performedBy.Length == 0)
            {
                throw new InvalidOperationException("Performed by is required.");
            }

            entity.PerformedBy = performedBy.Length <= 200 ? performedBy : performedBy[..200];
        }

        if (edits.Description is not null)
        {
            entity.Description = SubmissionInput.NormalizeOptional(edits.Description, 2000);
        }

        if (edits.CoveredSong is not null)
        {
            var covered = edits.CoveredSong.Trim();
            if (covered.Length > 0)
            {
                entity.CoveredSong = covered.Length <= 200 ? covered : covered[..200];
            }
        }
    }

    internal static void ApplyStatusChange(
        FanPerformanceSubmissionEntity entity,
        string status,
        string? actorEmail,
        string? reviewNotes,
        string? rejectionReason,
        bool requireNeedsInfoNotes = false)
    {
        if (!FanPerformanceSubmissionWorkflow.TryValidateStatusChange(entity.Status, status, out var error))
        {
            throw new InvalidOperationException(error);
        }

        var next = FanPerformanceSubmissionStatus.Normalize(status);
        var normalizedRejection = SubmissionInput.NormalizeOptional(rejectionReason, 500);
        if (next == FanPerformanceSubmissionStatus.Rejected && normalizedRejection is null)
        {
            throw new InvalidOperationException("A rejection reason is required.");
        }

        if (requireNeedsInfoNotes
            && next == FanPerformanceSubmissionStatus.NeedsInfo
            && SubmissionInput.NormalizeOptional(reviewNotes, 500) is null)
        {
            throw new InvalidOperationException("Review notes are required when requesting more information.");
        }

        entity.Status = next;
        entity.ReviewedAt = DateTimeOffset.UtcNow;
        if (!string.IsNullOrWhiteSpace(actorEmail))
        {
            entity.ReviewerEmail = SubmissionInput.NormalizeOptional(actorEmail, 256);
        }

        if (reviewNotes is not null)
        {
            entity.ReviewNotes = SubmissionInput.NormalizeOptional(reviewNotes, 500);
        }

        if (next == FanPerformanceSubmissionStatus.Rejected)
        {
            entity.RejectionReason = normalizedRejection;
        }
        else if (normalizedRejection is not null)
        {
            entity.RejectionReason = normalizedRejection;
        }
    }

    internal static string? BuildAuditDetails(string status, FanPerformanceSubmissionEntity entity) =>
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

    internal static FanPerformanceSubmission Map(
        FanPerformanceSubmissionEntity entity,
        string? displayName,
        string? email) =>
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
            displayName,
            email);
}
