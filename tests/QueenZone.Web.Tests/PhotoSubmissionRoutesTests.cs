using System.Net;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using QueenZone.Data;
using QueenZone.Storage;
using QueenZone.Web;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace QueenZone.Web.Tests;

public sealed partial class PhotoSubmissionRoutesTests :
    IClassFixture<InspectableBlobWebApplicationFactory>,
    IAsyncLifetime
{
    private const string AdminEmail = "admin@test.local";
    private readonly WebApplicationFactory<Program> factory;
    private readonly InspectableBlobWebApplicationFactory resettableFactory;

    public PhotoSubmissionRoutesTests(InspectableBlobWebApplicationFactory factory)
    {
        this.factory = factory;
        resettableFactory = factory;
    }

    public Task InitializeAsync() => resettableFactory.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Get_SubmitPhoto_RedirectsUnauthenticatedUsersToLogin()
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var response = await client.GetAsync("/submit/photo");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/account/login", response.Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task Post_ValidSubmission_CreatesPendingRow_AndConfirmation()
    {
        var client = await CreateSignedInMemberClientAsync(
            email: "photo-submit@example.com",
            displayName: "Photo Fan",
            subject: "google-photo-submit",
            options: new WebApplicationFactoryClientOptions
            {
                HandleCookies = true,
                AllowAutoRedirect = false,
            });

        var formPage = await client.GetStringAsync("/submit/photo");
        await using var png = await CreatePngAsync();

        using var content = new MultipartFormDataContent();
        content.Add(new StringContent(ExtractAntiforgeryToken(formPage)), "__RequestVerificationToken");
        content.Add(new StringContent("Wembley crowd shot"), "Title");
        content.Add(new StringContent("Queen"), "SuggestedCategory");
        var fileContent = new StreamContent(png);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        content.Add(fileContent, "PhotoFile", "wembley.png");

        var response = await client.PostAsync("/submit/photo", content);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("/submit/photo/confirmation/", response.Headers.Location!.OriginalString);

        var confirmation = await client.GetStringAsync(response.Headers.Location!.OriginalString);
        Assert.Contains("Your photo is under review.", confirmation);
        Assert.Contains("Wembley crowd shot", confirmation);

        var mySubmissions = await client.GetStringAsync("/account/my-submissions");
        Assert.Contains("Wembley crowd shot", mySubmissions);
        Assert.Contains(PhotoSubmissionStatus.Pending, mySubmissions);

        var repository = factory.Services.GetRequiredService<IPhotoSubmissionRepository>();
        var submissions = await repository.GetBySubmitterAsync(
            await GetMemberIdForEmailAsync("photo-submit@example.com"));
        var submission = Assert.Single(submissions.Items);
        Assert.Equal(PhotoSubmissionStatus.Pending, submission.Status);
        Assert.Equal("Wembley crowd shot", submission.Title);
    }

    [Fact]
    public async Task Post_MissingPhoto_ShowsValidationError()
    {
        var client = await CreateSignedInMemberClientAsync(
            email: "photo-missing@example.com",
            displayName: "Missing Photo",
            subject: "google-photo-missing",
            options: new WebApplicationFactoryClientOptions
            {
                HandleCookies = true,
                AllowAutoRedirect = false,
            });

        var formPage = await client.GetStringAsync("/submit/photo");
        using var content = new MultipartFormDataContent();
        content.Add(new StringContent(ExtractAntiforgeryToken(formPage)), "__RequestVerificationToken");
        content.Add(new StringContent("No file"), "Title");

        var response = await client.PostAsync("/submit/photo", content);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Choose a photo", body);
    }

    [Fact]
    public async Task Get_AdminQueue_RequiresAdminAuthentication()
    {
        var anonymous = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/admin/photo-submissions")).StatusCode);

        var stranger = CreateAdminClient("stranger@example.com");
        Assert.Equal(HttpStatusCode.Forbidden, (await stranger.GetAsync("/admin/photo-submissions")).StatusCode);

        var admin = CreateAdminClient(AdminEmail);
        var body = await admin.GetStringAsync("/admin/photo-submissions");
        Assert.Contains("Photo submissions", body);
    }

    [Fact]
    public async Task Admin_CanApproveRejectAndRequestChanges()
    {
        var memberClient = await CreateSignedInMemberClientAsync(
            email: "photo-admin-flow@example.com",
            displayName: "Admin Flow Fan",
            subject: "google-photo-admin-flow",
            options: new WebApplicationFactoryClientOptions
            {
                HandleCookies = true,
                AllowAutoRedirect = false,
            });

        var approveId = await SubmitPhotoAsync(memberClient, "Approve target", "Queen");
        var rejectId = await SubmitPhotoAsync(memberClient, "Reject target", "Queen");
        var needsInfoId = await SubmitPhotoAsync(memberClient, "Needs info target", "Queen");
        var underReviewId = await SubmitPhotoAsync(memberClient, "Under review target", "Queen");

        var admin = CreateAdminClient(AdminEmail);

        var queue = await admin.GetStringAsync("/admin/photo-submissions");
        Assert.Contains("Approve target", queue);

        var detail = await admin.GetStringAsync($"/admin/photo-submissions/{approveId}");
        Assert.Contains("Approve target", detail);
        Assert.Contains("Admin Flow Fan", detail);

        await PostAdminActionAsync(admin, $"/admin/photo-submissions/{underReviewId}/underreview", new Dictionary<string, string>
        {
            ["reviewNotes"] = "Starting",
        });
        await PostAdminActionAsync(admin, $"/admin/photo-submissions/{approveId}/approve", new Dictionary<string, string>
        {
            ["approvedCategory"] = "Queen",
            ["reviewNotes"] = "Looks good",
        });
        await PostAdminActionAsync(admin, $"/admin/photo-submissions/{rejectId}/reject", new Dictionary<string, string>
        {
            ["rejectionReason"] = "Too blurry",
            ["reviewNotes"] = "internal",
        });
        await PostAdminActionAsync(admin, $"/admin/photo-submissions/{needsInfoId}/needsinfo", new Dictionary<string, string>
        {
            ["reviewNotes"] = "Please add year",
        });

        // Missing rejection reason returns to detail with error.
        var rejectWithoutReason = await PostAdminActionAsync(
            admin,
            $"/admin/photo-submissions/{needsInfoId}/reject",
            new Dictionary<string, string>());
        Assert.Equal(HttpStatusCode.Redirect, rejectWithoutReason.StatusCode);

        var repository = factory.Services.GetRequiredService<IPhotoSubmissionRepository>();
        Assert.Equal(PhotoSubmissionStatus.Approved, (await repository.GetByIdAsync(approveId))!.Status);
        Assert.Equal(PhotoSubmissionStatus.Rejected, (await repository.GetByIdAsync(rejectId))!.Status);
        Assert.Equal(PhotoSubmissionStatus.NeedsInfo, (await repository.GetByIdAsync(needsInfoId))!.Status);
        Assert.Equal(PhotoSubmissionStatus.UnderReview, (await repository.GetByIdAsync(underReviewId))!.Status);

        var memberHistory = await memberClient.GetStringAsync("/account/my-submissions");
        Assert.Contains("Too blurry", memberHistory);
        Assert.Contains("Please add year", memberHistory);
    }

    [Fact]
    public async Task Confirmation_ReturnsNotFound_ForOtherMembersSubmission()
    {
        var owner = await CreateSignedInMemberClientAsync(
            email: "photo-owner@example.com",
            displayName: "Owner",
            subject: "google-photo-owner",
            options: new WebApplicationFactoryClientOptions
            {
                HandleCookies = true,
                AllowAutoRedirect = false,
            });
        var id = await SubmitPhotoAsync(owner, "Private shot", "Queen");

        var other = await CreateSignedInMemberClientAsync(
            email: "photo-other@example.com",
            displayName: "Other",
            subject: "google-photo-other",
            options: new WebApplicationFactoryClientOptions
            {
                HandleCookies = true,
                AllowAutoRedirect = false,
            });

        var response = await other.GetAsync($"/submit/photo/confirmation/{id}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private async Task<Guid> SubmitPhotoAsync(HttpClient client, string title, string category)
    {
        var formPage = await client.GetStringAsync("/submit/photo");
        await using var png = await CreatePngAsync();
        using var content = new MultipartFormDataContent();
        content.Add(new StringContent(ExtractAntiforgeryToken(formPage)), "__RequestVerificationToken");
        content.Add(new StringContent(title), "Title");
        content.Add(new StringContent(category), "SuggestedCategory");
        var fileContent = new StreamContent(png);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        content.Add(fileContent, "PhotoFile", "shot.png");

        var response = await client.PostAsync("/submit/photo", content);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var location = response.Headers.Location!.OriginalString;
        var idText = location.Split('/').Last();
        return Guid.Parse(idText);
    }

    private async Task<HttpResponseMessage> PostAdminActionAsync(
        HttpClient client,
        string actionPath,
        Dictionary<string, string> fields)
    {
        var id = Guid.Parse(actionPath.Split('/')[3]);
        var detail = await client.GetStringAsync($"/admin/photo-submissions/{id}");
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

    private static async Task<MemoryStream> CreatePngAsync()
    {
        using var image = new Image<Rgba32>(80, 60, new Rgba32(200, 50, 50));
        var stream = new MemoryStream();
        await image.SaveAsPngAsync(stream);
        stream.Position = 0;
        return stream;
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
