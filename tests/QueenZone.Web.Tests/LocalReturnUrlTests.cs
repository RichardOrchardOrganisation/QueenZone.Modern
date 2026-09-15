using QueenZone.Web.Infrastructure;
using QueenZone.Web.Pages.Account;

namespace QueenZone.Web.Tests;

/// <summary>
/// Open-redirect guard for OAuth returnUrl (issue #581; security regression suite #338).
/// </summary>
public sealed class LocalReturnUrlTests
{
    [Theory]
    [InlineData("/")]
    [InlineData("/forum")]
    [InlineData("/forum/topic/1")]
    [InlineData("/fan-performances/page/2")]
    [InlineData("/messages/compose?to=42")]
    [InlineData("/account/settings#profile")]
    public void Resolve_AllowsLocalPaths(string returnUrl)
    {
        Assert.True(LocalReturnUrl.IsLocal(returnUrl));
        Assert.Equal(returnUrl, LocalReturnUrl.Resolve(returnUrl));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("//evil.example.com")]
    [InlineData("//evil.example.com/phish")]
    [InlineData("/\\evil.example.com")]
    [InlineData("https://evil.example.com")]
    [InlineData("http://evil.example.com")]
    [InlineData("https://evil.example.com/phish")]
    [InlineData("evil.example.com")]
    [InlineData("forum/topic/1")]
    [InlineData("~/account/settings")]
    [InlineData("/forum\u0000topic")]
    public void Resolve_RejectsUnsafeValues(string? returnUrl)
    {
        Assert.False(LocalReturnUrl.IsLocal(returnUrl));
        Assert.Equal("/", LocalReturnUrl.Resolve(returnUrl));
    }

    [Fact]
    public void WellFormedRelativeUri_StillAllowsProtocolRelative_WhichLocalReturnUrlRejects()
    {
        // Documents why Uri.IsWellFormedUriString alone is insufficient for redirect guards.
        const string protocolRelative = "//evil.example.com/phish";
        Assert.True(Uri.IsWellFormedUriString(protocolRelative, UriKind.Relative));
        Assert.False(LocalReturnUrl.IsLocal(protocolRelative));
    }

    [Theory]
    [InlineData("/forum")]
    [InlineData("/messages/compose?to=42")]
    [InlineData("/account/settings#profile")]
    public void LoginFormRouteValue_UsesEscapeDataStringOnResolvedLocalPath(string returnUrl)
    {
        var resolved = LocalReturnUrl.Resolve(returnUrl);
        Assert.Equal(returnUrl, resolved);
        var encoded = Uri.EscapeDataString(resolved);
        Assert.Equal(encoded, Uri.EscapeDataString(returnUrl));
        Assert.DoesNotContain('<', encoded);
        Assert.DoesNotContain('>', encoded);
        Assert.DoesNotContain('"', encoded);
        Assert.Equal(resolved, LoginModel.DecodeFormReturnUrl(encoded));
        Assert.Equal(resolved, LoginModel.DecodeFormReturnUrl(resolved));
    }

    [Theory]
    [InlineData("//evil.example.com")]
    [InlineData("https://evil.example.com")]
    public void LoginFormDecode_StillRejectsUnsafeValuesAfterUnescape(string returnUrl)
    {
        var decoded = LoginModel.DecodeFormReturnUrl(Uri.EscapeDataString(returnUrl));
        Assert.Equal(returnUrl, decoded);
        Assert.Equal("/", LocalReturnUrl.Resolve(decoded));
    }
}
