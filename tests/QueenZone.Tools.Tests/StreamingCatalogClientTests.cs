using System.Net;
using System.Text;
using QueenZone.Data;
using QueenZone.Tools;

namespace QueenZone.Tools.Tests;

/// <summary>
/// iTunes Search and Spotify Web API parsing against trimmed responses in the shape the
/// services return. No test reaches the network.
/// </summary>
public sealed class StreamingCatalogClientTests
{
    private const string ITunesAlbumSearch = """
        {"resultCount":3,"results":[
          {"wrapperType":"collection","collectionType":"Album","artistName":"Queen","collectionId":1440650428,
           "collectionName":"A Night At The Opera (2011 Remaster)","trackCount":12,"releaseDate":"1975-11-21T08:00:00Z",
           "collectionViewUrl":"https://music.apple.com/gb/album/a-night-at-the-opera-2011-remaster/1440650428?uo=4"},
          {"wrapperType":"collection","collectionType":"Compilation","artistName":"Queen","collectionId":1440806053,
           "collectionName":"Greatest Hits","trackCount":17,"releaseDate":"1981-10-26T08:00:00Z",
           "collectionViewUrl":"https://music.apple.com/gb/album/greatest-hits/1440806053?uo=4"},
          {"wrapperType":"collection","collectionType":"Album","artistName":"Queen Tribute Band","collectionId":99,
           "collectionName":"A Night At The Opera","trackCount":12,"releaseDate":"2010-01-01T08:00:00Z",
           "collectionViewUrl":"https://music.apple.com/gb/album/x/99?uo=4"}]}
        """;

    private const string ITunesLookup = """
        {"resultCount":3,"results":[
          {"wrapperType":"collection","collectionId":1440650428},
          {"wrapperType":"track","kind":"song","trackId":1440650711,"trackName":"Bohemian Rhapsody (Remastered 2011)",
           "trackNumber":11,"discNumber":1,
           "trackViewUrl":"https://music.apple.com/gb/album/bohemian-rhapsody-remastered-2011/1440650428?i=1440650711&uo=4"},
          {"wrapperType":"track","kind":"song","trackId":1440650712,"trackName":"God Save The Queen (Remastered 2011)",
           "trackNumber":12,"discNumber":1,
           "trackViewUrl":"https://music.apple.com/gb/album/god-save-the-queen-remastered-2011/1440650428?i=1440650712&uo=4"}]}
        """;

    private const string SpotifyToken = """{"access_token":"token-1","token_type":"Bearer","expires_in":3600}""";

    private const string SpotifyAlbumSearch = """
        {"albums":{"items":[
          {"album_type":"album","id":"1GbtB4zTqAsyfZEsm1RZfx","name":"A Night At The Opera (2011 Remaster)",
           "release_date":"1975-11-21","total_tracks":12,
           "external_urls":{"spotify":"https://open.spotify.com/album/1GbtB4zTqAsyfZEsm1RZfx"},
           "artists":[{"name":"Queen"}]},
          {"album_type":"compilation","id":"4p0KwHZPZ7oaLUmOc5rJ5s","name":"Queen Forever",
           "release_date":"2014","total_tracks":20,
           "external_urls":{"spotify":"https://open.spotify.com/album/4p0KwHZPZ7oaLUmOc5rJ5s"},
           "artists":[{"name":"Queen"}]},
          {"album_type":"album","id":"0000000000000000000000","name":"A Night At The Opera",
           "release_date":"2001-05","total_tracks":12,
           "external_urls":{"spotify":"https://open.spotify.com/album/0000000000000000000000"},
           "artists":[{"name":"Someone Else"}]}]}}
        """;

