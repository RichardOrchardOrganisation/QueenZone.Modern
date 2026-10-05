using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml;
using QueenZone.Data;

namespace QueenZone.NewsAgent;

public static partial class NewsFeedParser
{
    public static IReadOnlyList<FetchedNewsItem> Parse(string feedXml)
    {
        if (feedXml.Contains("<feed", StringComparison.OrdinalIgnoreCase))
        {
            return ReadFeedItems(feedXml, "entry", ReadEntry);
        }

        return ReadFeedItems(feedXml, "item", ReadItem);
    }

    public static IReadOnlyList<FetchedNewsItem> ParseSitemap(string sitemapXml, int maxItems = 50)
    {
        var items = new List<FetchedNewsItem>();
        using var reader = XmlReader.Create(new StringReader(sitemapXml), new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit });
        while (reader.Read())
        {
            if (reader.NodeType != XmlNodeType.Element || reader.LocalName != "url")
            {
                continue;
            }

            var (location, lastModified) = ReadSitemapLocation(reader);

            if (string.IsNullOrWhiteSpace(location) || !NewsDiscoveryUrlFilter.IsLikelyArticleUrl(location))
            {
                continue;
            }

            items.Add(new FetchedNewsItem(
                location,
                BuildTitleFromUrl(location),
                lastModified,
                null));

            if (items.Count >= maxItems)
            {
                break;
            }
        }

