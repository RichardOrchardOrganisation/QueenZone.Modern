using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AngleSharp.Html.Parser;
using Microsoft.Extensions.DependencyInjection;
using QueenZone.Data;
using QueenZone.Data.Entities;

namespace QueenZone.Web.Tests;

public sealed class AdminCrosswordRoutesTests
{
    [Fact]
    public async Task List_filters_all_ten_drafts_and_requires_existing_admin_policy()
    {
        await using var host = new QueenZoneWebApplicationFactory();
        var catalog = host.Services.GetRequiredService<ICrosswordCatalogRepository>();
        await catalog.ImportAsync(CrosswordSampleData.Load(), Guid.NewGuid(), "tool");
        using var admin = host.CreateAdminClient();
        var html = await admin.GetStringAsync("/admin/crosswords");
        Assert.Contains("Meet the Band", html);
        var document = await new HtmlParser().ParseDocumentAsync(html);
        Assert.Equal(10, document.QuerySelectorAll("tbody tr").Length);
        Assert.Empty((await new HtmlParser().ParseDocumentAsync(await admin.GetStringAsync("/admin/crosswords?status=Published"))).QuerySelectorAll("tbody tr"));
        Assert.Single((await new HtmlParser().ParseDocumentAsync(await admin.GetStringAsync("/admin/crosswords?search=Meet"))).QuerySelectorAll("tbody tr"));
        using var guest = host.CreateAnonymousClient(false);
        Assert.Contains((await guest.GetAsync("/admin/crosswords")).StatusCode, new[] { HttpStatusCode.Unauthorized, HttpStatusCode.Redirect });
        using var other = host.CreateAdminClient("other@test.local");
        Assert.Contains((await other.GetAsync("/admin/crosswords")).StatusCode, new[] { HttpStatusCode.Forbidden, HttpStatusCode.Redirect });
    }

    [Fact]
    public async Task Draft_round_trip_preserves_grid_and_clues_and_stale_edit_does_not_overwrite()
    {
        await using var host = new QueenZoneWebApplicationFactory(); using var admin = host.CreateAdminClient();
        var seed = CrosswordSampleData.Load()[0] with { Slug = "admin-route-test" };
        var response = await AdminHttpTestHelpers.PostArticleAsync(admin, "/admin/crosswords/new", "/admin/crosswords/new",
            new() { ["SeedJson"] = CrosswordSeedJson.Export(seed) });
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var catalog = host.Services.GetRequiredService<ICrosswordCatalogRepository>();
        var item = Assert.Single(await catalog.GetAllAsync(), row => row.Seed.Slug == seed.Slug);
        Assert.Equal(CrosswordSeedJson.Export(seed), CrosswordSeedJson.Export(item.Seed));
        var path = $"/admin/crosswords/{item.Id}/edit";
        var fields = new Dictionary<string, string> { ["SeedJson"] = CrosswordSeedJson.Export(seed with { Title = "First edit" }), ["RowVersion"] = Convert.ToBase64String(item.RowVersion) };
        Assert.Equal(HttpStatusCode.Redirect, (await AdminHttpTestHelpers.PostArticleAsync(admin, path, path, fields)).StatusCode);
        fields["SeedJson"] = CrosswordSeedJson.Export(seed with { Title = "Stale edit" });
        var stale = await AdminHttpTestHelpers.PostArticleAsync(admin, path, path, fields);
        Assert.Equal(HttpStatusCode.OK, stale.StatusCode);
        Assert.Contains("Another admin changed", await stale.Content.ReadAsStringAsync());
        Assert.Equal("First edit", (await catalog.GetByIdAsync(item.Id))!.Seed.Title);
        Assert.Equal(2, (await catalog.GetAuditAsync(item.Id)).Count);
    }

