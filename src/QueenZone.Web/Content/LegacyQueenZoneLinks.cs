using System.Net;
using System.Text.RegularExpressions;
using AngleSharp.Dom;
using QueenZone.Routing;

namespace QueenZone.Web;

/// <summary>
/// Points links to the retired QueenZone Web Forms site (for example
/// <c>http://www.queenzone.com/queenzone/forumnew/forum_topic_view.aspx?Q=413200</c>) at the
/// modern routes that hold the same content. Stored bodies keep the original URL; the rewrite
/// only happens when a body is formatted for display.
/// </summary>
/// <remarks>
/// Forum topic and reply IDs go through <see cref="ForumRoutes.GetLegacyPostPath"/>, which looks
/// the ID up because a reply needs its topic, page and anchor. Forum category and news IDs are
/// the modern route IDs, so they link to a placeholder slug and the page redirects to its
/// canonical slug.
/// </remarks>
public static partial class LegacyQueenZoneLinks
{
    private static readonly HashSet<string> LegacyHosts = new(StringComparer.OrdinalIgnoreCase)
    {
        "queenzone.com",
        "www.queenzone.com",
        "queenzone.org",
        "www.queenzone.org",
    };

    /// <summary>Returns the modern site-relative path for a legacy QueenZone URL, or null.</summary>
    public static string? TryGetModernPath(string? href)
    {
        if (string.IsNullOrWhiteSpace(href)
            || !Uri.TryCreate(WebUtility.HtmlDecode(href.Trim()), UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            || !LegacyHosts.Contains(uri.Host))
        {
            return null;
        }

        var path = uri.AbsolutePath;
        var slugTopic = SlugTopicPath().Match(path);
        if (slugTopic.Success && int.TryParse(slugTopic.Groups[1].ValueSpan, out var slugTopicId))
        {
            return ForumRoutes.GetLegacyPostPath(slugTopicId);
        }

        var fileName = path[(path.LastIndexOf('/') + 1)..].ToLowerInvariant();
        var isForumFolder = path.Contains("/forum", StringComparison.OrdinalIgnoreCase);
        return fileName switch
        {
            "forum_topic_view.aspx" => QueryId(uri, "q") is { } topicId ? ForumRoutes.GetLegacyPostPath(topicId) : null,
            "forum_view.aspx" => QueryId(uri, "q") is { } forumId ? ForumRoutes.GetCategoryCanonicalPath(forumId, "forum") : null,
            "news_view.aspx" => QueryId(uri, "news_id") is { } newsId ? NewsRoutes.GetNewsDetailPath(newsId, "news") : null,
            "news.aspx" => NewsRoutes.GetArchiveCanonicalPath(1),
            "" or "default.aspx" when isForumFolder => "/forum",
            _ => null,
        };
    }

    /// <summary>
    /// Rewrites a legacy anchor to its modern path. Returns false (leaving the anchor untouched)
    /// when the href is not a legacy QueenZone URL.
    /// </summary>
    public static bool TryRewriteAnchor(IElement anchor)
    {
        var modern = TryGetModernPath(anchor.GetAttribute("href"));
        if (modern is null)
        {
            return false;
        }

        anchor.SetAttribute("href", modern);
        anchor.RemoveAttribute("target");
        anchor.RemoveAttribute("rel");
        return true;
    }

    /// <summary>
    /// Reads a numeric query value. Only leading digits count, because auto-linked plain text
    /// often runs into trailing punctuation or BBCode (<c>Q=307378[/QUOTE]</c>).
    /// </summary>
    private static int? QueryId(Uri uri, string key)
    {
        foreach (var pair in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = pair.IndexOf('=');
            if (separator <= 0 || !pair.AsSpan(0, separator).Equals(key, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var value = pair.AsSpan(separator + 1);
            var digits = 0;
            while (digits < value.Length && char.IsAsciiDigit(value[digits]))
            {
                digits++;
            }

            return int.TryParse(value[..digits], out var id) && id > 0 ? id : null;
        }

        return null;
    }

    /// <summary>Later legacy shape: <c>/forums/{topicId}/{slug}.aspx</c>.</summary>
    [GeneratedRegex(@"^/forums/(\d+)/[^/]+\.aspx", RegexOptions.IgnoreCase)]
    private static partial Regex SlugTopicPath();
}
