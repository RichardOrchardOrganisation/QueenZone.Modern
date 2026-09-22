using System.Globalization;
using System.Security.Claims;
using QueenZone.Data.Entities;

namespace QueenZone.Web;

/// <summary>
/// Decides whether an already-issued member credential is still acceptable.
/// Suspension rejects every credential. A deletion request rejects credentials issued
/// at or before <see cref="MemberAccount.DeletionRequestedAt"/> and leaves a later sign-in
/// intact so the member can cancel during the cooling-off period.
/// </summary>
internal static class MemberSessionGate
{
    public const string IssuedAtUnixMillisecondsClaim = "qz_iat";

    public static bool Reject(MemberAccount? account, DateTimeOffset? credentialIssuedAtUtc)
    {
        if (account is null || account.IsSuspended)
        {
            return true;
        }

        if (account.DeletionRequestedAt is not DateTime requestedAt)
        {
            return false;
        }

        if (credentialIssuedAtUtc is null)
        {
            return true;
        }

        var cutoff = requestedAt.Kind == DateTimeKind.Utc
            ? requestedAt
            : DateTime.SpecifyKind(requestedAt, DateTimeKind.Utc);
        var issuedMs = new DateTimeOffset(
                DateTime.SpecifyKind(credentialIssuedAtUtc.Value.UtcDateTime, DateTimeKind.Utc))
            .ToUnixTimeMilliseconds();
        var cutoffMs = new DateTimeOffset(cutoff).ToUnixTimeMilliseconds();
        return issuedMs <= cutoffMs;
    }

    public static DateTimeOffset? ReadIssuedAt(ClaimsPrincipal? principal)
    {
        var raw = principal?.FindFirst(IssuedAtUnixMillisecondsClaim)?.Value;
        return long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var unixMilliseconds)
            ? DateTimeOffset.FromUnixTimeMilliseconds(unixMilliseconds)
            : null;
    }

    /// <summary>
    /// Cookie tickets store <c>IssuedUtc</c> at whole-second resolution, so a sign-in in the
    /// same second as a deletion request looks older than the request. This claim keeps the
    /// millisecond the credential was actually issued.
    /// </summary>
    public static Claim CreateIssuedAtClaim(DateTimeOffset issuedAtUtc) =>
        new(
            IssuedAtUnixMillisecondsClaim,
            issuedAtUtc.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture),
            ClaimValueTypes.Integer64);

    public static DateTimeOffset? ResolveIssuedAt(ClaimsPrincipal? principal, DateTimeOffset? ticketIssuedUtc) =>
        ReadIssuedAt(principal) ?? ticketIssuedUtc;
}