    [Fact]
    public async Task Preview_uses_real_player_but_check_reveal_never_create_member_progress_or_completions()
    {
        await using var host = new QueenZoneWebApplicationFactory();
        var catalog = host.Services.GetRequiredService<ICrosswordCatalogRepository>();
        var seed = CrosswordSampleData.Load()[0] with { Slug = "admin-route-test" }; var id = await catalog.CreateDraftAsync(seed, Guid.NewGuid(), "editor");
        var puzzle = (await catalog.GetByIdAsync(id))!;
        using var admin = host.CreateAdminClient(); var path = $"/admin/crosswords/{id}/preview";
        var html = await admin.GetStringAsync(path);
        Assert.Contains("Preview — not published", html); Assert.Contains("data-crossword", html);
        var document = await new HtmlParser().ParseDocumentAsync(html);
        using var config = JsonDocument.Parse(document.QuerySelector("[data-puzzle]")!.TextContent);
        Assert.True(config.RootElement.GetProperty("preview").GetBoolean());
        Assert.Equal(JsonValueKind.Null, config.RootElement.GetProperty("memberId").ValueKind);
        admin.DefaultRequestHeaders.Add("RequestVerificationToken", AdminHttpTestHelpers.ExtractAntiforgeryToken(html));
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsJsonAsync(path + "?handler=Reveal", new CrosswordRevealRequestDto(new("grid"), puzzle.PlayVersion))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsJsonAsync(path + "?handler=Check", new CrosswordCheckRequestDto(string.Concat(seed.Grid.Rows), new("grid"), puzzle.PlayVersion, true))).StatusCode);
        var progress = host.Services.GetRequiredService<ICrosswordProgressRepository>();
        Assert.Empty(await progress.GetForPuzzleAsync(id)); Assert.Empty(await progress.GetCompletionsAsync(id, null));
        using var guest = host.CreateAnonymousClient(false);
        Assert.Equal(HttpStatusCode.NotFound, (await guest.GetAsync("/crosswords/" + seed.Slug)).StatusCode);
        Assert.Contains((await guest.GetAsync(path)).StatusCode, new[] { HttpStatusCode.Unauthorized, HttpStatusCode.Redirect });
    }

    [Fact]
    public async Task Import_previews_json_then_creates_one_Draft_and_never_overwrites_colliding_slug()
    {
        await using var host = new QueenZoneWebApplicationFactory(); using var admin = host.CreateAdminClient();
        var original = CrosswordSampleData.Load()[0]; var json = CrosswordSeedJson.Export(original);
        var preview = await AdminHttpTestHelpers.PostArticleMultipartAsync(admin, "/admin/crosswords/import", "/admin/crosswords/import?handler=Preview", [],
            System.Text.Encoding.UTF8.GetBytes(json), "puzzle.json", "application/json", fileFieldName: "Upload");
        Assert.Equal(HttpStatusCode.OK, preview.StatusCode); Assert.Contains("Create new Draft", await preview.Content.ReadAsStringAsync());
        var collision = await AdminHttpTestHelpers.PostArticleAsync(admin, "/admin/crosswords/import", "/admin/crosswords/import?handler=Create",
            new() { ["SeedJson"] = json, ["NewSlug"] = original.Slug });
        Assert.Contains("nothing was overwritten", await collision.Content.ReadAsStringAsync());
        var imported = await AdminHttpTestHelpers.PostArticleAsync(admin, "/admin/crosswords/import", "/admin/crosswords/import?handler=Create",
            new() { ["SeedJson"] = json, ["NewSlug"] = "route-import-copy" });
        Assert.Equal(HttpStatusCode.Redirect, imported.StatusCode);
        var catalog = host.Services.GetRequiredService<ICrosswordCatalogRepository>();
        var item = Assert.Single(await catalog.GetAllAsync(), row => row.Seed.Slug == "route-import-copy");
        Assert.Equal(CrosswordStatus.Draft, item.Status); Assert.Equal(original.Grid.Rows, item.Seed.Grid.Rows);
        Assert.Equal("Imported", Assert.Single(await catalog.GetAuditAsync(item.Id)).Action);
        var badType = await AdminHttpTestHelpers.PostArticleMultipartAsync(admin, "/admin/crosswords/import", "/admin/crosswords/import?handler=Preview", [],
            System.Text.Encoding.UTF8.GetBytes(json), "puzzle.json", "text/plain", fileFieldName: "Upload");
        Assert.Contains("application/json", await badType.Content.ReadAsStringAsync());
        var malformed = await AdminHttpTestHelpers.PostArticleMultipartAsync(admin, "/admin/crosswords/import", "/admin/crosswords/import?handler=Preview", [],
            System.Text.Encoding.UTF8.GetBytes("{broken"), "puzzle.json", "application/json", fileFieldName: "Upload");
        Assert.Contains("valid JSON", await malformed.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Live_validator_requires_antiforgery_and_invalid_draft_cannot_be_published()
    {
        await using var host = new QueenZoneWebApplicationFactory(); using var admin = host.CreateAdminClient();
        var valid = CrosswordSampleData.Load()[0] with { Slug = "unfinished-admin-draft" };
        var seed = valid with { Grid = valid.Grid with { Clues = [] } };
        var path = "/admin/crosswords/new";
        var input = JsonSerializer.Deserialize<JsonElement>(CrosswordSeedJson.Export(seed));
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync(path + "?handler=Validate", input)).StatusCode);
        admin.DefaultRequestHeaders.Add("RequestVerificationToken", AdminHttpTestHelpers.ExtractAntiforgeryToken(await admin.GetStringAsync(path)));
        var result = await admin.PostAsJsonAsync(path + "?handler=Validate", input);
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        using var data = JsonDocument.Parse(await result.Content.ReadAsStringAsync());
        Assert.NotEmpty(data.RootElement.GetProperty("errors").EnumerateArray()); Assert.NotEmpty(data.RootElement.GetProperty("runs").EnumerateArray());
        var created = await AdminHttpTestHelpers.PostArticleAsync(admin, path, path, new() { ["SeedJson"] = CrosswordSeedJson.Export(seed) });
        Assert.Equal(HttpStatusCode.Redirect, created.StatusCode);
        var catalog = host.Services.GetRequiredService<ICrosswordCatalogRepository>();
        var item = Assert.Single(await catalog.GetAllAsync(), row => row.Seed.Slug == seed.Slug);
        var publish = await AdminHttpTestHelpers.PostArticleAsync(admin, "/admin/crosswords", "/admin/crosswords?handler=Publication",
            new() { ["id"] = item.Id.ToString(), ["RowVersion"] = Convert.ToBase64String(item.RowVersion), ["status"] = "Published" });
        Assert.Equal(HttpStatusCode.OK, publish.StatusCode); Assert.Equal(CrosswordStatus.Draft, (await catalog.GetByIdAsync(item.Id))!.Status);
        Assert.Single(await catalog.GetAuditAsync(item.Id));
    }

    [Fact]
    public async Task Duplicate_has_one_audit_and_no_attempts_and_export_keeps_private_answers()
    {
        await using var host = new QueenZoneWebApplicationFactory();
        var catalog = host.Services.GetRequiredService<ICrosswordCatalogRepository>(); var seed = CrosswordSampleData.Load()[0] with { Slug = "admin-route-test" };
        var id = await catalog.CreateDraftAsync(seed, Guid.NewGuid(), "editor");
        using var admin = host.CreateAdminClient();
        var response = await AdminHttpTestHelpers.PostArticleAsync(admin, "/admin/crosswords", "/admin/crosswords?handler=Duplicate",
            new() { ["id"] = id.ToString(), ["newSlug"] = "a-new-copy" });
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var copy = (await catalog.GetAllAsync()).Single(row => row.Seed.Slug == "a-new-copy");
        Assert.Equal("Copy of " + seed.Title, copy.Seed.Title); Assert.Equal(CrosswordStatus.Draft, copy.Status);
        Assert.Equal(seed.Grid.Rows, copy.Seed.Grid.Rows); Assert.Equal(seed.Grid.Clues, copy.Seed.Grid.Clues);
        Assert.Equal("Duplicated", Assert.Single(await catalog.GetAuditAsync(copy.Id)).Action);
        Assert.Empty(await host.Services.GetRequiredService<ICrosswordProgressRepository>().GetForPuzzleAsync(copy.Id));
        var export = await admin.GetAsync($"/admin/crosswords/{id}/edit?handler=Export");
        Assert.Equal("application/json", export.Content.Headers.ContentType!.MediaType);
        Assert.Equal(CrosswordSeedJson.Export(seed), await export.Content.ReadAsStringAsync());
    }
}
