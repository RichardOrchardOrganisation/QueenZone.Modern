import * as Crypto from 'expo-crypto';
import * as WebBrowser from 'expo-web-browser';
import type { WebBrowserResultType } from 'expo-web-browser';
import { waitFor } from '@testing-library/react-native';
import { Linking } from 'react-native';
import {
  logoutRemote,
  refreshAccessToken,
  remoteAuthTimeoutMs,
  revokeRefreshToken,
  signInWithPassword,
  signInWithProvider,
} from './oauth';
import { TokenEndpointError } from '../api/errors';
import { isDefiniteAuthRefreshFailure, isTransientRefreshFailure } from './refreshFailure';
import { jsonResponse } from '../test/fixtures';

jest.mock('expo-web-browser', () => ({
  maybeCompleteAuthSession: jest.fn(),
  openAuthSessionAsync: jest.fn(),
  dismissAuthSession: jest.fn(),
  dismissBrowser: jest.fn(),
}));

jest.mock('expo-crypto', () => ({
  CryptoDigestAlgorithm: { SHA256: 'SHA-256' },
  CryptoEncoding: { BASE64: 'base64' },
  getRandomBytesAsync: jest.fn(async (n: number) => Uint8Array.from({ length: n }, (_, i) => i + 1)),
  digestStringAsync: jest.fn(async () => 'abc+def/ghi='),
}));

const openAuth = WebBrowser.openAuthSessionAsync as jest.MockedFunction<
  typeof WebBrowser.openAuthSessionAsync
>;
const fetchMock = jest.fn<Promise<Response>, [RequestInfo | URL, RequestInit?]>();

const linkingHandlers: ((event: { url: string }) => void)[] = [];

beforeEach(() => {
  fetchMock.mockReset();
  global.fetch = fetchMock as unknown as typeof fetch;
  (Crypto.getRandomBytesAsync as jest.Mock).mockClear();
  linkingHandlers.length = 0;
  jest.spyOn(Linking, 'addEventListener').mockImplementation((_type, handler) => {
    linkingHandlers.push(handler as (event: { url: string }) => void);
    return { remove: jest.fn() } as unknown as ReturnType<typeof Linking.addEventListener>;
  });
});

afterEach(() => {
  jest.restoreAllMocks();
});

function authorizeState(url: string) {
  return new URL(url).searchParams.get('state') ?? '';
}

describe('signInWithProvider', () => {
  it('exchanges a successful callback for tokens', async () => {
    openAuth.mockImplementation(async (url) => ({
      type: 'success',
      url: `queenzone://auth/callback?code=auth-code&state=${authorizeState(String(url))}`,
    }));
    fetchMock.mockResolvedValueOnce(
      jsonResponse({ access_token: 'a', refresh_token: 'r', expires_in: 900 }),
    );

    const tokens = await signInWithProvider('http://qz.test', 'Google');
    expect(tokens).toEqual({ accessToken: 'a', refreshToken: 'r', expiresIn: 900 });
    expect(String(fetchMock.mock.calls[0]?.[0])).toContain('/api/v1/auth/token');
  });

  it('maps cancel, missing code, state mismatch, and provider errors', async () => {
    openAuth.mockResolvedValueOnce({ type: 'cancel' as WebBrowserResultType });
    await expect(signInWithProvider('http://qz.test', 'Google')).rejects.toThrow('Sign-in was cancelled.');

    openAuth.mockResolvedValueOnce({
      type: 'success',
      url: 'queenzone://auth/callback?state=nope',
    });
    await expect(signInWithProvider('http://qz.test', 'Google')).rejects.toThrow(
      'Sign-in did not return an authorization code.',
    );

    openAuth.mockImplementationOnce(async (url) => ({
      type: 'success',
      url: `queenzone://auth/callback?code=x&state=${authorizeState(String(url))}x`,
    }));
    await expect(signInWithProvider('http://qz.test', 'Google')).rejects.toThrow('Sign-in state mismatch.');

    openAuth.mockResolvedValueOnce({
      type: 'success',
      url: 'queenzone://auth/callback?error=access_denied&error_description=Nope',
    });
    await expect(signInWithProvider('http://qz.test', 'Google')).rejects.toThrow('Nope');
  });

  it('completes sign-in from a deep link when the browser hop stays open', async () => {
    openAuth.mockImplementation(
      () =>
        new Promise(() => {
          /* custom tab never reports success */
        }),
    );
    fetchMock.mockResolvedValueOnce(
      jsonResponse({ access_token: 'a', refresh_token: 'r', expires_in: 900 }),
    );

    const pending = signInWithProvider('http://qz.test', 'Google');
    await waitFor(() => expect(openAuth).toHaveBeenCalled());
    await waitFor(() => expect(linkingHandlers.length).toBeGreaterThan(0));
    const authorizeUrl = String(openAuth.mock.calls[0]?.[0]);
    linkingHandlers[0]?.({
      url: `queenzone://auth/callback?code=auth-code&state=${authorizeState(authorizeUrl)}`,
    });

    await expect(pending).resolves.toEqual({ accessToken: 'a', refreshToken: 'r', expiresIn: 900 });
    expect(WebBrowser.dismissAuthSession).toHaveBeenCalled();
  });
});

