namespace QueenZone.Data;

public sealed record QuizOptionDraft(string Text, bool IsCorrect);

public sealed record QuizQuestionDraft(string Text, int Points, IReadOnlyList<QuizOptionDraft> Options);

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
    IReadOnlyList<QuizOptionView> Options);

public sealed record QuizAdminDetail(
    Guid Id,
    string Title,
    string? Description,
    bool IsPublished,
    DateTimeOffset? PublishedAt,
    DateTimeOffset CreatedAt,
    int AttemptCount,
    IReadOnlyList<QuizQuestionView> Questions);

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
