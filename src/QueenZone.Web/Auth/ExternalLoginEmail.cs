using System.Security.Claims;
using System.Text;
using System.Text.Json;

namespace QueenZone.Web;

/// <summary>
/// Reads the verified-email signal each external provider actually sends.
/// Google, Microsoft, and Apple use <c>email_verified</c>. Discord uses <c>verified</c>.
/// GitHub does not send a trustworthy address on the profile payload; the emails API
/// sets <see cref="EmailVerifiedClaimType"/> only after a verified primary address is chosen.
/// </summary>
public static class ExternalLoginEmail
{
    public const string EmailVerifiedClaimType = "email_verified";

    public const string DiscordVerifiedClaimType = "verified";

    public static bool IsVerified(string? provider, ClaimsPrincipal principal)
    {
        ArgumentNullException.ThrowIfNull(principal);

        var claimType = MemberAuthenticationSchemes.NormalizeExternalProvider(provider) switch
        {
            MemberAuthenticationSchemes.Discord => DiscordVerifiedClaimType,
            MemberAuthenticationSchemes.Google
                or MemberAuthenticationSchemes.Microsoft
                or MemberAuthenticationSchemes.Apple
                or MemberAuthenticationSchemes.GitHub => EmailVerifiedClaimType,
            _ => null,
        };

        return claimType is not null && IsAffirmative(principal.FindFirst(claimType)?.Value);
    }

    public static bool IsAffirmative(string? value) =>
        string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);

    public static bool EmailsMatch(string? left, string? right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
        {
            return false;
        }

        return string.Equals(left.Trim(), right.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Maps a JSON boolean (or the strings <c>true</c>/<c>false</c>) onto a claim value.
    /// Returns null when the property is absent so claim actions skip it.
    /// </summary>
    public static string? ReadJsonBoolean(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        return property.ValueKind switch
        {
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.String when IsAffirmative(property.GetString()) => "true",
            JsonValueKind.String when string.Equals(property.GetString(), "false", StringComparison.OrdinalIgnoreCase) => "false",
            _ => null,
        };
    }

    /// <summary>
    /// Copies <c>email_verified</c> from an identity-provider id token payload.
    /// The token is the one the provider's token endpoint returned to the server.
    /// An existing claim is left as-is.
    /// </summary>
    public static void ApplyIdTokenEmailVerified(ClaimsIdentity identity, string? idToken)
    {
        ArgumentNullException.ThrowIfNull(identity);

        if (identity.HasClaim(claim => claim.Type == EmailVerifiedClaimType))
        {
            return;
        }

        var value = ReadEmailVerifiedFromIdToken(idToken);
        if (value is null)
        {
            return;
        }

        identity.AddClaim(new Claim(EmailVerifiedClaimType, value, ClaimValueTypes.String, identity.AuthenticationType));
    }

    public static string? ReadEmailVerifiedFromIdToken(string? idToken)
    {
        if (string.IsNullOrWhiteSpace(idToken))
        {
            return null;
        }

        var parts = idToken.Split('.');
        if (parts.Length < 2 || string.IsNullOrEmpty(parts[1]))
        {
            return null;
        }

        try
        {
            var json = Encoding.UTF8.GetString(Base64UrlDecode(parts[1]));
            using var document = JsonDocument.Parse(json);
            return ReadJsonBoolean(document.RootElement, EmailVerifiedClaimType);
        }
        catch (FormatException)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private static byte[] Base64UrlDecode(string value)
    {
        var padded = value.Replace('-', '+').Replace('_', '/');
        padded = (padded.Length % 4) switch
        {
            2 => padded + "==",
            3 => padded + "=",
            0 => padded,
            _ => throw new FormatException("Invalid base64url payload."),
        };

        return Convert.FromBase64String(padded);
    }
}
