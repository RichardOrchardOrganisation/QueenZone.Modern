using QueenZone.Data;
using QueenZone.Tools;

namespace QueenZone.Tools.Tests;

[Collection(EnvironmentVariableCollection.Name)]
public sealed class SuggestStreamingLinksCommandTests
{
    private const string Connection = "Server=.;Database=test;";

    [Fact]
    public async Task ToolsApp_routes_the_command_and_rejects_missing_output()
    {
        using var error = new StringWriter();
        var originalError = Console.Error;
        Console.SetError(error);
        try
        {
            Assert.Equal(2, await ToolsApp.RunAsync(["suggest-streaming-links"]));
            Assert.Contains("--out is required.", error.ToString(), StringComparison.Ordinal);
            Assert.Contains("suggest-streaming-links --out", error.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            Console.SetError(originalError);
        }
    }

    [Fact]
    public void Parse_requires_spotify_credentials_unless_apple_music_only()
    {
        WithSpotify(null, null, () =>
        {
            var both = SuggestStreamingLinksOptions.Parse(["--out", "x.csv", "--connection-string", Connection]);
            Assert.False(both.IsValid);
            Assert.Contains("Spotify__ClientId", both.ErrorMessage, StringComparison.Ordinal);

            var apple = SuggestStreamingLinksOptions.Parse(["--out", "x.csv", "--connection-string", Connection, "--provider", "Apple-Music"]);
            Assert.True(apple.IsValid);
            Assert.Equal([StreamingProvider.AppleMusic], apple.Providers);
        });

        WithSpotify("id", "secret", () =>
        {
            var options = SuggestStreamingLinksOptions.Parse(
                ["--out", "x.csv", "--connection-string", Connection, "--album-id", "4", "--only-missing", "--country", "US"]);
            Assert.True(options.IsValid);
            Assert.Equal(StreamingProviders.All, options.Providers);
            Assert.Equal(4, options.AlbumId);
            Assert.True(options.OnlyMissing);
            Assert.Equal("us", options.Country);
            Assert.Equal("id", options.SpotifyClientId);
        });
    }

    [Theory]
    [InlineData("--provider", "deezer", "--provider must be spotify or apple-music.")]
    [InlineData("--album-id", "0", "--album-id must be a positive integer.")]
    [InlineData("--country", "gbr", "--country must be a two-letter code such as gb or us.")]
    [InlineData("--bogus", "1", "Unsupported or incomplete argument: --bogus")]
    public void Parse_rejects_bad_values(string flag, string value, string message)
    {
        var options = SuggestStreamingLinksOptions.Parse(["--out", "x.csv", "--connection-string", Connection, flag, value]);

        Assert.False(options.IsValid);
        Assert.Equal(message, options.ErrorMessage);
    }

    [Fact]
    public void Parse_needs_a_connection_string()
    {
        var previous = Environment.GetEnvironmentVariable("ConnectionStrings__QueenZoneLegacy");
        Environment.SetEnvironmentVariable("ConnectionStrings__QueenZoneLegacy", null);
        try
        {
            var options = SuggestStreamingLinksOptions.Parse(
                ["--out", "x.csv", "--provider", "apple-music", "--settings-file", Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}.json")]);
            Assert.False(options.IsValid);
            Assert.Contains("--connection-string", options.ErrorMessage, StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable("ConnectionStrings__QueenZoneLegacy", previous);
        }
    }

    [Fact]
    public async Task Suggest_writes_album_and_track_rows_with_scores_flags_and_existing_links()
    {
        var (store, repository) = Repository();
        var options = Options("--album-id", "4");
        var client = new FakeCatalogClient(
            StreamingProvider.AppleMusic,
            [
                new CatalogAlbum("1440650428", "A Night At The Opera (2011 Remaster)", 1975, 12, "https://music.apple.com/gb/album/a-night-at-the-opera/1440650428"),
                new CatalogAlbum("2", "A Night At The Opera (Deluxe Edition)", 1975, 24, "https://music.apple.com/gb/album/deluxe/2"),
            ],
            [new CatalogTrack("1440650711", "Bohemian Rhapsody (Remastered 2011)", 11, 1, "https://music.apple.com/gb/album/a-night-at-the-opera/1440650428?i=1440650711")]);

        var rows = await SuggestStreamingLinksCommand.SuggestAsync(options, repository, [client], TextWriter.Null, CancellationToken.None);

        Assert.Equal(13, rows.Count);
        var album = rows[0];
        Assert.Null(album.AlbumSongId);
        Assert.Equal("1440650428", album.ExternalId);
        Assert.Equal(100, album.Score);
        Assert.Equal([StreamingLinkMatcher.RemasterFlag], album.Flags);
        Assert.Equal("https://music.apple.com/gb/album/a-night-at-the-opera/1000000004", album.ExistingUrl);

        var bohemian = rows.Single(row => row.Title == "Bohemian Rhapsody");
        Assert.Equal(4011, bohemian.AlbumSongId);
        Assert.Equal("1440650711", bohemian.ExternalId);
        Assert.Equal(100, bohemian.Score);
        Assert.Equal("https://music.apple.com/gb/album/a-night-at-the-opera/1000000004?i=1000000411", bohemian.ExistingUrl);

        var unmatched = rows.Single(row => row.Title == "God Save the Queen");
        Assert.Null(unmatched.CandidateUrl);
        Assert.Equal([SuggestStreamingLinksCommand.NoMatchFlag], unmatched.Flags);
        Assert.Equal(["A Night at the Opera"], client.Searches);
        Assert.Equal(1, client.TrackLookups);
        Assert.NotNull(store);
    }

    [Fact]
    public async Task Only_missing_skips_linked_rows_and_skips_the_api_when_nothing_is_missing()
    {
        var (store, repository) = Repository();
        var admin = new InMemoryAdminDiscographyRepository(store);
        foreach (var song in (await admin.GetAlbumAsync(2))!.Songs)
        {
            await admin.SetSongStreamingLinkAsync(song.SongId, StreamingProvider.Spotify, Write($"spotify:track:{song.SongId.ToString().PadLeft(22, '0')}", StreamingLinkKind.Track));
        }

        await admin.SetAlbumStreamingLinkAsync(2, StreamingProvider.Spotify, Write("spotify:album:0000000000000000000002", StreamingLinkKind.Album));
        var client = new FakeCatalogClient(StreamingProvider.Spotify, [], []);

        var rows = await SuggestStreamingLinksCommand.SuggestAsync(Options("--album-id", "2", "--only-missing"), repository, [client], TextWriter.Null, CancellationToken.None);

        Assert.Empty(rows);
        Assert.Empty(client.Searches);

        // Album 4 has a Spotify album link and one track link: only the other 11 tracks are suggested.
        var opera = await SuggestStreamingLinksCommand.SuggestAsync(Options("--album-id", "4", "--only-missing"), repository, [client], TextWriter.Null, CancellationToken.None);
        Assert.Equal(11, opera.Count);
        Assert.All(opera, row => Assert.NotNull(row.AlbumSongId));
        Assert.DoesNotContain(opera, row => row.Title == "Bohemian Rhapsody");
        Assert.All(opera, row => Assert.Equal([SuggestStreamingLinksCommand.NoMatchFlag], row.Flags));
        Assert.Equal(0, client.TrackLookups);
    }

    [Fact]
    public async Task Http_failures_are_logged_as_error_rows_and_the_run_continues()
    {
        var (_, repository) = Repository();
        using var log = new StringWriter();
        var failing = new FakeCatalogClient(StreamingProvider.Spotify, [], [], fail: true);
        var working = new FakeCatalogClient(StreamingProvider.AppleMusic, [], []);

        var rows = await SuggestStreamingLinksCommand.SuggestAsync(Options("--album-id", "1"), repository, [failing, working], log, CancellationToken.None);

        var error = rows.First();
        Assert.Equal(StreamingProvider.Spotify, error.Provider);
        Assert.Equal([SuggestStreamingLinksCommand.ErrorFlag], error.Flags);
        Assert.Contains("Spotify lookup failed for album 1 (Queen)", log.ToString(), StringComparison.Ordinal);
        Assert.Contains(rows, row => row.Provider == StreamingProvider.AppleMusic);
    }

    [Fact]
    public async Task RunAsync_writes_the_csv_header_rows_and_summary()
    {
        var (_, repository) = Repository();
        using var csv = new StringWriter();
        var client = new FakeCatalogClient(StreamingProvider.Spotify, [new CatalogAlbum("x", "Queen", 1973, 10, "https://open.spotify.com/album/x")], []);
        using var stdout = new StringWriter();
        var original = Console.Out;
        Console.SetOut(stdout);
        try
        {
            Assert.Equal(0, await SuggestStreamingLinksCommand.RunAsync(Options("--album-id", "1"), repository, [client], csv));
        }
        finally
        {
            Console.SetOut(original);
        }

        var lines = csv.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal("albumId,albumSongId,title,provider,candidateUrl,externalId,score,flags,existingUrl,approved", lines[0]);
        Assert.Equal("1,,Queen,spotify,https://open.spotify.com/album/x,x,100,,,", lines[1]);
        Assert.Equal(12, lines.Length);
        Assert.Contains("1,1010,Seven Seas of Rhye,spotify,,,,no-match,https://open.spotify.com/track/0SampleSevenSeasOfRhye,", lines);
        Assert.Contains("Rows written: 11", stdout.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null, "")]
    [InlineData("plain", "plain")]
    [InlineData("Flash, Ming", "\"Flash, Ming\"")]
    [InlineData("Say \"hi\"", "\"Say \"\"hi\"\"\"")]
    [InlineData("=HYPERLINK(1)", "'=HYPERLINK(1)")]
    [InlineData("-1,2", "\"'-1,2\"")]
    public void Csv_escapes_fields_and_neutralises_formulas(string? value, string expected) =>
        Assert.Equal(expected, SuggestStreamingLinksCommand.Csv(value));

    [Fact]
    public void CreateClients_builds_one_client_per_provider()
    {
        WithSpotify("id", "secret", () =>
        {
            var options = SuggestStreamingLinksOptions.Parse(["--out", "x.csv", "--connection-string", Connection]);
            using var http = new HttpClient();

            var clients = SuggestStreamingLinksCommand.CreateClients(options, http);

            Assert.Equal([StreamingProvider.Spotify, StreamingProvider.AppleMusic], clients.Select(client => client.Provider));
        });
    }

    private static SuggestStreamingLinksOptions Options(params string[] extra) =>
        SuggestStreamingLinksOptions.Parse(["--out", "x.csv", "--connection-string", Connection, "--provider", "apple-music", .. extra]);

    private static (InMemoryDiscographyStore Store, IAdminDiscographyRepository Repository) Repository()
    {
        var store = new InMemoryDiscographyStore(SampleDiscographyData.CreateSeedAlbums(), SampleDiscographyData.CreateSeedStreamingLinks());
        return (store, new InMemoryAdminDiscographyRepository(store));
    }

    private static StreamingLinkWrite Write(string url, StreamingLinkKind kind)
    {
        Assert.True(StreamingLinkUrl.TryParse(url, kind, out var link, out var error), error);
        return new StreamingLinkWrite(link, StreamingLinkSource.Manual, null);
    }

    private static void WithSpotify(string? id, string? secret, Action action)
    {
        var previousId = Environment.GetEnvironmentVariable("Spotify__ClientId");
        var previousSecret = Environment.GetEnvironmentVariable("Spotify__ClientSecret");
        Environment.SetEnvironmentVariable("Spotify__ClientId", id);
        Environment.SetEnvironmentVariable("Spotify__ClientSecret", secret);
        try
        {
            action();
        }
        finally
        {
            Environment.SetEnvironmentVariable("Spotify__ClientId", previousId);
            Environment.SetEnvironmentVariable("Spotify__ClientSecret", previousSecret);
        }
    }

    private sealed class FakeCatalogClient(
        StreamingProvider provider,
        IReadOnlyList<CatalogAlbum> albums,
        IReadOnlyList<CatalogTrack> tracks,
        bool fail = false) : IStreamingCatalogClient
    {
        public StreamingProvider Provider => provider;

        public List<string> Searches { get; } = [];

        public int TrackLookups { get; private set; }

        public Task<IReadOnlyList<CatalogAlbum>> SearchAlbumsAsync(string albumName, CancellationToken cancellationToken)
        {
            Searches.Add(albumName);
            return fail ? throw new HttpRequestException("503") : Task.FromResult(albums);
        }

        public Task<IReadOnlyList<CatalogTrack>> GetTracksAsync(CatalogAlbum album, CancellationToken cancellationToken)
        {
            TrackLookups++;
            return Task.FromResult(tracks);
        }
    }
}
