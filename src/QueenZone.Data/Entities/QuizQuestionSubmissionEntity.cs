using System.Diagnostics.CodeAnalysis;

namespace QueenZone.Data.Entities;

[ExcludeFromCodeCoverage]
public sealed class QuizQuestionSubmissionEntity
{
    public Guid Id { get; set; }

    public Guid SubmitterMemberId { get; set; }

    public string QuestionText { get; set; } = string.Empty;

    public string? SourceNote { get; set; }

    public string Status { get; set; } = QuizQuestionSubmissionStatus.Pending;

    public DateTimeOffset SubmittedAt { get; set; }

    public DateTimeOffset? ReviewedAt { get; set; }

    public string? ReviewerEmail { get; set; }

    public string? ReviewNotes { get; set; }

    public string? RejectionReason { get; set; }

    /// <summary>Set once an admin has added this approved question into a quiz via the builder.</summary>
    public Guid? AddedToQuizId { get; set; }

    public DateTimeOffset? AddedToQuizAt { get; set; }

    public MemberAccount? Submitter { get; set; }

    public ICollection<QuizQuestionSubmissionOptionEntity> Options { get; set; } = [];

    public ICollection<QuizQuestionSubmissionAuditLogEntity> AuditLogs { get; set; } = [];
}

[ExcludeFromCodeCoverage]
public sealed class QuizQuestionSubmissionOptionEntity
{
    public Guid Id { get; set; }

    public Guid QuizQuestionSubmissionId { get; set; }

    public string OptionText { get; set; } = string.Empty;

    public int DisplayOrder { get; set; }

    public bool IsCorrect { get; set; }

    public QuizQuestionSubmissionEntity? Submission { get; set; }
}

[ExcludeFromCodeCoverage]
public sealed class QuizQuestionSubmissionAuditLogEntity
{
    public long Id { get; set; }

    public Guid QuizQuestionSubmissionId { get; set; }

    public string Action { get; set; } = string.Empty;

    public string ActorEmail { get; set; } = string.Empty;

    public DateTimeOffset OccurredAt { get; set; }

    public string? Details { get; set; }

    public QuizQuestionSubmissionEntity? Submission { get; set; }
}
