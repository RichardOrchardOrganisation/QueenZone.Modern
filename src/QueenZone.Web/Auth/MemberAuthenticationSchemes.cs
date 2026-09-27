namespace QueenZone.Web;

public static class MemberAuthenticationSchemes
{
    public const string MembersCookie = "MembersCookie";

    /// <summary>Short-lived cookie used only between an external provider's challenge and callback.</summary>
    public const string ExternalCookie = "MembersExternalCookie";

    /// <summary>
    /// Short-lived cookie for a new provider subject that matches an existing account and still
    /// needs that account to confirm the link.
    /// </summary>
    public const string ExternalLinkCookie = "MembersExternalLinkCookie";

    public const string Google = "Google";

    public const string Microsoft = "Microsoft";

    public const string Discord = "Discord";

    public const string GitHub = "GitHub";

    public const string Apple = "Apple";

    public const string MemberPolicy = "Member";

    /// <summary>JWT bearer scheme issued to the mobile client. Not the default web scheme.</summary>
    public const string MembersBearer = "MembersBearer";

    /// <summary>Member API access via a mobile access token. Does not accept the web cookie.</summary>
    public const string MobileMemberPolicy = "MobileMember";

    public static readonly string[] ExternalProviders =
    [
        Google,
        Microsoft,
        Discord,
        GitHub,
        Apple,
    ];

    public static string? NormalizeExternalProvider(string? provider)
    {
        if (string.IsNullOrWhiteSpace(provider))
        {
            return null;
        }

        foreach (var name in ExternalProviders)
        {
            if (string.Equals(name, provider, StringComparison.OrdinalIgnoreCase))
            {
                return name;
            }
        }

        return null;
    }
}
