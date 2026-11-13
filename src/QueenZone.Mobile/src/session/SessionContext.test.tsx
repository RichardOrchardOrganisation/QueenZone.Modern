import { useEffect } from 'react';
import { Alert, AppState, Linking, Text } from 'react-native';
import * as Notifications from 'expo-notifications';
import { act, screen, waitFor, userEvent } from '@testing-library/react-native';
import { ApiError, fetchJson } from '../api/client';
import { authTokensFixture, memberProfilePayload } from '../test/fixtures';
import { renderWithProviders } from '../test/render';
import { SessionProvider, useSession, useSessionActions, type SessionActions, type Session } from './SessionContext';
import * as oauth from './oauth';
import * as tokenStore from './tokenStore';
import * as notifications from '../notifications';
import { setDownloadManifestStorageForTests } from '../downloads/manifest';
import * as offlineQueue from '../offlineQueue/store';
import { setContentCacheForTests } from '../cache';

const mockAppConfig = {
  apiBaseUrl: 'http://qz.test',
  appEnv: 'development' as string,
  version: '0.1.0',
  smokeEmbed: false as boolean | undefined,
};

jest.mock('../config/appConfig', () => ({
  getAppConfig: () => mockAppConfig,
}));

jest.mock('expo-network', () => ({
  addNetworkStateListener: jest.fn(() => ({ remove: jest.fn() })),
}));

jest.mock('../api/client', () => {
  const actual = jest.requireActual('../api/client') as typeof import('../api/client');
  return {
    ...actual,
    fetchJson: jest.fn(),
  };
});

jest.mock('./oauth', () => ({
  signInWithProvider: jest.fn(),
  signInWithPassword: jest.fn(),
  refreshAccessToken: jest.fn(),
  revokeRefreshToken: jest.fn(),
  logoutRemote: jest.fn(),
}));

jest.mock('./tokenStore', () => {
  const actual = jest.requireActual('./tokenStore') as typeof import('./tokenStore');
  return {
    readStoredSession: jest.fn(),
    writeStoredSession: jest.fn(async (tokens: { accessToken: string; refreshToken: string; expiresIn: number }) => ({
      ...tokens,
      expiresAt: Date.now() + 60_000,
    })),
    writeStoredIdentityShell: jest.fn(async () => { }),
    clearStoredSession: jest.fn(async () => { }),
    isKeychainLockedError: actual.isKeychainLockedError,
  };
});

jest.mock('../notifications', () => ({
  syncPushRegistration: jest.fn(async () => { }),
  refreshPushRegistration: jest.fn(async () => { }),
  clearPushRegistration: jest.fn(async () => { }),
}));

const fetchJsonMock = fetchJson as jest.MockedFunction<typeof fetchJson>;
const readStored = tokenStore.readStoredSession as jest.MockedFunction<typeof tokenStore.readStoredSession>;
const writeStored = tokenStore.writeStoredSession as jest.MockedFunction<typeof tokenStore.writeStoredSession>;
const writeIdentity = tokenStore.writeStoredIdentityShell as jest.MockedFunction<
  typeof tokenStore.writeStoredIdentityShell
>;
const clearStored = tokenStore.clearStoredSession as jest.MockedFunction<typeof tokenStore.clearStoredSession>;
const signInWithProvider = oauth.signInWithProvider as jest.MockedFunction<typeof oauth.signInWithProvider>;
const signInWithPassword = oauth.signInWithPassword as jest.MockedFunction<typeof oauth.signInWithPassword>;
const refreshAccessToken = oauth.refreshAccessToken as jest.MockedFunction<typeof oauth.refreshAccessToken>;
const logoutRemote = oauth.logoutRemote as jest.MockedFunction<typeof oauth.logoutRemote>;
const revokeRefreshToken = oauth.revokeRefreshToken as jest.MockedFunction<typeof oauth.revokeRefreshToken>;
const syncPushRegistration = notifications.syncPushRegistration as jest.MockedFunction<
  typeof notifications.syncPushRegistration
>;
const clearPushRegistration = notifications.clearPushRegistration as jest.MockedFunction<
  typeof notifications.clearPushRegistration
>;

let capturedActions: SessionActions & Session;

