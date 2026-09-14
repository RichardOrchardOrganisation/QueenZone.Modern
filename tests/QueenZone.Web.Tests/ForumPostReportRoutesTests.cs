using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using QueenZone.Data;
using QueenZone.Data.Entities;
using QueenZone.Web;

namespace QueenZone.Web.Tests;

public sealed partial class ForumPostReportRoutesTests : IClassFixture<QueenZoneWebApplicationFactory>
{
    private const string AdminEmail = "admin@test.local";
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private readonly QueenZoneWebApplicationFactory factory;

    public ForumPostReportRoutesTests(QueenZoneWebApplicationFactory factory)
    {
        this.factory = factory;
    }

    [Fact]
    public async Task ReportApi_RequiresBearerAndCreatesOneSnapshotForDuplicateSubmissions()
    {
        var author = await CreateMemberAsync("Forum report author");
        var reporter = await CreateMemberAsync("Forum report reporter");
        var postId = await CreatePostAsync(author, "Evidence <strong>at report time</strong>");

        using var anonymous = factory.CreateAnonymousClient(allowAutoRedirect: false);
        using var unauthorized = await anonymous.PostAsJsonAsync(
            $"/api/v1/me/forum/posts/{postId}/report",
            new { category = ForumPostReportCategories.Spam, details = "Spam links" });
        Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode);

        using var client = CreateBearerClient(reporter);
        using var first = await client.PostAsJsonAsync(
            $"/api/v1/me/forum/posts/{postId}/report",
            new { category = ForumPostReportCategories.Harassment, details = "  Supporting context  " });
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        var created = await first.Content.ReadFromJsonAsync<ForumPostReportResponseDto>(JsonOptions);
        Assert.NotNull(created);
        Assert.False(created!.AlreadyReported);

        using var duplicate = await client.PostAsJsonAsync(
            $"/api/v1/me/forum/posts/{postId}/report",
            new { category = ForumPostReportCategories.Other, details = "Different details" });
        Assert.Equal(HttpStatusCode.OK, duplicate.StatusCode);
        var existing = await duplicate.Content.ReadFromJsonAsync<ForumPostReportResponseDto>(JsonOptions);
        Assert.Equal(created.ReportId, existing!.ReportId);
        Assert.True(existing.AlreadyReported);

