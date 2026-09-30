using System.Diagnostics.CodeAnalysis;

namespace QueenZone.Data.Entities;

[ExcludeFromCodeCoverage]
public sealed class ArticleSubmissionEntity
{
    public Guid Id { get; set; }

    public Guid AuthorMemberId { get; set; }

    public string Title { get; set; } = string.Empty;

    public string Slug { get; set; } = string.Empty;

    public string? Excerpt { get; set; }

    public string Body { get; set; } = string.Empty;

    public int WordCount { get; set; }

    public string? CoverImageBlobPath { get; set; }

    public string? Tags { get; set; }

    public string Status { get; set; } = ArticleSubmissionStatus.Draft;

    public DateTimeOffset? SubmittedAt { get; set; }

    public DateTimeOffset? PublishedAt { get; set; }

    public string? ReviewerEmail { get; set; }

    public string? ReviewNotes { get; set; }

    public string? RejectionReason { get; set; }

    public MemberAccount? Author { get; set; }
}
