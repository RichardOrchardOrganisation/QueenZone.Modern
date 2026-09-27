using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using QueenZone.Data;
using QueenZone.Data.Entities;

namespace QueenZone.Web.Tests;

public sealed partial class MutationRateLimitRouteTests : IClassFixture<WebHostVariantCache>
{
    private readonly VariantWebApplicationFactory anonymous1;
    private readonly VariantWebApplicationFactory member1Ip10;
    private readonly VariantWebApplicationFactory member10Ip1;

    public MutationRateLimitRouteTests(WebHostVariantCache variants)
    {
        anonymous1 = variants.Get(WebHostVariants.TestingMutationRateLimitAnonymous1);
        member1Ip10 = variants.Get(WebHostVariants.TestingMutationRateLimitMember1Ip10);
        member10Ip1 = variants.Get(WebHostVariants.TestingMutationRateLimitMember10Ip1);
    }

    [Fact]
    public async Task Anonymous_api_write_rejects_before_handler_and_safe_get_does_not_count()
    {
        using var client = anonymous1.CreateAnonymousClient(allowAutoRedirect: false);
        client.DefaultRequestHeaders.Add("X-Forwarded-For", "203.0.113.10");

        using var form = await client.GetAsync(ContactApiEndpoints.Path);
        using var first = await client.PostAsJsonAsync(ContactApiEndpoints.Path, new { });
        using var second = await client.PostAsJsonAsync(ContactApiEndpoints.Path, new { });

        Assert.Equal(HttpStatusCode.OK, form.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, first.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, second.StatusCode);
        Assert.Equal("application/problem+json", second.Content.Headers.ContentType?.MediaType);
        Assert.True(second.Headers.RetryAfter?.Delta > TimeSpan.Zero);
    }

    [Fact]
    public async Task Authenticated_members_have_independent_allowances_behind_one_client_ip()
    {
        var firstMember = await SeedMemberAsync(member1Ip10, "member-one@example.com", "Member One");
        var secondMember = await SeedMemberAsync(member1Ip10, "member-two@example.com", "Member Two");
        using var firstClient = CreateBearerClient(member1Ip10, firstMember, "203.0.113.10");
        using var secondClient = CreateBearerClient(member1Ip10, secondMember, "203.0.113.10");

        using var safeGet = await firstClient.GetAsync(MeApiEndpoints.Path);
        using var firstWrite = await firstClient.PatchAsJsonAsync(MeApiEndpoints.Path, new { });
        using var secondMemberWrite = await secondClient.PatchAsJsonAsync(MeApiEndpoints.Path, new { });
        using var firstMemberRetry = await firstClient.PatchAsJsonAsync(MeApiEndpoints.Path, new { });

        Assert.Equal(HttpStatusCode.OK, safeGet.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, firstWrite.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, secondMemberWrite.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, firstMemberRetry.StatusCode);
    }

    [Fact]
    public async Task Authenticated_ip_safety_net_applies_across_accounts()
    {
        var firstMember = await SeedMemberAsync(member10Ip1, "ip-one@example.com", "IP One");
        var secondMember = await SeedMemberAsync(member10Ip1, "ip-two@example.com", "IP Two");
        using var firstClient = CreateBearerClient(member10Ip1, firstMember, "203.0.113.10");
        using var secondClient = CreateBearerClient(member10Ip1, secondMember, "203.0.113.10");

        using var firstWrite = await firstClient.PatchAsJsonAsync(MeApiEndpoints.Path, new { });
        using var secondWrite = await secondClient.PatchAsJsonAsync(MeApiEndpoints.Path, new { });

        Assert.Equal(HttpStatusCode.BadRequest, firstWrite.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, secondWrite.StatusCode);
    }

