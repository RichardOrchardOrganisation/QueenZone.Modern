using System.Diagnostics.CodeAnalysis;

namespace QueenZone.Data.Entities;

[ExcludeFromCodeCoverage]
public sealed class QuizEntity
{
    public Guid Id { get; set; }

    public string Title { get; set; } = string.Empty;

    public string? Description { get; set; }

    public bool IsPublished { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Set the first time the quiz is published. Null means it has never been live.</summary>
    public DateTimeOffset? PublishedAt { get; set; }

    public Guid CreatedByMemberId { get; set; }

    public ICollection<QuizQuestionEntity> Questions { get; set; } = [];

    public ICollection<QuizAttemptEntity> Attempts { get; set; } = [];
}

[ExcludeFromCodeCoverage]
public sealed class QuizQuestionEntity
{
    public Guid Id { get; set; }

    public Guid QuizId { get; set; }

    public string QuestionText { get; set; } = string.Empty;

    public int DisplayOrder { get; set; }

    public int Points { get; set; } = 1;

    public QuizEntity? Quiz { get; set; }

    public ICollection<QuizOptionEntity> Options { get; set; } = [];
}

[ExcludeFromCodeCoverage]
public sealed class QuizOptionEntity
{
    public Guid Id { get; set; }

    public Guid QuestionId { get; set; }

    public string OptionText { get; set; } = string.Empty;

    public int DisplayOrder { get; set; }

    public bool IsCorrect { get; set; }

    public QuizQuestionEntity? Question { get; set; }
}

/// <summary>
/// A member's completed attempt at a quiz. Recorded server-side on submit; existence of any
/// attempt locks the quiz's questions/options from further admin edits. Also the source for
/// the weekly/all-time leaderboard.
/// </summary>
[ExcludeFromCodeCoverage]
public sealed class QuizAttemptEntity
{
    public Guid Id { get; set; }

    public Guid QuizId { get; set; }

    public Guid MemberAccountId { get; set; }

    public int Score { get; set; }

    public int CorrectCount { get; set; }

    public int QuestionCount { get; set; }

    public DateTimeOffset CompletedAt { get; set; }

    public QuizEntity? Quiz { get; set; }
}
