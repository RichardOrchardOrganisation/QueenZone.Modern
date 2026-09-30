using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace QueenZone.Web.Tests;

public sealed class HttpContextMemberAuthExtensionsTests
{
    private static readonly Guid MemberId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid AmbientId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Fact]
    public async Task AuthenticateMemberIdAsync_returns_id_from_member_cookie()
    {
        var http = CreateHttp(memberPrincipal: PrincipalFor(MemberId.ToString()));

        Assert.Equal(MemberId, await http.AuthenticateMemberIdAsync());
    }

    [Fact]
    public async Task AuthenticateMemberIdAsync_returns_null_when_member_auth_fails()
    {
        var http = CreateHttp(memberPrincipal: null);

        Assert.Null(await http.AuthenticateMemberIdAsync());
    }

    [Fact]
    public async Task AuthenticateMemberIdAsync_ignores_ambient_user()
    {
        var http = CreateHttp(memberPrincipal: null);
        http.User = PrincipalFor(AmbientId.ToString());

        Assert.Null(await http.AuthenticateMemberIdAsync());
    }

    [Fact]
    public async Task AuthenticateMemberIdAsync_returns_null_for_non_guid_identifier()
    {
        var http = CreateHttp(memberPrincipal: PrincipalFor("not-a-guid"));

        Assert.Null(await http.AuthenticateMemberIdAsync());
    }

    [Fact]
    public async Task GetSignedInMemberIdAsync_prefers_ambient_user()
    {
        var http = CreateHttp(memberPrincipal: PrincipalFor(MemberId.ToString()));
        http.User = PrincipalFor(AmbientId.ToString());

        Assert.Equal(AmbientId, await http.GetSignedInMemberIdAsync());
    }

    [Fact]
    public async Task GetSignedInMemberIdAsync_falls_back_to_member_auth()
    {
        var http = CreateHttp(memberPrincipal: PrincipalFor(MemberId.ToString()));

        Assert.Equal(MemberId, await http.GetSignedInMemberIdAsync());
    }

    [Fact]
    public async Task GetSignedInMemberIdAsync_returns_null_when_nobody_is_signed_in()
    {
        var http = CreateHttp(memberPrincipal: null);

        Assert.Null(await http.GetSignedInMemberIdAsync());
    }

    private static ClaimsPrincipal PrincipalFor(string nameIdentifier) =>
        new(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, nameIdentifier)], "test"));

    private static DefaultHttpContext CreateHttp(ClaimsPrincipal? memberPrincipal)
    {
        var services = new ServiceCollection()
            .AddSingleton<IAuthenticationService>(new StubAuthenticationService(memberPrincipal))
            .BuildServiceProvider();
        return new DefaultHttpContext { RequestServices = services };
    }

    private sealed class StubAuthenticationService(ClaimsPrincipal? memberPrincipal) : IAuthenticationService
    {
        public Task<AuthenticateResult> AuthenticateAsync(HttpContext context, string? scheme) =>
            Task.FromResult(
                scheme == MemberAuthenticationSchemes.MembersCookie && memberPrincipal is not null
                    ? AuthenticateResult.Success(new AuthenticationTicket(memberPrincipal, scheme))
                    : AuthenticateResult.NoResult());

        public Task ChallengeAsync(HttpContext context, string? scheme, AuthenticationProperties? properties) =>
            throw new NotSupportedException();

        public Task ForbidAsync(HttpContext context, string? scheme, AuthenticationProperties? properties) =>
            throw new NotSupportedException();

        public Task SignInAsync(HttpContext context, string? scheme, ClaimsPrincipal principal, AuthenticationProperties? properties) =>
            throw new NotSupportedException();

        public Task SignOutAsync(HttpContext context, string? scheme, AuthenticationProperties? properties) =>
            throw new NotSupportedException();
    }
}
