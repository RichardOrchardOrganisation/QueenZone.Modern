using System.Net;
using System.Text.RegularExpressions;
using QueenZone.Data;

namespace QueenZone.NewsAgent;

public sealed record ParsedArticlePage(
    string Title,
    string? Excerpt,
    string SourceName,
    IReadOnlyList<ParsedArticleMediaLink> MediaLinks);

public sealed record ParsedArticleMediaLink(
    string Label,
    string Url);

public static partial class NewsArticlePageParser
{
    private const int MaxExcerptLength = 1200;
    private const int MaxMediaLinks = 5;
    private const int MaxMediaUrlLength = 500;

    public static ParsedArticlePage Parse(string html, string pageUrl)
    {
        ArgumentNullException.ThrowIfNull(html);

        var title = FirstMatch(OgTitleRegex(), html)
            ?? FirstMatch(TwitterTitleRegex(), html)
            ?? FirstMatch(TitleTagRegex(), html)
            ?? DeriveTitleFromUrl(pageUrl);

        var excerpt = FirstMatch(MetaDescriptionRegex(), html)
            ?? FirstMatch(OgDescriptionRegex(), html)
            ?? FirstMatch(TwitterDescriptionRegex(), html);

        if (!string.IsNullOrWhiteSpace(excerpt))
        {
            excerpt = NormalizeWhitespace(Decode(excerpt));
            if (excerpt.Length > MaxExcerptLength)
            {
                excerpt = excerpt[..MaxExcerptLength].TrimEnd() + "…";
            }
        }

        var sourceName = Uri.TryCreate(pageUrl, UriKind.Absolute, out var uri)
            ? uri.Host
            : "Manual URL";

        return new ParsedArticlePage(
            NormalizeWhitespace(Decode(title)),
            string.IsNullOrWhiteSpace(excerpt) ? null : excerpt,
            sourceName,
            ExtractMediaLinks(html, pageUrl));
    }

    public static string? BuildEvidenceExcerpt(ParsedArticlePage page)
    {
        ArgumentNullException.ThrowIfNull(page);

        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(page.Excerpt))
        {
            parts.Add(page.Excerpt);
        }

        if (page.MediaLinks.Count > 0)
        {
            parts.Add("Direct media links supplied by the source:\n" + string.Join(
                '\n',
                page.MediaLinks.Select(link => $"- {link.Label}: {link.Url}")));
        }

