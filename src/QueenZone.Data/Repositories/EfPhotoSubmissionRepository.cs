using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using QueenZone.Data.Entities;

namespace QueenZone.Data;

public sealed class EfPhotoSubmissionRepository(QueenZoneDbContext dbContext) : IPhotoSubmissionRepository
{
    private static readonly NewestFirstOrder<PhotoSubmissionEntity> NewestFirst =
        new(row => row.SubmittedAt, row => row.Id);

    private static readonly Expression<Func<PhotoSubmissionEntity, PhotoSubmissionListItem>> ListItemProjection =
        row => new PhotoSubmissionListItem(
            row.Id,
            row.Title,
            row.SubmitterMemberId,
            row.Submitter != null ? row.Submitter.DisplayName : "Unknown member",
            row.SubmittedAt,
            row.SuggestedCategory,
            row.Status,
            row.ThumbnailBlobPath);

    private static readonly Expression<Func<PhotoSubmissionEntity, PhotoSubmission>> SubmissionProjection =
        row => new PhotoSubmission(
            row.Id,
            row.SubmitterMemberId,
            row.Title,
            row.Description,
            row.SuggestedCategory,
            row.ApprovedCategory,
            row.ApproximateYear,
            row.ApproximateDate,
            row.BlobPath,
            row.WebOptimizedBlobPath,
            row.ThumbnailBlobPath,
            row.OriginalFileName,
            row.FileSizeBytes,
            row.MimeType,
            row.ImageWidthPx,
            row.ImageHeightPx,
            row.Status,
            row.SubmittedAt,
            row.ReviewedAt,
            row.ReviewerEmail,
            row.ReviewNotes,
            row.RejectionReason,
            row.PromotedPicId,
            row.Submitter != null ? row.Submitter.DisplayName : null,
            row.Submitter != null ? row.Submitter.Email : null);

    // Map(entity) and the SQL projection must stay identical; compiling the projection keeps one copy.
    private static readonly Func<PhotoSubmissionEntity, PhotoSubmission> MapEntity = SubmissionProjection.Compile();

    public async Task<PhotoSubmission> CreateAsync(
        NewPhotoSubmission submission,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(submission);

        var entity = PhotoSubmissionRecords.NewEntity(submission);

        entity.AuditLogs.Add(new PhotoSubmissionAuditLogEntity
        {
            PhotoSubmissionId = entity.Id,
            Action = "Submitted",
            ActorEmail = string.Empty,
            OccurredAt = entity.SubmittedAt,
            Details = "Member submitted photo for review.",
        });

        dbContext.PhotoSubmissions.Add(entity);
        await dbContext.SaveChangesAsync(cancellationToken);
        return Map(entity);
    }

    public async Task<IReadOnlyList<PhotoSubmissionListItem>> GetPendingAsync(
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var (skip, take) = SubmissionPaging.Normalize(page, pageSize);
        return await PendingQueue().ToNewestFirstPageAsync(
            NewestFirst, ListItemProjection, skip, take, pageInSql: !dbContext.Database.IsSqliteProvider(), cancellationToken);
    }

    public async Task<PhotoSubmission?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var entity = await dbContext.PhotoSubmissions
            .AsNoTracking()
            .Include(row => row.Submitter)
            .SingleOrDefaultAsync(row => row.Id == id, cancellationToken);

