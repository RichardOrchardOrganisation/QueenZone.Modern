using System.Net;
using System.Net.Http.Json;
using QueenZone.Data;

namespace QueenZone.Web.Tests;

public sealed class ContentApiBiographyTests : IClassFixture<QueenZoneWebApplicationFactory>
{
    private readonly QueenZoneWebApplicationFactory factory;

    public ContentApiBiographyTests(QueenZoneWebApplicationFactory factory)
    {
        this.factory = factory;
    }

    [Fact]
    public async Task Biography_list_requires_no_auth_and_returns_chapters_in_reading_order()
    {
        using var client = factory.CreateAnonymousClient();

        using var response = await client.GetAsync($"{ContentApiEndpoints.RootPath}/biography");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<ApiPagedResponse<BiographyChapterListItemDto>>();
        Assert.NotNull(payload);
        Assert.True(payload!.Items.Count >= 5);
        Assert.Equal(1, payload.Items[0].DisplaySequence);
        Assert.True(payload.Items[0].DisplaySequence < payload.Items[1].DisplaySequence);
        Assert.All(payload.Items, item => Assert.False(string.IsNullOrWhiteSpace(item.DetailPath)));
    }

    [Fact]
    public async Task Biography_list_clamps_invalid_paging_query_values()
    {
        using var client = factory.CreateAnonymousClient();

        using var response = await client.GetAsync($"{ContentApiEndpoints.RootPath}/biography?page=0&pageSize=1000");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<ApiPagedResponse<BiographyChapterListItemDto>>();
        Assert.NotNull(payload);
        Assert.Equal(1, payload!.Page);
        Assert.Equal(ApiPagination.MaxPageSize, payload.PageSize);
    }

    [Fact]
    public async Task Biography_detail_returns_chapter_body_and_adjacent_navigation()
    {
        using var client = factory.CreateAnonymousClient();

        using var response = await client.GetAsync($"{ContentApiEndpoints.RootPath}/biography/2");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var chapter = await response.Content.ReadFromJsonAsync<BiographyChapterDetailDto>();
        Assert.NotNull(chapter);
        Assert.Equal(2, chapter!.Id);
        Assert.False(string.IsNullOrWhiteSpace(chapter.Body));
        Assert.NotNull(chapter.Previous);
        Assert.Equal(1, chapter.Previous!.Id);
        Assert.NotNull(chapter.Next);
        Assert.Equal(3, chapter.Next!.Id);
    }

    [Fact]
    public void ToBiographyChapterDetail_sanitizes_body_like_website_FormatBody()
    {
        var chapter = new BiographyChapterItem(
            42,
            "1975",
            "Summary",
            "<script>alert(1)</script><marquee>News</marquee><p>Hello <em>world</em></p>",
            3,
            new DateTime(2026, 9, 22, 0, 0, 0, DateTimeKind.Utc));

        var dto = ContentApiMapper.ToBiographyChapterDetail(chapter, new BiographyChapterNav(null, null));

        Assert.Equal(BiographyContent.FormatBody(chapter.Body), dto.Body);
        Assert.DoesNotContain("script", dto.Body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("marquee", dto.Body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("<p>Hello <em>world</em></p>", dto.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Biography_detail_returns_problem_details_for_missing_chapter()
    {
        using var client = factory.CreateAnonymousClient();

        using var response = await client.GetAsync($"{ContentApiEndpoints.RootPath}/biography/424242");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }
}
