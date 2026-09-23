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

public sealed class HelpRoutesTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> factory;

    public HelpRoutesTests(WebApplicationFactory<Program> factory)
    {
        this.factory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureTestServices(services =>
            {
                services
                    .AddAuthentication()
                    .AddScheme<AuthenticationSchemeOptions, ExternalCookieTestHandler>(
                        MemberAuthenticationSchemes.ExternalCookie, _ => { });
            });
        });
    }

    [Fact]
    public async Task Get_Contact_IsPublicAndAsksForContactDetails()
    {
        var client = factory.CreateClient();

        var body = await client.GetStringAsync("/contact");

        Assert.Contains("Contact us", body);
        Assert.Contains("Your name", body);
        Assert.Contains("Email address", body);
        Assert.Contains("Your message", body);
        Assert.Contains(">Contact<", body);
        Assert.DoesNotContain("style=", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Get_LegacyHelpPath_RedirectsToContact()
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var response = await client.GetAsync("/help");

        Assert.Equal(HttpStatusCode.MovedPermanently, response.StatusCode);
        Assert.Equal("/contact", response.Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task FooterAndPrivacy_LinkToContact()
    {
        var client = factory.CreateClient();

        var home = await client.GetStringAsync("/");
        Assert.Contains("href=\"/contact\"", home);
        Assert.Contains(">Contact<", home);

        var privacy = await client.GetStringAsync("/privacy");
        Assert.Contains("If you send a message from the", privacy);
        Assert.Contains("href=\"/contact\"", privacy);
    }

    [Fact]
    public async Task Post_AnonymousValidRequest_StoresRowAndRedirects()
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true,
        });

        var formPage = await client.GetStringAsync("/contact");
        using var content = new FormUrlEncodedContent(ValidGuestFields(formPage));

        var response = await client.PostAsync("/contact", content);
        var responseBody = await response.Content.ReadAsStringAsync();

        Assert.True(
            response.StatusCode == HttpStatusCode.Redirect,
            $"Expected redirect, got {response.StatusCode}. Body: {responseBody[..Math.Min(responseBody.Length, 800)]}");
        Assert.Equal("/contact/confirmation", response.Headers.Location!.OriginalString);

        var confirmation = await client.GetStringAsync("/contact/confirmation");
        Assert.Contains("we have your message", confirmation, StringComparison.OrdinalIgnoreCase);

        var repository = factory.Services.GetRequiredService<IHelpRequestRepository>();
        var list = await repository.ListAsync(HelpRequestStatus.Open, 1, 20);
        var stored = Assert.Single(list.Items, item => item.Subject == "Cannot open a forum topic");
        Assert.Equal("Alex Fan", stored.Name);
        Assert.Equal("alex-help@example.com", stored.Email);
        Assert.Null(stored.MemberId);
    }

    [Fact]
    public async Task Post_AnonymousMissingNameAndEmail_ShowsValidationErrors()
    {
        var client = factory.CreateClient();
        var formPage = await client.GetStringAsync("/contact");
        var fields = ValidGuestFields(formPage);
        fields["Name"] = "";
        fields["Email"] = "";

        var response = await client.PostAsync("/contact", new FormUrlEncodedContent(fields));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Name is required", body);
        Assert.Contains("Email address is required", body);
        Assert.DoesNotContain("style=", body, StringComparison.OrdinalIgnoreCase);
        var stored = await factory.Services.GetRequiredService<IHelpRequestRepository>()
            .ListAsync("all", 1, 50);
        Assert.DoesNotContain(stored.Items, item => item.Subject == "Cannot open a forum topic" && item.Name == "Alex Fan" && item.Email == "");
    }

    [Fact]
    public async Task Post_Honeypot_RedirectsWithoutStoring()
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var formPage = await client.GetStringAsync("/contact");
        var fields = ValidGuestFields(formPage, "Honeypot spam subject");
        fields["Website"] = "https://spam.example";

        var response = await client.PostAsync("/contact", new FormUrlEncodedContent(fields));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/contact/confirmation", response.Headers.Location!.OriginalString);
        var list = await factory.Services.GetRequiredService<IHelpRequestRepository>()
            .ListAsync("all", 1, 50);
        Assert.DoesNotContain(list.Items, item => item.Subject == "Honeypot spam subject");
    }

    [Fact]
    public async Task Post_MissingAntiforgery_Returns400()
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var fields = new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = "not-a-real-token",
            ["FormStamp"] = "not-a-real-stamp",
            ["Topic"] = HelpRequestTopic.Technical,
            ["Subject"] = "Cannot open a forum topic",
            ["Message"] = "The topic page returns an error when I click the latest thread.",
            ["Name"] = "Alex Fan",
            ["Email"] = "alex-help@example.com",
        };

        var response = await client.PostAsync("/contact", new FormUrlEncodedContent(fields));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Post_SignedInMember_OmitsContactFieldsAndStoresMemberId()
    {
        var client = await CreateSignedInMemberClientAsync(
            "help-member@example.com",
            "Help Member",
            "google-help-member");

        var formPage = await client.GetStringAsync("/contact");
        Assert.DoesNotContain("Your name", formPage);
        Assert.DoesNotContain("Email address", formPage);
        Assert.Contains("Signed in as", formPage);

        var fields = new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = ExtractField(formPage, "__RequestVerificationToken"),
            ["FormStamp"] = ExtractField(formPage, "FormStamp"),
            ["Topic"] = HelpRequestTopic.Account,
            ["Subject"] = "Please restore my display name",
            ["Message"] = "I changed my display name by mistake and would like the previous one back.",
        };

        var response = await client.PostAsync("/contact", new FormUrlEncodedContent(fields));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);

        var members = factory.Services.GetRequiredService<IMemberAccountRepository>();
        var account = await members.FindByEmailAsync("help-member@example.com");
        Assert.NotNull(account);

        var stored = await factory.Services.GetRequiredService<IHelpRequestRepository>()
            .ListAsync(HelpRequestStatus.Open, 1, 20);
        var item = Assert.Single(stored.Items, row => row.Subject == "Please restore my display name");
        Assert.Equal(account!.Id, item.MemberId);
        Assert.Equal("Help Member", item.Name);
        Assert.Equal("help-member@example.com", item.Email);
    }

    [Fact]
    public async Task Post_SignedInMember_Receives429AfterMemberCap()
    {
        using var limited = QueenZoneWebApplicationFactory.WithServices(services =>
        {
            services.PostConfigure<HelpRequestOptions>(options =>
            {
                options.MaxPerMemberPerMinute = 1;
                options.MaxAnonymousPerIpPerHour = 10;
            });
            services.AddAuthentication()
                .AddScheme<AuthenticationSchemeOptions, ExternalCookieTestHandler>(
                    MemberAuthenticationSchemes.ExternalCookie, _ => { });
        });
        using var client = limited.CreateAnonymousClient(allowAutoRedirect: false);
        client.DefaultRequestHeaders.Add(ExternalCookieTestHandler.ProviderHeader, "Google");
        client.DefaultRequestHeaders.Add(ExternalCookieTestHandler.SubjectHeader, $"help-limit-{Guid.NewGuid():N}");
        client.DefaultRequestHeaders.Add(ExternalCookieTestHandler.EmailHeader, $"help-limit-{Guid.NewGuid():N}@example.com");
        client.DefaultRequestHeaders.Add(ExternalCookieTestHandler.NameHeader, "Help Limit Member");
        using var callback = await client.GetAsync("/account/external-login-callback");
        Assert.True(callback.StatusCode is HttpStatusCode.OK or HttpStatusCode.Redirect);

        var page = await client.GetStringAsync("/contact");
        var fields = new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = ExtractField(page, "__RequestVerificationToken"),
            ["FormStamp"] = ExtractField(page, "FormStamp"),
            ["Topic"] = HelpRequestTopic.Account,
            ["Subject"] = "Signed-in contact request",
            ["Message"] = "I need some help from the site administrator please.",
        };
        using var first = await client.PostAsync("/contact", new FormUrlEncodedContent(fields));
        using var second = await client.PostAsync("/contact", new FormUrlEncodedContent(fields));

        Assert.Equal(HttpStatusCode.Redirect, first.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, second.StatusCode);
    }

    private static Dictionary<string, string> ValidGuestFields(string formHtml, string subject = "Cannot open a forum topic") =>
        new()
        {
            ["__RequestVerificationToken"] = ExtractField(formHtml, "__RequestVerificationToken"),
            ["FormStamp"] = ExtractField(formHtml, "FormStamp"),
            ["Topic"] = HelpRequestTopic.Technical,
            ["Subject"] = subject,
            ["Message"] = "The topic page returns an error when I click the latest thread.",
            ["Name"] = "Alex Fan",
            ["Email"] = "alex-help@example.com",
            ["Website"] = "",
        };

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

    private static string ExtractField(string html, string name)
    {
        var named = new Regex(
            $"""name="{Regex.Escape(name)}"[^>]*value="(?<value>[^"]*)"|value="(?<value>[^"]*)"[^>]*name="{Regex.Escape(name)}" """,
            RegexOptions.IgnoreCase);
        var match = named.Match(html);
        Assert.True(match.Success, $"Field {name} was not found in the form.");
        return match.Groups["value"].Value;
    }
}
