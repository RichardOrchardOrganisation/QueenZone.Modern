import type { ProblemDetails } from './types';

export type ApiFailureKind = 'timeout' | 'offline' | 'http' | 'malformed' | 'local-file';

export const TIMEOUT_MESSAGE =
  'QueenZone is taking too long to respond. Check your connection and try again.';

export const OFFLINE_MESSAGE = 'Unable to reach QueenZone. Check your connection and try again.';

export const MALFORMED_MESSAGE = 'QueenZone sent a response we could not read.';

export const LOCAL_FILE_MESSAGE = 'Could not read the selected photo. Try choosing it again.';

export class ApiError extends Error {
  readonly kind: ApiFailureKind;
  readonly status: number;
  readonly problem: ProblemDetails | null;
  retryAfterMs: number | null;

  constructor(
    status: number,
    message: string,
    problem: ProblemDetails | null = null,
    kind?: ApiFailureKind,
    cause?: unknown,
  ) {
    super(message, cause === undefined ? undefined : { cause });
    this.name = 'ApiError';
    this.status = status;
    this.problem = problem;
    this.kind = kind ?? (status === 0 ? 'offline' : 'http');
    this.retryAfterMs = null;
  }

  static timeout(cause?: unknown): ApiError {
    return new ApiError(0, TIMEOUT_MESSAGE, null, 'timeout', cause);
  }

  static offline(cause?: unknown): ApiError {
    return new ApiError(0, OFFLINE_MESSAGE, null, 'offline', cause);
  }

  static localFile(cause?: unknown): ApiError {
    return new ApiError(0, LOCAL_FILE_MESSAGE, null, 'local-file', cause);
  }

  static http(status: number, message: string, problem: ProblemDetails | null = null): ApiError {
    return new ApiError(status, message, problem, 'http');
  }

  static malformed(status: number): ApiError {
    return new ApiError(status, MALFORMED_MESSAGE, null, 'malformed');
  }
}

/**
 * Non-2xx from `/api/v1/auth/token`, carrying the OAuth2 `error` code alongside
 * the HTTP status. Refresh classification needs both: a 400 `invalid_grant` is a
 * dead grant worth signing out for, while a 400 `temporarily_unavailable`, a 429
 * from the per-account limiter, or a 502 from a cold start is a server having a
 * bad minute and must not end the session.
 */
export class TokenEndpointError extends ApiError {
  readonly oauthError: string;

  constructor(status: number, oauthError: string, message: string) {
    super(status, message, null, status === 0 ? 'offline' : 'http');
    this.name = 'TokenEndpointError';
    this.oauthError = oauthError;
  }
}

export function isTimeoutFailure(err: unknown): err is ApiError & { kind: 'timeout' } {
  return err instanceof ApiError && err.kind === 'timeout';
}

export function isOfflineFailure(err: unknown): err is ApiError & { kind: 'offline' } {
  return err instanceof ApiError && err.kind === 'offline';
}

export function isLocalFileFailure(err: unknown): err is ApiError & { kind: 'local-file' } {
  return err instanceof ApiError && err.kind === 'local-file';
}

function errorText(err: unknown): { name: string; message: string } {
  if (err instanceof Error) {
    return { name: err.name, message: err.message };
  }
  return { name: '', message: typeof err === 'string' ? err : '' };
}

/** Expo iOS fetch cancel — not a DOM AbortError, not a real offline failure. */
export function isExpoFetchCanceled(err: unknown): boolean {
  const { name, message } = errorText(err);
  return name === 'FetchRequestCanceledException' || message.includes('FetchRequestCanceledException');
}

/** Expo wraps the same cancel as UnexpectedException + this message. */
export function isLostConnectionMessage(err: unknown): boolean {
  return /network connection was lost/i.test(errorText(err).message);
}
