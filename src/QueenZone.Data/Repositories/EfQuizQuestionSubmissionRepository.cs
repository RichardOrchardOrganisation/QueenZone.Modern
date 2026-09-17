using Microsoft.EntityFrameworkCore;
using QueenZone.Data.Entities;

namespace QueenZone.Data;

public sealed class EfQuizQuestionSubmissionRepository(QueenZoneDbContext dbContext)
    : IQuizQuestionSubmissionRepository
{
    public async Task<QuizQuestionSubmission> CreateAsync(
        NewQuizQuestionSubmission submission,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(submission);

        var errors = QuizQuestionSubmissionValidation.ValidateSubmission(
            submission.QuestionText,
            submission.Options,
            submission.SourceNote);
        if (errors.Count > 0)
        {
            throw new ArgumentException(string.Join(" ", errors), nameof(submission));
        }

        var submissionId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var options = QuizQuestionSubmissionValidation.NormalizeOptions(submission.Options);
        var entity = new QuizQuestionSubmissionEntity
        {
            Id = submissionId,
            SubmitterMemberId = submission.SubmitterMemberId,
            QuestionText = submission.QuestionText.Trim(),
            SourceNote = NormalizeOptional(submission.SourceNote, QuizQuestionSubmissionValidation.MaxSourceNoteLength),
            Status = QuizQuestionSubmissionStatus.Pending,
            SubmittedAt = now,
            Options = options
                .Select((option, index) => new QuizQuestionSubmissionOptionEntity
                {
                    Id = Guid.NewGuid(),
                    QuizQuestionSubmissionId = submissionId,
                    OptionText = option.Text,
                    DisplayOrder = index,
                    IsCorrect = option.IsCorrect,
                })
                .ToList(),
        };

        entity.AuditLogs.Add(new QuizQuestionSubmissionAuditLogEntity
        {
            QuizQuestionSubmissionId = entity.Id,
            Action = "Submitted",
            ActorEmail = string.Empty,
            OccurredAt = now,
            Details = "Member submitted a quiz question for review.",
        });

        dbContext.QuizQuestionSubmissions.Add(entity);
        await dbContext.SaveChangesAsync(cancellationToken);
        return Map(entity);
    }

    public async Task<IReadOnlyList<QuizQuestionSubmissionListItem>> GetPendingAsync(
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var skip = (page - 1) * pageSize;

        return await dbContext.QuizQuestionSubmissions
            .AsNoTracking()
            .Where(row => row.Status == QuizQuestionSubmissionStatus.Pending)
            .OrderByDescending(row => row.SubmittedAt)
            .ThenBy(row => row.Id)
            .Skip(skip)
            .Take(pageSize)
            .Select(row => new QuizQuestionSubmissionListItem(
                row.Id,
                row.QuestionText,
                row.Submitter != null ? row.Submitter.DisplayName : "Unknown member",
                row.SubmittedAt,
                row.Status))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<QuizQuestionSubmissionListItem>> GetApprovedAndAvailableAsync(
        CancellationToken cancellationToken = default) =>
        await dbContext.QuizQuestionSubmissions
            .AsNoTracking()
            .Where(row => row.Status == QuizQuestionSubmissionStatus.Approved && row.AddedToQuizId == null)
            .OrderByDescending(row => row.ReviewedAt)
            .Select(row => new QuizQuestionSubmissionListItem(
                row.Id,
                row.QuestionText,
                row.Submitter != null ? row.Submitter.DisplayName : "Unknown member",
                row.SubmittedAt,
                row.Status))
            .ToListAsync(cancellationToken);

    public async Task<QuizQuestionSubmission?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var entity = await dbContext.QuizQuestionSubmissions
            .AsNoTracking()
            .Include(row => row.Submitter)
            .Include(row => row.Options)
            .SingleOrDefaultAsync(row => row.Id == id, cancellationToken);

        return entity is null ? null : Map(entity);
    }

    public async Task<SubmissionListPage<QuizQuestionSubmission>> GetBySubmitterAsync(
        Guid submitterMemberId,
        int page = 1,
        int pageSize = 10,
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var skip = (page - 1) * pageSize;

        var query = dbContext.QuizQuestionSubmissions
            .AsNoTracking()
            .Where(row => row.SubmitterMemberId == submitterMemberId);

        var totalCount = await query.CountAsync(cancellationToken);
        var entities = await query
            .Include(row => row.Submitter)
            .Include(row => row.Options)
            .OrderByDescending(row => row.SubmittedAt)
            .ThenBy(row => row.Id)
            .Skip(skip)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return new SubmissionListPage<QuizQuestionSubmission>(entities.Select(Map).ToList(), totalCount);
    }

    public async Task<QuizQuestionSubmission?> ApproveAsync(
        Guid id,
        QuizQuestionSubmissionEdit edit,
        string reviewerEmail,
        string? reviewNotes,
        CancellationToken cancellationToken = default)
    {
        var entity = await dbContext.QuizQuestionSubmissions
            .Include(row => row.Options)
            .SingleOrDefaultAsync(row => row.Id == id, cancellationToken);
        if (entity is null)
        {
            return null;
        }

        if (!QuizQuestionSubmissionWorkflow.TryValidateStatusChange(
                entity.Status,
                QuizQuestionSubmissionStatus.Approved,
                out var statusError))
        {
            throw new InvalidOperationException(statusError);
        }

        var errors = QuizQuestionSubmissionValidation.ValidateSubmission(
            edit.QuestionText,
            edit.Options,
            entity.SourceNote);
        if (errors.Count > 0)
        {
            throw new ArgumentException(string.Join(" ", errors), nameof(edit));
        }

        entity.QuestionText = edit.QuestionText.Trim();
        dbContext.QuizQuestionSubmissionOptions.RemoveRange(entity.Options);
        var options = QuizQuestionSubmissionValidation.NormalizeOptions(edit.Options);
        entity.Options = options
            .Select((option, index) => new QuizQuestionSubmissionOptionEntity
            {
                Id = Guid.NewGuid(),
                QuizQuestionSubmissionId = entity.Id,
                OptionText = option.Text,
                DisplayOrder = index,
                IsCorrect = option.IsCorrect,
            })
            .ToList();

        entity.Status = QuizQuestionSubmissionStatus.Approved;
        var now = DateTimeOffset.UtcNow;
        entity.ReviewedAt = now;
        entity.ReviewerEmail = NormalizeOptional(reviewerEmail, 256);
        entity.ReviewNotes = NormalizeOptional(reviewNotes, 500);

        dbContext.QuizQuestionSubmissionAuditLogs.Add(new QuizQuestionSubmissionAuditLogEntity
        {
            QuizQuestionSubmissionId = entity.Id,
            Action = QuizQuestionSubmissionStatus.Approved,
            ActorEmail = entity.ReviewerEmail ?? string.Empty,
            OccurredAt = now,
            Details = $"Approved for the quiz builder's question bank. Notes: {entity.ReviewNotes ?? "(none)"}",
        });

        await dbContext.SaveChangesAsync(cancellationToken);
        return await GetByIdAsync(id, cancellationToken);
    }

    public async Task<QuizQuestionSubmission?> RejectAsync(
        Guid id,
        string reviewerEmail,
        string rejectionReason,
        string? reviewNotes,
        CancellationToken cancellationToken = default)
    {
        var entity = await dbContext.QuizQuestionSubmissions
            .SingleOrDefaultAsync(row => row.Id == id, cancellationToken);
        if (entity is null)
        {
            return null;
        }

        if (!QuizQuestionSubmissionWorkflow.TryValidateStatusChange(
                entity.Status,
                QuizQuestionSubmissionStatus.Rejected,
                out var error))
        {
            throw new InvalidOperationException(error);
        }

        var normalizedReason = NormalizeOptional(rejectionReason, 500)
            ?? throw new InvalidOperationException("A rejection reason is required.");
        entity.Status = QuizQuestionSubmissionStatus.Rejected;
        entity.RejectionReason = normalizedReason;
        var now = DateTimeOffset.UtcNow;
        entity.ReviewedAt = now;
        entity.ReviewerEmail = NormalizeOptional(reviewerEmail, 256);
        entity.ReviewNotes = NormalizeOptional(reviewNotes, 500);

        dbContext.QuizQuestionSubmissionAuditLogs.Add(new QuizQuestionSubmissionAuditLogEntity
        {
            QuizQuestionSubmissionId = entity.Id,
            Action = QuizQuestionSubmissionStatus.Rejected,
            ActorEmail = entity.ReviewerEmail ?? string.Empty,
            OccurredAt = now,
            Details = $"Rejected. Reason: {entity.RejectionReason}. Notes: {entity.ReviewNotes ?? "(none)"}",
        });

        await dbContext.SaveChangesAsync(cancellationToken);
        return await GetByIdAsync(id, cancellationToken);
    }

    public async Task<QuizQuestionSubmission?> MarkAddedToQuizAsync(
        Guid id,
        Guid quizId,
        string actorEmail,
        CancellationToken cancellationToken = default)
    {
        var entity = await dbContext.QuizQuestionSubmissions
            .SingleOrDefaultAsync(row => row.Id == id, cancellationToken);
        if (entity is null)
        {
            return null;
        }

        if (!string.Equals(entity.Status, QuizQuestionSubmissionStatus.Approved, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Only approved submissions can be added to a quiz.");
        }

        var now = DateTimeOffset.UtcNow;
        entity.AddedToQuizId = quizId;
        entity.AddedToQuizAt = now;

        dbContext.QuizQuestionSubmissionAuditLogs.Add(new QuizQuestionSubmissionAuditLogEntity
        {
            QuizQuestionSubmissionId = entity.Id,
            Action = "AddedToQuiz",
            ActorEmail = NormalizeOptional(actorEmail, 256) ?? string.Empty,
            OccurredAt = now,
            Details = $"Added to quiz {quizId} via the builder.",
        });

        await dbContext.SaveChangesAsync(cancellationToken);
        return await GetByIdAsync(id, cancellationToken);
    }

    public async Task<SubmissionTypeCounts> GetDashboardCountsAsync(
        DateTimeOffset utcNow,
        CancellationToken cancellationToken = default)
    {
        var monthAgo = utcNow.AddDays(-30);
        var today = utcNow.UtcDateTime.Date;
        var weekAgo = today.AddDays(-6);

        var rows = await dbContext.QuizQuestionSubmissions
            .AsNoTracking()
            .Select(row => new { row.Status, row.SubmittedAt })
            .ToListAsync(cancellationToken);

        var pending = rows.Count(row => row.Status == QuizQuestionSubmissionStatus.Pending);
        var receivedToday = rows.Count(row => row.SubmittedAt.UtcDateTime.Date >= today);
        var receivedThisWeek = rows.Count(row => row.SubmittedAt.UtcDateTime.Date >= weekAgo);

        var last30 = rows.Where(row => row.SubmittedAt >= monthAgo).ToList();
        var approvedLast30 = last30.Count(row => row.Status == QuizQuestionSubmissionStatus.Approved);
        var rejectedLast30 = last30.Count(row => row.Status == QuizQuestionSubmissionStatus.Rejected);
        var pendingLast30 = last30.Count(row => row.Status == QuizQuestionSubmissionStatus.Pending);

        return new SubmissionTypeCounts(
            pending, receivedToday, receivedThisWeek, approvedLast30, rejectedLast30, pendingLast30);
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

    private static QuizQuestionSubmission Map(QuizQuestionSubmissionEntity entity) =>
        new(
            entity.Id,
            entity.SubmitterMemberId,
            entity.QuestionText,
            entity.Options
                .OrderBy(option => option.DisplayOrder)
                .Select(option => new QuizQuestionSubmissionOptionView(option.Id, option.OptionText, option.DisplayOrder, option.IsCorrect))
                .ToList(),
            entity.SourceNote,
            entity.Status,
            entity.SubmittedAt,
            entity.ReviewedAt,
            entity.ReviewerEmail,
            entity.ReviewNotes,
            entity.RejectionReason,
            entity.AddedToQuizId,
            entity.Submitter?.DisplayName,
            entity.Submitter?.Email);
}
