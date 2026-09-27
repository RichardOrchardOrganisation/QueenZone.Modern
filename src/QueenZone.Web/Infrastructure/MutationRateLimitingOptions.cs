namespace QueenZone.Web;

/// <summary>
/// Process-local burst limits for non-admin state-changing routes shared by the
/// website and mobile API. Safe while production remains a single B1 worker.
/// </summary>
public sealed class MutationRateLimitingOptions
{
    public const string SectionName = "RateLimiting:Mutations";

    /// <summary>Anonymous mutation requests allowed per client IP and window.</summary>
    public int AnonymousPermitLimit { get; set; } = 20;

    public int AnonymousWindowMinutes { get; set; } = 1;

    /// <summary>Authenticated mutation requests allowed per member and window.</summary>
    public int AuthenticatedMemberPermitLimit { get; set; } = 20;

    public int AuthenticatedMemberWindowMinutes { get; set; } = 1;

    /// <summary>
    /// Coarse cross-account safety net per client IP. This should remain looser
    /// than the member limit so ordinary shared networks retain fair access.
    /// </summary>
    public int AuthenticatedIpPermitLimit { get; set; } = 120;

    public int AuthenticatedIpWindowMinutes { get; set; } = 1;
}
