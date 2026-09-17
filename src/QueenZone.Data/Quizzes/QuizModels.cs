namespace QueenZone.Data;

public sealed record QuizOptionDraft(string Text, bool IsCorrect);

public sealed record QuizQuestionDraft(
    string Text,
    int Points,
    IReadOnlyList<QuizOptionDraft> Options,
    string? Category = null,
    string? Difficulty = null);

public sealed record AdminQuizDraft(
    string Title,
    string? Description,
    IReadOnlyList<QuizQuestionDraft> Questions);

public sealed record QuizAdminItem(
    Guid Id,
    string Title,
    bool IsPublished,
    DateTimeOffset? PublishedAt,
    DateTimeOffset CreatedAt,
    int QuestionCount,
    int AttemptCount);

public sealed record QuizOptionView(Guid Id, string Text, int DisplayOrder, bool IsCorrect);

public sealed record QuizQuestionView(
    Guid Id,
    string Text,
    int DisplayOrder,
    int Points,
    IReadOnlyList<QuizOptionView> Options,
    string? Category = null,
    string? Difficulty = null);

public sealed record QuizAdminDetail(
    Guid Id,
    string Title,
    string? Description,
    bool IsPublished,
    DateTimeOffset? PublishedAt,
    DateTimeOffset CreatedAt,
    int AttemptCount,
    IReadOnlyList<QuizQuestionView> Questions);

public sealed record QuizListItem(Guid Id, string Title, string? Description, int QuestionCount);

public sealed record QuizPlayOption(Guid Id, string Text);

public sealed record QuizPlayQuestion(
    Guid Id,
    string Text,
    int DisplayOrder,
    int Points,
    IReadOnlyList<QuizPlayOption> Options);

/// <summary>
/// A published quiz shaped for play: options only, no correct-answer flag. Never expose
/// <see cref="QuizOptionView.IsCorrect"/> to a client before submit.
/// </summary>
public sealed record QuizPlayView(Guid Id, string Title, string? Description, IReadOnlyList<QuizPlayQuestion> Questions);

public sealed record QuizAnswerSubmission(Guid QuestionId, Guid? SelectedOptionId);

public sealed record QuizAnswerResult(
    Guid QuestionId,
    string QuestionText,
    Guid? SelectedOptionId,
    string? SelectedOptionText,
    Guid CorrectOptionId,
    string CorrectOptionText,
    bool IsCorrect,
    int PointsAwarded);

/// <summary>
/// Computed once, on submit, and never persisted beyond the aggregate <see cref="Entities.QuizAttemptEntity"/>
/// row — per-question review is not retrievable later (out of scope for #1103).
/// </summary>
public sealed record QuizSubmissionResult(
    Guid QuizId,
    string QuizTitle,
    int Score,
    int MaxScore,
    int CorrectCount,
    int QuestionCount,
    bool Recorded,
    IReadOnlyList<QuizAnswerResult> Answers);

public enum QuizLeaderboardScope
{
    Week,
    AllTime,
}

public sealed record QuizLeaderboardEntry(int Rank, Guid MemberAccountId, int Score, int AttemptCount);

public sealed record QuizLeaderboardResult(
    IReadOnlyList<QuizLeaderboardEntry> Top,
    QuizLeaderboardEntry? Viewer,
    int TotalMembers);

public sealed class QuizException : Exception
{
    public QuizException(string code, string message)
        : base(message)
    {
        Code = code;
    }

    public string Code { get; }

    public const string NotFound = "not_found";
    public const string HasResults = "has_results";
}
