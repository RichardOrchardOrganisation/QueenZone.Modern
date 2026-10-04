using QueenZone.Data.Entities;

namespace QueenZone.Data;

/// <summary>
/// Related blocks on a song page: search-document <em>title</em> equals the canonical song
/// title (ordinal ignore-case). No body match and no link table.
/// </summary>
public static class SongRelatedContent
{
    public static bool IsRelatedType(string? contentType) => contentType switch
    {
        SiteSearchContentType.Forum => true,
        SiteSearchContentType.News => true,
        SiteSearchContentType.Article => true,
        SiteSearchContentType.LegacyArticle => true,
        SiteSearchContentType.Photo => true,
        SiteSearchContentType.Timeline => true,
        SiteSearchContentType.Tribute => true,
        SiteSearchContentType.FreddieTribute => true,
        _ => false,
    };

    public static string SectionKey(string contentType) => contentType switch
    {
        SiteSearchContentType.Article or SiteSearchContentType.LegacyArticle => "articles",
        SiteSearchContentType.Tribute or SiteSearchContentType.FreddieTribute => "tribute",
        _ => contentType,
    };

    public static string SectionLabel(string contentType) => contentType switch
    {
        SiteSearchContentType.News => "News",
        SiteSearchContentType.Forum => "Forum",
        SiteSearchContentType.Article or SiteSearchContentType.LegacyArticle => "Articles",
        SiteSearchContentType.Photo => "Photography",
        SiteSearchContentType.Timeline => "Timeline",
        SiteSearchContentType.Tribute or SiteSearchContentType.FreddieTribute => "Freddie Tribute",
        _ => contentType,
    };

    public static IReadOnlyList<SongRelatedSection> ForTitle(
        IEnumerable<SearchDocumentEntity> documents,
        string canonicalTitle)
    {
        return documents
            .Where(document =>
                IsRelatedType(document.ContentType)
                && string.Equals(document.Title, canonicalTitle, StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(document.Url))
            .GroupBy(document => SectionKey(document.ContentType), StringComparer.Ordinal)
            .OrderBy(group => SectionLabel(group.First().ContentType), StringComparer.OrdinalIgnoreCase)
            .Select(group => new SongRelatedSection(
                SectionLabel(group.First().ContentType),
                group
                    .Select(document => new SongRelatedLink(document.Title, document.Url, document.ContentType))
                    .ToList()))
            .Where(section => section.Links.Count > 0)
            .ToList();
    }
}

public sealed record SongRelatedSection(string Label, IReadOnlyList<SongRelatedLink> Links);

public sealed record SongRelatedLink(string Title, string Url, string ContentType);
