using QueenZone.Data.Entities;

namespace QueenZone.Data;

public sealed class InMemoryQuizQuestionSubmissionRepository : IQuizQuestionSubmissionRepository
{
    private readonly object sync = new();
    private readonly List<QuizQuestionSubmissionEntity> submissions = [];
    private readonly List<QuizQuestionSubmissionAuditLogEntity> auditLogs = [];
    private readonly Func<Guid, MemberAccount?>? resolveMember;
    private long nextAuditId = 1;

    public InMemoryQuizQuestionSubmissionRepository(Func<Guid, MemberAccount?>? resolveMember = null)
    {
        this.resolveMember = resolveMember;
    }

    public Task<QuizQuestionSubmission> CreateAsync(
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

        lock (sync)
        {
            var submissionId = Guid.NewGuid();
            var now = DateTimeOffset.UtcNow;
            var options = QuizQuestionSubmissionValidation.NormalizeOptions(submission.Options);
            var entity = new QuizQuestionSubmissionEntity
            {
                Id = submissionId,
                SubmitterMemberId = submission.SubmitterMemberId,
                QuestionText = submission.QuestionText.Trim(),
                SourceNote = SubmissionInput.NormalizeOptional(submission.SourceNote, QuizQuestionSubmissionValidation.MaxSourceNoteLength),
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

            submissions.Add(entity);
            auditLogs.Add(new QuizQuestionSubmissionAuditLogEntity
            {
                Id = nextAuditId++,
                QuizQuestionSubmissionId = entity.Id,
                Action = "Submitted",
                ActorEmail = string.Empty,
                OccurredAt = now,
                Details = "Member submitted a quiz question for review.",
            });

            return Task.FromResult(Map(entity));
        }
    }

    public Task<IReadOnlyList<QuizQuestionSubmissionListItem>> GetPendingAsync(
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        lock (sync)
        {
            IReadOnlyList<QuizQuestionSubmissionListItem> result = submissions
                .Where(row => row.Status == QuizQuestionSubmissionStatus.Pending)
                .OrderByDescending(row => row.SubmittedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(ToListItem)
                .ToList();

            return Task.FromResult(result);
        }
    }

    public Task<IReadOnlyList<QuizQuestionSubmissionListItem>> GetApprovedAndAvailableAsync(
        CancellationToken cancellationToken = default)
    {
        lock (sync)
        {
            IReadOnlyList<QuizQuestionSubmissionListItem> result = submissions
                .Where(row => row.Status == QuizQuestionSubmissionStatus.Approved && row.AddedToQuizId is null)
                .OrderByDescending(row => row.ReviewedAt)
                .Select(ToListItem)
                .ToList();

            return Task.FromResult(result);
        }
    }

    public Task<QuizQuestionSubmission?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        lock (sync)
        {
            var entity = submissions.SingleOrDefault(row => row.Id == id);
            return Task.FromResult(entity is null ? null : Map(entity));
        }
    }

    public Task<SubmissionListPage<QuizQuestionSubmission>> GetBySubmitterAsync(
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

            IReadOnlyList<QuizQuestionSubmission> items = owned
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(Map)
                .ToList();

            return Task.FromResult(new SubmissionListPage<QuizQuestionSubmission>(items, owned.Count));
        }
    }

