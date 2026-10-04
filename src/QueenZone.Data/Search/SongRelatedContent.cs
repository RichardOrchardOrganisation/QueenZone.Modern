using System.Collections.Frozen;
using QueenZone.Data.Entities;

namespace QueenZone.Data;

/// <summary>
/// Related blocks on a song page: search-document <em>title</em> equals the canonical song
/// title (ordinal ignore-case). No body match and no link table.
/// </summary>
public static class SongRelatedContent
{
    private static readonly FrozenSet<string> RelatedTypes = FrozenSet.ToFrozenSet(
        [
            SiteSearchContentType.Forum,
            SiteSearchContentType.News,
            SiteSearchContentType.Article,
            SiteSearchContentType.LegacyArticle,
            SiteSearchContentType.Photo,
            SiteSearchContentType.Timeline,
            SiteSearchContentType.Tribute,
            SiteSearchContentType.FreddieTribute,
        ],
        StringComparer.Ordinal);

    private static readonly FrozenDictionary<string, string> SectionKeys = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        [SiteSearchContentType.Article] = "articles",
        [SiteSearchContentType.LegacyArticle] = "articles",
        [SiteSearchContentType.Tribute] = "tribute",
        [SiteSearchContentType.FreddieTribute] = "tribute",
    }.ToFrozenDictionary(StringComparer.Ordinal);

    private static readonly FrozenDictionary<string, string> SectionLabels = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        [SiteSearchContentType.News] = "News",
        [SiteSearchContentType.Forum] = "Forum",
        [SiteSearchContentType.Article] = "Articles",
        [SiteSearchContentType.LegacyArticle] = "Articles",
        [SiteSearchContentType.Photo] = "Photography",
        [SiteSearchContentType.Timeline] = "Timeline",
        [SiteSearchContentType.Tribute] = "Freddie Tribute",
        [SiteSearchContentType.FreddieTribute] = "Freddie Tribute",
    }.ToFrozenDictionary(StringComparer.Ordinal);

    public static bool IsRelatedType(string? contentType) =>
        contentType is not null && RelatedTypes.Contains(contentType);

    public static string SectionKey(string contentType) =>
        SectionKeys.TryGetValue(contentType, out var key) ? key : contentType;

    public static string SectionLabel(string contentType) =>
        SectionLabels.TryGetValue(contentType, out var label) ? label : contentType;

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
