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
                Category = SubmissionInput.NormalizeOptional(submission.Category, TriviaValidation.MaxCategoryLength),
                Difficulty = TriviaValidation.NormalizeDifficulty(submission.Difficulty, TriviaValidation.MaxDifficultyLength),
                SourceNote = SubmissionInput.NormalizeOptional(submission.SourceNote, TriviaValidation.MaxSourceNoteLength),
                Status = TriviaFactSubmissionStatus.Pending,
                SubmittedAt = DateTimeOffset.UtcNow,
            };

            submissions.Add(entity);
            auditLogs.Add(SubmissionReview.Copy(
                new TriviaFactSubmissionAuditLogEntity
                {
                    Id = nextAuditId++,
                    TriviaFactSubmissionId = entity.Id,
                },
                SubmissionReview.Submitted(entity.SubmittedAt, "Member submitted a trivia fact for review.")));

            return Task.FromResult(Map(entity));
        }
    }

    public Task<IReadOnlyList<TriviaFactSubmissionListItem>> GetPendingAsync(
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var (skip, take) = SubmissionPaging.Normalize(page, pageSize);

        lock (sync)
        {
            IReadOnlyList<TriviaFactSubmissionListItem> result = submissions
                .Where(row => row.Status == TriviaFactSubmissionStatus.Pending)
                .OrderByDescending(row => row.SubmittedAt)
                .Skip(skip)
                .Take(take)
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
        var (skip, take) = SubmissionPaging.Normalize(page, pageSize);

        lock (sync)
        {
            var owned = submissions
                .Where(row => row.SubmitterMemberId == submitterMemberId)
                .OrderByDescending(row => row.SubmittedAt)
                .ToList();

            IReadOnlyList<TriviaFactSubmission> items = owned
                .Skip(skip)
                .Take(take)
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

            SubmissionReview.EnsureTransition(
                entity.Status,
                TriviaFactSubmissionStatus.Approved,
                TriviaFactSubmissionWorkflow.TryValidateStatusChange);

            entity.PromotedTriviaId = promotedTriviaId;
            var reviewedAt = SubmissionReview.Stamp(
                entity,
                TriviaFactSubmissionStatus.Approved,
                reviewerEmail,
                reviewNotes);

            auditLogs.Add(SubmissionReview.Copy(
                new TriviaFactSubmissionAuditLogEntity
                {
                    Id = nextAuditId++,
                    TriviaFactSubmissionId = entity.Id,
                },
                SubmissionReview.ForStatus(
                    TriviaFactSubmissionStatus.Approved,
                    entity.ReviewerEmail,
                    reviewedAt,
                    $"Approved and published as trivia fact #{promotedTriviaId}. Notes: {entity.ReviewNotes ?? "(none)"}")));

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

            SubmissionReview.EnsureTransition(
                entity.Status,
                TriviaFactSubmissionStatus.Rejected,
                TriviaFactSubmissionWorkflow.TryValidateStatusChange);

            entity.RejectionReason = SubmissionReview.RequireRejectionReason(rejectionReason);
            var reviewedAt = SubmissionReview.Stamp(
                entity,
                TriviaFactSubmissionStatus.Rejected,
                reviewerEmail,
                reviewNotes);

            auditLogs.Add(SubmissionReview.Copy(
                new TriviaFactSubmissionAuditLogEntity
                {
                    Id = nextAuditId++,
                    TriviaFactSubmissionId = entity.Id,
                },
                SubmissionReview.ForStatus(
                    TriviaFactSubmissionStatus.Rejected,
                    entity.ReviewerEmail,
                    reviewedAt,
                    $"Rejected. Reason: {entity.RejectionReason}. Notes: {entity.ReviewNotes ?? "(none)"}")));

            return Task.FromResult<TriviaFactSubmission?>(Map(entity));
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
                IsOpen = row.Status == TriviaFactSubmissionStatus.Pending,
                IsApproved = row.Status == TriviaFactSubmissionStatus.Approved,
                IsRejected = row.Status == TriviaFactSubmissionStatus.Rejected,
                IsStillPending = row.Status == TriviaFactSubmissionStatus.Pending,
            });
            return Task.FromResult(SubmissionDashboardQueries.CountRows(rows, utcNow));
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
}
