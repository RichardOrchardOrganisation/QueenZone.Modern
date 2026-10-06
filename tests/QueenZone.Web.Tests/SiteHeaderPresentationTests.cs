using System.Reflection;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using QueenZone.Data;
using QueenZone.Data.Entities;
using QueenZone.Web;

namespace QueenZone.Web.Tests;

public sealed class SiteHeaderPresentationTests
{
    [Theory]
    [InlineData("/", "/", true)]
    [InlineData("/news", "/", false)]
    [InlineData("/NEWS/1003/title", "/news", true)]
    [InlineData("/articles", "/news", false)]
    [InlineData("/account/settings", "/account/settings", true)]
    public void ActiveRoute_PreservesRootAndCaseInsensitivePrefixMatching(string path, string route, bool active) =>
        Assert.Equal(active, new SiteHeaderViewModel(path, true, null, null, false, 0, false, null).IsActive(route));

    [Theory]
    [InlineData(0, "Messages")]
    [InlineData(1, "Messages, 1 unread conversations")]
    [InlineData(12, "Messages, 12 unread conversations")]
    public void MessageLabel_PreservesUnreadAnnouncement(int unread, string expected) =>
        Assert.Equal(expected, new SiteHeaderViewModel("/", true, null, null, false, unread, false, null).MessagesIconLabel);

    [Theory]
    [InlineData(null, true)]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public async Task AnonymousHeader_UsesMastheadDefaultAndIgnoresAmbientAdminIdentity(bool? dark, bool expectedDark)
    {
        using var services = Services(AuthenticateResult.NoResult()).BuildServiceProvider();
        var context = Context(services);
        context.User = Principal(new Claim(ClaimTypes.Name, "Ambient administrator"));
        var model = await SiteHeaderPresentation.CreateAsync(context, dark, false);
        Assert.Equal(expectedDark, model.MastheadDark);
        Assert.Null(model.MemberId);
        Assert.Null(model.MemberDisplayName);
        Assert.False(model.MemberHasAvatar);
        Assert.Equal(0, model.UnreadMessageCount);
        Assert.Null(model.AdminEmail);
    }

    [Theory]
    [InlineData(0, "email@example.com")]
    [InlineData(1, "preferred@example.com")]
    [InlineData(2, "Identity name")]
    [InlineData(3, null)]
    public async Task AdminHeader_PreservesIdentityFallbackOrder(int state, string? expected)
    {
        using var services = Services(AuthenticateResult.NoResult()).BuildServiceProvider();
        var claims = new List<Claim>();
        if (state == 0)
        {
            claims.Add(new Claim(ClaimTypes.Email, "email@example.com"));
        }
        if (state <= 1)
        {
            claims.Add(new Claim("preferred_username", "preferred@example.com"));
        }
        if (state <= 2)
        {
            claims.Add(new Claim(ClaimTypes.Name, "Identity name"));
        }
        var context = Context(services);
        context.User = Principal(claims.ToArray());
        var model = await SiteHeaderPresentation.CreateAsync(context, null, true);
        Assert.True(model.ShowAdminNav);
        Assert.Equal(expected, model.AdminEmail);
        Assert.Null(model.MemberDisplayName);
    }

