using QueenZone.Data;

namespace QueenZone.Web.Tests;

/// <summary>
/// Read-model and in-memory store behaviour for discography streaming links (#2177).
/// SQL Server coverage lives in <c>AdminDiscographyRepositorySqlServerTests</c>.
/// </summary>
public sealed class DiscographyStreamingLinkTests
{
    private const string SpotifyTrack = "https://open.spotify.com/track/4KfrGvYXcZsFgMHVIdzkoW";

    [Fact]
    public void Song_header_takes_each_provider_from_the_earliest_appearance_that_has_it()
    {
        var spotifyLater = new StreamingLink(StreamingProvider.Spotify, "https://open.spotify.com/track/later");
        var spotifyEarly = new StreamingLink(StreamingProvider.Spotify, "https://open.spotify.com/track/early");
        var appleLater = new StreamingLink(StreamingProvider.AppleMusic, "https://music.apple.com/gb/song/later/2");
        var tracks = new[]
        {
            new SongTrackSource(30, "Love of My Life", null, null, false, 9, "Live Killers", new DateTime(1979, 6, 22))
            {
                StreamingLinks = [spotifyLater, appleLater],
            },
            new SongTrackSource(5, "Love of My Life", null, null, false, 4, "A Night at the Opera", new DateTime(1975, 11, 21))
            {
                StreamingLinks = [spotifyEarly],
            },
            new SongTrackSource(40, "Love of My Life", null, null, false, 12, "Greatest Hits", new DateTime(1981, 10, 26)),
        };

        var song = SongCatalog.DetailFor(tracks, "love-of-my-life")!;

        Assert.Equal([spotifyEarly, appleLater], song.StreamingLinks);
        Assert.Equal([spotifyEarly], song.Appearances[0].StreamingLinks);
        Assert.Equal([spotifyLater, appleLater], song.Appearances[1].StreamingLinks);
        Assert.Empty(song.Appearances[2].StreamingLinks);
    }

    [Fact]
    public void Tracks_from_albums_carry_track_links_but_not_album_links()
    {
        var trackLink = new StreamingLink(StreamingProvider.Spotify, SpotifyTrack);
        var album = new AlbumDetail(4, "A Night at the Opera", "a-night-at-the-opera", 1975, "Queen", null, null,
            [new AlbumSong(11, "Bohemian Rhapsody", true, null, null) { StreamingLinks = [trackLink] }])
        {
            StreamingLinks = [new StreamingLink(StreamingProvider.Spotify, "https://open.spotify.com/album/x")],
        };

        var track = Assert.Single(SongCatalog.TracksFromAlbums([album]));

        Assert.Equal([trackLink], track.StreamingLinks);
    }

    [Fact]
    public async Task Sample_data_exposes_album_track_and_cross_appearance_links()
    {
        var repository = new InMemoryDiscographyRepository(new InMemoryDiscographyStore(
            SampleDiscographyData.CreateSeedAlbums(),
            SampleDiscographyData.CreateSeedStreamingLinks()));

        var opera = (await repository.GetAlbumByIdAsync(4))!;
        Assert.Equal([StreamingProvider.Spotify, StreamingProvider.AppleMusic], opera.StreamingLinks.Select(link => link.Provider));
        var bohemian = Assert.Single(opera.Songs, song => song.Title == "Bohemian Rhapsody");
        Assert.Equal(2, bohemian.StreamingLinks.Count);
        Assert.All(opera.Songs.Where(song => song.Title != "Bohemian Rhapsody"), song => Assert.Empty(song.StreamingLinks));

        var sevenSeas = (await repository.GetSongBySlugAsync("seven-seas-of-rhye"))!;
        Assert.Equal(["Queen", "Queen II"], sevenSeas.Appearances.Select(appearance => appearance.AlbumName));
        Assert.Equal([StreamingProvider.Spotify, StreamingProvider.AppleMusic], sevenSeas.StreamingLinks.Select(link => link.Provider));
        Assert.Equal(StreamingProvider.Spotify, Assert.Single(sevenSeas.Appearances[0].StreamingLinks).Provider);
        Assert.Equal(StreamingProvider.AppleMusic, Assert.Single(sevenSeas.Appearances[1].StreamingLinks).Provider);
    }

