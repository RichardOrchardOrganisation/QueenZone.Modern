import type { MemberProfile } from '../../api/me';

export function initialLegacySelection(profile: MemberProfile): number | null {
  return profile.legacyLink.claimableMatches[0]?.userId ?? profile.legacyLink.match?.userId ?? null;
}

export function legacyClaimStatus(adoptName: boolean): string {
  return adoptName ? 'Legacy account claimed and display name updated.' : 'Legacy account claimed.';
}

export function settingsLegacyView(profile: MemberProfile | null) {
  const legacy = profile?.legacyLink;
  return {
    linkedMatch: legacy?.kind === 'linked' && legacy.match ? legacy.match : null,
    claimable: legacy?.kind === 'claimable' && legacy.claimableMatches.length > 0,
    claimableMatches: legacy?.claimableMatches ?? [],
  };
}

export function settingsAccountView(profile: MemberProfile | null) {
  return {
    minDisplayNameLength: profile?.limits.minDisplayNameLength ?? 2,
    maxDisplayNameLength: profile?.limits.maxDisplayNameLength ?? 100,
    linkedProviders: profile?.linkedProviders.length ? profile.linkedProviders.join(', ') : 'None linked',
    deletionLabel: profile?.scheduledDeletionAt ? 'Review or cancel deletion' : 'Delete my account',
  };
}
