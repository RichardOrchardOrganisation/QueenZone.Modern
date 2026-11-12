using QueenZone.Data;
using QueenZone.Data.Entities;

namespace QueenZone.Web;

/// <summary>Typed outcome of a member account operation so callers never branch on message text (#2436).</summary>
public enum MemberAccountOutcome
{
    Succeeded,
    NotFound,
    Invalid,
    InvalidCredentials,
    Suspended,
    PendingDeletion,
    Conflict,
    QuotaExceeded,
    UploadFailed,
    DeletionNotCancellable,
    PurgeIncomplete,
}

public sealed record MemberAccountResult(MemberAccountOutcome Outcome, MemberAccount? Account, string? Error)
{
    public bool Succeeded => Outcome == MemberAccountOutcome.Succeeded;

    public static MemberAccountResult Success(MemberAccount account) => new(MemberAccountOutcome.Succeeded, account, null);

    public static MemberAccountResult Failure(MemberAccountOutcome outcome, string error) => new(outcome, null, error);
}

public sealed record MemberSocialLinkFieldError(MemberSocialChannel Channel, string Message);

public sealed record MemberSocialLinksUpdateResult(
    MemberAccountOutcome Outcome,
    string? Error,
    IReadOnlyList<MemberSocialLink> Links,
    IReadOnlyList<MemberSocialLinkFieldError> FieldErrors)
{
    public bool Succeeded => Outcome == MemberAccountOutcome.Succeeded;

    public static MemberSocialLinksUpdateResult Success(IReadOnlyList<MemberSocialLink> links) =>
        new(MemberAccountOutcome.Succeeded, null, links, []);

    public static MemberSocialLinksUpdateResult Failure(MemberAccountOutcome outcome, string error) =>
        new(outcome, error, [], []);

    public static MemberSocialLinksUpdateResult Invalid(
        IReadOnlyList<MemberSocialLink> existing,
        IReadOnlyList<MemberSocialLinkFieldError> fieldErrors) =>
        new(MemberAccountOutcome.Invalid, null, existing, fieldErrors);
}

public enum LegacyAccountLinkKind
{
    None = 0,
    Linked = 1,
    Claimable = 2,
    Unavailable = 3,
}

public sealed record LegacyAccountLinkState(
    LegacyAccountLinkKind Kind,
    LegacyMemberMatch? Match,
    IReadOnlyList<LegacyMemberMatch> ClaimableMatches,
    IReadOnlyList<LegacyMemberMatch> UnavailableMatches)
{
    public bool HasMultipleClaimable => ClaimableMatches.Count > 1;

    public static LegacyAccountLinkState None() =>
        new(LegacyAccountLinkKind.None, null, [], []);

    public static LegacyAccountLinkState Linked(LegacyMemberMatch match) =>
        new(LegacyAccountLinkKind.Linked, match, [], []);

    public static LegacyAccountLinkState Claimable(
        IReadOnlyList<LegacyMemberMatch> claimableMatches,
        IReadOnlyList<LegacyMemberMatch>? unavailableMatches = null)
    {
        ArgumentNullException.ThrowIfNull(claimableMatches);
        if (claimableMatches.Count == 0)
        {
            throw new ArgumentException("At least one claimable match is required.", nameof(claimableMatches));
        }

        var unavailable = unavailableMatches ?? [];
        // Match is set for the common single-match UI/tests; multi-match leaves it null.
        var primary = claimableMatches.Count == 1 ? claimableMatches[0] : null;
        return new(LegacyAccountLinkKind.Claimable, primary, claimableMatches, unavailable);
    }

    public static LegacyAccountLinkState Claimable(LegacyMemberMatch match) =>
        Claimable([match], []);

    public static LegacyAccountLinkState Unavailable(IReadOnlyList<LegacyMemberMatch> unavailableMatches)
    {
        ArgumentNullException.ThrowIfNull(unavailableMatches);
        var primary = unavailableMatches.Count > 0 ? unavailableMatches[0] : null;
        return new(LegacyAccountLinkKind.Unavailable, primary, [], unavailableMatches);
    }

    public static LegacyAccountLinkState Unavailable(LegacyMemberMatch match) =>
        Unavailable([match]);
}
