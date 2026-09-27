using System.Net;
using System.Net.Http.Json;

namespace QueenZone.Web.Tests;

public sealed class ContentApiTimelineTests : IClassFixture<QueenZoneWebApplicationFactory>
{
    private readonly QueenZoneWebApplicationFactory factory;

    public ContentApiTimelineTests(QueenZoneWebApplicationFactory factory)
    {
        this.factory = factory;
    }

    [Fact]
    public async Task Timeline_list_requires_no_auth_and_returns_events_in_date_order()
    {
        using var client = factory.CreateAnonymousClient();

        using var response = await client.GetAsync($"{ContentApiEndpoints.RootPath}/timeline");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<ApiPagedResponse<TimelineEventDto>>();
        Assert.NotNull(payload);
        Assert.True(payload!.Items.Count >= 10);
        for (var i = 1; i < payload.Items.Count; i++)
        {
            Assert.True(payload.Items[i - 1].EventDate <= payload.Items[i].EventDate);
        }
        Assert.All(payload.Items, item => Assert.False(string.IsNullOrWhiteSpace(item.Category)));
        Assert.All(payload.Items, item => Assert.False(string.IsNullOrWhiteSpace(item.CategoryLabel)));
    }

    [Fact]
    public async Task Timeline_anchor_returns_the_cached_page_containing_the_focus_id()
    {
        using var client = factory.CreateAnonymousClient();
        using var firstResponse = await client.GetAsync($"{ContentApiEndpoints.RootPath}/timeline/anchor/1?pageSize=3");
        var first = await firstResponse.Content.ReadFromJsonAsync<ApiPagedResponse<TimelineEventDto>>();
        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
        Assert.Equal(1, first!.Page);
        Assert.Contains(first.Items, item => item.Id == 1);

        using var laterResponse = await client.GetAsync($"{ContentApiEndpoints.RootPath}/timeline/anchor/10?pageSize=3");
        var later = await laterResponse.Content.ReadFromJsonAsync<ApiPagedResponse<TimelineEventDto>>();
        Assert.Equal(HttpStatusCode.OK, laterResponse.StatusCode);
        Assert.True(later!.Page > 1);
        Assert.Contains(later.Items, item => item.Id == 10);

        using var missing = await client.GetAsync($"{ContentApiEndpoints.RootPath}/timeline/anchor/999999?pageSize=3");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Fact]
    public async Task Timeline_list_clamps_invalid_paging_query_values()
    {
        using var client = factory.CreateAnonymousClient();

        using var response = await client.GetAsync($"{ContentApiEndpoints.RootPath}/timeline?page=0&pageSize=1000");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<ApiPagedResponse<TimelineEventDto>>();
        Assert.NotNull(payload);
        Assert.Equal(1, payload!.Page);
        Assert.Equal(ApiPagination.MaxPageSize, payload.PageSize);
    }

    [Fact]
    public async Task Timeline_list_maps_concert_events_to_live_category()
    {
        using var client = factory.CreateAnonymousClient();

        using var response = await client.GetAsync($"{ContentApiEndpoints.RootPath}/timeline?pageSize=100");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<ApiPagedResponse<TimelineEventDto>>();
        Assert.NotNull(payload);
        var liveAid = Assert.Single(payload!.Items, item => item.Title == "Queen's Live Aid performance");
        Assert.Equal("live", liveAid.Category);
        Assert.Equal("Live", liveAid.CategoryLabel);
    }
}
