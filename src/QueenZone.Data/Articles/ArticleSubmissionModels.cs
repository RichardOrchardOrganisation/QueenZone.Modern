namespace QueenZone.Data;

/// <summary>
/// Input model for creating or updating a draft. On update, a null <c>CoverImageBlobPath</c>
/// keeps the stored cover image and an empty one clears it.
/// </summary>
public sealed record ArticleSubmissionDraft(
    Guid? Id,
    Guid AuthorMemberId,
    string Title,
    string? Excerpt,
    string Body,
    string? CoverImageBlobPath,
    string? Tags);

/// <summary>Row shown in the admin review queue and member's submission list.</summary>
public sealed record ArticleSubmissionListItem(
    Guid Id,
    string Title,
    string Status,
    string AuthorDisplayName,
    DateTimeOffset? SubmittedAt,
    DateTimeOffset? PublishedAt,
    int WordCountEstimate);

/// <summary>Full article submission detail for admin review and member status page.</summary>
public sealed record ArticleSubmission(
    Guid Id,
    Guid AuthorMemberId,
    string Title,
    string Slug,
    string? Excerpt,
    string Body,
    string? CoverImageBlobPath,
    string? Tags,
    string Status,
    DateTimeOffset? SubmittedAt,
    DateTimeOffset? PublishedAt,
    string? ReviewerEmail,
    string? ReviewNotes,
    string? RejectionReason,
    string? AuthorDisplayName,
    string? AuthorEmail);

/// <summary>Published article suitable for the public /articles listing.</summary>
public sealed record PublishedArticleSubmission(
    Guid Id,
    string Title,
    string Slug,
    string? Excerpt,
    string Body,
    string? CoverImageBlobPath,
    string? Tags,
    DateTimeOffset PublishedAt,
    string? AuthorDisplayName,
    int WordCount,
    Guid? AuthorMemberId = null,
    string? Category = null,
    string? Source = null)
{
    public int ReadTimeMinutes => Math.Max(1, WordCount / 200);
}
