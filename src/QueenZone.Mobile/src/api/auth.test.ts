import assert from 'node:assert/strict';
import { describe, it } from 'node:test';
import {
  buildAuthorizeUrl,
  mobileClientId,
  mobileRedirectUri,
  parseAuthCallback,
  parseAuthProviders,
  parseTokenResponse,
  tokenFormBody,
} from './auth.ts';

describe('buildAuthorizeUrl', () => {
  it('includes PKCE and provider query parameters', () => {
    const url = buildAuthorizeUrl({
      apiBaseUrl: 'https://www.queenzone.org',
      provider: 'Google',
      state: 'abc',
      codeChallenge: 'challenge-value',
    });
    assert.match(url, /^https:\/\/www\.queenzone\.org\/api\/v1\/auth\/authorize\?/);
    assert.match(url, /client_id=queenzone-mobile/);
    assert.match(url, /code_challenge_method=S256/);
    assert.match(url, /provider=Google/);
    assert.match(url, /redirect_uri=queenzone%3A%2F%2Fauth%2Fcallback/);
    assert.equal(mobileClientId, 'queenzone-mobile');
    assert.equal(mobileRedirectUri, 'queenzone://auth/callback');
  });

  it('strips trailing slashes on the API origin without stacking path separators', () => {
    const url = buildAuthorizeUrl({
      apiBaseUrl: 'https://www.queenzone.org///',
      provider: 'Google',
      state: 'abc',
      codeChallenge: 'challenge-value',
    });
    assert.match(url, /^https:\/\/www\.queenzone\.org\/api\/v1\/auth\/authorize\?/);
    assert.equal(buildAuthorizeUrl({
      apiBaseUrl: '',
      provider: 'Google',
      state: 'abc',
      codeChallenge: 'c',
    }).startsWith('/api/v1/auth/authorize?'), true);
  });

  it('finishes quickly when the origin is a long slash run', () => {
    const started = performance.now();
    const url = buildAuthorizeUrl({
      apiBaseUrl: `https://www.queenzone.org${'/'.repeat(40_000)}`,
      provider: 'Google',
      state: 'abc',
      codeChallenge: 'c',
    });
    assert.match(url, /^https:\/\/www\.queenzone\.org\/api\/v1\/auth\/authorize\?/);
    assert.ok(performance.now() - started < 100);
  });
});

describe('parseAuthCallback', () => {
  it('reads code and error from the app redirect', () => {
    const ok = parseAuthCallback('queenzone://auth/callback?code=xyz&state=abc');
    assert.equal(ok.code, 'xyz');
    assert.equal(ok.state, 'abc');

    const failed = parseAuthCallback(
      'queenzone://auth/callback?error=access_denied&error_description=Cancelled',
    );
    assert.equal(failed.error, 'access_denied');
    assert.equal(failed.errorDescription, 'Cancelled');
  });
});

describe('parseAuthProviders', () => {
  it('falls back to website provider labels when the list is empty', () => {
    const providers = parseAuthProviders({ providers: [{ id: 'Google', label: 'Continue with Google' }] });
    assert.equal(providers[0]?.id, 'Google');
    assert.equal(parseAuthProviders({ providers: [] })[0]?.id, 'Google');
  });
});

describe('parseTokenResponse', () => {
  it('reads RFC 6749 token names', () => {
    const tokens = parseTokenResponse({
      access_token: 'a',
      refresh_token: 'r',
      token_type: 'Bearer',
      expires_in: 900,
    });
    assert.equal(tokens.accessToken, 'a');
    assert.equal(tokens.refreshToken, 'r');
    assert.equal(tokens.expiresIn, 900);
  });
});

describe('tokenFormBody', () => {
  it('encodes application/x-www-form-urlencoded fields', () => {
    assert.equal(tokenFormBody({ grant_type: 'authorization_code', code: 'a b' }), 'grant_type=authorization_code&code=a+b');
  });
});
