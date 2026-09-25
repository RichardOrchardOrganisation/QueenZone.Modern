/** Member account contract for `/api/v1/me` (issues #752 / #753 / #754). */

export const meApiPath = '/me';

export type MessagePrivacy = 'members' | 'followed' | 'nobody';

export type LegacyLinkKind = 'none' | 'linked' | 'claimable' | 'unavailable';

export type LegacyMatch = {
  userId: number;
  username: string;
};

export type LegacyLink = {
  kind: LegacyLinkKind;
  match: LegacyMatch | null;
  claimableMatches: LegacyMatch[];
  unavailableMatches: LegacyMatch[];
};

export type MemberProfileLimits = {
  minDisplayNameLength: number;
  maxDisplayNameLength: number;
  maxAvatarBytes: number;
  allowedAvatarContentTypes: string[];
  deletionRetentionDays: number;
};

export type AccountDeletionInfo = {
  confirmationPhrase: string;
  confirmationHint: string;
  requestedTitle: string;
  requestedMessage: string;
  whatHappens: string[];
};

export type MemberProfile = {
  memberId: string;
  email: string;
  displayName: string;
  createdAt: string;
  lastLoginAt: string | null;
  hasAvatar: boolean;
  avatarPath: string | null;
  avatarThumbPath: string | null;
  messagePrivacy: MessagePrivacy;
  linkedProviders: string[];
  legacyLink: LegacyLink;
  scheduledDeletionAt: string | null;
  limits: MemberProfileLimits;
  deletion: AccountDeletionInfo;
};

export type DeletionRequested = {
  requested: boolean;
  scheduledDeletionAt: string | null;
  title: string;
  message: string;
  statusReceipt: string | null;
};

export const fallbackProfileLimits: MemberProfileLimits = {
  minDisplayNameLength: 2,
  maxDisplayNameLength: 100,
  maxAvatarBytes: 2 * 1024 * 1024,
  allowedAvatarContentTypes: ['image/jpeg', 'image/png', 'image/webp'],
  deletionRetentionDays: 30,
};

export const messagePrivacyOptions: { value: MessagePrivacy; label: string }[] = [
  { value: 'members', label: 'Signed-in members' },
  { value: 'followed', label: 'People I follow' },
  { value: 'nobody', label: 'Nobody' },
];

export function avatarUrl(apiBaseUrl: string, avatarPath: string | null, cacheToken?: string): string | null {
  if (!avatarPath) {
    return null;
  }

  const origin = apiBaseUrl.replace(/\/+$/, '');
  const path = avatarPath.startsWith('/') ? avatarPath : `/${avatarPath}`;
  const url = `${origin}${path}`;
  return cacheToken ? `${url}${path.includes('?') ? '&' : '?'}v=${encodeURIComponent(cacheToken)}` : url;
}

export function formatMemberSince(createdAt: string): string {
  const date = new Date(createdAt);
  if (Number.isNaN(date.getTime())) {
    return '';
  }

  return date.toLocaleDateString('en-GB', { month: 'long', year: 'numeric' });
}

