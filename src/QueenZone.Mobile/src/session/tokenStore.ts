import * as SecureStore from 'expo-secure-store';
import type { AuthTokens } from '../api/auth';
import { getAppConfig } from '../config/appConfig';

/**
 * Key names written before the atomic origin-scoped grant.
 * Read once, migrated into `queenzone.mobile.<origin>.grant`, then deleted.
 */
const predecessorKeys = {
  unscopedGrant: 'queenzone.mobile.grant',
  unscopedAccess: 'queenzone.mobile.accessToken',
  unscopedRefresh: 'queenzone.mobile.refreshToken',
  unscopedExpiry: 'queenzone.mobile.accessExpiresAt',
  unscopedIdentity: 'queenzone.mobile.identityShell',
} as const;

type ScopedKeys = {
  grant: string;
  identity: string;
  access: string;
  refresh: string;
  expiry: string;
};

/**
 * A grant is only valid at the origin that issued it, and TestFlight ships
 * staging and production builds under one bundle id — so an unscoped key let a
 * `dev.queenzone.org` refresh token reach `www.queenzone.org`, where it is an
 * unknown hash and comes back `invalid_grant`. Scoping keeps both sessions and
 * makes switching between those builds a no-op instead of a silent sign-out.
 *
 * The grant itself is one JSON value so a killed process can never tear it
 * into a partial state (access present, refresh gone).
 *
 * SecureStore keys accept `[A-Za-z0-9._-]` only, so the origin is sanitised.
 */