    [Fact]
    public async Task ITunes_search_keeps_queen_albums_and_normalises_urls()
    {
        var handler = new FakeHandler(_ => Json(ITunesAlbumSearch));
        var client = new AppleMusicCatalogClient(new HttpClient(handler), "gb", TimeSpan.Zero);

        var albums = await client.SearchAlbumsAsync("A Night at the Opera", CancellationToken.None);

        Assert.Equal(2, albums.Count);
        var opera = albums[0];
        Assert.Equal("1440650428", opera.ExternalId);
        Assert.Equal(1975, opera.ReleaseYear);
        Assert.Equal(12, opera.TrackCount);
        Assert.Equal("https://music.apple.com/gb/album/a-night-at-the-opera-2011-remaster/1440650428", opera.Url);
        Assert.False(opera.IsCompilation);
        Assert.True(albums[1].IsCompilation);

        var request = Assert.Single(handler.Requests);
        Assert.StartsWith("https://itunes.apple.com/search?", request.RequestUri!.ToString(), StringComparison.Ordinal);
        Assert.Contains("country=gb", request.RequestUri.Query, StringComparison.Ordinal);
        Assert.Contains("term=Queen%20A%20Night%20at%20the%20Opera", request.RequestUri.Query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ITunes_lookup_returns_tracks_with_track_links()
    {
        var client = new AppleMusicCatalogClient(new HttpClient(new FakeHandler(_ => Json(ITunesLookup))), "gb", TimeSpan.Zero);

        var tracks = await client.GetTracksAsync(new CatalogAlbum("1440650428", "x", 1975, 12, "https://x"), CancellationToken.None);

        Assert.Equal(2, tracks.Count);
        Assert.Equal("1440650711", tracks[0].ExternalId);
        Assert.Equal(11, tracks[0].TrackNumber);
        Assert.Equal("https://music.apple.com/gb/album/bohemian-rhapsody-remastered-2011/1440650428?i=1440650711", tracks[0].Url);
    }

    [Fact]
    public async Task ITunes_http_failures_surface_as_http_request_exceptions()
    {
        var client = new AppleMusicCatalogClient(
            new HttpClient(new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable))),
            "gb",
            TimeSpan.Zero);

        await Assert.ThrowsAsync<HttpRequestException>(() => client.SearchAlbumsAsync("Jazz", CancellationToken.None));
    }

