namespace QueenZone.Data;

public static class EditorialArticleStatus
{
    public const string Draft = "Draft";
    public const string Published = "Published";
    public const string Unpublished = "Unpublished";
}

public sealed record EditorialArticle(
    Guid Id, int? LegacyArticleId, Guid? SourceSubmissionId, string Title, string Slug, string Excerpt,
    string Body, string AuthorName, string Category, string? Tags, string? Source,
    string? ImageBlobKey, string Status, DateTimeOffset PublishedAt,
    DateTimeOffset UpdatedAt, string UpdatedBy, string? PublishedImageBlobKey = null,
    bool HasPublishedVersion = false, int WordCount = 0);

public sealed record EditorialArticleDraft(
    Guid? Id, int? LegacyArticleId, Guid? SourceSubmissionId, string Title, string? Slug, string Excerpt,
    string Body, string AuthorName, string Category, string? Tags, string? Source,
    string? ImageBlobKey, DateTimeOffset PublishedAt);
