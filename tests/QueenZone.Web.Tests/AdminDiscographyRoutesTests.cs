using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using QueenZone.Storage;
using QueenZone.Web;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace QueenZone.Web.Tests;

public sealed partial class AdminDiscographyRoutesTests : IClassFixture<QueenZoneWebApplicationFactory>
{
    private const string TokenField = "__RequestVerificationToken";

    private readonly QueenZoneWebApplicationFactory factory;

    public AdminDiscographyRoutesTests(QueenZoneWebApplicationFactory factory)
    {
        this.factory = factory;
    }

    [Fact]
    public async Task Index_requires_an_admin()
    {
        var client = factory.CreateAnonymousClient(allowAutoRedirect: false);

        var response = await client.GetAsync("/admin/discography");

        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Index_lists_seed_albums_including_add_link()
    {
        var client = AdminClient();

        var body = await client.GetStringAsync("/admin/discography");

        Assert.Contains("Queen II", body);
        Assert.Contains("href=\"/admin/discography/new\"", body);
        Assert.Contains("href=\"/admin/discography/2\"", body);
    }

    [Fact]
    public async Task Dashboard_links_to_discography_admin()
    {
        var body = await AdminClient().GetStringAsync("/admin");

        Assert.Contains("href=\"/admin/discography\"", body);
    }

    [Fact]
    public async Task Unknown_album_and_song_return_not_found()
    {
        var client = AdminClient();

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/admin/discography/250")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/admin/discography/songs/999999")).StatusCode);
    }

    [Fact]
    public async Task New_album_rejects_blank_name_and_keeps_the_form()
    {
        var client = AdminClient();

        var response = await PostFormAsync(client, "/admin/discography/new", "/admin/discography/new", new()
        {
            ["AlbumForm.Name"] = "  ",
            ["AlbumForm.ArtistId"] = "1",
            ["AlbumForm.GeneralNotes"] = "Kept notes",
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Album name is required.", body);
        Assert.Contains("Kept notes", body);
    }

    [Fact]
    public async Task Songs_can_be_inserted_at_a_position_moved_and_deleted()
    {
        var client = AdminClient();
        var albumId = await CreateAlbumAsync(client, "Insert Order Album", visible: true);

        var procession = await AddSongAsync(client, albumId, "Procession", position: 99);
        await AddSongAsync(client, albumId, "Father to Son", position: 99);
        await AddSongAsync(client, albumId, "The Loser in the End", position: 99);

        // The Queen II case: a missing track goes in mid-album and later tracks shift down.
        var ogre = await AddSongAsync(client, albumId, "Ogre Battle", position: 2);
        Assert.Equal(
            ["Procession", "Ogre Battle", "Father to Son", "The Loser in the End"],
            await AdminTrackTitlesAsync(client, albumId));

        var publicBody = await client.GetStringAsync($"/discography/albums/{albumId}/insert-order-album");
        Assert.True(
            publicBody.IndexOf("Ogre Battle", StringComparison.Ordinal)
                < publicBody.IndexOf("Father to Son", StringComparison.Ordinal));

        var move = await PostFormAsync(client, AlbumPath(albumId), AlbumPath(albumId) + "?handler=MoveSong", new()
        {
            ["songId"] = ogre.ToString(CultureInfo.InvariantCulture),
            ["position"] = "4",
        });
        Assert.Equal(HttpStatusCode.Redirect, move.StatusCode);
        Assert.Equal(
            ["Procession", "Father to Son", "The Loser in the End", "Ogre Battle"],
            await AdminTrackTitlesAsync(client, albumId));

        var delete = await PostFormAsync(client, AlbumPath(albumId), AlbumPath(albumId) + "?handler=DeleteSong", new()
        {
            ["songId"] = procession.ToString(CultureInfo.InvariantCulture),
        });
        Assert.Equal(HttpStatusCode.Redirect, delete.StatusCode);
        Assert.Equal(
            ["Father to Son", "The Loser in the End", "Ogre Battle"],
            await AdminTrackTitlesAsync(client, albumId));
    }

    [Fact]
    public async Task Add_song_with_blank_title_shows_errors_and_keeps_lyrics()
    {
        var client = AdminClient();
        var albumId = await CreateAlbumAsync(client, "Validation Album", visible: false);

        var response = await PostFormAsync(client, AlbumPath(albumId), AlbumPath(albumId) + "?handler=AddSong", new()
        {
            ["NewSong.Title"] = "",
            ["NewSong.Position"] = "1",
            ["NewSong.Lyrics"] = "Lyrics that should survive",
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Song title is required.", body);
        Assert.Contains("Lyrics that should survive", body);
        Assert.Contains("value=\"Validation Album\"", body);
    }

    [Fact]
    public async Task Album_save_updates_details_and_rejects_invalid_input()
    {
        var client = AdminClient();
        var albumId = await CreateAlbumAsync(client, "Rename Me", visible: false);

        var invalid = await PostFormAsync(client, AlbumPath(albumId), AlbumPath(albumId) + "?handler=Save", new()
        {
            ["AlbumForm.Name"] = "",
            ["AlbumForm.ArtistId"] = "1",
        });
        Assert.Equal(HttpStatusCode.OK, invalid.StatusCode);
        Assert.Contains("Album name is required.", await invalid.Content.ReadAsStringAsync());

        var saved = await PostFormAsync(client, AlbumPath(albumId), AlbumPath(albumId) + "?handler=Save", new()
        {
            ["AlbumForm.Name"] = "Renamed Album",
            ["AlbumForm.ArtistId"] = "1",
            ["AlbumForm.ReleaseDate"] = "1980-06-30",
            ["AlbumForm.IsActive"] = "true",
        });
        Assert.Equal(HttpStatusCode.Redirect, saved.StatusCode);

        var publicResponse = await client.GetAsync($"/discography/albums/{albumId}/renamed-album");
        Assert.Equal(HttpStatusCode.OK, publicResponse.StatusCode);
        Assert.Contains("Renamed Album", await client.GetStringAsync("/discography"));
    }

    [Fact]
    public async Task Album_cover_upload_stores_square_webp_and_remove_deletes_it()
    {
        var client = AdminClient();
        var albumId = await CreateAlbumAsync(client, "Cover Album", visible: true);

        var upload = await PostCoverAsync(client, AlbumPath(albumId), 800, 600, crop: (100, 0, 600, 600));
        Assert.Equal(HttpStatusCode.Redirect, upload.StatusCode);

        var body = await client.GetStringAsync(AlbumPath(albumId));
        Assert.Contains("Album cover updated.", body);
        var fileName = CoverFileName().Match(body).Groups[1].Value;
        Assert.NotEmpty(fileName);

        var blobs = factory.Services.GetRequiredService<IGalleryPhotoBlobService>();
        await using (var stored = await blobs.OpenReadAsync(AdminDiscographyService.CoverContainer, AdminDiscographyService.BlobName(fileName)))
        {
            Assert.NotNull(stored);
            using var image = await Image.LoadAsync(stored);
            Assert.Equal(600, image.Width);
            Assert.Equal(600, image.Height);
        }

        var thumbName = fileName.Replace(".webp", "_t.webp", StringComparison.Ordinal);
        await using (var thumb = await blobs.OpenReadAsync(AdminDiscographyService.CoverContainer, AdminDiscographyService.BlobName(thumbName)))
        {
            using var image = await Image.LoadAsync(thumb!);
            Assert.Equal(DiscographyCoverImageProcessor.ThumbSize, image.Width);
        }

        Assert.Contains(fileName, await client.GetStringAsync($"/discography/albums/{albumId}/cover-album"));

        var remove = await PostFormAsync(client, AlbumPath(albumId), AlbumPath(albumId) + "?handler=RemoveCover", new());
        Assert.Equal(HttpStatusCode.Redirect, remove.StatusCode);
        Assert.Null(await blobs.OpenReadAsync(AdminDiscographyService.CoverContainer, AdminDiscographyService.BlobName(fileName)));
        Assert.DoesNotContain(fileName, await client.GetStringAsync(AlbumPath(albumId)));
    }

    [Fact]
    public async Task Cover_upload_without_a_file_shows_an_error()
    {
        var client = AdminClient();
        var albumId = await CreateAlbumAsync(client, "No File Album", visible: false);

        var response = await PostFormAsync(client, AlbumPath(albumId), AlbumPath(albumId) + "?handler=Cover", new());

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("Choose an image to upload.", await client.GetStringAsync(AlbumPath(albumId)));
    }

    [Fact]
    public async Task Cover_upload_rejects_images_smaller_than_the_minimum()
    {
        var client = AdminClient();
        var albumId = await CreateAlbumAsync(client, "Tiny Cover Album", visible: false);

        await PostCoverAsync(client, AlbumPath(albumId), 120, 120, crop: null);

        Assert.Contains("Cover image is too small", await client.GetStringAsync(AlbumPath(albumId)));
    }

    [Fact]
    public async Task Song_page_saves_edits_moves_track_uploads_single_cover_and_deletes()
    {
        var client = AdminClient();
        var albumId = await CreateAlbumAsync(client, "Singles Album", visible: true);
        await AddSongAsync(client, albumId, "Opener", position: 1);
        var songId = await AddSongAsync(client, albumId, "Hit Single", position: 2);
        var songPath = $"/admin/discography/songs/{songId}";

        var page = await client.GetStringAsync(songPath);
        Assert.Contains("Track 2 of 2", page);

        var invalid = await PostFormAsync(client, songPath, songPath + "?handler=Save", new()
        {
            ["SongForm.Title"] = "",
            ["SongForm.Position"] = "2",
        });
        Assert.Equal(HttpStatusCode.OK, invalid.StatusCode);
        Assert.Contains("Song title is required.", await invalid.Content.ReadAsStringAsync());

        var save = await PostFormAsync(client, songPath, songPath + "?handler=Save", new()
        {
            ["SongForm.Title"] = "Hit Single",
            ["SongForm.Position"] = "1",
            ["SongForm.IsSingle"] = "true",
            ["SongForm.Notes"] = "Number one",
            ["SongForm.Lyrics"] = "La la la",
        });
        Assert.Equal(HttpStatusCode.Redirect, save.StatusCode);
        Assert.Equal(["Hit Single", "Opener"], await AdminTrackTitlesAsync(client, albumId));

        var upload = await PostCoverAsync(client, songPath, 400, 400, crop: null);
        Assert.Equal(HttpStatusCode.Redirect, upload.StatusCode);
        var songBody = await client.GetStringAsync(songPath);
        Assert.Contains("Single cover updated.", songBody);
        var fileName = CoverFileName().Match(songBody).Groups[1].Value;

        var publicAlbum = await client.GetStringAsync($"/discography/albums/{albumId}/singles-album");
        Assert.Contains("qz-album-track__cover", publicAlbum);
        Assert.Contains(fileName, publicAlbum);
        Assert.Contains(fileName, await client.GetStringAsync("/songs/hit-single"));

        var blobs = factory.Services.GetRequiredService<IGalleryPhotoBlobService>();
        var delete = await PostFormAsync(client, songPath, songPath + "?handler=Delete", new());
        Assert.Equal(HttpStatusCode.Redirect, delete.StatusCode);
        Assert.Equal(AlbumPath(albumId) + "#tracklist", delete.Headers.Location!.OriginalString);
        Assert.Equal(["Opener"], await AdminTrackTitlesAsync(client, albumId));
        Assert.Null(await blobs.OpenReadAsync(AdminDiscographyService.CoverContainer, AdminDiscographyService.BlobName(fileName)));
    }

    [Fact]
    public async Task Song_cover_can_be_removed()
    {
        var client = AdminClient();
        var albumId = await CreateAlbumAsync(client, "Remove Single Cover", visible: false);
        var songId = await AddSongAsync(client, albumId, "Covered", position: 1);
        var songPath = $"/admin/discography/songs/{songId}";
        await PostCoverAsync(client, songPath, 320, 320, crop: null);

        var remove = await PostFormAsync(client, songPath, songPath + "?handler=RemoveCover", new());

        Assert.Equal(HttpStatusCode.Redirect, remove.StatusCode);
        var body = await client.GetStringAsync(songPath);
        Assert.Contains("Single cover removed.", body);
        Assert.DoesNotMatch(CoverFileName(), body);
    }

    [Fact]
    public async Task Deleting_an_album_removes_it_and_its_songs()
    {
        var client = AdminClient();
        var albumId = await CreateAlbumAsync(client, "Doomed Album", visible: true);
        var songId = await AddSongAsync(client, albumId, "Doomed Song", position: 1);

        var delete = await PostFormAsync(client, AlbumPath(albumId), AlbumPath(albumId) + "?handler=Delete", new());

        Assert.Equal(HttpStatusCode.Redirect, delete.StatusCode);
        Assert.Equal("/admin/discography", delete.Headers.Location!.OriginalString);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(AlbumPath(albumId))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/admin/discography/songs/{songId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/discography/albums/{albumId}/doomed-album")).StatusCode);
    }

    [Fact]
    public async Task Writes_on_missing_records_flash_an_error()
    {
        var client = AdminClient();
        var albumId = await CreateAlbumAsync(client, "Missing Song Album", visible: false);

        var response = await PostFormAsync(client, AlbumPath(albumId), AlbumPath(albumId) + "?handler=MoveSong", new()
        {
            ["songId"] = "987654",
            ["position"] = "1",
        });

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("Song 987654 was not found.", await client.GetStringAsync(AlbumPath(albumId)));
    }

    [Fact]
    public async Task Album_streaming_links_save_normalised_skip_unchanged_and_remove()
    {
        var client = AdminClient();
        var albumId = await CreateAlbumAsync(client, "Streaming Album", visible: true);
        var cache = factory.Services.GetRequiredService<PublicQueryCacheService>();
        Assert.Empty((await cache.GetDiscographyAlbumByIdAsync(albumId))!.StreamingLinks);

        var save = await PostStreamingLinksAsync(client, AlbumPath(albumId), new()
        {
            ["StreamingLinksForm.Spotify"] = $"{SpotifyAlbumUrl}?si=tracking",
            ["StreamingLinksForm.AppleMusic"] = "",
        });
        Assert.Equal(HttpStatusCode.Redirect, save.StatusCode);
        Assert.Equal(AlbumPath(albumId) + "#streaming-links", save.Headers.Location!.OriginalString);

        var body = await client.GetStringAsync(AlbumPath(albumId));
        Assert.Contains("Streaming links saved.", body);
        Assert.Contains($"value=\"{SpotifyAlbumUrl}\"", body);
        Assert.DoesNotContain("si=tracking", body);
        Assert.Contains("Open on Spotify", body);
        Assert.Contains($"by {AdminHttpTestHelpers.AdminEmail}", body);

        // The admin write invalidates the cached public album.
        var link = Assert.Single((await cache.GetDiscographyAlbumByIdAsync(albumId))!.StreamingLinks);
        Assert.Equal(new QueenZone.Data.StreamingLink(QueenZone.Data.StreamingProvider.Spotify, SpotifyAlbumUrl), link);

        await PostStreamingLinksAsync(client, AlbumPath(albumId), new() { ["StreamingLinksForm.Spotify"] = SpotifyAlbumUrl });
        Assert.Contains("No changes to the streaming links.", await client.GetStringAsync(AlbumPath(albumId)));

        await PostStreamingLinksAsync(client, AlbumPath(albumId), new() { ["StreamingLinksForm.Spotify"] = " " });
        Assert.DoesNotContain("Open on Spotify", await client.GetStringAsync(AlbumPath(albumId)));
        Assert.Empty((await cache.GetDiscographyAlbumByIdAsync(albumId))!.StreamingLinks);
    }

    [Fact]
    public async Task Invalid_streaming_link_shows_the_error_keeps_input_and_saves_nothing()
    {
        var client = AdminClient();
        var albumId = await CreateAlbumAsync(client, "Bad Link Album", visible: false);

        var response = await PostStreamingLinksAsync(client, AlbumPath(albumId), new()
        {
            ["StreamingLinksForm.Spotify"] = SpotifyAlbumUrl,
            ["StreamingLinksForm.AppleMusic"] = "https://apple.co/short",
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Apple Music link: Only open.spotify.com and music.apple.com links are accepted.", body);
        Assert.Contains("value=\"https://apple.co/short\"", body);
        Assert.Contains("value=\"Bad Link Album\"", body);
        Assert.DoesNotContain("Open on Spotify", await client.GetStringAsync(AlbumPath(albumId)));
    }

    [Fact]
    public async Task Song_streaming_links_need_track_links()
    {
        var client = AdminClient();
        var albumId = await CreateAlbumAsync(client, "Track Link Album", visible: true);
        var songId = await AddSongAsync(client, albumId, "Linked Song", position: 1);
        var songPath = $"/admin/discography/songs/{songId}";

        var wrongKind = await PostStreamingLinksAsync(client, songPath, new() { ["StreamingLinksForm.Spotify"] = SpotifyAlbumUrl });
        Assert.Equal(HttpStatusCode.OK, wrongKind.StatusCode);
        Assert.Contains("this field needs a track link.", await wrongKind.Content.ReadAsStringAsync());

        var save = await PostStreamingLinksAsync(client, songPath, new()
        {
            ["StreamingLinksForm.AppleMusic"] = "https://music.apple.com/gb/album/track-link-album/111?i=222&ls",
        });
        Assert.Equal(songPath + "#streaming-links", save.Headers.Location!.OriginalString);
        Assert.Contains("Open on Apple Music", await client.GetStringAsync(songPath));
        Assert.Contains("· Apple Music</span>", await client.GetStringAsync(AlbumPath(albumId)));

        var publicSong = Assert.Single((await factory.Services.GetRequiredService<PublicQueryCacheService>()
            .GetDiscographyAlbumByIdAsync(albumId))!.Songs);
        Assert.Equal("https://music.apple.com/gb/album/track-link-album/111?i=222", Assert.Single(publicSong.StreamingLinks).Url);
    }

    [Fact]
    public async Task Index_shows_link_coverage_and_filters_albums_missing_links()
    {
        var client = AdminClient();
        var albumId = await CreateAlbumAsync(client, "Unlinked Coverage Album", visible: false);

        var all = await client.GetStringAsync("/admin/discography");
        Assert.Contains("Streaming links", all);
        Assert.Contains("href=\"/admin/discography?missing=links\"", all);

        var missing = await client.GetStringAsync("/admin/discography?missing=links");
        Assert.Contains($"href=\"/admin/discography/{albumId}\"", missing);

        // Seed album 4 (A Night at the Opera) has both album-level links.
        Assert.DoesNotContain("href=\"/admin/discography/4\"", missing);
        Assert.Contains("href=\"/admin/discography/4\"", all);
    }

    private const string SpotifyAlbumUrl = "https://open.spotify.com/album/4KfrGvYXcZsFgMHVIdzkoW";

    private static Task<HttpResponseMessage> PostStreamingLinksAsync(
        HttpClient client,
        string pagePath,
        Dictionary<string, string> fields) =>
        PostFormAsync(client, pagePath, pagePath + "?handler=StreamingLinks", fields);

    private static string AlbumPath(int albumId) => $"/admin/discography/{albumId}";

    private HttpClient AdminClient() => AdminHttpTestHelpers.CreateClient(factory, AdminHttpTestHelpers.AdminEmail);

    private static async Task<int> CreateAlbumAsync(HttpClient client, string name, bool visible)
    {
        var fields = new Dictionary<string, string>
        {
            ["AlbumForm.Name"] = name,
            ["AlbumForm.ArtistId"] = "1",
            ["AlbumForm.ReleaseDate"] = "1979-01-01",
        };
        if (visible)
        {
            fields["AlbumForm.IsActive"] = "true";
        }

        var response = await PostFormAsync(client, "/admin/discography/new", "/admin/discography/new", fields);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        return int.Parse(response.Headers.Location!.OriginalString.Split('/')[^1], CultureInfo.InvariantCulture);
    }

    private static async Task<int> AddSongAsync(HttpClient client, int albumId, string title, int position)
    {
        var response = await PostFormAsync(client, AlbumPath(albumId), AlbumPath(albumId) + "?handler=AddSong", new()
        {
            ["NewSong.Title"] = title,
            ["NewSong.Position"] = position.ToString(CultureInfo.InvariantCulture),
        });
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);

        var body = await client.GetStringAsync(AlbumPath(albumId));
        var match = Regex.Match(body, $"href=\"/admin/discography/songs/(\\d+)\">{Regex.Escape(System.Net.WebUtility.HtmlEncode(title))}</a>");
        Assert.True(match.Success, $"Song link for {title} not found.");
        return int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
    }

    private static async Task<IReadOnlyList<string>> AdminTrackTitlesAsync(HttpClient client, int albumId)
    {
        var body = await client.GetStringAsync(AlbumPath(albumId));
        return TrackLink().Matches(body)
            .Select(match => System.Net.WebUtility.HtmlDecode(match.Groups[1].Value))
            .Where(text => text != "Edit")
            .ToList();
    }

    private static async Task<HttpResponseMessage> PostFormAsync(
        HttpClient client,
        string formPath,
        string postPath,
        Dictionary<string, string> fields)
    {
        var formPage = await client.GetStringAsync(formPath);
        fields[TokenField] = AdminHttpTestHelpers.ExtractAntiforgeryToken(formPage);
        return await client.PostAsync(postPath, new FormUrlEncodedContent(fields));
    }

    private static async Task<HttpResponseMessage> PostCoverAsync(
        HttpClient client,
        string pagePath,
        int width,
        int height,
        (int X, int Y, int Width, int Height)? crop)
    {
        var page = await client.GetStringAsync(pagePath);
        using var image = new Image<Rgba32>(width, height, new Rgba32(180, 40, 40));
        await using var stream = new MemoryStream();
        await image.SaveAsPngAsync(stream);
        stream.Position = 0;

        using var content = new MultipartFormDataContent();
        content.Add(new StringContent(AdminHttpTestHelpers.ExtractAntiforgeryToken(page)), TokenField);
        if (crop is var (x, y, w, h))
        {
            content.Add(new StringContent(x.ToString(CultureInfo.InvariantCulture)), "cropX");
            content.Add(new StringContent(y.ToString(CultureInfo.InvariantCulture)), "cropY");
            content.Add(new StringContent(w.ToString(CultureInfo.InvariantCulture)), "cropWidth");
            content.Add(new StringContent(h.ToString(CultureInfo.InvariantCulture)), "cropHeight");
        }

        var file = new StreamContent(stream);
        file.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        content.Add(file, "coverFile", "cover.png");
        return await client.PostAsync(pagePath + "?handler=Cover", content);
    }

    [GeneratedRegex("href=\"/admin/discography/songs/\\d+\">([^<]+)</a>")]
    private static partial Regex TrackLink();

    [GeneratedRegex("images/discography/([0-9a-f]{32}\\.webp)")]
    private static partial Regex CoverFileName();
}
