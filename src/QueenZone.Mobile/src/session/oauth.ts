import * as Crypto from 'expo-crypto';
import * as WebBrowser from 'expo-web-browser';
import { Linking } from 'react-native';
import {
  buildAuthorizeUrl,
  mobileClientId,
  mobileRedirectUri,
  parseAuthCallback,
  parseTokenResponse,
  tokenFormBody,
  authLogoutUrl,
  authRevokeUrl,
  authTokenUrl,
  type AuthTokens,
} from '../api/auth';
import { TokenEndpointError } from '../api/errors';

WebBrowser.maybeCompleteAuthSession();

export async function signInWithProvider(apiBaseUrl: string, provider: string): Promise<AuthTokens> {
  const state = await randomUrlSafe(16);
  const verifier = await randomUrlSafe(32);
  const challenge = await sha256Base64Url(verifier);
  const authorizeUrl = buildAuthorizeUrl({
    apiBaseUrl,
    provider,
    state,
    codeChallenge: challenge,
  });

  const callbackUrl = await collectAuthCallback(authorizeUrl);
  const callback = parseAuthCallback(callbackUrl);
  if (callback.error) {
    throw new Error(callback.errorDescription ?? callback.error);
  }

  if (!callback.code) {
    throw new Error('Sign-in did not return an authorization code.');
  }

  if (callback.state !== state) {
    throw new Error('Sign-in state mismatch.');
  }

  return exchangeAuthorizationCode(apiBaseUrl, callback.code, verifier);
}

export async function refreshAccessToken(apiBaseUrl: string, refreshToken: string): Promise<AuthTokens> {
  return postToken(apiBaseUrl, {
    grant_type: 'refresh_token',
    client_id: mobileClientId,
    refresh_token: refreshToken,
  });
}

/** Resource-owner password grant for operator-created accounts (App Review / non-social). */
export async function signInWithPassword(
  apiBaseUrl: string,
  email: string,
  password: string,
): Promise<AuthTokens> {
  const response = await fetch(authTokenUrl(apiBaseUrl), {
    method: 'POST',
    headers: {
      Accept: 'application/json',
      'Content-Type': 'application/x-www-form-urlencoded',
    },
    body: tokenFormBody({
      grant_type: 'password',
      client_id: mobileClientId,
      username: email,
      password,
    }),
  });
  const payload: unknown = await response.json().catch(() => null);
  if (!response.ok) {
    throw new Error(passwordGrantError(payload));
  }

  return parseTokenResponse(payload);
}

function passwordGrantError(payload: unknown): string {
  const error =
    payload && typeof payload === 'object'
      ? (payload as { error?: unknown; error_description?: unknown })
      : null;
  const description = typeof error?.error_description === 'string' ? error.error_description : '';
  if (description === 'This account has been suspended.') {
    return description;
  }

  if (error?.error === 'invalid_grant') {
    return 'Incorrect email or password.';
  }

  return description.length > 0 ? description : 'Could not complete sign-in.';
}

/** Caps best-effort logout/revoke so a hung React Native fetch cannot block sign-out. */
export const remoteAuthTimeoutMs = 8_000;

export async function revokeRefreshToken(apiBaseUrl: string, refreshToken: string): Promise<void> {
  await fetchBestEffort(authRevokeUrl(apiBaseUrl), {
    method: 'POST',
    headers: { 'Content-Type': 'application/x-www-form-urlencoded', Accept: 'application/json' },
    body: tokenFormBody({ token: refreshToken, client_id: mobileClientId }),
  });
}

export async function logoutRemote(apiBaseUrl: string, accessToken: string): Promise<void> {
  await fetchBestEffort(authLogoutUrl(apiBaseUrl), {
    method: 'POST',
    headers: { Accept: 'application/json', Authorization: `Bearer ${accessToken}` },
  });
}

async function fetchBestEffort(url: string, init: RequestInit, timeoutMs = remoteAuthTimeoutMs): Promise<void> {
  const controller = new AbortController();
  let timer: ReturnType<typeof setTimeout> | undefined;
  const timeout = new Promise<void>((resolve) => {
    timer = setTimeout(() => {
      controller.abort();
      resolve();
    }, timeoutMs);
  });

  try {
    await Promise.race([
      fetch(url, { ...init, signal: controller.signal }).then(
        () => undefined,
        () => undefined,
      ),
      timeout,
    ]);
  } finally {
    if (timer !== undefined) {
      clearTimeout(timer);
    }
  }
}

type AuthHop =
  | { type: 'url'; url: string }
  | { type: 'cancelled' }
  | { type: 'failed' };

