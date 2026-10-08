using QueenZone.Data;
using QueenZone.Web;

namespace QueenZone.Web.Tests;

public sealed class DiscographyStreamingLinkFormTests
{
    private const string SpotifyAlbum = "https://open.spotify.com/album/4KfrGvYXcZsFgMHVIdzkoW";

    private const string AppleAlbum = "https://music.apple.com/gb/album/queen-ii/111";

    [Fact]
    public void New_values_are_normalised_into_manual_writes()
    {
        var (changes, errors) = DiscographyStreamingLinkForm.Parse(
            new StreamingLinksFormInput { Spotify = $" {SpotifyAlbum}?si=abc ", AppleMusic = AppleAlbum + "?ls=1" },
            StreamingLinkKind.Album,
            [],
            "admin@example.test");

        Assert.Empty(errors);
        Assert.Equal([StreamingProvider.Spotify, StreamingProvider.AppleMusic], changes.Select(change => change.Provider));
        Assert.Equal(SpotifyAlbum, changes[0].Link!.Link.Url);
        Assert.Equal(AppleAlbum, changes[1].Link!.Link.Url);
        Assert.All(changes, change => Assert.Equal(StreamingLinkSource.Manual, change.Link!.Source));
        Assert.Equal("admin@example.test", changes[0].Link!.UpdatedBy);
    }

    [Fact]
    public void Unchanged_values_are_skipped_and_blanks_remove_only_stored_links()
    {
        AdminStreamingLink[] stored = [Stored(StreamingProvider.Spotify, SpotifyAlbum, StreamingLinkSource.Imported)];

        var unchanged = DiscographyStreamingLinkForm.Parse(
            new StreamingLinksFormInput { Spotify = SpotifyAlbum + "?si=new", AppleMusic = "  " },
            StreamingLinkKind.Album,
            stored,
            null);
        Assert.Empty(unchanged.Errors);
        Assert.Empty(unchanged.Changes);

        var removed = DiscographyStreamingLinkForm.Parse(new StreamingLinksFormInput(), StreamingLinkKind.Album, stored, null);
        var change = Assert.Single(removed.Changes);
        Assert.Equal(StreamingProvider.Spotify, change.Provider);
        Assert.Null(change.Link);
    }

    [Fact]
    public void Any_error_blocks_every_change_and_names_the_field()
    {
        var (changes, errors) = DiscographyStreamingLinkForm.Parse(
            new StreamingLinksFormInput { Spotify = AppleAlbum, AppleMusic = "https://example.com/album/1" },
            StreamingLinkKind.Album,
            [Stored(StreamingProvider.AppleMusic, AppleAlbum, StreamingLinkSource.Manual)],
            null);

        Assert.Empty(changes);
        Assert.Equal(2, errors.Count);
        Assert.Equal("Spotify link: that link is from Apple Music. Paste it in the Apple Music field instead.", errors[0]);
        Assert.StartsWith("Apple Music link: Only open.spotify.com and music.apple.com", errors[1]);
    }

    [Fact]
    public void Track_fields_reject_album_links()
    {
        var (_, errors) = DiscographyStreamingLinkForm.Parse(
            new StreamingLinksFormInput { Spotify = SpotifyAlbum },
            StreamingLinkKind.Track,
            [],
            null);

        Assert.Equal("Spotify link: That is an album link from Spotify; this field needs a track link.", Assert.Single(errors));
    }

    [Fact]
    public void Form_round_trips_stored_links()
    {
        var form = StreamingLinksFormInput.From([Stored(StreamingProvider.AppleMusic, AppleAlbum, StreamingLinkSource.Manual)]);

        Assert.Null(form.Spotify);
        Assert.Equal(AppleAlbum, form.Value(StreamingProvider.AppleMusic));
        Assert.Equal("AppleMusic", StreamingLinksFormInput.FieldName(StreamingProvider.AppleMusic));
    }

    private static AdminStreamingLink Stored(StreamingProvider provider, string url, StreamingLinkSource source) =>
        new(provider, "id", url, source, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), null);
}