    public Task<QuizQuestionSubmission?> ApproveAsync(
        Guid id,
        QuizQuestionSubmissionEdit edit,
        string reviewerEmail,
        string? reviewNotes,
        CancellationToken cancellationToken = default)
    {
        lock (sync)
        {
            var entity = submissions.SingleOrDefault(row => row.Id == id);
            if (entity is null)
            {
                return Task.FromResult<QuizQuestionSubmission?>(null);
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
            entity.ReviewerEmail = SubmissionInput.NormalizeOptional(reviewerEmail, 256);
            entity.ReviewNotes = SubmissionInput.NormalizeOptional(reviewNotes, 500);

            auditLogs.Add(new QuizQuestionSubmissionAuditLogEntity
            {
                Id = nextAuditId++,
                QuizQuestionSubmissionId = entity.Id,
                Action = QuizQuestionSubmissionStatus.Approved,
                ActorEmail = entity.ReviewerEmail ?? string.Empty,
                OccurredAt = now,
                Details = $"Approved for the quiz builder's question bank. Notes: {entity.ReviewNotes ?? "(none)"}",
            });

            return Task.FromResult<QuizQuestionSubmission?>(Map(entity));
        }
    }

    public Task<QuizQuestionSubmission?> RejectAsync(
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
                return Task.FromResult<QuizQuestionSubmission?>(null);
            }

            if (!QuizQuestionSubmissionWorkflow.TryValidateStatusChange(
                    entity.Status,
                    QuizQuestionSubmissionStatus.Rejected,
                    out var error))
            {
                throw new InvalidOperationException(error);
            }

            var normalizedReason = SubmissionInput.NormalizeOptional(rejectionReason, 500)
                ?? throw new InvalidOperationException("A rejection reason is required.");
            entity.Status = QuizQuestionSubmissionStatus.Rejected;
            entity.RejectionReason = normalizedReason;
            var now = DateTimeOffset.UtcNow;
            entity.ReviewedAt = now;
            entity.ReviewerEmail = SubmissionInput.NormalizeOptional(reviewerEmail, 256);
            entity.ReviewNotes = SubmissionInput.NormalizeOptional(reviewNotes, 500);

            auditLogs.Add(new QuizQuestionSubmissionAuditLogEntity
            {
                Id = nextAuditId++,
                QuizQuestionSubmissionId = entity.Id,
                Action = QuizQuestionSubmissionStatus.Rejected,
                ActorEmail = entity.ReviewerEmail ?? string.Empty,
                OccurredAt = now,
                Details = $"Rejected. Reason: {entity.RejectionReason}. Notes: {entity.ReviewNotes ?? "(none)"}",
            });

            return Task.FromResult<QuizQuestionSubmission?>(Map(entity));
        }
    }

    public Task<QuizQuestionSubmission?> MarkAddedToQuizAsync(
        Guid id,
        Guid quizId,
        string actorEmail,
        CancellationToken cancellationToken = default)
    {
        lock (sync)
        {
            var entity = submissions.SingleOrDefault(row => row.Id == id);
            if (entity is null)
            {
                return Task.FromResult<QuizQuestionSubmission?>(null);
            }

            if (!string.Equals(entity.Status, QuizQuestionSubmissionStatus.Approved, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Only approved submissions can be added to a quiz.");
            }

            var now = DateTimeOffset.UtcNow;
            entity.AddedToQuizId = quizId;
            entity.AddedToQuizAt = now;

            auditLogs.Add(new QuizQuestionSubmissionAuditLogEntity
            {
                Id = nextAuditId++,
                QuizQuestionSubmissionId = entity.Id,
                Action = "AddedToQuiz",
                ActorEmail = SubmissionInput.NormalizeOptional(actorEmail, 256) ?? string.Empty,
                OccurredAt = now,
                Details = $"Added to quiz {quizId} via the builder.",
            });

            return Task.FromResult<QuizQuestionSubmission?>(Map(entity));
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
            var pending = submissions.Count(row => row.Status == QuizQuestionSubmissionStatus.Pending);
            var receivedToday = submissions.Count(row => row.SubmittedAt.UtcDateTime.Date >= today);
            var receivedThisWeek = submissions.Count(row => row.SubmittedAt.UtcDateTime.Date >= weekAgo);

            var last30 = submissions.Where(row => row.SubmittedAt >= monthAgo).ToList();
            var approvedLast30 = last30.Count(row => row.Status == QuizQuestionSubmissionStatus.Approved);
            var rejectedLast30 = last30.Count(row => row.Status == QuizQuestionSubmissionStatus.Rejected);
            var pendingLast30 = last30.Count(row => row.Status == QuizQuestionSubmissionStatus.Pending);

            return Task.FromResult(new SubmissionTypeCounts(
                pending, receivedToday, receivedThisWeek, approvedLast30, rejectedLast30, pendingLast30));
        }
    }

    /// <summary>Test helper: audit entries written for a submission.</summary>
    public IReadOnlyList<QuizQuestionSubmissionAuditLogEntity> GetAuditLogs(Guid submissionId)
    {
        lock (sync)
        {
            return auditLogs.Where(log => log.QuizQuestionSubmissionId == submissionId).ToList();
        }
    }

    private QuizQuestionSubmissionListItem ToListItem(QuizQuestionSubmissionEntity row)
    {
        var member = resolveMember?.Invoke(row.SubmitterMemberId);
        return new QuizQuestionSubmissionListItem(
            row.Id,
            row.QuestionText,
            member?.DisplayName ?? "Unknown member",
            row.SubmittedAt,
            row.Status);
    }

    private QuizQuestionSubmission Map(QuizQuestionSubmissionEntity entity)
    {
        var member = resolveMember?.Invoke(entity.SubmitterMemberId);
        return new QuizQuestionSubmission(
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
            member?.DisplayName,
            member?.Email);
    }
}