function Probe() {
  const session = useSession();
  useEffect(() => { capturedActions = session; }, [session]);
  return (
    <>
      <Text>{session.isRestoring ? 'restoring' : session.isSignedIn ? 'signed-in' : 'signed-out'}</Text>
      <Text>{session.displayName ?? 'anonymous'}</Text>
      <Text>{session.accessToken ?? 'no-token'}</Text>
      <Text onPress={() => { void session.ensureAccessToken(); }}>do-ensure-token</Text>
    </>
  );
}

async function renderSession() {
  return await renderWithProviders(
    <SessionProvider>
      <Probe />
    </SessionProvider>,
    { navigation: false },
  );
}

// The react-native jest preset mocks `AppState.currentState` as a function, so
// `jest.replaceProperty` refuses it. Redefine it per test and put it back after.
const originalAppStateDescriptor = Object.getOwnPropertyDescriptor(AppState, 'currentState');


afterEach(() => {
  setDownloadManifestStorageForTests(null);
  offlineQueue.setOfflineQueueStorageForTests(null);
  setContentCacheForTests(null);
  if (originalAppStateDescriptor) {
    Object.defineProperty(AppState, 'currentState', originalAppStateDescriptor);
  }
});

beforeEach(() => {
  // `restoreMocks: true` (jest.config.js) restores a spied `jest.fn()` to a bare
  // stub that returns `undefined`, not to the `{ remove: jest.fn() }}`-returning
  // factory implementation the react-native jest preset ships. Re-establish that
  // default every test so SessionProvider's `AppState.addEventListener(...)`
  // cleanup (`subscription.remove()`) never runs against `undefined`, regardless
  // of whether an earlier test in this file spied on and restored the mock.
  jest.spyOn(AppState, 'addEventListener').mockImplementation(() => ({ remove: jest.fn() }));
  mockAppConfig.appEnv = 'development';
  mockAppConfig.version = '0.1.0';
  mockAppConfig.smokeEmbed = false;
  fetchJsonMock.mockReset();
  readStored.mockReset();
  writeStored.mockReset();
  writeStored.mockImplementation(async (tokens) => ({
    ...tokens,
    expiresAt: Date.now() + 60_000,
  }));
  writeIdentity.mockReset();
  writeIdentity.mockResolvedValue(undefined);
  clearStored.mockReset();
  signInWithProvider.mockReset();
  signInWithPassword.mockReset();
  refreshAccessToken.mockReset();
  logoutRemote.mockReset();
  revokeRefreshToken.mockReset();
  syncPushRegistration.mockReset();
  clearPushRegistration.mockReset();
  fetchJsonMock.mockResolvedValue(memberProfilePayload());
  logoutRemote.mockResolvedValue(undefined);
  revokeRefreshToken.mockResolvedValue(undefined);
  syncPushRegistration.mockResolvedValue(undefined);
  clearPushRegistration.mockResolvedValue(undefined);
});

