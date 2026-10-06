using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using QueenZone.Web;

namespace QueenZone.Web.Tests;

public sealed class AppearancePageTests(QueenZoneWebApplicationFactory factory) : IClassFixture<QueenZoneWebApplicationFactory>
{
    [Theory]
    [InlineData("System", "system")]
    [InlineData("Light", "light")]
    [InlineData("Dark", "dark")]
    public async Task Guest_choice_persists_securely_and_updates_the_header(string choice, string attribute)
    {
        using var client = Client();
        var token = (await client.GetFromJsonAsync<TokenResponse>("/appearance?handler=Token"))!.Token;
        using var request = new HttpRequestMessage(HttpMethod.Post, "/appearance")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["DeviceTheme"] = choice,
                ["ReturnUrl"] = "/news",
            }),
        };
        request.Headers.Add("RequestVerificationToken", token);
        var saved = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Redirect, saved.StatusCode);
        Assert.Equal("/news", saved.Headers.Location!.OriginalString);
        Assert.Contains(saved.Headers.GetValues("Set-Cookie"), value =>
            value.StartsWith("qz_theme=" + attribute, StringComparison.Ordinal) && value.Contains("; secure"));
        var home = await client.GetAsync("/");
        var html = await home.Content.ReadAsStringAsync();
        Assert.Contains($"data-theme=\"{attribute}\"", html);
        Assert.True(home.Headers.CacheControl?.Private);
        Assert.True(home.Headers.CacheControl?.NoStore);
    }

    [Theory]
    [InlineData("99")]
    [InlineData("invalid")]
    public async Task Invalid_choice_does_not_set_a_cookie(string choice)
    {
        using var client = Client();
        var token = (await client.GetFromJsonAsync<TokenResponse>("/appearance?handler=Token"))!.Token;
        client.DefaultRequestHeaders.Add("RequestVerificationToken", token);
        var response = await client.PostAsync("/appearance", new FormUrlEncodedContent(new Dictionary<string, string> { ["DeviceTheme"] = choice }));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.DoesNotContain(response.Headers.TryGetValues("Set-Cookie", out var values) ? values : [], cookie => cookie.StartsWith("qz_theme="));
    }

    [Fact]
    public async Task Public_header_does_not_show_an_appearance_control()
    {
        using var client = Client();
        var html = await client.GetStringAsync("/");
        Assert.Empty(TestHtmlAssertions.Select(html, "[data-theme-picker]"));
        Assert.Empty(TestHtmlAssertions.Select(html, "[data-theme-fallback]"));
        Assert.Empty(TestHtmlAssertions.Select(html, "[data-theme-status]"));
    }

    [Fact]
    public async Task Post_requires_antiforgery_and_public_header_does_not_mint_a_token()
    {
        using var client = Client();
        var home = await client.GetAsync("/");
        Assert.False(home.Headers.Contains("Set-Cookie"));
        var rejected = await client.PostAsync("/appearance", new FormUrlEncodedContent(new Dictionary<string, string> { ["DeviceTheme"] = "Dark" }));
        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        var prepared = await client.GetAsync("/appearance?handler=Token");
        Assert.True(prepared.Headers.CacheControl?.NoStore);
        Assert.Contains("Appearance", await client.GetStringAsync("/appearance"));
    }

    [Fact]
    public async Task Account_default_clears_override_and_rejects_external_return_url()
    {
        using var client = Client();
        var token = (await client.GetFromJsonAsync<TokenResponse>("/appearance?handler=Token"))!.Token;
        client.DefaultRequestHeaders.Add("RequestVerificationToken", token);
        var response = await client.PostAsync("/appearance", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["DeviceTheme"] = "Account",
            ["ReturnUrl"] = "//external.example",
        }));
        Assert.Equal("/", response.Headers.Location!.OriginalString);
        Assert.Contains(response.Headers.GetValues("Set-Cookie"), cookie => cookie.StartsWith("qz_theme=;") && cookie.Contains("expires="));
        Assert.DoesNotContain("data-theme=", await client.GetStringAsync("/"));
    }

    private HttpClient Client() => factory.CreateClient(new WebApplicationFactoryClientOptions
    {
        BaseAddress = new Uri("https://localhost"),
        AllowAutoRedirect = false,
        HandleCookies = true,
    });

    private sealed record TokenResponse(string Token);
}
