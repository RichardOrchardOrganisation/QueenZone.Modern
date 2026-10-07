using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using QueenZone.Data;

namespace QueenZone.Tools;

/// <summary>One CSV row: a suggested link for an album (<see cref="AlbumSongId"/> null) or track.</summary>
internal sealed record StreamingLinkSuggestion(
    int AlbumId,
    int? AlbumSongId,
    string Title,
    StreamingProvider Provider,
    string? CandidateUrl,
    string? ExternalId,
    int? Score,
    IReadOnlyList<string> Flags,
    string? ExistingUrl);

/// <summary>
/// <c>suggest-streaming-links</c>: searches Apple Music (iTunes Search API) and Spotify (Web API)
/// for every album and track and writes a CSV for a human to review. It never writes to the
/// database; <c>apply-streaming-links</c> (#2182) imports rows marked <c>approved=yes</c>.
/// See <c>docs/architecture/streaming-links-backfill.md</c>.
/// </summary>
internal static class SuggestStreamingLinksCommand
{
    public const string NoMatchFlag = "no-match";

    public const string ErrorFlag = "error";

    public static readonly IReadOnlyList<string> CsvColumns =
        ["albumId", "albumSongId", "title", "provider", "candidateUrl", "externalId", "score", "flags", "existingUrl", "approved"];

