using QueenZone.Data.Entities;

namespace QueenZone.Data;

/// <summary>
/// Review stamp and audit-row values shared by trivia, quiz, photo, and fan-performance
/// submissions. Type-specific rules stay in the concrete repository or its records type.
/// </summary>
internal static class SubmissionReview
{
    private const int ReviewerEmailMaxLength = 256;
    private const int ReviewNotesMaxLength = 500;
    private const int RejectionReasonMaxLength = 500;

    internal delegate bool StatusValidator(string current, string next, out string? error);

    /// <summary>
    /// Throws <see cref="InvalidOperationException"/> with the workflow's own message when
    /// <paramref name="current"/> cannot move to <paramref name="next"/>.
    /// </summary>
    internal static void EnsureTransition(string current, string next, StatusValidator validate)
    {
        if (!validate(current, next, out var error))
        {
            throw new InvalidOperationException(error);
        }
    }

    /// <summary>
    /// Trims and truncates a rejection reason. Blank input throws the message the repositories
    /// already use.
    /// </summary>
    internal static string RequireRejectionReason(string? rejectionReason) =>
        SubmissionInput.NormalizeOptional(rejectionReason, RejectionReasonMaxLength)
            ?? throw new InvalidOperationException("A rejection reason is required.");

    /// <summary>
    /// Audit row for a new submission: action <c>Submitted</c>, empty actor, occurred-at equal to
    /// <paramref name="submittedAt"/>.
    /// </summary>
    internal static SubmissionAuditValues Submitted(DateTimeOffset submittedAt, string details) =>
        new("Submitted", string.Empty, submittedAt, details);

    /// <summary>Audit row for a review decision. A missing actor email is stored as empty.</summary>
    internal static SubmissionAuditValues ForStatus(
        string action,
        string? actorEmail,
        DateTimeOffset occurredAt,
        string? details) =>
        new(action, actorEmail ?? string.Empty, occurredAt, details);

    /// <summary>
    /// Overwrites status, reviewed-at, reviewer email, and review notes. Returns the review instant
    /// so the audit row can store the same value. Fan-performance status updates do not use this:
    /// a blank actor or a null notes argument leaves the previous values in place.
    /// </summary>
    internal static DateTimeOffset Stamp(
        TriviaFactSubmissionEntity entity,
        string status,
        string? reviewerEmail,
        string? reviewNotes) =>
        ApplyStamp(
            statusValue => entity.Status = statusValue,
            reviewedAt => entity.ReviewedAt = reviewedAt,
            email => entity.ReviewerEmail = email,
            notes => entity.ReviewNotes = notes,
            status,
            reviewerEmail,
            reviewNotes);

    internal static DateTimeOffset Stamp(
        QuizQuestionSubmissionEntity entity,
        string status,
        string? reviewerEmail,
        string? reviewNotes) =>
        ApplyStamp(
            statusValue => entity.Status = statusValue,
            reviewedAt => entity.ReviewedAt = reviewedAt,
            email => entity.ReviewerEmail = email,
            notes => entity.ReviewNotes = notes,
            status,
            reviewerEmail,
            reviewNotes);

    internal static DateTimeOffset Stamp(
        PhotoSubmissionEntity entity,
        string status,
        string? reviewerEmail,
        string? reviewNotes) =>
        ApplyStamp(
            statusValue => entity.Status = statusValue,
            reviewedAt => entity.ReviewedAt = reviewedAt,
            email => entity.ReviewerEmail = email,
            notes => entity.ReviewNotes = notes,
            status,
            reviewerEmail,
            reviewNotes);

    internal static DateTimeOffset Stamp(
        FanPerformanceSubmissionEntity entity,
        string status,
        string? reviewerEmail,
        string? reviewNotes) =>
        ApplyStamp(
            statusValue => entity.Status = statusValue,
            reviewedAt => entity.ReviewedAt = reviewedAt,
            email => entity.ReviewerEmail = email,
            notes => entity.ReviewNotes = notes,
            status,
            reviewerEmail,
            reviewNotes);

    internal static TriviaFactSubmissionAuditLogEntity Copy(
        TriviaFactSubmissionAuditLogEntity entry,
        SubmissionAuditValues values)
    {
        ApplyValues(
            values,
            action => entry.Action = action,
            actorEmail => entry.ActorEmail = actorEmail,
            occurredAt => entry.OccurredAt = occurredAt,
            details => entry.Details = details);
        return entry;
    }

    internal static QuizQuestionSubmissionAuditLogEntity Copy(
        QuizQuestionSubmissionAuditLogEntity entry,
        SubmissionAuditValues values)
    {
        ApplyValues(
            values,
            action => entry.Action = action,
            actorEmail => entry.ActorEmail = actorEmail,
            occurredAt => entry.OccurredAt = occurredAt,
            details => entry.Details = details);
        return entry;
    }

    internal static PhotoSubmissionAuditLogEntity Copy(
        PhotoSubmissionAuditLogEntity entry,
        SubmissionAuditValues values)
    {
        ApplyValues(
            values,
            action => entry.Action = action,
            actorEmail => entry.ActorEmail = actorEmail,
            occurredAt => entry.OccurredAt = occurredAt,
            details => entry.Details = details);
        return entry;
    }

    internal static FanPerformanceSubmissionAuditLogEntity Copy(
        FanPerformanceSubmissionAuditLogEntity entry,
        SubmissionAuditValues values)
    {
        ApplyValues(
            values,
            action => entry.Action = action,
            actorEmail => entry.ActorEmail = actorEmail,
            occurredAt => entry.OccurredAt = occurredAt,
            details => entry.Details = details);
        return entry;
    }

    private static DateTimeOffset ApplyStamp(
        Action<string> setStatus,
        Action<DateTimeOffset?> setReviewedAt,
        Action<string?> setReviewerEmail,
        Action<string?> setReviewNotes,
        string status,
        string? reviewerEmail,
        string? reviewNotes)
    {
        var reviewedAt = DateTimeOffset.UtcNow;
        setStatus(status);
        setReviewedAt(reviewedAt);
        setReviewerEmail(SubmissionInput.NormalizeOptional(reviewerEmail, ReviewerEmailMaxLength));
        setReviewNotes(SubmissionInput.NormalizeOptional(reviewNotes, ReviewNotesMaxLength));
        return reviewedAt;
    }

    private static void ApplyValues(
        SubmissionAuditValues values,
        Action<string> setAction,
        Action<string> setActorEmail,
        Action<DateTimeOffset> setOccurredAt,
        Action<string?> setDetails)
    {
        setAction(values.Action);
        setActorEmail(values.ActorEmail);
        setOccurredAt(values.OccurredAt);
        setDetails(values.Details);
    }
}

/// <summary>Action, actor, time, and details for one submission audit row.</summary>
internal readonly record struct SubmissionAuditValues(
    string Action,
    string ActorEmail,
    DateTimeOffset OccurredAt,
    string? Details);
