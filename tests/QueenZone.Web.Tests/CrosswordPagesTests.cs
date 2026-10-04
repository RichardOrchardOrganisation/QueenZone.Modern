using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AngleSharp.Html.Parser;
using Microsoft.Extensions.DependencyInjection;
using QueenZone.Data;
using QueenZone.Data.Entities;

namespace QueenZone.Web.Tests;

public sealed class CrosswordPagesTests
{
    [Fact]
    public async Task Public_pages_only_list_visible_puzzles_and_never_embed_solutions()
    {
        await using var host = new QueenZoneWebApplicationFactory();
        var puzzle = await Publish(host);
        using var client = host.CreateAnonymousClient();
        var list = await client.GetStringAsync("/crosswords");
        Assert.Contains("/crosswords/" + puzzle.Seed.Slug, list);
        Assert.Contains("No crosswords are available", await client.GetStringAsync("/crosswords?difficulty=hard&size=large"));
        var html = await client.GetStringAsync(Path(puzzle));
        var document = await new HtmlParser().ParseDocumentAsync(html);
        var grid = document.QuerySelector("[role=grid]")!;
        Assert.Equal(puzzle.Seed.Grid.Height.ToString(), grid.GetAttribute("aria-rowcount"));
        Assert.Equal(puzzle.Seed.Grid.Rows.Sum(row => row.Count(letter => letter != '#')), document.QuerySelectorAll("[data-cell]").Length);
        Assert.All(document.QuerySelectorAll("[data-cell]"), cell => Assert.True(cell.HasAttribute("disabled")));
        using var data = JsonDocument.Parse(document.QuerySelector("[data-puzzle]")!.TextContent);
        var projected = data.RootElement.GetProperty("puzzle");
        Assert.False(projected.TryGetProperty("rows", out _));
        Assert.Equal(puzzle.PlayVersion, projected.GetProperty("playVersion").GetGuid());
        Assert.All(projected.GetProperty("clues").EnumerateArray(), clue =>
        {
            Assert.False(clue.TryGetProperty("answer", out _));
            Assert.False(clue.TryGetProperty("explanation", out _));
        });
        Assert.DoesNotContain("Private explanation", html);
        var catalog = host.Services.GetRequiredService<ICrosswordCatalogRepository>();
        await catalog.SetPublicationAsync(puzzle.Id, CrosswordStatus.Archived, null, puzzle.RowVersion, "editor");
        Assert.DoesNotContain("/crosswords/" + puzzle.Seed.Slug, await client.GetStringAsync("/crosswords"));
        Assert.Contains("Archived puzzle", await client.GetStringAsync(Path(puzzle)));
        var archived = (await catalog.GetByIdAsync(puzzle.Id))!;
        await catalog.SetPublicationAsync(puzzle.Id, CrosswordStatus.Draft, null, archived.RowVersion, "editor");
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(Path(puzzle))).StatusCode);
    }

    [Theory]
    [InlineData("Check")]
    [InlineData("Reveal")]
    [InlineData("Save")]
    [InlineData("Complete")]
    public async Task Website_mutations_require_antiforgery_and_current_version(string handler)
    {
        await using var host = new QueenZoneWebApplicationFactory();
        var puzzle = await Publish(host);
        using var client = Member(host);
        var token = AdminHttpTestHelpers.ExtractAntiforgeryToken(await client.GetStringAsync(Path(puzzle)));
        object Body(Guid version) => handler switch
        {
            "Check" => new CrosswordCheckRequestDto(CrosswordPlayRules.EmptyLetters(puzzle.Seed.Grid), new("grid"), version),
            "Reveal" => new CrosswordRevealRequestDto(new("grid"), version),
            _ => Write(puzzle) with { PlayVersion = version }
        };
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(Path(puzzle) + "?handler=" + handler, Body(puzzle.PlayVersion))).StatusCode);
        client.DefaultRequestHeaders.Add("RequestVerificationToken", token);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(Path(puzzle) + "?handler=" + handler, Body(Guid.Empty))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync(Path(puzzle) + "?handler=" + handler, Body(Guid.NewGuid()))).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.GetAsync(Path(puzzle) + "?handler=Progress")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync(Path(puzzle) + "?handler=" + handler, Body(puzzle.PlayVersion))).StatusCode);
    }

    [Fact]
    public async Task Guests_can_check_and_reveal_but_member_saves_and_completions_require_sign_in()
    {
        await using var host = new QueenZoneWebApplicationFactory();
        var puzzle = await Publish(host);
        using var client = host.CreateAnonymousClient(allowAutoRedirect: false);
        client.DefaultRequestHeaders.Add("RequestVerificationToken", AdminHttpTestHelpers.ExtractAntiforgeryToken(await client.GetStringAsync(Path(puzzle))));
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync(Path(puzzle) + "?handler=Check",
            new CrosswordCheckRequestDto(CrosswordPlayRules.EmptyLetters(puzzle.Seed.Grid), new("grid"), puzzle.PlayVersion))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync(Path(puzzle) + "?handler=Reveal", new CrosswordRevealRequestDto(new("grid"), puzzle.PlayVersion))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync(Path(puzzle) + "?handler=Save", Write(puzzle))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync(Path(puzzle) + "?handler=Complete", Write(puzzle))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(Path(puzzle) + "?handler=Progress")).StatusCode);
    }

    [Fact]
    public async Task Cookie_save_restore_assistance_and_completion_use_the_shared_server_rules()
    {
        await using var host = new QueenZoneWebApplicationFactory();
        var puzzle = await Publish(host);
        using var client = Member(host);
        client.DefaultRequestHeaders.Add("RequestVerificationToken", AdminHttpTestHelpers.ExtractAntiforgeryToken(await client.GetStringAsync(Path(puzzle))));
        var write = Write(puzzle);
        await client.PostAsJsonAsync(Path(puzzle) + "?handler=Save", write);
        await client.PostAsJsonAsync(Path(puzzle) + "?handler=Reveal", new CrosswordRevealRequestDto(new("grid"), puzzle.PlayVersion));
        await client.PostAsJsonAsync(Path(puzzle) + "?handler=Save", write with { UpdatedAt = write.UpdatedAt.AddSeconds(-1), ElapsedSeconds = 1 });
        var restored = (await client.GetFromJsonAsync<CrosswordProgressDto>(Path(puzzle) + "?handler=Progress"))!;
        Assert.Equal(120, restored.ElapsedSeconds);
        Assert.NotEmpty(restored.RevealedCells);
        var wrong = await (await client.PostAsJsonAsync(Path(puzzle) + "?handler=Complete", write)).Content.ReadFromJsonAsync<CrosswordCompletionResultDto>();
        Assert.False(wrong!.Correct);
        Assert.Empty(wrong.Review);
        var solved = write with { Letters = string.Concat(puzzle.Seed.Grid.Rows), UpdatedAt = write.UpdatedAt.AddSeconds(1) };
        var result = (await (await client.PostAsJsonAsync(Path(puzzle) + "?handler=Complete", solved)).Content.ReadFromJsonAsync<CrosswordCompletionResultDto>())!;
        Assert.True(result.Correct);
        Assert.False(result.Completion!.Clean);
        var repeat = (await (await client.PostAsJsonAsync(Path(puzzle) + "?handler=Complete", solved with { ElapsedSeconds = 240 })).Content.ReadFromJsonAsync<CrosswordCompletionResultDto>())!;
        Assert.Equal(result.Completion, repeat.Completion);
        Assert.Contains("Completed", await client.GetStringAsync("/crosswords"));
    }

    [Fact]
    public async Task Offline_shell_excludes_identity_tokens_and_authenticated_header()
    {
        await using var host = new QueenZoneWebApplicationFactory();
        var puzzle = await Publish(host);
        using var client = Member(host);
        var member = client.DefaultRequestHeaders.GetValues("X-Crossword-Member").Single();
        using var shell = await client.GetAsync(Path(puzzle) + "?handler=OfflineShell");
        var html = await shell.Content.ReadAsStringAsync();
        Assert.Equal("public", shell.Headers.GetValues("X-QueenZone-Crossword-Shell").Single());
        Assert.DoesNotContain(member, html);
        Assert.DoesNotContain("__RequestVerificationToken", html);
        Assert.DoesNotContain("qz-site-header", html);
        using var sessionResponse = await client.GetAsync(Path(puzzle) + "?handler=Session");
        Assert.Contains("no-store", sessionResponse.Headers.CacheControl!.ToString());
        using var session = JsonDocument.Parse(await sessionResponse.Content.ReadAsStringAsync());
        Assert.Equal(member, session.RootElement.GetProperty("memberId").GetString());
        Assert.False(string.IsNullOrEmpty(session.RootElement.GetProperty("tokens").GetString()));
    }

    [Fact]
    public async Task Account_hint_never_authorizes_and_account_change_cannot_save_another_members_letters()
    {
        await using var host = new QueenZoneWebApplicationFactory();
        var puzzle = await Publish(host);
        using var anonymous = host.CreateAnonymousClient(allowAutoRedirect: false);
        anonymous.DefaultRequestHeaders.Add("RequestVerificationToken", AdminHttpTestHelpers.ExtractAntiforgeryToken(await anonymous.GetStringAsync(Path(puzzle))));
        var guessed = Guid.NewGuid().ToString();
        anonymous.DefaultRequestHeaders.Add("X-Crossword-Member", guessed);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsJsonAsync(Path(puzzle) + "?handler=Save", Write(puzzle))).StatusCode);
        using var member = Member(host);
        member.DefaultRequestHeaders.Remove("X-Crossword-Member");
        member.DefaultRequestHeaders.Add("X-Crossword-Member", guessed);
        member.DefaultRequestHeaders.Add("RequestVerificationToken", AdminHttpTestHelpers.ExtractAntiforgeryToken(await member.GetStringAsync(Path(puzzle))));
        Assert.Equal(HttpStatusCode.Conflict, (await member.PostAsJsonAsync(Path(puzzle) + "?handler=Save", Write(puzzle))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await member.PostAsJsonAsync(Path(puzzle) + "?handler=Complete", Write(puzzle))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await member.GetAsync(Path(puzzle) + "?handler=Progress")).StatusCode);
        member.DefaultRequestHeaders.Remove("X-Crossword-Member");
        member.DefaultRequestHeaders.Add("X-Crossword-Member", member.DefaultRequestHeaders.GetValues(TestMemberAuthHandler.MemberIdHeader).Single());
        Assert.Equal(HttpStatusCode.NoContent, (await member.GetAsync(Path(puzzle) + "?handler=Progress")).StatusCode);
    }

    [Fact]
    public async Task Home_teaser_hides_without_published_content_and_offers_continue_after_save()
    {
        await using var host = new QueenZoneWebApplicationFactory();
        using var client = Member(host);
        Assert.DoesNotContain("id=\"crossword-callout\"", await client.GetStringAsync("/"));
        var puzzle = await Publish(host);
        Assert.Contains("Play crossword", await client.GetStringAsync("/"));
        client.DefaultRequestHeaders.Add("RequestVerificationToken", AdminHttpTestHelpers.ExtractAntiforgeryToken(await client.GetStringAsync(Path(puzzle))));
        await client.PostAsJsonAsync(Path(puzzle) + "?handler=Save", Write(puzzle));
        Assert.Contains("Continue crossword", await client.GetStringAsync("/"));
        await client.PostAsJsonAsync(Path(puzzle) + "?handler=Complete", Write(puzzle) with { Letters = string.Concat(puzzle.Seed.Grid.Rows) });
        Assert.DoesNotContain("Continue crossword", await client.GetStringAsync("/"));
        Assert.Contains("Play crossword", await client.GetStringAsync("/"));
    }

    [Fact]
    public async Task Online_layout_refreshes_account_partition_but_public_offline_shell_has_no_identity()
    {
        await using var host = new QueenZoneWebApplicationFactory();
        var puzzle = await Publish(host);
        using var client = Member(host);
        var member = client.DefaultRequestHeaders.GetValues(TestMemberAuthHandler.MemberIdHeader).Single();
        Assert.Contains("data-crossword-member=\"" + member + "\"", await client.GetStringAsync(Path(puzzle)));
        Assert.DoesNotContain("data-crossword-member", await client.GetStringAsync(Path(puzzle) + "?handler=OfflineShell"));
        using var anonymous = host.CreateAnonymousClient();
        Assert.Contains("data-crossword-member=\"\"", await anonymous.GetStringAsync("/account/login"));
    }

    private static string Path(CrosswordCatalogItem puzzle) => "/crosswords/" + puzzle.Seed.Slug;
    private static CrosswordProgressRequestDto Write(CrosswordCatalogItem puzzle) =>
        new(CrosswordPlayRules.EmptyLetters(puzzle.Seed.Grid), 120, [], false, DateTimeOffset.UtcNow, puzzle.PlayVersion);
    private static HttpClient Member(QueenZoneWebApplicationFactory host)
    {
        var client = host.CreateAnonymousClient(allowAutoRedirect: false);
        var member = Guid.NewGuid().ToString();
        client.DefaultRequestHeaders.Add(TestMemberAuthHandler.MemberIdHeader, member);
        client.DefaultRequestHeaders.Add("X-Crossword-Member", member);
        return client;
    }
    private static async Task<CrosswordCatalogItem> Publish(QueenZoneWebApplicationFactory host)
    {
        var catalog = host.Services.GetRequiredService<ICrosswordCatalogRepository>();
        var seed = CrosswordSampleData.Load()[0];
        seed = seed with
        {
            Slug = "page-" + Guid.NewGuid().ToString("N"),
            Grid = seed.Grid with
            { Clues = seed.Grid.Clues.Select(clue => clue with { Explanation = "Private explanation" }).ToArray() }
        };
        await catalog.ImportAsync([seed], Guid.NewGuid(), "seed", publish: true);
        return (await catalog.GetAllAsync()).Single(item => item.Seed.Slug == seed.Slug);
    }
}
