using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using QueenZone.Data;
using QueenZone.Data.Entities;

namespace QueenZone.Web.Tests;

public sealed class CrosswordResultsApiTests
{
    [Fact]
    public async Task Leaderboard_projects_only_display_names_and_returns_my_rank_beyond_top_fifty()
    {
        await using var host = new QueenZoneWebApplicationFactory();
        var catalog = host.Services.GetRequiredService<ICrosswordCatalogRepository>();
        var seed = CrosswordSampleData.Load().Single(seed => seed.Slug == "meet-the-band");
        var initial = (await catalog.GetAllAsync()).Single(row => row.Seed.Slug == seed.Slug);
        await catalog.SetPublicationAsync(initial.Id, CrosswordStatus.Published, null, initial.RowVersion, "fixture");
        var puzzle = (await catalog.GetByIdAsync(initial.Id))!;
        var progress = host.Services.GetRequiredService<ICrosswordProgressRepository>();
        var ids = Enumerable.Range(0, 61).Select(_ => Guid.NewGuid()).ToArray();
        for (var index = 0; index < ids.Length; index++)
        {
            MemberBearerAccounts.Ensure(host.Services, ids[index], $"private-{index}@example.test", "Solver " + index);
            await progress.CompleteAsync(puzzle.Id, ids[index], new(string.Concat(seed.Grid.Rows), 100 + index, [], false, DateTimeOffset.UtcNow, puzzle.PlayVersion));
        }
        var tooFast = Guid.NewGuid(); var assisted = Guid.NewGuid();
        await progress.CompleteAsync(puzzle.Id, tooFast, new(string.Concat(seed.Grid.Rows), 1, [], false, DateTimeOffset.UtcNow, puzzle.PlayVersion));
        await progress.CompleteAsync(puzzle.Id, assisted, new(string.Concat(seed.Grid.Rows), 50, [], true, DateTimeOffset.UtcNow, puzzle.PlayVersion));
        // A second faster write cannot replace the member's immutable first solve.
        await progress.CompleteAsync(puzzle.Id, ids[60], new(string.Concat(seed.Grid.Rows), 30, [], false, DateTimeOffset.UtcNow, puzzle.PlayVersion));
        using var client = Bearer(host, ids[60]); var path = $"/api/v1/crosswords/{puzzle.Id}/leaderboard";
        var response = await client.GetAsync(path); Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("no-store", response.Headers.CacheControl!.ToString());
        var json = await response.Content.ReadAsStringAsync(); Assert.DoesNotContain("@example.test", json); Assert.DoesNotContain(ids[60].ToString(), json);
        var board = (await response.Content.ReadFromJsonAsync<CrosswordLeaderboardDto>())!;
        Assert.Equal(50, board.Top.Count); Assert.Equal(61, board.TotalMembers); Assert.Equal(61, board.Viewer!.Rank);
        Assert.Equal("Solver 60", board.Viewer.DisplayName); Assert.Equal(160, board.Viewer.ElapsedSeconds);
        using var guest = host.CreateAnonymousClient();
        Assert.Null((await guest.GetFromJsonAsync<CrosswordLeaderboardDto>(path))!.Viewer);
        Assert.Contains("leaderboard", await guest.GetStringAsync("/crosswords/meet-the-band/leaderboard"));
    }

    [Fact]
    public async Task My_crosswords_requires_auth_and_is_partitioned_by_actual_member_identity()
    {
        await using var host = new QueenZoneWebApplicationFactory();
        var catalog = host.Services.GetRequiredService<ICrosswordCatalogRepository>();
        var item = (await catalog.GetAllAsync())[0];
        await catalog.SetPublicationAsync(item.Id, CrosswordStatus.Published, null, item.RowVersion, "fixture");
        var puzzle = (await catalog.GetByIdAsync(item.Id))!; var owner = Guid.NewGuid(); var other = Guid.NewGuid();
        var progress = host.Services.GetRequiredService<ICrosswordProgressRepository>();
        await progress.CompleteAsync(puzzle.Id, owner, new(string.Concat(puzzle.Seed.Grid.Rows), 120, [], false, DateTimeOffset.UtcNow, puzzle.PlayVersion));
        using var member = Bearer(host, owner); using var second = Bearer(host, other); using var guest = host.CreateAnonymousClient(false);
        Assert.Equal(HttpStatusCode.Unauthorized, (await guest.GetAsync("/api/v1/crosswords/mine")).StatusCode);
        var history = (await member.GetFromJsonAsync<CrosswordHistoryDto>("/api/v1/crosswords/mine"))!;
        Assert.Equal(1, history.TotalCompleted); Assert.Equal(1, history.WeeklyStreak); Assert.True(Assert.Single(history.Items).Clean);
        Assert.Empty((await second.GetFromJsonAsync<CrosswordHistoryDto>("/api/v1/crosswords/mine"))!.Items);
        var latest = (await catalog.GetByIdAsync(puzzle.Id))!;
        await catalog.SetPublicationAsync(puzzle.Id, CrosswordStatus.Draft, null, latest.RowVersion, "fixture");
        Assert.Equal(HttpStatusCode.NotFound, (await guest.GetAsync($"/api/v1/crosswords/{puzzle.Id}/leaderboard")).StatusCode);
        Assert.False(Assert.Single((await member.GetFromJsonAsync<CrosswordHistoryDto>("/api/v1/crosswords/mine"))!.Items).Playable);
        using var cookie = host.CreateAnonymousClient(false); cookie.DefaultRequestHeaders.Add(TestMemberAuthHandler.MemberIdHeader, owner.ToString());
        Assert.Contains(puzzle.Seed.Title, await cookie.GetStringAsync("/account/crosswords"));
    }

    private static HttpClient Bearer(QueenZoneWebApplicationFactory host, Guid id)
    {
        MemberBearerAccounts.Ensure(host.Services, id, $"{id:N}@example.test", "Member");
        using var scope = host.Services.CreateScope(); var issuer = scope.ServiceProvider.GetRequiredService<MobileAuthTokenIssuer>();
        var client = host.CreateAnonymousClient(false);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", issuer.IssueAccessToken(id, $"{id:N}@example.test", "Member"));
        return client;
    }
}
