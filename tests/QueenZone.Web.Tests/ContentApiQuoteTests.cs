using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using QueenZone.Data;

namespace QueenZone.Web.Tests;

public sealed class ContentApiQuoteTests : IClassFixture<QueenZoneWebApplicationFactory>, IClassFixture<WebHostVariantCache>
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly QueenZoneWebApplicationFactory factory;
    private readonly WebHostVariantCache variants;

    public ContentApiQuoteTests(QueenZoneWebApplicationFactory factory, WebHostVariantCache variants)
    {
        this.factory = factory;
        this.variants = variants;
    }

    [Fact]
    public async Task Random_quote_requires_no_auth_and_returns_a_published_quote()
    {
        using var client = factory.CreateAnonymousClient();

        using var response = await client.GetAsync($"{ContentApiEndpoints.RootPath}/quotes/random");

        var payload = await ReadRandomQuoteJsonAsync<QuoteDto?>(response);
        Assert.NotNull(payload);
        Assert.False(string.IsNullOrWhiteSpace(payload.Text));
        Assert.False(string.IsNullOrWhiteSpace(payload.WhoSaid));
    }

    [Fact]
    public async Task Random_quote_returns_json_null_when_nothing_is_published()
    {
        using var client = variants.Get(WebHostVariants.EmptyQuotes).CreateAnonymousClient();

        using var response = await client.GetAsync($"{ContentApiEndpoints.RootPath}/quotes/random");

        var payload = await ReadRandomQuoteJsonAsync<QuoteDto?>(response);
        Assert.Null(payload);
    }

    [Fact]
    public async Task Quote_detail_returns_published_quote_with_context()
    {
        var quotes = factory.Services.GetRequiredService<IQuoteRepository>();
        var id = await quotes.CreateAsync(
            new AdminQuoteDraft("A kind of magic", "Freddie Mercury", true, "Live Aid, 1985"));
        using var client = factory.CreateAnonymousClient();

        using var response = await client.GetAsync($"{ContentApiEndpoints.RootPath}/quotes/{id}");

        var payload = await ReadRandomQuoteJsonAsync<QuoteDto>(response);
        Assert.NotNull(payload);
        Assert.Equal(id, payload.Id);
        Assert.Equal("A kind of magic", payload.Text);
        Assert.Equal("Freddie Mercury", payload.WhoSaid);
        Assert.Equal("Live Aid, 1985", payload.Context);
    }

    [Fact]
    public async Task Quote_detail_omits_blank_context()
    {
        var quotes = factory.Services.GetRequiredService<IQuoteRepository>();
        var id = await quotes.CreateAsync(
            new AdminQuoteDraft("We will rock you", "Brian May", true, "   "));
        using var client = factory.CreateAnonymousClient();

        using var response = await client.GetAsync($"{ContentApiEndpoints.RootPath}/quotes/{id}");

        var payload = await ReadRandomQuoteJsonAsync<QuoteDto>(response);
        Assert.NotNull(payload);
        Assert.Equal(id, payload.Id);
        Assert.Null(payload.Context);
    }

    [Fact]
    public async Task Quote_detail_returns_404_for_unpublished_or_missing()
    {
        var quotes = factory.Services.GetRequiredService<IQuoteRepository>();
        var unpublishedId = await quotes.CreateAsync(
            new AdminQuoteDraft("Draft line", "Roger Taylor", false, "Studio notes"));
        using var client = factory.CreateAnonymousClient();

        using var unpublished = await client.GetAsync($"{ContentApiEndpoints.RootPath}/quotes/{unpublishedId}");
        Assert.Equal(HttpStatusCode.NotFound, unpublished.StatusCode);
        Assert.Equal("application/problem+json", unpublished.Content.Headers.ContentType?.MediaType);

        using var missing = await client.GetAsync($"{ContentApiEndpoints.RootPath}/quotes/424242");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    private static async Task<T?> ReadRandomQuoteJsonAsync<T>(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        Assert.False(
            string.IsNullOrWhiteSpace(body),
            "Random quote must return JSON (object or null), not an empty 200 body.");
        Assert.Equal(
            "application/json",
            response.Content.Headers.ContentType?.MediaType);

        return JsonSerializer.Deserialize<T>(body, JsonOptions);
    }
}
