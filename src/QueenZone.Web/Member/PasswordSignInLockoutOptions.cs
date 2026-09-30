namespace QueenZone.Web;

/// <summary>
/// Per-account password failure window shared by website login and the mobile password grant.
/// The IP limiter stays as a separate backstop.
/// </summary>
public sealed class PasswordSignInLockoutOptions
{
    public const string SectionName = "MemberAccounts:PasswordLockout";

    /// <summary>Failed password attempts allowed for one account inside the window.</summary>
    public int MaxFailures { get; set; } = 10;

    /// <summary>Length of the fixed failure window, in minutes.</summary>
    public int WindowMinutes { get; set; } = 15;
}