        return items;
    }

    private static (string? Location, DateTime? LastModified) ReadSitemapLocation(XmlReader reader)
    {
        string? location = null;
        DateTime? lastModified = null;
        using var subtree = reader.ReadSubtree();
        while (subtree.Read())
        {
            if (subtree.NodeType != XmlNodeType.Element)
            {
                continue;
            }

            if (subtree.LocalName == "loc")
            {
                location = subtree.ReadElementContentAsString().Trim();
            }
            else if (subtree.LocalName == "lastmod")
            {
                var value = subtree.ReadElementContentAsString().Trim();
                if (DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed))
                {
                    lastModified = parsed;
                }
            }
        }
        return (location, lastModified);
    }

    public static IReadOnlyList<FetchedNewsItem> ParseAllowlistedPageLinks(
        string html,
        Uri pageUri,
        int maxItems = 50)
    {
        var items = new List<FetchedNewsItem>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var pagePath = pageUri.AbsolutePath.TrimEnd('/');
        var pageFileName = Path.GetFileName(pagePath);
        var isPhpListingPage = pageFileName.EndsWith(".php", StringComparison.OrdinalIgnoreCase);

        foreach (Match match in HrefRegex().Matches(html))
        {
            var absoluteUri = ResolveAllowlistedLink(match.Groups[1].Value, pageUri, pagePath, pageFileName, isPhpListingPage);
            if (absoluteUri is null)
            {
                continue;
            }

            if (absoluteUri.AbsolutePath.Length < 2
                || seen.Contains(absoluteUri.AbsoluteUri)
                || !NewsDiscoveryUrlFilter.IsLikelyArticleUrl(absoluteUri.AbsoluteUri))
            {
                continue;
            }

            seen.Add(absoluteUri.AbsoluteUri);
            items.Add(new FetchedNewsItem(
                absoluteUri.AbsoluteUri,
                BuildTitleFromUrl(absoluteUri.AbsoluteUri),
                null,
                null));

            if (items.Count >= maxItems)
            {
                break;
            }
        }

        return items;
    }

    private static Uri? ResolveAllowlistedLink(string rawHref, Uri pageUri, string pagePath, string pageFileName, bool isPhpListingPage)
    {
        var href = rawHref.Trim();
        if (string.IsNullOrWhiteSpace(href) || href.StartsWith('#') || href.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (!Uri.TryCreate(pageUri, href, out var absoluteUri))
        {
            return null;
        }

        if (!string.Equals(absoluteUri.Host, pageUri.Host, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (isPhpListingPage)
        {
            var listingStem = Path.GetFileNameWithoutExtension(pageFileName);
            var expectedDetailPath = "/" + listingStem + "_detail.php";
            if (!absoluteUri.AbsolutePath.Equals(expectedDetailPath, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }
        }
        else if (pagePath.Length > 1)
        {
            var linkPath = absoluteUri.AbsolutePath.TrimEnd('/');
            if (!linkPath.StartsWith(pagePath + "/", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }
        }
        return absoluteUri;
    }

    private static IReadOnlyList<FetchedNewsItem> ReadFeedItems(string feedXml, string elementName, Func<XmlReader, FetchedNewsItem?> readItem)
    {
        var items = new List<FetchedNewsItem>();
        using var reader = XmlReader.Create(new StringReader(feedXml), new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit });
        while (reader.Read())
        {
            if (reader.NodeType != XmlNodeType.Element || reader.LocalName != elementName)
            {
                continue;
            }

            var item = readItem(reader);
            if (item is not null)
            {
                items.Add(item);
            }
        }

        return items;
    }

    private static FetchedNewsItem? ReadItem(XmlReader reader)
    {
        string? link = null;
        string? title = null;
        string? description = null;
        DateTime? publishedAt = null;
        using var subtree = reader.ReadSubtree();
        while (subtree.Read())
        {
            if (subtree.NodeType != XmlNodeType.Element)
            {
                continue;
            }

            switch (subtree.LocalName)
            {
                case "link":
                    link = subtree.ReadElementContentAsString().Trim();
                    break;
                case "title":
                    title = subtree.ReadElementContentAsString().Trim();
                    break;
                case "description":
                    description = StripHtml(subtree.ReadElementContentAsString().Trim());
                    break;
                case "pubDate":
                    if (TryParseFeedDate(subtree.ReadElementContentAsString().Trim(), out var parsedPubDate))
                    {
                        publishedAt = parsedPubDate;
                    }

                    break;
            }
        }

        if (string.IsNullOrWhiteSpace(link)
            || string.IsNullOrWhiteSpace(title)
            || !NewsDiscoveryUrlFilter.IsLikelyArticleUrl(link))
        {
            return null;
        }

        return new FetchedNewsItem(link, title, publishedAt, TruncateExcerpt(description));
    }

    private static FetchedNewsItem? ReadEntry(XmlReader reader)
    {
        string? link = null;
        string? title = null;
        string? summary = null;
        DateTime? publishedAt = null;
        using var subtree = reader.ReadSubtree();
        while (subtree.Read())
        {
            if (subtree.NodeType != XmlNodeType.Element)
            {
                continue;
            }

            switch (subtree.LocalName)
            {
                case "link" when subtree.GetAttribute("rel") is null or "alternate":
                    link ??= subtree.GetAttribute("href")?.Trim();
                    break;
                case "title":
                    title = subtree.ReadElementContentAsString().Trim();
                    break;
                case "summary":
                case "content":
                    summary ??= StripHtml(subtree.ReadElementContentAsString().Trim());
                    break;
                case "published":
                case "updated":
                    if (TryParseFeedDate(subtree.ReadElementContentAsString().Trim(), out var parsedAtomDate))
                    {
                        publishedAt ??= parsedAtomDate;
                    }

                    break;
            }
        }

        if (string.IsNullOrWhiteSpace(link)
            || string.IsNullOrWhiteSpace(title)
            || !NewsDiscoveryUrlFilter.IsLikelyArticleUrl(link))
        {
            return null;
        }

        return new FetchedNewsItem(link, title, publishedAt, TruncateExcerpt(summary));
    }

    private static string BuildTitleFromUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return url;
        }

        var slug = uri.AbsolutePath.Trim('/').Split('/').LastOrDefault() ?? uri.Host;
        return slug.Replace('-', ' ').Replace('_', ' ');
    }

    private static string? TruncateExcerpt(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Length <= 4000 ? value : value[..4000];
    }

    private static string StripHtml(string value) =>
        HtmlTags.Pattern().Replace(value, " ").Trim();

    private static bool TryParseFeedDate(string value, out DateTime parsed)
    {
        if (DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out parsed))
        {
            return true;
        }

        return DateTime.TryParse(value, CultureInfo.GetCultureInfo("en-US"), DateTimeStyles.AssumeUniversal, out parsed);
    }

    [GeneratedRegex("""href\s*=\s*["']([^"']+)["']""", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex HrefRegex();
}
