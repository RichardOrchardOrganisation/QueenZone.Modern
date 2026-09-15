using System.Security.Cryptography;
using System.Text;

namespace QueenZone.Web;

/// <summary>
/// Stable, non-reversible identifiers for values that must not appear in logs.
/// </summary>
public static class LogRedaction
{
    public const int EmailFingerprintLength = 12;

    /// <summary>
    /// Lowercase/trim <paramref name="email"/>, SHA-256 hex, first 12 characters.
    /// Empty or whitespace input returns an empty string.
    /// </summary>
    public static string EmailFingerprint(string? email)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return string.Empty;
        }

        var normalized = email.Trim().ToLowerInvariant();
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        return Convert.ToHexStringLower(hash)[..EmailFingerprintLength];
    }
}
