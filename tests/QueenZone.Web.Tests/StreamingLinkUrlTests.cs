using QueenZone.Data;

namespace QueenZone.Web.Tests;

public sealed class StreamingLinkUrlTests
{
    private const string SpotifyId = "4KfrGvYXcZsFgMHVIdzkoW";

    [Theory]
    [InlineData("https://open.spotify.com/album/" + SpotifyId, "https://open.spotify.com/album/" + SpotifyId)]
    [InlineData("  https://open.spotify.com/album/" + SpotifyId + "?si=abc123&nd=1  ", "https://open.spotify.com/album/" + SpotifyId)]
    [InlineData("https://open.spotify.com/intl-de/album/" + SpotifyId, "https://open.spotify.com/album/" + SpotifyId)]
    [InlineData("https://OPEN.SPOTIFY.COM/Album/" + SpotifyId + "/", "https://open.spotify.com/album/" + SpotifyId)]
    [InlineData("spotify:album:" + SpotifyId, "https://open.spotify.com/album/" + SpotifyId)]
    public void Spotify_album_links_normalise(string input, string expected)
    {
        Assert.True(StreamingLinkUrl.TryParse(input, StreamingLinkKind.Album, out var link, out var error), error);
        Assert.Equal(StreamingProvider.Spotify, link.Provider);
        Assert.Equal(StreamingLinkKind.Album, link.Kind);
        Assert.Equal(SpotifyId, link.ExternalId);
        Assert.Equal(expected, link.Url);
    }

    [Theory]
    [InlineData("https://open.spotify.com/track/" + SpotifyId + "?si=x")]
    [InlineData("https://open.spotify.com/intl-pt-br/track/" + SpotifyId)]
    [InlineData("spotify:track:" + SpotifyId)]
    public void Spotify_track_links_normalise(string input)
    {
        Assert.True(StreamingLinkUrl.TryParse(input, StreamingLinkKind.Track, out var link, out var error), error);
        Assert.Equal(StreamingLinkKind.Track, link.Kind);
        Assert.Equal("https://open.spotify.com/track/" + SpotifyId, link.Url);
    }

    [Theory]
    [InlineData("https://music.apple.com/gb/album/a-night-at-the-opera/1440650428", "1440650428", "https://music.apple.com/gb/album/a-night-at-the-opera/1440650428")]
    [InlineData("https://music.apple.com/US/album/a-night-at-the-opera/1440650428?ls=1&app=music", "1440650428", "https://music.apple.com/us/album/a-night-at-the-opera/1440650428")]
    [InlineData("https://music.apple.com/gb/album/1440650428", "1440650428", "https://music.apple.com/gb/album/1440650428")]
    [InlineData("https://music.apple.com/de/album/m%C3%BCsica/123", "123", "https://music.apple.com/de/album/m%C3%BCsica/123")]
    public void Apple_album_links_normalise(string input, string externalId, string expected)
    {
        Assert.True(StreamingLinkUrl.TryParse(input, StreamingLinkKind.Album, out var link, out var error), error);
        Assert.Equal(StreamingProvider.AppleMusic, link.Provider);
        Assert.Equal(externalId, link.ExternalId);
        Assert.Equal(expected, link.Url);
    }

    [Theory]
    [InlineData("https://music.apple.com/gb/album/a-night-at-the-opera/1440650428?i=1440650711&ls", "1440650711", "https://music.apple.com/gb/album/a-night-at-the-opera/1440650428?i=1440650711")]
    [InlineData("https://music.apple.com/gb/song/bohemian-rhapsody/1440650711", "1440650711", "https://music.apple.com/gb/song/bohemian-rhapsody/1440650711")]
    [InlineData("https://music.apple.com/gb/song/1440650711?l=en", "1440650711", "https://music.apple.com/gb/song/1440650711")]
    public void Apple_track_links_normalise(string input, string externalId, string expected)
    {
        Assert.True(StreamingLinkUrl.TryParse(input, StreamingLinkKind.Track, out var link, out var error), error);
        Assert.Equal(StreamingLinkKind.Track, link.Kind);
        Assert.Equal(externalId, link.ExternalId);
        Assert.Equal(expected, link.Url);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    [InlineData("open.spotify.com/album/" + SpotifyId)]
    [InlineData("http://open.spotify.com/album/" + SpotifyId)]
    [InlineData("https://spotify.link/abcdef")]
    [InlineData("https://open.spotify.com.evil.example/album/" + SpotifyId)]
    [InlineData("https://open.spotify.com/album/tooShort")]
    [InlineData("https://open.spotify.com/playlist/" + SpotifyId)]
    [InlineData("https://open.spotify.com/album/" + SpotifyId + "/extra")]
    [InlineData("spotify:artist:" + SpotifyId)]
    [InlineData("https://apple.co/3abcdef")]
    [InlineData("https://geo.music.apple.com/gb/album/x/1")]
    [InlineData("https://music.apple.com/album/a-night-at-the-opera/1440650428")]
    [InlineData("https://music.apple.com/gb/playlist/queen-essentials/pl.123")]
    [InlineData("https://music.apple.com/gb/album/a-night-at-the-opera/notanid")]
    [InlineData("https://music.apple.com/gb/album/bad!slug/1")]
    [InlineData("https://music.apple.com/gb/album/a/b/1")]
    [InlineData("https://www.youtube.com/watch?v=fJ9rUzIMcZQ")]
    [InlineData("javascript:alert(1)")]
    public void Rejects_unsupported_links(string? input)
    {
        Assert.False(StreamingLinkUrl.TryParse(input, StreamingLinkKind.Album, out var link, out var error));
        Assert.Null(link);
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    [Fact]
    public void Rejects_an_invalid_apple_track_id()
    {
        Assert.False(StreamingLinkUrl.TryParse(
            "https://music.apple.com/gb/album/x/1?i=abc", StreamingLinkKind.Track, out _, out var error));
        Assert.Contains("track id", error);
    }

    [Fact]
    public void Rejects_the_wrong_kind_with_a_helpful_message()
    {
        Assert.False(StreamingLinkUrl.TryParse(
            "https://open.spotify.com/track/" + SpotifyId, StreamingLinkKind.Album, out _, out var albumError));
        Assert.Equal("That is a track link from Spotify; this field needs an album link.", albumError);

        Assert.False(StreamingLinkUrl.TryParse(
            "https://music.apple.com/gb/album/x/1", StreamingLinkKind.Track, out _, out var trackError));
        Assert.Equal("That is an album link from Apple Music; this field needs a track link.", trackError);
    }

    [Fact]
    public void Rejects_links_longer_than_the_column()
    {
        var url = $"https://music.apple.com/gb/album/{new string('a', StreamingLinkUrl.MaxUrlLength)}/1";

        Assert.False(StreamingLinkUrl.TryParse(url, StreamingLinkKind.Album, out _, out var error));
        Assert.Contains("longer", error);
    }

    [Fact]
    public void Provider_and_source_keys_round_trip()
    {
        foreach (var provider in StreamingProviders.All)
        {
            Assert.Equal(provider, StreamingProviders.FromKey(provider.Key()));
        }

        Assert.Equal("apple-music", StreamingProvider.AppleMusic.Key());
        Assert.Equal(StreamingLinkSource.Imported, StreamingProviders.SourceFromKey(StreamingLinkSource.Imported.Key()));
        Assert.Throws<ArgumentOutOfRangeException>(() => StreamingProviders.FromKey("youtube-music"));
        Assert.Throws<ArgumentOutOfRangeException>(() => StreamingProviders.SourceFromKey("other"));
    }
}
