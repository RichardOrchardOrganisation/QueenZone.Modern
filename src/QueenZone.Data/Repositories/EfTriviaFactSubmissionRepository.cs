using Microsoft.EntityFrameworkCore;
using QueenZone.Data.Entities;

namespace QueenZone.Data;

public sealed class EfTriviaFactSubmissionRepository(QueenZoneDbContext dbContext) : ITriviaFactSubmissionRepository
{
    public async Task<TriviaFactSubmission> CreateAsync(
        NewTriviaFactSubmission submission,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(submission);

        var entity = new TriviaFactSubmissionEntity
        {
            Id = submission.Id is { } preferredId && preferredId != Guid.Empty
                ? preferredId
                : Guid.NewGuid(),
            SubmitterMemberId = submission.SubmitterMemberId,
            Text = submission.Text.Trim(),
            Category = NormalizeOptional(submission.Category, TriviaValidation.MaxCategoryLength),
            Difficulty = NormalizeDifficulty(submission.Difficulty),
            SourceNote = NormalizeOptional(submission.SourceNote, TriviaValidation.MaxSourceNoteLength),
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
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var skip = (page - 1) * pageSize;

        if (IsSqliteDatabase())
        {
            var rows = await dbContext.TriviaFactSubmissions
                .AsNoTracking()
                .Where(row => row.Status == TriviaFactSubmissionStatus.Pending)
                .Select(row => new
                {
                    row.Id,
                    row.Text,
                    DisplayName = row.Submitter != null ? row.Submitter.DisplayName : string.Empty,
                    row.SubmittedAt,
                    row.Category,
                    row.Status,
                })
                .ToListAsync(cancellationToken);

            return rows
                .OrderByDescending(row => row.SubmittedAt)
                .ThenBy(row => row.Id)
                .Skip(skip)
                .Take(pageSize)
                .Select(row => new TriviaFactSubmissionListItem(
                    row.Id,
                    row.Text,
                    string.IsNullOrWhiteSpace(row.DisplayName) ? "Unknown member" : row.DisplayName,
                    row.SubmittedAt,
                    row.Category,
                    row.Status))
                .ToList();
        }

        return await PendingQueueQuery(skip, pageSize).ToListAsync(cancellationToken);
    }

    public async Task<TriviaFactSubmission?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var entity = await dbContext.TriviaFactSubmissions
            .AsNoTracking()
            .Include(row => row.Submitter)
            .SingleOrDefaultAsync(row => row.Id == id, cancellationToken);

        return entity is null ? null : Map(entity);
    }

