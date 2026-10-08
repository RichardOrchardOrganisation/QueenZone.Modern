namespace QueenZone.Web;

/// <summary>
/// Stable list/card shape for public article archive surfaces.
/// </summary>
public sealed record ArticleArchiveItem(
    int Id,
    string Title,
    string Excerpt,
    DateTime PublishedAt,
    string? CategoryName,
    string DetailPath);

/// <summary>
/// Stable detail shape for public article pages.
/// </summary>
public sealed record ArticleDetailItem(
    int Id,
    string Title,
    string Excerpt,
    string Body,
    DateTime PublishedAt,
    string? Source,
    string? CategoryName,
    string DetailPath,
    string? ImageUrl = null,
    string? AuthorName = null,
    string? Tags = null);

/// <summary>Shared public article hero, independent of the article's storage and route.</summary>
public sealed record ArticleHeader(
    string Title,
    string? ImageUrl,
    IReadOnlyList<BreadcrumbItem> Breadcrumbs,
    string Label,
    DateTime PublishedAt,
    string? AuthorName = null,
    Guid? AuthorMemberId = null,
    int? ReadTimeMinutes = null);
