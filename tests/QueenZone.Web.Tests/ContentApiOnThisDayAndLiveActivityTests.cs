using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using QueenZone.Web;

namespace QueenZone.Web.Tests;

public sealed class ContentApiOnThisDayAndLiveActivityTests :
    IClassFixture<QueenZoneWebApplicationFactory>,
    IClassFixture<WebHostVariantCache>
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly QueenZoneWebApplicationFactory factory;
    private readonly WebHostVariantCache variants;

    public ContentApiOnThisDayAndLiveActivityTests(
        QueenZoneWebApplicationFactory factory,
        WebHostVariantCache variants)
    {
        this.factory = factory;
        this.variants = variants;
    }

    [Fact]
    public async Task OnThisDay_requires_no_auth_and_returns_200()
    {
        // Pin the clock: sample seed is sparse. 27 Aug 2026 (the CI flake date) has
        // no exact match and nothing inside the +/-7 day window (John Deacon is
        // 19 Aug; Freddie's birthday is 5 Sep).
        using var client = variants.Get(WebHostVariants.FixedUtc20260713).CreateAnonymousClient();

        using var response = await client.GetAsync($"{ContentApiEndpoints.RootPath}/on-this-day");

        var payload = await ReadOnThisDayJsonAsync<TimelineEventDto?>(response);
        Assert.NotNull(payload);
        Assert.False(string.IsNullOrWhiteSpace(payload.Title));
        Assert.False(string.IsNullOrWhiteSpace(payload.FormattedDate));
        Assert.Equal("Queen's Live Aid performance", payload.Title);
    }

    [Fact]
    public async Task OnThisDay_falls_back_to_nearby_seed_event()
    {
        using var client = variants.Get(WebHostVariants.FixedUtc20260712).CreateAnonymousClient();

        using var response = await client.GetAsync($"{ContentApiEndpoints.RootPath}/on-this-day");

        var payload = await ReadOnThisDayJsonAsync<TimelineEventDto?>(response);
        Assert.NotNull(payload);
        Assert.Equal("Queen's Live Aid performance", payload.Title);
    }

    [Fact]
    public async Task OnThisDay_returns_json_null_when_seed_has_no_nearby_event()
    {
        using var client = variants.Get(WebHostVariants.FixedUtc20260827).CreateAnonymousClient();

        using var response = await client.GetAsync($"{ContentApiEndpoints.RootPath}/on-this-day");

        var payload = await ReadOnThisDayJsonAsync<TimelineEventDto?>(response);
        Assert.Null(payload);
    }

    [Fact]
    public async Task LiveActivity_requires_no_auth_and_returns_a_non_negative_count()
    {
        using var client = factory.CreateAnonymousClient();

        using var response = await client.GetAsync($"{ContentApiEndpoints.RootPath}/live-activity");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<LiveActivitySummaryDto>();
        Assert.NotNull(payload);
        Assert.True(payload!.NewForumRepliesToday >= 0);
    }

    private static async Task<T?> ReadOnThisDayJsonAsync<T>(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        Assert.False(
            string.IsNullOrWhiteSpace(body),
            "OnThisDay must return JSON (object or null), not an empty 200 body.");
        Assert.Equal(
            "application/json",
            response.Content.Headers.ContentType?.MediaType);

        return JsonSerializer.Deserialize<T>(body, JsonOptions);
    }
}