    public async Task<SubmissionListPage<TriviaFactSubmission>> GetBySubmitterAsync(
        Guid submitterMemberId,
        int page = 1,
        int pageSize = 10,
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var query = dbContext.TriviaFactSubmissions
            .AsNoTracking()
            .Where(row => row.SubmitterMemberId == submitterMemberId);

        var totalCount = await query.CountAsync(cancellationToken);
        var skip = (page - 1) * pageSize;

        if (IsSqliteDatabase())
        {
            var sqliteRows = await query
                .Select(row => new
                {
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
                    DisplayName = row.Submitter != null ? row.Submitter.DisplayName : null,
                    Email = row.Submitter != null ? row.Submitter.Email : null,
                })
                .ToListAsync(cancellationToken);

            var sqliteItems = sqliteRows
                .OrderByDescending(row => row.SubmittedAt)
                .ThenBy(row => row.Id)
                .Skip(skip)
                .Take(pageSize)
                .Select(row => new TriviaFactSubmission(
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
                    row.DisplayName,
                    row.Email))
                .ToList();
            return new SubmissionListPage<TriviaFactSubmission>(sqliteItems, totalCount);
        }

        var items = await MemberQueueQuery(submitterMemberId, skip, pageSize).ToListAsync(cancellationToken);
        return new SubmissionListPage<TriviaFactSubmission>(items, totalCount);
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
        entity.ReviewerEmail = NormalizeOptional(reviewerEmail, 256);
        entity.ReviewNotes = NormalizeOptional(reviewNotes, 500);

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

        var normalizedReason = NormalizeOptional(rejectionReason, 500)
            ?? throw new InvalidOperationException("A rejection reason is required.");
        entity.Status = TriviaFactSubmissionStatus.Rejected;
        entity.RejectionReason = normalizedReason;
        entity.ReviewedAt = DateTimeOffset.UtcNow;
        entity.ReviewerEmail = NormalizeOptional(reviewerEmail, 256);
        entity.ReviewNotes = NormalizeOptional(reviewNotes, 500);

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

    public async Task<SubmissionTypeCounts> GetDashboardCountsAsync(
        DateTimeOffset utcNow,
        CancellationToken cancellationToken = default)
    {
        // Materialize first so DateTimeOffset comparisons work on SQLite and SQL Server.
        var monthAgo = utcNow.AddDays(-30);
        var today = utcNow.UtcDateTime.Date;
        var weekAgo = today.AddDays(-6);

        var rows = await dbContext.TriviaFactSubmissions
            .AsNoTracking()
            .Select(row => new { row.Status, row.SubmittedAt })
            .ToListAsync(cancellationToken);

        var pending = rows.Count(row => row.Status == TriviaFactSubmissionStatus.Pending);
        var receivedToday = rows.Count(row => row.SubmittedAt.UtcDateTime.Date >= today);
        var receivedThisWeek = rows.Count(row => row.SubmittedAt.UtcDateTime.Date >= weekAgo);

        var last30 = rows.Where(row => row.SubmittedAt >= monthAgo).ToList();
        var approvedLast30 = last30.Count(row => row.Status == TriviaFactSubmissionStatus.Approved);
        var rejectedLast30 = last30.Count(row => row.Status == TriviaFactSubmissionStatus.Rejected);
        var pendingLast30 = last30.Count(row => row.Status == TriviaFactSubmissionStatus.Pending);

        return new SubmissionTypeCounts(
            pending, receivedToday, receivedThisWeek, approvedLast30, rejectedLast30, pendingLast30);
    }

    private static string? NormalizeDifficulty(string? value)
    {
        var trimmed = NormalizeOptional(value, TriviaValidation.MaxDifficultyLength);
        return trimmed?.ToLowerInvariant();
    }

    private static string? NormalizeOptional(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
    }

    internal IQueryable<TriviaFactSubmissionListItem> PendingQueueQuery(int skip, int take) =>
        dbContext.TriviaFactSubmissions
            .AsNoTracking()
            .Where(row => row.Status == TriviaFactSubmissionStatus.Pending)
            .OrderByDescending(row => row.SubmittedAt)
            .ThenBy(row => row.Id)
            .Skip(skip)
            .Take(take)
            .Select(row => new TriviaFactSubmissionListItem(
                row.Id,
                row.Text,
                row.Submitter != null ? row.Submitter.DisplayName : "Unknown member",
                row.SubmittedAt,
                row.Category,
                row.Status));

    internal IQueryable<TriviaFactSubmission> MemberQueueQuery(Guid submitterMemberId, int skip, int take) =>
        dbContext.TriviaFactSubmissions
            .AsNoTracking()
            .Where(row => row.SubmitterMemberId == submitterMemberId)
            .OrderByDescending(row => row.SubmittedAt)
            .ThenBy(row => row.Id)
            .Skip(skip)
            .Take(take)
            .Select(row => new TriviaFactSubmission(
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
                row.Submitter != null ? row.Submitter.Email : null));

    private bool IsSqliteDatabase() =>
        string.Equals(
            dbContext.Database.ProviderName,
            "Microsoft.EntityFrameworkCore.Sqlite",
            StringComparison.Ordinal);

    private static TriviaFactSubmission Map(TriviaFactSubmissionEntity entity) =>
        new(
            entity.Id,
            entity.SubmitterMemberId,
            entity.Text,
            entity.Category,
            entity.Difficulty,
            entity.SourceNote,
            entity.Status,
            entity.SubmittedAt,
            entity.ReviewedAt,
            entity.ReviewerEmail,
            entity.ReviewNotes,
            entity.RejectionReason,
            entity.PromotedTriviaId,
            entity.Submitter?.DisplayName,
            entity.Submitter?.Email);
}
