using QueenZone.Data.Entities;

namespace QueenZone.Web;

public enum ExternalLoginStatus
{
    SignedIn = 0,
    LinkConfirmationRequired = 1,
    UnverifiedEmail = 2,
    Suspended = 3,
}

public sealed record ExternalLoginResolution(ExternalLoginStatus Status, MemberAccount? Account)
{
    public static ExternalLoginResolution SignedIn(MemberAccount account) =>
        new(ExternalLoginStatus.SignedIn, account);

    public static ExternalLoginResolution ConfirmationRequired(MemberAccount account) =>
        new(ExternalLoginStatus.LinkConfirmationRequired, account);

    public static ExternalLoginResolution Unverified() =>
        new(ExternalLoginStatus.UnverifiedEmail, null);

    public static ExternalLoginResolution Suspended(MemberAccount account) =>
        new(ExternalLoginStatus.Suspended, account);
}