    [Fact]
    public async Task Admin_writes_set_replace_and_remove_links()
    {
        var store = new InMemoryDiscographyStore(SampleDiscographyData.CreateSeedAlbums());
        var admin = new InMemoryAdminDiscographyRepository(store);
        var repository = new InMemoryDiscographyRepository(store);
        var songId = (await admin.GetAlbumAsync(6))!.Songs[0].SongId;

        await admin.SetAlbumStreamingLinkAsync(6, StreamingProvider.AppleMusic, Write("https://music.apple.com/gb/album/news-of-the-world/1", StreamingLinkKind.Album));
        await admin.SetSongStreamingLinkAsync(songId, StreamingProvider.Spotify, Write(SpotifyTrack + "?si=1", StreamingLinkKind.Track, " admin@example.test "));

        var adminAlbum = (await admin.GetAlbumAsync(6))!;
        var albumLink = Assert.Single(adminAlbum.StreamingLinks);
        Assert.Equal("1", albumLink.ExternalId);
        Assert.Equal(StreamingLinkSource.Manual, albumLink.Source);
        var songLink = Assert.Single((await admin.GetSongAsync(songId))!.StreamingLinks);
        Assert.Equal(SpotifyTrack, songLink.Url);
        Assert.Equal("admin@example.test", songLink.UpdatedBy);
        Assert.Equal(SpotifyTrack, Assert.Single((await repository.GetAlbumByIdAsync(6))!.Songs[0].StreamingLinks).Url);

        await admin.SetSongStreamingLinkAsync(songId, StreamingProvider.Spotify, Write("spotify:track:0000000000000000000000", StreamingLinkKind.Track));
        Assert.Equal("https://open.spotify.com/track/0000000000000000000000", Assert.Single((await admin.GetSongAsync(songId))!.StreamingLinks).Url);

        await admin.SetSongStreamingLinkAsync(songId, StreamingProvider.Spotify, null);
        await admin.SetAlbumStreamingLinkAsync(6, StreamingProvider.AppleMusic, null);
        Assert.Empty((await admin.GetSongAsync(songId))!.StreamingLinks);
        Assert.Empty((await repository.GetAlbumByIdAsync(6))!.StreamingLinks);
    }

    [Fact]
    public async Task Admin_writes_reject_mismatched_links_and_missing_targets()
    {
        var store = new InMemoryDiscographyStore(SampleDiscographyData.CreateSeedAlbums());
        var admin = new InMemoryAdminDiscographyRepository(store);
        var track = Write(SpotifyTrack, StreamingLinkKind.Track);

        await Assert.ThrowsAsync<ArgumentException>(() => admin.SetAlbumStreamingLinkAsync(1, StreamingProvider.Spotify, track));
        await Assert.ThrowsAsync<ArgumentException>(() => admin.SetSongStreamingLinkAsync(1001, StreamingProvider.AppleMusic, track));
        await Assert.ThrowsAsync<InvalidOperationException>(() => admin.SetAlbumStreamingLinkAsync(99, StreamingProvider.Spotify, null));
        await Assert.ThrowsAsync<InvalidOperationException>(() => admin.SetSongStreamingLinkAsync(99, StreamingProvider.Spotify, track));
    }

    [Fact]
    public void Updated_by_is_trimmed_and_capped()
    {
        var link = Parse(SpotifyTrack, StreamingLinkKind.Track);

        Assert.Null(new StreamingLinkWrite(link, StreamingLinkSource.Imported, "  ").TrimmedUpdatedBy());
        Assert.Equal(
            StreamingLinkWrite.MaxUpdatedByLength,
            new StreamingLinkWrite(link, StreamingLinkSource.Manual, new string('a', 150)).TrimmedUpdatedBy()!.Length);
    }

    private static StreamingLinkWrite Write(string url, StreamingLinkKind kind, string? updatedBy = null) =>
        new(Parse(url, kind), StreamingLinkSource.Manual, updatedBy);

    private static ParsedStreamingLink Parse(string url, StreamingLinkKind kind)
    {
        Assert.True(StreamingLinkUrl.TryParse(url, kind, out var link, out var error), error);
        return link;
    }
}
