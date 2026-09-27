namespace QueenZone.Data;

public sealed record QuizQuestionSubmissionOptionDraft(string Text, bool IsCorrect);

public sealed record QuizQuestionSubmissionOptionView(Guid Id, string Text, int DisplayOrder, bool IsCorrect);

public sealed record NewQuizQuestionSubmission(
    Guid SubmitterMemberId,
    string QuestionText,
    IReadOnlyList<QuizQuestionSubmissionOptionDraft> Options,
    string? SourceNote);

/// <summary>Admin edits to wording/options before approving (logged internally, not shown to the submitter).</summary>
public sealed record QuizQuestionSubmissionEdit(
    string QuestionText,
    IReadOnlyList<QuizQuestionSubmissionOptionDraft> Options);

public sealed record QuizQuestionSubmission(
    Guid Id,
    Guid SubmitterMemberId,
    string QuestionText,
    IReadOnlyList<QuizQuestionSubmissionOptionView> Options,
    string? SourceNote,
    string Status,
    DateTimeOffset SubmittedAt,
    DateTimeOffset? ReviewedAt,
    string? ReviewerEmail,
    string? ReviewNotes,
    string? RejectionReason,
    Guid? AddedToQuizId,
    string? SubmitterDisplayName = null,
    string? SubmitterEmail = null);

public sealed record QuizQuestionSubmissionListItem(
    Guid Id,
    string QuestionText,
    string SubmitterDisplayName,
    DateTimeOffset SubmittedAt,
    string Status);
