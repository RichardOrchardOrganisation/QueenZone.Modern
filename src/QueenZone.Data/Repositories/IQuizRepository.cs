namespace QueenZone.Data;

public interface IQuizRepository
{
    Task<IReadOnlyList<QuizAdminItem>> GetAllAsync(CancellationToken cancellationToken = default);

    Task<QuizAdminDetail?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<Guid> CreateAsync(
        AdminQuizDraft draft,
        Guid createdByMemberId,
        CancellationToken cancellationToken = default);

    Task UpdateAsync(Guid id, AdminQuizDraft draft, CancellationToken cancellationToken = default);

    Task PublishAsync(Guid id, CancellationToken cancellationToken = default);

    Task UnpublishAsync(Guid id, CancellationToken cancellationToken = default);

    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Records a completed attempt. Scoring is computed by the caller (server-side); this just
    /// persists the result. Any recorded attempt locks the quiz's questions/options from edits.
    /// </summary>
    Task RecordAttemptAsync(
        Guid quizId,
        Guid memberAccountId,
        int score,
        int correctCount,
        int questionCount,
        CancellationToken cancellationToken = default);

    /// <summary>Published quizzes for the public quiz list. Unpublished quizzes never appear.</summary>
    Task<IReadOnlyList<QuizListItem>> GetPublishedAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// A published quiz shaped for play — options only, no correct-answer flag. Null when the
    /// quiz does not exist or is not published.
    /// </summary>
    Task<QuizPlayView?> GetPublishedForPlayAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Scores <paramref name="answers"/> server-side against the quiz's stored correct options.
    /// When <paramref name="memberAccountId"/> is given, also records the attempt (leaderboard +
    /// admin edit lock); anonymous play still returns a score but is never recorded. Null when
    /// the quiz does not exist or is not published.
    /// </summary>
    Task<QuizSubmissionResult?> SubmitAsync(
        Guid quizId,
        Guid? memberAccountId,
        IReadOnlyList<QuizAnswerSubmission> answers,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Ranks members by the sum of their recorded attempt scores within <paramref name="scope"/>
    /// (current UTC week, Monday start, or all-time). <paramref name="viewerMemberId"/>'s own
    /// entry is included even when outside the top page.
    /// </summary>
    Task<QuizLeaderboardResult> GetLeaderboardAsync(
        QuizLeaderboardScope scope,
        Guid? viewerMemberId,
        int top = 10,
        CancellationToken cancellationToken = default);
}
