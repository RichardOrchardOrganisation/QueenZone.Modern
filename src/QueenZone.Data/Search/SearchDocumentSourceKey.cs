using System.Globalization;

namespace QueenZone.Data;

/// <summary>
/// Parses and formats <see cref="Entities.SearchDocumentEntity.SourceKey"/> values written by the
/// search reindex builder (<c>news:123</c>, <c>forum-thread:4521</c>, <c>article:some-slug</c>, …).
/// </summary>
public static class SearchDocumentSourceKey
{
    public static string ForNews(int newsId) => $"news:{newsId}";

    public static string ForArticle(string slug) => $"article:{slug}";

    public static string ForBiography(int chapterId) => $"biography:{chapterId}";

    public static string ForForumThread(int topicId) => $"forum-thread:{topicId}";

    /// <summary>Stable identity for a Freddie tribute row, e.g. <c>tribute:187</c>.</summary>
    public static string ForTribute(int tributeId) => $"tribute:{tributeId}";

    /// <summary>
    /// True for tribute source keys, including the historical <c>freddie-tribute:</c> prefix.
    /// </summary>
    public static bool IsTribute(string? sourceKey)
    {
        if (string.IsNullOrWhiteSpace(sourceKey))
        {
            return false;
        }

        return sourceKey.StartsWith("tribute:", StringComparison.OrdinalIgnoreCase)
            || sourceKey.StartsWith("freddie-tribute:", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Returns the numeric id after the last colon when the suffix is an integer; otherwise
    /// <see langword="null"/> (slug keys such as <c>article:some-slug</c>).
    /// </summary>
    public static int? TryParseNumericId(string? sourceKey)
    {
        if (string.IsNullOrWhiteSpace(sourceKey))
        {
            return null;
        }

        var separator = sourceKey.LastIndexOf(':');
        if (separator < 0 || separator == sourceKey.Length - 1)
        {
            return null;
        }

        var suffix = sourceKey.AsSpan(separator + 1);
        return int.TryParse(suffix, NumberStyles.None, CultureInfo.InvariantCulture, out var id)
            ? id
            : null;
    }
}
