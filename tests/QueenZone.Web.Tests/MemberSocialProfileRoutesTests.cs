using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using QueenZone.Data;
using QueenZone.Data.Entities;
using QueenZone.Web;

namespace QueenZone.Web.Tests;

public sealed class MemberSocialProfileRoutesTests : IClassFixture<QueenZoneWebApplicationFactory>
{
    private readonly WebApplicationFactory<Program> factory;

    public MemberSocialProfileRoutesTests(QueenZoneWebApplicationFactory factory)
    {
        this.factory = factory;
    }

    [Fact]
    public async Task Profile_ShowsOnlyFilledChannels_ForGuestsAndOtherMembers()
    {
        var owner = await CreateMemberAsync("social-owner@example.com", "Social Owner");
        var viewer = await CreateMemberAsync("social-viewer@example.com", "Social Viewer");
        using (var scope = factory.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<IMemberAccountRepository>()
                .ReplaceSocialLinksAsync(owner.Id,
                [
                    new MemberSocialLink(MemberSocialChannel.X, "https://x.com/queen"),
                    new MemberSocialLink(MemberSocialChannel.YouTube, "https://www.youtube.com/@QueenOfficial"),
                ]);
        }

        var guest = factory.CreateClient();
        var guestHtml = await guest.GetStringAsync($"/members/{owner.Id}");
        Assert.Contains("rel=\"noopener noreferrer me\"", guestHtml);
        Assert.Contains("https://x.com/queen", guestHtml);
        Assert.Contains("https://www.youtube.com/@QueenOfficial", guestHtml);
        Assert.DoesNotContain("instagram.com", guestHtml);
        Assert.DoesNotContain("Save social profiles", guestHtml);
        Assert.DoesNotContain("name=\"SocialX\"", guestHtml);

        var viewerClient = CreateSignedInClient(viewer);
        var viewerHtml = await viewerClient.GetStringAsync($"/members/{owner.Id}");
        Assert.Contains("https://x.com/queen", viewerHtml);
        Assert.DoesNotContain("Save social profiles", viewerHtml);
        Assert.Contains(">Follow<", viewerHtml);
    }

    [Fact]
    public async Task Profile_OmitsSocialRow_WhenMemberHasNone()
    {
        var member = await CreateMemberAsync("social-empty@example.com", "Empty Socials");
        var html = await factory.CreateClient().GetStringAsync($"/members/{member.Id}");

        Assert.Contains("Empty Socials", html);
        Assert.DoesNotContain("qz-member-profile__socials", html);
        Assert.DoesNotContain("rel=\"noopener noreferrer me\"", html);
    }

    [Fact]
    public async Task Settings_CannotChangeAnotherMembersSocials()
    {
        var owner = await CreateMemberAsync("social-keep@example.com", "Keep Socials");
        var other = await CreateMemberAsync("social-other@example.com", "Other Socials");
        using (var scope = factory.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<IMemberAccountRepository>()
                .ReplaceSocialLinksAsync(owner.Id,
                [
                    new MemberSocialLink(MemberSocialChannel.X, "https://x.com/queen"),
                ]);
        }

        var otherClient = CreateSignedInClient(other);
        var settings = await otherClient.GetStringAsync("/account/settings");
        Assert.Contains("name=\"SocialX\"", settings);
        Assert.DoesNotContain("value=\"https://x.com/queen\"", settings);

        var response = await otherClient.PostAsync(
            "/account/settings?handler=UpdateSocialLinks",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = ExtractAntiforgeryToken(settings),
                ["SocialX"] = "intruder",
            }));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);

        using var verify = factory.Services.CreateScope();
        var members = verify.ServiceProvider.GetRequiredService<IMemberAccountRepository>();
        var ownerLinks = await members.ListSocialLinksAsync(owner.Id);
        var otherLinks = await members.ListSocialLinksAsync(other.Id);
        Assert.Equal(["https://x.com/queen"], ownerLinks.Select(link => link.Url));
        Assert.Equal(["https://x.com/intruder"], otherLinks.Select(link => link.Url));
    }

    private async Task<MemberAccount> CreateMemberAsync(string email, string displayName)
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IMemberAccountRepository>()
            .CreateAsync(new MemberAccount
            {
                Id = Guid.NewGuid(),
                Email = email,
                DisplayName = displayName,
                CreatedAt = DateTime.UtcNow,
            });
    }

    private HttpClient CreateSignedInClient(MemberAccount member)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            HandleCookies = true,
            AllowAutoRedirect = false,
        });
        client.DefaultRequestHeaders.Add(TestMemberAuthHandler.MemberIdHeader, member.Id.ToString());
        client.DefaultRequestHeaders.Add(TestMemberAuthHandler.DisplayNameHeader, member.DisplayName);
        client.DefaultRequestHeaders.Add(TestMemberAuthHandler.EmailHeader, member.Email);
        return client;
    }

    private static string ExtractAntiforgeryToken(string html)
    {
        var match = Regex.Match(
            html,
            """name="__RequestVerificationToken" value="(?<token>[^"]+)""",
            RegexOptions.IgnoreCase);
        Assert.True(match.Success, "Antiforgery token was not found in the form.");
        return match.Groups["token"].Value;
    }
}
