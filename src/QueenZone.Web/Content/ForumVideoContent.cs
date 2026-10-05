using System.Collections.Frozen;
using System.Globalization;
using System.Text.RegularExpressions;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using Microsoft.AspNetCore.WebUtilities;

namespace QueenZone.Web;

/// <summary>A trusted video mapped to a zero-based anchor in the sanitized post body.</summary>
public sealed record ForumYoutubeVideo(string Provider, string VideoId, string WatchUrl, int? StartSeconds, int AnchorIndex);

public sealed record ForumVideoProjection(string Body, IReadOnlyList<ForumYoutubeVideo> YoutubeVideos);

/// <summary>Forum-only, local display projection. Never changes storage or the shared UGC sanitizer.</summary>
public static partial class ForumVideoContent
{
    public const int MaximumVideosPerPost = 3;
    public const int MaximumStartSeconds = 86_400;

    public static ForumVideoProjection Project(string? body, UgcHtml ugcHtml)
    {
        var sanitized = ugcHtml.FormatForDisplay(body);
        var document = new HtmlParser().ParseDocument(sanitized);
        var anchors = document.Body!.QuerySelectorAll("a");
        var eligible = new HashSet<IElement>();
        CollectEligible(document.Body, eligible);
        var seen = new HashSet<(string, int?)>();
        var videos = new List<ForumYoutubeVideo>();
        for (var index = 0; index < anchors.Length && videos.Count < MaximumVideosPerPost; index++)
        {
            var anchor = anchors[index];
            if (eligible.Contains(anchor)
                && TryRecognize(anchor.GetAttribute("href"), index) is { } video
                && seen.Add((video.VideoId, video.StartSeconds)))
            {
                videos.Add(video);
            }
        }

        return new(sanitized, videos);
    }

    public static ForumYoutubeVideo? TryRecognize(string? url, int anchorIndex = 0)
    {
        var uri = TryParseSafeUrl(url);
        if (uri is null)
        {
            return null;
        }

        var shortHost = uri.Host is "youtu.be" or "www.youtu.be";
        if (!shortHost && uri.Host is not ("youtube.com" or "www.youtube.com" or "m.youtube.com"))
        {
            return null;
        }

        var query = QueryHelpers.ParseQuery(uri.Query);
        var id = ReadVideoId(uri, shortHost, query);

        if (id is null || !VideoIdPattern().IsMatch(id))
        {
            return null;
        }

        int? start = null;
        var times = new[] { "t", "start" }.Where(query.ContainsKey).SelectMany(key => query[key]).ToArray();
        if (times.Length == 1)
        {
            start = ParseStart(times[0]);
        }

        // Zero seconds is the default and deduplicates with a URL without a start time.
        if (start == 0)
        {
            start = null;
        }

        var watchUrl = $"https://www.youtube.com/watch?v={id}";
        if (start is int seconds)
        {
            watchUrl += "&t=" + seconds.ToString(CultureInfo.InvariantCulture) + "s";
        }

        return new("youtube", id, watchUrl, start, anchorIndex);
    }

    // Uri normalizes escapes, dot segments, backslashes and control characters. Reject those
    // spellings first so normalization cannot turn hostile input into an approved URL.
    private static bool HasUnsafeUrlSpelling(string? url) =>
        string.IsNullOrEmpty(url) || url.Any(char.IsWhiteSpace) || url.Any(char.IsControl)
            || url.Contains('\\') || url.Contains('%')
            || url.Contains("/../", StringComparison.Ordinal) || url.Contains("/./", StringComparison.Ordinal);

