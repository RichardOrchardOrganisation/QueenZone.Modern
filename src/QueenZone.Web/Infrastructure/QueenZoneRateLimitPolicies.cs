namespace QueenZone.Web;

/// <summary>
/// Named ASP.NET Core rate-limit policies for abuse-sensitive routes.
/// Process-local (single B1 instance); partition by member id when signed in, else client IP.
/// </summary>
public static class QueenZoneRateLimitPolicies
{
    /// <summary>OAuth start / login challenges and mobile <c>/api/v1/auth</c> — IP partition.</summary>
    public const string Auth = "qz-auth";

    /// <summary>Unauthenticated state-changing routes — client-IP partition.</summary>
    public const string AnonymousWrite = "qz-anonymous-write";

    /// <summary>
    /// Authenticated state-changing routes — member partition plus a selective
    /// global client-IP safety net for cross-account bursts.
    /// </summary>
    public const string AuthenticatedWrite = "qz-authenticated-write";

    /// <summary>
    /// Legacy member content-submission policy. New endpoint migrations should
    /// use <see cref="AuthenticatedWrite"/>; retained until #1644 completes.
    /// </summary>
    public const string MemberWrite = "qz-member-write";

    /// <summary>Editor image and avatar uploads.</summary>
    public const string Upload = "qz-upload";

    /// <summary>Public archive search (GET with query).</summary>
    public const string Search = "qz-search";
}