        var repository = factory.Services.GetRequiredService<IForumPostReportRepository>();
        var report = await repository.GetAsync(created.ReportId);
        Assert.NotNull(report);
        Assert.Equal("Supporting context", report!.Details);
        Assert.Equal(author.Id, report.ReportedMemberId);
        Assert.Equal("Evidence <strong>at report time</strong>", report.PostBodySnapshot);
        Assert.Equal("Ranking every studio album", report.ThreadTitleSnapshot);
    }

    [Fact]
    public async Task ReportApi_RejectsInvalidOwnAndMissingPosts()
    {
        var author = await CreateMemberAsync("Forum report validation");
        var postId = await CreatePostAsync(author, "Author-owned post");
        using var client = CreateBearerClient(author);

        using var invalid = await client.PostAsJsonAsync(
            $"/api/v1/me/forum/posts/{postId}/report",
            new { category = "Invalid", details = (string?)null });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);

        using var own = await client.PostAsJsonAsync(
            $"/api/v1/me/forum/posts/{postId}/report",
            new { category = ForumPostReportCategories.Other, details = (string?)null });
        Assert.Equal(HttpStatusCode.BadRequest, own.StatusCode);

        using var missing = await client.PostAsJsonAsync(
            "/api/v1/me/forum/posts/2147483647/report",
            new { category = ForumPostReportCategories.Other, details = (string?)null });
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Fact]
    public async Task BlockApi_ReusesMemberBlockAndModerationState()
    {
        var author = await CreateMemberAsync("Forum block author");
        var viewer = await CreateMemberAsync("Forum block viewer");
        var postId = await CreatePostAsync(author, "Blockable post");
        using var client = CreateBearerClient(viewer);

        using var blocked = await client.PostAsync($"/api/v1/me/forum/posts/{postId}/block", null);
        Assert.Equal(HttpStatusCode.NoContent, blocked.StatusCode);

        using var stateResponse = await client.PostAsJsonAsync(
            "/api/v1/me/forum/posts/moderation-state",
            new { postIds = new[] { postId }, authorMemberIds = new[] { author.Id } });
        Assert.Equal(HttpStatusCode.OK, stateResponse.StatusCode);
        Assert.Equal("no-store", stateResponse.Headers.CacheControl?.ToString());
        var state = await stateResponse.Content.ReadFromJsonAsync<ForumPostModerationStateDto>(JsonOptions);
        Assert.Contains(author.Id, state!.BlockedMemberIds);

        var messages = factory.Services.GetRequiredService<PrivateMessageService>();
        Assert.Contains(author.Id, await messages.ListBlockedMemberIdsAsync(viewer.Id, [author.Id]));
    }

    [Fact]
    public async Task UnblockApi_ReusesMemberBlockAndClearsModerationState()
    {
        var author = await CreateMemberAsync("Forum unblock author");
        var viewer = await CreateMemberAsync("Forum unblock viewer");
        var postId = await CreatePostAsync(author, "Unblockable post");
        using var anonymous = factory.CreateAnonymousClient(allowAutoRedirect: false);
        using var unauthorized = await anonymous.PostAsync($"/api/v1/me/forum/posts/{postId}/unblock", null);
        Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode);

        using var client = CreateBearerClient(viewer);
        using var missing = await client.PostAsync("/api/v1/me/forum/posts/2147483647/unblock", null);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);

        using var blocked = await client.PostAsync($"/api/v1/me/forum/posts/{postId}/block", null);
        Assert.Equal(HttpStatusCode.NoContent, blocked.StatusCode);

        using var unblocked = await client.PostAsync($"/api/v1/me/forum/posts/{postId}/unblock", null);
        Assert.Equal(HttpStatusCode.NoContent, unblocked.StatusCode);

        using var stateResponse = await client.PostAsJsonAsync(
            "/api/v1/me/forum/posts/moderation-state",
            new { postIds = new[] { postId }, authorMemberIds = new[] { author.Id } });
        Assert.Equal(HttpStatusCode.OK, stateResponse.StatusCode);
        var state = await stateResponse.Content.ReadFromJsonAsync<ForumPostModerationStateDto>(JsonOptions);
        Assert.DoesNotContain(author.Id, state!.BlockedMemberIds);

        var messages = factory.Services.GetRequiredService<PrivateMessageService>();
        Assert.False(await messages.HasBlockedAsync(viewer.Id, author.Id));
    }

    [Fact]
    public async Task WebsiteReport_ChallengesAnonymousAndReturnsMemberToExactPost()
    {
        var author = await CreateMemberAsync("Forum website author");
        var reporter = await CreateMemberAsync("Forum website reporter");
        var postId = await CreatePostAsync(author, "Website report post");
        var reportPath = $"/forum/post/{postId}/report";

        using var anonymous = factory.CreateAnonymousClient(allowAutoRedirect: false);
        using var challenge = await anonymous.GetAsync(reportPath);
        Assert.Equal(HttpStatusCode.Redirect, challenge.StatusCode);
        Assert.Contains("/account/login", challenge.Headers.Location!.OriginalString, StringComparison.Ordinal);
        Assert.Contains(Uri.EscapeDataString(reportPath), challenge.Headers.Location.OriginalString, StringComparison.Ordinal);

        using var member = CreateMemberClient(reporter);
        var reportPage = await member.GetStringAsync(reportPath);
        Assert.Contains("Report forum post", reportPage, StringComparison.Ordinal);
        Assert.Contains("noindex, nofollow", reportPage, StringComparison.Ordinal);

        var returnUrl = $"/forum/topic/1002/ranking-every-studio-album/page/2#post-{postId}";
        using var submitted = await member.PostAsync(reportPath, new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = ExtractAntiforgeryToken(reportPage),
            ["Category"] = ForumPostReportCategories.Spam,
            ["Details"] = "Website details",
            ["ReturnUrl"] = returnUrl,
        }));
        Assert.Equal(HttpStatusCode.Redirect, submitted.StatusCode);
        Assert.Equal(returnUrl, submitted.Headers.Location!.OriginalString);

        var thread = await member.GetStringAsync(returnUrl);
        Assert.Contains("Report submitted", thread, StringComparison.Ordinal);

        using var blocked = await member.PostAsync(
            $"/forum/post/{postId}/block",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = ExtractAntiforgeryToken(thread),
            }));
        Assert.Equal(HttpStatusCode.Redirect, blocked.StatusCode);
        Assert.Equal(
            $"/forum/topic/1002/ranking-every-studio-album#post-{postId}",
            blocked.Headers.Location!.OriginalString);
        Assert.True(await factory.Services.GetRequiredService<PrivateMessageService>()
            .HasBlockedAsync(reporter.Id, author.Id));

        var blockedThread = await member.GetStringAsync(returnUrl);
        Assert.Contains("Member blocked", blockedThread, StringComparison.Ordinal);
        Assert.Contains("Post from a blocked member. Show this post", blockedThread, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AdminQueue_ShowsReportAndSupportsAuditedStatusChange()
    {
        var author = await CreateMemberAsync("Forum admin author");
        var reporter = await CreateMemberAsync("Forum admin reporter");
        var postId = await CreatePostAsync(author, "Admin queue evidence");
        var service = factory.Services.GetRequiredService<ForumPostReportService>();
        var created = await service.ReportAsync(reporter.Id, postId, ForumPostReportCategories.Threats, "Review this");

        using var admin = factory.CreateAdminClient(AdminEmail);
        var queue = await admin.GetStringAsync("/admin/forum-reports");
        Assert.Contains($"/admin/forum-reports/{created.ReportId}", queue, StringComparison.Ordinal);

        var detail = await admin.GetStringAsync($"/admin/forum-reports/{created.ReportId}");
        Assert.Contains("Admin queue evidence", detail, StringComparison.Ordinal);
        Assert.Contains("Review this", detail, StringComparison.Ordinal);
        Assert.Contains("Current post state</dt><dd>Visible", detail, StringComparison.Ordinal);
        Assert.Contains("Current member state</dt><dd>Active", detail, StringComparison.Ordinal);
        Assert.Contains($"<a href=\"/admin/members/{author.Id}\">Forum admin author</a>", detail, StringComparison.Ordinal);
        Assert.Contains($"<a href=\"/admin/members/{reporter.Id}\">Forum admin reporter</a>", detail, StringComparison.Ordinal);
        Assert.Contains("Open current post and moderation controls", detail, StringComparison.Ordinal);

        using var changed = await admin.PostAsync(
            $"/admin/forum-reports/{created.ReportId}/status",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = ExtractAntiforgeryToken(detail),
                ["status"] = PrivateMessageReportStatus.Actioned,
            }));
        Assert.Equal(HttpStatusCode.Redirect, changed.StatusCode);
        Assert.Equal(
            PrivateMessageReportStatus.Actioned,
            (await factory.Services.GetRequiredService<IForumPostReportRepository>().GetAsync(created.ReportId!.Value))!.Status);
    }

    private async Task<MemberAccount> CreateMemberAsync(string displayName)
    {
        var id = Guid.NewGuid();
        return await factory.Services.GetRequiredService<IMemberAccountRepository>().CreateAsync(new MemberAccount
        {
            Id = id,
            Email = $"{id:N}@example.test",
            NormalizedEmail = $"{id:N}@example.test".ToUpperInvariant(),
            DisplayName = displayName,
            CreatedAt = DateTime.UtcNow,
        });
    }

    private async Task<int> CreatePostAsync(MemberAccount author, string body) =>
        await factory.Services.GetRequiredService<IForumWriteRepository>().CreatePostAsync(
            new NewForumPost(1002, author.Id, author.DisplayName, body, DateTimeOffset.UtcNow));

    private HttpClient CreateBearerClient(MemberAccount member)
    {
        var issuer = factory.Services.GetRequiredService<MobileAuthTokenIssuer>();
        var token = issuer.IssueAccessToken(member.Id, member.Email, member.DisplayName);
        var client = factory.CreateAnonymousClient(allowAutoRedirect: false);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private HttpClient CreateMemberClient(MemberAccount member)
    {
        var client = factory.CreateAnonymousClient(allowAutoRedirect: false);
        client.DefaultRequestHeaders.Add(TestMemberAuthHandler.MemberIdHeader, member.Id.ToString());
        client.DefaultRequestHeaders.Add(TestMemberAuthHandler.DisplayNameHeader, member.DisplayName);
        return client;
    }

    private static string ExtractAntiforgeryToken(string html)
    {
        var match = AntiforgeryTokenRegex().Match(html);
        Assert.True(match.Success, "Antiforgery token was not found.");
        return match.Groups["token"].Value;
    }

    [GeneratedRegex("""name="__RequestVerificationToken"[^>]*value="(?<token>[^"]+)""", RegexOptions.IgnoreCase)]
    private static partial Regex AntiforgeryTokenRegex();
}