    public static async Task<int> RunAsync(string[] args)
    {
        var options = SuggestStreamingLinksOptions.Parse(args);
        if (!options.IsValid)
        {
            WriteUsage(options.ErrorMessage);
            return 2;
        }

        var dbOptions = new DbContextOptionsBuilder<QueenZoneDbContext>().UseSqlServer(options.ConnectionString).Options;
        await using var dbContext = new QueenZoneDbContext(dbOptions);
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("QueenZone-Tools/1.0 (+https://queenzone.org)");
        await using var output = new StreamWriter(options.OutputPath, append: false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        return await RunAsync(options, new EfAdminDiscographyRepository(dbContext), CreateClients(options, http), output);
    }

    internal static IReadOnlyList<IStreamingCatalogClient> CreateClients(SuggestStreamingLinksOptions options, HttpClient http) =>
        options.Providers
            .Select<StreamingProvider, IStreamingCatalogClient>(provider => provider switch
            {
                StreamingProvider.AppleMusic => new AppleMusicCatalogClient(http, options.Country, AppleMusicCatalogClient.DefaultMinInterval),
                _ => new SpotifyCatalogClient(
                    http,
                    options.SpotifyClientId!,
                    options.SpotifyClientSecret!,
                    options.Country.ToUpperInvariant(),
                    SpotifyCatalogClient.DefaultMinInterval),
            })
            .ToList();

    internal static async Task<int> RunAsync(
        SuggestStreamingLinksOptions options,
        IAdminDiscographyRepository repository,
        IReadOnlyList<IStreamingCatalogClient> clients,
        TextWriter output)
    {
        var suggestions = await SuggestAsync(options, repository, clients, Console.Error, options.CancellationToken);
        WriteCsv(output, suggestions);
        await output.FlushAsync(options.CancellationToken);

        var withCandidate = suggestions.Count(row => row.CandidateUrl is not null);
        Console.WriteLine($"Rows written: {suggestions.Count}");
        Console.WriteLine($"With a candidate: {withCandidate}");
        Console.WriteLine($"Without a candidate: {suggestions.Count - withCandidate}");
        Console.WriteLine("Review the CSV, set approved=yes on rows to keep, then run apply-streaming-links.");
        return 0;
    }

    internal static async Task<IReadOnlyList<StreamingLinkSuggestion>> SuggestAsync(
        SuggestStreamingLinksOptions options,
        IAdminDiscographyRepository repository,
        IReadOnlyList<IStreamingCatalogClient> clients,
        TextWriter log,
        CancellationToken cancellationToken)
    {
        var rows = new List<StreamingLinkSuggestion>();
        var albums = await repository.GetAlbumsAsync(cancellationToken);
        foreach (var listItem in albums.Where(album => options.AlbumId is null || album.AlbumId == options.AlbumId))
        {
            var album = await repository.GetAlbumAsync(listItem.AlbumId, cancellationToken);
            if (album is null)
            {
                continue;
            }

            foreach (var client in clients)
            {
                try
                {
                    rows.AddRange(await SuggestForAlbumAsync(album, client, options.OnlyMissing, cancellationToken));
                }
                catch (HttpRequestException ex)
                {
                    await log.WriteLineAsync($"{client.Provider.DisplayName()} lookup failed for album {album.AlbumId} ({album.Name}): {ex.Message}");
                    rows.Add(new StreamingLinkSuggestion(album.AlbumId, null, album.Name, client.Provider, null, null, null, [ErrorFlag], Existing(album.StreamingLinks, client.Provider)));
                }
            }
        }

        return rows;
    }

    private static async Task<IReadOnlyList<StreamingLinkSuggestion>> SuggestForAlbumAsync(
        AdminAlbum album,
        IStreamingCatalogClient client,
        bool onlyMissing,
        CancellationToken cancellationToken)
    {
        var provider = client.Provider;
        var albumExisting = Existing(album.StreamingLinks, provider);
        var songsNeeding = album.Songs
            .Where(song => !onlyMissing || Existing(song.StreamingLinks, provider) is null)
            .ToList();
        var albumNeeded = !onlyMissing || albumExisting is null;
        if (!albumNeeded && songsNeeding.Count == 0)
        {
            return [];
        }

        var candidates = await client.SearchAlbumsAsync(album.Name, cancellationToken);
        var best = StreamingLinkMatcher.BestAlbum(album.Name, album.ReleaseDate?.Year, album.Songs.Count, candidates);
        var rows = new List<StreamingLinkSuggestion>();
        if (albumNeeded)
        {
            rows.Add(best is null
                ? new StreamingLinkSuggestion(album.AlbumId, null, album.Name, provider, null, null, null, [NoMatchFlag], albumExisting)
                : new StreamingLinkSuggestion(album.AlbumId, null, album.Name, provider, best.Candidate.Url, best.Candidate.ExternalId, best.Score, best.Flags, albumExisting));
        }

        var tracks = best is null || songsNeeding.Count == 0
            ? []
            : await client.GetTracksAsync(best.Candidate, cancellationToken);
        foreach (var song in songsNeeding)
        {
            var existing = Existing(song.StreamingLinks, provider);
            var match = StreamingLinkMatcher.BestTrack(song.Title, song.TrackNumber, album.Name, tracks);
            rows.Add(match is null
                ? new StreamingLinkSuggestion(album.AlbumId, song.SongId, song.Title, provider, null, null, null, [NoMatchFlag], existing)
                : new StreamingLinkSuggestion(album.AlbumId, song.SongId, song.Title, provider, match.Candidate.Url, match.Candidate.ExternalId, match.Score, match.Flags, existing));
        }

        return rows;
    }

    internal static void WriteCsv(TextWriter output, IEnumerable<StreamingLinkSuggestion> rows)
    {
        output.WriteLine(string.Join(',', CsvColumns));
        foreach (var row in rows)
        {
            output.WriteLine(string.Join(
                ',',
                Csv(row.AlbumId.ToString(CultureInfo.InvariantCulture)),
                Csv(row.AlbumSongId?.ToString(CultureInfo.InvariantCulture)),
                Csv(row.Title),
                Csv(row.Provider.Key()),
                Csv(row.CandidateUrl),
                Csv(row.ExternalId),
                Csv(row.Score?.ToString(CultureInfo.InvariantCulture)),
                Csv(string.Join(';', row.Flags)),
                Csv(row.ExistingUrl),
                string.Empty));
        }
    }

    /// <summary>RFC 4180 field; leading formula characters are prefixed so spreadsheets keep text as text.</summary>
    internal static string Csv(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        if (value[0] is '=' or '+' or '-' or '@')
        {
            value = "'" + value;
        }

        return value.IndexOfAny([',', '"', '\n', '\r']) >= 0
            ? "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\""
            : value;
    }

    private static string? Existing(IReadOnlyList<AdminStreamingLink> links, StreamingProvider provider) =>
        links.FirstOrDefault(link => link.Provider == provider)?.Url;

    private static void WriteUsage(string? errorMessage) =>
        ToolArgs.WriteUsage(
            errorMessage,
            "Usage:",
            "  dotnet run --project src/QueenZone.Tools -- suggest-streaming-links --out <file.csv> [options]",
            "",
            "Options:",
            "  --provider spotify|apple-music   Only one provider (default: both)",
            "  --album-id <id>                  Only one album",
            "  --only-missing                   Skip albums/tracks that already have a link",
            "  --country <cc>                   Storefront / market, default gb",
            "  --connection-string <value>      Or ConnectionStrings__QueenZoneLegacy / appsettings.Local.json",
            "  --settings-file <path>",
            "",
            "Spotify needs Spotify__ClientId and Spotify__ClientSecret (see docs/architecture/streaming-links-backfill.md).");
}

internal sealed class SuggestStreamingLinksOptions
{
    private SuggestStreamingLinksOptions()
    {
    }

    public string OutputPath { get; private init; } = string.Empty;