    [Theory]
    [InlineData(false, 0, false, false)]
    [InlineData(false, 0, true, false)]
    [InlineData(true, 3, true, false)]
    [InlineData(true, 3, true, true)]
    public async Task MemberHeader_LoadsOptionalChromeAndFailsSoft(bool avatar, int unread, bool repositories, bool fail)
    {
        var id = Guid.NewGuid();
        var collection = Services(Success(Principal(new Claim(ClaimTypes.NameIdentifier, id.ToString()), new Claim(ClaimTypes.Name, "Member Fan"))));
        if (repositories)
        {
            collection.AddSingleton(HeaderRepositoryProxy.Create<IMemberAccountRepository>((_, _) => fail
                ? throw new InvalidOperationException("Database unavailable")
                : Task.FromResult<MemberAccount?>(new MemberAccount
                {
                    Id = id,
                    DisplayName = "Member Fan",
                    Email = "member@example.com",
                    AvatarUrl = avatar ? "members/avatar.webp" : "  ",
                })));
            collection.AddSingleton(HeaderRepositoryProxy.Create<IPrivateMessageRepository>((_, _) => Task.FromResult(unread)));
        }
        using var services = collection.BuildServiceProvider();
        var model = await SiteHeaderPresentation.CreateAsync(Context(services), false, false);
        Assert.Equal("Member Fan", model.MemberDisplayName);
        Assert.Equal(id, model.MemberId);
        Assert.Equal(repositories && !fail && avatar, model.MemberHasAvatar);
        Assert.Equal(repositories && !fail ? unread : 0, model.UnreadMessageCount);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("not-a-guid")]
    public async Task MemberHeader_WithMissingOrInvalidIdStillPreservesDisplayName(string? id)
    {
        var claims = new List<Claim> { new(ClaimTypes.Name, "Member without id") };
        if (id is not null)
        {
            claims.Add(new Claim(ClaimTypes.NameIdentifier, id));
        }
        using var services = Services(Success(Principal(claims.ToArray()))).BuildServiceProvider();
        var model = await SiteHeaderPresentation.CreateAsync(Context(services), null, false);
        Assert.Equal("Member without id", model.MemberDisplayName);
        Assert.Null(model.MemberId);
        Assert.False(model.MemberHasAvatar);
        Assert.Equal(0, model.UnreadMessageCount);
    }

    [Fact]
    public void Navigation_PreservesTheThreeGroupsAndAllDestinations()
    {
        var groups = SiteHeaderNavigation.Groups;
        Assert.Equal(new[] { "band", "archive", "community" }, groups.Select(group => group.Id));
        Assert.Equal(new[]
        {
            "/biography", "/discography", "/discography/rare-discography", "/timeline", "/trivia",
            "/news", "/articles", "/photography", "/links", "/crosswords", "/forum", "/fan-performances", "/freddie-mercury-tribute",
        }, groups.SelectMany(group => group.Items).Select(item => item.Href));
    }

    private static ClaimsPrincipal Principal(params Claim[] claims) => new(new ClaimsIdentity(claims, "Header tests"));

    private static AuthenticateResult Success(ClaimsPrincipal principal) =>
        AuthenticateResult.Success(new AuthenticationTicket(principal, MemberAuthenticationSchemes.MembersCookie));

    private static ServiceCollection Services(AuthenticateResult result)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IAuthenticationService>(new HeaderAuthenticationService(result));
        return services;
    }

    private static DefaultHttpContext Context(IServiceProvider services)
    {
        var context = new DefaultHttpContext { RequestServices = services };
        context.Request.Path = "/news";
        return context;
    }

    private sealed class HeaderAuthenticationService(AuthenticateResult result) : IAuthenticationService
    {
        public Task<AuthenticateResult> AuthenticateAsync(HttpContext context, string? scheme) => Task.FromResult(result);
        public Task ChallengeAsync(HttpContext context, string? scheme, AuthenticationProperties? properties) => throw new NotSupportedException();
        public Task ForbidAsync(HttpContext context, string? scheme, AuthenticationProperties? properties) => throw new NotSupportedException();
        public Task SignInAsync(HttpContext context, string? scheme, ClaimsPrincipal principal, AuthenticationProperties? properties) => throw new NotSupportedException();
        public Task SignOutAsync(HttpContext context, string? scheme, AuthenticationProperties? properties) => throw new NotSupportedException();
    }
}

public class HeaderRepositoryProxy : DispatchProxy
{
    private Func<MethodInfo?, object?[]?, object?> invoke = null!;

    public static T Create<T>(Func<MethodInfo?, object?[]?, object?> invoke) where T : class
    {
        var proxy = Create<T, HeaderRepositoryProxy>();
        ((HeaderRepositoryProxy)(object)proxy).invoke = invoke;
        return proxy;
    }

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => invoke(targetMethod, args);
}
