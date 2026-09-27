using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using QueenZone.Data;
using QueenZone.Data.Entities;
using QueenZone.Web;

namespace QueenZone.Web.Tests;

/// <summary>
/// Confirms rate-limited sends surface as an ordinary inline form error (same shape as a
/// blocked send), not a special-cased response, per the "clear but non-revealing" requirement.
/// </summary>
public sealed class PrivateMessageRateLimitRouteTests : IClassFixture<WebHostVariantCache>
{
    private readonly VariantWebApplicationFactory factory;

    public PrivateMessageRateLimitRouteTests(WebHostVariantCache variants)
    {
        factory = variants.Get(WebHostVariants.TestingPrivateMessageRateLimit1);
    }

    [Fact]
    public async Task Compose_ShowsGenericRateLimitError_WhenVolumeLimitExceeded()
    {
        var (aliceClient, alice) = await CreateMemberAsync(factory, "pm-limit-alice@example.com", "Limit Alice");
        var (_, bob) = await CreateMemberAsync(factory, "pm-limit-bob@example.com", "Limit Bob");

        var composePage = await aliceClient.GetStringAsync($"/messages/compose?to={bob.Id}");
        var firstSend = await aliceClient.PostAsync("/messages/compose", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = ExtractAntiforgeryToken(composePage),
            ["Input.RecipientMemberId"] = bob.Id.ToString(),
            ["Input.Body"] = "First message",
        }));
        Assert.Equal(HttpStatusCode.Redirect, firstSend.StatusCode);

        var secondComposePage = await aliceClient.GetStringAsync($"/messages/compose?to={bob.Id}");
        var secondSend = await aliceClient.PostAsync("/messages/compose", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = ExtractAntiforgeryToken(secondComposePage),
            ["Input.RecipientMemberId"] = bob.Id.ToString(),
            ["Input.Body"] = "Second message",
        }));

        Assert.Equal(HttpStatusCode.OK, secondSend.StatusCode);
        var body = await secondSend.Content.ReadAsStringAsync();
        Assert.Contains("sending messages too quickly", body, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<(HttpClient Client, MemberAccount Member)> CreateMemberAsync(
        WebApplicationFactory<Program> factory,
        string email,
        string displayName)
    {
        var members = factory.Services.GetRequiredService<IMemberAccountRepository>();
        var member = await members.CreateAsync(new MemberAccount
        {
            Id = Guid.NewGuid(),
            Email = email,
            DisplayName = displayName,
            CreatedAt = DateTime.UtcNow,
        });

        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            HandleCookies = true,
            AllowAutoRedirect = false,
        });
        client.DefaultRequestHeaders.Add(TestMemberAuthHandler.MemberIdHeader, member.Id.ToString());
        client.DefaultRequestHeaders.Add(TestMemberAuthHandler.DisplayNameHeader, member.DisplayName);
        client.DefaultRequestHeaders.Add(TestMemberAuthHandler.EmailHeader, member.Email);
        return (client, member);
    }

    private static string ExtractAntiforgeryToken(string html)
    {
        var match = System.Text.RegularExpressions.Regex.Match(
            html,
            """(?:name=["']__RequestVerificationToken["'][^>]*value=["'](?<token>[^"']+)["'])|(?:value=["'](?<token>[^"']+)["'][^>]*name=["']__RequestVerificationToken["'])""",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        Assert.True(match.Success, "Antiforgery token was not found in the form.");
        return match.Groups["token"].Value;
    }
}
