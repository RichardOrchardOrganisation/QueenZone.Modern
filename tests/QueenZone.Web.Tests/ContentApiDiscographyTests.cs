using System.Net;
using System.Net.Http.Json;
using QueenZone.Data;

namespace QueenZone.Web.Tests;

public sealed class ContentApiDiscographyTests : IClassFixture<QueenZoneWebApplicationFactory>
{
    private readonly QueenZoneWebApplicationFactory factory;

    public ContentApiDiscographyTests(QueenZoneWebApplicationFactory factory)
    {
        this.factory = factory;
    }

    [Fact]
    public async Task Discography_list_requires_no_auth_and_returns_albums()
    {
        using var client = factory.CreateAnonymousClient();

        using var response = await client.GetAsync($"{ContentApiEndpoints.RootPath}/discography");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<ApiPagedResponse<AlbumListItemDto>>();
        Assert.NotNull(payload);
        Assert.True(payload!.Items.Count >= 6);
        Assert.All(payload.Items, item => Assert.False(string.IsNullOrWhiteSpace(item.DetailPath)));
    }

    [Fact]
    public async Task Discography_list_clamps_invalid_paging_query_values()
    {
        using var client = factory.CreateAnonymousClient();

        using var response = await client.GetAsync($"{ContentApiEndpoints.RootPath}/discography?page=0&pageSize=1000");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<ApiPagedResponse<AlbumListItemDto>>();
        Assert.NotNull(payload);
        Assert.Equal(1, payload!.Page);
        Assert.Equal(ApiPagination.MaxPageSize, payload.PageSize);
    }

    [Fact]
    public async Task Discography_detail_returns_album_with_tracklist()
    {
        using var client = factory.CreateAnonymousClient();

        using var response = await client.GetAsync($"{ContentApiEndpoints.RootPath}/discography/4");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var album = await response.Content.ReadFromJsonAsync<AlbumDetailDto>();
        Assert.NotNull(album);
        Assert.Equal(4, album!.AlbumId);
        Assert.Equal("A Night at the Opera", album.Name);
        Assert.NotEmpty(album.Songs);
        Assert.Contains(album.Songs, song => song.Title == "Bohemian Rhapsody");
    }

    [Fact]
    public async Task Discography_detail_includes_streaming_links_as_provider_keys()
    {
        using var client = factory.CreateAnonymousClient();

        using var withLinks = System.Text.Json.JsonDocument.Parse(
            await client.GetStringAsync($"{ContentApiEndpoints.RootPath}/discography/4"));
        var albumLinks = withLinks.RootElement.GetProperty("streamingLinks");
        Assert.Equal(
            ["spotify", "apple-music"],
            albumLinks.EnumerateArray().Select(link => link.GetProperty("provider").GetString()));
        Assert.Equal(
            "https://open.spotify.com/album/0SampleNightAtTheOpera",
            albumLinks[0].GetProperty("url").GetString());

        var songs = withLinks.RootElement.GetProperty("songs").EnumerateArray().ToList();
        var bohemian = songs.Single(song => song.GetProperty("title").GetString() == "Bohemian Rhapsody");
        Assert.Equal(2, bohemian.GetProperty("streamingLinks").GetArrayLength());
        Assert.All(
            songs.Where(song => song.GetProperty("title").GetString() != "Bohemian Rhapsody"),
            song => Assert.Equal(0, song.GetProperty("streamingLinks").GetArrayLength()));

        // Albums without links still carry an empty array, never null or a missing property.
        using var withoutLinks = System.Text.Json.JsonDocument.Parse(
            await client.GetStringAsync($"{ContentApiEndpoints.RootPath}/discography/3"));
        Assert.Equal(System.Text.Json.JsonValueKind.Array, withoutLinks.RootElement.GetProperty("streamingLinks").ValueKind);
        Assert.Equal(0, withoutLinks.RootElement.GetProperty("streamingLinks").GetArrayLength());
    }

    [Fact]
    public void ToAlbumDetail_formats_notes_and_lyrics_like_the_website()
    {
        var album = new AlbumDetail(
            42,
            "Album",
            "album",
            1975,
            "Queen",
            "<script>alert(1)</script><p>Recorded at <em>Rockfield</em>.</p>",
            null,
            [new AlbumSong(7, "Track", false, "First line\n<script>not markup</script>", "Plain notes")]);

        var dto = ContentApiMapper.ToAlbumDetail(album);

        Assert.Equal(NewsArticleContent.FormatBody(album.GeneralNotes!), dto.GeneralNotes);
        Assert.DoesNotContain("<script", dto.GeneralNotes, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(LyricsFormatter.Format(album.Songs[0].Lyrics!), dto.Songs[0].Lyrics);
        Assert.DoesNotContain("<script", dto.Songs[0].Lyrics, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("&lt;script&gt;not markup&lt;/script&gt;", dto.Songs[0].Lyrics, StringComparison.Ordinal);
        Assert.Equal("Plain notes", dto.Songs[0].Notes);
    }

    [Fact]
    public async Task Discography_detail_returns_problem_details_for_missing_album()
    {
        using var client = factory.CreateAnonymousClient();

        using var response = await client.GetAsync($"{ContentApiEndpoints.RootPath}/discography/424242");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }
}
