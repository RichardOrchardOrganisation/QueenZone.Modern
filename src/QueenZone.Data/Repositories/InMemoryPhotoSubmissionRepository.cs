using QueenZone.Data.Entities;

namespace QueenZone.Data;

public sealed class InMemoryPhotoSubmissionRepository : IPhotoSubmissionRepository
{
    private readonly object sync = new();
    private readonly List<PhotoSubmissionEntity> submissions = [];
    private readonly List<PhotoSubmissionAuditLogEntity> auditLogs = [];
    private readonly Func<Guid, MemberAccount?>? resolveMember;
    private long nextAuditId = 1;

    public InMemoryPhotoSubmissionRepository(Func<Guid, MemberAccount?>? resolveMember = null)
    {
        this.resolveMember = resolveMember;
    }

    public Task<PhotoSubmission> CreateAsync(
        NewPhotoSubmission submission,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(submission);

        lock (sync)
        {
            var entity = new PhotoSubmissionEntity
            {
                Id = submission.Id is { } preferredId && preferredId != Guid.Empty
                    ? preferredId
                    : Guid.NewGuid(),
                SubmitterMemberId = submission.SubmitterMemberId,
                Title = submission.Title.Trim(),
                Description = SubmissionInput.NormalizeOptional(submission.Description, 1000),
                SuggestedCategory = SubmissionInput.NormalizeOptional(submission.SuggestedCategory, 100),
                ApproximateYear = submission.ApproximateYear,
                ApproximateDate = submission.ApproximateDate,
                BlobPath = submission.BlobPath.Trim(),
                WebOptimizedBlobPath = submission.WebOptimizedBlobPath.Trim(),
                ThumbnailBlobPath = submission.ThumbnailBlobPath.Trim(),
                OriginalFileName = submission.OriginalFileName.Trim(),
                FileSizeBytes = submission.FileSizeBytes,
                MimeType = submission.MimeType.Trim(),
                ImageWidthPx = submission.ImageWidthPx,
                ImageHeightPx = submission.ImageHeightPx,
                Status = PhotoSubmissionStatus.Pending,
                SubmittedAt = DateTimeOffset.UtcNow,
            };

            submissions.Add(entity);
            auditLogs.Add(new PhotoSubmissionAuditLogEntity
            {
                Id = nextAuditId++,
                PhotoSubmissionId = entity.Id,
                Action = "Submitted",
                ActorEmail = string.Empty,
                OccurredAt = entity.SubmittedAt,
                Details = "Member submitted photo for review.",
            });

            return Task.FromResult(Map(entity));
        }
    }

    public Task<IReadOnlyList<PhotoSubmissionListItem>> GetPendingAsync(
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        lock (sync)
        {
            IReadOnlyList<PhotoSubmissionListItem> result = submissions
                .Where(row =>
                    row.Status is PhotoSubmissionStatus.Pending
                        or PhotoSubmissionStatus.UnderReview
                        or PhotoSubmissionStatus.NeedsInfo)
                .OrderByDescending(row => row.SubmittedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(row =>
                {
                    var member = resolveMember?.Invoke(row.SubmitterMemberId);
                    return new PhotoSubmissionListItem(
                        row.Id,
                        row.Title,
                        row.SubmitterMemberId,
                        member?.DisplayName ?? "Unknown member",
                        row.SubmittedAt,
                        row.SuggestedCategory,
                        row.Status,
                        row.ThumbnailBlobPath);
                })
                .ToList();

            return Task.FromResult(result);
        }
    }

    public Task<PhotoSubmission?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        lock (sync)
        {
            var entity = submissions.SingleOrDefault(row => row.Id == id);
            return Task.FromResult(entity is null ? null : Map(entity));
        }
    }

