using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;

namespace QueenZone.Data;

/// <summary>
/// Validates and normalises pasted Spotify and Apple Music links. Only
/// <c>https://open.spotify.com</c> (or a <c>spotify:</c> URI) and <c>https://music.apple.com</c>
/// are accepted. Tracking parameters are dropped. Shared by admin editing and the backfill tools.
/// </summary>
public static partial class StreamingLinkUrl
{
    public const int MaxUrlLength = 300;

    public const int MaxExternalIdLength = 64;

    private const string SpotifyHost = "open.spotify.com";

    private const string AppleHost = "music.apple.com";

    public static bool TryParse(
        string? input,
        StreamingLinkKind expectedKind,
        [NotNullWhen(true)] out ParsedStreamingLink? link,
        [NotNullWhen(false)] out string? error)
    {
        link = null;
        var text = input?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            error = "Enter a link.";
            return false;
        }

        if (text.StartsWith("spotify:", StringComparison.OrdinalIgnoreCase))
        {
            link = ParseSpotifyUri(text, out error);
        }
        else if (!Uri.TryCreate(text, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
        {
            error = "Enter a full https:// link from Spotify or Apple Music.";
            return false;
        }
        else if (uri.Scheme != Uri.UriSchemeHttps)
        {
            error = "Use an https:// link.";
            return false;
        }
        else if (string.Equals(uri.Host, SpotifyHost, StringComparison.OrdinalIgnoreCase))
        {
            link = ParseSpotifyUrl(uri, out error);
        }
        else if (string.Equals(uri.Host, AppleHost, StringComparison.OrdinalIgnoreCase))
        {
            link = ParseAppleUrl(uri, out error);
        }
        else
        {
            error = $"Only {SpotifyHost} and {AppleHost} links are accepted.";
            return false;
        }

        if (link is null)
        {
            error ??= "That link is not a recognised album or track link.";
            return false;
        }

        if (link.Kind != expectedKind)
        {
            error = $"That is {KindArticle(link.Kind)} {KindName(link.Kind)} link from {link.Provider.DisplayName()}; this field needs {KindArticle(expectedKind)} {KindName(expectedKind)} link.";
            link = null;
            return false;
        }

        if (link.Url.Length > MaxUrlLength)
        {
            error = $"That link is longer than {MaxUrlLength} characters.";
            link = null;
            return false;
        }

        error = null;
        return true;
    }

    private static ParsedStreamingLink? ParseSpotifyUri(string text, out string? error)
    {
        // spotify:album:{id} / spotify:track:{id}
        var parts = text.Split(':');
        error = null;
        if (parts.Length != 3 || !TryKind(parts[1], out var kind) || !SpotifyId().IsMatch(parts[2]))
        {
            error = "That Spotify link is not an album or track link.";
            return null;
        }

        return Spotify(kind, parts[2]);
    }

    private static ParsedStreamingLink? ParseSpotifyUrl(Uri uri, out string? error)
    {
        // /album/{id}, /track/{id}, optionally prefixed by a locale segment such as /intl-de/.
        error = null;
        var segments = Segments(uri);
        if (segments.Length > 0 && SpotifyLocale().IsMatch(segments[0]))
        {
            segments = segments[1..];
        }

        if (segments.Length != 2 || !TryKind(segments[0], out var kind) || !SpotifyId().IsMatch(segments[1]))
        {
            error = "That Spotify link is not an album or track link.";
            return null;
        }

        return Spotify(kind, segments[1]);
    }

    private static ParsedStreamingLink? ParseAppleUrl(Uri uri, out string? error)
    {
        // /{cc}/album/{slug}/{albumId}[?i={trackId}], /{cc}/album/{albumId}[?i=...], /{cc}/song/{slug}/{trackId}
        error = null;
        var segments = Segments(uri);
        if (segments.Length < 3 || !AppleStorefront().IsMatch(segments[0]))
        {
            error = "Apple Music links must include the country code, for example music.apple.com/gb/album/...";
            return null;
        }

        var storefront = segments[0].ToLowerInvariant();
        var type = segments[1].ToLowerInvariant();
        var rest = segments[2..];
        if (rest.Length > 2 || !AppleId().IsMatch(rest[^1]) || (rest.Length == 2 && !IsSlug(rest[0])))
        {
            error = "That Apple Music link is not an album or track link.";
            return null;
        }

        var id = rest[^1];
        var path = rest.Length == 2 ? $"{rest[0]}/{id}" : id;
        if (type == "song")
        {
            return new ParsedStreamingLink(StreamingProvider.AppleMusic, StreamingLinkKind.Track, id, $"https://{AppleHost}/{storefront}/song/{path}");
        }

        if (type != "album")
        {
            error = "That Apple Music link is not an album or track link.";
            return null;
        }

        var trackId = QueryValue(uri, "i");
        if (trackId is null)
        {
            return new ParsedStreamingLink(StreamingProvider.AppleMusic, StreamingLinkKind.Album, id, $"https://{AppleHost}/{storefront}/album/{path}");
        }

        if (!AppleId().IsMatch(trackId))
        {
            error = "That Apple Music track link has an invalid track id.";
            return null;
        }

        return new ParsedStreamingLink(StreamingProvider.AppleMusic, StreamingLinkKind.Track, trackId, $"https://{AppleHost}/{storefront}/album/{path}?i={trackId}");
    }

    private static ParsedStreamingLink Spotify(StreamingLinkKind kind, string id) =>
        new(StreamingProvider.Spotify, kind, id, $"https://{SpotifyHost}/{KindName(kind)}/{id}");

    private static bool TryKind(string segment, out StreamingLinkKind kind)
    {
        switch (segment.ToLowerInvariant())
        {
            case "album":
                kind = StreamingLinkKind.Album;
                return true;
            case "track":
                kind = StreamingLinkKind.Track;
                return true;
            default:
                kind = default;
                return false;
        }
    }

    private static string[] Segments(Uri uri) =>
        uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);

    private static string? QueryValue(Uri uri, string name)
    {
        foreach (var pair in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = pair.IndexOf('=');
            var key = separator < 0 ? pair : pair[..separator];
            if (string.Equals(key, name, StringComparison.Ordinal))
            {
                return separator < 0 ? string.Empty : Uri.UnescapeDataString(pair[(separator + 1)..]);
            }
        }

        return null;
    }

    private static bool IsSlug(string segment) =>
        !AppleId().IsMatch(segment) && AppleSlug().IsMatch(segment);

    private static string KindName(StreamingLinkKind kind) => kind == StreamingLinkKind.Album ? "album" : "track";

    private static string KindArticle(StreamingLinkKind kind) => kind == StreamingLinkKind.Album ? "an" : "a";

    [GeneratedRegex("^[0-9A-Za-z]{22}$", RegexOptions.CultureInvariant)]
    private static partial Regex SpotifyId();

    [GeneratedRegex("^intl-[a-z]{2}(-[a-z]{2})?$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex SpotifyLocale();

    [GeneratedRegex("^[a-z]{2}$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex AppleStorefront();

    [GeneratedRegex("^[0-9]{1,20}$", RegexOptions.CultureInvariant)]
    private static partial Regex AppleId();

    // Path segments arrive percent-encoded, so non-ASCII titles are still in this set.
    [GeneratedRegex("^[A-Za-z0-9%._~-]+$", RegexOptions.CultureInvariant)]
    private static partial Regex AppleSlug();
}