    private static Uri? TryParseSafeUrl(string? url)
    {
        if (HasUnsafeUrlSpelling(url) || !Uri.TryCreate(url, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            || !uri.IsDefaultPort || uri.UserInfo.Length != 0)
        {
            return null;
        }
        return uri;
    }

    private static string? ReadVideoId(Uri uri, bool shortHost, Dictionary<string, Microsoft.Extensions.Primitives.StringValues> query)
    {
        string? id;
        if (!shortHost && uri.AbsolutePath == "/watch")
        {
            if (!query.TryGetValue("v", out var ids) || ids.Count != 1)
            {
                return null;
            }

            id = ids[0];
        }
        else
        {
            // Path IDs plus a query ID are ambiguous even when equal.
            if (query.ContainsKey("v"))
            {
                return null;
            }

            var segments = uri.AbsolutePath.Split('/');
            if (shortHost && segments.Length == 2)
            {
                id = segments[1];
            }
            else if (!shortHost && segments.Length == 3 && segments[1] is "shorts" or "embed")
            {
                id = segments[2];
            }
            else
            {
                id = null;
            }
        }

        return id;
    }

    private static int? ParseStart(string? value)
    {
        if (value is null || value.Length > 20)
        {
            return null;
        }

        if (SecondsPattern().IsMatch(value)
            && int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var seconds))
        {
            return seconds <= MaximumStartSeconds ? seconds : null;
        }

        var match = DurationPattern().Match(value);
        if (!match.Success || value.Length == 0)
        {
            return null;
        }

        long total = 0;
        foreach (var (name, multiplier) in new[] { ("h", 3600), ("m", 60), ("s", 1) })
        {
            var group = match.Groups[name];
            if (group.Success)
            {
                if (!int.TryParse(group.Value, NumberStyles.None, CultureInfo.InvariantCulture, out var component))
                {
                    return null;
                }

                total += (long)component * multiplier;
            }
        }

        return total <= MaximumStartSeconds ? (int)total : null;
    }

    private static void CollectEligible(IElement container, HashSet<IElement> eligible)
    {
        // Meaningful tokens are isolated by block and line boundaries. Formatting wrappers are
        // transparent; headings, lists, images and quotes are never treated as standalone links.
        var line = new List<INode>();
        foreach (var child in container.ChildNodes)
        {
            VisitEligibleNode(child, line, eligible);
        }

        FlushEligibleLine(line, eligible);
    }

    private static void FlushEligibleLine(List<INode> line, HashSet<IElement> eligible)
    {
        if (line.Count == 1 && line[0] is IElement { LocalName: "a" } anchor
            && !anchor.QuerySelectorAll("img, br").Any())
        {
            eligible.Add(anchor);
        }

        line.Clear();
    }

    private static readonly FrozenSet<string> BlockBoundaryTags =
        new[] { "br", "blockquote", "pre", "ul", "ol", "li", "h2", "h3", "h4" }.ToFrozenSet(StringComparer.Ordinal);

    private static readonly FrozenSet<string> TransparentFormattingTags =
        new[] { "span", "strong", "b", "em", "i", "u" }.ToFrozenSet(StringComparer.Ordinal);

    private static void VisitEligibleNode(INode node, List<INode> line, HashSet<IElement> eligible)
    {
        if (node is IText text)
        {
            if (!string.IsNullOrWhiteSpace(text.Data))
            {
                line.Add(node);
            }

            return;
        }

        if (node is not IElement element)
        {
            return;
        }

        if (element.LocalName is "p" or "div")
        {
            FlushEligibleLine(line, eligible);
            if (!element.ClassList.Contains("qz-bbcode-quote"))
            {
                CollectEligible(element, eligible);
            }

            return;
        }

        if (BlockBoundaryTags.Contains(element.LocalName))
        {
            FlushEligibleLine(line, eligible);
        }
        else if (TransparentFormattingTags.Contains(element.LocalName))
        {
            VisitFormattingChildren(element, line, eligible);
        }
        else
        {
            line.Add(element);
        }
    }

    private static void VisitFormattingChildren(IElement element, List<INode> line, HashSet<IElement> eligible)
    {
        if (element.ClassList.Contains("qz-bbcode-quote"))
        {
            line.Add(element);
            return;
        }

        foreach (var child in element.ChildNodes)
        {
            VisitEligibleNode(child, line, eligible);
        }
    }

    [GeneratedRegex("\\A[A-Za-z0-9_-]{11}\\z", RegexOptions.CultureInvariant)]
    private static partial Regex VideoIdPattern();

    [GeneratedRegex("\\A[0-9]+\\z", RegexOptions.CultureInvariant)]
    private static partial Regex SecondsPattern();

    [GeneratedRegex("\\A(?:(?<h>[0-9]+)h)?(?:(?<m>[0-9]+)m)?(?:(?<s>[0-9]+)s)?\\z", RegexOptions.CultureInvariant)]
    private static partial Regex DurationPattern();
}