    public Task<SubmissionListPage<PhotoSubmission>> GetBySubmitterAsync(
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

            IReadOnlyList<PhotoSubmission> items = owned
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(Map)
                .ToList();

            return Task.FromResult(new SubmissionListPage<PhotoSubmission>(items, owned.Count));
        }
    }

    public Task<PhotoSubmission?> UpdateStatusAsync(
        Guid id,
        string status,
        string? reviewerEmail,
        string? reviewNotes,
        string? rejectionReason,
        string? approvedCategory = null,
        CancellationToken cancellationToken = default)
    {
        lock (sync)
        {
            var entity = submissions.SingleOrDefault(row => row.Id == id);
            if (entity is null)
            {
                return Task.FromResult<PhotoSubmission?>(null);
            }

            if (!PhotoSubmissionWorkflow.TryValidateStatusChange(entity.Status, status, out var error))
            {
                throw new InvalidOperationException(error);
            }

            var next = PhotoSubmissionStatus.Normalize(status);
            entity.Status = next;
            entity.ReviewedAt = DateTimeOffset.UtcNow;
            entity.ReviewerEmail = SubmissionInput.NormalizeOptional(reviewerEmail, 256);
            entity.ReviewNotes = SubmissionInput.NormalizeOptional(reviewNotes, 500);

            if (next == PhotoSubmissionStatus.Rejected)
            {
                entity.RejectionReason = SubmissionInput.NormalizeOptional(rejectionReason, 500)
                    ?? throw new InvalidOperationException("A rejection reason is required.");
            }
            else if (!string.IsNullOrWhiteSpace(rejectionReason))
            {
                entity.RejectionReason = SubmissionInput.NormalizeOptional(rejectionReason, 500);
            }

            if (next == PhotoSubmissionStatus.Approved)
            {
                var category = SubmissionInput.NormalizeOptional(approvedCategory, 100)
                    ?? SubmissionInput.NormalizeOptional(entity.SuggestedCategory, 100);
                entity.ApprovedCategory = category
                    ?? throw new InvalidOperationException("An approved gallery category is required.");
            }
            else if (!string.IsNullOrWhiteSpace(approvedCategory))
            {
                entity.ApprovedCategory = SubmissionInput.NormalizeOptional(approvedCategory, 100);
            }

            auditLogs.Add(new PhotoSubmissionAuditLogEntity
            {
                Id = nextAuditId++,
                PhotoSubmissionId = entity.Id,
                Action = next,
                ActorEmail = entity.ReviewerEmail ?? string.Empty,
                OccurredAt = entity.ReviewedAt.Value,
                Details = next switch
                {
                    PhotoSubmissionStatus.Approved =>
                        $"Approved for category '{entity.ApprovedCategory}'. Notes: {entity.ReviewNotes ?? "(none)"}",
                    PhotoSubmissionStatus.Rejected =>
                        $"Rejected. Reason: {entity.RejectionReason}. Notes: {entity.ReviewNotes ?? "(none)"}",
                    PhotoSubmissionStatus.NeedsInfo =>
                        $"Needs info. Notes: {entity.ReviewNotes ?? "(none)"}",
                    _ => entity.ReviewNotes,
                },
            });

            return Task.FromResult<PhotoSubmission?>(Map(entity));
        }
    }

    public Task<PhotoSubmission?> PromoteAsync(
        Guid id,
        int promotedPicId,
        string approvedCategory,
        string reviewerEmail,
        string? reviewNotes,
        CancellationToken cancellationToken = default)
    {
        lock (sync)
        {
            var entity = submissions.SingleOrDefault(row => row.Id == id);
            if (entity is null)
            {
                return Task.FromResult<PhotoSubmission?>(null);
            }

            if (!PhotoSubmissionWorkflow.TryValidateStatusChange(entity.Status, PhotoSubmissionStatus.Approved, out var error))
            {
                throw new InvalidOperationException(error);
            }

            entity.Status = PhotoSubmissionStatus.Approved;
            entity.ApprovedCategory = SubmissionInput.NormalizeOptional(approvedCategory, 100)
                ?? throw new InvalidOperationException("An approved gallery category is required.");
            entity.PromotedPicId = promotedPicId;
            entity.ReviewedAt = DateTimeOffset.UtcNow;
            entity.ReviewerEmail = SubmissionInput.NormalizeOptional(reviewerEmail, 256);
            entity.ReviewNotes = SubmissionInput.NormalizeOptional(reviewNotes, 500);

            auditLogs.Add(new PhotoSubmissionAuditLogEntity
            {
                Id = nextAuditId++,
                PhotoSubmissionId = entity.Id,
                Action = PhotoSubmissionStatus.Approved,
                ActorEmail = entity.ReviewerEmail ?? string.Empty,
                OccurredAt = entity.ReviewedAt.Value,
                Details = $"Approved for category '{entity.ApprovedCategory}' and published to gallery as photo #{promotedPicId}. Notes: {entity.ReviewNotes ?? "(none)"}",
            });

            return Task.FromResult<PhotoSubmission?>(Map(entity));
        }
    }

    public Task<SubmissionTypeCounts> GetDashboardCountsAsync(
        DateTimeOffset utcNow,
        CancellationToken cancellationToken = default)
    {
        var today = utcNow.UtcDateTime.Date;
        var weekAgo = today.AddDays(-6);
        var monthAgo = utcNow.AddDays(-30);

        lock (sync)
        {
            var pending = submissions.Count(r =>
                r.Status is PhotoSubmissionStatus.Pending
                    or PhotoSubmissionStatus.UnderReview
                    or PhotoSubmissionStatus.NeedsInfo);

            var receivedToday = submissions.Count(r => r.SubmittedAt.UtcDateTime.Date >= today);
            var receivedThisWeek = submissions.Count(r => r.SubmittedAt.UtcDateTime.Date >= weekAgo);

            var last30 = submissions.Where(r => r.SubmittedAt >= monthAgo).ToList();
            var approvedLast30 = last30.Count(r => r.Status == PhotoSubmissionStatus.Approved);
            var rejectedLast30 = last30.Count(r => r.Status == PhotoSubmissionStatus.Rejected);
            var pendingLast30 = last30.Count(r =>
                r.Status is PhotoSubmissionStatus.Pending
                    or PhotoSubmissionStatus.UnderReview
                    or PhotoSubmissionStatus.NeedsInfo);

            return Task.FromResult(new SubmissionTypeCounts(
                pending, receivedToday, receivedThisWeek, approvedLast30, rejectedLast30, pendingLast30));
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
                .Where(r => r.SubmittedAt >= monthStart)
                .GroupBy(r => r.SubmitterMemberId)
                .Select(g =>
                {
                    var member = resolveMember?.Invoke(g.Key);
                    return new SubmissionContributor(g.Key, member?.DisplayName ?? "Unknown member", g.Count());
                })
                .OrderByDescending(c => c.Count)
                .Take(maxCount)
                .ToList();

            return Task.FromResult(result);
        }
    }

    /// <summary>Test helper: audit entries written for a submission.</summary>
    public IReadOnlyList<PhotoSubmissionAuditLogEntity> GetAuditLogs(Guid submissionId)
    {
        lock (sync)
        {
            return auditLogs.Where(log => log.PhotoSubmissionId == submissionId).ToList();
        }
    }

    private PhotoSubmission Map(PhotoSubmissionEntity entity)
    {
        var member = resolveMember?.Invoke(entity.SubmitterMemberId);
        return new PhotoSubmission(
            entity.Id,
            entity.SubmitterMemberId,
            entity.Title,
            entity.Description,
            entity.SuggestedCategory,
            entity.ApprovedCategory,
            entity.ApproximateYear,
            entity.ApproximateDate,
            entity.BlobPath,
            entity.WebOptimizedBlobPath,
            entity.ThumbnailBlobPath,
            entity.OriginalFileName,
            entity.FileSizeBytes,
            entity.MimeType,
            entity.ImageWidthPx,
            entity.ImageHeightPx,
            entity.Status,
            entity.SubmittedAt,
            entity.ReviewedAt,
            entity.ReviewerEmail,
            entity.ReviewNotes,
            entity.RejectionReason,
            entity.PromotedPicId,
            member?.DisplayName,
            member?.Email);
    }
}