    [Fact]
    public async Task Spotify_search_uses_client_credentials_and_filters_to_queen()
    {
        var handler = new FakeHandler(request => request.RequestUri!.Host == "accounts.spotify.com" ? Json(SpotifyToken) : Json(SpotifyAlbumSearch));
        var client = new SpotifyCatalogClient(new HttpClient(handler), "id", "secret", "GB", TimeSpan.Zero);

        var albums = await client.SearchAlbumsAsync("A Night at the Opera", CancellationToken.None);

        Assert.Equal(["1GbtB4zTqAsyfZEsm1RZfx", "4p0KwHZPZ7oaLUmOc5rJ5s"], albums.Select(album => album.ExternalId));
        Assert.Equal(1975, albums[0].ReleaseYear);
        Assert.Equal(2014, albums[1].ReleaseYear);
        Assert.True(albums[1].IsCompilation);
        Assert.Equal("https://open.spotify.com/album/1GbtB4zTqAsyfZEsm1RZfx", albums[0].Url);

        var token = handler.Requests[0];
        Assert.Equal(HttpMethod.Post, token.Method);
        Assert.Equal("Basic", token.Headers.Authorization!.Scheme);
        Assert.Equal(Convert.ToBase64String(Encoding.UTF8.GetBytes("id:secret")), token.Headers.Authorization.Parameter);
        var search = handler.Requests[1];
        Assert.Equal("Bearer token-1", search.Headers.Authorization!.ToString());
        Assert.Contains("market=GB", search.RequestUri!.Query, StringComparison.Ordinal);
        Assert.Contains("q=album%3AA%20Night%20at%20the%20Opera%20artist%3AQueen", search.RequestUri.Query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Spotify_tracks_follow_paging_and_retry_after_429()
    {
        var calls = 0;
        var handler = new FakeHandler(request =>
        {
            if (request.RequestUri!.Host == "accounts.spotify.com")
            {
                return Json(SpotifyToken);
            }

            calls++;
            if (calls == 1)
            {
                var limited = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
                limited.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.Zero);
                return limited;
            }

            return request.RequestUri.Query.Contains("offset=50", StringComparison.Ordinal)
                ? Json("""{"items":[{"id":"0000000000000000000002","name":"Track 51","track_number":1,"disc_number":2,"external_urls":{"spotify":"https://open.spotify.com/track/0000000000000000000002"}}],"next":null}""")
                : Json("""{"items":[{"id":"0000000000000000000001","name":"Bohemian Rhapsody - Remastered 2011","track_number":11,"disc_number":1,"external_urls":{"spotify":"https://open.spotify.com/track/0000000000000000000001?si=x"}}],"next":"https://api.spotify.com/v1/albums/a/tracks?offset=50&limit=50"}""");
        });
        var client = new SpotifyCatalogClient(new HttpClient(handler), "id", "secret", "GB", TimeSpan.Zero);

        var tracks = await client.GetTracksAsync(new CatalogAlbum("a", "x", 1975, 12, "https://x"), CancellationToken.None);

        Assert.Equal(["0000000000000000000001", "0000000000000000000002"], tracks.Select(track => track.ExternalId));
        Assert.Equal("https://open.spotify.com/track/0000000000000000000001", tracks[0].Url);
        Assert.Equal(2, tracks[1].DiscNumber);
        Assert.Equal(3, calls);
    }

    [Fact]
    public async Task Spotify_refreshes_an_expired_token_once_and_reports_bad_credentials()
    {
        var tokenRequests = 0;
        var apiCalls = 0;
        var handler = new FakeHandler(request =>
        {
            if (request.RequestUri!.Host == "accounts.spotify.com")
            {
                tokenRequests++;
                return Json(SpotifyToken);
            }

            return ++apiCalls == 1 ? new HttpResponseMessage(HttpStatusCode.Unauthorized) : Json("""{"albums":{"items":[]}}""");
        });
        var client = new SpotifyCatalogClient(new HttpClient(handler), "id", "secret", "GB", TimeSpan.Zero);

        Assert.Empty(await client.SearchAlbumsAsync("Jazz", CancellationToken.None));
        Assert.Equal(2, tokenRequests);

        var rejecting = new SpotifyCatalogClient(
            new HttpClient(new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.BadRequest))),
            "id",
            "wrong",
            "GB",
            TimeSpan.Zero);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => rejecting.SearchAlbumsAsync("Jazz", CancellationToken.None));
        Assert.Contains("Spotify__ClientId", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Retry_after_uses_the_header_and_never_goes_negative()
    {
        using var delta = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        delta.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(7));
        Assert.Equal(TimeSpan.FromSeconds(7), SpotifyCatalogClient.RetryAfter(delta));

        using var past = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        past.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(DateTimeOffset.UtcNow.AddMinutes(-1));
        Assert.Equal(TimeSpan.Zero, SpotifyCatalogClient.RetryAfter(past));

        using var none = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        Assert.Equal(TimeSpan.FromSeconds(1), SpotifyCatalogClient.RetryAfter(none));
    }

    [Fact]
    public async Task Request_pacer_spaces_calls_only_when_an_interval_is_set()
    {
        var none = new RequestPacer(TimeSpan.Zero);
        await none.WaitAsync(CancellationToken.None);

        var paced = new RequestPacer(TimeSpan.FromMilliseconds(60));
        await paced.WaitAsync(CancellationToken.None);
        var started = DateTimeOffset.UtcNow;
        await paced.WaitAsync(CancellationToken.None);
        Assert.True(DateTimeOffset.UtcNow - started >= TimeSpan.FromMilliseconds(40));
    }

    private static HttpResponseMessage Json(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    internal sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(respond(request));
        }
    }
}
