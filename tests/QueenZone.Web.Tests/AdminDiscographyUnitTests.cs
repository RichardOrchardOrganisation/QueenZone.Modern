using QueenZone.Data;
using QueenZone.Web;
using QueenZone.Web.Pages.Admin.Discography;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace QueenZone.Web.Tests;

public sealed class AdminDiscographyValidationTests
{
    [Fact]
    public void Album_validation_enforces_legacy_column_limits()
    {
        Assert.Empty(AdminDiscographyValidation.Validate(new AdminAlbumInput("Jazz", 1, new DateTime(1978, 11, 10), "Notes", true)));

        var errors = AdminDiscographyValidation.Validate(new AdminAlbumInput(
            new string('a', AdminDiscographyValidation.AlbumNameMaxLength + 1),
            0,
            new DateTime(1850, 1, 1),
            new string('n', AdminDiscographyValidation.GeneralNotesMaxLength + 1),
            false));

        Assert.Equal(4, errors.Count);
        Assert.Contains("Album name is required.", AdminDiscographyValidation.Validate(new AdminAlbumInput(" ", 1, null, null, false)));
    }

    [Fact]
    public void Song_validation_enforces_legacy_column_limits()
    {
        Assert.Empty(AdminDiscographyValidation.Validate(new AdminSongInput("Mustapha", "Allah", null, false)));
        Assert.Contains("Song title is required.", AdminDiscographyValidation.Validate(new AdminSongInput("", null, null, false)));

        var errors = AdminDiscographyValidation.Validate(new AdminSongInput(
            new string('t', AdminDiscographyValidation.SongTitleMaxLength + 1),
            new string('l', AdminDiscographyValidation.LyricsMaxLength + 1),
            new string('n', AdminDiscographyValidation.SongNotesMaxLength + 1),
            true));
        Assert.Equal(3, errors.Count);
    }

    [Theory]
    [InlineData(1, 3, 1)]
    [InlineData(4, 3, 4)]
    [InlineData(0, 3, 4)]
    [InlineData(9, 3, 4)]
    [InlineData(1, 0, 1)]
    public void ClampPosition_appends_out_of_range_positions(int requested, int count, int expected) =>
        Assert.Equal(expected, AdminDiscographyValidation.ClampPosition(requested, count));

    [Fact]
    public void MoveTo_reorders_and_clamps()
    {
        int[] order = [10, 20, 30, 40];

        Assert.Equal([30, 10, 20, 40], AdminDiscographyValidation.MoveTo(order, 30, 1));
        Assert.Equal([20, 30, 40, 10], AdminDiscographyValidation.MoveTo(order, 10, 99));
        Assert.Equal([10, 20, 30, 40], AdminDiscographyValidation.MoveTo(order, 20, 2));
        Assert.Equal([50, 10, 20, 30, 40], AdminDiscographyValidation.MoveTo(order, 50, 0));
    }

    [Fact]
    public void Admin_records_expose_cover_urls_and_inputs()
    {
        var song = new AdminAlbumSong(5, 2, 1, "Ogre Battle", null, null, true, "ogre.webp");
        var album = new AdminAlbum(2, "Queen II", 1, null, null, true, "t.webp", null, [song]);

        Assert.Equal(AlbumCoverUrl.Build("ogre.webp"), song.CoverUrl);
        Assert.Equal(new AdminSongInput("Ogre Battle", null, null, true), song.ToInput());
        Assert.Equal(AlbumCoverUrl.Build("t.webp"), album.CoverUrl);
        Assert.Equal("queen-ii", album.Slug);
        Assert.Equal(new AdminAlbumInput("Queen II", 1, null, null, true), album.ToInput());
        Assert.Null(new AdminAlbumListItem(1, "x", null, false, null, 0).ThumbnailUrl);
    }
}

public sealed class DiscographyTrackPositionsTests
{
    private static readonly IReadOnlyList<AdminAlbumSong> Songs =
    [
        new(1, 2, 1, "Procession", null, null, false, null),
        new(2, 2, 2, "Father to Son", null, null, false, null),
    ];

    [Fact]
    public void Insert_positions_name_the_following_track_and_end()
    {
        var options = DiscographyTrackPositions.ForInsert(Songs);

        Assert.Equal([1, 2, 3], options.Select(option => option.Value));
        Assert.Equal("1 — before “Procession”", options[0].Label);
        Assert.Equal("3 — at the end", options[2].Label);
        Assert.Equal("1 — at the end", Assert.Single(DiscographyTrackPositions.ForInsert([])).Label);
    }

