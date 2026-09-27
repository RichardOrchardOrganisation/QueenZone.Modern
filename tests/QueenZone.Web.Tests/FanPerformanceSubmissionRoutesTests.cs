using System.Net;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using QueenZone.Data;
using QueenZone.Storage;
using QueenZone.Web;
using QueenZone.Web.Pages.Submit;

namespace QueenZone.Web.Tests;

public sealed partial class FanPerformanceSubmissionRoutesTests : IClassFixture<InspectableBlobWebApplicationFactory>
{
    private readonly WebApplicationFactory<Program> factory;
    private readonly InMemoryBlobStorageBackend blobBackend;

    public FanPerformanceSubmissionRoutesTests(InspectableBlobWebApplicationFactory factory)
    {
        this.factory = factory;
        blobBackend = factory.BlobBackend;
    }

    [Fact]
    public async Task Get_SubmitFanPerformance_RedirectsUnauthenticatedUsersToLogin()
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var response = await client.GetAsync("/submit/fan-performance");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/account/login", response.Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task Post_ValidSubmission_CreatesPendingRow_AndConfirmation()
    {
        var client = await CreateSignedInMemberClientAsync(
            email: "fanperf-submit@example.com",
            displayName: "Stage Fan",
            subject: "google-fanperf-submit",
            options: new WebApplicationFactoryClientOptions
            {
                HandleCookies = true,
                AllowAutoRedirect = false,
            });

        var formPage = await client.GetStringAsync("/submit/fan-performance");
        Assert.Contains("audio/mpeg,audio/mp3,audio/flac,.mp3,.flac", formPage);
        Assert.Contains(FanPerformanceModel.RightsDeclarationCopy, formPage);

        var response = await PostSubmissionAsync(client, formPage, "Reaching Out cover", "Reaching Out", "Stage Fan");
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("/submit/fan-performance/confirmation/", response.Headers.Location!.OriginalString);

        var confirmation = await client.GetStringAsync(response.Headers.Location!.OriginalString);
        Assert.Contains("Your fan performance is under review.", confirmation);
        Assert.Contains("Reaching Out cover", confirmation);
        Assert.Contains("Reaching Out", confirmation);

        var mySubmissions = await client.GetStringAsync("/account/my-submissions?tab=performances");
        Assert.Contains("Reaching Out cover", mySubmissions);
        Assert.Contains(FanPerformanceSubmissionStatus.Pending, mySubmissions);

        var repository = factory.Services.GetRequiredService<IFanPerformanceSubmissionRepository>();
        var submissions = await repository.GetBySubmitterAsync(
            await GetMemberIdForEmailAsync("fanperf-submit@example.com"));
        var submission = Assert.Single(submissions.Items);
        Assert.Equal(FanPerformanceSubmissionStatus.Pending, submission.Status);
        Assert.Equal("Reaching Out cover", submission.Title);
        Assert.Equal("Reaching Out", submission.CoveredSong);
        Assert.Equal(FanPerformanceSubmissionRights.DeclarationVersion, submission.RightsDeclarationVersion);
        Assert.True(blobBackend.Exists(BlobUploadContainers.FanPerformances, submission.BlobPath));
        Assert.False(blobBackend.Exists(SongFileUrl.ContainerName, submission.BlobPath));
    }

