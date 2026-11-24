using QueenZone.Data.Entities;

namespace QueenZone.Application;

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