    [Fact]
    public void Move_positions_mark_the_current_slot()
    {
        var options = DiscographyTrackPositions.ForMove(Songs, songId: 2);

        Assert.Equal("1 — where “Procession” is now", options[0].Label);
        Assert.Equal("2 — current position", options[1].Label);
    }
}

public sealed class InMemoryDiscographyStoreTests
{
    private static InMemoryDiscographyStore CreateStore() => new(SampleDiscographyData.CreateSeedAlbums());

    [Fact]
    public async Task Hidden_albums_stay_off_public_reads_but_show_in_admin()
    {
        var store = CreateStore();
        var admin = new InMemoryAdminDiscographyRepository(store);
        var publicRepository = new InMemoryDiscographyRepository(store);

        var albumId = await admin.CreateAlbumAsync(new AdminAlbumInput("  Hot Space ", 1, new DateTime(1982, 5, 21), " ", false));

        Assert.Contains(await admin.GetAlbumsAsync(), album => album.AlbumId == albumId && !album.IsActive);
        Assert.DoesNotContain(await publicRepository.GetAlbumsAsync(), album => album.AlbumId == albumId);
        Assert.Null(await publicRepository.GetAlbumByIdAsync(albumId));

        var created = await admin.GetAlbumAsync(albumId);
        Assert.Equal("Hot Space", created!.Name);
        Assert.Null(created.GeneralNotes);

        await admin.UpdateAlbumAsync(albumId, created.ToInput() with { IsActive = true });
        var detail = await publicRepository.GetAlbumByIdAsync(albumId);
        Assert.Equal("Queen", detail!.ArtistName);
        Assert.Equal(1982, detail.ReleaseYear);
    }

    [Fact]
    public async Task Song_writes_keep_a_dense_order_and_flow_to_song_pages()
    {
        var store = CreateStore();
        var admin = new InMemoryAdminDiscographyRepository(store);
        var publicRepository = new InMemoryDiscographyRepository(store);

        var songId = await admin.CreateSongAsync(2, new AdminSongInput("Ogre Battle Live", " Lyrics ", "  ", true), 3);
        var song = await admin.GetSongAsync(songId);
        Assert.Equal(3, song!.TrackNumber);
        Assert.Equal("Lyrics", song.Lyrics);
        Assert.Null(song.Notes);

        await admin.SetSongCoverAsync(songId, "single.webp");
        var detail = await publicRepository.GetSongBySlugAsync("ogre-battle-live");
        Assert.Equal(AlbumCoverUrl.Build("single.webp"), detail!.CoverUrl);
        Assert.True(Assert.Single(detail.Appearances).IsSingle);
        Assert.Equal(AlbumCoverUrl.Build("single.webp"), detail.Appearances[0].CoverUrl);

        await admin.MoveSongAsync(songId, 1);
        Assert.Equal("Ogre Battle Live", (await publicRepository.GetAlbumByIdAsync(2))!.Songs[0].Title);

        await admin.UpdateSongAsync(songId, new AdminSongInput("Renamed", null, "n", false));
        Assert.Equal("Renamed", (await admin.GetSongAsync(songId))!.Title);

        await admin.SetSongCoverAsync(songId, " ");
        Assert.Null((await admin.GetSongAsync(songId))!.CoverFileName);

        await admin.DeleteSongAsync(songId);
        Assert.Null(await admin.GetSongAsync(songId));
        Assert.Equal(
            Enumerable.Range(1, 11),
            (await admin.GetAlbumAsync(2))!.Songs.Select(s => s.TrackNumber));
    }

    [Fact]
    public async Task Album_cover_and_delete_round_trip()
    {
        var store = CreateStore();
        var admin = new InMemoryAdminDiscographyRepository(store);

        await admin.SetAlbumCoverAsync(1, new AdminAlbumCover("p.webp", 1200, 1200, "p_t.webp", 300, 300));
        var album = await admin.GetAlbumAsync(1);
        Assert.Equal("p.webp", album!.PictureFileName);
        Assert.Equal("p_t.webp", album.ThumbFileName);

        await admin.SetAlbumCoverAsync(1, null);
        Assert.Null((await admin.GetAlbumAsync(1))!.CoverUrl);

        await admin.DeleteAlbumAsync(1);
        Assert.Null(await admin.GetAlbumAsync(1));
        Assert.Equal("Queen", Assert.Single(await admin.GetArtistsAsync()).Name);
    }

