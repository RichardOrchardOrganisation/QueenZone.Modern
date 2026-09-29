namespace QueenZone.Data;

/// <summary>
/// Substring-match search over the in-memory index store, used in the "Testing" environment and
/// local no-database dev where SQL Server full-text search isn't available. Title matches rank
/// above body-only matches, then results fall back to publish date — this is a reasonable stand-in
/// for FTS rank, not an attempt to replicate it.
/// </summary>
public sealed class InMemorySiteSearchService(SharedSearchIndexStore store) : ISiteSearchService
{
    private const int MaxPageSize = 100;

    public Task<SiteSearchPage> SearchAsync(
        string query,
        string? contentType,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        if (SiteSearchLimits.IsBelowMinimumLength(query))
        {
            return Task.FromResult(new SiteSearchPage([], 0, page, pageSize));
        }

        var normalizedPage = Math.Max(page, 1);
        var take = Math.Clamp(pageSize, 1, MaxPageSize);
        var trimmedQuery = query.Trim();

        var matches = store.GetAll()
            .Where(document => !SiteSearchContentType.IsExcludedFromSiteSearch(document.ContentType)
                && !SearchDocumentSourceKey.IsTribute(document.SourceKey))
            .Where(document => contentType is null || document.ContentType == contentType)
            .Select(document => new
            {
                Document = document,
                TitleMatch = document.Title.Contains(trimmedQuery, StringComparison.OrdinalIgnoreCase),
                BodyMatch = document.Body.Contains(trimmedQuery, StringComparison.OrdinalIgnoreCase)
                    || (document.Summary?.Contains(trimmedQuery, StringComparison.OrdinalIgnoreCase) ?? false),
            })
            .Where(candidate => candidate.TitleMatch || candidate.BodyMatch)
            .OrderByDescending(candidate => candidate.TitleMatch)
            .ThenByDescending(candidate => candidate.Document.PublishedAt)
            .Select(candidate => candidate.Document)
            .ToList();

        var totalCount = matches.Count;
        var pageItems = matches
            .Skip((normalizedPage - 1) * take)
            .Take(take)
            .Select(Map)
            .ToList();

        return Task.FromResult(new SiteSearchPage(pageItems, totalCount, normalizedPage, take));
    }

    internal static SiteSearchResult Map(Entities.SearchDocumentEntity document) =>
        new(
            document.ContentType,
            document.SourceKey,
            document.Title,
            document.Summary ?? string.Empty,
            document.Url,
            document.PublishedAt,
            document.ImageUrl,
            document.Category,
            document.AuthorDisplayName);
}
