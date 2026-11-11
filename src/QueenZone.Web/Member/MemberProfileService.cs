using QueenZone.Data;
using QueenZone.Data.Entities;

namespace QueenZone.Web;

/// <summary>Validates and updates member profile details and preferences.</summary>
public sealed class MemberProfileService(IMemberAccountRepository memberAccountRepository)
{
    public const int MinDisplayNameLength = 2;

    public const int MaxDisplayNameLength = 100;

    public const string PendingDeletionEditError = "Cancel account deletion before changing your public profile.";

    /// <summary>
    /// Updates the member's display name. Names are not unique — multiple members may share
    /// the same display name (common on forums). Leading/trailing whitespace is stripped.
    /// </summary>
    public async Task<MemberAccountResult> UpdateDisplayNameAsync(Guid memberId, string displayName, CancellationToken cancellationToken = default)
    {
        var account = await memberAccountRepository.FindByIdAsync(memberId, cancellationToken);
        if (account?.DeletionRequestedAt is not null)
        {
            return MemberAccountResult.Failure(MemberAccountOutcome.PendingDeletion, PendingDeletionEditError);
        }

        var trimmed = displayName?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
        {
            return MemberAccountResult.Failure(MemberAccountOutcome.Invalid, "Display name is required.");
        }

        if (trimmed.Length < MinDisplayNameLength)
        {
            return MemberAccountResult.Failure(MemberAccountOutcome.Invalid, $"Display name must be at least {MinDisplayNameLength} characters.");
        }

        if (trimmed.Length > MaxDisplayNameLength)
        {
            return MemberAccountResult.Failure(MemberAccountOutcome.Invalid, $"Display name must be at most {MaxDisplayNameLength} characters.");
        }

        var updated = await memberAccountRepository.UpdateDisplayNameAsync(memberId, trimmed, cancellationToken);
        if (updated is null)
        {
            return MemberAccountResult.Failure(MemberAccountOutcome.NotFound, "Account not found.");
        }

        return MemberAccountResult.Success(updated);
    }

    /// <summary>
    /// Updates who may start a new private conversation with this member.
    /// </summary>
    public async Task<MemberAccountResult> UpdateMessagePrivacyAsync(
        Guid memberId,
        MemberMessagePrivacy messagePrivacy,
        CancellationToken cancellationToken = default)
    {
        var account = await memberAccountRepository.FindByIdAsync(memberId, cancellationToken);
        if (account is null)
        {
            return MemberAccountResult.Failure(MemberAccountOutcome.NotFound, "Account not found.");
        }

        if (account.DeletionRequestedAt is not null)
        {
            return MemberAccountResult.Failure(MemberAccountOutcome.PendingDeletion, PendingDeletionEditError);
        }

        if (!Enum.IsDefined(messagePrivacy))
        {
            return MemberAccountResult.Failure(MemberAccountOutcome.Invalid, "Choose a valid messaging privacy option.");
        }

        var updated = await memberAccountRepository.UpdateMessagePrivacyAsync(
            memberId,
            messagePrivacy,
            cancellationToken);
        if (updated is null)
        {
            return MemberAccountResult.Failure(MemberAccountOutcome.NotFound, "Account not found.");
        }

        return MemberAccountResult.Success(updated);
    }

    /// <summary>
    /// Updates the member's light/dark appearance override (shared by web and mobile).
    /// </summary>
    public async Task<MemberAccountResult> UpdateThemePreferenceAsync(
        Guid memberId,
        MemberThemePreference themePreference,
        CancellationToken cancellationToken = default)
    {
        var account = await memberAccountRepository.FindByIdAsync(memberId, cancellationToken);
        if (account is null)
        {
            return MemberAccountResult.Failure(MemberAccountOutcome.NotFound, "Account not found.");
        }

        if (account.DeletionRequestedAt is not null)
        {
            return MemberAccountResult.Failure(MemberAccountOutcome.PendingDeletion, PendingDeletionEditError);
        }

        if (!Enum.IsDefined(themePreference))
        {
            return MemberAccountResult.Failure(MemberAccountOutcome.Invalid, "Choose a valid appearance option.");
        }

        var updated = await memberAccountRepository.UpdateThemePreferenceAsync(
            memberId,
            themePreference,
            cancellationToken);
        if (updated is null)
        {
            return MemberAccountResult.Failure(MemberAccountOutcome.NotFound, "Account not found.");
        }

        return MemberAccountResult.Success(updated);
    }

    public Task<IReadOnlyList<MemberSocialLink>> ListSocialLinksAsync(
        Guid memberId,
        CancellationToken cancellationToken = default) =>
        memberAccountRepository.ListSocialLinksAsync(memberId, cancellationToken);

    /// <summary>
    /// Validates each channel independently. Empty input removes that row. Invalid input
    /// is a field error and leaves every existing row untouched.
    /// </summary>
    public async Task<MemberSocialLinksUpdateResult> UpdateSocialLinksAsync(
        Guid memberId,
        IReadOnlyDictionary<MemberSocialChannel, string?> inputs,
        CancellationToken cancellationToken = default)
    {
        var account = await memberAccountRepository.FindByIdAsync(memberId, cancellationToken);
        if (account is null)
        {
            return MemberSocialLinksUpdateResult.Failure(MemberAccountOutcome.NotFound, "Account not found.");
        }

        if (account.DeletionRequestedAt is not null)
        {
            return MemberSocialLinksUpdateResult.Failure(MemberAccountOutcome.PendingDeletion, PendingDeletionEditError);
        }

        var fieldErrors = new List<MemberSocialLinkFieldError>();
        var accepted = new List<MemberSocialLink>();
        foreach (var channel in MemberSocialChannels.All)
        {
            inputs.TryGetValue(channel, out var raw);
            if (!MemberSocialLinkUrl.TryNormalize(channel, raw, out var canonicalUrl, out var error))
            {
                fieldErrors.Add(new MemberSocialLinkFieldError(channel, error));
                continue;
            }

            if (!string.IsNullOrWhiteSpace(canonicalUrl))
            {
                accepted.Add(new MemberSocialLink(channel, canonicalUrl));
            }
        }

        if (fieldErrors.Count > 0)
        {
            var existing = await memberAccountRepository.ListSocialLinksAsync(memberId, cancellationToken);
            return MemberSocialLinksUpdateResult.Invalid(existing, fieldErrors);
        }

        await memberAccountRepository.ReplaceSocialLinksAsync(memberId, accepted, cancellationToken);
        return MemberSocialLinksUpdateResult.Success(accepted);
    }
}
