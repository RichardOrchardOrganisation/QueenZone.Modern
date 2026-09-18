using QueenZone.Data.Entities;

namespace QueenZone.Data;

public sealed class InMemoryTriviaFactSubmissionRepository : ITriviaFactSubmissionRepository
{
    private readonly object sync = new();
    private readonly List<TriviaFactSubmissionEntity> submissions = [];
    private readonly List<TriviaFactSubmissionAuditLogEntity> auditLogs = [];
    private readonly Func<Guid, MemberAccount?>? resolveMember;
    private long nextAuditId = 1;

    public InMemoryTriviaFactSubmissionRepository(Func<Guid, MemberAccount?>? resolveMember = null)
    {
        this.resolveMember = resolveMember;
    }

    public Task<TriviaFactSubmission> CreateAsync(
        NewTriviaFactSubmission submission,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(submission);

        lock (sync)
        {
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

            submissions.Add(entity);
            auditLogs.Add(new TriviaFactSubmissionAuditLogEntity
            {
                Id = nextAuditId++,
                TriviaFactSubmissionId = entity.Id,
                Action = "Submitted",
                ActorEmail = string.Empty,
                OccurredAt = entity.SubmittedAt,
                Details = "Member submitted a trivia fact for review.",
            });

            return Task.FromResult(Map(entity));
        }
    }

    public Task<IReadOnlyList<TriviaFactSubmissionListItem>> GetPendingAsync(
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        lock (sync)
        {
            IReadOnlyList<TriviaFactSubmissionListItem> result = submissions
                .Where(row => row.Status == TriviaFactSubmissionStatus.Pending)
                .OrderByDescending(row => row.SubmittedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(row =>
                {
                    var member = resolveMember?.Invoke(row.SubmitterMemberId);
                    return new TriviaFactSubmissionListItem(
                        row.Id,
                        row.Text,
                        member?.DisplayName ?? "Unknown member",
                        row.SubmittedAt,
                        row.Category,
                        row.Status);
                })
                .ToList();

            return Task.FromResult(result);
        }
    }

    public Task<TriviaFactSubmission?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        lock (sync)
        {
            var entity = submissions.SingleOrDefault(row => row.Id == id);
            return Task.FromResult(entity is null ? null : Map(entity));
        }
    }

    public Task<SubmissionListPage<TriviaFactSubmission>> GetBySubmitterAsync(
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

            IReadOnlyList<TriviaFactSubmission> items = owned
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(Map)
                .ToList();

            return Task.FromResult(new SubmissionListPage<TriviaFactSubmission>(items, owned.Count));
        }
    }

    public Task<TriviaFactSubmission?> ApproveAsync(
        Guid id,
        int promotedTriviaId,
        string reviewerEmail,
        string? reviewNotes,
        CancellationToken cancellationToken = default)
    {
        lock (sync)
        {
            var entity = submissions.SingleOrDefault(row => row.Id == id);
            if (entity is null)
            {
                return Task.FromResult<TriviaFactSubmission?>(null);
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

            auditLogs.Add(new TriviaFactSubmissionAuditLogEntity
            {
                Id = nextAuditId++,
                TriviaFactSubmissionId = entity.Id,
                Action = TriviaFactSubmissionStatus.Approved,
                ActorEmail = entity.ReviewerEmail ?? string.Empty,
                OccurredAt = entity.ReviewedAt.Value,
                Details = $"Approved and published as trivia fact #{promotedTriviaId}. Notes: {entity.ReviewNotes ?? "(none)"}",
            });

            return Task.FromResult<TriviaFactSubmission?>(Map(entity));
        }
    }

    public Task<TriviaFactSubmission?> RejectAsync(
        Guid id,
        string reviewerEmail,
        string rejectionReason,
        string? reviewNotes,
        CancellationToken cancellationToken = default)
    {
        lock (sync)
        {
            var entity = submissions.SingleOrDefault(row => row.Id == id);
            if (entity is null)
            {
                return Task.FromResult<TriviaFactSubmission?>(null);
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

            auditLogs.Add(new TriviaFactSubmissionAuditLogEntity
            {
                Id = nextAuditId++,
                TriviaFactSubmissionId = entity.Id,
                Action = TriviaFactSubmissionStatus.Rejected,
                ActorEmail = entity.ReviewerEmail ?? string.Empty,
                OccurredAt = entity.ReviewedAt.Value,
                Details = $"Rejected. Reason: {entity.RejectionReason}. Notes: {entity.ReviewNotes ?? "(none)"}",
            });

            return Task.FromResult<TriviaFactSubmission?>(Map(entity));
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
            var pending = submissions.Count(row => row.Status == TriviaFactSubmissionStatus.Pending);
            var receivedToday = submissions.Count(row => row.SubmittedAt.UtcDateTime.Date >= today);
            var receivedThisWeek = submissions.Count(row => row.SubmittedAt.UtcDateTime.Date >= weekAgo);

            var last30 = submissions.Where(row => row.SubmittedAt >= monthAgo).ToList();
            var approvedLast30 = last30.Count(row => row.Status == TriviaFactSubmissionStatus.Approved);
            var rejectedLast30 = last30.Count(row => row.Status == TriviaFactSubmissionStatus.Rejected);
            var pendingLast30 = last30.Count(row => row.Status == TriviaFactSubmissionStatus.Pending);

            return Task.FromResult(new SubmissionTypeCounts(
                pending, receivedToday, receivedThisWeek, approvedLast30, rejectedLast30, pendingLast30));
        }
    }

    /// <summary>Test helper: audit entries written for a submission.</summary>
    public IReadOnlyList<TriviaFactSubmissionAuditLogEntity> GetAuditLogs(Guid submissionId)
    {
        lock (sync)
        {
            return auditLogs.Where(log => log.TriviaFactSubmissionId == submissionId).ToList();
        }
    }

    private TriviaFactSubmission Map(TriviaFactSubmissionEntity entity)
    {
        var member = resolveMember?.Invoke(entity.SubmitterMemberId);
        return new TriviaFactSubmission(
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
            member?.DisplayName,
            member?.Email);
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
}
