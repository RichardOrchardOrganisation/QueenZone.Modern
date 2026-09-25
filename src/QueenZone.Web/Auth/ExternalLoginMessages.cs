namespace QueenZone.Web;

/// <summary>
/// Visitor-facing copy for external-login decisions shared by the website callback and mobile completion.
/// </summary>
public static class ExternalLoginMessages
{
    public const string UnverifiedEmail =
        "QueenZone could not confirm that email address with the provider. Verify it with the provider, then try again.";

    public const string ExpiredLink =
        "That sign-in confirmation expired. Start again from the login page.";

    public const string ProviderMismatch =
        "That sign-in does not belong to this QueenZone account. Use its password or a provider already linked to it.";

    public const string EmailMismatch =
        "Sign in to the QueenZone account for that email before linking this provider.";

    public const string AlreadyLinked =
        "That provider account is already linked to a different QueenZone member.";

    public const string UnknownProvider = "Unknown sign-in provider.";
}
