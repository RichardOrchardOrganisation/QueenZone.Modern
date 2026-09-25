using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace QueenZone.Web.Tests;

public sealed class MobileAdminApiAuthorizationTests : IClassFixture<QueenZoneWebApplicationFactory>
{
    private readonly QueenZoneWebApplicationFactory factory;

    public MobileAdminApiAuthorizationTests(QueenZoneWebApplicationFactory factory)
    {
        this.factory = factory;
    }

    [Fact]
    public async Task Member_access_token_cannot_call_admin_api_or_admin_pages()
    {
        var token = IssueMemberToken("fan@example.com");
        using var client = factory.CreateAnonymousClient(allowAutoRedirect: false);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var api = await client.GetAsync(AdminApiEndpoints.RootPath);
        Assert.Equal(HttpStatusCode.Unauthorized, api.StatusCode);
        Assert.Equal("application/problem+json", api.Content.Headers.ContentType?.MediaType);
        var problem = await api.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(StatusCodes.Status401Unauthorized, problem.GetProperty("status").GetInt32());

        using var page = await client.GetAsync("/admin");
        Assert.Equal(HttpStatusCode.Unauthorized, page.StatusCode);
    }

    [Fact]
    public async Task Member_access_token_with_allowlisted_email_still_cannot_call_admin_api()
    {
        var token = IssueMemberToken(AdminHttpTestHelpers.AdminEmail);
        using var client = factory.CreateAnonymousClient(allowAutoRedirect: false);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var api = await client.GetAsync(AdminApiEndpoints.RootPath);
        Assert.Equal(HttpStatusCode.Unauthorized, api.StatusCode);

        using var session = await client.GetAsync(MobileAuthEndpoints.SessionPath);
        Assert.Equal(HttpStatusCode.OK, session.StatusCode);
    }

    [Fact]
    public async Task Admin_allowlist_header_can_call_admin_api_and_unknown_email_is_forbidden()
    {
        using var admin = factory.CreateAdminClient();
        using var allowed = await admin.GetAsync(AdminApiEndpoints.RootPath);
        Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
        var payload = await allowed.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(AdminHttpTestHelpers.AdminEmail, payload.GetProperty("email").GetString());

        using var stranger = factory.CreateAnonymousClient(allowAutoRedirect: false);
        stranger.DefaultRequestHeaders.Add(TestAuthHandler.UserEmailHeader, "not-admin@test.local");
        using var forbidden = await stranger.GetAsync(AdminApiEndpoints.RootPath);
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        Assert.Equal("application/problem+json", forbidden.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Anonymous_admin_api_returns_problem_details()
    {
        using var client = factory.CreateAnonymousClient(allowAutoRedirect: false);
        using var response = await client.GetAsync(AdminApiEndpoints.RootPath);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    private string IssueMemberToken(string email)
    {
        var memberId = Guid.NewGuid();
        MemberBearerAccounts.Ensure(factory.Services, memberId, email, "Fan");
        using var scope = factory.Services.CreateScope();
        var issuer = scope.ServiceProvider.GetRequiredService<MobileAuthTokenIssuer>();
        return issuer.IssueAccessToken(memberId, email, "Fan");
    }
}