export function parseMemberProfile(payload: unknown): MemberProfile {
  if (!payload || typeof payload !== 'object') {
    throw new Error('Profile response was empty.');
  }

  const raw = payload as Record<string, unknown>;
  if (typeof raw.memberId !== 'string' || typeof raw.displayName !== 'string' || typeof raw.email !== 'string') {
    throw new Error('Profile response was missing member identity.');
  }

  const limitsRaw = raw.limits && typeof raw.limits === 'object' ? (raw.limits as Record<string, unknown>) : {};
  const deletionRaw = raw.deletion && typeof raw.deletion === 'object' ? (raw.deletion as Record<string, unknown>) : {};
  const legacyRaw = raw.legacyLink && typeof raw.legacyLink === 'object' ? (raw.legacyLink as Record<string, unknown>) : {};

  return {
    memberId: raw.memberId,
    email: raw.email,
    displayName: raw.displayName,
    createdAt: typeof raw.createdAt === 'string' ? raw.createdAt : '',
    lastLoginAt: typeof raw.lastLoginAt === 'string' ? raw.lastLoginAt : null,
    hasAvatar: raw.hasAvatar === true,
    avatarPath: typeof raw.avatarPath === 'string' ? raw.avatarPath : null,
    avatarThumbPath: typeof raw.avatarThumbPath === 'string' ? raw.avatarThumbPath : null,
    messagePrivacy: parseMessagePrivacy(raw.messagePrivacy),
    linkedProviders: Array.isArray(raw.linkedProviders)
      ? raw.linkedProviders.filter((item): item is string => typeof item === 'string')
      : [],
    legacyLink: {
      kind: parseLegacyKind(legacyRaw.kind),
      match: parseLegacyMatch(legacyRaw.match),
      claimableMatches: parseLegacyMatches(legacyRaw.claimableMatches),
      unavailableMatches: parseLegacyMatches(legacyRaw.unavailableMatches),
    },
    scheduledDeletionAt: typeof raw.scheduledDeletionAt === 'string' ? raw.scheduledDeletionAt : null,
    limits: {
      minDisplayNameLength: readPositiveInt(limitsRaw.minDisplayNameLength, fallbackProfileLimits.minDisplayNameLength),
      maxDisplayNameLength: readPositiveInt(limitsRaw.maxDisplayNameLength, fallbackProfileLimits.maxDisplayNameLength),
      maxAvatarBytes: readPositiveInt(limitsRaw.maxAvatarBytes, fallbackProfileLimits.maxAvatarBytes),
      allowedAvatarContentTypes: Array.isArray(limitsRaw.allowedAvatarContentTypes)
        ? limitsRaw.allowedAvatarContentTypes.filter((item): item is string => typeof item === 'string')
        : fallbackProfileLimits.allowedAvatarContentTypes,
      deletionRetentionDays: readPositiveInt(
        limitsRaw.deletionRetentionDays,
        fallbackProfileLimits.deletionRetentionDays,
      ),
    },
    deletion: {
      confirmationPhrase:
        typeof deletionRaw.confirmationPhrase === 'string' ? deletionRaw.confirmationPhrase : 'DELETE',
      confirmationHint:
        typeof deletionRaw.confirmationHint === 'string'
          ? deletionRaw.confirmationHint
          : 'Type DELETE to schedule deletion of the account.',
      requestedTitle:
        typeof deletionRaw.requestedTitle === 'string' ? deletionRaw.requestedTitle : 'Account deletion scheduled',
      requestedMessage:
        typeof deletionRaw.requestedMessage === 'string'
          ? deletionRaw.requestedMessage
          : 'You have been signed out. You can sign back in and cancel deletion during the 30-day cooling-off period.',
      whatHappens: Array.isArray(deletionRaw.whatHappens)
        ? deletionRaw.whatHappens.filter((item): item is string => typeof item === 'string')
        : [],
    },
  };
}

export function parseDeletionRequested(payload: unknown): DeletionRequested {
  if (!payload || typeof payload !== 'object') {
    throw new Error('Deletion response was empty.');
  }

  const raw = payload as Record<string, unknown>;
  if (raw.requested !== true || (raw.scheduledDeletionAt !== null && typeof raw.scheduledDeletionAt !== 'string')) {
    throw new Error('Deletion was not confirmed.');
  }

  return {
    requested: true,
    scheduledDeletionAt: raw.scheduledDeletionAt,
    statusReceipt: typeof raw.statusReceipt === 'string' ? raw.statusReceipt : null,
    title: typeof raw.title === 'string'
      ? raw.title
      : raw.scheduledDeletionAt === null ? 'Account deletion requested' : 'Account deletion scheduled',
    message:
      typeof raw.message === 'string'
        ? raw.message
        : raw.scheduledDeletionAt === null
          ? 'Your account has been disabled and your personal data is being removed.'
          : 'You have been signed out. You can sign back in and cancel deletion during the 30-day cooling-off period.',
  };
}

export function validateDisplayName(
  value: string,
  limits: MemberProfileLimits = fallbackProfileLimits,
): string | null {
  const trimmed = value.trim();
  if (trimmed.length === 0) {
    return 'Display name is required.';
  }

  if (trimmed.length < limits.minDisplayNameLength) {
    return `Display name must be at least ${limits.minDisplayNameLength} characters.`;
  }

  if (trimmed.length > limits.maxDisplayNameLength) {
    return `Display name must be at most ${limits.maxDisplayNameLength} characters.`;
  }

  return null;
}

function parseMessagePrivacy(value: unknown): MessagePrivacy {
  if (value === 'followed' || value === 'nobody' || value === 'members') {
    return value;
  }

  return 'members';
}

function parseLegacyKind(value: unknown): LegacyLinkKind {
  if (value === 'linked' || value === 'claimable' || value === 'unavailable' || value === 'none') {
    return value;
  }

  return 'none';
}

function parseLegacyMatch(value: unknown): LegacyMatch | null {
  if (!value || typeof value !== 'object') {
    return null;
  }

  const raw = value as { userId?: unknown; username?: unknown };
  if (typeof raw.userId !== 'number' || typeof raw.username !== 'string') {
    return null;
  }

  return { userId: raw.userId, username: raw.username };
}

function parseLegacyMatches(value: unknown): LegacyMatch[] {
  if (!Array.isArray(value)) {
    return [];
  }

  return value.flatMap((item) => {
    const match = parseLegacyMatch(item);
    return match ? [match] : [];
  });
}

function readPositiveInt(value: unknown, fallback: number): number {
  return typeof value === 'number' && Number.isFinite(value) && value > 0 ? Math.trunc(value) : fallback;
}
