namespace QueenZone.Data.Entities;

public sealed class EditorialArticleEntity
{
    public Guid Id { get; set; }
    public int? LegacyArticleId { get; set; }
    public Guid? SourceSubmissionId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string Excerpt { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public string AuthorName { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string? Tags { get; set; }
    public string? Source { get; set; }
    public string? ImageBlobKey { get; set; }
    public string Status { get; set; } = EditorialArticleStatus.Draft;
    public DateTimeOffset PublishedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public string UpdatedBy { get; set; } = string.Empty;
    public string? LiveTitle { get; set; }
    public string? LiveSlug { get; set; }
    public string? LiveExcerpt { get; set; }
    public string? LiveBody { get; set; }
    public int LiveWordCount { get; set; }
    public string? LiveAuthorName { get; set; }
    public string? LiveCategory { get; set; }
    public string? LiveTags { get; set; }
    public string? LiveSource { get; set; }
    public string? LiveImageBlobKey { get; set; }
    public DateTimeOffset? LivePublishedAt { get; set; }
}
