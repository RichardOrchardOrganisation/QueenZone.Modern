using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using QueenZone.Data.Entities;

namespace QueenZone.Data;

public sealed class EfTriviaFactSubmissionRepository(QueenZoneDbContext dbContext) : ITriviaFactSubmissionRepository
{
    private static readonly NewestFirstOrder<TriviaFactSubmissionEntity> NewestFirst =
        new(row => row.SubmittedAt, row => row.Id);

    private static readonly Expression<Func<TriviaFactSubmissionEntity, TriviaFactSubmissionListItem>> ListItemProjection =
        row => new TriviaFactSubmissionListItem(
            row.Id,
            row.Text,
            row.Submitter != null ? row.Submitter.DisplayName : "Unknown member",
            row.SubmittedAt,
            row.Category,
            row.Status);

    private static readonly Expression<Func<TriviaFactSubmissionEntity, TriviaFactSubmission>> SubmissionProjection =
        row => new TriviaFactSubmission(
            row.Id,
            row.SubmitterMemberId,
            row.Text,
            row.Category,
            row.Difficulty,
            row.SourceNote,
            row.Status,
            row.SubmittedAt,
            row.ReviewedAt,
            row.ReviewerEmail,
            row.ReviewNotes,
            row.RejectionReason,
            row.PromotedTriviaId,
            row.Submitter != null ? row.Submitter.DisplayName : null,
            row.Submitter != null ? row.Submitter.Email : null);

    // Map(entity) and the SQL projection must stay identical; compiling the projection keeps one copy.
    private static readonly Func<TriviaFactSubmissionEntity, TriviaFactSubmission> MapEntity = SubmissionProjection.Compile();

    public async Task<TriviaFactSubmission> CreateAsync(
        NewTriviaFactSubmission submission,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(submission);

        var entity = new TriviaFactSubmissionEntity
        {
            Id = SubmissionInput.IdOrNew(submission.Id),
            SubmitterMemberId = submission.SubmitterMemberId,
            Text = submission.Text.Trim(),
            Category = SubmissionInput.NormalizeOptional(submission.Category, TriviaValidation.MaxCategoryLength),
            Difficulty = NormalizeDifficulty(submission.Difficulty),
            SourceNote = SubmissionInput.NormalizeOptional(submission.SourceNote, TriviaValidation.MaxSourceNoteLength),
            Status = TriviaFactSubmissionStatus.Pending,
            SubmittedAt = DateTimeOffset.UtcNow,
        };

        entity.AuditLogs.Add(new TriviaFactSubmissionAuditLogEntity
        {
            TriviaFactSubmissionId = entity.Id,
            Action = "Submitted",
            ActorEmail = string.Empty,
            OccurredAt = entity.SubmittedAt,
            Details = "Member submitted a trivia fact for review.",
        });

        dbContext.TriviaFactSubmissions.Add(entity);
        await dbContext.SaveChangesAsync(cancellationToken);
        return Map(entity);
    }

    public async Task<IReadOnlyList<TriviaFactSubmissionListItem>> GetPendingAsync(
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var (skip, take) = SubmissionPaging.Normalize(page, pageSize);
        return await PendingQueue().ToNewestFirstPageAsync(
            NewestFirst, ListItemProjection, skip, take, pageInSql: !dbContext.Database.IsSqliteProvider(), cancellationToken);
    }

    public async Task<TriviaFactSubmission?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var entity = await dbContext.TriviaFactSubmissions
            .AsNoTracking()
            .Include(row => row.Submitter)
            .SingleOrDefaultAsync(row => row.Id == id, cancellationToken);