    [Fact]
    public async Task Missing_records_throw()
    {
        var admin = new InMemoryAdminDiscographyRepository(CreateStore());
        var song = new AdminSongInput("x", null, null, false);

        await Assert.ThrowsAsync<InvalidOperationException>(() => admin.UpdateAlbumAsync(99, new AdminAlbumInput("x", 1, null, null, true)));
        await Assert.ThrowsAsync<InvalidOperationException>(() => admin.SetAlbumCoverAsync(99, null));
        await Assert.ThrowsAsync<InvalidOperationException>(() => admin.DeleteAlbumAsync(99));
        await Assert.ThrowsAsync<InvalidOperationException>(() => admin.CreateSongAsync(99, song, 1));
        await Assert.ThrowsAsync<InvalidOperationException>(() => admin.UpdateSongAsync(99, song));
        await Assert.ThrowsAsync<InvalidOperationException>(() => admin.MoveSongAsync(99, 1));
        await Assert.ThrowsAsync<InvalidOperationException>(() => admin.SetSongCoverAsync(99, null));
        await Assert.ThrowsAsync<InvalidOperationException>(() => admin.DeleteSongAsync(99));
        Assert.Null(await admin.GetSongAsync(99));
    }

    [Fact]
    public async Task Album_ids_stop_at_the_tinyint_limit()
    {
        var admin = new InMemoryAdminDiscographyRepository(new InMemoryDiscographyStore(
            [new AlbumSeed(255, "Last", 2000, "", [])]));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => admin.CreateAlbumAsync(new AdminAlbumInput("Overflow", 1, null, null, false)));
        Assert.Contains("255", ex.Message);
    }
}

public sealed class DiscographyCoverImageProcessorTests
{
    [Fact]
    public void ResolveCrop_uses_valid_square_crops_and_centres_otherwise()
    {
        Assert.Equal(new Rectangle(10, 20, 300, 300), DiscographyCoverImageProcessor.ResolveCrop(800, 600, new NewsArticleImageCrop(10, 20, 300, 300)));
        Assert.Equal(new Rectangle(100, 0, 600, 600), DiscographyCoverImageProcessor.ResolveCrop(800, 600, null));
        Assert.Equal(new Rectangle(100, 0, 600, 600), DiscographyCoverImageProcessor.ResolveCrop(800, 600, new NewsArticleImageCrop(0, 0, 600, 400)));
        Assert.Equal(new Rectangle(100, 0, 600, 600), DiscographyCoverImageProcessor.ResolveCrop(800, 600, new NewsArticleImageCrop(500, 0, 600, 600)));
        Assert.Equal(new Rectangle(0, 50, 400, 400), DiscographyCoverImageProcessor.CenterSquare(400, 500));
    }

    [Fact]
    public async Task ProcessAsync_produces_full_and_thumbnail_webp()
    {
        await using var source = await PngAsync(1600, 1600);

        await using var processed = await DiscographyCoverImageProcessor.ProcessAsync(source, "cover.png", null);

        Assert.Equal(DiscographyCoverImageProcessor.FullMaxSide, processed.FullWidth);
        Assert.Equal(DiscographyCoverImageProcessor.FullMaxSide, processed.FullHeight);
        Assert.Equal(DiscographyCoverImageProcessor.ThumbSize, processed.ThumbWidth);
        Assert.True(processed.Thumbnail.Length > 0);
    }

    [Fact]
    public async Task ProcessAsync_rejects_wrong_types_and_tiny_images()
    {
        await using var tiny = await PngAsync(200, 200);
        await Assert.ThrowsAsync<InvalidOperationException>(() => DiscographyCoverImageProcessor.ProcessAsync(tiny, "tiny.png", null));

        await using var text = new MemoryStream("not an image"u8.ToArray());
        await Assert.ThrowsAsync<InvalidOperationException>(() => DiscographyCoverImageProcessor.ProcessAsync(text, "x.png", null));
    }

    [Theory]
    [InlineData("0123456789abcdef0123456789abcdef.webp", true)]
    [InlineData("0123456789abcdef0123456789abcdef_t.webp", true)]
    [InlineData("queen-ii.jpg", false)]
    [InlineData("../0123456789abcdef0123456789abcdef.webp", false)]
    [InlineData(null, false)]
    public void Only_generated_cover_names_are_owned(string? fileName, bool expected) =>
        Assert.Equal(expected, AdminDiscographyService.IsOwnedCoverFileName(fileName));

    private static async Task<MemoryStream> PngAsync(int width, int height)
    {
        using var image = new Image<Rgba32>(width, height, new Rgba32(20, 90, 160));
        var stream = new MemoryStream();
        await image.SaveAsPngAsync(stream);
        stream.Position = 0;
        return stream;
    }
}
