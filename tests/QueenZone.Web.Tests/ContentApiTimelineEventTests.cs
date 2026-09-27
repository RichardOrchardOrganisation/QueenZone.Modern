using System.Net;
using System.Text.Json;
using QueenZone.Web;

namespace QueenZone.Web.Tests;

public sealed class ContentApiTimelineEventTests :
    IClassFixture<QueenZoneWebApplicationFactory>,
    IClassFixture<WebHostVariantCache>
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly QueenZoneWebApplicationFactory factory;
    private readonly WebHostVariantCache variants;

    public ContentApiTimelineEventTests(QueenZoneWebApplicationFactory factory, WebHostVariantCache variants)
    {
        this.factory = factory;
        this.variants = variants;
    }

    [Fact]
    public async Task Timeline_list_requires_no_auth()
    {
        using var client = factory.CreateAnonymousClient();

        using var response = await client.GetAsync($"{ContentApiEndpoints.RootPath}/timeline");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Timeline_event_detail_returns_a_published_event_that_is_off_the_first_page()
    {
        using var client = variants.Get(WebHostVariants.TimelineDeepOffPage).CreateAnonymousClient();

        using var page = await client.GetAsync($"{ContentApiEndpoints.RootPath}/timeline?page=1&pageSize=1");
        var list = await ReadJsonAsync<ApiPagedResponse<TimelineEventDto>>(page);
        Assert.NotNull(list);
        Assert.Equal(2, list.TotalCount);
        Assert.DoesNotContain(list.Items, item => item.Id == 9999);

        using var response = await client.GetAsync($"{ContentApiEndpoints.RootPath}/timeline/9999");

        var payload = await ReadJsonAsync<TimelineEventDto>(response);
        Assert.NotNull(payload);
        Assert.Equal(9999, payload.Id);
        Assert.Equal("Deep off-page event", payload.Title);
        Assert.Equal("Queen play Live Aid.", payload.Summary);
        Assert.Equal("13 Jul 1985", payload.FormattedDate);
        Assert.False(string.IsNullOrWhiteSpace(payload.Category));
        Assert.False(string.IsNullOrWhiteSpace(payload.CategoryLabel));
        Assert.Equal("https://en.wikipedia.org/wiki/Live_Aid", payload.SourceUrl);
    }

    [Fact]
    public async Task Timeline_event_detail_returns_404_for_unpublished_or_missing()
    {
        using var client = variants.Get(WebHostVariants.UnpublishedTimelineEvent).CreateAnonymousClient();

        using var unpublished = await client.GetAsync($"{ContentApiEndpoints.RootPath}/timeline/13");
        Assert.Equal(HttpStatusCode.NotFound, unpublished.StatusCode);
        Assert.Equal("application/problem+json", unpublished.Content.Headers.ContentType?.MediaType);

        using var missing = await client.GetAsync($"{ContentApiEndpoints.RootPath}/timeline/424242");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    private static async Task<T?> ReadJsonAsync<T>(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        Assert.False(string.IsNullOrWhiteSpace(body), "Timeline JSON must not be an empty 200 body.");
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);

        return JsonSerializer.Deserialize<T>(body, JsonOptions);
    }
}
