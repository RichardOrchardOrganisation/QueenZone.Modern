using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using QueenZone.Data;

namespace QueenZone.Tools;

/// <summary>
/// Spotify catalogue via the Web API with the Client Credentials flow (no user sign-in). One
/// development-mode app is enough because only admins run this locally. <c>429</c> responses
/// are retried after <c>Retry-After</c>, up to <see cref="MaxRetries"/> times.
/// </summary>
internal sealed class SpotifyCatalogClient(
    HttpClient http,
    string clientId,
    string clientSecret,
    string market,
    TimeSpan minInterval) : IStreamingCatalogClient
{
    public const int MaxRetries = 3;

    public static readonly TimeSpan DefaultMinInterval = TimeSpan.FromMilliseconds(250);

    private const string Artist = "Queen";

    private readonly RequestPacer pacer = new(minInterval);

    private string? accessToken;

    public StreamingProvider Provider => StreamingProvider.Spotify;

    public async Task<IReadOnlyList<CatalogAlbum>> SearchAlbumsAsync(string albumName, CancellationToken cancellationToken)
    {
        var query = Uri.EscapeDataString($"album:{albumName} artist:{Artist}");
        using var document = await GetJsonAsync(
            $"https://api.spotify.com/v1/search?type=album&limit=10&market={Uri.EscapeDataString(market)}&q={query}",
            cancellationToken);
        var albums = new List<CatalogAlbum>();
        if (!document.RootElement.TryGetProperty("albums", out var page) || !page.TryGetProperty("items", out var items))
        {
            return albums;
        }

        foreach (var item in items.EnumerateArray())
        {
            if (!IsQueen(item) || !StreamingLinkUrl.TryParse(SpotifyUrl(item), StreamingLinkKind.Album, out var link, out _))
            {
                continue;
            }

            albums.Add(new CatalogAlbum(
                link.ExternalId,
                Text(item, "name"),
                Year(Text(item, "release_date")),
                Int(item, "total_tracks"),
                link.Url,
                IsCompilation: string.Equals(Text(item, "album_type"), "compilation", StringComparison.OrdinalIgnoreCase)));
        }

        return albums;
    }

    public async Task<IReadOnlyList<CatalogTrack>> GetTracksAsync(CatalogAlbum album, CancellationToken cancellationToken)
    {
        var tracks = new List<CatalogTrack>();
        string? next = $"https://api.spotify.com/v1/albums/{Uri.EscapeDataString(album.ExternalId)}/tracks?limit=50&market={Uri.EscapeDataString(market)}";
        while (next is not null)
        {
            using var document = await GetJsonAsync(next, cancellationToken);
            foreach (var item in document.RootElement.GetProperty("items").EnumerateArray())
            {
                if (StreamingLinkUrl.TryParse(SpotifyUrl(item), StreamingLinkKind.Track, out var link, out _))
                {
                    tracks.Add(new CatalogTrack(link.ExternalId, Text(item, "name"), Int(item, "track_number"), Int(item, "disc_number"), link.Url));
                }
            }

            next = document.RootElement.TryGetProperty("next", out var nextValue) && nextValue.ValueKind == JsonValueKind.String
                ? nextValue.GetString()
                : null;
        }

        return tracks;
    }

    private async Task<JsonDocument> GetJsonAsync(string url, CancellationToken cancellationToken)
    {
        var attempt = 0;
        while (true)
        {
            attempt++;
            await pacer.WaitAsync(cancellationToken);
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await TokenAsync(cancellationToken));
            using var response = await http.SendAsync(request, cancellationToken);
            if (response.StatusCode == HttpStatusCode.TooManyRequests && attempt <= MaxRetries)
            {
                await Task.Delay(RetryAfter(response), cancellationToken);
                continue;
            }

            if (response.StatusCode == HttpStatusCode.Unauthorized && attempt == 1)
            {
                // Tokens last an hour; a long run can outlive one.
                accessToken = null;
                continue;
            }

            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        }
    }

    private async Task<string> TokenAsync(CancellationToken cancellationToken)
    {
        if (accessToken is not null)
        {
            return accessToken;
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, "https://accounts.spotify.com/api/token")
        {
            Content = new FormUrlEncodedContent([new KeyValuePair<string, string>("grant_type", "client_credentials")]),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Basic",
            Convert.ToBase64String(Encoding.UTF8.GetBytes($"{clientId}:{clientSecret}")));
        using var response = await http.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"Spotify token request failed with {(int)response.StatusCode}. Check Spotify__ClientId and Spotify__ClientSecret.");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        accessToken = document.RootElement.GetProperty("access_token").GetString()
            ?? throw new InvalidOperationException("Spotify token response had no access_token.");
        return accessToken;
    }

    internal static TimeSpan RetryAfter(HttpResponseMessage response)
    {
        var retryAfter = response.Headers.RetryAfter;
        var delay = retryAfter?.Delta
            ?? (retryAfter?.Date is DateTimeOffset date ? date - DateTimeOffset.UtcNow : TimeSpan.FromSeconds(1));
        return delay < TimeSpan.Zero ? TimeSpan.Zero : delay;
    }

    private static bool IsQueen(JsonElement item) =>
        item.TryGetProperty("artists", out var artists)
        && artists.EnumerateArray().Any(artist => string.Equals(Text(artist, "name"), Artist, StringComparison.OrdinalIgnoreCase));

    private static string SpotifyUrl(JsonElement item) =>
        item.TryGetProperty("external_urls", out var urls) ? Text(urls, "spotify") : string.Empty;

    // release_date is "1975", "1975-11", or "1975-11-21" depending on release_date_precision.
    private static int? Year(string releaseDate) =>
        releaseDate.Length >= 4 && int.TryParse(releaseDate[..4], NumberStyles.None, CultureInfo.InvariantCulture, out var year)
            ? year
            : null;

    private static string Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? string.Empty : string.Empty;

    private static int Int(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number ? value.GetInt32() : 0;
}
