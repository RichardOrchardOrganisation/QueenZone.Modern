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
        // Uri normalizes escapes, dot segments, backslashes and control characters. Reject those
        // spellings before parsing so normalization cannot turn hostile input into an approved URL.
        if (string.IsNullOrEmpty(url) || url.Any(char.IsWhiteSpace) || url.Any(char.IsControl)
            || url.Contains('\\') || url.Contains('%')
            || url.Contains("/../", StringComparison.Ordinal) || url.Contains("/./", StringComparison.Ordinal)
            || !Uri.TryCreate(url, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            || !uri.IsDefaultPort || uri.UserInfo.Length != 0)
        {
            return null;
        }

        var shortHost = uri.Host is "youtu.be" or "www.youtu.be";
        if (!shortHost && uri.Host is not ("youtube.com" or "www.youtube.com" or "m.youtube.com"))
        {
            return null;
        }

        var query = QueryHelpers.ParseQuery(uri.Query);
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
        void Flush()
        {
            if (line.Count == 1 && line[0] is IElement { LocalName: "a" } anchor
                && !anchor.QuerySelectorAll("img, br").Any())
            {
                eligible.Add(anchor);
            }

            line.Clear();
        }

        void Visit(INode node)
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
                Flush();
                if (!element.ClassList.Contains("qz-bbcode-quote"))
                {
                    CollectEligible(element, eligible);
                }

                return;
            }

            if (element.LocalName is "br" or "blockquote" or "pre" or "ul" or "ol" or "li" or "h2" or "h3" or "h4")
            {
                Flush();
            }
            else if (element.LocalName is "span" or "strong" or "b" or "em" or "i" or "u")
            {
                if (element.ClassList.Contains("qz-bbcode-quote"))
                {
                    line.Add(element);
                    return;
                }

                foreach (var child in element.ChildNodes)
                {
                    Visit(child);
                }
            }
            else
            {
                line.Add(element);
            }
        }

        foreach (var child in container.ChildNodes)
        {
            Visit(child);
        }

        Flush();
    }

    [GeneratedRegex("\\A[A-Za-z0-9_-]{11}\\z", RegexOptions.CultureInvariant)]
    private static partial Regex VideoIdPattern();

    [GeneratedRegex("\\A[0-9]+\\z", RegexOptions.CultureInvariant)]
    private static partial Regex SecondsPattern();

    [GeneratedRegex("\\A(?:(?<h>[0-9]+)h)?(?:(?<m>[0-9]+)m)?(?:(?<s>[0-9]+)s)?\\z", RegexOptions.CultureInvariant)]
    private static partial Regex DurationPattern();
}
