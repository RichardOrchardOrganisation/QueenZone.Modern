namespace QueenZone.Web;

/// <summary>
/// This browser's own light/dark override. It wins over the member's account preference
/// (<see cref="MemberThemeClaim"/>), which in turn wins over the system setting. Absent means
/// "use the account setting, else the system".
/// </summary>
public static class DeviceThemeCookie
{
    public const string CookieName = "qz_theme";

    private static readonly TimeSpan Lifetime = TimeSpan.FromDays(365);

    /// <summary>The <c>data-theme</c> value ("light" or "dark") this device forces, or <see langword="null"/>.</summary>
    public static string? Read(HttpRequest request) =>
        request.Cookies.TryGetValue(CookieName, out var value) && value is "light" or "dark" ? value : null;

    public static void Write(HttpContext httpContext, DeviceThemeChoice choice)
    {
        var value = choice switch
        {
            DeviceThemeChoice.Light => "light",
            DeviceThemeChoice.Dark => "dark",
            _ => null,
        };

        if (value is null)
        {
            httpContext.Response.Cookies.Delete(CookieName, Options(httpContext));
            return;
        }

        httpContext.Response.Cookies.Append(CookieName, value, Options(httpContext));
    }

    public static DeviceThemeChoice ToChoice(string? value) => value switch
    {
        "light" => DeviceThemeChoice.Light,
        "dark" => DeviceThemeChoice.Dark,
        _ => DeviceThemeChoice.Account,
    };

    private static CookieOptions Options(HttpContext httpContext) => new()
    {
        Path = "/",
        HttpOnly = true,
        Secure = httpContext.Request.IsHttps,
        SameSite = SameSiteMode.Lax,
        MaxAge = Lifetime,
        IsEssential = true,
    };
}

/// <summary>What a member picks for the current browser.</summary>
public enum DeviceThemeChoice
{
    /// <summary>No device override: follow the account setting, else the system.</summary>
    Account = 0,

    Light = 1,

    Dark = 2,
}
