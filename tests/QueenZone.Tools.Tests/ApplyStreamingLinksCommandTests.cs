using QueenZone.Data;
using QueenZone.Tools;

namespace QueenZone.Tools.Tests;

[Collection(EnvironmentVariableCollection.Name)]
public sealed class ApplyStreamingLinksCommandTests
{
    private const string Header = "albumId,albumSongId,title,provider,candidateUrl,externalId,score,flags,existingUrl,approved";

    private const string SpotifyAlbum = "https://open.spotify.com/album/1GbtB4zTqAsyfZEsm1RZfx";

    private const string SpotifyTrack = "https://open.spotify.com/track/0000000000000000000001";

    [Fact]
    public async Task ToolsApp_routes_the_command_and_requires_a_file()
    {
        using var error = new StringWriter();
        var originalError = Console.Error;
        Console.SetError(error);
        try
        {
            Assert.Equal(2, await ToolsApp.RunAsync(["apply-streaming-links"]));
            Assert.Contains("--file is required.", error.ToString(), StringComparison.Ordinal);

            Assert.Equal(2, await ApplyStreamingLinksCommand.RunAsync(
                ["--file", Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}.csv"), "--connection-string", "Server=.;"]));
            Assert.Contains("CSV file was not found", error.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            Console.SetError(originalError);
        }
    }

    [Fact]
    public void Parse_reads_flags_and_rejects_unknown_arguments()
    {
        var options = ApplyStreamingLinksOptions.Parse(["--file", "x.csv", "--connection-string", "Server=.;", "--apply", "--overwrite-manual"]);
        Assert.True(options.IsValid);
        Assert.True(options.Apply);
        Assert.True(options.OverwriteManual);

        Assert.False(ApplyStreamingLinksOptions.Parse(["--file", "x.csv", "--connection-string", "Server=.;", "--force"]).IsValid);

        var previous = Environment.GetEnvironmentVariable("ConnectionStrings__QueenZoneLegacy");
        Environment.SetEnvironmentVariable("ConnectionStrings__QueenZoneLegacy", null);
        try
        {
            var noConnection = ApplyStreamingLinksOptions.Parse(
                ["--file", "x.csv", "--settings-file", Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}.json")]);
            Assert.False(noConnection.IsValid);
            Assert.Contains("--connection-string", noConnection.ErrorMessage, StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable("ConnectionStrings__QueenZoneLegacy", previous);
        }
    }

    [Fact]
    public async Task Dry_run_plans_without_writing_and_apply_writes_imported_links_once()
    {
        var (admin, _) = Repository();
        var csv = Csv(
            $"6,,News of the World,spotify,{SpotifyAlbum}?si=x,1GbtB4zTqAsyfZEsm1RZfx,100,remaster,,yes",
            $"6,6001,We Will Rock You,spotify,{SpotifyTrack},0000000000000000000001,100,,,YES",
            "6,6002,We Are the Champions,spotify,https://open.spotify.com/track/0000000000000000000002,x,90,,,",
            "6,6003,Sheer Heart Attack,spotify,,,,no-match,,no");

        var (exit, output) = await RunAsync(admin, csv);
        Assert.Equal(0, exit);
        Assert.Contains("Approved rows: 2 (not approved: 2)", output, StringComparison.Ordinal);
        Assert.Contains("Would insert: 2", output, StringComparison.Ordinal);
        Assert.Contains("Dry run only.", output, StringComparison.Ordinal);
        Assert.Empty((await admin.GetAlbumAsync(6))!.StreamingLinks);

        (exit, output) = await RunAsync(admin, csv, "--apply");
        Assert.Equal(0, exit);
        Assert.Contains("Inserted: 2", output, StringComparison.Ordinal);
        var album = (await admin.GetAlbumAsync(6))!;
        var albumLink = Assert.Single(album.StreamingLinks);
        Assert.Equal(SpotifyAlbum, albumLink.Url);
        Assert.Equal(StreamingLinkSource.Imported, albumLink.Source);
        Assert.Equal(ApplyStreamingLinksCommand.UpdatedBy, albumLink.UpdatedBy);
        Assert.Equal(SpotifyTrack, Assert.Single(album.Songs[0].StreamingLinks).Url);
        Assert.Empty(album.Songs[1].StreamingLinks);

        // Re-running the same file changes nothing.
        (exit, output) = await RunAsync(admin, csv, "--apply");
        Assert.Equal(0, exit);
        Assert.Contains("Inserted: 0", output, StringComparison.Ordinal);
        Assert.Contains("Unchanged: 2", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Manual_links_are_kept_unless_overwrite_manual_and_imported_links_update()
    {
        var (admin, _) = Repository();

        // Seed album 4 has manual Spotify links on the album and Bohemian Rhapsody (song 4011).
        var csv = Csv(
            $"4,,A Night at the Opera,spotify,{SpotifyAlbum},x,100,,https://open.spotify.com/album/0SampleNightAtTheOpera,yes");

        var (_, output) = await RunAsync(admin, csv, "--apply");
        Assert.Contains("Skipped (manual link kept): 1", output, StringComparison.Ordinal);
        Assert.Contains("pass --overwrite-manual to replace", output, StringComparison.Ordinal);
        Assert.Equal("https://open.spotify.com/album/0SampleNightAtTheOpera", Spotify(await admin.GetAlbumAsync(4)).Url);

        (_, output) = await RunAsync(admin, csv, "--apply", "--overwrite-manual");
        Assert.Contains("Updated: 1", output, StringComparison.Ordinal);
        var replaced = Spotify(await admin.GetAlbumAsync(4));
        Assert.Equal(SpotifyAlbum, replaced.Url);
        Assert.Equal(StreamingLinkSource.Imported, replaced.Source);

        // An imported link updates without --overwrite-manual.
        var newer = Csv("4,,A Night at the Opera,spotify,https://open.spotify.com/album/2222222222222222222222,x,100,,,yes");
        (_, output) = await RunAsync(admin, newer, "--apply");
        Assert.Contains("Updated: 1", output, StringComparison.Ordinal);
        Assert.Equal("https://open.spotify.com/album/2222222222222222222222", Spotify(await admin.GetAlbumAsync(4)).Url);
    }

    [Fact]
    public async Task Any_invalid_approved_row_blocks_every_write()
    {
        var (admin, _) = Repository();
        var csv = Csv(
            $"6,,News of the World,spotify,{SpotifyAlbum},x,100,,,yes",
            $"6,,News of the World,spotify,{SpotifyAlbum},x,100,,,yes",
            $"6,6001,We Will Rock You,apple-music,{SpotifyTrack},x,100,,,yes",
            $"6,6001,We Will Rock You,spotify,{SpotifyAlbum},x,100,,,yes",
            "6,6001,We Will Rock You,spotify,,,,no-match,,yes",
            $"6,1001,Keep Yourself Alive,spotify,{SpotifyTrack},x,100,,,yes",
            $"99,,Missing,spotify,{SpotifyAlbum},x,100,,,yes",
            $"x,,Bad,spotify,{SpotifyAlbum},x,100,,,yes",
            $"6,-1,Bad,spotify,{SpotifyTrack},x,100,,,yes",
            $"6,,Bad,deezer,{SpotifyAlbum},x,100,,,yes");

        using var error = new StringWriter();
        var originalError = Console.Error;
        Console.SetError(error);
        int exit;
        try
        {
            (exit, _) = await RunAsync(admin, csv, "--apply");
        }
        finally
        {
            Console.SetError(originalError);
        }

        var text = error.ToString();
        Assert.Equal(1, exit);
        Assert.Contains("Nothing was written", text, StringComparison.Ordinal);
        Assert.Contains("Row 3: approves a second spotify link for the same album as row 2.", text, StringComparison.Ordinal);
        Assert.Contains("Row 4: candidateUrl is a Spotify link but provider is apple-music.", text, StringComparison.Ordinal);
        Assert.Contains("Row 5: That is an album link from Spotify; this field needs a track link.", text, StringComparison.Ordinal);
        Assert.Contains("Row 6: approved but has no candidateUrl.", text, StringComparison.Ordinal);
        Assert.Contains("Row 9: albumId 'x' is not a positive number.", text, StringComparison.Ordinal);
        Assert.Contains("Row 10: albumSongId '-1' is not a positive number.", text, StringComparison.Ordinal);
        Assert.Contains("Row 11: provider 'deezer' must be spotify or apple-music.", text, StringComparison.Ordinal);
        Assert.Empty((await admin.GetAlbumAsync(6))!.StreamingLinks);
    }

    [Fact]
    public async Task Album_and_song_existence_are_checked_before_writing()
    {
        var (admin, _) = Repository();
        using var error = new StringWriter();
        var originalError = Console.Error;
        Console.SetError(error);
        try
        {
            var (exit, _) = await RunAsync(
                admin,
                Csv(
                    $"6,,News of the World,spotify,{SpotifyAlbum},x,100,,,yes",
                    $"6,1001,Keep Yourself Alive,spotify,{SpotifyTrack},x,100,,,yes",
                    $"99,,Missing,spotify,{SpotifyAlbum},x,100,,,yes"),
                "--apply");
            Assert.Equal(1, exit);
        }
        finally
        {
            Console.SetError(originalError);
        }

        Assert.Contains("Row 3: song 1001 is not on album 6 (News of the World).", error.ToString(), StringComparison.Ordinal);
        Assert.Contains("Row 4: album 99 does not exist.", error.ToString(), StringComparison.Ordinal);
        Assert.Empty((await admin.GetAlbumAsync(6))!.StreamingLinks);
    }

    [Fact]
    public void Columns_are_matched_by_name_with_a_bom_and_spreadsheet_quoting()
    {
        using var reader = new StringReader(
            "﻿approved,provider,candidateUrl,albumSongId,albumId,notes\n"
            + $"Yes,spotify,\"{SpotifyAlbum}\",,6,\"Reviewed, fine\"\n"
            + "\n");

        var (rows, notApproved, errors) = ApplyStreamingLinksCommand.ReadApproved(reader);

        Assert.Empty(errors);
        Assert.Equal(0, notApproved);
        var row = Assert.Single(rows);
        Assert.Equal(6, row.AlbumId);
        Assert.Null(row.AlbumSongId);
        Assert.Equal(SpotifyAlbum, row.Link.Url);
    }

    [Fact]
    public void Missing_columns_and_empty_files_are_reported()
    {
        using var empty = new StringReader(string.Empty);
        Assert.Equal("The CSV file is empty.", Assert.Single(ApplyStreamingLinksCommand.ReadApproved(empty).Errors));

        using var partial = new StringReader("albumId,provider\n1,spotify\n");
        Assert.Contains("Missing column(s): albumSongId, candidateUrl, approved", Assert.Single(ApplyStreamingLinksCommand.ReadApproved(partial).Errors), StringComparison.Ordinal);
    }

    [Fact]
    public void Suggest_output_round_trips_through_apply_parsing()
    {
        using var writer = new StringWriter();
        SuggestStreamingLinksCommand.WriteCsv(writer, [new StreamingLinkSuggestion(6, 6001, "=We Will, Rock You", StreamingProvider.Spotify, SpotifyTrack, "x", 100, ["remaster"], null)]);
        var approved = writer.ToString().Replace(",\r\n", ",yes\r\n", StringComparison.Ordinal).Replace(",\n", ",yes\n", StringComparison.Ordinal);

        var (rows, _, errors) = ApplyStreamingLinksCommand.ReadApproved(new StringReader(approved));

        Assert.Empty(errors);
        Assert.Equal(6001, Assert.Single(rows).AlbumSongId);
    }

    private static AdminStreamingLink Spotify(AdminAlbum? album) =>
        album!.StreamingLinks.Single(link => link.Provider == StreamingProvider.Spotify);

    private static string Csv(params string[] rows) => string.Join('\n', [Header, .. rows]) + "\n";

    private static async Task<(int Exit, string Output)> RunAsync(IAdminDiscographyRepository repository, string csv, params string[] flags)
    {
        var options = ApplyStreamingLinksOptions.Parse(["--file", "x.csv", "--connection-string", "Server=.;", .. flags]);
        using var output = new StringWriter();
        var original = Console.Out;
        Console.SetOut(output);
        try
        {
            var exit = await ApplyStreamingLinksCommand.RunAsync(options, repository, new StringReader(csv));
            return (exit, output.ToString());
        }
        finally
        {
            Console.SetOut(original);
        }
    }

    private static (IAdminDiscographyRepository Admin, InMemoryDiscographyStore Store) Repository()
    {
        var store = new InMemoryDiscographyStore(SampleDiscographyData.CreateSeedAlbums(), SampleDiscographyData.CreateSeedStreamingLinks());
        return (new InMemoryAdminDiscographyRepository(store), store);
    }
}
