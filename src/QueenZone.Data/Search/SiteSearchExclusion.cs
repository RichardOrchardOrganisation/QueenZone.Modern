namespace QueenZone.Data;

/// <summary>
/// Single rule for rows that must never appear in site search: tribute content types
/// (including the historical <c>freddie-tribute</c> alias) and the matching source-key
/// prefixes. Used by the indexer, leftover cleanup SQL, and
/// <c>dbo.SearchDocument_Search</c>.
/// </summary>
public static class SiteSearchExclusion
{
    public static readonly IReadOnlyList<string> ContentTypes =
    [
        SiteSearchContentType.Tribute,
        SiteSearchContentType.FreddieTribute,
    ];

    public static readonly IReadOnlyList<string> SourceKeyPrefixes =
        ContentTypes.Select(type => type + ":").ToArray();

    public static bool IsExcluded(string? contentType, string? sourceKey) =>
        IsExcludedContentType(contentType) || IsExcludedSourceKey(sourceKey);

    public static bool IsExcludedContentType(string? contentType)
    {
        if (string.IsNullOrWhiteSpace(contentType))
        {
            return false;
        }

        var trimmed = contentType.Trim();
        foreach (var candidate in ContentTypes)
        {
            if (string.Equals(candidate, trimmed, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    public static bool IsExcludedSourceKey(string? sourceKey)
    {
        if (string.IsNullOrWhiteSpace(sourceKey))
        {
            return false;
        }

        foreach (var prefix in SourceKeyPrefixes)
        {
            if (sourceKey.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// SQL predicate matching leftover tribute rows (migration DELETE).
    /// </summary>
    public static string SqlIsExcluded(string contentTypeColumn, string sourceKeyColumn)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contentTypeColumn);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceKeyColumn);

        var types = string.Join(", ", ContentTypes.Select(SqlNvarcharLiteral));
        var likes = string.Join(
            " OR ",
            SourceKeyPrefixes.Select(prefix => $"{sourceKeyColumn} LIKE {SqlNvarcharLiteral(prefix + "%")}"));
        return $"{contentTypeColumn} IN ({types}) OR {likes}";
    }

    /// <summary>
    /// SQL predicate for rows allowed into <c>#Matches</c>.
    /// </summary>
    public static string SqlIsSearchable(string tableAlias)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tableAlias);
        return $"NOT ({SqlIsExcluded($"{tableAlias}.ContentType", $"{tableAlias}.SourceKey")})";
    }

    private static string SqlNvarcharLiteral(string value) =>
        $"N'{value.Replace("'", "''", StringComparison.Ordinal)}'";
}
