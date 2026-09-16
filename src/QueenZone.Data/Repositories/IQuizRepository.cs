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
}
