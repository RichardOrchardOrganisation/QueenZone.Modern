import assert from 'node:assert/strict';
import { register } from 'node:module';
import { describe, it } from 'node:test';
import { pathToFileURL } from 'node:url';

register(
  `data:text/javascript,${encodeURIComponent(`
    export async function resolve(specifier, context, nextResolve) {
      if (specifier.startsWith('.') && !/\\\\.(?:[cm]?[jt]s|json)$/.test(specifier)) {
        try {
          return await nextResolve(specifier + '.ts', context);
        } catch {
          return nextResolve(specifier, context);
        }
      }
      return nextResolve(specifier, context);
    }
  `)}`,
  pathToFileURL('./'),
);

const { ApiError, TokenEndpointError } = await import('../api/errors.ts');
const { isDefiniteAuthRefreshFailure, isTransientRefreshFailure } = await import('./refreshFailure.ts');

describe('refresh failure classification', () => {
  it('treats 401 and invalid_grant as definite auth failure', () => {
    assert.equal(isDefiniteAuthRefreshFailure(ApiError.http(401, 'Unauthorized')), true);
    assert.equal(isDefiniteAuthRefreshFailure(new Error('invalid_grant')), true);
    assert.equal(isTransientRefreshFailure(new Error('invalid_grant')), false);
  });

  it('retains identity for offline and timeout refresh failures', () => {
    assert.equal(isTransientRefreshFailure(ApiError.offline()), true);
    assert.equal(isTransientRefreshFailure(ApiError.timeout()), true);
    assert.equal(isTransientRefreshFailure(new TypeError('Network request failed')), true);
    assert.equal(isDefiniteAuthRefreshFailure(new TypeError('Network request failed')), false);
  });

  it('treats a token-endpoint invalid_grant as definite whatever the status', () => {
    const dead = new TokenEndpointError(400, 'invalid_grant', 'invalid_grant');
    assert.equal(isDefiniteAuthRefreshFailure(dead), true);
    assert.equal(isTransientRefreshFailure(dead), false);
    assert.equal(
      isDefiniteAuthRefreshFailure(new TokenEndpointError(401, '', 'invalid_grant')),
      true,
    );
  });

  it('keeps the session for a plain 429 or 5xx ApiError from any caller', () => {
    for (const err of [ApiError.http(429, 'Too Many Requests'), ApiError.http(503, 'Service Unavailable')]) {
      assert.equal(isDefiniteAuthRefreshFailure(err), false);
      assert.equal(isTransientRefreshFailure(err), true);
    }
  });

  it('keeps the session for rate limits, outages, and unconfigured auth', () => {
    // Each of these used to fall through to clearLocal() and sign the member
    // out on the next launch after an overnight idle.
    const rateLimited = new TokenEndpointError(
      429,
      'temporarily_unavailable',
      'Too many attempts. Try again later.',
    );
    const notConfigured = new TokenEndpointError(
      400,
      'temporarily_unavailable',
      'Mobile auth is not configured.',
    );
    const coldStart = new TokenEndpointError(503, '', 'Could not complete sign-in.');

    for (const err of [rateLimited, notConfigured, coldStart]) {
      assert.equal(isDefiniteAuthRefreshFailure(err), false);
      assert.equal(isTransientRefreshFailure(err), true);
    }
  });
});
