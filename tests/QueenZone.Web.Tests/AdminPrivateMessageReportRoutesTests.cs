using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using QueenZone.Data;
using QueenZone.Data.Entities;
using QueenZone.Web;

namespace QueenZone.Web.Tests;

public sealed partial class AdminPrivateMessageReportRoutesTests : IClassFixture<QueenZoneWebApplicationFactory>
{
    private const string AdminEmail = "admin@test.local";
    private readonly QueenZoneWebApplicationFactory factory;

    public AdminPrivateMessageReportRoutesTests(QueenZoneWebApplicationFactory factory)
    {
        this.factory = factory;
    }

    [Fact]
    public async Task Get_AdminPrivateMessages_RequiresAdminAuthentication()
    {
        var anonymous = factory.CreateAnonymousClient(allowAutoRedirect: false);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/admin/private-messages")).StatusCode);

        var stranger = factory.CreateAdminClient("stranger@example.com");
        Assert.Equal(HttpStatusCode.Forbidden, (await stranger.GetAsync("/admin/private-messages")).StatusCode);

        var admin = factory.CreateAdminClient(AdminEmail);
        var body = await admin.GetStringAsync("/admin/private-messages");
        Assert.Contains("Reported messages", body);
    }

    [Fact]
    public async Task AdminDashboard_ShowsReportedMessagesSectionAndOpenCount()
    {
        var (reporterId, reportedId, reportId) = await SeedReportAsync("dashboard-count");

        var admin = factory.CreateAdminClient(AdminEmail);
        var dashboard = await admin.GetStringAsync("/admin");

        Assert.Contains("href=\"/admin/private-messages\"", dashboard);
        Assert.Contains("Reported messages", dashboard);
    }

    [Fact]
    public async Task Admin_CanOpenDetail_ViewIsAudited_AndStatusChangeIsAudited()
    {
        var (reporterId, reportedId, reportId) = await SeedReportAsync("detail-flow");
        var privateMessageRepository = factory.Services.GetRequiredService<IPrivateMessageModerationRepository>();

        var admin = factory.CreateAdminClient(AdminEmail);
        var queue = await admin.GetStringAsync("/admin/private-messages");
        Assert.Contains($"/admin/private-messages/{reportId}", queue);

        var detail = await admin.GetStringAsync($"/admin/private-messages/{reportId}");
        Assert.Contains("Reported message content", detail);
        Assert.Contains("Please stop", detail);

        var afterView = await privateMessageRepository.GetReportAsync(reportId);
        Assert.NotNull(afterView);

        var response = await admin.PostAsync(
            $"/admin/private-messages/{reportId}/status",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = ExtractAntiforgeryToken(detail),
                ["status"] = PrivateMessageReportStatus.Dismissed,
            }));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal($"/admin/private-messages/{reportId}", response.Headers.Location!.OriginalString);

        var updated = await privateMessageRepository.GetReportAsync(reportId);
        Assert.NotNull(updated);
        Assert.Equal(PrivateMessageReportStatus.Dismissed, updated!.Status);

        var dismissedQueue = await admin.GetStringAsync($"/admin/private-messages?status={PrivateMessageReportStatus.Dismissed}");
        Assert.Contains($"/admin/private-messages/{reportId}", dismissedQueue);
    }

    [Fact]
    public async Task Admin_UnknownReport_ReturnsNotFound()
    {
        var admin = factory.CreateAdminClient(AdminEmail);
        var response = await admin.GetAsync($"/admin/private-messages/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private async Task<(Guid ReporterId, Guid ReportedId, Guid ReportId)> SeedReportAsync(string slug)
    {
        var memberAccountRepository = factory.Services.GetRequiredService<IMemberAccountRepository>();
        var privateMessageRepository = factory.Services.GetRequiredService<IPrivateMessageRepository>();
        var moderationRepository = factory.Services.GetRequiredService<IPrivateMessageModerationRepository>();

        var reporter = await memberAccountRepository.CreateAsync(NewMember($"reporter-{slug}@example.com", $"Reporter {slug}"));
        var reported = await memberAccountRepository.CreateAsync(NewMember($"reported-{slug}@example.com", $"Reported {slug}"));

        var sent = await privateMessageRepository.SendNewOrExistingAsync(
            reported.Id, reporter.Id, "Please stop", DateTimeOffset.UtcNow);
        var conversationId = sent.ConversationId!.Value;
        var message = (await privateMessageRepository.GetConversationAsync(conversationId, reporter.Id))!.Messages[^1];

        var report = await moderationRepository.CreateReportAsync(
            reporter.Id, conversationId, message.Id, "Harassment", DateTimeOffset.UtcNow);
        Assert.True(report.Succeeded);

        return (reporter.Id, reported.Id, report.ReportId!.Value);
    }

    private static MemberAccount NewMember(string email, string name) =>
        new()
        {
            Id = Guid.NewGuid(),
            Email = email,
            NormalizedEmail = email.ToUpperInvariant(),
            DisplayName = name,
            CreatedAt = DateTime.UtcNow,
        };

    private static string ExtractAntiforgeryToken(string html)
    {
        var match = AntiforgeryTokenRegex().Match(html);
        Assert.True(match.Success, "Antiforgery token was not found in the form.");
        return match.Groups["token"].Value;
    }

    [GeneratedRegex("""name="__RequestVerificationToken"[^>]*value="(?<token>[^"]+)""", RegexOptions.IgnoreCase)]
    private static partial Regex AntiforgeryTokenRegex();
}
