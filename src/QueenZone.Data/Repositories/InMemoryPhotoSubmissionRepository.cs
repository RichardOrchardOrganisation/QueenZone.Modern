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
            var entity = PhotoSubmissionRecords.NewEntity(submission);

            submissions.Add(entity);
            auditLogs.Add(SubmissionReview.Copy(
                new PhotoSubmissionAuditLogEntity
                {
                    Id = nextAuditId++,
                    PhotoSubmissionId = entity.Id,
                },
                SubmissionReview.Submitted(entity.SubmittedAt, "Member submitted photo for review.")));

            return Task.FromResult(Map(entity));
        }
    }

    public Task<IReadOnlyList<PhotoSubmissionListItem>> GetPendingAsync(
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var (skip, take) = SubmissionPaging.Normalize(page, pageSize);

        lock (sync)
        {
            IReadOnlyList<PhotoSubmissionListItem> result = submissions
                .Where(row =>
                    row.Status is PhotoSubmissionStatus.Pending
                        or PhotoSubmissionStatus.UnderReview
                        or PhotoSubmissionStatus.NeedsInfo)
                .OrderByDescending(row => row.SubmittedAt)
                .Skip(skip)
                .Take(take)
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
        var (skip, take) = SubmissionPaging.Normalize(page, pageSize);

        lock (sync)
        {
            var owned = submissions
                .Where(row => row.SubmitterMemberId == submitterMemberId)
                .OrderByDescending(row => row.SubmittedAt)
                .ToList();

            IReadOnlyList<PhotoSubmission> items = owned
                .Skip(skip)
                .Take(take)
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

            var next = PhotoSubmissionRecords.ApplyStatusChange(
                entity, status, reviewerEmail, reviewNotes, rejectionReason, approvedCategory);

            auditLogs.Add(SubmissionReview.Copy(
                new PhotoSubmissionAuditLogEntity
                {
                    Id = nextAuditId++,
                    PhotoSubmissionId = entity.Id,
                },
                SubmissionReview.ForStatus(
                    next,
                    entity.ReviewerEmail,
                    entity.ReviewedAt!.Value,
                    PhotoSubmissionRecords.AuditDetails(next, entity))));

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

            SubmissionReview.EnsureTransition(
                entity.Status,
                PhotoSubmissionStatus.Approved,
                PhotoSubmissionWorkflow.TryValidateStatusChange);

            entity.Status = PhotoSubmissionStatus.Approved;
            entity.ApprovedCategory = SubmissionInput.NormalizeOptional(approvedCategory, 100)
                ?? throw new InvalidOperationException("An approved gallery category is required.");
            entity.PromotedPicId = promotedPicId;
            var reviewedAt = SubmissionReview.Stamp(
                entity,
                PhotoSubmissionStatus.Approved,
                reviewerEmail,
                reviewNotes);

            auditLogs.Add(SubmissionReview.Copy(
                new PhotoSubmissionAuditLogEntity
                {
                    Id = nextAuditId++,
                    PhotoSubmissionId = entity.Id,
                },
                SubmissionReview.ForStatus(
                    PhotoSubmissionStatus.Approved,
                    entity.ReviewerEmail,
                    reviewedAt,
                    $"Approved for category '{entity.ApprovedCategory}' and published to gallery as photo #{promotedPicId}. Notes: {entity.ReviewNotes ?? "(none)"}")));

            return Task.FromResult<PhotoSubmission?>(Map(entity));
        }
    }

    public Task<SubmissionTypeCounts> GetDashboardCountsAsync(
        DateTimeOffset utcNow,
        CancellationToken cancellationToken = default)
    {
        lock (sync)
        {
            var rows = submissions.Select(row => new SubmissionCountRow
            {
                SubmittedAt = row.SubmittedAt,
                IsOpen = row.Status is PhotoSubmissionStatus.Pending or PhotoSubmissionStatus.UnderReview or PhotoSubmissionStatus.NeedsInfo,
                IsApproved = row.Status == PhotoSubmissionStatus.Approved,
                IsRejected = row.Status == PhotoSubmissionStatus.Rejected,
                IsStillPending = row.Status is PhotoSubmissionStatus.Pending or PhotoSubmissionStatus.UnderReview or PhotoSubmissionStatus.NeedsInfo,
            });
            return Task.FromResult(SubmissionDashboardQueries.CountRows(rows, utcNow));
        }
    }

    public Task<IReadOnlyList<SubmissionContributor>> GetTopContributorsThisMonthAsync(
        DateTimeOffset monthStart,
        int maxCount,
        CancellationToken cancellationToken = default)
    {
        lock (sync)
        {
            var rows = submissions.Select(row => new SubmissionContributorRow
            {
                MemberId = row.SubmitterMemberId,
                SubmittedAt = row.SubmittedAt,
            });
            return Task.FromResult(SubmissionDashboardQueries.TopContributors(rows, monthStart, maxCount, id => resolveMember?.Invoke(id)?.DisplayName));
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
