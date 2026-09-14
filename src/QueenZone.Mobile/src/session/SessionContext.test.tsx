import { useState } from 'react';
import { AppState, Text } from 'react-native';
import * as Notifications from 'expo-notifications';
import { act, screen, waitFor, userEvent } from '@testing-library/react-native';
import { ApiError, fetchJson } from '../api/client';
import { TokenEndpointError } from '../api/errors';
import { authTokensFixture, deferred, memberProfilePayload } from '../test/fixtures';
import { initials } from '../ui/initials';
import { renderWithProviders } from '../test/render';
import { SessionProvider, useSession, useSessionActions, type SessionActions } from './SessionContext';
import * as oauth from './oauth';
import * as tokenStore from './tokenStore';
import * as notifications from '../notifications';
import {
  ContentCache,
  conversationCacheKey,
  createMemoryStorage,
  forumTopicCacheKey,
  setContentCacheForTests,
} from '../cache';

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
    writeStoredIdentityShell: jest.fn(async () => {}),
    clearStoredSession: jest.fn(async () => {}),
    isKeychainLockedError: actual.isKeychainLockedError,
  };
});

jest.mock('../notifications', () => ({
  syncPushRegistration: jest.fn(async () => {}),
  refreshPushRegistration: jest.fn(async () => {}),
  clearPushRegistration: jest.fn(async () => {}),
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

function Probe() {
  const session = useSession();
  const [smokeResult, setSmokeResult] = useState('smoke-idle');
  return (
    <>
      <Text>{session.isRestoring ? 'restoring' : session.isSignedIn ? 'signed-in' : 'signed-out'}</Text>
      <Text>{session.displayName ?? 'anonymous'}</Text>
      <Text>{session.isSignedIn ? initials(session.displayName) || 'no-initials' : 'signed-out-avatar'}</Text>
      <Text>{session.accessToken ?? 'no-token'}</Text>
      <Text>{smokeResult}</Text>
      <Text
        onPress={() => {
          void session.signIn('Google').catch(() => {});
        }}
      >
        do-sign-in
      </Text>
      <Text
        onPress={() => {
          void session.signInWithPassword('reviewer@example.com', 'review-pass').catch(() => {});
        }}
      >
        do-password-sign-in
      </Text>
      <Text onPress={() => void session.signOut()}>do-sign-out</Text>
      <Text
        onPress={() => {
          void session.applySmokeSession('smoke-access').then((ok) => {
            setSmokeResult(ok ? 'smoke-applied' : 'smoke-rejected');
          });
        }}
      >
        do-smoke-auth
      </Text>
      <Text onPress={() => session.setAccessToken('manual-token')}>set-token</Text>
      <Text onPress={() => session.setAccessToken(null)}>clear-token</Text>
      <Text onPress={() => session.setAccessToken('')}>set-empty-token</Text>
      <Text
        onPress={() => {
          void session.ensureAccessToken();
        }}
      >
        do-ensure-token
      </Text>
      <Text
        onPress={() => {
          void session.refreshProfile().catch(() => {});
        }}
      >
        do-refresh-profile
      </Text>
    </>
  );
}

function renderSession() {
  return renderWithProviders(
    <SessionProvider>
      <Probe />
    </SessionProvider>,
    { navigation: false },
  );
}

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

describe('SessionProvider', () => {
  it('finishes restore with no stored session as signed out', async () => {
    readStored.mockResolvedValue(null);
    renderSession();
    await waitFor(() => expect(screen.getByText('signed-out')).toBeOnTheScreen());
    expect(screen.getByText('no-token')).toBeOnTheScreen();
  });

  it('applies the smoke session when Release smokeEmbed is baked', async () => {
    const user = userEvent.setup();
    const runtime = globalThis as typeof globalThis & { __DEV__?: boolean };
    const previous = runtime.__DEV__;
    runtime.__DEV__ = false;
    mockAppConfig.smokeEmbed = true;
    try {
      readStored.mockReturnValue(new Promise(() => {}));
      renderSession();
      await waitFor(() => expect(screen.getByText('signed-out')).toBeOnTheScreen());
      expect(readStored).not.toHaveBeenCalled();

      await user.press(screen.getByText('do-smoke-auth'));
      await waitFor(() => expect(screen.getByText('signed-in')).toBeOnTheScreen());
      expect(screen.getByText('smoke-applied')).toBeOnTheScreen();
      expect(signInWithProvider).not.toHaveBeenCalled();
      expect(writeStored).not.toHaveBeenCalled();
      expect(writeIdentity).not.toHaveBeenCalled();
      expect(fetchJsonMock).toHaveBeenCalledWith('/me', { accessToken: 'smoke-access' });
    } finally {
      runtime.__DEV__ = previous;
    }
  });

  it('rejects the smoke session when __DEV__ is false', async () => {
    const user = userEvent.setup();
    const runtime = globalThis as typeof globalThis & { __DEV__?: boolean };
    const previous = runtime.__DEV__;
    runtime.__DEV__ = false;
    try {
      readStored.mockResolvedValue(null);
      renderSession();
      await waitFor(() => expect(screen.getByText('signed-out')).toBeOnTheScreen());

      await user.press(screen.getByText('do-smoke-auth'));
      await waitFor(() => expect(screen.getByText('smoke-rejected')).toBeOnTheScreen());
      expect(screen.getByText('signed-out')).toBeOnTheScreen();
      expect(writeStored).not.toHaveBeenCalled();
    } finally {
      runtime.__DEV__ = previous;
    }
  });

  it('restores a valid stored access token and profile', async () => {
    readStored.mockResolvedValue({
      ...authTokensFixture(),
      expiresAt: Date.now() + 60_000,
    });
    renderSession();
    await waitFor(() => expect(screen.getByText('signed-in')).toBeOnTheScreen());
    expect(screen.getByText('Freddie')).toBeOnTheScreen();
    expect(refreshAccessToken).not.toHaveBeenCalled();
  });

  it('refreshes an expired stored token', async () => {
    readStored.mockResolvedValue({
      ...authTokensFixture(),
      expiresAt: Date.now() - 1_000,
    });
    refreshAccessToken.mockResolvedValue(authTokensFixture({ accessToken: 'next' }));
    renderSession();
    await waitFor(() => expect(screen.getByText('signed-in')).toBeOnTheScreen());
    expect(refreshAccessToken).toHaveBeenCalledWith('http://qz.test', 'refresh-token');
  });

  it('applies a cached identity shell before an expired-access refresh resolves', async () => {
    const refresh = deferred<ReturnType<typeof authTokensFixture>>();
    let tokenCalls = 0;
    refreshAccessToken.mockImplementation(async () => {
      tokenCalls += 1;
      if (tokenCalls > 1) {
        throw new Error('invalid_grant');
      }
      return refresh.promise;
    });
    readStored.mockResolvedValue({
      ...authTokensFixture(),
      expiresAt: Date.now() - 1_000,
      identity: { displayName: 'Freddie', memberId: 'member-1' },
    });
    renderSession();

    await waitFor(() => expect(screen.getByText('signed-in')).toBeOnTheScreen());
    expect(screen.getByText('Freddie')).toBeOnTheScreen();
    expect(screen.getByText('FR')).toBeOnTheScreen();
    expect(screen.queryByText('restoring')).toBeNull();
    expect(refreshAccessToken).toHaveBeenCalledTimes(1);
    expect(refreshAccessToken).toHaveBeenCalledWith('http://qz.test', 'refresh-token');
    expect(screen.getByText('access-token')).toBeOnTheScreen();

    await act(async () => {
      refresh.resolve(authTokensFixture({ accessToken: 'next' }));
    });
    await waitFor(() => expect(screen.getByText('next')).toBeOnTheScreen());
    expect(screen.getByText('Freddie')).toBeOnTheScreen();
    expect(screen.getByText('FR')).toBeOnTheScreen();
    expect(screen.getByText('signed-in')).toBeOnTheScreen();
    expect(refreshAccessToken).toHaveBeenCalledTimes(1);
    expect(clearStored).not.toHaveBeenCalled();
  });

  it('single-flights concurrent ensureAccessToken callers onto one /token', async () => {
    const user = userEvent.setup();
    const refresh = deferred<ReturnType<typeof authTokensFixture>>();
    let tokenCalls = 0;
    refreshAccessToken.mockImplementation(async () => {
      tokenCalls += 1;
      if (tokenCalls > 1) {
        throw new Error('invalid_grant');
      }
      return refresh.promise;
    });
    readStored.mockResolvedValue({
      ...authTokensFixture(),
      expiresAt: Date.now() - 1_000,
      identity: { displayName: 'Freddie', memberId: 'member-1' },
    });
    renderSession();

    await waitFor(() => expect(screen.getByText('signed-in')).toBeOnTheScreen());
    expect(screen.getByText('Freddie')).toBeOnTheScreen();
    expect(screen.queryByText('restoring')).toBeNull();

    await user.press(screen.getByText('do-ensure-token'));
    await user.press(screen.getByText('do-ensure-token'));
    expect(refreshAccessToken).toHaveBeenCalledTimes(1);

    await act(async () => {
      refresh.resolve(authTokensFixture({ accessToken: 'next' }));
    });
    await waitFor(() => expect(screen.getByText('next')).toBeOnTheScreen());
    expect(screen.getByText('signed-in')).toBeOnTheScreen();
    expect(screen.getByText('Freddie')).toBeOnTheScreen();
    expect(refreshAccessToken).toHaveBeenCalledTimes(1);
    expect(clearStored).not.toHaveBeenCalled();
  });

  it('clears local state when refresh fails', async () => {
    readStored.mockResolvedValue({
      ...authTokensFixture(),
      expiresAt: Date.now() - 1_000,
      identity: { displayName: 'Freddie', memberId: 'member-1' },
    });
    refreshAccessToken.mockRejectedValue(new Error('expired'));
    renderSession();
    await waitFor(() => expect(screen.getByText('signed-out')).toBeOnTheScreen());
    expect(clearStored).toHaveBeenCalled();
    expect(screen.getByText('anonymous')).toBeOnTheScreen();
    expect(screen.getByText('signed-out-avatar')).toBeOnTheScreen();
  });

  it('keeps local member identity when an expired refresh fails offline', async () => {
    readStored.mockResolvedValue({
      ...authTokensFixture(),
      expiresAt: Date.now() - 1_000,
      identity: { displayName: 'Freddie', memberId: 'member-1' },
    });
    refreshAccessToken.mockRejectedValue(new TypeError('Network request failed'));
    renderSession();
    await waitFor(() => expect(screen.getByText('signed-in')).toBeOnTheScreen());
    expect(clearStored).not.toHaveBeenCalled();
    expect(screen.getByText('Freddie')).toBeOnTheScreen();
  });

  it('clears local state on a definite invalid_grant refresh failure', async () => {
    readStored.mockResolvedValue({
      ...authTokensFixture(),
      expiresAt: Date.now() - 1_000,
      identity: { displayName: 'Freddie', memberId: 'member-1' },
    });
    refreshAccessToken.mockRejectedValue(new Error('invalid_grant'));
    renderSession();
    await waitFor(() => expect(screen.getByText('signed-out')).toBeOnTheScreen());
    expect(clearStored).toHaveBeenCalled();
  });

  it('does not clear a stored grant when the app version changes', async () => {
    const stored = {
      ...authTokensFixture(),
      expiresAt: Date.now() + 60_000,
      identity: { displayName: 'Freddie', memberId: 'member-1' as const },
    };
    readStored.mockResolvedValue(stored);
    mockAppConfig.version = '0.1.0';
    const first = renderSession();
    await waitFor(() => expect(screen.getByText('Freddie')).toBeOnTheScreen());
    first.unmount();

    mockAppConfig.version = '0.1.214';
    renderSession();
    await waitFor(() => expect(screen.getByText('signed-in')).toBeOnTheScreen());
    expect(screen.getByText('Freddie')).toBeOnTheScreen();
    expect(clearStored).not.toHaveBeenCalled();
  });

  it('signs in through the provider hop and can sign out', async () => {
    const user = userEvent.setup();
    readStored.mockResolvedValue(null);
    signInWithProvider.mockResolvedValue(authTokensFixture());
    renderSession();
    await waitFor(() => expect(screen.getByText('signed-out')).toBeOnTheScreen());

    await user.press(screen.getByText('do-sign-in'));
    await waitFor(() => expect(screen.getByText('signed-in')).toBeOnTheScreen());
    await waitFor(() => expect(screen.getByText('Freddie')).toBeOnTheScreen());
    expect(signInWithProvider).toHaveBeenCalledWith('http://qz.test', 'Google');
    await waitFor(() =>
      expect(syncPushRegistration).toHaveBeenCalledWith(authTokensFixture().accessToken, 'member-1'),
    );

    await user.press(screen.getByText('do-sign-out'));
    await waitFor(() => expect(screen.getByText('signed-out')).toBeOnTheScreen());
    expect(clearPushRegistration).toHaveBeenCalledWith(authTokensFixture().accessToken, 'member-1');
    expect(logoutRemote).toHaveBeenCalled();
    expect(revokeRefreshToken).toHaveBeenCalled();
    expect(clearStored).toHaveBeenCalled();
    expect(writeIdentity).toHaveBeenCalledWith({
      displayName: 'Freddie',
      memberId: 'member-1',
      avatarPath: null,
    });
  });

  it('signs in with email and password through the same token store as OAuth', async () => {
    const user = userEvent.setup();
    readStored.mockResolvedValue(null);
    signInWithPassword.mockResolvedValue(authTokensFixture({ accessToken: 'password-access' }));
    renderSession();
    await waitFor(() => expect(screen.getByText('signed-out')).toBeOnTheScreen());

    await user.press(screen.getByText('do-password-sign-in'));
    await waitFor(() => expect(screen.getByText('signed-in')).toBeOnTheScreen());
    expect(signInWithPassword).toHaveBeenCalledWith('http://qz.test', 'reviewer@example.com', 'review-pass');
    expect(signInWithProvider).not.toHaveBeenCalled();
    expect(writeStored).toHaveBeenCalledWith({
      accessToken: 'password-access',
      refreshToken: 'refresh-token',
      expiresIn: 900,
    });
    await waitFor(() => expect(syncPushRegistration).toHaveBeenCalledWith('password-access', 'member-1'));
  });

  it('signs out locally even when remote logout never completes', async () => {
    const user = userEvent.setup();
    readStored.mockResolvedValue({
      ...authTokensFixture(),
      expiresAt: Date.now() + 60_000,
    });
    clearPushRegistration.mockImplementation(() => new Promise(() => {}));
    logoutRemote.mockImplementation(() => new Promise(() => {}));
    revokeRefreshToken.mockImplementation(() => new Promise(() => {}));
    renderSession();
    await waitFor(() => expect(screen.getByText('signed-in')).toBeOnTheScreen());

    await user.press(screen.getByText('do-sign-out'));
    await waitFor(() => expect(screen.getByText('signed-out')).toBeOnTheScreen());
    expect(clearStored).toHaveBeenCalled();
    expect(screen.getByText('no-token')).toBeOnTheScreen();
  });

  it('does not call remote logout when already signed out', async () => {
    const user = userEvent.setup();
    readStored.mockResolvedValue(null);
    renderSession();
    await waitFor(() => expect(screen.getByText('signed-out')).toBeOnTheScreen());

    await user.press(screen.getByText('do-sign-out'));
    await waitFor(() => expect(clearStored).toHaveBeenCalled());
    expect(clearPushRegistration).not.toHaveBeenCalled();
    expect(logoutRemote).not.toHaveBeenCalled();
    expect(revokeRefreshToken).not.toHaveBeenCalled();
    expect(screen.getByText('signed-out')).toBeOnTheScreen();
  });

  it('signs out in memory when SecureStore delete fails', async () => {
    const user = userEvent.setup();
    readStored.mockResolvedValue({
      ...authTokensFixture(),
      expiresAt: Date.now() + 60_000,
    });
    clearStored.mockRejectedValue(new Error('secure-store-unavailable'));
    renderSession();
    await waitFor(() => expect(screen.getByText('signed-in')).toBeOnTheScreen());

    await user.press(screen.getByText('do-sign-out'));
    await waitFor(() => expect(screen.getByText('signed-out')).toBeOnTheScreen());
    expect(screen.getByText('no-token')).toBeOnTheScreen();
  });

  it('does not register for push before sign-in', async () => {
    readStored.mockResolvedValue(null);
    renderSession();
    await waitFor(() => expect(screen.getByText('signed-out')).toBeOnTheScreen());
    expect(syncPushRegistration).not.toHaveBeenCalled();
  });

  it('registers for push when a stored session is restored', async () => {
    readStored.mockResolvedValue({
      ...authTokensFixture(),
      expiresAt: Date.now() + 60_000,
    });
    renderSession();
    await waitFor(() => expect(screen.getByText('signed-in')).toBeOnTheScreen());
    await waitFor(() =>
      expect(syncPushRegistration).toHaveBeenCalledWith(authTokensFixture().accessToken, 'member-1'),
    );
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

    renderSession();
    await waitFor(() =>
      expect(syncPushRegistration).toHaveBeenCalledWith(authTokensFixture().accessToken, 'member-1'),
    );

    syncPushRegistration.mockClear();
    await act(async () => {
      onToken?.();
    });
    expect(syncPushRegistration).toHaveBeenCalledWith(authTokensFixture().accessToken, 'member-1');
  });

  it('re-registers push after signing out and back in as a different member (#1094)', async () => {
    const user = userEvent.setup();
    readStored.mockResolvedValue(null);
    signInWithProvider
      .mockResolvedValueOnce(authTokensFixture({ accessToken: 'token-a' }))
      .mockResolvedValueOnce(authTokensFixture({ accessToken: 'token-b' }));
    fetchJsonMock
      .mockResolvedValueOnce(memberProfilePayload({ memberId: 'member-a', displayName: 'Alice' }))
      .mockResolvedValueOnce(memberProfilePayload({ memberId: 'member-b', displayName: 'Bob' }));

    renderSession();
    await waitFor(() => expect(screen.getByText('signed-out')).toBeOnTheScreen());

    await user.press(screen.getByText('do-sign-in'));
    await waitFor(() => expect(screen.getByText('Alice')).toBeOnTheScreen());
    await waitFor(() => expect(syncPushRegistration).toHaveBeenCalledWith('token-a', 'member-a'));

    await user.press(screen.getByText('do-sign-out'));
    await waitFor(() => expect(screen.getByText('signed-out')).toBeOnTheScreen());
    expect(clearPushRegistration).toHaveBeenCalledWith('token-a', 'member-a');

    syncPushRegistration.mockClear();
    await user.press(screen.getByText('do-sign-in'));
    await waitFor(() => expect(screen.getByText('Bob')).toBeOnTheScreen());
    await waitFor(() => expect(syncPushRegistration).toHaveBeenCalledWith('token-b', 'member-b'));
  });

  it('re-registers push when signing in as a different member without signing out first', async () => {
    const user = userEvent.setup();
    readStored.mockResolvedValue(null);
    signInWithProvider
      .mockResolvedValueOnce(authTokensFixture({ accessToken: 'token-a' }))
      .mockResolvedValueOnce(authTokensFixture({ accessToken: 'token-b' }));
    fetchJsonMock
      .mockResolvedValueOnce(memberProfilePayload({ memberId: 'member-a', displayName: 'Alice' }))
      .mockResolvedValueOnce(memberProfilePayload({ memberId: 'member-b', displayName: 'Bob' }));

    renderSession();
    await waitFor(() => expect(screen.getByText('signed-out')).toBeOnTheScreen());

    await user.press(screen.getByText('do-sign-in'));
    await waitFor(() => expect(screen.getByText('Alice')).toBeOnTheScreen());
    await waitFor(() => expect(syncPushRegistration).toHaveBeenCalledWith('token-a', 'member-a'));

    await user.press(screen.getByText('do-sign-in'));
    await waitFor(() => expect(screen.getByText('Bob')).toBeOnTheScreen());
    await waitFor(() => expect(syncPushRegistration).toHaveBeenCalledWith('token-b', 'member-b'));
  });

  it('stays signed out when the provider hop is cancelled', async () => {
    const user = userEvent.setup();
    readStored.mockResolvedValue(null);
    signInWithProvider.mockRejectedValue(new Error('Sign-in was cancelled.'));
    renderSession();
    await waitFor(() => expect(screen.getByText('signed-out')).toBeOnTheScreen());

    await user.press(screen.getByText('do-sign-in'));
    expect(signInWithProvider).toHaveBeenCalledWith('http://qz.test', 'Google');
    expect(screen.getByText('signed-out')).toBeOnTheScreen();
    expect(clearStored).not.toHaveBeenCalled();
  });

  it('applies a Debug smoke access token without the OAuth hop when appEnv is development', async () => {
    const user = userEvent.setup();
    readStored.mockResolvedValue(null);
    renderSession();
    await waitFor(() => expect(screen.getByText('signed-out')).toBeOnTheScreen());

    await user.press(screen.getByText('do-smoke-auth'));
    await waitFor(() => expect(screen.getByText('signed-in')).toBeOnTheScreen());
    expect(screen.getByText('smoke-applied')).toBeOnTheScreen();
    expect(signInWithProvider).not.toHaveBeenCalled();
    expect(writeStored).toHaveBeenCalledWith({
      accessToken: 'smoke-access',
      refreshToken: 'smoke-debug-no-refresh',
      expiresIn: 3600,
    });
  });

  it.each(['staging', 'production'] as const)(
    'rejects the smoke session when appEnv is %s',
    async (appEnv) => {
      const user = userEvent.setup();
      mockAppConfig.appEnv = appEnv;
      mockAppConfig.smokeEmbed = true;
      readStored.mockResolvedValue(null);
      renderSession();
      await waitFor(() => expect(screen.getByText('signed-out')).toBeOnTheScreen());

      await user.press(screen.getByText('do-smoke-auth'));
      await waitFor(() => expect(screen.getByText('smoke-rejected')).toBeOnTheScreen());
      expect(screen.getByText('signed-out')).toBeOnTheScreen();
      expect(screen.getByText('no-token')).toBeOnTheScreen();
      expect(writeStored).not.toHaveBeenCalled();
    },
  );

  it('derives isSignedIn from setAccessToken and stays signed out without a token', async () => {
    const user = userEvent.setup();
    readStored.mockResolvedValue(null);
    renderSession();
    await waitFor(() => expect(screen.getByText('signed-out')).toBeOnTheScreen());

    await user.press(screen.getByText('clear-token'));
    expect(screen.getByText('signed-out')).toBeOnTheScreen();
    expect(screen.getByText('no-token')).toBeOnTheScreen();

    await user.press(screen.getByText('set-empty-token'));
    expect(screen.getByText('signed-out')).toBeOnTheScreen();
    expect(screen.getByText('no-token')).toBeOnTheScreen();

    await user.press(screen.getByText('set-token'));
    expect(screen.getByText('signed-in')).toBeOnTheScreen();
    expect(screen.getByText('manual-token')).toBeOnTheScreen();

    await user.press(screen.getByText('clear-token'));
    expect(screen.getByText('signed-out')).toBeOnTheScreen();
    expect(screen.getByText('no-token')).toBeOnTheScreen();
  });

  it('fail-opens a hung development restore so Profile is not stuck restoring', async () => {
    jest.useFakeTimers();
    try {
      readStored.mockReturnValue(new Promise(() => {}));
      renderSession();
      expect(screen.getByText('restoring')).toBeOnTheScreen();
      await act(async () => {
        jest.advanceTimersByTime(5_000);
      });
      expect(screen.getByText('signed-out')).toBeOnTheScreen();
      expect(clearStored).not.toHaveBeenCalled();
    } finally {
      jest.useRealTimers();
    }
  });

  it('does not sign out when a locked keychain blocks restore, and retries on active', async () => {
    const locked = Object.assign(new Error('User interaction is not allowed'), {
      name: 'KeyChainException',
    });
    const appStateHandlers: ((state: string) => void)[] = [];
    jest.spyOn(AppState, 'addEventListener').mockImplementation((type, handler) => {
      if (type === 'change') {
        appStateHandlers.push(handler as (state: string) => void);
      }
      return { remove: jest.fn() };
    });

    readStored.mockRejectedValueOnce(locked);
    readStored.mockResolvedValue({
      ...authTokensFixture(),
      expiresAt: Date.now() + 60_000,
    });

    renderSession();
    await waitFor(() => expect(readStored).toHaveBeenCalledTimes(1));
    expect(screen.getByText('restoring')).toBeOnTheScreen();
    expect(screen.queryByText('signed-out')).toBeNull();
    expect(clearStored).not.toHaveBeenCalled();

    await act(async () => {
      for (const handler of appStateHandlers) {
        handler('active');
      }
    });
    await waitFor(() => expect(screen.getByText('signed-in')).toBeOnTheScreen());
    expect(screen.getByText('Freddie')).toBeOnTheScreen();
    expect(clearStored).not.toHaveBeenCalled();
  });

  it('retries a locked session write when the app becomes active', async () => {
    const user = userEvent.setup();
    const now = 1_700_000_000_000;
    const dateNow = jest.spyOn(Date, 'now').mockReturnValue(now);
    const locked = Object.assign(new Error('User interaction is not allowed'), {
      name: 'KeyChainException',
    });
    const appStateHandlers: ((state: string) => void)[] = [];
    jest.spyOn(AppState, 'addEventListener').mockImplementation((type, handler) => {
      if (type === 'change') {
        appStateHandlers.push(handler as (state: string) => void);
      }
      return { remove: jest.fn() };
    });

    try {
      readStored.mockResolvedValue({
        ...authTokensFixture(),
        expiresAt: now + 60_000,
      });
      renderSession();
      await waitFor(() => expect(screen.getByText('signed-in')).toBeOnTheScreen());

      writeStored.mockRejectedValueOnce(locked);
      writeStored.mockImplementation(async (tokens) => ({
        ...tokens,
        expiresAt: now + 180_000,
      }));
      refreshAccessToken.mockResolvedValue(authTokensFixture({ accessToken: 'next' }));
      dateNow.mockReturnValue(now + 120_000);
      await user.press(screen.getByText('do-ensure-token'));

      await waitFor(() => expect(screen.getByText('next')).toBeOnTheScreen());
      expect(screen.getByText('signed-in')).toBeOnTheScreen();
      expect(clearStored).not.toHaveBeenCalled();

      writeStored.mockClear();
      writeStored.mockImplementation(async (tokens) => ({
        ...tokens,
        expiresAt: now + 180_000,
      }));
      await act(async () => {
        for (const handler of appStateHandlers) {
          handler('active');
        }
      });
      await waitFor(() => expect(writeStored).toHaveBeenCalled());
      expect(clearStored).not.toHaveBeenCalled();
      expect(screen.getByText('signed-in')).toBeOnTheScreen();
    } finally {
      dateNow.mockRestore();
    }
  });

  it('stays signed in when profile load fails', async () => {
    readStored.mockResolvedValue({
      ...authTokensFixture(),
      expiresAt: Date.now() + 60_000,
    });
    fetchJsonMock.mockRejectedValue(new Error('offline'));
    renderSession();
    await waitFor(() => expect(screen.getByText('signed-in')).toBeOnTheScreen());
    expect(screen.getByText('anonymous')).toBeOnTheScreen();
  });

  it('exposes the stored access token before profile load finishes', async () => {
    let resolveProfile: (value: unknown) => void = () => {};
    fetchJsonMock.mockImplementation(
      () =>
        new Promise((resolve) => {
          resolveProfile = resolve;
        }),
    );
    readStored.mockResolvedValue({
      ...authTokensFixture(),
      expiresAt: Date.now() + 60_000,
    });
    renderSession();
    await waitFor(() => expect(screen.getByText('signed-in')).toBeOnTheScreen());
    expect(screen.getByText('access-token')).toBeOnTheScreen();
    expect(screen.getByText('anonymous')).toBeOnTheScreen();
    expect(refreshAccessToken).not.toHaveBeenCalled();

    await act(async () => {
      resolveProfile(memberProfilePayload());
    });
    await waitFor(() => expect(screen.getByText('Freddie')).toBeOnTheScreen());
  });

  it('refreshes a locally valid token when /me returns 401', async () => {
    readStored.mockResolvedValue({
      ...authTokensFixture(),
      expiresAt: Date.now() + 60_000,
    });
    fetchJsonMock.mockRejectedValueOnce(ApiError.http(401, 'Unauthorized'));
    fetchJsonMock.mockResolvedValue(memberProfilePayload());
    refreshAccessToken.mockResolvedValue(authTokensFixture({ accessToken: 'next' }));
    renderSession();
    await waitFor(() => expect(screen.getByText('next')).toBeOnTheScreen());
    expect(refreshAccessToken).toHaveBeenCalledWith('http://qz.test', 'refresh-token');
    expect(screen.getByText('Freddie')).toBeOnTheScreen();
  });

  it('single-flights a refreshProfile 401 recovery with a concurrent refresh', async () => {
    // The server treats a reused refresh token as theft and revokes every grant
    // the member holds, so two concurrent recoveries must not present the same
    // rotating grant twice.
    const user = userEvent.setup();
    const refresh = deferred<ReturnType<typeof authTokensFixture>>();
    refreshAccessToken.mockImplementation(async () => refresh.promise);
    readStored.mockResolvedValue({
      ...authTokensFixture(),
      expiresAt: Date.now() + 60_000,
      identity: { displayName: 'Freddie', memberId: 'member-1' },
    });
    fetchJsonMock.mockResolvedValue(memberProfilePayload());
    renderSession();
    await waitFor(() => expect(screen.getByText('signed-in')).toBeOnTheScreen());

    // Every /me from here on is a 401, so both presses take the recovery path.
    fetchJsonMock.mockRejectedValue(ApiError.http(401, 'Unauthorized'));
    await user.press(screen.getByText('do-refresh-profile'));
    await user.press(screen.getByText('do-refresh-profile'));
    expect(refreshAccessToken).toHaveBeenCalledTimes(1);

    fetchJsonMock.mockResolvedValue(memberProfilePayload());
    await act(async () => {
      refresh.resolve(authTokensFixture({ accessToken: 'next' }));
    });
    await waitFor(() => expect(screen.getByText('next')).toBeOnTheScreen());
    expect(refreshAccessToken).toHaveBeenCalledTimes(1);
    expect(clearStored).not.toHaveBeenCalled();
  });

  // Each of these used to read as a dead grant and sign the member out on the
  // first launch after an overnight idle.
  async function expectSessionSurvives(failure: Error): Promise<void> {
    readStored.mockResolvedValue({
      ...authTokensFixture(),
      expiresAt: Date.now() - 1_000,
      identity: { displayName: 'Freddie', memberId: 'member-1' },
    });
    refreshAccessToken.mockRejectedValue(failure);
    renderSession();

    await waitFor(() => expect(screen.getByText('signed-in')).toBeOnTheScreen());
    expect(screen.getByText('Freddie')).toBeOnTheScreen();
    expect(clearStored).not.toHaveBeenCalled();
  }

  it('keeps the session when the per-account limiter rate limits a refresh', async () => {
    await expectSessionSurvives(
      new TokenEndpointError(429, 'temporarily_unavailable', 'Too many attempts. Try again later.'),
    );
  });

  it('keeps the session when the token endpoint is down', async () => {
    await expectSessionSurvives(new TokenEndpointError(503, '', 'Could not complete sign-in.'));
  });

  it('keeps the session when mobile auth is unconfigured after a deploy', async () => {
    await expectSessionSurvives(
      new TokenEndpointError(400, 'temporarily_unavailable', 'Mobile auth is not configured.'),
    );
  });

  it('still signs out on a dead grant from the token endpoint', async () => {
    readStored.mockResolvedValue({
      ...authTokensFixture(),
      expiresAt: Date.now() - 1_000,
      identity: { displayName: 'Freddie', memberId: 'member-1' },
    });
    refreshAccessToken.mockRejectedValue(new TokenEndpointError(400, 'invalid_grant', 'invalid_grant'));
    renderSession();

    await waitFor(() => expect(screen.getByText('signed-out')).toBeOnTheScreen());
    expect(clearStored).toHaveBeenCalled();
  });

  it('refreshes an expired token from ensureAccessToken and app foreground', async () => {
    const user = userEvent.setup();
    const now = 1_700_000_000_000;
    const dateNow = jest.spyOn(Date, 'now').mockReturnValue(now);
    const appStateHandlers: ((state: string) => void)[] = [];
    jest.spyOn(AppState, 'addEventListener').mockImplementation((type, handler) => {
      if (type === 'change') {
        appStateHandlers.push(handler as (state: string) => void);
      }
      return { remove: jest.fn() };
    });
    try {
      readStored.mockResolvedValue({
        ...authTokensFixture(),
        expiresAt: now + 60_000,
      });
      refreshAccessToken.mockResolvedValue(authTokensFixture({ accessToken: 'next' }));
      renderSession();
      await waitFor(() => expect(screen.getByText('signed-in')).toBeOnTheScreen());
      expect(refreshAccessToken).not.toHaveBeenCalled();

      dateNow.mockReturnValue(now + 120_000);
      await user.press(screen.getByText('do-ensure-token'));
      await waitFor(() => expect(screen.getByText('next')).toBeOnTheScreen());

      refreshAccessToken.mockClear();
      refreshAccessToken.mockResolvedValue(authTokensFixture({ accessToken: 'foreground' }));
      dateNow.mockReturnValue(now + 180_000);
      await act(async () => {
        for (const handler of appStateHandlers) {
          handler('active');
        }
      });
      await waitFor(() => expect(screen.getByText('foreground')).toBeOnTheScreen());
    } finally {
      dateNow.mockRestore();
    }
  });

  it('stays signed in with the refreshed token when a background refresh still gets a 401 from /me', async () => {
    const user = userEvent.setup();
    const now = 1_700_000_000_000;
    const dateNow = jest.spyOn(Date, 'now').mockReturnValue(now);
    try {
      readStored.mockResolvedValue({
        ...authTokensFixture(),
        expiresAt: now + 60_000,
      });
      renderSession();
      await waitFor(() => expect(screen.getByText('Freddie')).toBeOnTheScreen());

      refreshAccessToken.mockResolvedValue(authTokensFixture({ accessToken: 'next' }));
      fetchJsonMock.mockReset();
      fetchJsonMock.mockRejectedValue(ApiError.http(401, 'Unauthorized'));
      dateNow.mockReturnValue(now + 120_000);
      await user.press(screen.getByText('do-ensure-token'));

      await waitFor(() => expect(screen.getByText('next')).toBeOnTheScreen());
      expect(screen.getByText('signed-in')).toBeOnTheScreen();
      expect(screen.getByText('Freddie')).toBeOnTheScreen();
      expect(screen.getByText('FR')).toBeOnTheScreen();
      expect(clearStored).not.toHaveBeenCalled();
    } finally {
      dateNow.mockRestore();
    }
  });

  it('keeps a cached identity shell when /me fails after a successful refresh', async () => {
    const refresh = deferred<ReturnType<typeof authTokensFixture>>();
    readStored.mockResolvedValue({
      ...authTokensFixture(),
      expiresAt: Date.now() - 1_000,
      identity: { displayName: 'Freddie', memberId: 'member-1', avatarPath: '/avatars/1.jpg' },
    });
    refreshAccessToken.mockReturnValue(refresh.promise);
    fetchJsonMock.mockRejectedValue(new Error('offline'));
    renderSession();

    await waitFor(() => expect(screen.getByText('Freddie')).toBeOnTheScreen());
    await act(async () => {
      refresh.resolve(authTokensFixture({ accessToken: 'next' }));
    });
    await waitFor(() => expect(screen.getByText('next')).toBeOnTheScreen());
    expect(screen.getByText('signed-in')).toBeOnTheScreen();
    expect(screen.getByText('Freddie')).toBeOnTheScreen();
    expect(clearStored).not.toHaveBeenCalled();
  });
});

describe('SessionProvider private cache isolation', () => {
  let cache: ContentCache;

  beforeEach(async () => {
    cache = new ContentCache({ storage: createMemoryStorage() });
    setContentCacheForTests(cache);
    await cache.put(conversationCacheKey('member-1', 'c1'), { body: 'secret from A' });
    await cache.put(forumTopicCacheKey(1002), { id: 1002 });
  });

  afterEach(() => {
    setContentCacheForTests(null);
  });

  it('purges conversations on sign-out and leaves the public forum cache', async () => {
    const user = userEvent.setup();
    readStored.mockResolvedValue({
      ...authTokensFixture(),
      expiresAt: Date.now() + 60_000,
    });
    renderSession();
    await waitFor(() => expect(screen.getByText('signed-in')).toBeOnTheScreen());

    await user.press(screen.getByText('do-sign-out'));
    await waitFor(() => expect(screen.getByText('signed-out')).toBeOnTheScreen());

    expect(await cache.get(conversationCacheKey('member-1', 'c1'))).toBeNull();
    expect(await cache.get(forumTopicCacheKey(1002))).toEqual({ id: 1002 });
  });

  it('purges member A conversations when session restore establishes member B', async () => {
    const user = userEvent.setup();
    readStored.mockResolvedValue({
      ...authTokensFixture(),
      expiresAt: Date.now() + 60_000,
    });
    renderSession();
    await waitFor(() => expect(screen.getByText('Freddie')).toBeOnTheScreen());

    signInWithProvider.mockResolvedValue(authTokensFixture({ accessToken: 'next' }));
    fetchJsonMock.mockResolvedValue(memberProfilePayload({ memberId: 'member-2', displayName: 'Brian' }));
    await user.press(screen.getByText('do-sign-in'));
    await waitFor(() => expect(screen.getByText('Brian')).toBeOnTheScreen());

    expect(await cache.get(conversationCacheKey('member-1', 'c1'))).toBeNull();
    expect(await cache.get(conversationCacheKey('member-2', 'c1'))).toBeNull();
    expect(await cache.get(forumTopicCacheKey(1002))).toEqual({ id: 1002 });
  });
});

describe('SessionProvider actions context stability', () => {
  it('keeps actions identity stable across a token refresh and a profile load', async () => {
    const user = userEvent.setup();
    const now = 1_700_000_000_000;
    const dateNow = jest.spyOn(Date, 'now').mockReturnValue(now);
    const actionsSeen: SessionActions[] = [];
    let actionsOnlyRenders = 0;

    function ActionsOnlyChild() {
      const actions = useSessionActions();
      actionsOnlyRenders += 1;
      actionsSeen.push(actions);
      return <Text>actions-only</Text>;
    }

    let resolveProfile: (value: unknown) => void = () => {};
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
      renderWithProviders(
        <SessionProvider>
          <ActionsOnlyChild />
          <Probe />
        </SessionProvider>,
        { navigation: false },
      );

      await waitFor(() => expect(screen.getByText('signed-in')).toBeOnTheScreen());
      expect(screen.getByText('anonymous')).toBeOnTheScreen();
      expect(screen.getByText('actions-only')).toBeOnTheScreen();
      const rendersAfterToken = actionsOnlyRenders;
      const actionsAfterToken = actionsSeen[actionsSeen.length - 1];
      expect(rendersAfterToken).toBeGreaterThan(0);
      expect(actionsAfterToken).toBeDefined();

      fetchJsonMock.mockResolvedValue(memberProfilePayload());
      await act(async () => {
        resolveProfile(memberProfilePayload());
      });
      await waitFor(() => expect(screen.getByText('Freddie')).toBeOnTheScreen());
      expect(actionsOnlyRenders).toBe(rendersAfterToken);
      expect(actionsSeen[actionsSeen.length - 1]).toBe(actionsAfterToken);

      refreshAccessToken.mockResolvedValue(authTokensFixture({ accessToken: 'access-token' }));
      dateNow.mockReturnValue(now + 120_000);
      await user.press(screen.getByText('do-ensure-token'));
      await waitFor(() => expect(refreshAccessToken).toHaveBeenCalledWith('http://qz.test', 'refresh-token'));
      expect(screen.getByText('signed-in')).toBeOnTheScreen();
      expect(screen.getByText('access-token')).toBeOnTheScreen();
      expect(actionsOnlyRenders).toBe(rendersAfterToken);
      expect(actionsSeen[actionsSeen.length - 1]).toBe(actionsAfterToken);
    } finally {
      dateNow.mockRestore();
    }
  });
});
