namespace QueenZone.Web;

/// <summary>
/// Visitor-facing copy for account deletion (website <c>/account/delete</c> and
/// <c>/api/v1/me/deletion-request</c>).
/// </summary>
public static class AccountDeletionCopy
{
    public const string ConfirmationPhrase = "DELETE";

    public const string RequestedTitle = "Account deletion scheduled";

    public const string ImmediateTitle = "Account deletion requested";

    public const string ImmediateMessage =
        "Your account has been disabled and your personal data is being removed now. " +
        "Most requests finish within minutes; cleanup that needs a retry is checked every six hours. " +
        "You have been signed out. This cannot be undone.";

    public const string RequestedMessage =
        "You have been signed out. Your public name is now Deleted member, your profile and avatar are hidden, and retained content has anonymised attribution. " +
        "You can sign back in and cancel deletion at any time during the 30-day cooling-off period. " +
        "Cancelling within 30 days restores your identity and attribution. After 30 days, your sign-in data and stored avatar are permanently removed.";

    public const string ConfirmationHint = "Type DELETE to delete the account.";

    public const string ConfirmationRequired = "Type DELETE to confirm account deletion.";

    public static readonly string[] WhatHappens =
    [
        "You are signed out after requesting deletion.",
        "Your public name changes immediately to Deleted member, your profile is hidden, and your avatar is hidden.",
        "Your account is disabled immediately and removal of your personal data begins.",
        "Your modern forum posts and private messages become deleted placeholders.",
        "Your legacy forum archive posts remain, but your modern account link is removed.",
    ];
}
