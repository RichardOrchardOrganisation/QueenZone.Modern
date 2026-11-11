using QueenZone.Data;
using QueenZone.Data.Entities;

namespace QueenZone.Web;

/// <summary>Resolves, claims, unlinks and backfills legacy member identities.</summary>
public sealed class LegacyAccountLinkService(
    IMemberAccountRepository memberAccountRepository,
    ILegacyMemberLookupRepository legacyMemberLookupRepository,
    MemberProfileService profiles)
{
    /// <summary>
    /// Resolves whether the member is already linked to a legacy account, or can claim one
    /// or more free USERS_T rows that match their modern email.
    /// </summary>
    public async Task<LegacyAccountLinkState> GetLegacyLinkStateAsync(
        MemberAccount account,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(account);

        if (account.LinkedLegacyUserId is int linkedId)
        {
            var linked = await legacyMemberLookupRepository.FindByUserIdAsync(linkedId, cancellationToken)
                ?? new LegacyMemberMatch(linkedId, $"User #{linkedId}");
            return LegacyAccountLinkState.Linked(linked);
        }

        var matches = await legacyMemberLookupRepository.FindAllByEmailAsync(account.Email, cancellationToken);
        if (matches.Count == 0)
        {
            return LegacyAccountLinkState.None();
        }

        var claimable = new List<LegacyMemberMatch>(matches.Count);
        var unavailable = new List<LegacyMemberMatch>();
        foreach (var match in matches)
        {
            var takenBy = await memberAccountRepository.FindByLinkedLegacyUserIdAsync(match.UserId, cancellationToken);
            if (takenBy is not null && takenBy.Id != account.Id)
            {
                unavailable.Add(match);
            }
            else
            {
                claimable.Add(match);
            }
        }

        if (claimable.Count > 0)
        {
            return LegacyAccountLinkState.Claimable(claimable, unavailable);
        }

        return LegacyAccountLinkState.Unavailable(unavailable);
    }

    /// <summary>
    /// Explicitly claims a free legacy USERS_T account that matches the member's email.
    /// When multiple rows share that email, <paramref name="legacyUserId"/> selects which one.
    /// Optionally adopts the legacy username as the modern display name.
    /// </summary>
    public async Task<MemberAccountResult> ClaimLegacyAccountAsync(
        Guid memberId,
        int legacyUserId,
        bool adoptLegacyDisplayName,
        CancellationToken cancellationToken = default)
    {
        var account = await memberAccountRepository.FindByIdAsync(memberId, cancellationToken);
        if (account is null)
        {
            return MemberAccountResult.Failure(MemberAccountOutcome.NotFound, "Account not found.");
        }

        if (account.LinkedLegacyUserId is not null)
        {
            return MemberAccountResult.Success(account);
        }

        var matches = await legacyMemberLookupRepository.FindAllByEmailAsync(account.Email, cancellationToken);
        var match = matches.FirstOrDefault(item => item.UserId == legacyUserId);
        if (match is null)
        {
            return MemberAccountResult.Failure(
                MemberAccountOutcome.Invalid,
                "That legacy account does not match your email, or was not found.");
        }

        var takenBy = await memberAccountRepository.FindByLinkedLegacyUserIdAsync(match.UserId, cancellationToken);
        if (takenBy is not null && takenBy.Id != memberId)
        {
            return MemberAccountResult.Failure(
                MemberAccountOutcome.Conflict,
                "That legacy account is already linked to another modern member.");
        }

        var linked = await memberAccountRepository.LinkLegacyUserIdAsync(memberId, match.UserId, cancellationToken);
        if (linked is null)
        {
            return MemberAccountResult.Failure(MemberAccountOutcome.NotFound, "Account not found.");
        }

        if (adoptLegacyDisplayName && !string.IsNullOrWhiteSpace(match.Username))
        {
            var renamed = await profiles.UpdateDisplayNameAsync(memberId, match.Username, cancellationToken);
            if (renamed.Succeeded && renamed.Account is not null)
            {
                return renamed;
            }
        }

        return MemberAccountResult.Success(linked);
    }

    /// <summary>
    /// Clears the member's legacy USERS_T link so they can claim a different free match
    /// (or remain unlinked). Does not change display name.
    /// </summary>
    public async Task<MemberAccountResult> UnlinkLegacyAccountAsync(
        Guid memberId,
        CancellationToken cancellationToken = default)
    {
        var account = await memberAccountRepository.FindByIdAsync(memberId, cancellationToken);
        if (account is null)
        {
            return MemberAccountResult.Failure(MemberAccountOutcome.NotFound, "Account not found.");
        }

        if (account.LinkedLegacyUserId is null)
        {
            return MemberAccountResult.Success(account);
        }

        var unlinked = await memberAccountRepository.UnlinkLegacyUserIdAsync(memberId, cancellationToken);
        if (unlinked is null)
        {
            return MemberAccountResult.Failure(MemberAccountOutcome.NotFound, "Account not found.");
        }

        return MemberAccountResult.Success(unlinked);
    }

    /// <summary>
    /// Silently links a free legacy account when the member is still unlinked and their email
    /// matches exactly one free USERS_T row. Used to backfill accounts created before the claim UI
    /// existed. Multiple free matches are left unlinked so the member can choose in Settings.
    /// </summary>
    internal async Task<MemberAccount> TryBackfillLegacyLinkAsync(
        MemberAccount account,
        CancellationToken cancellationToken)
    {
        if (account.LinkedLegacyUserId is not null)
        {
            return account;
        }

        var matches = await legacyMemberLookupRepository.FindAllByEmailAsync(account.Email, cancellationToken);
        if (matches.Count == 0)
        {
            return account;
        }

        LegacyMemberMatch? soleFree = null;
        foreach (var match in matches)
        {
            var takenBy = await memberAccountRepository.FindByLinkedLegacyUserIdAsync(match.UserId, cancellationToken);
            if (takenBy is not null && takenBy.Id != account.Id)
            {
                continue;
            }

            if (soleFree is not null)
            {
                // Ambiguous: more than one free legacy account for this email.
                return account;
            }

            soleFree = match;
        }

        if (soleFree is null)
        {
            return account;
        }

        return await memberAccountRepository.LinkLegacyUserIdAsync(account.Id, soleFree.UserId, cancellationToken)
            ?? account;
    }
}