    [Fact]
    public async Task Post_MissingRights_ShowsValidationError()
    {
        var client = await CreateSignedInMemberClientAsync(
            email: "fanperf-rights@example.com",
            displayName: "Rights Fan",
            subject: "google-fanperf-rights",
            options: new WebApplicationFactoryClientOptions
            {
                HandleCookies = true,
                AllowAutoRedirect = false,
            });

        var formPage = await client.GetStringAsync("/submit/fan-performance");
        using var content = new MultipartFormDataContent();
        content.Add(new StringContent(ExtractAntiforgeryToken(formPage)), "__RequestVerificationToken");
        content.Add(new StringContent("No rights"), "Title");
        content.Add(new StringContent("Song"), "CoveredSong");
        content.Add(new StringContent("Me"), "PerformedBy");
        var fileContent = new StreamContent(new MemoryStream(CreateMpegPayload(200)));
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("audio/mpeg");
        content.Add(fileContent, "AudioFile", "cover.mp3");

        var response = await client.PostAsync("/submit/fan-performance", content);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("own performance", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Post_MissingAudio_ShowsValidationError()
    {
        var client = await CreateSignedInMemberClientAsync(
            email: "fanperf-missing@example.com",
            displayName: "Missing Audio",
            subject: "google-fanperf-missing",
            options: new WebApplicationFactoryClientOptions
            {
                HandleCookies = true,
                AllowAutoRedirect = false,
            });

        var formPage = await client.GetStringAsync("/submit/fan-performance");
        using var content = new MultipartFormDataContent();
        content.Add(new StringContent(ExtractAntiforgeryToken(formPage)), "__RequestVerificationToken");
        content.Add(new StringContent("No file"), "Title");
        content.Add(new StringContent("Song"), "CoveredSong");
        content.Add(new StringContent("Me"), "PerformedBy");
        content.Add(new StringContent("true"), "RightsDeclarationAccepted");

        var response = await client.PostAsync("/submit/fan-performance", content);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Choose an audio file", body);
    }

    [Fact]
    public async Task Get_Confirmation_RedirectsUnauthenticatedUsersToLogin()
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var response = await client.GetAsync($"/submit/fan-performance/confirmation/{Guid.NewGuid():D}");
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/account/login", response.Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task Confirmation_ReturnsNotFound_ForOtherMembersSubmission()
    {
        var owner = await CreateSignedInMemberClientAsync(
            email: "fanperf-owner@example.com",
            displayName: "Owner",
            subject: "google-fanperf-owner",
            options: new WebApplicationFactoryClientOptions
            {
                HandleCookies = true,
                AllowAutoRedirect = false,
            });
        var formPage = await owner.GetStringAsync("/submit/fan-performance");
        var submit = await PostSubmissionAsync(owner, formPage, "Private cover", "Song", "Owner");
        var id = Guid.Parse(submit.Headers.Location!.OriginalString.Split('/').Last());

        var other = await CreateSignedInMemberClientAsync(
            email: "fanperf-other@example.com",
            displayName: "Other",
            subject: "google-fanperf-other",
            options: new WebApplicationFactoryClientOptions
            {
                HandleCookies = true,
                AllowAutoRedirect = false,
            });

        var response = await other.GetAsync($"/submit/fan-performance/confirmation/{id}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task MySubmissions_Withdraw_DeletesPendingBlob()
    {
        var client = await CreateSignedInMemberClientAsync(
            email: "fanperf-withdraw@example.com",
            displayName: "Withdraw Fan",
            subject: "google-fanperf-withdraw",
            options: new WebApplicationFactoryClientOptions
            {
                HandleCookies = true,
                AllowAutoRedirect = false,
            });

        var formPage = await client.GetStringAsync("/submit/fan-performance");
        var submit = await PostSubmissionAsync(client, formPage, "Withdraw target", "Song", "Withdraw Fan");
        var id = Guid.Parse(submit.Headers.Location!.OriginalString.Split('/').Last());

        var repository = factory.Services.GetRequiredService<IFanPerformanceSubmissionRepository>();
        var before = await repository.GetByIdAsync(id);
        Assert.NotNull(before);
        Assert.True(blobBackend.Exists(BlobUploadContainers.FanPerformances, before!.BlobPath));

        var listPage = await client.GetStringAsync("/account/my-submissions?tab=performances");
        Assert.Contains("Withdraw target", listPage);

        using var withdraw = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = ExtractAntiforgeryToken(listPage),
            ["id"] = id.ToString("D"),
        });
        var response = await client.PostAsync("/account/my-submissions?handler=WithdrawFanPerformance", withdraw);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);

        var after = await repository.GetByIdAsync(id);
        Assert.Equal(FanPerformanceSubmissionStatus.Withdrawn, after!.Status);
        Assert.False(blobBackend.Exists(BlobUploadContainers.FanPerformances, before.BlobPath));

        var withdrawnPage = await client.GetStringAsync("/account/my-submissions?tab=performances");
        Assert.Contains("Withdrawn", withdrawnPage);
    }

    [Fact]
    public async Task MySubmissions_NeedsInfoReply_MovesToUnderReview()
    {
        var client = await CreateSignedInMemberClientAsync(
            email: "fanperf-reply@example.com",
            displayName: "Reply Fan",
            subject: "google-fanperf-reply",
            options: new WebApplicationFactoryClientOptions
            {
                HandleCookies = true,
                AllowAutoRedirect = false,
            });

        var formPage = await client.GetStringAsync("/submit/fan-performance");
        var submit = await PostSubmissionAsync(client, formPage, "Reply target", "Song", "Reply Fan");
        var id = Guid.Parse(submit.Headers.Location!.OriginalString.Split('/').Last());

        var repository = factory.Services.GetRequiredService<IFanPerformanceSubmissionRepository>();
        await repository.UpdateStatusAsync(
            id,
            FanPerformanceSubmissionStatus.NeedsInfo,
            "admin@test.local",
            "Please name the Queen song",
            null);

        var listPage = await client.GetStringAsync("/account/my-submissions?tab=performances");
        Assert.Contains("Please name the Queen song", listPage);
        Assert.Contains("Send reply", listPage);

        using var reply = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = ExtractAntiforgeryToken(listPage),
            ["id"] = id.ToString("D"),
            ["reply"] = "It is Reaching Out.",
        });
        var response = await client.PostAsync("/account/my-submissions?handler=ReplyFanPerformance", reply);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);

        var updated = await repository.GetByIdAsync(id);
        Assert.Equal(FanPerformanceSubmissionStatus.UnderReview, updated!.Status);
        Assert.Equal("Please name the Queen song", updated.ReviewNotes);
    }

    [Fact]
    public async Task PublicFanPerformances_DoNotExposePendingSubmissionTitle()
    {
        var client = await CreateSignedInMemberClientAsync(
            email: "fanperf-public@example.com",
            displayName: "Public Check",
            subject: "google-fanperf-public",
            options: new WebApplicationFactoryClientOptions
            {
                HandleCookies = true,
                AllowAutoRedirect = false,
            });

        var formPage = await client.GetStringAsync("/submit/fan-performance");
        await PostSubmissionAsync(client, formPage, "Secret pending cover title", "Song", "Public Check");

        var anonymous = factory.CreateClient();
        var publicPage = await anonymous.GetStringAsync("/fan-performances");
        Assert.DoesNotContain("Secret pending cover title", publicPage);
    }

    private static async Task<HttpResponseMessage> PostSubmissionAsync(
        HttpClient client,
        string formPage,
        string title,
        string coveredSong,
        string performedBy)
    {
        using var content = new MultipartFormDataContent();
        content.Add(new StringContent(ExtractAntiforgeryToken(formPage)), "__RequestVerificationToken");
        content.Add(new StringContent(title), "Title");
        content.Add(new StringContent(coveredSong), "CoveredSong");
        content.Add(new StringContent(performedBy), "PerformedBy");
        content.Add(new StringContent("true"), "RightsDeclarationAccepted");
        var fileContent = new StreamContent(new MemoryStream(CreateMpegPayload(400)));
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("audio/mpeg");
        content.Add(fileContent, "AudioFile", "cover.mp3");
        return await client.PostAsync("/submit/fan-performance", content);
    }

    private async Task<HttpClient> CreateSignedInMemberClientAsync(
        string email,
        string displayName,
        string subject,
        WebApplicationFactoryClientOptions? options = null)
    {
        var client = factory.CreateClient(options ?? new WebApplicationFactoryClientOptions
        {
            HandleCookies = true,
            AllowAutoRedirect = true,
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

    private async Task<Guid> GetMemberIdForEmailAsync(string email)
    {
        var members = factory.Services.GetRequiredService<IMemberAccountRepository>();
        var account = await members.FindByEmailAsync(email);
        Assert.NotNull(account);
        return account!.Id;
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
