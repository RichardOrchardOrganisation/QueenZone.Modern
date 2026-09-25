/** Mobile OAuth2 PKCE helpers. Native browser hop lives in `session/oauth.ts`. */

import { trimTrailingChar } from '../text/trimRuns';

export const mobileClientId = 'queenzone-mobile';

export const mobileRedirectUri = 'queenzone://auth/callback';

export type AuthProvider = {
  id: string;
  label: string;
};

export type AuthTokens = {
  accessToken: string;
  refreshToken: string;
  expiresIn: number;
};

export type AuthCallback = {
  code?: string;
  state?: string;
  error?: string;
  errorDescription?: string;
};

export const fallbackAuthProviders: AuthProvider[] = [
  { id: 'Google', label: 'Continue with Google' },
  { id: 'Microsoft', label: 'Continue with Microsoft' },
  { id: 'Discord', label: 'Continue with Discord' },
  { id: 'GitHub', label: 'Continue with GitHub' },
  { id: 'Apple', label: 'Continue with Apple' },
];

export function authAuthorizeUrl(apiBaseUrl: string): string {
  return `${origin(apiBaseUrl)}/api/v1/auth/authorize`;
}

export function authTokenUrl(apiBaseUrl: string): string {
  return `${origin(apiBaseUrl)}/api/v1/auth/token`;
}

export function authRevokeUrl(apiBaseUrl: string): string {
  return `${origin(apiBaseUrl)}/api/v1/auth/revoke`;
}

export function authLogoutUrl(apiBaseUrl: string): string {
  return `${origin(apiBaseUrl)}/api/v1/auth/logout`;
}

export function authProvidersUrl(apiBaseUrl: string): string {
  return `${origin(apiBaseUrl)}/api/v1/auth/providers`;
}

export function buildAuthorizeUrl(input: {
  apiBaseUrl: string;
  provider: string;
  state: string;
  codeChallenge: string;
  redirectUri?: string;
  clientId?: string;
}): string {
  const params = new URLSearchParams({
    response_type: 'code',
    client_id: input.clientId ?? mobileClientId,
    redirect_uri: input.redirectUri ?? mobileRedirectUri,
    code_challenge: input.codeChallenge,
    code_challenge_method: 'S256',
    state: input.state,
    provider: input.provider,
  });
  return `${authAuthorizeUrl(input.apiBaseUrl)}?${params.toString()}`;
}

export function parseAuthCallback(url: string): AuthCallback {
  const parsed = tryParseUrl(url);
  if (!parsed) {
    return { error: 'invalid_request', errorDescription: 'Sign-in did not return a valid callback.' };
  }

  const error = parsed.searchParams.get('error') ?? undefined;
  return {
    code: parsed.searchParams.get('code') ?? undefined,
    state: parsed.searchParams.get('state') ?? undefined,
    error,
    errorDescription: parsed.searchParams.get('error_description') ?? undefined,
  };
}

export function parseAuthProviders(payload: unknown): AuthProvider[] {
  if (!payload || typeof payload !== 'object') {
    return fallbackAuthProviders;
  }

  const raw = payload as { providers?: unknown };
  if (!Array.isArray(raw.providers)) {
    return fallbackAuthProviders;
  }

  const providers = raw.providers.flatMap((item) => {
    if (!item || typeof item !== 'object') {
      return [];
    }

    const provider = item as { id?: unknown; label?: unknown };
    if (typeof provider.id !== 'string' || typeof provider.label !== 'string') {
      return [];
    }

    return [{ id: provider.id, label: provider.label }];
  });

  return providers.length > 0 ? providers : fallbackAuthProviders;
}

export function parseTokenResponse(payload: unknown): AuthTokens {
  if (!payload || typeof payload !== 'object') {
    throw new Error('Token response was empty.');
  }

  const raw = payload as Record<string, unknown>;
  if (typeof raw.access_token !== 'string' || typeof raw.refresh_token !== 'string') {
    const description = typeof raw.error_description === 'string' ? raw.error_description : 'Sign-in did not return tokens.';
    throw new Error(description);
  }

  return {
    accessToken: raw.access_token,
    refreshToken: raw.refresh_token,
    expiresIn: typeof raw.expires_in === 'number' && raw.expires_in > 0 ? Math.trunc(raw.expires_in) : 900,
  };
}

export function tokenFormBody(input: Record<string, string>): string {
  const params = new URLSearchParams();
  for (const [key, value] of Object.entries(input)) {
    params.set(key, value);
  }

  return params.toString();
}

function origin(apiBaseUrl: string): string {
  return trimTrailingChar(apiBaseUrl, '/');
}

function tryParseUrl(url: string): URL | null {
  try {
    return new URL(url);
  } catch {
    return null;
  }
}
