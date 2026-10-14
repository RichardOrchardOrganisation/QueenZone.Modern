namespace QueenZone.Web;

public sealed class AdminOptions
{
    public const string SectionName = "Admin";

    public string[] AllowedEmails { get; init; } = [];

    public string[] AllowedPrincipals { get; init; } = [];

    public bool EnforcePrincipalPin { get; init; }
}