    public string ConnectionString { get; private init; } = string.Empty;

    public IReadOnlyList<StreamingProvider> Providers { get; private init; } = StreamingProviders.All;

    public int? AlbumId { get; private init; }

    public bool OnlyMissing { get; private init; }

    public string Country { get; private init; } = "gb";

    public string? SpotifyClientId { get; private init; }

    public string? SpotifyClientSecret { get; private init; }

    public bool IsValid { get; private init; }

    public string ErrorMessage { get; private init; } = string.Empty;

    public CancellationToken CancellationToken { get; init; }

    public static SuggestStreamingLinksOptions Parse(string[] args)
    {
        var state = new ParseState();
        for (var index = 0; index < args.Length; index++)
        {
            var error = ReadOption(args, ref index, state);
            if (error is not null)
            {
                return Invalid(error);
            }
        }

        return Complete(state);
    }

    /// <summary>Reads one option at <paramref name="index"/>; returns an error message or null.</summary>
    private static string? ReadOption(string[] args, ref int index, ParseState state)
    {
        var arg = args[index];
        if (ToolArgs.TryReadValue(args, ref index, "--out", out var outValue))
        {
            state.Output = outValue;
            return null;
        }

        if (ToolArgs.TryReadCommonOption(args, ref index, ref state.ConnectionString, ref state.StorageConnectionString, ref state.SettingsFile))
        {
            return null;
        }

        if (ToolArgs.TryReadValue(args, ref index, "--provider", out var providerValue))
        {
            return TryProvider(providerValue, state);
        }

        if (ToolArgs.TryReadInt(args, ref index, "--album-id", 1, out var albumValue, out var albumError))
        {
            state.AlbumId = albumValue;
            return albumError;
        }

        if (ToolArgs.TryReadValue(args, ref index, "--country", out var countryValue))
        {
            state.Country = countryValue.Trim().ToLowerInvariant();
            return state.Country.Length == 2 && state.Country.All(char.IsAsciiLetterLower)
                ? null
                : "--country must be a two-letter code such as gb or us.";
        }

        if (string.Equals(arg, "--only-missing", StringComparison.OrdinalIgnoreCase))
        {
            state.OnlyMissing = true;
            return null;
        }

        return $"Unsupported or incomplete argument: {arg}";
    }

    private static string? TryProvider(string value, ParseState state)
    {
        try
        {
            state.Providers = [StreamingProviders.FromKey(value.Trim().ToLowerInvariant())];
            return null;
        }
        catch (ArgumentOutOfRangeException)
        {
            return "--provider must be spotify or apple-music.";
        }
    }

    private static SuggestStreamingLinksOptions Complete(ParseState state)
    {
        if (string.IsNullOrWhiteSpace(state.Output))
        {
            return Invalid("--out is required.");
        }

        var connectionString = state.ConnectionString ?? Environment.GetEnvironmentVariable("ConnectionStrings__QueenZoneLegacy");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            var settings = ToolsLocalSettings.TryLoad(state.SettingsFile);
            connectionString = settings?.QueenZoneLegacy ?? settings?.QueenZoneLegacyLive;
        }

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return Invalid("--connection-string, ConnectionStrings__QueenZoneLegacy, or appsettings.Local.json is required.");
        }

        var spotifyId = Environment.GetEnvironmentVariable("Spotify__ClientId");
        var spotifySecret = Environment.GetEnvironmentVariable("Spotify__ClientSecret");
        if (state.Providers.Contains(StreamingProvider.Spotify)
            && (string.IsNullOrWhiteSpace(spotifyId) || string.IsNullOrWhiteSpace(spotifySecret)))
        {
            return Invalid("Spotify needs Spotify__ClientId and Spotify__ClientSecret. Set them, or pass --provider apple-music.");
        }

        return new SuggestStreamingLinksOptions
        {
            OutputPath = state.Output,
            ConnectionString = connectionString,
            Providers = state.Providers,
            AlbumId = state.AlbumId,
            OnlyMissing = state.OnlyMissing,
            Country = state.Country,
            SpotifyClientId = spotifyId,
            SpotifyClientSecret = spotifySecret,
            IsValid = true,
        };
    }

    private static SuggestStreamingLinksOptions Invalid(string message) => new() { ErrorMessage = message, IsValid = false };

    private sealed class ParseState
    {
        public string? Output;

        public string? ConnectionString;

        public string? StorageConnectionString;

        public string? SettingsFile;

        public IReadOnlyList<StreamingProvider> Providers = StreamingProviders.All;

        public int? AlbumId;

        public bool OnlyMissing;

        public string Country = "gb";
    }
}
