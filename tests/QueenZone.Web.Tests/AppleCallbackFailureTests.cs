using System.Net;
using System.Text;
using System.Text.Json;
using AspNet.Security.OAuth.Apple;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace QueenZone.Web.Tests;

public sealed class AppleCallbackFailureTests
{
    [Fact]
    public async Task Direct_callback_visit_returns_retry_instructions()
    {
        using var backchannel = new StubBackchannel();
        using var server = CreateServer(backchannel);
        using var client = server.GetTestClient();

        using var response = await client.GetAsync("/signin-apple");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("/account/login", await response.Content.ReadAsStringAsync());
        Assert.False(response.Headers.Contains("Set-Cookie"));
        Assert.Equal(0, backchannel.Requests);
    }

    [Theory]
    [InlineData("GET", null)]
    [InlineData("GET", "")]
    [InlineData("GET", "untrusted-state")]
    [InlineData("POST", null)]
    [InlineData("POST", "")]
    [InlineData("POST", "untrusted-state")]
    public async Task Missing_or_invalid_state_returns_safe_bad_request(string method, string? state)
    {
        using var backchannel = new StubBackchannel();
        using var server = CreateServer(backchannel);
        using var client = server.GetTestClient();
        var values = new Dictionary<string, string> { ["code"] = "private-code", ["error_description"] = "private-error" };
        if (state is not null)
        {
            values["state"] = state;
        }

        using var request = CreateCallback(method, values);
        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("text/plain", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("utf-8", response.Content.Headers.ContentType?.CharSet);
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.Null(response.Headers.Location);
        Assert.False(response.Headers.Contains("Set-Cookie"));
        Assert.Equal(
            "Apple sign-in could not be completed. Please go to /account/login and try again.",
            await response.Content.ReadAsStringAsync());
        Assert.Equal(0, backchannel.Requests);
    }

    [Fact]
    public async Task Protected_state_without_correlation_still_fails()
    {
        using var backchannel = new StubBackchannel();
        using var server = CreateServer(backchannel);
        using var client = server.GetTestClient();
        var state = await StartSignInAsync(client);

        var exception = await Assert.ThrowsAsync<AuthenticationFailureException>(() =>
            client.GetAsync(QueryHelpers.AddQueryString("/signin-apple", "state", state.State)));

        Assert.Equal("Correlation failed.", exception.InnerException?.Message);
        Assert.Equal(0, backchannel.Requests);
    }

    [Theory]
    [InlineData("code-missing", "Code was not found.")]
    [InlineData("provider-error", "server_error")]
    [InlineData("provider-matching-message", "The OAuth state was missing or invalid.")]
    [InlineData("access-denied", "Access was denied")]
    [InlineData("token-error", "private-token-failure")]
    [InlineData("token-validation-error", "Unexpected token validation")]
    public async Task Valid_state_preserves_other_failure_handling(string scenario, string expectedFailure)
    {
        using var backchannel = new StubBackchannel(fail: scenario == "token-error");
        using var server = CreateServer(backchannel);
        using var client = server.GetTestClient();
        var signIn = await StartSignInAsync(client);
        var values = new Dictionary<string, string> { ["state"] = signIn.State };
        if (scenario == "provider-error")
        {
            values["error"] = "server_error";
        }
        if (scenario == "provider-matching-message")
        {
            values["error"] = "The OAuth state was missing or invalid.";
        }
        if (scenario == "access-denied")
        {
            values["error"] = "access_denied";
        }
        if (scenario is "token-error" or "token-validation-error")
        {
            values["code"] = "private-code";
        }
        using var request = CreateCallback("POST", values);
        request.Headers.Add("Cookie", signIn.Cookie);

        var exception = await Assert.ThrowsAsync<AuthenticationFailureException>(() => client.SendAsync(request));

        Assert.Contains(expectedFailure, exception.ToString());
        Assert.Equal(scenario is "token-error" or "token-validation-error" ? 1 : 0, backchannel.Requests);
    }

    [Fact]
    public async Task Valid_callback_still_validates_token_and_signs_in()
    {
        using var backchannel = new StubBackchannel();
        var tokenValidated = false;
        using var server = CreateServer(backchannel, context =>
        {
            Assert.StartsWith("eyJhbGciOiJub25lIn0.", context.IdToken);
            tokenValidated = true;
            return Task.CompletedTask;
        });
        using var client = server.GetTestClient();
        var signIn = await StartSignInAsync(client);
        using var request = CreateCallback("POST", new Dictionary<string, string>
        {
            ["state"] = signIn.State,
            ["code"] = "private-code",
            ["user"] = """{"name":{"firstName":"Test","lastName":"Member"}}""",
        });
        request.Headers.Add("Cookie", signIn.Cookie);

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/complete", response.Headers.Location?.OriginalString);
        Assert.True(tokenValidated);
        Assert.Equal(1, backchannel.Requests);
        var cookieName = server.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
            .Get(MemberAuthenticationSchemes.ExternalCookie).Cookie.Name;
        var cookie = Assert.Single(response.Headers.GetValues("Set-Cookie"), value =>
            value.StartsWith(cookieName + "=", StringComparison.Ordinal));
        using var complete = new HttpRequestMessage(HttpMethod.Get, "/complete");
        complete.Headers.Add("Cookie", cookie.Split(';')[0]);
        using var completed = await client.SendAsync(complete);
        Assert.Equal(HttpStatusCode.OK, completed.StatusCode);
        Assert.Equal("Test Member", await completed.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Matching_message_from_a_different_exception_or_with_properties_is_not_handled()
    {
        using var server = CreateServer(new StubBackchannel());
        var options = server.Services.GetRequiredService<IOptionsMonitor<AppleAuthenticationOptions>>()
            .Get(MemberAuthenticationSchemes.Apple);
        var scheme = new AuthenticationScheme(MemberAuthenticationSchemes.Apple, null, typeof(AppleAuthenticationHandler));
        foreach (var failure in new Exception[]
        {
            new InvalidOperationException("The OAuth state was missing or invalid."),
            new AuthenticationFailureException("The OAuth state was missing or invalid.", new Exception("inner")),
            new AuthenticationFailureException("Other failure"),
        })
        {
            var context = new RemoteFailureContext(new DefaultHttpContext(), scheme, options, failure);
            await options.Events.OnRemoteFailure(context);
            Assert.Null(context.Result);
        }

        var withProperties = new RemoteFailureContext(new DefaultHttpContext(), scheme, options,
            new AuthenticationFailureException("The OAuth state was missing or invalid."))
        {
            Properties = new AuthenticationProperties(),
        };
        await options.Events.OnRemoteFailure(withProperties);
        Assert.Null(withProperties.Result);
    }

    private static HttpRequestMessage CreateCallback(string method, Dictionary<string, string> values) =>
        method == "POST"
            ? new HttpRequestMessage(HttpMethod.Post, "/signin-apple") { Content = new FormUrlEncodedContent(values) }
            : new HttpRequestMessage(HttpMethod.Get, QueryHelpers.AddQueryString("/signin-apple", values!));

    private static async Task<(string State, string Cookie)> StartSignInAsync(HttpClient client)
    {
        using var response = await client.GetAsync("/start");
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("appleid.apple.com", response.Headers.Location?.Host);
        var query = QueryHelpers.ParseQuery(response.Headers.Location!.Query);
        Assert.Equal("form_post", query["response_mode"]);
        return (query["state"].ToString(), response.Headers.GetValues("Set-Cookie").Single().Split(';')[0]);
    }

    private static IHost CreateServer(StubBackchannel backchannel, Func<AppleValidateIdTokenContext, Task>? validate = null)
    {
        var host = new HostBuilder().ConfigureWebHost(builder => builder.UseTestServer()
            .UseEnvironment("Development")
            .ConfigureServices((context, services) =>
            {
                var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["AzureAd:ClientId"] = "",
                    ["Authentication:Apple:ClientId"] = "org.queenzone.web",
                    ["Authentication:Apple:TeamId"] = "TEAM123456",
                    ["Authentication:Apple:KeyId"] = "KEY1234567",
                    ["Authentication:Apple:PrivateKey"] = "test-private-key",
                }).Build();
                services.AddDataProtection().UseEphemeralDataProtectionProvider();
                services.AddQueenZoneAuth(configuration, context.HostingEnvironment);
                services.PostConfigure<AppleAuthenticationOptions>(MemberAuthenticationSchemes.Apple, options =>
                {
                    options.Backchannel = new HttpClient(backchannel);
                    // Keep the real callback pipeline; replace only external Apple services.
                    options.Events.OnGenerateClientSecret = context =>
                    {
                        context.Options.ClientSecret = "test-client-secret";
                        return Task.CompletedTask;
                    };
                    options.Events.OnValidateIdToken = validate ?? (_ => throw new InvalidOperationException("Unexpected token validation"));
                });
            })
            .Configure(app =>
            {
                app.UseAuthentication();
                app.Run(async context =>
                {
                    if (context.Request.Path == "/start")
                    {
                        await context.ChallengeAsync(MemberAuthenticationSchemes.Apple,
                            new AuthenticationProperties { RedirectUri = "/complete" });
                        return;
                    }
                    if (context.Request.Path == "/complete")
                    {
                        var result = await context.AuthenticateAsync(MemberAuthenticationSchemes.ExternalCookie);
                        Assert.True(result.Succeeded);
                        await context.Response.WriteAsync(result.Principal!.Identity!.Name!);
                        return;
                    }
                    context.Response.StatusCode = StatusCodes.Status404NotFound;
                });
            })).Build();
        host.Start();
        return host;
    }

    private sealed class StubBackchannel(bool fail = false) : HttpMessageHandler
    {
        public int Requests { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests++;
            Assert.Equal("appleid.apple.com", request.RequestUri?.Host);
            var idToken = "eyJhbGciOiJub25lIn0." +
                WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes("""{"sub":"test-subject","email":"member@example.test","email_verified":true}""")) + ".";
            return Task.FromResult(new HttpResponseMessage(fail ? HttpStatusCode.BadRequest : HttpStatusCode.OK)
            {
                Content = new StringContent(fail
                    ? """{"error":"private-token-failure"}"""
                    : JsonSerializer.Serialize(new { access_token = "test-access-token", token_type = "Bearer", id_token = idToken }),
                    Encoding.UTF8, "application/json"),
            });
        }
    }
}
