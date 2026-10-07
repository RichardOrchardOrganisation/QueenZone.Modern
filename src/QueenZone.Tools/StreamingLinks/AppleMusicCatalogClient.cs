using System.Globalization;
using System.Text.Json;
using QueenZone.Data;

namespace QueenZone.Tools;

/// <summary>
/// Apple Music catalogue via the public iTunes Search API (no auth). Apple documents roughly
/// 20 calls per minute, so requests are spaced by <see cref="DefaultMinInterval"/>.
/// URLs are passed through <see cref="StreamingLinkUrl"/> to drop the <c>uo</c> tracking parameter.
/// </summary>
internal sealed class AppleMusicCatalogClient(HttpClient http, string country, TimeSpan minInterval) : IStreamingCatalogClient
{
    public static readonly TimeSpan DefaultMinInterval = TimeSpan.FromSeconds(3);

    private const string Artist = "Queen";

    private readonly RequestPacer pacer = new(minInterval);

    public StreamingProvider Provider => StreamingProvider.AppleMusic;

    public async Task<IReadOnlyList<CatalogAlbum>> SearchAlbumsAsync(string albumName, CancellationToken cancellationToken)
    {
        var url = "https://itunes.apple.com/search?entity=album&attribute=albumTerm&limit=25"
            + $"&country={Uri.EscapeDataString(country)}&term={Uri.EscapeDataString($"{Artist} {albumName}")}";
        using var document = await GetJsonAsync(url, cancellationToken);
        var albums = new List<CatalogAlbum>();
        foreach (var result in Results(document))
        {
            if (!IsQueen(result) || !TryAlbumUrl(result, out var albumUrl))
            {
                continue;
            }

            albums.Add(new CatalogAlbum(
                Number(result, "collectionId").ToString(CultureInfo.InvariantCulture),
                Text(result, "collectionName"),
                Year(result),
                (int)Number(result, "trackCount"),
                albumUrl,
                IsCompilation: string.Equals(Text(result, "collectionType"), "Compilation", StringComparison.OrdinalIgnoreCase)));
        }

        return albums;
    }

    public async Task<IReadOnlyList<CatalogTrack>> GetTracksAsync(CatalogAlbum album, CancellationToken cancellationToken)
    {
        var url = $"https://itunes.apple.com/lookup?entity=song&limit=200&country={Uri.EscapeDataString(country)}"
            + $"&id={Uri.EscapeDataString(album.ExternalId)}";
        using var document = await GetJsonAsync(url, cancellationToken);
        var tracks = new List<CatalogTrack>();
        foreach (var result in Results(document))
        {
            if (!string.Equals(Text(result, "wrapperType"), "track", StringComparison.Ordinal)
                || !StreamingLinkUrl.TryParse(Text(result, "trackViewUrl"), StreamingLinkKind.Track, out var link, out _))
            {
                continue;
            }

            tracks.Add(new CatalogTrack(
                link.ExternalId,
                Text(result, "trackName"),
                (int)Number(result, "trackNumber"),
                (int)Number(result, "discNumber"),
                link.Url));
        }

        return tracks;
    }

    private async Task<JsonDocument> GetJsonAsync(string url, CancellationToken cancellationToken)
    {
        await pacer.WaitAsync(cancellationToken);
        using var response = await http.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
    }

    private static IEnumerable<JsonElement> Results(JsonDocument document) =>
        document.RootElement.TryGetProperty("results", out var results) && results.ValueKind == JsonValueKind.Array
            ? results.EnumerateArray()
            : [];

    private static bool IsQueen(JsonElement result) =>
        string.Equals(Text(result, "artistName"), Artist, StringComparison.OrdinalIgnoreCase);

    private static bool TryAlbumUrl(JsonElement result, out string url)
    {
        url = string.Empty;
        if (!StreamingLinkUrl.TryParse(Text(result, "collectionViewUrl"), StreamingLinkKind.Album, out var link, out _))
        {
            return false;
        }

        url = link.Url;
        return true;
    }

    private static int? Year(JsonElement result) =>
        DateTimeOffset.TryParse(Text(result, "releaseDate"), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var date)
            ? date.Year
            : null;

    private static string Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? string.Empty : string.Empty;

    private static long Number(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number ? value.GetInt64() : 0;
}

/// <summary>Spaces outbound requests at least <c>minInterval</c> apart (zero disables pacing).</summary>
internal sealed class RequestPacer(TimeSpan minInterval)
{
    private DateTimeOffset nextAllowed = DateTimeOffset.MinValue;

    public async Task WaitAsync(CancellationToken cancellationToken)
    {
        if (minInterval <= TimeSpan.Zero)
        {
            return;
        }

        var wait = nextAllowed - DateTimeOffset.UtcNow;
        if (wait > TimeSpan.Zero)
        {
            await Task.Delay(wait, cancellationToken);
        }

        nextAllowed = DateTimeOffset.UtcNow + minInterval;
    }
}
