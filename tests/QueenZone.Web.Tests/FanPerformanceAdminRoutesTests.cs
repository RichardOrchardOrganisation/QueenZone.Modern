using System.Net;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using QueenZone.Data;
using QueenZone.Routing;
using QueenZone.Storage;
using QueenZone.Web;
using QueenZone.Web.Pages.Admin.FanPerformanceSubmissions;

namespace QueenZone.Web.Tests;

public sealed partial class FanPerformanceAdminRoutesTests :
    IClassFixture<InspectableBlobWebApplicationFactory>,
    IAsyncLifetime
{
    private const string AdminEmail = "admin@test.local";
    private readonly WebApplicationFactory<Program> factory;
    private readonly InspectableBlobWebApplicationFactory resettableFactory;

    public FanPerformanceAdminRoutesTests(InspectableBlobWebApplicationFactory factory)
    {
        this.factory = factory;
        resettableFactory = factory;
    }

    public Task InitializeAsync() => resettableFactory.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Get_AdminQueue_RequiresAdminAuthentication()
    {
        var anonymous = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await anonymous.GetAsync("/admin/fan-performance-submissions")).StatusCode);

        var stranger = CreateAdminClient("stranger@example.com");
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await stranger.GetAsync("/admin/fan-performance-submissions")).StatusCode);

        var admin = CreateAdminClient(AdminEmail);
        var body = await admin.GetStringAsync("/admin/fan-performance-submissions");
        Assert.Contains("Fan performance submissions", body);
    }

    [Fact]
    public async Task Admin_CanPreviewAudio_WithoutPublicOrCdnUrl()
    {
        var memberClient = await CreateSignedInMemberClientAsync(
            "fanperf-audio@example.com",
            "Audio Fan",
            "google-fanperf-audio");
        var id = await SubmitAsync(memberClient, "Audio preview target", "Reaching Out", "Audio Fan");

        var anonymous = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await anonymous.GetAsync($"/admin/fan-performance-submissions/{id}/audio")).StatusCode);

        var stranger = CreateAdminClient("stranger@example.com");
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await stranger.GetAsync($"/admin/fan-performance-submissions/{id}/audio")).StatusCode);

        var admin = CreateAdminClient(AdminEmail);
        var detail = await admin.GetStringAsync($"/admin/fan-performance-submissions/{id}");
        Assert.Contains($"/admin/fan-performance-submissions/{id}/audio", detail);
        Assert.DoesNotContain("cdn.queenzone", detail, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("blob.core.windows.net", detail, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("/fan-performances/", detail);

        using var audio = await admin.GetAsync($"/admin/fan-performance-submissions/{id}/audio");
        Assert.Equal(HttpStatusCode.OK, audio.StatusCode);
        Assert.Equal("audio/mpeg", audio.Content.Headers.ContentType?.MediaType);
        Assert.Contains("bytes", audio.Headers.AcceptRanges.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Admin_CanApproveRejectRequestChanges_AndWithdrawnIsReadOnly()
    {
        var memberClient = await CreateSignedInMemberClientAsync(
            "fanperf-admin-flow@example.com",
            "Admin Flow Fan",
            "google-fanperf-admin-flow");

        var approveId = await SubmitAsync(memberClient, "Approve target", "Reaching Out", "Admin Flow Fan");
        var rejectId = await SubmitAsync(memberClient, "Reject target", "Song", "Admin Flow Fan");
        var needsInfoId = await SubmitAsync(memberClient, "Needs info target", "Song", "Admin Flow Fan");
        var underReviewId = await SubmitAsync(memberClient, "Under review target", "Song", "Admin Flow Fan");
        var withdrawId = await SubmitAsync(memberClient, "Withdraw target", "Song", "Admin Flow Fan");

        var repository = factory.Services.GetRequiredService<IFanPerformanceSubmissionRepository>();
        await repository.UpdateStatusAsync(
            withdrawId,
            FanPerformanceSubmissionStatus.Withdrawn,
            string.Empty,
            null,
            null);

        var admin = CreateAdminClient(AdminEmail);
        var queue = await admin.GetStringAsync("/admin/fan-performance-submissions");
        Assert.Contains("Approve target", queue);
        Assert.DoesNotContain("Withdraw target", queue);

        var withdrawnDetail = await admin.GetStringAsync($"/admin/fan-performance-submissions/{withdrawId}");
        Assert.Contains("withdrawn by the member", withdrawnDetail);
        Assert.DoesNotContain(">Approve<", withdrawnDetail);
        Assert.DoesNotContain(">Reject<", withdrawnDetail);

        await PostAdminActionAsync(admin, $"/admin/fan-performance-submissions/{underReviewId}/underreview", new Dictionary<string, string>
        {
            ["reviewNotes"] = "Starting",
        });
        await PostAdminActionAsync(admin, $"/admin/fan-performance-submissions/{approveId}/approve", new Dictionary<string, string>
        {
            ["title"] = "Published title",
            ["performedBy"] = "Published performer",
            ["description"] = "Published notes",
            ["coveredSong"] = "Reaching Out",
            ["reviewNotes"] = "Looks good",
        });
        await PostAdminActionAsync(admin, $"/admin/fan-performance-submissions/{rejectId}/reject", new Dictionary<string, string>
        {
            ["rejectionReason"] = "Not a Queen cover",
            ["reviewNotes"] = "internal",
        });
        await PostAdminActionAsync(admin, $"/admin/fan-performance-submissions/{needsInfoId}/needsinfo", new Dictionary<string, string>
        {
            ["reviewNotes"] = "Please name the song",
        });

        var rejectWithoutReason = await PostAdminActionAsync(
            admin,
            $"/admin/fan-performance-submissions/{needsInfoId}/reject",
            new Dictionary<string, string>());
        Assert.Equal(HttpStatusCode.Redirect, rejectWithoutReason.StatusCode);

        var approved = await repository.GetByIdAsync(approveId);
        Assert.Equal(FanPerformanceSubmissionStatus.Approved, approved!.Status);
        Assert.NotNull(approved.PromotedStageId);
        Assert.Equal("Published title", approved.Title);
        Assert.Contains(
            await repository.GetAuditLogsAsync(approveId),
            log => log.Action == FanPerformanceSubmissionStatus.Approved && log.ActorEmail == AdminEmail);

        Assert.Equal(FanPerformanceSubmissionStatus.Rejected, (await repository.GetByIdAsync(rejectId))!.Status);
        Assert.Equal(FanPerformanceSubmissionStatus.NeedsInfo, (await repository.GetByIdAsync(needsInfoId))!.Status);
        Assert.Equal(FanPerformanceSubmissionStatus.UnderReview, (await repository.GetByIdAsync(underReviewId))!.Status);

        var memberHistory = await memberClient.GetStringAsync("/account/my-submissions?tab=performances");
        Assert.Contains("Not a Queen cover", memberHistory);
        Assert.Contains("Please name the song", memberHistory);
        Assert.DoesNotContain("internal", memberHistory);
        Assert.Contains(FanPerformanceRoutes.GetPublicPath(approved.PromotedStageId!.Value), memberHistory);
        Assert.Contains("View live performance", memberHistory);

        var publicPage = await factory.CreateClient().GetStringAsync("/fan-performances");
        Assert.Contains("Published title", publicPage);
        Assert.DoesNotContain("cdn.queenzone", publicPage, StringComparison.OrdinalIgnoreCase);
    }

    private async Task<Guid> SubmitAsync(HttpClient client, string title, string coveredSong, string performedBy)
    {
        var formPage = await client.GetStringAsync("/submit/fan-performance");
        using var content = new MultipartFormDataContent();
        content.Add(new StringContent(ExtractAntiforgeryToken(formPage)), "__RequestVerificationToken");
        content.Add(new StringContent(title), "Title");
        content.Add(new StringContent(coveredSong), "CoveredSong");
        content.Add(new StringContent(performedBy), "PerformedBy");
        content.Add(new StringContent("true"), "RightsDeclarationAccepted");
        var fileContent = new StreamContent(new MemoryStream(CreateMpegPayload(400)));
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("audio/mpeg");
        content.Add(fileContent, "AudioFile", "cover.mp3");

        var response = await client.PostAsync("/submit/fan-performance", content);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        return Guid.Parse(response.Headers.Location!.OriginalString.Split('/').Last());
    }

    private async Task<HttpResponseMessage> PostAdminActionAsync(
        HttpClient client,
        string actionPath,
        Dictionary<string, string> fields)
    {
        var id = Guid.Parse(actionPath.Split('/')[3]);
        var detail = await client.GetStringAsync($"/admin/fan-performance-submissions/{id}");
        var form = new Dictionary<string, string>(fields)
        {
            ["__RequestVerificationToken"] = ExtractAntiforgeryToken(detail),
        };
        return await client.PostAsync(actionPath, new FormUrlEncodedContent(form));
    }

    private HttpClient CreateAdminClient(string? email = null)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        if (!string.IsNullOrWhiteSpace(email))
        {
            client.DefaultRequestHeaders.Add(TestAuthHandler.UserEmailHeader, email);
        }

        return client;
    }

    private async Task<HttpClient> CreateSignedInMemberClientAsync(string email, string displayName, string subject)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            HandleCookies = true,
            AllowAutoRedirect = false,
        });
        client.DefaultRequestHeaders.Add(ExternalCookieTestHandler.ProviderHeader, "Google");
        client.DefaultRequestHeaders.Add(ExternalCookieTestHandler.SubjectHeader, subject);
        client.DefaultRequestHeaders.Add(ExternalCookieTestHandler.EmailHeader, email);
        client.DefaultRequestHeaders.Add(ExternalCookieTestHandler.NameHeader, displayName);

        var callbackResponse = await client.GetAsync("/account/external-login-callback");
        Assert.True(
            callbackResponse.StatusCode is HttpStatusCode.OK or HttpStatusCode.Redirect,
            $"Unexpected callback status code: {callbackResponse.StatusCode}");
        return client;
    }

    private static byte[] CreateMpegPayload(int length)
    {
        var bytes = new byte[Math.Max(length, 4)];
        Mp3DurationTests.CreateMpeg1Layer3Header(9).CopyTo(bytes.AsSpan());
        return bytes;
    }

    private static string ExtractAntiforgeryToken(string html)
    {
        var match = AntiforgeryTokenRegex().Match(html);
        Assert.True(match.Success, "Antiforgery token was not found in the form.");
        return match.Groups["token"].Value;
    }

    [GeneratedRegex("""name="__RequestVerificationToken" value="(?<token>[^"]+)""", RegexOptions.IgnoreCase)]
    private static partial Regex AntiforgeryTokenRegex();
}

public sealed class FanPerformanceAdminIndexFormattingTests
{
    [Theory]
    [InlineData(null, "—")]
    [InlineData(-1, "—")]
    [InlineData(0, "0:00")]
    [InlineData(75, "1:15")]
    [InlineData(125, "2:05")]
    public void FormatDuration_RendersMinutesAndSeconds(int? seconds, string expected)
    {
        Assert.Equal(expected, IndexModel.FormatDuration(seconds));
    }
}
