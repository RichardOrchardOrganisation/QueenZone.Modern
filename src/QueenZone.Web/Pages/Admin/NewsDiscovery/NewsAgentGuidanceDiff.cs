namespace QueenZone.Web.Pages.Admin.NewsDiscovery;

public static class NewsAgentGuidanceDiff
{
    public static IReadOnlyList<NewsAgentGuidanceDiffLine> Compare(string? published, string? draft)
    {
        var left = SplitLines(published);
        var right = SplitLines(draft);
        var lines = new List<NewsAgentGuidanceDiffLine>();
        var max = Math.Max(left.Length, right.Length);
        for (var i = 0; i < max; i++)
        {
            var publishedLine = i < left.Length ? left[i] : null;
            var draftLine = i < right.Length ? right[i] : null;
            var kind = (publishedLine, draftLine) switch
            {
                _ when publishedLine == draftLine => NewsAgentGuidanceDiffKind.Unchanged,
                (null, _) => NewsAgentGuidanceDiffKind.Added,
                (_, null) => NewsAgentGuidanceDiffKind.Removed,
                _ => NewsAgentGuidanceDiffKind.Changed,
            };
            lines.Add(new NewsAgentGuidanceDiffLine(kind, publishedLine, draftLine));
        }

        return lines;
    }

    private static string[] SplitLines(string? value) =>
        string.IsNullOrEmpty(value)
            ? []
            : value.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
}

public enum NewsAgentGuidanceDiffKind
{
    Unchanged,
    Added,
    Removed,
    Changed
}

public sealed record NewsAgentGuidanceDiffLine(
    NewsAgentGuidanceDiffKind Kind,
    string? PublishedLine,
    string? DraftLine);
