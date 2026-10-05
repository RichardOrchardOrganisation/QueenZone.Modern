import { fetchJson, type FetchJsonOptions } from '../api/client';
import { getContentCache } from './defaultCache';
import type { CacheLease, ContentCache } from './contentCache';
import { withOfflineCacheResult, type CachedResult, type OfflineCacheOptions } from './withOfflineCache';

export type FetchCachedOptions = FetchJsonOptions &
  OfflineCacheOptions & {
    /** Stable key within the content cache (e.g. `news:42`). */
    cacheKey: string;
    /** Override the default AsyncStorage-backed cache (tests). */
    cache?: ContentCache;
  };

type SharedFlight = {
  lease: CacheLease;
  promise: Promise<CachedResult<unknown>>;
  controller: AbortController;
  subscribers: number;
  settled: boolean;
};

const inFlight = new WeakMap<ContentCache, Map<string, SharedFlight>>();

function aborted(): Error {
  return Object.assign(new Error('Aborted'), { name: 'AbortError' });
}

function subscribe<T>(flight: SharedFlight, signal: AbortSignal | undefined, abandon: () => void): Promise<CachedResult<T>> {
  flight.subscribers++;
  return new Promise((resolve, reject) => {
    let active = true;
    const release = () => {
      if (!active) return;
      active = false;
      signal?.removeEventListener('abort', onAbort);
      flight.subscribers--;
    };
    const onAbort = () => {
      release();
      reject(aborted());
      if (!flight.settled && flight.subscribers === 0) abandon();
    };
    signal?.addEventListener('abort', onAbort);
    flight.promise.then(
      (value) => { if (active) { release(); resolve(value as CachedResult<T>); } },
      (error: unknown) => { if (active) { release(); reject(error); } },
    );
  });
}

/**
 * Network-first fetch with offline cache fallback for previously opened details.
 * Mirrors the PWA navigate strategy in `wwwroot/sw.js`.
 * Concurrent callers with the same `cacheKey` share a transport. Each caller
 * cancels its own subscription; only the last cancellation aborts the transport.
 */
export async function fetchJsonWithOfflineCache<T>(
  path: string,
  options: FetchCachedOptions,
): Promise<T> {
  const result = await fetchJsonWithOfflineCacheResult<T>(path, options);
  return result.data;
}

export async function fetchJsonWithOfflineCacheResult<T>(
  path: string,
  options: FetchCachedOptions,
): Promise<CachedResult<T>> {
  const { cacheKey, cache = getContentCache(), invalidateOn, fallback, ttlMs, signal, ...fetchOptions } = options;
  if (signal?.aborted) throw aborted();

  let flights = inFlight.get(cache);
  if (!flights) {
    flights = new Map();
    inFlight.set(cache, flights);
  }
  const existing = flights.get(cacheKey);
  if (existing?.lease.current) {
    return subscribe<T>(existing, signal, () => {
      existing.controller.abort();
      if (flights.get(cacheKey) === existing) flights.delete(cacheKey);
    });
  }
  const lease = cache.acquireLease(cacheKey);
  const controller = new AbortController();

  const pending = withOfflineCacheResult(cache, cacheKey, async () => {
    const value = await fetchJson<T>(path, { ...fetchOptions, signal: controller.signal });
    if (controller.signal.aborted) throw aborted();
    return value;
  }, {
    invalidateOn,
    fallback,
    ttlMs,
  }).finally(() => {
    flight.settled = true;
    lease.release();
    if (flights.get(cacheKey)?.promise === pending) {
      flights.delete(cacheKey);
    }
  });

  const flight: SharedFlight = { lease, promise: pending, controller, subscribers: 0, settled: false };
  flights.set(cacheKey, flight);
  return subscribe<T>(flight, signal, () => {
    controller.abort();
    if (flights.get(cacheKey) === flight) flights.delete(cacheKey);
  });
}

export type { CachedResult };
