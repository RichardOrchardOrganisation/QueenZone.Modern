using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using QueenZone.Data;
using QueenZone.Web;

namespace QueenZone.Web.Tests;

public sealed partial class TriviaFactSubmissionRoutesTests : IClassFixture<ExternalCookieWebApplicationFactory>
{
    private const string AdminEmail = "admin@test.local";
    private readonly WebApplicationFactory<Program> factory;

    public TriviaFactSubmissionRoutesTests(ExternalCookieWebApplicationFactory factory)
    {
        this.factory = factory;
    }

    [Fact]
    public async Task Get_SubmitTrivia_RedirectsUnauthenticatedUsersToLogin()
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var response = await client.GetAsync("/submit/trivia");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/account/login", response.Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task Post_ValidSubmission_CreatesPendingRow_AndConfirmation()
    {
        const string factText = "Unique pending trivia fact about the Red Special.";
        var client = await CreateSignedInMemberClientAsync(
            email: "trivia-submit@example.com",
            displayName: "Trivia Fan",
            subject: "google-trivia-submit",
            options: new WebApplicationFactoryClientOptions
            {
                HandleCookies = true,
                AllowAutoRedirect = false,
            });

        var formPage = await client.GetStringAsync("/submit/trivia");
        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = ExtractAntiforgeryToken(formPage),
            ["Text"] = factText,
            ["Category"] = "Instruments",
            ["Difficulty"] = TriviaDifficulty.Medium,
            ["SourceNote"] = "Brian May interview",
        });

        var response = await client.PostAsync("/submit/trivia", content);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("/submit/trivia/confirmation/", response.Headers.Location!.OriginalString);

        var confirmation = await client.GetStringAsync(response.Headers.Location!.OriginalString);
        Assert.Contains("Your trivia fact is under review.", confirmation);
        Assert.Contains(factText, confirmation);

        var mySubmissions = await client.GetStringAsync("/account/my-submissions?tab=trivia");
        Assert.Contains(factText, mySubmissions);
        Assert.Contains(TriviaFactSubmissionStatus.Pending, mySubmissions);

        var repository = factory.Services.GetRequiredService<ITriviaFactSubmissionRepository>();
        var memberId = await GetMemberIdForEmailAsync("trivia-submit@example.com");
        var submissions = await repository.GetBySubmitterAsync(memberId);
        var submission = Assert.Single(submissions.Items);
        Assert.Equal(TriviaFactSubmissionStatus.Pending, submission.Status);
        Assert.Equal(factText, submission.Text);
        Assert.Equal("Instruments", submission.Category);
        Assert.Equal(TriviaDifficulty.Medium, submission.Difficulty);

        var trivia = factory.Services.GetRequiredService<ITriviaRepository>();
        var published = (await trivia.GetAllAsync()).Where(fact => fact.IsPublished);
        Assert.DoesNotContain(published, fact => fact.Text == factText);

        using var anonymous = factory.CreateClient();
        var randomJson = await anonymous.GetStringAsync($"{ContentApiEndpoints.RootPath}/trivia/random");
        Assert.DoesNotContain(factText, randomJson);
    }

    [Fact]
    public async Task Post_MissingText_ShowsValidationError()
    {
        var client = await CreateSignedInMemberClientAsync(
            email: "trivia-missing@example.com",
            displayName: "Missing Text",
            subject: "google-trivia-missing",
            options: new WebApplicationFactoryClientOptions
            {
                HandleCookies = true,
                AllowAutoRedirect = false,
            });

        var formPage = await client.GetStringAsync("/submit/trivia");
        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = ExtractAntiforgeryToken(formPage),
            ["Text"] = "   ",
        });

        var response = await client.PostAsync("/submit/trivia", content);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Fact text is required", body);
    }

    [Fact]
    public async Task Get_AdminQueue_RequiresAdminAuthentication()
    {
        var anonymous = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/admin/trivia-submissions")).StatusCode);

        var stranger = CreateAdminClient("stranger@example.com");
        Assert.Equal(HttpStatusCode.Forbidden, (await stranger.GetAsync("/admin/trivia-submissions")).StatusCode);

        var admin = CreateAdminClient(AdminEmail);
        var body = await admin.GetStringAsync("/admin/trivia-submissions");
        Assert.Contains("Trivia submissions", body);
    }

    [Fact]
    public async Task Admin_CanApproveWithEditedWording_AndRejectWithReason()
    {
        var memberClient = await CreateSignedInMemberClientAsync(
            email: "trivia-admin-flow@example.com",
            displayName: "Admin Flow Fan",
            subject: "google-trivia-admin-flow",
            options: new WebApplicationFactoryClientOptions
            {
                HandleCookies = true,
                AllowAutoRedirect = false,
            });

        var approveId = await SubmitTriviaAsync(
            memberClient,
            "Original wording about Live Aid that an admin will edit.");
        var rejectId = await SubmitTriviaAsync(
            memberClient,
            "Reject this unsourced trivia suggestion about a rumour.");

        var admin = CreateAdminClient(AdminEmail);

        var queue = await admin.GetStringAsync("/admin/trivia-submissions");
        Assert.Contains("Original wording about Live Aid", queue);
        Assert.Contains("Admin Flow Fan", queue);

        var detail = await admin.GetStringAsync($"/admin/trivia-submissions/{approveId}");
        Assert.Contains("Original wording about Live Aid", detail);
        Assert.Contains("Approve and publish", detail);
        Assert.Contains("__RequestVerificationToken", detail);

        const string editedText = "Queen played at Live Aid on 13 July 1985.";
        await PostAdminActionAsync(admin, approveId, "approve", new Dictionary<string, string>
        {
            ["text"] = editedText,
            ["category"] = "Concerts",
            ["difficulty"] = TriviaDifficulty.Easy,
            ["source"] = "BBC archive",
            ["reviewNotes"] = "light wording edit",
        }, detail);

        var rejectDetail = await admin.GetStringAsync($"/admin/trivia-submissions/{rejectId:D}");
        var rejectWithoutReason = await PostAdminActionAsync(
            admin,
            rejectId,
            "reject",
            new Dictionary<string, string>(),
            rejectDetail);
        Assert.Equal(HttpStatusCode.Redirect, rejectWithoutReason.StatusCode);

        await PostAdminActionAsync(admin, rejectId, "reject", new Dictionary<string, string>
        {
            ["rejectionReason"] = "Could not verify this claim.",
            ["reviewNotes"] = "keep this internal",
        }, rejectDetail);

        var submissions = factory.Services.GetRequiredService<ITriviaFactSubmissionRepository>();
        var approved = await submissions.GetByIdAsync(approveId);
        var rejected = await submissions.GetByIdAsync(rejectId);
        Assert.Equal(TriviaFactSubmissionStatus.Approved, approved!.Status);
        Assert.Equal(TriviaFactSubmissionStatus.Rejected, rejected!.Status);
        Assert.Equal("Could not verify this claim.", rejected.RejectionReason);
        Assert.Equal("keep this internal", rejected.ReviewNotes);
        Assert.NotNull(approved.PromotedTriviaId);

        var trivia = factory.Services.GetRequiredService<ITriviaRepository>();
        var published = await trivia.GetByIdAsync(approved.PromotedTriviaId!.Value);
        Assert.NotNull(published);
        Assert.True(published!.IsPublished);
        Assert.Equal(editedText, published.Text);
        Assert.Equal("Concerts", published.Category);

        var inMemory = Assert.IsType<InMemoryTriviaFactSubmissionRepository>(submissions);
        Assert.Contains(inMemory.GetAuditLogs(approveId), log => log.Action == TriviaFactSubmissionStatus.Approved);
        Assert.Contains(inMemory.GetAuditLogs(rejectId), log => log.Action == TriviaFactSubmissionStatus.Rejected);

        var memberHistory = await memberClient.GetStringAsync("/account/my-submissions?tab=trivia");
        Assert.Contains("Could not verify this claim.", memberHistory);
        Assert.DoesNotContain("keep this internal", memberHistory);
        Assert.DoesNotContain("light wording edit", memberHistory);
        Assert.Contains(editedText is var _ ? "Original wording about Live Aid" : string.Empty, memberHistory);
    }

    [Fact]
    public async Task Confirmation_ReturnsNotFound_ForOtherMembersSubmission()
    {
        var owner = await CreateSignedInMemberClientAsync(
            email: "trivia-owner@example.com",
            displayName: "Owner",
            subject: "google-trivia-owner",
            options: new WebApplicationFactoryClientOptions
            {
                HandleCookies = true,
                AllowAutoRedirect = false,
            });
        var id = await SubmitTriviaAsync(owner, "Private trivia suggestion for confirmation isolation.");

        var other = await CreateSignedInMemberClientAsync(
            email: "trivia-other@example.com",
            displayName: "Other",
            subject: "google-trivia-other",
            options: new WebApplicationFactoryClientOptions
            {
                HandleCookies = true,
                AllowAutoRedirect = false,
            });

        var response = await other.GetAsync($"/submit/trivia/confirmation/{id}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Post_InvalidDifficulty_ShowsValidationError()
    {
        var client = await CreateSignedInMemberClientAsync(
            email: "trivia-difficulty@example.com",
            displayName: "Bad Difficulty",
            subject: "google-trivia-difficulty",
            options: new WebApplicationFactoryClientOptions
            {
                HandleCookies = true,
                AllowAutoRedirect = false,
            });

        var formPage = await client.GetStringAsync("/submit/trivia");
        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = ExtractAntiforgeryToken(formPage),
            ["Text"] = "A fact with an invalid difficulty.",
            ["Difficulty"] = "expert",
        });

        var response = await client.PostAsync("/submit/trivia", content);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("easy, medium, or hard", body);
    }

    [Fact]
    public async Task Admin_Approve_InvalidDraft_ReturnsError()
    {
        var memberClient = await CreateSignedInMemberClientAsync(
            email: "trivia-invalid-approve@example.com",
            displayName: "Invalid Approve",
            subject: "google-trivia-invalid-approve",
            options: new WebApplicationFactoryClientOptions
            {
                HandleCookies = true,
                AllowAutoRedirect = false,
            });
        var id = await SubmitTriviaAsync(memberClient, "A valid member suggestion that admin will blank out.");

        var admin = CreateAdminClient(AdminEmail);
        var invalidDetail = await admin.GetStringAsync($"/admin/trivia-submissions/{id:D}");
        await PostAdminActionAsync(admin, id, "approve", new Dictionary<string, string>
        {
            ["text"] = "   ",
        }, invalidDetail);

        var detail = await admin.GetStringAsync($"/admin/trivia-submissions/{id}");
        Assert.Contains("Fact text is required", detail);

        var stored = await factory.Services.GetRequiredService<ITriviaFactSubmissionRepository>().GetByIdAsync(id);
        Assert.Equal(TriviaFactSubmissionStatus.Pending, stored!.Status);
    }

    [Fact]
    public async Task Admin_Approve_AlreadyReviewed_ReturnsError()
    {
        var memberClient = await CreateSignedInMemberClientAsync(
            email: "trivia-already-approved@example.com",
            displayName: "Already Approved",
            subject: "google-trivia-already-approved",
            options: new WebApplicationFactoryClientOptions
            {
                HandleCookies = true,
                AllowAutoRedirect = false,
            });
        var id = await SubmitTriviaAsync(memberClient, "A suggestion that will already be approved.");

        var admin = CreateAdminClient(AdminEmail);
        var firstDetail = await admin.GetStringAsync($"/admin/trivia-submissions/{id:D}");
        await PostAdminActionAsync(admin, id, "approve", new Dictionary<string, string>
        {
            ["text"] = "Published after first approve.",
        }, firstDetail);
        await PostAdminActionAsync(admin, id, "approve", new Dictionary<string, string>
        {
            ["text"] = "Should not publish again.",
        }, firstDetail);

        var detail = await admin.GetStringAsync($"/admin/trivia-submissions/{id}");
        Assert.Contains("Only pending suggestions can be approved", detail);
    }

    [Fact]
    public async Task Get_AdminDetail_ReturnsNotFound_ForUnknownId()
    {
        var admin = CreateAdminClient(AdminEmail);
        var response = await admin.GetAsync($"/admin/trivia-submissions/{Guid.NewGuid():D}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Post_AdminActions_ReturnNotFound_ForUnknownId()
    {
        var memberClient = await CreateSignedInMemberClientAsync(
            email: "trivia-admin-404@example.com",
            displayName: "404 Fan",
            subject: "google-trivia-admin-404",
            options: new WebApplicationFactoryClientOptions
            {
                HandleCookies = true,
                AllowAutoRedirect = false,
            });
        var existingId = await SubmitTriviaAsync(memberClient, "Used only to obtain an antiforgery token.");
        var admin = CreateAdminClient(AdminEmail);
        var detail = await admin.GetStringAsync($"/admin/trivia-submissions/{existingId:D}");
        var missing = Guid.NewGuid();

        var approveMissing = await PostAdminActionAsync(
            admin,
            missing,
            "approve",
            new Dictionary<string, string> { ["text"] = "Should not exist." },
            detail);
        var rejectMissing = await PostAdminActionAsync(
            admin,
            missing,
            "reject",
            new Dictionary<string, string> { ["rejectionReason"] = "Gone." },
            detail);

        Assert.Equal(HttpStatusCode.NotFound, approveMissing.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, rejectMissing.StatusCode);
    }

    [Fact]
    public async Task Get_Confirmation_RedirectsUnauthenticatedUsersToLogin()
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var response = await client.GetAsync($"/submit/trivia/confirmation/{Guid.NewGuid():D}");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/account/login", response.Headers.Location!.OriginalString);
    }

    private async Task<Guid> SubmitTriviaAsync(HttpClient client, string text)
    {
        var formPage = await client.GetStringAsync("/submit/trivia");
        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = ExtractAntiforgeryToken(formPage),
            ["Text"] = text,
            ["Category"] = "Band",
        });

        var response = await client.PostAsync("/submit/trivia", content);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var location = response.Headers.Location!.OriginalString;
        var idText = location.Split('/').Last();
        return Guid.Parse(idText);
    }

    private async Task<HttpResponseMessage> PostAdminActionAsync(
        HttpClient client,
        Guid id,
        string handler,
        Dictionary<string, string> fields,
        string? detailHtml = null)
    {
        var html = detailHtml ?? await client.GetStringAsync($"/admin/trivia-submissions/{id:D}");
        var form = new Dictionary<string, string>(fields)
        {
            ["__RequestVerificationToken"] = ExtractAntiforgeryToken(html),
        };
        return await client.PostAsync(
            $"/admin/trivia-submissions/{id:D}/{handler}",
            new FormUrlEncodedContent(form));
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

    private static string ExtractAntiforgeryToken(string html)
    {
        var match = AntiforgeryTokenRegex().Match(html);
        Assert.True(match.Success, "Antiforgery token was not found in the form.");
        return match.Groups["token"].Value;
    }

    [GeneratedRegex("""name="__RequestVerificationToken"[^>]*value="(?<token>[^"]+)""", RegexOptions.IgnoreCase)]
    private static partial Regex AntiforgeryTokenRegex();
}
