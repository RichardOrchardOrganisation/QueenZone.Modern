using System.Net;
using System.Net.Http.Json;
using QueenZone.Data;

namespace QueenZone.Web.Tests;

public sealed class ContentApiSongsTests : IClassFixture<QueenZoneWebApplicationFactory>
{
    private readonly QueenZoneWebApplicationFactory factory;

    public ContentApiSongsTests(QueenZoneWebApplicationFactory factory)
    {
        this.factory = factory;
    }

    [Fact]
    public async Task Songs_list_requires_no_auth_and_returns_canonical_songs()
    {
        using var client = factory.CreateAnonymousClient();

        using var response = await client.GetAsync($"{ContentApiEndpoints.RootPath}/songs?pageSize=100");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<ApiPagedResponse<SongListItemDto>>();
        Assert.NotNull(payload);
        Assert.Contains(payload!.Items, item => item.Slug == "bohemian-rhapsody" && item.DetailPath == "/songs/bohemian-rhapsody");
        var sevenSeas = Assert.Single(payload.Items, item => item.Slug == "seven-seas-of-rhye");
        Assert.Equal(2, sevenSeas.AppearanceCount);
    }

    [Fact]
    public async Task Songs_list_clamps_invalid_paging_query_values()
    {
        using var client = factory.CreateAnonymousClient();

        using var response = await client.GetAsync($"{ContentApiEndpoints.RootPath}/songs?page=0&pageSize=1000");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<ApiPagedResponse<SongListItemDto>>();
        Assert.NotNull(payload);
        Assert.Equal(1, payload!.Page);
        Assert.Equal(ApiPagination.MaxPageSize, payload.PageSize);
    }

    [Fact]
    public async Task Songs_detail_returns_appearances_and_formatted_lyrics()
    {
        using var client = factory.CreateAnonymousClient();

        using var response = await client.GetAsync($"{ContentApiEndpoints.RootPath}/songs/keep-yourself-alive");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var song = await response.Content.ReadFromJsonAsync<SongDetailDto>();
        Assert.NotNull(song);
        Assert.Equal("keep-yourself-alive", song!.Slug);
        Assert.Equal("Keep Yourself Alive", song.Title);
        Assert.Equal("/songs/keep-yourself-alive", song.DetailPath);
        Assert.Contains(song.Appearances, appearance => appearance.AlbumName == "Queen");
        Assert.NotNull(song.Lyrics);
        Assert.Contains("&lt;/li&gt;&lt;/ol&gt;", song.Lyrics, StringComparison.Ordinal);
        Assert.DoesNotContain("<script", song.Lyrics, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ToSongDetail_formats_lyrics_like_the_website()
    {
        var song = new SongDetail(
            "track",
            "Track",
            "First line\n<script>not markup</script>",
            [new SongAppearance(4, "A Night at the Opera", "a-night-at-the-opera", 1975, false, "Plain notes")]);

        var dto = ContentApiMapper.ToSongDetail(song);

        Assert.Equal(LyricsFormatter.Format(song.Lyrics!), dto.Lyrics);
        Assert.DoesNotContain("<script", dto.Lyrics, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Plain notes", dto.Appearances[0].Notes);
        Assert.Equal("/discography/albums/4/a-night-at-the-opera", dto.Appearances[0].AlbumPath);
    }

    [Fact]
    public async Task Songs_detail_returns_problem_details_for_missing_song()
    {
        using var client = factory.CreateAnonymousClient();

        using var response = await client.GetAsync($"{ContentApiEndpoints.RootPath}/songs/not-a-queen-song");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Album_songs_include_additive_detail_path()
    {
        using var client = factory.CreateAnonymousClient();

        using var response = await client.GetAsync($"{ContentApiEndpoints.RootPath}/discography/4");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var album = await response.Content.ReadFromJsonAsync<AlbumDetailDto>();
        Assert.NotNull(album);
        var rhapsody = Assert.Single(album!.Songs, song => song.Title == "Bohemian Rhapsody");
        Assert.Equal("/songs/bohemian-rhapsody", rhapsody.DetailPath);
    }
}
