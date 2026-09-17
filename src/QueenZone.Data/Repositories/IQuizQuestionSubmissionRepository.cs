namespace QueenZone.Data;

public interface IQuizQuestionSubmissionRepository
{
    Task<QuizQuestionSubmission> CreateAsync(
        NewQuizQuestionSubmission submission,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<QuizQuestionSubmissionListItem>> GetPendingAsync(
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    /// <summary>Approved suggestions not yet added to a quiz — the quiz builder's question bank.</summary>
    Task<IReadOnlyList<QuizQuestionSubmissionListItem>> GetApprovedAndAvailableAsync(
        CancellationToken cancellationToken = default);

    Task<QuizQuestionSubmission?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<SubmissionListPage<QuizQuestionSubmission>> GetBySubmitterAsync(
        Guid submitterMemberId,
        int page = 1,
        int pageSize = 10,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Approves a pending suggestion, applying any admin wording/option edits. This makes the
    /// question available in the quiz builder's question bank — it does not by itself publish
    /// it into a live quiz.
    /// </summary>
    Task<QuizQuestionSubmission?> ApproveAsync(
        Guid id,
        QuizQuestionSubmissionEdit edit,
        string reviewerEmail,
        string? reviewNotes,
        CancellationToken cancellationToken = default);

    /// <summary>Rejects a pending suggestion. <paramref name="rejectionReason"/> is shown to the submitter.</summary>
    Task<QuizQuestionSubmission?> RejectAsync(
        Guid id,
        string reviewerEmail,
        string rejectionReason,
        string? reviewNotes,
        CancellationToken cancellationToken = default);

    /// <summary>Records that an admin added this approved question into <paramref name="quizId"/> via the builder.</summary>
    Task<QuizQuestionSubmission?> MarkAddedToQuizAsync(
        Guid id,
        Guid quizId,
        string actorEmail,
        CancellationToken cancellationToken = default);

    Task<SubmissionTypeCounts> GetDashboardCountsAsync(
        DateTimeOffset utcNow,
        CancellationToken cancellationToken = default);
}
