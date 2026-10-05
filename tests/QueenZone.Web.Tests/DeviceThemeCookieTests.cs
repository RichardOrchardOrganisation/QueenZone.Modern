using Microsoft.AspNetCore.Http;
using QueenZone.Web;

namespace QueenZone.Web.Tests;

public sealed class DeviceThemeCookieTests
{
    [Theory]
    [InlineData("http", DeviceThemeChoice.Light)]
    [InlineData("https", DeviceThemeChoice.Dark)]
    [InlineData("https", DeviceThemeChoice.System)]
    [InlineData("http", DeviceThemeChoice.Account)]
    public void Write_and_delete_always_restrict_cookie_to_https(string scheme, DeviceThemeChoice choice)
    {
        var context = new DefaultHttpContext();
        context.Request.Scheme = scheme;
        DeviceThemeCookie.Write(context, choice);
        var header = context.Response.Headers.SetCookie.ToString();
        Assert.Contains("; secure", header);
        Assert.Contains("; httponly", header);
        Assert.Contains("; samesite=lax", header);
        Assert.Contains("path=/", header);
        if (choice == DeviceThemeChoice.Account) Assert.Contains("expires=", header);
        else Assert.Contains("max-age=31536000", header);
    }
}