describe('SessionProvider wiring', () => {
  it('does not clear a stored grant when the app version changes', async () => {
    const stored = {
      ...authTokensFixture(),
      expiresAt: Date.now() + 60_000,
      identity: { displayName: 'Freddie', memberId: 'member-1' as const },
    };
    readStored.mockResolvedValue(stored);
    mockAppConfig.version = '0.1.0';
    const first = await renderSession();
    await waitFor(() => expect(screen.getByText('Freddie')).toBeOnTheScreen());
    await first.unmount();

    mockAppConfig.version = '0.1.214';
    await renderSession();
    await waitFor(() => expect(screen.getByText('signed-in')).toBeOnTheScreen());
    expect(screen.getByText('Freddie')).toBeOnTheScreen();
    expect(clearStored).not.toHaveBeenCalled();
  });

  it('re-syncs push with the current member when the native token rotates', async () => {
    readStored.mockResolvedValue({
      ...authTokensFixture(),
      expiresAt: Date.now() + 60_000,
    });
    let onToken: (() => void) | undefined;
    jest.spyOn(Notifications, 'addPushTokenListener').mockImplementation((listener) => {
      onToken = () => listener({ data: 'rotated', type: 'ios' });
      return { remove: jest.fn() };
    });

    await renderSession();
    await waitFor(() =>
      expect(syncPushRegistration).toHaveBeenCalledWith(authTokensFixture().accessToken, 'member-1'),
    );

    syncPushRegistration.mockClear();
    await act(async () => {
      onToken?.();
    });
    expect(syncPushRegistration).toHaveBeenCalledWith(authTokensFixture().accessToken, 'member-1');
  });

  it('keeps actions identity stable across a token refresh and a profile load', async () => {
    const user = userEvent.setup();
    const now = 1_700_000_000_000;
    const dateNow = jest.spyOn(Date, 'now').mockReturnValue(now);
    const recordActionsRender = jest.fn<void, [SessionActions]>();

    function ActionsOnlyChild() {
      const actions = useSessionActions();
      recordActionsRender(actions);
      return <Text>actions-only</Text>;
    }

    let resolveProfile: (value: unknown) => void = () => { };
    fetchJsonMock.mockImplementation(
      () =>
        new Promise((resolve) => {
          resolveProfile = resolve;
        }),
    );
    readStored.mockResolvedValue({
      ...authTokensFixture(),
      expiresAt: now + 60_000,
    });

    try {
      await renderWithProviders(
        <SessionProvider>
          <ActionsOnlyChild />
          <Probe />
        </SessionProvider>,
        { navigation: false },
      );

      await waitFor(() => expect(screen.getByText('signed-in')).toBeOnTheScreen());
      expect(screen.getByText('anonymous')).toBeOnTheScreen();
      expect(screen.getByText('actions-only')).toBeOnTheScreen();
      const rendersAfterToken = recordActionsRender.mock.calls.length;
      const actionsAfterToken = recordActionsRender.mock.lastCall?.[0];
      expect(rendersAfterToken).toBeGreaterThan(0);
      expect(actionsAfterToken).toBeDefined();

      fetchJsonMock.mockResolvedValue(memberProfilePayload());
      await act(async () => {
        resolveProfile(memberProfilePayload());
      });
      await waitFor(() => expect(screen.getByText('Freddie')).toBeOnTheScreen());
      expect(recordActionsRender).toHaveBeenCalledTimes(rendersAfterToken);
      expect(recordActionsRender.mock.lastCall?.[0]).toBe(actionsAfterToken);

      refreshAccessToken.mockResolvedValue(authTokensFixture({ accessToken: 'access-token' }));
      dateNow.mockReturnValue(now + 120_000);
      await user.press(screen.getByText('do-ensure-token'));
      await waitFor(() => expect(refreshAccessToken).toHaveBeenCalledWith('http://qz.test', 'refresh-token'));
      expect(screen.getByText('signed-in')).toBeOnTheScreen();
      expect(screen.getByText('access-token')).toBeOnTheScreen();
      expect(recordActionsRender).toHaveBeenCalledTimes(rendersAfterToken);
      expect(recordActionsRender.mock.lastCall?.[0]).toBe(actionsAfterToken);
    } finally {
      dateNow.mockRestore();
    }
  });

  it('maps restoring, signed-out and signed-in snapshots onto the public hook', async () => {
    readStored.mockResolvedValue(null);
    await renderSession();
    await waitFor(() => expect(capturedActions.isRestoring).toBe(false));
    expect(capturedActions.isSignedIn).toBe(false);
    await act(async () => { capturedActions.setAccessToken(' manual-token '); });
    expect(capturedActions.accessToken).toBe('manual-token');
    expect(capturedActions.isSignedIn).toBe(true);
    expect(capturedActions.displayName).toBeNull();
    expect(capturedActions.profile).toBeNull();
    await act(async () => { capturedActions.setAccessToken(null); });
    expect(capturedActions.isSignedIn).toBe(false);
  });

  it('shows cleanupFailed with the existing copy and delegates Retry', async () => {
    readStored.mockResolvedValue({ ...authTokensFixture(), expiresAt: Date.now() + 60_000 });
    clearStored.mockRejectedValueOnce(new Error('private storage error')).mockResolvedValue(undefined);
    const alert = jest.spyOn(Alert, 'alert');
    await renderSession();
    await waitFor(() => expect(capturedActions.displayName).toBe('Freddie'));
    await act(async () => { await capturedActions.signOut(); });
    await waitFor(() => expect(alert).toHaveBeenCalled());
    expect(alert.mock.lastCall?.[0]).toBe('Sign-out cleanup incomplete');
    expect(alert.mock.lastCall?.[1]).toBe('You are signed out in this app. Saved sign-in or offline data may remain on this device. Retry cleanup before closing the app or signing in again. A restart could restore a session or pending sends.');
    expect(JSON.stringify(alert.mock.calls)).not.toContain('private storage error');
    await act(async () => { alert.mock.lastCall?.[2]?.find(button => button.text === 'Retry cleanup')?.onPress?.(); });
    expect(clearStored).toHaveBeenCalledTimes(2);
  });

  it.each([false, true])('maps pending-send confirmation and cancellation (inspectionFailed=%s)', async inspectionFailed => {
    readStored.mockResolvedValue({ ...authTokensFixture(), expiresAt: Date.now() + 60_000 });
    const count = jest.spyOn(offlineQueue, 'countPendingOfflineItems');
    if (inspectionFailed) count.mockRejectedValue(new Error('inspection failed'));
    else count.mockResolvedValue(2);
    const discard = jest.fn(async () => { });
    jest.spyOn(offlineQueue, 'prepareOfflineQueueDiscard').mockReturnValue(discard);
    const alert = jest.spyOn(Alert, 'alert');
    await renderSession();
    await waitFor(() => expect(capturedActions.displayName).toBe('Freddie'));
    for (const choice of ['Cancel', 'dismiss', 'Sign out']) {
      let pending!: Promise<void>;
      await act(async () => { pending = capturedActions.signOut(); });
      expect(alert.mock.lastCall?.[0]).toBe(inspectionFailed ? 'Unable to check pending sends' : 'Discard pending sends?');
      expect(alert.mock.lastCall?.[1]).toBe(inspectionFailed
        ? 'Pending messages, replies and crossword saves could not be checked. Sign out and discard pending changes when storage is available?'
        : 'Messages, replies and crossword saves waiting to sync will be deleted.');
      expect(capturedActions.isSignedIn).toBe(true);
      await act(async () => {
        if (choice === 'dismiss') alert.mock.lastCall?.[3]?.onDismiss?.();
        else alert.mock.lastCall?.[2]?.find(button => button.text === choice)?.onPress?.();
        await pending;
      });
      expect(capturedActions.isSignedIn).toBe(choice !== 'Sign out');
    }
    expect(discard).toHaveBeenCalledTimes(1);
  });

  it('delegates a smoke-auth link to the manager', async () => {
    readStored.mockResolvedValue(null);
    let onUrl: ((event: { url: string }) => void) | undefined;
    jest.spyOn(Linking, 'addEventListener').mockImplementation((_type, listener) => {
      onUrl = listener;
      return { remove: jest.fn() };
    });
    await renderSession();
    await waitFor(() => expect(capturedActions.isRestoring).toBe(false));
    await act(async () => { onUrl?.({ url: 'queenzone://smoke-auth?accessToken=linked-smoke' }); });
    expect(capturedActions.accessToken).toBe('linked-smoke');
    expect(writeStored).toHaveBeenCalledWith(expect.objectContaining({ accessToken: 'linked-smoke' }), true);
  });

  it('returns null for non-401 profile errors at the platform port', async () => {
    readStored.mockResolvedValue({ ...authTokensFixture(), expiresAt: Date.now() + 60_000 });
    fetchJsonMock.mockRejectedValue(new TypeError('offline'));
    await renderSession();
    await waitFor(() => expect(capturedActions.isRestoring).toBe(false));
    expect(capturedActions.isSignedIn).toBe(true);
    expect(capturedActions.profile).toBeNull();
  });

  it('preserves a 401 from the profile port for manager recovery', async () => {
    readStored.mockResolvedValue({ ...authTokensFixture(), expiresAt: Date.now() + 60_000 });
    fetchJsonMock.mockRejectedValueOnce(ApiError.http(401, 'Unauthorized')).mockResolvedValue(memberProfilePayload());
    refreshAccessToken.mockResolvedValue(authTokensFixture({ accessToken: 'recovered' }));
    await renderSession();
    await waitFor(() => expect(capturedActions.accessToken).toBe('recovered'));
    expect(refreshAccessToken).toHaveBeenCalledTimes(1);
  });
});
