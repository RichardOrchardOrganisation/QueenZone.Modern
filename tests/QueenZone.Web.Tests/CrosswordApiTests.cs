using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using QueenZone.Data;
using QueenZone.Data.Entities;

namespace QueenZone.Web.Tests;

public sealed class CrosswordApiTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task Published_detail_contains_blocks_numbering_and_clues_without_private_solutions()
    {
        await using var factory = new CrosswordFactory();
        var (catalog, item) = await CreateAsync(factory);
        await catalog.SetPublicationAsync(item.Id, CrosswordStatus.Published, null, item.RowVersion, "publisher");
        using var client = factory.CreateAnonymousClient();
        var response = await client.GetAsync($"{CrosswordApiEndpoints.RootPath}/{item.Id}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        var raw = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("answer", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("explanation", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("solution", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("PRIVATE EXPLANATION TOKEN", raw, StringComparison.Ordinal);
        using var json = JsonDocument.Parse(raw);
        var detail = json.RootElement.Deserialize<CrosswordDetailDto>(JsonOptions)!;
        Assert.Equal(item.Seed.Grid.Width * item.Seed.Grid.Height, detail.Blocks.Count);
        Assert.Equal(detail.Blocks.Count, detail.Numbering.Count);
        Assert.Equal(item.Seed.Grid.Clues.Count, detail.Clues.Count);
        Assert.False(detail.Archived);
        foreach (var row in item.Seed.Grid.Rows.Where(row => !row.All(cell => cell == '#')))
        {
            Assert.DoesNotContain(row, raw, StringComparison.Ordinal);
        }
        var runs = CrosswordGridValidator.Validate(item.Seed.Grid).Runs;
        foreach (var run in runs)
        {
            Assert.Equal(run.Number, detail.Numbering[run.Row * detail.Width + run.Column]);
            var clue = Assert.Single(detail.Clues, clue => clue.Number == run.Number
                && clue.Direction == (run.Direction == CrosswordDirection.Across ? "across" : "down"));
            Assert.Equal(run.Answer.Length, clue.Length);
        }
    }

    [Fact]
    public async Task Draft_and_future_schedule_are_hidden_but_schedule_becomes_visible_without_restart()
    {
        await using var factory = new CrosswordFactory();
        var (catalog, item) = await CreateAsync(factory);
        using var client = factory.CreateAnonymousClient();
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"{CrosswordApiEndpoints.RootPath}/{item.Id}")).StatusCode);
        await catalog.SetPublicationAsync(item.Id, CrosswordStatus.Scheduled,
            factory.Clock.GetUtcNow().AddHours(1), item.RowVersion, "publisher");
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"{CrosswordApiEndpoints.RootPath}/{item.Id}")).StatusCode);
        Assert.Equal(0, (await ListAsync(client)).TotalCount);
        factory.Clock.Advance(TimeSpan.FromHours(1));
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"{CrosswordApiEndpoints.RootPath}/{item.Id}")).StatusCode);
        Assert.Equal(item.Id, Assert.Single((await ListAsync(client)).Items).Id);
    }

    [Fact]
    public async Task Archive_stays_playable_with_banner_flag_and_is_absent_from_public_list()
    {
        await using var factory = new CrosswordFactory();
        var (catalog, item) = await CreateAsync(factory);
        await catalog.SetPublicationAsync(item.Id, CrosswordStatus.Published, null, item.RowVersion, "publisher");
        item = (await catalog.GetByIdAsync(item.Id))!;
        await catalog.SetPublicationAsync(item.Id, CrosswordStatus.Archived, null, item.RowVersion, "publisher");
        using var client = factory.CreateAnonymousClient();
        var detail = await client.GetFromJsonAsync<CrosswordDetailDto>($"{CrosswordApiEndpoints.RootPath}/{item.Id}", JsonOptions);
        Assert.NotNull(detail);
        Assert.True(detail.Archived);
        Assert.Equal(0, (await ListAsync(client)).TotalCount);
        item = (await catalog.GetByIdAsync(item.Id))!;
        await catalog.SetPublicationAsync(item.Id, CrosswordStatus.Draft, null, item.RowVersion, "publisher");
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"{CrosswordApiEndpoints.RootPath}/{item.Id}")).StatusCode);
    }

    [Fact]
    public async Task Published_list_uses_v1_pagination_and_detail_not_found_is_problem_details()
    {
        await using var factory = new CrosswordFactory();
        var (catalog, first) = await CreateAsync(factory);
        var (_, second) = await CreateAsync(factory);
        await catalog.SetPublicationAsync(first.Id, CrosswordStatus.Published, null, first.RowVersion, "publisher");
        await catalog.SetPublicationAsync(second.Id, CrosswordStatus.Published, null, second.RowVersion, "publisher");
        using var client = factory.CreateAnonymousClient();
        var page = await client.GetFromJsonAsync<ApiPagedResponse<CrosswordListItemDto>>(
            $"{CrosswordApiEndpoints.RootPath}?page=2&pageSize=1", JsonOptions);
        Assert.NotNull(page);
        Assert.Equal(2, page.TotalCount);
        Assert.Equal(2, page.Page);
        Assert.Single(page.Items);
        var missing = await client.GetAsync($"{CrosswordApiEndpoints.RootPath}/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.Equal("application/problem+json", missing.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Read_endpoints_are_in_openapi_without_solution_fields()
    {
        await using var factory = new CrosswordFactory();
        using var client = factory.CreateAnonymousClient();
        using var document = JsonDocument.Parse(await client.GetStringAsync(ApiV1.OpenApiPath));
        var paths = document.RootElement.GetProperty("paths");
        Assert.True(paths.TryGetProperty(CrosswordApiEndpoints.RootPath, out _));
        Assert.True(paths.TryGetProperty(CrosswordApiEndpoints.RootPath + "/{id}", out _));
        var properties = document.RootElement.GetProperty("components").GetProperty("schemas")
            .GetProperty(nameof(CrosswordDetailDto)).GetProperty("properties");
        Assert.False(properties.TryGetProperty("answers", out _));
        Assert.False(properties.TryGetProperty("solutionRowsJson", out _));
        Assert.True(properties.TryGetProperty("archived", out _));
    }

    private static async Task<ApiPagedResponse<CrosswordListItemDto>> ListAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<ApiPagedResponse<CrosswordListItemDto>>(CrosswordApiEndpoints.RootPath, JsonOptions))!;

    private static async Task<(ICrosswordCatalogRepository Catalog, CrosswordCatalogItem Item)> CreateAsync(CrosswordFactory factory)
    {
        var catalog = factory.Services.GetRequiredService<ICrosswordCatalogRepository>();
        var seed = CrosswordSampleData.Load().Single(seed => seed.Slug == "meet-the-band");
        seed = seed with
        {
            Slug = "test-" + Guid.NewGuid().ToString("N"),
            Grid = seed.Grid with { Clues = seed.Grid.Clues.Select(clue => clue with { Explanation = "PRIVATE EXPLANATION TOKEN" }).ToArray() }
        };
        var id = await catalog.CreateDraftAsync(seed, Guid.NewGuid(), "editor");
        return (catalog, (await catalog.GetByIdAsync(id))!);
    }

    private sealed class CrosswordFactory : QueenZoneWebApplicationFactory
    {
        public FakeTimeProvider Clock { get; } = new(new DateTimeOffset(2026, 10, 4, 0, 0, 0, TimeSpan.Zero));

        protected override void ConfigureTestServices(IWebHostBuilder builder) =>
            builder.ConfigureServices(services => services.AddSingleton<TimeProvider>(Clock));
    }
}
