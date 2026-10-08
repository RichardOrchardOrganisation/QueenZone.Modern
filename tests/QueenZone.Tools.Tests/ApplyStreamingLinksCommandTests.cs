using QueenZone.Data;
using QueenZone.Tools;

namespace QueenZone.Tools.Tests;

[Collection(EnvironmentVariableCollection.Name)]
public sealed class ApplyStreamingLinksCommandTests
{
    private const string Connection = "Server=.;Database=test;";

    private const string Header = "albumId,albumSongId,title,provider,candidateUrl,externalId,score,flags,existingUrl,approved";

    private const string NewSpotifyTrack = "https://open.spotify.com/track/1111111111111111111111";

    private const string NewAppleAlbum = "https://music.apple.com/gb/album/queen/1440650001";

    [Fact]
    public async Task ToolsApp_routes_the_command_and_rejects_missing_file_option()
    {
        using var error = new StringWriter();
        var originalError = Console.Error;
        Console.SetError(error);
        try
        {
            Assert.Equal(2, await ToolsApp.RunAsync(["apply-streaming-links", "--connection-string", Connection]));
            Assert.Contains("--file is required.", error.ToString(), StringComparison.Ordinal);
            Assert.Contains("apply-streaming-links --file", error.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            Console.SetError(originalError);
        }
    }

    [Fact]
    public void Parse_reads_flags_and_rejects_unknown_arguments()
    {
        var options = ApplyStreamingLinksOptions.Parse(["--file", "x.csv", "--connection-string", Connection, "--apply", "--overwrite-manual"]);
        Assert.True(options.IsValid);
        Assert.Equal("x.csv", options.FilePath);
        Assert.True(options.Apply);
        Assert.True(options.OverwriteManual);

        var dryRun = ApplyStreamingLinksOptions.Parse(["--file", "x.csv", "--connection-string", Connection]);
        Assert.False(dryRun.Apply);
        Assert.False(dryRun.OverwriteManual);

        var bogus = ApplyStreamingLinksOptions.Parse(["--file", "x.csv", "--connection-string", Connection, "--bogus"]);
        Assert.False(bogus.IsValid);
        Assert.Equal("Unsupported or incomplete argument: --bogus", bogus.ErrorMessage);
    }

    [Fact]
    public void Parse_needs_a_connection_string_even_for_a_dry_run()
    {
        var previous = Environment.GetEnvironmentVariable("ConnectionStrings__QueenZoneLegacy");
        Environment.SetEnvironmentVariable("ConnectionStrings__QueenZoneLegacy", null);
        try
        {
            var options = ApplyStreamingLinksOptions.Parse(
                ["--file", "x.csv", "--settings-file", Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}.json")]);
            Assert.False(options.IsValid);
            Assert.Contains("--connection-string", options.ErrorMessage, StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable("ConnectionStrings__QueenZoneLegacy", previous);
        }
    }

    [Fact]
    public async Task Dry_run_prints_insert_update_and_skip_without_writing()
    {
        var repository = Repository();
        var csv = Csv(
            Row("1", "", "apple-music", NewAppleAlbum, "yes"),
            Row("1", "1010", "spotify", NewSpotifyTrack, "yes"),
            Row("4", "", "spotify", "https://open.spotify.com/album/0SampleNightAtTheOpera", "yes"));

        var (exitCode, output) = await RunAsync(repository, csv);

        Assert.Equal(0, exitCode);
        Assert.Contains($"Row 2: insert album 1 apple-music: {NewAppleAlbum}", output, StringComparison.Ordinal);
        Assert.Contains("Row 3: skip (manual link; pass --overwrite-manual to replace) track 1010 (album 1) spotify:", output, StringComparison.Ordinal);
        Assert.Contains("Row 4: skip (unchanged) album 4 spotify:", output, StringComparison.Ordinal);
        Assert.Contains("Dry run only. No database changes were made.", output, StringComparison.Ordinal);

        var album = await repository.GetAlbumAsync(1);
        Assert.DoesNotContain(album!.StreamingLinks, link => link.Provider == StreamingProvider.AppleMusic);
        Assert.Equal(
            "https://open.spotify.com/track/0SampleSevenSeasOfRhye",
            album.Songs.Single(song => song.SongId == 1010).StreamingLinks.Single().Url);
    }

    [Fact]
    public async Task Only_rows_approved_yes_are_considered()
    {
        var repository = Repository();
        var csv = Csv(
            Row("1", "", "apple-music", NewAppleAlbum, " YES "),
            Row("2", "", "apple-music", "https://music.apple.com/gb/album/queen-ii/1440650002", ""),
            Row("3", "", "apple-music", "https://music.apple.com/gb/album/sheer-heart-attack/1440650003", "no"),
            Row("5", "", "apple-music", "not even a url", "maybe"));

        var (exitCode, output) = await RunAsync(repository, csv, "--apply");

        Assert.Equal(0, exitCode);
        Assert.Contains("Approved rows: 1", output, StringComparison.Ordinal);
        Assert.Contains("Not approved (ignored): 3", output, StringComparison.Ordinal);
        var applied = (await repository.GetAlbumAsync(1))!.StreamingLinks.Single(link => link.Provider == StreamingProvider.AppleMusic);
        Assert.Equal(NewAppleAlbum, applied.Url);
        Assert.Equal(StreamingLinkSource.Imported, applied.Source);
        Assert.Equal(ApplyStreamingLinksCommand.UpdatedBy, applied.UpdatedBy);
        Assert.Empty((await repository.GetAlbumAsync(2))!.StreamingLinks);
    }

    [Fact]
    public async Task Manual_links_are_kept_unless_overwrite_manual_is_passed()
    {
        var repository = Repository();
        var csv = Csv(Row("1", "1010", "spotify", NewSpotifyTrack, "yes"));

        var (kept, keptOutput) = await RunAsync(repository, csv, "--apply");
        Assert.Equal(0, kept);
        Assert.Contains("Skipped manual links: 1", keptOutput, StringComparison.Ordinal);
        var manual = await TrackLink(repository, 1, 1010, StreamingProvider.Spotify);
        Assert.Equal(StreamingLinkSource.Manual, manual.Source);
        Assert.Equal("https://open.spotify.com/track/0SampleSevenSeasOfRhye", manual.Url);

        var (replaced, replacedOutput) = await RunAsync(repository, csv, "--apply", "--overwrite-manual");
        Assert.Equal(0, replaced);
        Assert.Contains("Row 2: update track 1010 (album 1) spotify: https://open.spotify.com/track/0SampleSevenSeasOfRhye (manual) -> " + NewSpotifyTrack, replacedOutput, StringComparison.Ordinal);
        var imported = await TrackLink(repository, 1, 1010, StreamingProvider.Spotify);
        Assert.Equal(StreamingLinkSource.Imported, imported.Source);
        Assert.Equal(NewSpotifyTrack, imported.Url);
    }

    [Fact]
    public async Task Imported_links_are_updated_and_re_running_the_same_file_is_a_no_op()
    {
        var repository = Repository();
        var csv = Csv(
            Row("1", "", "apple-music", NewAppleAlbum, "yes"),
            Row("2", "2011", "spotify", "https://open.spotify.com/intl-de/track/2222222222222222222222?si=abc", "yes"));

        var (first, firstOutput) = await RunAsync(repository, csv, "--apply");
        Assert.Equal(0, first);
        Assert.Contains("Insert: 2", firstOutput, StringComparison.Ordinal);
        Assert.Contains("Failed: 0", firstOutput, StringComparison.Ordinal);
        Assert.Contains("cached in the web app for up to 30 minutes", firstOutput, StringComparison.Ordinal);
        Assert.Equal(
            "https://open.spotify.com/track/2222222222222222222222",
            (await TrackLink(repository, 2, 2011, StreamingProvider.Spotify)).Url);

        var (second, secondOutput) = await RunAsync(repository, csv, "--apply");
        Assert.Equal(0, second);
        Assert.Contains("Insert: 0", secondOutput, StringComparison.Ordinal);
        Assert.Contains("Update: 0", secondOutput, StringComparison.Ordinal);
        Assert.Contains("Unchanged: 2", secondOutput, StringComparison.Ordinal);

        // An imported link is replaced without --overwrite-manual.
        var corrected = Csv(Row("1", "", "apple-music", "https://music.apple.com/us/album/queen/1440650099", "yes"));
        var (third, thirdOutput) = await RunAsync(repository, corrected, "--apply");
        Assert.Equal(0, third);
        Assert.Contains("Update: 1", thirdOutput, StringComparison.Ordinal);
        Assert.Equal(
            "https://music.apple.com/us/album/queen/1440650099",
            (await repository.GetAlbumAsync(1))!.StreamingLinks.Single(link => link.Provider == StreamingProvider.AppleMusic).Url);
    }

    [Fact]
    public async Task Invalid_approved_rows_are_reported_skipped_and_fail_the_exit_code()
    {
        var repository = Repository();
        var csv = Csv(
            Row("x", "", "spotify", NewSpotifyTrack, "yes"),
            Row("1", "-3", "spotify", NewSpotifyTrack, "yes"),
            Row("1", "", "deezer", NewAppleAlbum, "yes"),
            Row("1", "", "spotify", "", "yes"),
            Row("1", "", "spotify", "https://spotify.link/abc", "yes"),
            Row("1", "", "spotify", NewSpotifyTrack, "yes"),
            Row("1", "", "spotify", NewAppleAlbum, "yes"),
            Row("99", "", "apple-music", NewAppleAlbum, "yes"),
            Row("1", "4011", "spotify", NewSpotifyTrack, "yes"),
            Row("1", "", "apple-music", NewAppleAlbum, "yes"),
            Row("1", "", "apple-music", "https://music.apple.com/gb/album/queen/1440650098", "yes"));

        var (exitCode, output) = await RunAsync(repository, csv, "--apply");

        Assert.Equal(1, exitCode);
        Assert.Contains("Row 2: skip (invalid) album ? ?: albumId must be a positive integer", output, StringComparison.Ordinal);
        Assert.Contains("Row 3: skip (invalid) album 1 ?: albumSongId must be blank or a positive integer", output, StringComparison.Ordinal);
        Assert.Contains("Row 4: skip (invalid) album 1 ?: provider must be spotify or apple-music", output, StringComparison.Ordinal);
        Assert.Contains("Row 5: skip (invalid) album 1 spotify: approved but candidateUrl is empty", output, StringComparison.Ordinal);
        Assert.Contains("Row 6: skip (invalid) album 1 spotify: Only open.spotify.com and music.apple.com links are accepted.", output, StringComparison.Ordinal);
        Assert.Contains("Row 7: skip (invalid) album 1 spotify: That is a track link from Spotify", output, StringComparison.Ordinal);
        Assert.Contains("Row 8: skip (invalid) album 1 spotify: candidateUrl is from Apple Music but provider is spotify", output, StringComparison.Ordinal);
        Assert.Contains("Row 9: skip (invalid) album 99 apple-music: album 99 was not found", output, StringComparison.Ordinal);
        Assert.Contains("Row 10: skip (invalid) track 4011 (album 1) spotify: track 4011 is not on album 1", output, StringComparison.Ordinal);
        Assert.Contains("Row 11: insert album 1 apple-music", output, StringComparison.Ordinal);
        Assert.Contains("Row 12: skip (invalid) album 1 apple-music: duplicate of row 11", output, StringComparison.Ordinal);
        Assert.Contains("Invalid: 10", output, StringComparison.Ordinal);

        // Valid rows still apply; the duplicate does not win.
        Assert.Equal(
            NewAppleAlbum,
            (await repository.GetAlbumAsync(1))!.StreamingLinks.Single(link => link.Provider == StreamingProvider.AppleMusic).Url);
    }

    [Fact]
    public void ReadCsv_maps_columns_by_header_and_rejects_missing_columns()
    {
        var reordered = ApplyStreamingLinksCommand.ReadCsv(new StringReader(
            "﻿approved,provider,candidateUrl,albumSongId,albumId,note\r\nyes,spotify,\"https://open.spotify.com/track/1111111111111111111111\",1010,1,\"a, b\"\r\n,,,,,\r\n"));

        var row = Assert.Single(reordered);
        Assert.Equal(2, row.RowNumber);
        Assert.Equal("1", row["albumId"]);
        Assert.Equal("1010", row["albumSongId"]);
        Assert.True(ApplyStreamingLinksCommand.IsApproved(row));

        var error = Assert.Throws<InvalidOperationException>(() => ApplyStreamingLinksCommand.ReadCsv(new StringReader("albumId,provider\n1,spotify\n")));
        Assert.Contains("albumSongId, candidateUrl, approved", error.Message, StringComparison.Ordinal);
        Assert.Throws<InvalidOperationException>(() => ApplyStreamingLinksCommand.ReadCsv(new StringReader(string.Empty)));
    }

    [Fact]
    public async Task A_suggest_csv_round_trips_into_apply()
    {
        var repository = Repository();
        using var written = new StringWriter();
        SuggestStreamingLinksCommand.WriteCsv(written, [
            new StreamingLinkSuggestion(1, null, "Queen", StreamingProvider.AppleMusic, NewAppleAlbum, "1440650001", 100, [], null),
        ]);
        var approved = written.ToString().TrimEnd() + "yes" + Environment.NewLine;

        var (exitCode, output) = await RunAsync(repository, approved);

        Assert.Equal(0, exitCode);
        Assert.Contains("Insert: 1", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Bad_csv_header_returns_usage_error()
    {
        var (exitCode, _) = await RunAsync(Repository(), "albumId\n1\n");

        Assert.Equal(2, exitCode);
    }

    private static async Task<(int ExitCode, string Output)> RunAsync(IAdminDiscographyRepository repository, string csv, params string[] extra)
    {
        var options = ApplyStreamingLinksOptions.Parse(["--file", "x.csv", "--connection-string", Connection, .. extra]);
        Assert.True(options.IsValid, options.ErrorMessage);
        using var output = new StringWriter();
        var originalError = Console.Error;
        Console.SetError(TextWriter.Null);
        try
        {
            var exitCode = await ApplyStreamingLinksCommand.RunAsync(options, repository, new StringReader(csv), output);
            return (exitCode, output.ToString());
        }
        finally
        {
            Console.SetError(originalError);
        }
    }

    private static async Task<AdminStreamingLink> TrackLink(IAdminDiscographyRepository repository, int albumId, int songId, StreamingProvider provider) =>
        (await repository.GetAlbumAsync(albumId))!.Songs.Single(song => song.SongId == songId).StreamingLinks.Single(link => link.Provider == provider);

    private static string Csv(params string[] rows) => Header + "\n" + string.Join("\n", rows) + "\n";

    private static string Row(string albumId, string songId, string provider, string url, string approved) =>
        $"{albumId},{songId},Title,{provider},{url},id,90,,,{approved}";

    private static InMemoryAdminDiscographyRepository Repository() =>
        new(new InMemoryDiscographyStore(SampleDiscographyData.CreateSeedAlbums(), SampleDiscographyData.CreateSeedStreamingLinks()));
}
