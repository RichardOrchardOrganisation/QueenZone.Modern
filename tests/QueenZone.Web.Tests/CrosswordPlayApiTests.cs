using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using QueenZone.Data;
using QueenZone.Data.Entities;

namespace QueenZone.Web.Tests;

public sealed class CrosswordPlayApiTests
{
    private static readonly Guid Member = Guid.Parse("77777777-8888-9999-aaaa-bbbbbbbbbbbb");

    [Fact]
    public async Task Anonymous_can_check_selected_letters_and_reveal_but_cannot_save_or_complete()
    {
        await using var host = new PlayFactory();
        var puzzle = await Publish(host);
        using var client = host.CreateAnonymousClient();
        var root = Root(puzzle);
        var run = CrosswordGridValidator.Validate(puzzle.Seed.Grid).Runs[0];
        var selection = new CrosswordSelectionDto("entry", Number: run.Number, Direction: Direction(run.Direction));
        var checkedResponse = await client.PostAsJsonAsync(root + "/check", new CrosswordCheckRequestDto(run.Answer.ToLowerInvariant(), selection));
        Assert.Equal(HttpStatusCode.OK, checkedResponse.StatusCode);
        Assert.True(checkedResponse.Headers.CacheControl?.NoStore);
        var check = (await checkedResponse.Content.ReadFromJsonAsync<CrosswordCheckResultDto>())!;
        Assert.All(check.Cells, cell => Assert.Equal("correct", cell.Status));
        Assert.DoesNotContain(run.Answer, await checkedResponse.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.False(check.Complete);
        Assert.Contains(check.Explanations, clue => clue.Number == run.Number && clue.Direction == Direction(run.Direction));
        var reveal = (await (await client.PostAsJsonAsync(root + "/reveal", new CrosswordRevealRequestDto(selection)))
            .Content.ReadFromJsonAsync<CrosswordRevealResultDto>())!;
        Assert.Equal(run.Answer, string.Concat(reveal.Cells.Select(cell => cell.Letter)));
        Assert.False(reveal.Clean);
        var repository = host.Services.GetRequiredService<ICrosswordProgressRepository>();
        Assert.Empty(await repository.GetForMemberAsync(Member));
        var write = Write(host, puzzle);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PutAsJsonAsync(root + "/progress", write)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(root + "/progress")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync(root + "/complete", write)).StatusCode);
    }

    [Fact]
    public async Task Member_progress_isolated_and_reveal_or_auto_check_cannot_be_erased_by_save()
    {
        await using var host = new PlayFactory();
        var puzzle = await Publish(host);
        using var member = Bearer(host, Member);
        using var other = Bearer(host, Guid.NewGuid());
        var root = Root(puzzle);
        Assert.Equal(HttpStatusCode.NoContent, (await member.GetAsync(root + "/progress")).StatusCode);
        var cells = CrosswordPlayRules.SelectCells(puzzle.Seed.Grid, new("grid"));
        Assert.Equal(HttpStatusCode.OK, (await member.PostAsJsonAsync(root + "/reveal",
            new CrosswordRevealRequestDto(new("cell", cells[0])))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await member.PostAsJsonAsync(root + "/check",
            new CrosswordCheckRequestDto("", new("entry", Number: 999, Direction: "down")))).StatusCode);
        var write = Write(host, puzzle);
        var checkedResponse = await member.PostAsJsonAsync(root + "/check", new CrosswordCheckRequestDto(write.Letters, new("grid"), true));
        Assert.Equal(HttpStatusCode.OK, checkedResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await member.PutAsJsonAsync(root + "/progress", write)).StatusCode);
        var saved = (await member.GetFromJsonAsync<CrosswordProgressDto>(root + "/progress"))!;
        Assert.True(saved.AutoCheckUsed);
        Assert.Equal(new[] { cells[0] }, saved.RevealedCells);
        Assert.Equal(HttpStatusCode.NoContent, (await other.GetAsync(root + "/progress")).StatusCode);
        host.Clock.Advance(TimeSpan.FromSeconds(1));
        var complete = (await (await member.PostAsJsonAsync(root + "/complete", write with
        {
            Letters = string.Concat(puzzle.Seed.Grid.Rows),
            UpdatedAt = host.Clock.GetUtcNow()
        })).Content.ReadFromJsonAsync<CrosswordCompletionResultDto>())!;
        Assert.True(complete.Correct);
        Assert.False(complete.Completion!.Clean);
        Assert.False(complete.Completion.RankingEligible);
        Assert.Equal(puzzle.Seed.Grid.Clues.Count, complete.Review.Count);
    }

    [Fact]
    public async Task Completion_rechecks_and_keeps_first_result_without_revealing_answers_when_wrong()
    {
        await using var host = new PlayFactory();
        var puzzle = await Publish(host);
        using var member = Bearer(host, Member);
        var root = Root(puzzle);
        var write = Write(host, puzzle);
        var wrong = (await (await member.PostAsJsonAsync(root + "/complete", write)).Content.ReadFromJsonAsync<CrosswordCompletionResultDto>())!;
        Assert.False(wrong.Correct);
        Assert.Null(wrong.Completion);
        Assert.Empty(wrong.Review);
        host.Clock.Advance(TimeSpan.FromSeconds(1));
        var solved = write with { Letters = string.Concat(puzzle.Seed.Grid.Rows), UpdatedAt = host.Clock.GetUtcNow() };
        var first = (await (await member.PostAsJsonAsync(root + "/complete", solved)).Content.ReadFromJsonAsync<CrosswordCompletionResultDto>())!;
        Assert.True(first.Correct);
        Assert.True(first.Completion!.Clean);
        Assert.True(first.Completion.RankingEligible);
        Assert.Equal(120, first.Completion.ElapsedSeconds);
        Assert.All(first.Review, entry => Assert.Equal("Private fact", entry.Explanation));
        host.Clock.Advance(TimeSpan.FromHours(1));
        var replay = (await (await member.PostAsJsonAsync(root + "/complete", solved with
        {
            ElapsedSeconds = 240,
            AutoCheckUsed = true,
            UpdatedAt = host.Clock.GetUtcNow()
        })).Content.ReadFromJsonAsync<CrosswordCompletionResultDto>())!;
        Assert.Equal(first.Completion, replay.Completion);
        var afterWrong = (await (await member.PostAsJsonAsync(root + "/complete", write)).Content.ReadFromJsonAsync<CrosswordCompletionResultDto>())!;
        Assert.False(afterWrong.Correct);
        Assert.Equal(first.Completion, afterWrong.Completion);
        Assert.Empty(afterWrong.Review);
        var detail = await member.GetStringAsync(root);
        Assert.DoesNotContain("Private fact", detail, StringComparison.Ordinal);
        Assert.DoesNotContain("answer", detail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Invalid_selections_and_progress_return_problem_details_without_state_change()
    {
        await using var host = new PlayFactory();
        var puzzle = await Publish(host);
        using var member = Bearer(host, Member);
        var root = Root(puzzle);
        var requests = new[]
        {
            new CrosswordCheckRequestDto("", new("bad")),
            new CrosswordCheckRequestDto("A", new("cell", -1)),
            new CrosswordCheckRequestDto("A", new("entry", Number: 1, Direction: "diagonal")),
            new CrosswordCheckRequestDto("?", new("cell", CrosswordPlayRules.SelectCells(puzzle.Seed.Grid, new("grid"))[0])),
            new CrosswordCheckRequestDto("ABC", new("grid")),
            new CrosswordCheckRequestDto("ABC", null!)
        };
        foreach (var request in requests)
        {
            var response = await member.PostAsJsonAsync(root + "/check", request);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        }
        var invalid = await member.PutAsJsonAsync(root + "/progress", Write(host, puzzle) with { ElapsedSeconds = -1 });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await member.GetAsync(root + "/progress")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await member.PostAsJsonAsync(root + "/reveal", new CrosswordRevealRequestDto(new("cell", 999)))).StatusCode);
    }

    [Fact]
    public async Task Draft_mutations_are_hidden_and_archive_mutations_remain_playable()
    {
        await using var host = new PlayFactory();
        var puzzle = await Publish(host);
        using var member = Bearer(host, Member);
        var root = Root(puzzle);
        var catalog = host.Services.GetRequiredService<ICrosswordCatalogRepository>();
        await catalog.SetPublicationAsync(puzzle.Id, CrosswordStatus.Archived, null, puzzle.RowVersion, "editor");
        Assert.Equal(HttpStatusCode.OK, (await member.PostAsJsonAsync(root + "/check", new CrosswordCheckRequestDto(Write(host, puzzle).Letters, new("grid")))).StatusCode);
        var archived = (await catalog.GetByIdAsync(puzzle.Id))!;
        await catalog.SetPublicationAsync(puzzle.Id, CrosswordStatus.Draft, null, archived.RowVersion, "editor");
        Assert.Equal(HttpStatusCode.NotFound, (await member.PostAsJsonAsync(root + "/check", new CrosswordCheckRequestDto(Write(host, puzzle).Letters, new("grid")))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await member.PostAsJsonAsync(root + "/reveal", new CrosswordRevealRequestDto(new("grid")))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await member.PutAsJsonAsync(root + "/progress", Write(host, puzzle))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await member.PostAsJsonAsync(root + "/complete", Write(host, puzzle))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await member.GetAsync(root + "/progress")).StatusCode);
    }

    [Fact]
    public async Task Play_routes_are_documented_in_openapi()
    {
        await using var host = new PlayFactory();
        using var client = host.CreateAnonymousClient();
        using var document = JsonDocument.Parse(await client.GetStringAsync(ApiV1.OpenApiPath));
        var paths = document.RootElement.GetProperty("paths");
        foreach (var suffix in new[] { "check", "reveal", "progress", "complete" })
        {
            Assert.True(paths.TryGetProperty(CrosswordApiEndpoints.RootPath + "/{id}/" + suffix, out _));
        }
    }

    [Fact]
    public async Task Personal_list_status_tracks_saved_progress_and_first_completion_without_anonymous_leak()
    {
        await using var host = new PlayFactory();
        var puzzle = await Publish(host);
        using var member = Bearer(host, Member);
        using var anonymous = host.CreateAnonymousClient();
        var first = (await member.GetFromJsonAsync<ApiPagedResponse<CrosswordListItemDto>>(CrosswordApiEndpoints.RootPath))!
            .Items.Single(item => item.Id == puzzle.Id);
        Assert.Equal("notStarted", first.Progress);
        Assert.Null(first.ElapsedSeconds);
        var write = Write(host, puzzle);
        await member.PutAsJsonAsync(Root(puzzle) + "/progress", write);
        var started = (await member.GetFromJsonAsync<ApiPagedResponse<CrosswordListItemDto>>(CrosswordApiEndpoints.RootPath))!
            .Items.Single(item => item.Id == puzzle.Id);
        Assert.Equal("inProgress", started.Progress);
        Assert.Equal(0, started.ProgressPercent);
        Assert.Equal(120, started.ElapsedSeconds);
        host.Clock.Advance(TimeSpan.FromSeconds(1));
        await member.PostAsJsonAsync(Root(puzzle) + "/complete", write with
        {
            Letters = string.Concat(puzzle.Seed.Grid.Rows),
            UpdatedAt = host.Clock.GetUtcNow()
        });
        var completed = (await member.GetFromJsonAsync<ApiPagedResponse<CrosswordListItemDto>>(CrosswordApiEndpoints.RootPath))!
            .Items.Single(item => item.Id == puzzle.Id);
        Assert.Equal("completed", completed.Progress);
        Assert.Equal(100, completed.ProgressPercent);
        Assert.Equal(120, completed.ElapsedSeconds);
        var publicItem = (await anonymous.GetFromJsonAsync<ApiPagedResponse<CrosswordListItemDto>>(CrosswordApiEndpoints.RootPath))!
            .Items.Single(item => item.Id == puzzle.Id);
        Assert.Null(publicItem.Progress);
        Assert.Null(publicItem.ProgressPercent);
        Assert.Null(publicItem.ElapsedSeconds);
    }

    private static string Root(CrosswordCatalogItem puzzle) => CrosswordApiEndpoints.RootPath + "/" + puzzle.Id;
    private static string Direction(CrosswordDirection direction) => direction == CrosswordDirection.Across ? "across" : "down";
    private static CrosswordProgressRequestDto Write(PlayFactory host, CrosswordCatalogItem puzzle) =>
        new(CrosswordPlayRules.EmptyLetters(puzzle.Seed.Grid), 120, [], false, host.Clock.GetUtcNow());

    private static async Task<CrosswordCatalogItem> Publish(PlayFactory host)
    {
        var catalog = host.Services.GetRequiredService<ICrosswordCatalogRepository>();
        var seed = CrosswordSampleData.Load().Single(seed => seed.Slug == "meet-the-band");
        seed = seed with
        {
            Slug = "test-" + Guid.NewGuid().ToString("N"),
            Grid = seed.Grid with { Clues = seed.Grid.Clues.Select(clue => clue with { Explanation = "Private fact" }).ToArray() }
        };
        await catalog.ImportAsync([seed], Member, "seed", publish: true);
        return (await catalog.GetAllAsync()).Single(puzzle => puzzle.Seed.Slug == seed.Slug);
    }

    private static HttpClient Bearer(PlayFactory host, Guid id)
    {
        MemberBearerAccounts.Ensure(host.Services, id, $"{id:N}@example.test", "Crossword member");
        using var scope = host.Services.CreateScope();
        var issuer = scope.ServiceProvider.GetRequiredService<MobileAuthTokenIssuer>();
        var client = host.CreateAnonymousClient(allowAutoRedirect: false);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            issuer.IssueAccessToken(id, $"{id:N}@example.test", "Crossword member"));
        return client;
    }

    private sealed class PlayFactory : QueenZoneWebApplicationFactory
    {
        public FakeTimeProvider Clock { get; } = new(DateTimeOffset.UtcNow);
        protected override void ConfigureTestServices(IWebHostBuilder builder) =>
            builder.ConfigureServices(services => services.AddSingleton<TimeProvider>(Clock));
    }
}
