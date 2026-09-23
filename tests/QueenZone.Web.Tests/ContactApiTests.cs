using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using QueenZone.Data;
using QueenZone.Data.Entities;
using QueenZone.Web;

namespace QueenZone.Web.Tests;

public sealed class ContactApiTests : IClassFixture<QueenZoneWebApplicationFactory>
{
    private readonly QueenZoneWebApplicationFactory factory;

    public ContactApiTests(QueenZoneWebApplicationFactory factory)
    {
        this.factory = factory;
    }

    [Fact]
    public async Task Get_ContactForm_IsPublicAndMatchesWebsiteCopy()
    {
        using var client = factory.CreateAnonymousClient();

        using var response = await client.GetAsync(ContactApiEndpoints.Path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("no-store", response.Headers.CacheControl?.ToString(), StringComparison.OrdinalIgnoreCase);

        var form = await response.Content.ReadFromJsonAsync<ContactFormDto>();
        Assert.NotNull(form);
        Assert.False(form!.SignedIn);
        Assert.True(form.RequiresContactDetails);
        Assert.False(string.IsNullOrWhiteSpace(form.FormStamp));
        Assert.Equal(ContactCopy.Intro, form.Intro);
        Assert.Equal(ContactCopy.ConfirmationTitle, form.ConfirmationTitle);
        Assert.Equal(ContactCopy.ConfirmationMessage, form.ConfirmationMessage);
        Assert.Equal(HelpRequestTopic.All.Count, form.Topics.Count);
        Assert.Contains(form.Topics, topic => topic.Value == HelpRequestTopic.Technical && topic.Label == "Technical problem");
        Assert.Equal(HelpRequestService.MinMessageLength, form.Limits.MinMessageLength);
    }

    [Fact]
    public async Task Post_AnonymousValidRequest_StoresRowAndReturnsWebsiteConfirmation()
    {
        using var client = factory.CreateAnonymousClient();
        var stamp = await ReadFormStampAsync(client);
        const string subject = "API cannot open a forum topic";

        using var response = await client.PostAsJsonAsync(
            ContactApiEndpoints.Path,
            GuestBody(stamp, subject));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<ContactSubmitResponse>();
        Assert.NotNull(payload);
        Assert.True(payload!.Submitted);
        Assert.Equal(ContactCopy.ConfirmationTitle, payload.ConfirmationTitle);
        Assert.Equal(ContactCopy.ConfirmationMessage, payload.ConfirmationMessage);

        var stored = await FindOpenBySubjectAsync(subject);
        Assert.Equal("Alex Fan", stored.Name);
        Assert.Equal("alex-contact@example.com", stored.Email);
        Assert.Null(stored.MemberId);

        var admin = factory.CreateAdminClient();
        var inbox = await admin.GetStringAsync("/admin/help");
        Assert.Contains(subject, inbox);
        Assert.Contains($"/admin/help/{stored.Id}", inbox);
    }

    [Fact]
    public async Task Post_DoesNotRequireAntiforgery()
    {
        using var client = factory.CreateAnonymousClient(allowAutoRedirect: false);
        var stamp = await ReadFormStampAsync(client);

        using var response = await client.PostAsJsonAsync(
            ContactApiEndpoints.Path,
            GuestBody(stamp, "Contact without an antiforgery cookie"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Post_AnonymousMissingNameAndEmail_ReturnsProblemDetails()
    {
        using var client = factory.CreateAnonymousClient();
        var stamp = await ReadFormStampAsync(client);

        using var response = await client.PostAsJsonAsync(
            ContactApiEndpoints.Path,
            new
            {
                topic = HelpRequestTopic.Technical,
                subject = "Cannot open a forum topic",
                message = "The topic page returns an error when I click the latest thread.",
                name = "",
                email = "",
                formStamp = stamp,
            });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(StatusCodes.Status400BadRequest, problem.GetProperty("status").GetInt32());
        Assert.Contains("Name", problem.GetProperty("detail").GetString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Post_Honeypot_ReturnsConfirmationWithoutStoring()
    {
        using var client = factory.CreateAnonymousClient();
        var stamp = await ReadFormStampAsync(client);
        const string subject = "Honeypot API spam subject";

        using var response = await client.PostAsJsonAsync(
            ContactApiEndpoints.Path,
            new
            {
                topic = HelpRequestTopic.Other,
                subject,
                message = "This is definitely a long enough spam advertisement body.",
                name = "Bot",
                email = "bot-contact@example.com",
                website = "https://spam.example",
                formStamp = stamp,
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<ContactSubmitResponse>();
        Assert.True(payload!.Submitted);

        var list = await factory.Services.GetRequiredService<IHelpRequestRepository>()
            .ListAsync("all", 1, 50);
        Assert.DoesNotContain(list.Items, item => item.Subject == subject);
    }

    [Fact]
    public async Task Post_AnonymousRateLimit_ReturnsProblemDetails()
    {
        using var limited = QueenZoneWebApplicationFactory.WithServices(services =>
        {
            services.PostConfigure<HelpRequestOptions>(options => options.MaxAnonymousPerIpPerHour = 1);
        });
        using var client = limited.CreateAnonymousClient();
        var stamp = await ReadFormStampAsync(client);

        using var first = await client.PostAsJsonAsync(
            ContactApiEndpoints.Path,
            GuestBody(stamp, "First allowed contact from this IP"));
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        using var second = await client.PostAsJsonAsync(
            ContactApiEndpoints.Path,
            GuestBody(await ReadFormStampAsync(client), "Second contact should be rate limited"));

        Assert.Equal(HttpStatusCode.TooManyRequests, second.StatusCode);
        Assert.Equal("application/problem+json", second.Content.Headers.ContentType?.MediaType);
        var problem = await second.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(StatusCodes.Status429TooManyRequests, problem.GetProperty("status").GetInt32());
        Assert.Contains("Too many", problem.GetProperty("detail").GetString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Post_InvalidBearer_ReturnsUnauthorized()
    {
        using var client = factory.CreateAnonymousClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "not-a-real-token");

        using var response = await client.PostAsJsonAsync(
            ContactApiEndpoints.Path,
            GuestBody("ignored", "Should not store this contact request"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Post_SignedInMemberJwt_OmitsContactFieldsAndStoresMemberId()
    {
        using var isolated = QueenZoneWebApplicationFactory.WithServices(_ => { });
        var members = isolated.Services.GetRequiredService<IMemberAccountRepository>();
        var member = await members.CreateAsync(new MemberAccount
        {
            Id = Guid.NewGuid(),
            Email = "contact-member@example.com",
            DisplayName = "Contact Member",
            CreatedAt = DateTime.UtcNow,
        });
        var token = isolated.Services.GetRequiredService<MobileAuthTokenIssuer>()
            .IssueAccessToken(member.Id, member.Email, member.DisplayName);

        using var client = isolated.CreateAnonymousClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var formResponse = await client.GetAsync(ContactApiEndpoints.Path);
        var form = await formResponse.Content.ReadFromJsonAsync<ContactFormDto>();
        Assert.True(form!.SignedIn);
        Assert.False(form.RequiresContactDetails);
        Assert.Equal("Contact Member", form.SignedInDisplayName);

        const string subject = "Please restore my contact display name";
        using var response = await client.PostAsJsonAsync(
            ContactApiEndpoints.Path,
            new
            {
                topic = HelpRequestTopic.Account,
                subject,
                message = "I changed my display name by mistake and would like the previous one back.",
                formStamp = form.FormStamp,
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var list = await isolated.Services.GetRequiredService<IHelpRequestRepository>()
            .ListAsync(HelpRequestStatus.Open, 1, 20);
        var stored = Assert.Single(list.Items, item => item.Subject == subject);
        Assert.Equal(member.Id, stored.MemberId);
        Assert.Equal("Contact Member", stored.Name);
        Assert.Equal("contact-member@example.com", stored.Email);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Post_SignedInMember_IsRateLimitedForCookieAndBearer(bool useBearer)
    {
        using var limited = QueenZoneWebApplicationFactory.WithServices(services =>
        {
            services.PostConfigure<HelpRequestOptions>(options =>
            {
                options.MaxPerMemberPerMinute = 1;
                options.MaxAnonymousPerIpPerHour = 10;
            });
        });
        var member = await limited.Services.GetRequiredService<IMemberAccountRepository>()
            .CreateAsync(new MemberAccount
            {
                Id = Guid.NewGuid(),
                Email = $"contact-limit-{Guid.NewGuid():N}@example.com",
                DisplayName = "Contact Limit Member",
                CreatedAt = DateTime.UtcNow,
            });
        using var client = limited.CreateAnonymousClient();
        if (useBearer)
        {
            var token = limited.Services.GetRequiredService<MobileAuthTokenIssuer>()
                .IssueAccessToken(member.Id, member.Email, member.DisplayName);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }
        else
        {
            client.DefaultRequestHeaders.Add(TestMemberAuthHandler.MemberIdHeader, member.Id.ToString());
            client.DefaultRequestHeaders.Add(TestMemberAuthHandler.DisplayNameHeader, member.DisplayName);
            client.DefaultRequestHeaders.Add(TestMemberAuthHandler.EmailHeader, member.Email);
        }

        using var form = await client.GetAsync(ContactApiEndpoints.Path);
        var stamp = (await form.Content.ReadFromJsonAsync<ContactFormDto>())!.FormStamp;
        var request = new
        {
            topic = HelpRequestTopic.Account,
            subject = "First signed-in contact request",
            message = "This is a valid message to the site administrator.",
            formStamp = stamp,
        };
        using var first = await client.PostAsJsonAsync(ContactApiEndpoints.Path, request);
        using var second = await client.PostAsJsonAsync(ContactApiEndpoints.Path, request);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, second.StatusCode);
        var list = await limited.Services.GetRequiredService<IHelpRequestRepository>()
            .ListAsync(HelpRequestStatus.Open, 1, 20);
        Assert.Single(list.Items, item => item.MemberId == member.Id);
    }

    [Fact]
    public async Task OpenApi_IncludesContactRoutes()
    {
        using var client = factory.CreateAnonymousClient();
        using var response = await client.GetAsync(ApiV1.OpenApiPath);
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        var paths = payload.GetProperty("paths");
        Assert.True(paths.TryGetProperty("/api/v1/contact", out _));
    }

    [Fact]
    public async Task WebsiteConfirmation_UsesTheSameMessageAsTheApi()
    {
        using var client = factory.CreateAnonymousClient();
        var body = await client.GetStringAsync("/contact/confirmation");
        Assert.Contains("we have your message", body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("reply by email if a response is needed", body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(">Contact<", body);
    }

    private async Task<string> ReadFormStampAsync(HttpClient client)
    {
        using var response = await client.GetAsync(ContactApiEndpoints.Path);
        response.EnsureSuccessStatusCode();
        var form = await response.Content.ReadFromJsonAsync<ContactFormDto>();
        Assert.False(string.IsNullOrWhiteSpace(form?.FormStamp));
        return form!.FormStamp;
    }

    private async Task<HelpRequestListItem> FindOpenBySubjectAsync(string subject)
    {
        var list = await factory.Services.GetRequiredService<IHelpRequestRepository>()
            .ListAsync(HelpRequestStatus.Open, 1, 50);
        return Assert.Single(list.Items, item => item.Subject == subject);
    }

    private static object GuestBody(string formStamp, string subject) =>
        new
        {
            topic = HelpRequestTopic.Technical,
            subject,
            message = "The topic page returns an error when I click the latest thread.",
            name = "Alex Fan",
            email = "alex-contact@example.com",
            website = "",
            formStamp,
        };
}