async function collectAuthCallback(authorizeUrl: string): Promise<string> {
  const pending = watchAuthCallbackUrl();
  try {
    if (typeof WebBrowser.warmUpAsync === 'function') {
      await WebBrowser.warmUpAsync();
    }

    const hop = await Promise.race([
      WebBrowser.openAuthSessionAsync(authorizeUrl, mobileRedirectUri).then((result): AuthHop => {
        if (result.type === 'success' && 'url' in result && result.url) {
          return { type: 'url', url: result.url };
        }
        if (result.type === 'cancel' || result.type === 'dismiss') {
          return { type: 'cancelled' };
        }
        return { type: 'failed' };
      }),
      pending.promise.then((url): AuthHop => ({ type: 'url', url })),
    ]);

    if (hop.type === 'url') {
      dismissAuthBrowser();
      return hop.url;
    }

    const late = await waitForPendingUrl(pending, 400);
    if (late) {
      dismissAuthBrowser();
      return late;
    }

    throw new Error(hop.type === 'cancelled' ? 'Sign-in was cancelled.' : 'Sign-in did not complete.');
  } finally {
    pending.cancel();
    if (typeof WebBrowser.coolDownAsync === 'function') {
      await WebBrowser.coolDownAsync();
    }
  }
}

function watchAuthCallbackUrl(): {
  promise: Promise<string>;
  cancel: () => void;
  url: () => string | null;
} {
  let settled = false;
  let captured: string | null = null;
  let subscription: { remove: () => void } | undefined;
  let resolvePromise: (url: string) => void = () => {};
  const promise = new Promise<string>((resolve) => {
    resolvePromise = resolve;
  });

  const onUrl = (url: string) => {
    if (settled || !isAuthCallbackUrl(url)) {
      return;
    }
    settled = true;
    captured = url;
    subscription?.remove();
    dismissAuthBrowser();
    resolvePromise(url);
  };

  subscription = Linking.addEventListener('url', ({ url }) => onUrl(url));

  return {
    promise,
    cancel: () => {
      settled = true;
      subscription?.remove();
    },
    url: () => captured,
  };
}

async function waitForPendingUrl(
  pending: { promise: Promise<string>; url: () => string | null },
  ms: number,
): Promise<string | null> {
  const existing = pending.url();
  if (existing) {
    return existing;
  }

  return Promise.race([
    pending.promise,
    new Promise<null>((resolve) => {
      setTimeout(() => resolve(null), ms);
    }),
  ]);
}

function dismissAuthBrowser() {
  try {
    WebBrowser.dismissAuthSession();
  } catch {
    try {
      void WebBrowser.dismissBrowser();
    } catch {
      // Best-effort: the custom tab may already be gone.
    }
  }
}

function isAuthCallbackUrl(url: string): boolean {
  return url.startsWith(mobileRedirectUri) || /:\/\/auth\/callback(?:\?|$)/.test(url);
}

async function exchangeAuthorizationCode(
  apiBaseUrl: string,
  code: string,
  verifier: string,
): Promise<AuthTokens> {
  return postToken(apiBaseUrl, {
    grant_type: 'authorization_code',
    client_id: mobileClientId,
    redirect_uri: mobileRedirectUri,
    code,
    code_verifier: verifier,
  });
}

async function postToken(apiBaseUrl: string, fields: Record<string, string>): Promise<AuthTokens> {
  const response = await fetch(authTokenUrl(apiBaseUrl), {
    method: 'POST',
    headers: {
      Accept: 'application/json',
      'Content-Type': 'application/x-www-form-urlencoded',
    },
    body: tokenFormBody(fields),
  });
  const payload: unknown = await response.json().catch(() => null);
  if (!response.ok) {
    const error =
      payload && typeof payload === 'object'
        ? (payload as { error?: unknown; error_description?: unknown })
        : null;
    const code = typeof error?.error === 'string' ? error.error : '';
    // A 5xx or a Cloudflare error page has no JSON body at all, so the status is
    // the only signal the caller gets — carry it rather than flattening every
    // failure into a bare Error the refresh path then reads as a dead grant.
    if (code === 'invalid_grant' || response.status === 401) {
      throw new TokenEndpointError(response.status, code || 'invalid_grant', 'invalid_grant');
    }
    const description =
      typeof error?.error_description === 'string' ? error.error_description : 'Could not complete sign-in.';
    throw new TokenEndpointError(response.status, code, description);
  }

  return parseTokenResponse(payload);
}

async function randomUrlSafe(byteLength: number): Promise<string> {
  const bytes = await Crypto.getRandomBytesAsync(byteLength);
  return base64Url(bytes);
}

async function sha256Base64Url(value: string): Promise<string> {
  const digest = await Crypto.digestStringAsync(Crypto.CryptoDigestAlgorithm.SHA256, value, {
    encoding: Crypto.CryptoEncoding.BASE64,
  });
  return digest.replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/g, '');
}

function base64Url(bytes: Uint8Array): string {
  let binary = '';
  for (const byte of bytes) {
    binary += String.fromCharCode(byte);
  }

  return btoa(binary).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/g, '');
}