        return entity is null ? null : Map(entity);
    }

    public Task<SubmissionListPage<TriviaFactSubmission>> GetBySubmitterAsync(
        Guid submitterMemberId,
        int page = 1,
        int pageSize = 10,
        CancellationToken cancellationToken = default)
    {
        var (skip, take) = SubmissionPaging.Normalize(page, pageSize);
        return SubmittedBy(submitterMemberId).ToNewestFirstListPageAsync(
            NewestFirst, SubmissionProjection, skip, take, pageInSql: !dbContext.Database.IsSqliteProvider(), cancellationToken);
    }

    public async Task<TriviaFactSubmission?> ApproveAsync(
        Guid id,
        int promotedTriviaId,
        string reviewerEmail,
        string? reviewNotes,
        CancellationToken cancellationToken = default)
    {
        var entity = await dbContext.TriviaFactSubmissions
            .SingleOrDefaultAsync(row => row.Id == id, cancellationToken);
        if (entity is null)
        {
            return null;
        }

        if (!TriviaFactSubmissionWorkflow.TryValidateStatusChange(
                entity.Status,
                TriviaFactSubmissionStatus.Approved,
                out var error))
        {
            throw new InvalidOperationException(error);
        }

        entity.Status = TriviaFactSubmissionStatus.Approved;
        entity.PromotedTriviaId = promotedTriviaId;
        entity.ReviewedAt = DateTimeOffset.UtcNow;
        entity.ReviewerEmail = SubmissionInput.NormalizeOptional(reviewerEmail, 256);
        entity.ReviewNotes = SubmissionInput.NormalizeOptional(reviewNotes, 500);

        dbContext.TriviaFactSubmissionAuditLogs.Add(new TriviaFactSubmissionAuditLogEntity
        {
            TriviaFactSubmissionId = entity.Id,
            Action = TriviaFactSubmissionStatus.Approved,
            ActorEmail = entity.ReviewerEmail ?? string.Empty,
            OccurredAt = entity.ReviewedAt.Value,
            Details = $"Approved and published as trivia fact #{promotedTriviaId}. Notes: {entity.ReviewNotes ?? "(none)"}",
        });

        await dbContext.SaveChangesAsync(cancellationToken);
        return Map(entity);
    }

    public async Task<TriviaFactSubmission?> RejectAsync(
        Guid id,
        string reviewerEmail,
        string rejectionReason,
        string? reviewNotes,
        CancellationToken cancellationToken = default)
    {
        var entity = await dbContext.TriviaFactSubmissions
            .SingleOrDefaultAsync(row => row.Id == id, cancellationToken);
        if (entity is null)
        {
            return null;
        }

        if (!TriviaFactSubmissionWorkflow.TryValidateStatusChange(
                entity.Status,
                TriviaFactSubmissionStatus.Rejected,
                out var error))
        {
            throw new InvalidOperationException(error);
        }

        var normalizedReason = SubmissionInput.NormalizeOptional(rejectionReason, 500)
            ?? throw new InvalidOperationException("A rejection reason is required.");
        entity.Status = TriviaFactSubmissionStatus.Rejected;
        entity.RejectionReason = normalizedReason;
        entity.ReviewedAt = DateTimeOffset.UtcNow;
        entity.ReviewerEmail = SubmissionInput.NormalizeOptional(reviewerEmail, 256);
        entity.ReviewNotes = SubmissionInput.NormalizeOptional(reviewNotes, 500);

        dbContext.TriviaFactSubmissionAuditLogs.Add(new TriviaFactSubmissionAuditLogEntity
        {
            TriviaFactSubmissionId = entity.Id,
            Action = TriviaFactSubmissionStatus.Rejected,
            ActorEmail = entity.ReviewerEmail ?? string.Empty,
            OccurredAt = entity.ReviewedAt.Value,
            Details = $"Rejected. Reason: {entity.RejectionReason}. Notes: {entity.ReviewNotes ?? "(none)"}",
        });

        await dbContext.SaveChangesAsync(cancellationToken);
        return Map(entity);
    }

    public Task<SubmissionTypeCounts> GetDashboardCountsAsync(
        DateTimeOffset utcNow,
        CancellationToken cancellationToken = default) =>
        dbContext.TriviaFactSubmissions
            .AsNoTracking()
            .Select(row => new SubmissionCountRow
            {
                SubmittedAt = row.SubmittedAt,
                IsOpen = row.Status == TriviaFactSubmissionStatus.Pending,
                IsApproved = row.Status == TriviaFactSubmissionStatus.Approved,
                IsRejected = row.Status == TriviaFactSubmissionStatus.Rejected,
                IsStillPending = row.Status == TriviaFactSubmissionStatus.Pending,
            })
            .ToDashboardCountsAsync(utcNow, aggregateInSql: false, cancellationToken);

    private static string? NormalizeDifficulty(string? value)
    {
        var trimmed = SubmissionInput.NormalizeOptional(value, TriviaValidation.MaxDifficultyLength);
        return trimmed?.ToLowerInvariant();
    }

    internal IQueryable<TriviaFactSubmissionListItem> PendingQueueQuery(int skip, int take) =>
        PendingQueue().NewestFirstPage(NewestFirst, ListItemProjection, skip, take);

    private IQueryable<TriviaFactSubmissionEntity> PendingQueue() =>
        dbContext.TriviaFactSubmissions
            .AsNoTracking()
            .Where(row => row.Status == TriviaFactSubmissionStatus.Pending);

    internal IQueryable<TriviaFactSubmission> MemberQueueQuery(Guid submitterMemberId, int skip, int take) =>
        SubmittedBy(submitterMemberId).NewestFirstPage(NewestFirst, SubmissionProjection, skip, take);

    private IQueryable<TriviaFactSubmissionEntity> SubmittedBy(Guid submitterMemberId) =>
        dbContext.TriviaFactSubmissions
            .AsNoTracking()
            .Where(row => row.SubmitterMemberId == submitterMemberId);

    private static TriviaFactSubmission Map(TriviaFactSubmissionEntity entity) => MapEntity(entity);
}