    [Fact]
    public async Task Website_and_mobile_writes_share_member_allowance()
    {
        var member = await SeedMemberAsync(member1Ip10, "shared-route@example.com", "Shared Route Fan");
        using var website = member1Ip10.CreateAnonymousClient(allowAutoRedirect: false);
        website.DefaultRequestHeaders.Add(TestMemberAuthHandler.MemberIdHeader, member.Id.ToString());
        website.DefaultRequestHeaders.Add(TestMemberAuthHandler.DisplayNameHeader, member.DisplayName);
        website.DefaultRequestHeaders.Add(TestMemberAuthHandler.EmailHeader, member.Email);
        website.DefaultRequestHeaders.Add("X-Forwarded-For", "203.0.113.11");

        var page = await website.GetStringAsync("/account/settings");
        using var websiteWrite = await website.PostAsync(
            "/account/settings",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = ExtractAntiforgeryToken(page),
                ["DisplayName"] = "Updated On Website",
            }));

        using var mobile = CreateBearerClient(member1Ip10, member, "203.0.113.11");
        using var mobileWrite = await mobile.PatchAsJsonAsync(
            MeApiEndpoints.Path,
            new { displayName = "Tried On Mobile" });

        Assert.Equal(HttpStatusCode.Redirect, websiteWrite.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, mobileWrite.StatusCode);
        var stored = await member1Ip10.Services.GetRequiredService<IMemberAccountRepository>()
            .FindByIdAsync(member.Id);
        Assert.Equal("Updated On Website", stored!.DisplayName);
    }

    [Fact]
    public async Task Rejected_upload_does_not_reach_multipart_body_binding()
    {
        var member = await SeedMemberAsync(member1Ip10, "upload-limit@example.com", "Upload Limit Fan");
        using var client = CreateBearerClient(member1Ip10, member, "203.0.113.12");

        using var allowance = await client.PatchAsJsonAsync(MeApiEndpoints.Path, new { });
        using var malformedUpload = new HttpRequestMessage(HttpMethod.Post, "/api/v1/me/avatar")
        {
            Content = new ByteArrayContent("not valid multipart"u8.ToArray()),
        };
        malformedUpload.Content.Headers.ContentType =
            MediaTypeHeaderValue.Parse("multipart/form-data; boundary=missing-boundary");
        using var rejected = await client.SendAsync(malformedUpload);

        Assert.Equal(HttpStatusCode.BadRequest, allowance.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
        var stored = await member1Ip10.Services.GetRequiredService<IMemberAccountRepository>()
            .FindByIdAsync(member.Id);
        Assert.Null(stored!.AvatarUrl);
    }

    private static async Task<MemberAccount> SeedMemberAsync(
        QueenZoneWebApplicationFactory factory,
        string email,
        string displayName)
    {
        var member = new MemberAccount
        {
            Id = Guid.NewGuid(),
            Email = email,
            DisplayName = displayName,
            CreatedAt = DateTime.UtcNow,
        };
        return await factory.Services.GetRequiredService<IMemberAccountRepository>().CreateAsync(member);
    }

    private static HttpClient CreateBearerClient(
        QueenZoneWebApplicationFactory factory,
        MemberAccount member,
        string forwardedFor)
    {
        using var scope = factory.Services.CreateScope();
        var token = scope.ServiceProvider.GetRequiredService<MobileAuthTokenIssuer>()
            .IssueAccessToken(member.Id, member.Email, member.DisplayName);
        var client = factory.CreateAnonymousClient(allowAutoRedirect: false);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        client.DefaultRequestHeaders.Add("X-Forwarded-For", forwardedFor);
        return client;
    }

    private static string ExtractAntiforgeryToken(string html)
    {
        var match = AntiforgeryTokenRegex().Match(html);
        Assert.True(match.Success, "Antiforgery token was not found in the form.");
        return match.Groups["token"].Value;
    }

    [GeneratedRegex("name=\"__RequestVerificationToken\" value=\"(?<token>[^\"]+)\"", RegexOptions.IgnoreCase)]
    private static partial Regex AntiforgeryTokenRegex();
}