        return entity is null ? null : Map(entity);
    }

    public Task<SubmissionListPage<PhotoSubmission>> GetBySubmitterAsync(
        Guid submitterMemberId,
        int page = 1,
        int pageSize = 10,
        CancellationToken cancellationToken = default)
    {
        var (skip, take) = SubmissionPaging.Normalize(page, pageSize);
        return SubmittedBy(submitterMemberId).ToNewestFirstListPageAsync(
            NewestFirst, SubmissionProjection, skip, take, pageInSql: !dbContext.Database.IsSqliteProvider(), cancellationToken);
    }

    public async Task<PhotoSubmission?> UpdateStatusAsync(
        Guid id,
        string status,
        string? reviewerEmail,
        string? reviewNotes,
        string? rejectionReason,
        string? approvedCategory = null,
        CancellationToken cancellationToken = default)
    {
        var entity = await dbContext.PhotoSubmissions
            .SingleOrDefaultAsync(row => row.Id == id, cancellationToken);
        if (entity is null)
        {
            return null;
        }

        var next = PhotoSubmissionRecords.ApplyStatusChange(
            entity, status, reviewerEmail, reviewNotes, rejectionReason, approvedCategory);

        dbContext.PhotoSubmissionAuditLogs.Add(new PhotoSubmissionAuditLogEntity
        {
            PhotoSubmissionId = entity.Id,
            Action = next,
            ActorEmail = entity.ReviewerEmail ?? string.Empty,
            OccurredAt = entity.ReviewedAt.Value,
            Details = BuildAuditDetails(next, entity),
        });

        await dbContext.SaveChangesAsync(cancellationToken);
        return Map(entity);
    }

    public async Task<PhotoSubmission?> PromoteAsync(
        Guid id,
        int promotedPicId,
        string approvedCategory,
        string reviewerEmail,
        string? reviewNotes,
        CancellationToken cancellationToken = default)
    {
        var entity = await dbContext.PhotoSubmissions
            .SingleOrDefaultAsync(row => row.Id == id, cancellationToken);
        if (entity is null)
        {
            return null;
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

        dbContext.PhotoSubmissionAuditLogs.Add(new PhotoSubmissionAuditLogEntity
        {
            PhotoSubmissionId = entity.Id,
            Action = PhotoSubmissionStatus.Approved,
            ActorEmail = entity.ReviewerEmail ?? string.Empty,
            OccurredAt = entity.ReviewedAt.Value,
            Details = $"Approved for category '{entity.ApprovedCategory}' and published to gallery as photo #{promotedPicId}. Notes: {entity.ReviewNotes ?? "(none)"}",
        });

        await dbContext.SaveChangesAsync(cancellationToken);
        return Map(entity);
    }

    public Task<SubmissionTypeCounts> GetDashboardCountsAsync(
        DateTimeOffset utcNow,
        CancellationToken cancellationToken = default) =>
        dbContext.PhotoSubmissions
            .AsNoTracking()
            .Select(row => new SubmissionCountRow
            {
                SubmittedAt = row.SubmittedAt,
                IsOpen = row.Status == PhotoSubmissionStatus.Pending
                    || row.Status == PhotoSubmissionStatus.UnderReview
                    || row.Status == PhotoSubmissionStatus.NeedsInfo,
                IsApproved = row.Status == PhotoSubmissionStatus.Approved,
                IsRejected = row.Status == PhotoSubmissionStatus.Rejected,
                IsStillPending = row.Status == PhotoSubmissionStatus.Pending
                    || row.Status == PhotoSubmissionStatus.UnderReview
                    || row.Status == PhotoSubmissionStatus.NeedsInfo,
            })
            .ToDashboardCountsAsync(utcNow, aggregateInSql: !dbContext.Database.IsSqliteProvider(), cancellationToken);

    public Task<IReadOnlyList<SubmissionContributor>> GetTopContributorsThisMonthAsync(
        DateTimeOffset monthStart,
        int maxCount,
        CancellationToken cancellationToken = default) =>
        dbContext.PhotoSubmissions
            .AsNoTracking()
            .Select(row => new SubmissionContributorRow
            {
                MemberId = row.SubmitterMemberId,
                DisplayName = row.Submitter != null ? row.Submitter.DisplayName : null,
                SubmittedAt = row.SubmittedAt,
            })
            .ToTopContributorsAsync(monthStart, maxCount, aggregateInSql: !dbContext.Database.IsSqliteProvider(), cancellationToken);

    private static string? BuildAuditDetails(string status, PhotoSubmissionEntity entity) =>
        status switch
        {
            PhotoSubmissionStatus.Approved =>
                $"Approved for category '{entity.ApprovedCategory}'. Notes: {entity.ReviewNotes ?? "(none)"}",
            PhotoSubmissionStatus.Rejected =>
                $"Rejected. Reason: {entity.RejectionReason}. Notes: {entity.ReviewNotes ?? "(none)"}",
            PhotoSubmissionStatus.NeedsInfo =>
                $"Needs info. Notes: {entity.ReviewNotes ?? "(none)"}",
            _ => entity.ReviewNotes,
        };

    internal IQueryable<PhotoSubmissionListItem> PendingQueueQuery(int skip, int take) =>
        PendingQueue().NewestFirstPage(NewestFirst, ListItemProjection, skip, take);

    private IQueryable<PhotoSubmissionEntity> PendingQueue() =>
        dbContext.PhotoSubmissions
            .AsNoTracking()
            .Where(row =>
                row.Status == PhotoSubmissionStatus.Pending
                || row.Status == PhotoSubmissionStatus.UnderReview
                || row.Status == PhotoSubmissionStatus.NeedsInfo);

    internal IQueryable<PhotoSubmission> MemberQueueQuery(Guid submitterMemberId, int skip, int take) =>
        SubmittedBy(submitterMemberId).NewestFirstPage(NewestFirst, SubmissionProjection, skip, take);

    private IQueryable<PhotoSubmissionEntity> SubmittedBy(Guid submitterMemberId) =>
        dbContext.PhotoSubmissions
            .AsNoTracking()
            .Where(row => row.SubmitterMemberId == submitterMemberId);

    private static PhotoSubmission Map(PhotoSubmissionEntity entity) => MapEntity(entity);
}