function apiScope(): string {
  return getAppConfig()
    .apiBaseUrl.replace(/^https?:\/\//i, '')
    .replace(/[^\w.-]+/g, '_');
}

function scopedKeys(): ScopedKeys {
  const scope = apiScope();
  return {
    grant: `queenzone.mobile.${scope}.grant`,
    identity: `queenzone.mobile.${scope}.identityShell`,
    access: `queenzone.mobile.${scope}.accessToken`,
    refresh: `queenzone.mobile.${scope}.refreshToken`,
    expiry: `queenzone.mobile.${scope}.accessExpiresAt`,
  };
}

type StoredGrant = {
  accessToken: string;
  refreshToken: string;
  expiresAt: number;
};

/** Shared iOS accessibility for the session keys. Do not add requireAuthentication. */
const sessionStoreOptions: SecureStore.SecureStoreOptions = {
  keychainAccessible: SecureStore.AFTER_FIRST_UNLOCK,
};

export type StoredIdentityShell = {
  displayName: string;
  memberId: string;
  avatarPath?: string | null;
};

export type StoredSession = AuthTokens & {
  expiresAt: number;
  identity?: StoredIdentityShell | null;
};

export class KeychainLockedError extends Error {
  constructor(cause?: unknown) {
    super('User interaction is not allowed', cause === undefined ? undefined : { cause });
    this.name = 'KeychainLockedError';
  }
}

function errorText(error: unknown): { name: string; message: string } {
  if (error instanceof Error) {
    return { name: error.name, message: error.message };
  }
  return { name: '', message: typeof error === 'string' ? error : '' };
}

/** Locked-device / background Keychain — not a missing session and not a generic outage. */
export function isKeychainLockedError(error: unknown): boolean {
  if (error instanceof KeychainLockedError) {
    return true;
  }
  const { name, message } = errorText(error);
  const haystack = `${name} ${message}`;
  return (
    name === 'KeyChainException' ||
    /KeyChainException/i.test(haystack) ||
    /user interaction is not allowed/i.test(haystack) ||
    /interaction[- ]not[- ]allowed/i.test(haystack)
  );
}

function rethrowKeychainError(error: unknown): never {
  if (isKeychainLockedError(error)) {
    throw error instanceof KeychainLockedError ? error : new KeychainLockedError(error);
  }
  throw error;
}

/** Delete then set — SecItemUpdate cannot change accessibility (expo/expo#23924). */
async function writeSessionItem(key: string, value: string): Promise<void> {
  await SecureStore.deleteItemAsync(key, sessionStoreOptions);
  await SecureStore.setItemAsync(key, value, sessionStoreOptions);
}

function parseGrant(raw: string | null): StoredGrant | null {
  if (!raw) {
    return null;
  }

  try {
    const parsed: unknown = JSON.parse(raw);
    if (!parsed || typeof parsed !== 'object') {
      return null;
    }

    const rec = parsed as Record<string, unknown>;
    if (typeof rec.accessToken !== 'string' || typeof rec.refreshToken !== 'string') {
      return null;
    }

    const expiresAt = typeof rec.expiresAt === 'number' && Number.isFinite(rec.expiresAt) ? rec.expiresAt : 0;
    return { accessToken: rec.accessToken, refreshToken: rec.refreshToken, expiresAt };
  } catch {
    return null;
  }
}

function grantFromFields(
  accessToken: string | null,
  refreshToken: string | null,
  expiry: string | null,
): StoredGrant | null {
  if (!accessToken || !refreshToken) {
    return null;
  }

  const parsedExpiry = expiry ? Number.parseInt(expiry, 10) : 0;
  return {
    accessToken,
    refreshToken,
    expiresAt: Number.isFinite(parsedExpiry) ? parsedExpiry : 0,
  };
}

/**
 * Persist an adopted predecessor into the running build's scoped grant.
 * Best effort: the grant is usable either way, and a failed copy just repeats
 * on the next launch.
 */
async function persistAdoptedGrant(grant: StoredGrant): Promise<void> {
  const keys = scopedKeys();
  try {
    await writeSessionItem(keys.grant, JSON.stringify(grant));
    const scopedIdentity = await SecureStore.getItemAsync(keys.identity, sessionStoreOptions);
    if (!scopedIdentity) {
      const legacyIdentity = await SecureStore.getItemAsync(
        predecessorKeys.unscopedIdentity,
        sessionStoreOptions,
      );
      if (legacyIdentity) {
        await writeSessionItem(keys.identity, legacyIdentity);
      }
    }
    await clearPredecessorKeys();
  } catch {
    // Keep the predecessor so the next launch can try again.
  }
}

/**
 * Adopt a grant written before `queenzone.mobile.<origin>.grant` existed.
 * Order: origin-scoped per-field keys (#1491), then an unscoped atomic grant,
 * then the original unscoped per-field keys. Incomplete predecessors are
 * ignored so a torn per-field write cannot become a session.
 */
async function adoptPredecessorGrant(): Promise<StoredGrant | null> {
  const keys = scopedKeys();
  const [scopedAccess, scopedRefresh, scopedExpiry] = await Promise.all([
    SecureStore.getItemAsync(keys.access, sessionStoreOptions),
    SecureStore.getItemAsync(keys.refresh, sessionStoreOptions),
    SecureStore.getItemAsync(keys.expiry, sessionStoreOptions),
  ]);
  const scopedFields = grantFromFields(scopedAccess, scopedRefresh, scopedExpiry);
  if (scopedFields) {
    await persistAdoptedGrant(scopedFields);
    return scopedFields;
  }

  const unscopedGrant = parseGrant(
    await SecureStore.getItemAsync(predecessorKeys.unscopedGrant, sessionStoreOptions),
  );
  if (unscopedGrant) {
    await persistAdoptedGrant(unscopedGrant);
    return unscopedGrant;
  }

  const [accessToken, refreshToken, expiry] = await Promise.all([
    SecureStore.getItemAsync(predecessorKeys.unscopedAccess, sessionStoreOptions),
    SecureStore.getItemAsync(predecessorKeys.unscopedRefresh, sessionStoreOptions),
    SecureStore.getItemAsync(predecessorKeys.unscopedExpiry, sessionStoreOptions),
  ]);
  const unscopedFields = grantFromFields(accessToken, refreshToken, expiry);
  if (unscopedFields) {
    await persistAdoptedGrant(unscopedFields);
    return unscopedFields;
  }

  return null;
}

export async function readStoredSession(): Promise<StoredSession | null> {
  try {
    const keys = scopedKeys();
    const [grantRaw, identityRaw] = await Promise.all([
      SecureStore.getItemAsync(keys.grant, sessionStoreOptions),
      SecureStore.getItemAsync(keys.identity, sessionStoreOptions),
    ]);

    const grant = parseGrant(grantRaw) ?? (await adoptPredecessorGrant());
    if (!grant) {
      return null;
    }

    // Identity may have been copied onto the scoped key during adopt, after
    // the first parallel read. Re-read the scoped key before falling back.
    const identity =
      parseIdentityShell(identityRaw) ??
      parseIdentityShell(await SecureStore.getItemAsync(keys.identity, sessionStoreOptions)) ??
      parseIdentityShell(
        await SecureStore.getItemAsync(predecessorKeys.unscopedIdentity, sessionStoreOptions),
      );

    return {
      accessToken: grant.accessToken,
      refreshToken: grant.refreshToken,
      expiresIn: 900,
      expiresAt: grant.expiresAt,
      identity,
    };
  } catch (error) {
    rethrowKeychainError(error);
  }
}

export async function writeStoredSession(tokens: AuthTokens): Promise<StoredSession> {
  const expiresAt = Date.now() + Math.max(tokens.expiresIn - 30, 30) * 1000;
  const grant: StoredGrant = { accessToken: tokens.accessToken, refreshToken: tokens.refreshToken, expiresAt };
  try {
    await writeSessionItem(scopedKeys().grant, JSON.stringify(grant));
  } catch (error) {
    rethrowKeychainError(error);
  }
  // The scoped grant is authoritative now; an orphaned predecessor must not be
  // picked up later by a build pointed at a different origin.
  await clearPredecessorKeys();
  return { ...tokens, expiresAt };
}

export async function writeStoredIdentityShell(shell: StoredIdentityShell): Promise<void> {
  const payload: StoredIdentityShell = {
    displayName: shell.displayName,
    memberId: shell.memberId,
  };
  if (shell.avatarPath) {
    payload.avatarPath = shell.avatarPath;
  }
  try {
    await writeSessionItem(scopedKeys().identity, JSON.stringify(payload));
  } catch (error) {
    rethrowKeychainError(error);
  }
}

async function clearPredecessorKeys(): Promise<void> {
  const keys = scopedKeys();
  try {
    await Promise.all(
      [
        keys.access,
        keys.refresh,
        keys.expiry,
        predecessorKeys.unscopedGrant,
        predecessorKeys.unscopedAccess,
        predecessorKeys.unscopedRefresh,
        predecessorKeys.unscopedExpiry,
        predecessorKeys.unscopedIdentity,
      ].map((key) => SecureStore.deleteItemAsync(key, sessionStoreOptions)),
    );
  } catch {
    // Nothing to recover: the scoped grant is what gets read.
  }
}

export async function clearStoredSession(): Promise<void> {
  const keys = scopedKeys();
  await Promise.all([
    SecureStore.deleteItemAsync(keys.grant, sessionStoreOptions),
    SecureStore.deleteItemAsync(keys.identity, sessionStoreOptions),
    clearPredecessorKeys(),
  ]);
}

function parseIdentityShell(raw: string | null): StoredIdentityShell | null {
  if (!raw) {
    return null;
  }

  try {
    const parsed: unknown = JSON.parse(raw);
    if (!parsed || typeof parsed !== 'object') {
      return null;
    }

    const rec = parsed as Record<string, unknown>;
    if (typeof rec.displayName !== 'string' || rec.displayName.trim().length === 0) {
      return null;
    }
    if (typeof rec.memberId !== 'string' || rec.memberId.trim().length === 0) {
      return null;
    }

    return {
      displayName: rec.displayName,
      memberId: rec.memberId,
      avatarPath: typeof rec.avatarPath === 'string' ? rec.avatarPath : null,
    };
  } catch {
    return null;
  }
}
