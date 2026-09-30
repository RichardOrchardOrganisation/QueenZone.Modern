import type { CacheLease, CacheRecord, ContentCache } from './contentCache';

export type CacheSource = 'network' | 'cache';

export type CachedResult<T> = {
  data: T;
  source: CacheSource;
  cachedAt: string;
};

export type OfflineCacheOptions = {
  /**
   * HTTP statuses that delete the cached key before rethrowing.
   * Online 401/403/404 are authoritative for private data; 404 for public topics.
   */
  invalidateOn?: readonly number[];
  /** Network-only (pull-to-refresh): write-through on success, never serve cache. */
  fallback?: boolean;
  /**
   * Stale-while-revalidate window. A cached entry younger than this is served
   * immediately (no network round trip) while a fresh fetch runs in the
   * background to update the cache for the next call. Ignored when
   * `fallback: false` (pull-to-refresh stays network-only). Unset means the
   * existing network-first behaviour (no TTL short-circuit).
   */
  ttlMs?: number;
};

function isAbortError(err: unknown): boolean {
  return err instanceof Error && err.name === 'AbortError';
}

function apiKind(err: unknown): unknown {
  return err instanceof Error && err.name === 'ApiError' && 'kind' in err
    ? (err as { kind: unknown }).kind
    : undefined;
}

function isCacheFallbackFailure(err: unknown): boolean {
  const kind = apiKind(err);
  return kind === 'offline' || kind === 'timeout';
}

function httpStatus(err: unknown): number | null {
  if (!(err instanceof Error) || err.name !== 'ApiError' || !('status' in err)) {
    return null;
  }
  const status = (err as { status: unknown }).status;
  return typeof status === 'number' ? status : null;
}

function cacheAgeMs(cachedAt: string): number {
  const parsed = Date.parse(cachedAt);
  return Number.isNaN(parsed) ? Number.POSITIVE_INFINITY : Date.now() - parsed;
}

// Tracks cacheKeys with a background revalidation in flight per ContentCache
// instance, so concurrent SWR hits (e.g. two screens reading the same key)
// only trigger one network call. Scoped by instance so tests using a fresh
// ContentCache never see another test's in-flight state.
const revalidating = new WeakMap<ContentCache, Map<string, CacheLease>>();

function beginRevalidation(cache: ContentCache, cacheKey: string): CacheLease | null {
  let keys = revalidating.get(cache);
  if (!keys) {
    keys = new Map();
    revalidating.set(cache, keys);
  }
  if (keys.get(cacheKey)?.current) return null;
  const lease = cache.acquireLease(cacheKey);
  keys.set(cacheKey, lease);
  return lease;
}

function endRevalidation(cache: ContentCache, cacheKey: string, lease: CacheLease): void {
  if (revalidating.get(cache)?.get(cacheKey) === lease) revalidating.get(cache)?.delete(cacheKey);
  lease.release();
}

/**
 * Fire-and-forget refresh behind a stale-while-revalidate hit. Mirrors the
 * invalidateOn/abort handling of the foreground path, but never surfaces a
 * failure — the caller already has stale data to show.
 */
function revalidateInBackground<T>(
  cache: ContentCache,
  cacheKey: string,
  fetchFresh: () => Promise<T>,
  invalidateOn: readonly number[],
): void {
  const lease = beginRevalidation(cache, cacheKey);
  if (!lease) {
    return;
  }
  void (async () => {
    try {
      const data = await fetchFresh();
      try {
        await cache.put(cacheKey, data, lease);
      } catch {
        // Device store full/unavailable: stale value stays put until it ages out.
      }
    } catch (err) {
      if (isAbortError(err)) {
        return;
      }
      const status = httpStatus(err);
      if (status !== null && invalidateOn.includes(status)) {
        try {
          await cache.remove(cacheKey, lease);
        } catch {}
      }
    } finally {
      endRevalidation(cache, cacheKey, lease);
    }
  })();
}

/**
 * Network-first with optional offline/timeout cache fallback.
 * Returns provenance so screens can show “Offline · last updated …”.
 */
export async function withOfflineCacheResult<T>(
  cache: ContentCache,
  cacheKey: string,
  fetchFresh: () => Promise<T>,
  options: OfflineCacheOptions = {},
): Promise<CachedResult<T>> {
  const lease = cache.acquireLease(cacheKey);
  try {
    return await readWithLease(cache, cacheKey, fetchFresh, options, lease);
  } finally {
    lease.release();
  }
}

async function readWithLease<T>(
  cache: ContentCache,
  cacheKey: string,
  fetchFresh: () => Promise<T>,
  options: OfflineCacheOptions,
  lease: CacheLease,
): Promise<CachedResult<T>> {
  const fallback = options.fallback !== false;
  const invalidateOn = options.invalidateOn ?? [];

  if (fallback && options.ttlMs !== undefined && options.ttlMs > 0) {
    let fresh: CacheRecord<T> | null = null;
    try {
      fresh = await cache.read<T>(cacheKey, lease);
    } catch {}
    if (lease.current && fresh !== null && cacheAgeMs(fresh.cachedAt) < options.ttlMs) {
      revalidateInBackground(cache, cacheKey, fetchFresh, invalidateOn);
      return { data: fresh.payload, source: 'cache', cachedAt: fresh.cachedAt };
    }
  }

  try {
    const data = await fetchFresh();
    let cachedAt = new Date().toISOString();
    try {
      cachedAt = await cache.put(cacheKey, data, lease);
    } catch {
      // Read path still succeeds if the device store is full or unavailable.
    }
    return { data, source: 'network', cachedAt };
  } catch (err) {
    if (isAbortError(err)) {
      throw err;
    }

    const status = httpStatus(err);
    if (status !== null && invalidateOn.includes(status)) {
      try {
        await cache.remove(cacheKey, lease);
      } catch {}
      throw err;
    }

    if (!fallback || !isCacheFallbackFailure(err)) {
      throw err;
    }

    try {
      const cached = await cache.read<T>(cacheKey, lease);
      if (lease.current && cached !== null) {
        return { data: cached.payload, source: 'cache', cachedAt: cached.cachedAt };
      }
    } catch {}
    throw err;
  }
}

/**
 * Convenience wrapper for screens that do not need provenance
 * (news / biography / discography details).
 */
export async function withOfflineCache<T>(
  cache: ContentCache,
  cacheKey: string,
  fetchFresh: () => Promise<T>,
  options?: OfflineCacheOptions,
): Promise<T> {
  const result = await withOfflineCacheResult(cache, cacheKey, fetchFresh, options);
  return result.data;
}
