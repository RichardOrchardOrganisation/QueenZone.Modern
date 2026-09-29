using QueenZone.Data;

namespace QueenZone.Web.Search;

/// <summary>
/// Normalized anonymous search identity. The same values are used as the cache/inflight
/// key and as the arguments passed to the inner <see cref="ISiteSearchService"/>.
/// </summary>
internal readonly record struct SiteSearchCacheKey(
    string Query,
    string? ContentType,
    int Page,
    int PageSize)
{
    public static SiteSearchCacheKey Normalize(string query, string? contentType, int page, int pageSize)
    {
        var normalizedQuery = (query ?? string.Empty).Trim().ToLowerInvariant();
        var normalizedType = string.IsNullOrWhiteSpace(contentType)
            ? null
            : contentType.Trim().ToLowerInvariant();
        return new(
            normalizedQuery,
            normalizedType,
            SiteSearchLimits.NormalizePage(page),
            SiteSearchLimits.NormalizePageSize(pageSize));
    }
}
