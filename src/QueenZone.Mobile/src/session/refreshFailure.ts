import { ApiError, isOfflineFailure, isTimeoutFailure, TokenEndpointError } from '../api/errors';

function errorText(err: unknown): string {
  if (err instanceof Error) {
    return `${err.name} ${err.message}`;
  }
  return typeof err === 'string' ? err : '';
}

/** OAuth2 `error` codes that mean the grant itself is gone and cannot be retried. */
const deadGrantCodes = new Set(['invalid_grant', 'invalid_token', 'invalid_client', 'unauthorized_client']);

/** Token endpoint said the grant is dead. Sign the member out. */
export function isDefiniteAuthRefreshFailure(err: unknown): boolean {
  if (err instanceof TokenEndpointError) {
    return err.status === 401 || deadGrantCodes.has(err.oauthError);
  }

  if (err instanceof ApiError && (err.status === 401 || err.status === 400)) {
    return true;
  }

  return /invalid_grant|invalid_token|unauthorized/i.test(errorText(err));
}

/**
 * Anything short of "the grant is dead". Keep the local member identity so
 * same-account downloads stay playable offline and the next launch can retry.
 *
 * The token endpoint answering at all without a dead-grant code is transient by
 * default: a 429 from the per-account limiter, a `temporarily_unavailable` from
 * an unconfigured signing key, and a 502/503 from an App Service cold start all
 * used to fall through to sign-out because they were neither "definite" nor
 * recognisably network-shaped.
 */
export function isTransientRefreshFailure(err: unknown): boolean {
  if (isDefiniteAuthRefreshFailure(err)) {
    return false;
  }
  if (err instanceof TokenEndpointError) {
    return true;
  }
  if (err instanceof ApiError && (err.status === 429 || err.status >= 500)) {
    return true;
  }
  if (isOfflineFailure(err) || isTimeoutFailure(err)) {
    return true;
  }
  if (err instanceof TypeError) {
    return true;
  }

  return /offline|timeout|network|failed to fetch|network request failed|internet|connection|enotfound|econnrefused/i.test(
    errorText(err),
  );
}