        return parts.Count == 0 ? null : string.Join("\n\n", parts);
    }

    private static IReadOnlyList<ParsedArticleMediaLink> ExtractMediaLinks(string html, string pageUrl)
    {
        Uri.TryCreate(pageUrl, UriKind.Absolute, out var pageUri);
        var links = new List<ParsedArticleMediaLink>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (Match match in AnchorTagRegex().Matches(html))
        {
            var href = FirstMatch(HrefAttributeRegex(), match.Groups["attributes"].Value);
            var nearbyText = GetNearbyText(html, match.Index, match.Length);
            AddMediaLink(links, seen, pageUri, href, nearbyText);

            if (links.Count >= MaxMediaLinks)
            {
                return links;
            }
        }

        foreach (Match match in IframeTagRegex().Matches(html))
        {
            var src = FirstMatch(SrcAttributeRegex(), match.Groups["attributes"].Value);
            var title = FirstMatch(TitleAttributeRegex(), match.Groups["attributes"].Value);
            AddMediaLink(links, seen, pageUri, src, title ?? "Watch the video", iframe: true);

            if (links.Count >= MaxMediaLinks)
            {
                break;
            }
        }

        return links;
    }

    private static void AddMediaLink(
        List<ParsedArticleMediaLink> links,
        HashSet<string> seen,
        Uri? pageUri,
        string? value,
        string context,
        bool iframe = false)
    {
        if (string.IsNullOrWhiteSpace(value)
            || !Uri.TryCreate(pageUri, Decode(value), out var uri)
            || !OutboundUrlSafety.TryValidatePublicHttpUrl(uri.AbsoluteUri, out _, out var safeUrl)
            || string.IsNullOrWhiteSpace(safeUrl))
        {
            return;
        }

        var normalizedContext = NormalizeWhitespace(Decode(HtmlTags.Pattern().Replace(context, " ")));
        if ((iframe && !IsVideoHost(uri.Host))
            || (!iframe && !IsMediaLink(uri, normalizedContext)))
        {
            return;
        }

        var normalizedUrl = NormalizeMediaUrl(uri);
        if (normalizedUrl.Length > MaxMediaUrlLength || !seen.Add(normalizedUrl))
        {
            return;
        }

        links.Add(new ParsedArticleMediaLink(BuildMediaLabel(uri, normalizedContext), normalizedUrl));
    }

    private static bool IsMediaLink(Uri uri, string context)
    {
        var host = uri.Host;
        var hasMediaContext = ContainsAny(
            context,
            "listen",
            "hear",
            "watch",
            "stream",
            "song",
            "single",
            "video",
            "audio");

        if (IsVideoHost(host))
        {
            return hasMediaContext || IsDirectVideoUrl(uri);
        }

        return (IsStreamingHost(host) || IsSmartLinkHost(host)) && hasMediaContext;
    }

    private static bool IsDirectVideoUrl(Uri uri) =>
        uri.Host.Equals("youtu.be", StringComparison.OrdinalIgnoreCase)
        || uri.AbsolutePath.StartsWith("/watch", StringComparison.OrdinalIgnoreCase)
        || uri.AbsolutePath.StartsWith("/embed/", StringComparison.OrdinalIgnoreCase)
        || (uri.Host.EndsWith("vimeo.com", StringComparison.OrdinalIgnoreCase)
            && uri.AbsolutePath.Trim('/').All(char.IsDigit));

    private static string BuildMediaLabel(Uri uri, string context)
    {
        if (IsVideoHost(uri.Host) || ContainsAny(context, "watch", "video"))
        {
            return "Watch the video";
        }

        return "Listen to the song";
    }

    private static string NormalizeMediaUrl(Uri uri)
    {
        if (uri.Host.EndsWith("youtube.com", StringComparison.OrdinalIgnoreCase)
            && uri.AbsolutePath.StartsWith("/embed/", StringComparison.OrdinalIgnoreCase))
        {
            var videoId = uri.AbsolutePath["/embed/".Length..]
                .Split('/', StringSplitOptions.RemoveEmptyEntries)
                .FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(videoId))
            {
                return $"https://www.youtube.com/watch?v={Uri.EscapeDataString(videoId)}";
            }
        }

        return uri.AbsoluteUri;
    }

    private static string GetNearbyText(string html, int matchIndex, int matchLength)
    {
        var paragraphStart = html.LastIndexOf("<p", matchIndex, StringComparison.OrdinalIgnoreCase);
        var previousParagraphEnd = html.LastIndexOf("</p>", matchIndex, StringComparison.OrdinalIgnoreCase);
        var paragraphEnd = html.IndexOf("</p>", matchIndex + matchLength, StringComparison.OrdinalIgnoreCase);
        if (paragraphStart > previousParagraphEnd && paragraphEnd >= matchIndex)
        {
            return html[paragraphStart..Math.Min(html.Length, paragraphEnd + "</p>".Length)];
        }

        const int contextLength = 180;
        var start = Math.Max(0, matchIndex - contextLength);
        var end = Math.Min(html.Length, matchIndex + matchLength + contextLength);
        return html[start..end];
    }

    private static bool IsVideoHost(string host) =>
        host.Equals("youtu.be", StringComparison.OrdinalIgnoreCase)
        || host.EndsWith(".youtube.com", StringComparison.OrdinalIgnoreCase)
        || host.Equals("youtube.com", StringComparison.OrdinalIgnoreCase)
        || host.EndsWith(".vimeo.com", StringComparison.OrdinalIgnoreCase)
        || host.Equals("vimeo.com", StringComparison.OrdinalIgnoreCase);

    private static bool IsStreamingHost(string host) =>
        host.EndsWith(".spotify.com", StringComparison.OrdinalIgnoreCase)
        || host.Equals("spotify.com", StringComparison.OrdinalIgnoreCase)
        || host.Equals("music.apple.com", StringComparison.OrdinalIgnoreCase)
        || host.EndsWith(".soundcloud.com", StringComparison.OrdinalIgnoreCase)
        || host.Equals("soundcloud.com", StringComparison.OrdinalIgnoreCase)
        || host.EndsWith(".bandcamp.com", StringComparison.OrdinalIgnoreCase)
        || host.Equals("bandcamp.com", StringComparison.OrdinalIgnoreCase)
        || host.EndsWith(".tidal.com", StringComparison.OrdinalIgnoreCase)
        || host.Equals("tidal.com", StringComparison.OrdinalIgnoreCase)
        || host.EndsWith(".deezer.com", StringComparison.OrdinalIgnoreCase)
        || host.Equals("deezer.com", StringComparison.OrdinalIgnoreCase);

    private static bool IsSmartLinkHost(string host) =>
        host.EndsWith(".lnk.to", StringComparison.OrdinalIgnoreCase)
        || host.Equals("lnk.to", StringComparison.OrdinalIgnoreCase)
        || host.EndsWith(".ffm.to", StringComparison.OrdinalIgnoreCase)
        || host.Equals("ffm.to", StringComparison.OrdinalIgnoreCase)
        || host.EndsWith(".smarturl.it", StringComparison.OrdinalIgnoreCase)
        || host.Equals("smarturl.it", StringComparison.OrdinalIgnoreCase);

    private static bool ContainsAny(string value, params string[] terms) =>
        terms.Any(term => value.Contains(term, StringComparison.OrdinalIgnoreCase));

    private static string DeriveTitleFromUrl(string pageUrl)
    {
        if (!Uri.TryCreate(pageUrl, UriKind.Absolute, out var uri))
        {
            return "Submitted article";
        }

        var segment = uri.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries).LastOrDefault();
        if (string.IsNullOrWhiteSpace(segment))
        {
            return uri.Host;
        }

        return segment.Replace('-', ' ').Replace('_', ' ');
    }

    private static string? FirstMatch(Regex regex, string html)
    {
        var match = regex.Match(html);
        if (!match.Success)
        {
            return null;
        }

        var value = match.Groups["value"].Value;
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static string Decode(string value) =>
        WebUtility.HtmlDecode(value);

    private static string NormalizeWhitespace(string value) =>
        string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    [GeneratedRegex(@"<title[^>]*>(?<value>[\s\S]*?)</title>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex TitleTagRegex();

    [GeneratedRegex(
        @"<meta\s+[^>]*property\s*=\s*[""']og:title[""'][^>]*content\s*=\s*(?<delimiter>[""'])(?<value>[\s\S]*?)\k<delimiter>",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex OgTitleRegex();

    [GeneratedRegex(
        @"<meta\s+[^>]*name\s*=\s*[""']twitter:title[""'][^>]*content\s*=\s*(?<delimiter>[""'])(?<value>[\s\S]*?)\k<delimiter>",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex TwitterTitleRegex();

    [GeneratedRegex(
        @"<meta\s+[^>]*name\s*=\s*[""']description[""'][^>]*content\s*=\s*(?<delimiter>[""'])(?<value>[\s\S]*?)\k<delimiter>",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex MetaDescriptionRegex();

    [GeneratedRegex(
        @"<meta\s+[^>]*property\s*=\s*[""']og:description[""'][^>]*content\s*=\s*(?<delimiter>[""'])(?<value>[\s\S]*?)\k<delimiter>",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex OgDescriptionRegex();

    [GeneratedRegex(
        @"<meta\s+[^>]*name\s*=\s*[""']twitter:description[""'][^>]*content\s*=\s*(?<delimiter>[""'])(?<value>[\s\S]*?)\k<delimiter>",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex TwitterDescriptionRegex();

    [GeneratedRegex(@"<a\b(?<attributes>[^>]*)>[\s\S]*?</a>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex AnchorTagRegex();

    [GeneratedRegex(@"<iframe\b(?<attributes>[^>]*)>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex IframeTagRegex();

    [GeneratedRegex(@"\bhref\s*=\s*(?<delimiter>[""'])(?<value>[\s\S]*?)\k<delimiter>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex HrefAttributeRegex();

    [GeneratedRegex(@"\bsrc\s*=\s*(?<delimiter>[""'])(?<value>[\s\S]*?)\k<delimiter>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SrcAttributeRegex();

    [GeneratedRegex(@"\btitle\s*=\s*(?<delimiter>[""'])(?<value>[\s\S]*?)\k<delimiter>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex TitleAttributeRegex();
}
