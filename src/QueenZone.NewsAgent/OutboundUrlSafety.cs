using System.Net;

namespace QueenZone.NewsAgent;

/// <summary>
/// SSRF guards for news-discovery outbound HTTP: scheme allowlist and
/// private/link-local/metadata IP blocking after DNS resolution.
/// </summary>
public static class OutboundUrlSafety
{
    // Special-purpose ranges from the IANA IPv4/IPv6 registries:
    // https://www.iana.org/assignments/iana-ipv4-special-registry/
    // https://www.iana.org/assignments/iana-ipv6-special-registry/
    // Translation/transition prefixes are blocked outright: news hosts do not need
    // literal NAT64, IPv4-compatible or 6to4 destinations that can embed private IPv4.
    private static readonly IPNetwork[] BlockedNetworks =
    [
        IPNetwork.Parse("0.0.0.0/8"),
        IPNetwork.Parse("10.0.0.0/8"),
        IPNetwork.Parse("100.64.0.0/10"),
        IPNetwork.Parse("127.0.0.0/8"),
        IPNetwork.Parse("169.254.0.0/16"),
        IPNetwork.Parse("172.16.0.0/12"),
        IPNetwork.Parse("192.0.0.0/24"),
        IPNetwork.Parse("192.0.2.0/24"),
        IPNetwork.Parse("192.168.0.0/16"),
        IPNetwork.Parse("198.18.0.0/15"),
        IPNetwork.Parse("198.51.100.0/24"),
        IPNetwork.Parse("203.0.113.0/24"),
        IPNetwork.Parse("224.0.0.0/4"), // Multicast (RFC 1112).
        IPNetwork.Parse("240.0.0.0/4"), // Reserved, including limited broadcast.
        IPNetwork.Parse("::/96"), // Unspecified, loopback and deprecated IPv4-compatible.
        IPNetwork.Parse("64:ff9b::/96"),
        IPNetwork.Parse("64:ff9b:1::/48"),
        IPNetwork.Parse("2002::/16"),
        IPNetwork.Parse("fc00::/7"),
        IPNetwork.Parse("fe80::/10"),
        IPNetwork.Parse("fec0::/10"), // Deprecated site-local.
        IPNetwork.Parse("ff00::/8"), // IPv6 multicast (RFC 4291).
    ];

    public const int DefaultMaxResponseBytes = 5 * 1024 * 1024;
    public const int MaxUrlLength = 2000;

    /// <summary>
    /// Ensures <paramref name="url"/> is an absolute http(s) URL suitable for discovery fetch.
    /// Prefer HTTPS; HTTP is allowed for legacy feeds but still subject to IP blocking on connect.
    /// </summary>
    public static void EnsureAllowedHttpUrl(string url)
    {
        if (!TryValidatePublicHttpUrl(url, out var error, out _))
        {
            throw new InvalidOperationException(error);
        }
    }

    /// <summary>
    /// Deterministic URL safety check for admin submission and worker ingestion.
    /// Does not perform DNS; DNS-aware IP blocking still runs on connect and redirects.
    /// </summary>
    public static bool TryValidatePublicHttpUrl(string? url, out string error, out string? normalizedUrl)
    {
        error = string.Empty;
        normalizedUrl = null;

        if (string.IsNullOrWhiteSpace(url))
        {
            error = "Article URL is required.";
            return false;
        }

        var trimmed = url.Trim();
        if (trimmed.Length > MaxUrlLength)
        {
            error = $"Article URL must be at most {MaxUrlLength} characters.";
            return false;
        }

        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
        {
            error = "Article URL must be an absolute HTTP or HTTPS address.";
            return false;
        }

        if (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp)
        {
            error = $"URL scheme '{uri.Scheme}' is not allowed. Use http or https.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(uri.Host))
        {
            error = "Article URL host is required.";
            return false;
        }

        if (!string.IsNullOrEmpty(uri.UserInfo))
        {
            error = "Article URL must not include credentials.";
            return false;
        }

        // Literal IP in the URL — check immediately (no DNS required).
        if (IPAddress.TryParse(uri.DnsSafeHost, out var literal)
            && IsBlockedAddress(literal))
        {
            error = $"Article URL host resolves to a blocked address family ({literal}).";
            return false;
        }

        if (IsBlockedHostName(uri.DnsSafeHost))
        {
            error = $"Article URL host '{uri.DnsSafeHost}' is not allowed.";
            return false;
        }

        normalizedUrl = uri.AbsoluteUri;
        return true;
    }

    public static bool IsBlockedHostName(string host)
    {
        if (string.IsNullOrWhiteSpace(host))
        {
            return true;
        }

        if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
            || host.Equals("metadata", StringComparison.OrdinalIgnoreCase)
            || host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase)
            || host.EndsWith(".local", StringComparison.OrdinalIgnoreCase)
            || host.EndsWith(".internal", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // Azure IMDS host
        if (host.Equals("169.254.169.254", StringComparison.OrdinalIgnoreCase)
            || host.Equals("metadata.google.internal", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }

    public static bool IsBlockedAddress(IPAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);

        if (address.IsIPv4MappedToIPv6)
        {
            return IsBlockedAddress(address.MapToIPv4());
        }

        return BlockedNetworks.Any(network => network.Contains(address));
    }

    public static bool IsAllowedTextContentType(string? contentType)
    {
        if (string.IsNullOrWhiteSpace(contentType))
        {
            return true;
        }

        var mediaType = contentType.Split(';', 2)[0].Trim();
        return mediaType.Equals("text/html", StringComparison.OrdinalIgnoreCase)
            || mediaType.Equals("application/xhtml+xml", StringComparison.OrdinalIgnoreCase)
            || mediaType.Equals("text/plain", StringComparison.OrdinalIgnoreCase)
            || mediaType.Equals("application/xml", StringComparison.OrdinalIgnoreCase)
            || mediaType.Equals("text/xml", StringComparison.OrdinalIgnoreCase)
            || mediaType.Equals("application/rss+xml", StringComparison.OrdinalIgnoreCase)
            || mediaType.Equals("application/atom+xml", StringComparison.OrdinalIgnoreCase);
    }
}
