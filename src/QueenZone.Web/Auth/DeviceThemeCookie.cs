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
        request.Cookies.TryGetValue(CookieName, out var value) && value is "light" or "dark" or "system" ? value : null;

    public static void Write(HttpContext httpContext, DeviceThemeChoice choice)
    {
        var value = choice switch
        {
            DeviceThemeChoice.Light => "light",
            DeviceThemeChoice.Dark => "dark",
            DeviceThemeChoice.System => "system",
            _ => null,
        };

        if (value is null)
        {
            httpContext.Response.Cookies.Delete(CookieName, Options());
            return;
        }

        httpContext.Response.Cookies.Append(CookieName, value, Options());
    }

    public static DeviceThemeChoice ToChoice(string? value) => value switch
    {
        "light" => DeviceThemeChoice.Light,
        "dark" => DeviceThemeChoice.Dark,
        "system" => DeviceThemeChoice.System,
        _ => DeviceThemeChoice.Account,
    };

    private static CookieOptions Options() => new()
    {
        Path = "/",
        HttpOnly = true,
        Secure = true,
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

    /// <summary>Follow the OS even when the account has an explicit preference.</summary>
    System = 3,
}
