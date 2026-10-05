using System.Security.Claims;
using QueenZone.Data;

namespace QueenZone.Web;

/// <summary>
/// Carries the member's light/dark override in the member cookie so the layout can render
/// <c>data-theme</c> without a database read on every page.
/// </summary>
public static class MemberThemeClaim
{
    public const string ClaimType = "qz:theme";

    public static Claim Create(MemberThemePreference preference) =>
        new(ClaimType, ToAttributeValue(preference) ?? "system");

    /// <summary>
    /// The <c>data-theme</c> attribute value for an explicit choice, or <see langword="null"/> when the
    /// page should follow the system setting.
    /// </summary>
    public static string? ToAttributeValue(MemberThemePreference preference) => preference switch
    {
        MemberThemePreference.Light => "light",
        MemberThemePreference.Dark => "dark",
        _ => null,
    };

    public static MemberThemePreference Read(ClaimsPrincipal? principal) =>
        principal?.FindFirstValue(ClaimType) switch
        {
            "light" => MemberThemePreference.Light,
            "dark" => MemberThemePreference.Dark,
            _ => MemberThemePreference.System,
        };
}
