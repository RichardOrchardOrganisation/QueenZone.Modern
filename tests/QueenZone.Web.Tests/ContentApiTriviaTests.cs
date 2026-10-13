using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using QueenZone.Data;

namespace QueenZone.Web.Tests;

public sealed class ContentApiTriviaTests :
    IClassFixture<QueenZoneWebApplicationFactory>,
    IClassFixture<WebHostVariantCache>,
    IAsyncLifetime
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly QueenZoneWebApplicationFactory factory;
    private readonly VariantWebApplicationFactory isolatedTrivia;

    public ContentApiTriviaTests(QueenZoneWebApplicationFactory factory, WebHostVariantCache variants)
    {
        this.factory = factory;
        isolatedTrivia = variants.Get(WebHostVariants.IsolatedTrivia);
    }

    public Task InitializeAsync() => isolatedTrivia.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Random_trivia_requires_no_auth_and_returns_a_published_fact()
    {
        using var client = factory.CreateAnonymousClient();

        using var response = await client.GetAsync($"{ContentApiEndpoints.RootPath}/trivia/random");

        var payload = await ReadRandomTriviaJsonAsync<TriviaDto?>(response);
        Assert.NotNull(payload);
        Assert.False(string.IsNullOrWhiteSpace(payload.Text));
        Assert.True(payload.Id > 0);
    }

    [Fact]
    public async Task Random_trivia_returns_json_null_when_nothing_is_published()
    {
        using var client = isolatedTrivia.CreateAnonymousClient();

        using var response = await client.GetAsync($"{ContentApiEndpoints.RootPath}/trivia/random");

        var payload = await ReadRandomTriviaJsonAsync<TriviaDto?>(response);
        Assert.Null(payload);
    }

    [Fact]
    public async Task Random_trivia_returns_json_null_when_only_unpublished_facts_exist()
    {
        var trivia = isolatedTrivia.Services.GetRequiredService<ITriviaRepository>();
        await trivia.CreateAsync(new AdminTriviaDraft("Draft only", false, "Band", TriviaDifficulty.Easy, "Notes"));
        using var client = isolatedTrivia.CreateAnonymousClient();

        using var response = await client.GetAsync($"{ContentApiEndpoints.RootPath}/trivia/random");

        var payload = await ReadRandomTriviaJsonAsync<TriviaDto?>(response);
        Assert.Null(payload);
    }

    [Fact]
    public async Task Random_trivia_returns_optional_fields()
    {
        var trivia = isolatedTrivia.Services.GetRequiredService<ITriviaRepository>();
        var id = await trivia.CreateAsync(new AdminTriviaDraft(
            "Freddie Mercury was born Farrokh Bulsara.",
            true,
            "Band",
            TriviaDifficulty.Easy,
            "Queen official biography"));
        using var client = isolatedTrivia.CreateAnonymousClient();

        using var response = await client.GetAsync($"{ContentApiEndpoints.RootPath}/trivia/random");

        var payload = await ReadRandomTriviaJsonAsync<TriviaDto>(response);
        Assert.NotNull(payload);
        Assert.Equal(id, payload.Id);
        Assert.Equal("Freddie Mercury was born Farrokh Bulsara.", payload.Text);
        Assert.Equal("Band", payload.Category);
        Assert.Equal(TriviaDifficulty.Easy, payload.Difficulty);
        Assert.Equal("Queen official biography", payload.Source);
    }

    [Fact]
    public async Task Random_trivia_omits_blank_optional_fields()
    {
        var trivia = isolatedTrivia.Services.GetRequiredService<ITriviaRepository>();
        var id = await trivia.CreateAsync(new AdminTriviaDraft(
            "We Will Rock You uses stadium stomp percussion.",
            true,
            "   ",
            "  ",
            "   "));
        using var client = isolatedTrivia.CreateAnonymousClient();

        using var response = await client.GetAsync($"{ContentApiEndpoints.RootPath}/trivia/random");

        var payload = await ReadRandomTriviaJsonAsync<TriviaDto>(response);
        Assert.NotNull(payload);
        Assert.Equal(id, payload.Id);
        Assert.Null(payload.Category);
        Assert.Null(payload.Difficulty);
        Assert.Null(payload.Source);
    }

    [Fact]
    public async Task Detail_returns_only_the_requested_published_fact_without_auth()
    {
        var trivia = isolatedTrivia.Services.GetRequiredService<ITriviaRepository>();
        await trivia.CreateAsync(new AdminTriviaDraft("Other fact", true, null, null, null));
        var id = await trivia.CreateAsync(new AdminTriviaDraft("Widget fact", true, "Live", TriviaDifficulty.Easy, "Archive"));
        using var client = isolatedTrivia.CreateAnonymousClient();
        using var response = await client.GetAsync($"{ContentApiEndpoints.RootPath}/trivia/{id}");
        var payload = await ReadRandomTriviaJsonAsync<TriviaDto>(response);
        Assert.NotNull(payload);
        Assert.Equal(id, payload.Id);
        Assert.Equal("Widget fact", payload.Text);
        Assert.Equal("Live", payload.Category);
        Assert.Equal(TriviaDifficulty.Easy, payload.Difficulty);
        Assert.Equal("Archive", payload.Source);
    }

    [Fact]
    public async Task Detail_returns_404_for_missing_unpublished_and_removed_facts()
    {
        var trivia = isolatedTrivia.Services.GetRequiredService<ITriviaRepository>();
        var draft = await trivia.CreateAsync(new AdminTriviaDraft("Secret draft", false, null, null, null));
        var removed = await trivia.CreateAsync(new AdminTriviaDraft("Removed fact", true, null, null, null));
        await trivia.DeleteAsync(removed);
        using var client = isolatedTrivia.CreateAnonymousClient();
        foreach (var id in new[] { 0, -1, int.MaxValue, draft, removed })
        {
            using var response = await client.GetAsync($"{ContentApiEndpoints.RootPath}/trivia/{id}");
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.DoesNotContain("Secret draft", await response.Content.ReadAsStringAsync());
        }
    }

    [Fact]
    public void Mapper_omits_blank_optional_fields()
    {
        var dto = ContentApiMapper.ToTriviaDto(
            new TriviaFactItem(24, "A fact", DateTime.UtcNow, true, "   ", null, "  "));

        Assert.Equal(24, dto.Id);
        Assert.Equal("A fact", dto.Text);
        Assert.Null(dto.Category);
        Assert.Null(dto.Difficulty);
        Assert.Null(dto.Source);
    }

    private static async Task<T?> ReadRandomTriviaJsonAsync<T>(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        Assert.False(
            string.IsNullOrWhiteSpace(body),
            "Random trivia must return JSON (object or null), not an empty 200 body.");
        Assert.Equal(
            "application/json",
            response.Content.Headers.ContentType?.MediaType);

        return JsonSerializer.Deserialize<T>(body, JsonOptions);
    }
}
