using QueenZone.Data.Entities;

namespace QueenZone.Data;

/// <summary>
/// Row construction and review transitions shared by the EF and in-memory quiz question
/// submission repositories, so both stores apply the same workflow rules.
/// </summary>
internal static class QuizQuestionSubmissionRecords
{
    /// <summary>
    /// Builds a pending submission row and its options. The caller has already validated the input.
    /// </summary>
    internal static QuizQuestionSubmissionEntity NewEntity(NewQuizQuestionSubmission submission, DateTimeOffset now)
    {
        var submissionId = Guid.NewGuid();
        var options = QuizQuestionSubmissionValidation.NormalizeOptions(submission.Options);
        return new QuizQuestionSubmissionEntity
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
    }

    /// <summary>
    /// Validates and applies a rejection. Returns the review time used for the audit row.
    /// </summary>
    internal static DateTimeOffset ApplyRejection(
        QuizQuestionSubmissionEntity entity,
        string reviewerEmail,
        string rejectionReason,
        string? reviewNotes)
    {
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
        return now;
    }

    internal static string RejectionAuditDetails(QuizQuestionSubmissionEntity entity) =>
        $"Rejected. Reason: {entity.RejectionReason}. Notes: {entity.ReviewNotes ?? "(none)"}";
}