describe('signInWithPassword', () => {
  it('exchanges email and password for the same token shape as authorization_code', async () => {
    fetchMock.mockResolvedValueOnce(
      jsonResponse({ access_token: 'a', refresh_token: 'r', expires_in: 900 }),
    );

    await expect(signInWithPassword('http://qz.test', 'reviewer@example.com', 'secret')).resolves.toEqual({
      accessToken: 'a',
      refreshToken: 'r',
      expiresIn: 900,
    });
    expect(String(fetchMock.mock.calls[0]?.[0])).toContain('/api/v1/auth/token');
    expect(fetchMock.mock.calls[0]?.[1]?.body).toBe(
      'grant_type=password&client_id=queenzone-mobile&username=reviewer%40example.com&password=secret',
    );
  });

  it('maps invalid_grant to a generic credential error', async () => {
    fetchMock.mockResolvedValueOnce(
      jsonResponse({ error: 'invalid_grant', error_description: 'The password grant is invalid.' }, 400),
    );
    await expect(signInWithPassword('http://qz.test', 'reviewer@example.com', 'nope')).rejects.toThrow(
      'Incorrect email or password.',
    );
  });

  it('surfaces a suspended account without treating it as a generic credential error', async () => {
    fetchMock.mockResolvedValueOnce(
      jsonResponse({ error: 'invalid_grant', error_description: 'This account has been suspended.' }, 400),
    );
    await expect(signInWithPassword('http://qz.test', 'reviewer@example.com', 'secret')).rejects.toThrow(
      'This account has been suspended.',
    );
  });
});

describe('token maintenance', () => {
  it('carries the OAuth2 code and HTTP status off a rejected refresh', async () => {
    // The refresh path classifies on these two fields: without them a 429 or a
    // 5xx is indistinguishable from a dead grant and signs the member out.
    fetchMock.mockResolvedValueOnce(
      jsonResponse({ error: 'temporarily_unavailable', error_description: 'Too many attempts. Try again later.' }, 429),
    );
    const rateLimited = await refreshAccessToken('http://qz.test', 'r').catch((err: unknown) => err);
    expect(rateLimited).toBeInstanceOf(TokenEndpointError);
    expect(rateLimited).toMatchObject({ status: 429, oauthError: 'temporarily_unavailable' });
    expect(isTransientRefreshFailure(rateLimited)).toBe(true);
  });

  it('treats an unparseable 5xx body as a transient outage, not a dead grant', async () => {
    // An App Service cold start or a Cloudflare error page is HTML, so there is
    // no OAuth2 error code to read — only the status.
    fetchMock.mockResolvedValueOnce({
      ok: false,
      status: 503,
      json: async () => {
        throw new Error('not json');
      },
    } as unknown as Response);
    const downstream = await refreshAccessToken('http://qz.test', 'r').catch((err: unknown) => err);
    expect(downstream).toBeInstanceOf(TokenEndpointError);
    expect(downstream).toMatchObject({ status: 503, message: 'Could not complete sign-in.' });
    expect(isDefiniteAuthRefreshFailure(downstream)).toBe(false);
    expect(isTransientRefreshFailure(downstream)).toBe(true);
  });

  it('still reports a dead grant as invalid_grant', async () => {
    fetchMock.mockResolvedValueOnce(jsonResponse({ error: 'invalid_grant' }, 400));
    const dead = await refreshAccessToken('http://qz.test', 'r').catch((err: unknown) => err);
    expect(dead).toBeInstanceOf(TokenEndpointError);
    expect(dead).toMatchObject({ status: 400, oauthError: 'invalid_grant', message: 'invalid_grant' });
    expect(isDefiniteAuthRefreshFailure(dead)).toBe(true);
  });

  it('refreshes an access token', async () => {
    fetchMock.mockResolvedValueOnce(
      jsonResponse({ access_token: 'a2', refresh_token: 'r2', expires_in: 600 }),
    );
    await expect(refreshAccessToken('http://qz.test', 'r')).resolves.toEqual({
      accessToken: 'a2',
      refreshToken: 'r2',
      expiresIn: 600,
    });
  });

  it('treats logout and revoke as best-effort', async () => {
    fetchMock.mockRejectedValue(new TypeError('offline'));
    await expect(logoutRemote('http://qz.test', 'a')).resolves.toBeUndefined();
    await expect(revokeRefreshToken('http://qz.test', 'r')).resolves.toBeUndefined();
  });

  it('completes logout and revoke when the server responds', async () => {
    fetchMock.mockResolvedValue(jsonResponse({}));
    await expect(logoutRemote('http://qz.test', 'a')).resolves.toBeUndefined();
    await expect(revokeRefreshToken('http://qz.test', 'r')).resolves.toBeUndefined();
    expect(String(fetchMock.mock.calls[0]?.[0])).toContain('/api/v1/auth/logout');
    expect(String(fetchMock.mock.calls[1]?.[0])).toContain('/api/v1/auth/revoke');
  });

  it('resolves logout and revoke when fetch never settles', async () => {
    jest.useFakeTimers();
    try {
      fetchMock.mockImplementation(() => new Promise(() => {}));
      const logout = logoutRemote('http://qz.test', 'a');
      const revoke = revokeRefreshToken('http://qz.test', 'r');
      await jest.advanceTimersByTimeAsync(remoteAuthTimeoutMs);
      await expect(logout).resolves.toBeUndefined();
      await expect(revoke).resolves.toBeUndefined();
    } finally {
      jest.useRealTimers();
    }
  });
});
