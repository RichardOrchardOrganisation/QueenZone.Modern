using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using QueenZone.Data;

namespace QueenZone.Web.Tests;

/// <summary>
/// Covers the email/password sign-in fallback on /account/login. There is no registration
/// UI for this path: accounts only exist if provisioned out-of-band (e.g. via
/// QueenZone.Tools create-reviewer-account), so these tests seed accounts directly through
/// MemberAccountService, the same way that tool does.
/// </summary>
public sealed partial class PasswordSignInTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> factory;

    public PasswordSignInTests(WebApplicationFactory<Program> factory)
    {
        this.factory = factory.WithWebHostBuilder(builder => builder.UseEnvironment("Testing"));
    }

    [Fact]
    public async Task Get_WithLocalReturnUrl_PutsResolvedUrlInHiddenFieldNotFormAction()
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
        });

        const string returnUrl = "/messages/compose?to=42";
        var body = await client.GetStringAsync(
            $"/account/login?returnUrl={Uri.EscapeDataString(returnUrl)}");
        var formAction = ExtractPasswordFormAction(body);

        Assert.DoesNotContain("returnUrl=", formAction, StringComparison.Ordinal);
        Assert.Equal(returnUrl, ExtractHiddenReturnUrl(body));
        Assert.Contains(
            $"""<input type="hidden" name="returnUrl" value="{returnUrl}" />""",
            body,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Get_WithQuotedReturnUrl_HtmlEncodesHiddenField()
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
        });

        const string returnUrl = "/forum?q=\"><script>alert(1)</script>";
        var body = await client.GetStringAsync(
            $"/account/login?returnUrl={Uri.EscapeDataString(returnUrl)}");

        Assert.Equal("/forum?q=&quot;&gt;&lt;script&gt;alert(1)&lt;/script&gt;", ExtractHiddenReturnUrlRaw(body));
        Assert.DoesNotContain("<script>", body, StringComparison.Ordinal);
        Assert.DoesNotContain("returnUrl=", ExtractPasswordFormAction(body), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Post_WithValidCredentials_RedirectsToResolvedReturnUrl()
    {
        await SeedAccountAsync("return-url@example.com", "correct horse battery staple", "Return Fan");

        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            HandleCookies = true,
            AllowAutoRedirect = false,
        });

        const string returnUrl = "/forum";
        var loginPage = await client.GetStringAsync(
            $"/account/login?returnUrl={Uri.EscapeDataString(returnUrl)}");
        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = ExtractAntiforgeryToken(loginPage),
            ["returnUrl"] = ExtractHiddenReturnUrl(loginPage),
            ["Input.Email"] = "return-url@example.com",
            ["Input.Password"] = "correct horse battery staple",
        });

        var response = await client.PostAsync(ExtractPasswordFormAction(loginPage), content);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(returnUrl, response.Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task Post_WithValidCredentials_SignsInAndGrantsMemberAccess()
    {
        await SeedAccountAsync("reviewer@example.com", "correct horse battery staple", "App Reviewer");

        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            HandleCookies = true,
            AllowAutoRedirect = false,
        });

        var loginPage = await client.GetStringAsync("/account/login");
        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = ExtractAntiforgeryToken(loginPage),
            ["Input.Email"] = "reviewer@example.com",
            ["Input.Password"] = "correct horse battery staple",
        });

        var response = await client.PostAsync("/account/login", content);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.DoesNotContain("/account/login", response.Headers.Location!.OriginalString);

        var probeResponse = await client.GetAsync("/account/member-probe");
        Assert.Equal(HttpStatusCode.OK, probeResponse.StatusCode);
    }

    [Fact]
    public async Task Post_WithWrongPassword_DoesNotSignIn()
    {
        await SeedAccountAsync("wrongpass@example.com", "the-real-password-123", "Wrong Pass");

        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            HandleCookies = true,
            AllowAutoRedirect = false,
        });

        var loginPage = await client.GetStringAsync("/account/login");
        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = ExtractAntiforgeryToken(loginPage),
            ["Input.Email"] = "wrongpass@example.com",
            ["Input.Password"] = "not-the-password",
        });

        var response = await client.PostAsync("/account/login", content);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await client.GetAsync("/account/member-probe");
        Assert.Equal(HttpStatusCode.Redirect, body.StatusCode);
    }

    [Fact]
    public async Task Post_WithInvalidEmail_ShowsValidationErrorAndDoesNotSignIn()
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            HandleCookies = true,
            AllowAutoRedirect = false,
        });

        var loginPage = await client.GetStringAsync("/account/login");
        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = ExtractAntiforgeryToken(loginPage),
            ["Input.Email"] = "not-an-email",
            ["Input.Password"] = "",
        });

        var response = await client.PostAsync("/account/login", content);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Enter your email and password.", body);

        var probeResponse = await client.GetAsync("/account/member-probe");
        Assert.Equal(HttpStatusCode.Redirect, probeResponse.StatusCode);
    }

    [Fact]
    public async Task Post_WithUnknownEmail_DoesNotSignIn()
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            HandleCookies = true,
            AllowAutoRedirect = false,
        });

        var loginPage = await client.GetStringAsync("/account/login");
        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = ExtractAntiforgeryToken(loginPage),
            ["Input.Email"] = "nobody@example.com",
            ["Input.Password"] = "whatever-password",
        });

        var response = await client.PostAsync("/account/login", content);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var probeResponse = await client.GetAsync("/account/member-probe");
        Assert.Equal(HttpStatusCode.Redirect, probeResponse.StatusCode);
    }

    [Fact]
    public void LoginPage_HasNoRegistrationRoute()
    {
        // There is deliberately no self-service sign-up for the password path — only the
        // OnGet/OnPost handlers used by AccountPageModel-derived pages should exist under
        // /account. This guards against someone later adding a Register.cshtml that would
        // reopen the exact spam surface social login was chosen to avoid.
        var registerPagePath = Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..", "src", "QueenZone.Web", "Pages", "Account", "Register.cshtml");
        Assert.False(File.Exists(registerPagePath));
    }

    private async Task SeedAccountAsync(string email, string password, string displayName)
    {
        using var scope = factory.Services.CreateScope();
        var memberAccountService = scope.ServiceProvider.GetRequiredService<MemberAccountService>();
        var result = await memberAccountService.RegisterAsync(email, password, displayName);
        Assert.True(result.Succeeded, result.Error);
    }

    private static string ExtractAntiforgeryToken(string html)
    {
        var match = AntiforgeryTokenRegex().Match(html);
        Assert.True(match.Success, "Antiforgery token was not found in the form.");
        return match.Groups["token"].Value;
    }

    private static string ExtractPasswordFormAction(string html)
    {
        var match = PasswordFormActionRegex().Match(html);
        Assert.True(match.Success, "Password sign-in form action was not found.");
        return System.Net.WebUtility.HtmlDecode(match.Groups["action"].Value);
    }

    private static string ExtractHiddenReturnUrl(string html) =>
        System.Net.WebUtility.HtmlDecode(ExtractHiddenReturnUrlRaw(html));

    private static string ExtractHiddenReturnUrlRaw(string html)
    {
        var match = HiddenReturnUrlRegex().Match(html);
        Assert.True(match.Success, "Hidden returnUrl field was not found.");
        return match.Groups["value"].Value;
    }

    [GeneratedRegex("""name="__RequestVerificationToken"[^>]*value="(?<token>[^"]+)""", RegexOptions.IgnoreCase)]
    private static partial Regex AntiforgeryTokenRegex();

    [GeneratedRegex("""<form[^>]*action="(?<action>[^"]+)"[^>]*>""", RegexOptions.IgnoreCase)]
    private static partial Regex PasswordFormActionRegex();

    [GeneratedRegex("""<input[^>]*name="returnUrl"[^>]*value="(?<value>[^"]*)"[^>]*/?>""", RegexOptions.IgnoreCase)]
    private static partial Regex HiddenReturnUrlRegex();
}
