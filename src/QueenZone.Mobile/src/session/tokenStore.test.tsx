import * as SecureStore from 'expo-secure-store';
import {
  clearStoredSession,
  isKeychainLockedError,
  KeychainLockedError,
  readStoredSession,
  writeStoredIdentityShell,
  writeStoredSession,
} from './tokenStore';

const mockMemory = new Map<string, string>();

const sessionStoreOptions = {
  keychainAccessible: SecureStore.AFTER_FIRST_UNLOCK,
};

const productionBaseUrl = 'https://www.queenzone.org';
const stagingBaseUrl = 'https://dev.queenzone.org';
let mockApiBaseUrl = productionBaseUrl;

jest.mock('../config/appConfig', () => ({
  getAppConfig: () => ({ appEnv: 'production', apiBaseUrl: mockApiBaseUrl, version: '0.1.0' }),
}));

/** Scoped key for the build currently under test. */
function key(name: string, baseUrl = mockApiBaseUrl): string {
  const scope = baseUrl.replace(/^https?:\/\//i, '').replace(/[^\w.-]+/g, '_');
  return `queenzone.mobile.${scope}.${name}`;
}

const legacyKey = (name: string) => `queenzone.mobile.${name}`;

jest.mock('expo-secure-store', () => ({
  AFTER_FIRST_UNLOCK: 'AFTER_FIRST_UNLOCK',
  getItemAsync: jest.fn(async (key: string) => mockMemory.get(key) ?? null),
  setItemAsync: jest.fn(async (key: string, value: string) => {
    mockMemory.set(key, value);
  }),
  deleteItemAsync: jest.fn(async (key: string) => {
    mockMemory.delete(key);
  }),
}));

beforeEach(() => {
  mockApiBaseUrl = productionBaseUrl;
  mockMemory.clear();
  (SecureStore.getItemAsync as jest.Mock).mockReset();
  (SecureStore.setItemAsync as jest.Mock).mockReset();
  (SecureStore.deleteItemAsync as jest.Mock).mockReset();
  (SecureStore.getItemAsync as jest.Mock).mockImplementation(async (key: string) => mockMemory.get(key) ?? null);
  (SecureStore.setItemAsync as jest.Mock).mockImplementation(async (key: string, value: string) => {
    mockMemory.set(key, value);
  });
  (SecureStore.deleteItemAsync as jest.Mock).mockImplementation(async (key: string) => {
    mockMemory.delete(key);
  });
});

function keychainLockedError(): Error {
  return Object.assign(new Error('User interaction is not allowed'), { name: 'KeyChainException' });
}

describe('tokenStore', () => {
  it('writes and reads a stored session', async () => {
    const stored = await writeStoredSession({
      accessToken: 'a',
      refreshToken: 'r',
      expiresIn: 900,
    });
    expect(stored.accessToken).toBe('a');
    expect(stored.refreshToken).toBe('r');
    expect(stored.expiresAt).toBeGreaterThan(Date.now());

    const roundTrip = await readStoredSession();
    expect(roundTrip?.accessToken).toBe('a');
    expect(roundTrip?.refreshToken).toBe('r');
    expect(roundTrip?.expiresAt).toBe(stored.expiresAt);
    expect(SecureStore.setItemAsync).toHaveBeenCalledWith(
      key('grant'),
      JSON.stringify({ accessToken: 'a', refreshToken: 'r', expiresAt: stored.expiresAt }),
      sessionStoreOptions,
    );
  });

  it('passes AFTER_FIRST_UNLOCK on every get, set, and delete', async () => {
    await writeStoredSession({ accessToken: 'a', refreshToken: 'r', expiresIn: 900 });
    await writeStoredIdentityShell({ displayName: 'Freddie', memberId: 'member-1' });
    await readStoredSession();
    await clearStoredSession();

    for (const call of (SecureStore.getItemAsync as jest.Mock).mock.calls) {
      expect(call[1]).toEqual(sessionStoreOptions);
    }
    for (const call of (SecureStore.setItemAsync as jest.Mock).mock.calls) {
      expect(call[2]).toEqual(sessionStoreOptions);
    }
    for (const call of (SecureStore.deleteItemAsync as jest.Mock).mock.calls) {
      expect(call[1]).toEqual(sessionStoreOptions);
    }
  });

  it('deletes each session item before setting it so accessibility can migrate', async () => {
    const order: string[] = [];
    (SecureStore.deleteItemAsync as jest.Mock).mockImplementation(async (key: string) => {
      order.push(`delete:${key}`);
      mockMemory.delete(key);
    });
    (SecureStore.setItemAsync as jest.Mock).mockImplementation(async (key: string, value: string) => {
      order.push(`set:${key}`);
      mockMemory.set(key, value);
    });

    await writeStoredSession({ accessToken: 'a', refreshToken: 'r', expiresIn: 900 });
    const grantKey = key('grant');
    expect(order.filter((entry) => entry.endsWith(`:${grantKey}`))).toEqual([
      `delete:${grantKey}`,
      `set:${grantKey}`,
    ]);

    order.length = 0;
    await writeStoredIdentityShell({ displayName: 'Freddie', memberId: 'member-1' });
    expect(order).toEqual([`delete:${key('identityShell')}`, `set:${key('identityShell')}`]);
  });

  it('returns null when there is no grant', async () => {
    await expect(readStoredSession()).resolves.toBeNull();
  });

  it('returns null when a leftover per-field key is missing its pair', async () => {
    await SecureStore.setItemAsync(key('accessToken'), 'a');
    await expect(readStoredSession()).resolves.toBeNull();
  });

  it('never leaves an access token without a refresh token when the write is interrupted mid-flight', async () => {
    await writeStoredSession({ accessToken: 'old-a', refreshToken: 'old-r', expiresIn: 900 });

    (SecureStore.setItemAsync as jest.Mock).mockImplementationOnce(async () => {
      throw new Error('process killed mid-write');
    });
    await expect(
      writeStoredSession({ accessToken: 'new-a', refreshToken: 'new-r', expiresIn: 900 }),
    ).rejects.toThrow('process killed mid-write');

    // The delete half of the torn write already ran, so the grant is gone entirely —
    // never left with a new access token and the old (or no) refresh token.
    await expect(readStoredSession()).resolves.toBeNull();
  });

  it('migrates a legacy per-field grant to the scoped combined key without signing the member out', async () => {
    await SecureStore.setItemAsync(legacyKey('accessToken'), 'legacy-a');
    await SecureStore.setItemAsync(legacyKey('refreshToken'), 'legacy-r');
    await SecureStore.setItemAsync(legacyKey('accessExpiresAt'), '12345');

    const stored = await readStoredSession();
    expect(stored?.accessToken).toBe('legacy-a');
    expect(stored?.refreshToken).toBe('legacy-r');
    expect(stored?.expiresAt).toBe(12345);

    expect(mockMemory.has(legacyKey('accessToken'))).toBe(false);
    expect(mockMemory.has(legacyKey('refreshToken'))).toBe(false);
    expect(mockMemory.has(legacyKey('accessExpiresAt'))).toBe(false);
    expect(mockMemory.get(key('grant'))).toBe(
      JSON.stringify({ accessToken: 'legacy-a', refreshToken: 'legacy-r', expiresAt: 12345 }),
    );

    const again = await readStoredSession();
    expect(again?.accessToken).toBe('legacy-a');
  });

  it('does not migrate a legacy grant missing either field', async () => {
    await SecureStore.setItemAsync(legacyKey('accessToken'), 'legacy-a');
    await expect(readStoredSession()).resolves.toBeNull();
    expect(mockMemory.has(key('grant'))).toBe(false);
  });

  it('clears stored tokens', async () => {
    await writeStoredSession({ accessToken: 'a', refreshToken: 'r', expiresIn: 900 });
    await clearStoredSession();
    await expect(readStoredSession()).resolves.toBeNull();
  });

  it('surfaces SecureStore failures instead of swallowing them', async () => {
    (SecureStore.getItemAsync as jest.Mock).mockRejectedValueOnce(new Error('secure-store unavailable'));
    await expect(readStoredSession()).rejects.toThrow('secure-store unavailable');
  });

  it('does not reject interaction-not-allowed as a raw Keychain throw', async () => {
    const raw = keychainLockedError();
    (SecureStore.getItemAsync as jest.Mock).mockRejectedValueOnce(raw);
    const rejected = readStoredSession();
    await expect(rejected).rejects.toBeInstanceOf(KeychainLockedError);
    await expect(rejected).rejects.not.toBe(raw);
    await expect(rejected).rejects.toMatchObject({ name: 'KeychainLockedError' });
  });

  it('does not treat a locked keychain as a missing session', async () => {
    (SecureStore.getItemAsync as jest.Mock).mockRejectedValueOnce(keychainLockedError());
    let thrown: unknown;
    try {
      await readStoredSession();
    } catch (error) {
      thrown = error;
    }
    expect(thrown).toBeDefined();
    expect(thrown).not.toBeNull();
    expect(isKeychainLockedError(thrown)).toBe(true);
  });

  it('surfaces a locked write as isKeychainLockedError instead of a raw Keychain throw', async () => {
    const raw = keychainLockedError();
    (SecureStore.deleteItemAsync as jest.Mock).mockRejectedValueOnce(raw);
    await expect(
      writeStoredSession({ accessToken: 'a', refreshToken: 'r', expiresIn: 900 }),
    ).rejects.toBeInstanceOf(KeychainLockedError);
  });

  it('identifies the QUEENZONE-MOBILE-3 Keychain shape', () => {
    const sentryShape = new Error(
      'getValueWithKeyAsync failed with KeyChainException: User interaction is not allowed',
    );
    expect(isKeychainLockedError(sentryShape)).toBe(true);
    expect(isKeychainLockedError(keychainLockedError())).toBe(true);
    expect(isKeychainLockedError(new KeychainLockedError())).toBe(true);
    expect(isKeychainLockedError(new Error('secure-store unavailable'))).toBe(false);
    expect(isKeychainLockedError('interaction-not-allowed')).toBe(true);
  });

  it('writes and reads a non-secret identity shell next to the grant', async () => {
    await writeStoredSession({ accessToken: 'a', refreshToken: 'r', expiresIn: 900 });
    await writeStoredIdentityShell({
      displayName: 'Freddie',
      memberId: 'member-1',
      avatarPath: '/avatars/1.jpg',
    });

    const stored = await readStoredSession();
    expect(stored?.refreshToken).toBe('r');
    expect(stored?.identity).toEqual({
      displayName: 'Freddie',
      memberId: 'member-1',
      avatarPath: '/avatars/1.jpg',
    });
    expect(SecureStore.setItemAsync).toHaveBeenCalledWith(
      key('identityShell'),
      JSON.stringify({
        displayName: 'Freddie',
        memberId: 'member-1',
        avatarPath: '/avatars/1.jpg',
      }),
      sessionStoreOptions,
    );
    const persisted = mockMemory.get(key('identityShell'));
    expect(persisted).toBeTruthy();
    expect(persisted).not.toContain('email');
    expect(persisted).not.toContain('@');
  });

  it('clears the identity shell with the grant', async () => {
    await writeStoredSession({ accessToken: 'a', refreshToken: 'r', expiresIn: 900 });
    await writeStoredIdentityShell({ displayName: 'Freddie', memberId: 'member-1' });
    await clearStoredSession();
    await expect(readStoredSession()).resolves.toBeNull();
    await expect(SecureStore.getItemAsync(key('identityShell'))).resolves.toBeNull();
  });

  it('ignores a malformed identity shell without dropping the grant', async () => {
    await writeStoredSession({ accessToken: 'a', refreshToken: 'r', expiresIn: 900 });
    await SecureStore.setItemAsync(key('identityShell'), '{not-json');
    const stored = await readStoredSession();
    expect(stored?.accessToken).toBe('a');
    expect(stored?.identity).toBeNull();
  });

  it('keeps the grant and identity shell after a simulated app version bump', async () => {
    await writeStoredSession({ accessToken: 'a', refreshToken: 'r', expiresIn: 900 });
    await writeStoredIdentityShell({ displayName: 'Freddie', memberId: 'member-1' });

    const previousVersion = '0.1.0';
    const nextVersion = '0.1.214';
    expect(previousVersion).not.toBe(nextVersion);
    expect(mockMemory.has(key('grant'))).toBe(true);
    expect(mockMemory.has(`${key('grant')}.${nextVersion}`)).toBe(false);
    expect(mockMemory.has(`${key('identityShell')}.${nextVersion}`)).toBe(false);

    const stored = await readStoredSession();
    expect(stored?.refreshToken).toBe('r');
    expect(stored?.identity?.displayName).toBe('Freddie');
    expect(stored?.identity?.memberId).toBe('member-1');
  });

  describe('api origin scope', () => {
    it('does not hand a staging grant to a production build', async () => {
      // TestFlight ships both under org.queenzone.mobile, so an unscoped key let
      // a dev.queenzone.org refresh token reach www.queenzone.org and come back
      // invalid_grant — a silent sign-out on the next launch.
      mockApiBaseUrl = stagingBaseUrl;
      await writeStoredSession({ accessToken: 'staging-a', refreshToken: 'staging-r', expiresIn: 900 });
      await writeStoredIdentityShell({ displayName: 'Freddie', memberId: 'member-1' });

      mockApiBaseUrl = productionBaseUrl;
      await expect(readStoredSession()).resolves.toBeNull();
    });

    it('keeps both sessions so switching builds back restores the original', async () => {
      mockApiBaseUrl = stagingBaseUrl;
      await writeStoredSession({ accessToken: 'staging-a', refreshToken: 'staging-r', expiresIn: 900 });

      mockApiBaseUrl = productionBaseUrl;
      await writeStoredSession({ accessToken: 'prod-a', refreshToken: 'prod-r', expiresIn: 900 });
      await expect(readStoredSession()).resolves.toMatchObject({ refreshToken: 'prod-r' });

      mockApiBaseUrl = stagingBaseUrl;
      await expect(readStoredSession()).resolves.toMatchObject({ refreshToken: 'staging-r' });
    });

    it('signs out only the scope that cleared', async () => {
      mockApiBaseUrl = stagingBaseUrl;
      await writeStoredSession({ accessToken: 'staging-a', refreshToken: 'staging-r', expiresIn: 900 });
      mockApiBaseUrl = productionBaseUrl;
      await writeStoredSession({ accessToken: 'prod-a', refreshToken: 'prod-r', expiresIn: 900 });

      await clearStoredSession();
      await expect(readStoredSession()).resolves.toBeNull();

      mockApiBaseUrl = stagingBaseUrl;
      await expect(readStoredSession()).resolves.toMatchObject({ refreshToken: 'staging-r' });
    });
  });

  describe('legacy unscoped grant', () => {
    function seedLegacy(): void {
      mockMemory.set(legacyKey('accessToken'), 'legacy-a');
      mockMemory.set(legacyKey('refreshToken'), 'legacy-r');
      mockMemory.set(legacyKey('accessExpiresAt'), String(Date.now() + 60_000));
      mockMemory.set(
        legacyKey('identityShell'),
        JSON.stringify({ displayName: 'Freddie', memberId: 'member-1' }),
      );
    }

    it('adopts a pre-scope grant into the running build so nobody is signed out once', async () => {
      seedLegacy();

      const stored = await readStoredSession();
      expect(stored?.accessToken).toBe('legacy-a');
      expect(stored?.refreshToken).toBe('legacy-r');
      expect(stored?.identity?.displayName).toBe('Freddie');

      expect(JSON.parse(mockMemory.get(key('grant')) ?? '{}')).toMatchObject({ refreshToken: 'legacy-r' });
      expect(mockMemory.has(legacyKey('refreshToken'))).toBe(false);

      await expect(readStoredSession()).resolves.toMatchObject({ refreshToken: 'legacy-r' });
    });

    it('prefers the scoped grant and drops an orphaned legacy one on write', async () => {
      seedLegacy();
      await writeStoredSession({ accessToken: 'prod-a', refreshToken: 'prod-r', expiresIn: 900 });

      expect(mockMemory.has(legacyKey('refreshToken'))).toBe(false);
      await expect(readStoredSession()).resolves.toMatchObject({ refreshToken: 'prod-r' });
    });

    it('clears the legacy copy alongside the scoped one', async () => {
      seedLegacy();
      await clearStoredSession();
      expect(mockMemory.has(legacyKey('refreshToken'))).toBe(false);
      await expect(readStoredSession()).resolves.toBeNull();
    });
  });

  describe('predecessor grant shapes', () => {
    it('migrates a #1491 origin-scoped per-field grant to the atomic key', async () => {
      await SecureStore.setItemAsync(key('accessToken'), 'scoped-a');
      await SecureStore.setItemAsync(key('refreshToken'), 'scoped-r');
      await SecureStore.setItemAsync(key('accessExpiresAt'), '67890');
      await SecureStore.setItemAsync(
        key('identityShell'),
        JSON.stringify({ displayName: 'Freddie', memberId: 'member-1' }),
      );

      const stored = await readStoredSession();
      expect(stored?.accessToken).toBe('scoped-a');
      expect(stored?.refreshToken).toBe('scoped-r');
      expect(stored?.expiresAt).toBe(67890);
      expect(stored?.identity?.displayName).toBe('Freddie');

      expect(mockMemory.get(key('grant'))).toBe(
        JSON.stringify({ accessToken: 'scoped-a', refreshToken: 'scoped-r', expiresAt: 67890 }),
      );
      expect(mockMemory.has(key('accessToken'))).toBe(false);
      expect(mockMemory.has(key('refreshToken'))).toBe(false);
      expect(mockMemory.has(key('accessExpiresAt'))).toBe(false);
    });

    it('adopts an unscoped atomic grant into the running origin so nobody is signed out', async () => {
      mockMemory.set(
        legacyKey('grant'),
        JSON.stringify({ accessToken: 'atomic-a', refreshToken: 'atomic-r', expiresAt: 111 }),
      );

      const stored = await readStoredSession();
      expect(stored?.refreshToken).toBe('atomic-r');
      expect(mockMemory.get(key('grant'))).toBe(
        JSON.stringify({ accessToken: 'atomic-a', refreshToken: 'atomic-r', expiresAt: 111 }),
      );
      expect(mockMemory.has(legacyKey('grant'))).toBe(false);
    });

    it('does not migrate a staging scoped per-field grant into a production build', async () => {
      mockApiBaseUrl = stagingBaseUrl;
      await SecureStore.setItemAsync(key('accessToken'), 'staging-a');
      await SecureStore.setItemAsync(key('refreshToken'), 'staging-r');

      mockApiBaseUrl = productionBaseUrl;
      await expect(readStoredSession()).resolves.toBeNull();
      expect(mockMemory.has(key('grant', stagingBaseUrl))).toBe(false);
      expect(mockMemory.get(key('refreshToken', stagingBaseUrl))).toBe('staging-r');
    });
  });
});
