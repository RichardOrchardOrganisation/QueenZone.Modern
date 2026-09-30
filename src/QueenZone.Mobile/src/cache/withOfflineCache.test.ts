import assert from 'node:assert/strict';
import { register } from 'node:module';
import { describe, it, mock } from 'node:test';
import { pathToFileURL } from 'node:url';
import { ApiError } from '../api/errors.ts';

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

const { ContentCache } = await import('./contentCache.ts');
const { createMemoryStorage } = await import('./storage.ts');
const { withOfflineCacheResult } = await import('./withOfflineCache.ts');

function newCache() {
  return new ContentCache({ storage: createMemoryStorage() });
}

// Background revalidation is fire-and-forget; flush the microtask queue so
// its `.then`/`.catch` chain settles before assertions run.
function flush(): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, 0));
}

describe('withOfflineCacheResult TTL / stale-while-revalidate', () => {
  it('goes to the network when there is no cached entry, even with a ttlMs set', async () => {
    const cache = newCache();
    const fetchFresh = mock.fn(async () => ({ value: 'fresh' }));

    const result = await withOfflineCacheResult(cache, 'k', fetchFresh, { ttlMs: 60_000 });

    assert.deepEqual(result, { data: { value: 'fresh' }, source: 'network', cachedAt: result.cachedAt });
    assert.equal(fetchFresh.mock.calls.length, 1);
  });

  it('serves the cached value immediately without waiting on the network', async () => {
    const cache = newCache();
    await cache.put('k', { value: 'cached' });
    let releaseFetch!: (value: { value: string }) => void;
    const fetchFresh = mock.fn(
      () =>
        new Promise<{ value: string }>((resolve) => {
          releaseFetch = resolve;
        }),
    );

    const result = await withOfflineCacheResult(cache, 'k', fetchFresh, { ttlMs: 60_000 });

    assert.equal(result.source, 'cache');
    assert.deepEqual(result.data, { value: 'cached' });

    releaseFetch({ value: 'revalidated' });
    await flush();
  });

  it('revalidates in the background and updates the cache for the next read', async () => {
    const cache = newCache();
    await cache.put('k', { value: 'cached' });
    const fetchFresh = mock.fn(async () => ({ value: 'revalidated' }));

    const result = await withOfflineCacheResult(cache, 'k', fetchFresh, { ttlMs: 60_000 });
    assert.equal(result.source, 'cache');

    await flush();

    assert.equal(fetchFresh.mock.calls.length, 1);
    assert.deepEqual(await cache.get('k'), { value: 'revalidated' });
  });

  it('goes to the network once the cached entry is older than ttlMs', async () => {
    const cache = newCache();
    await cache.put('k', { value: 'stale' });
    const fetchFresh = mock.fn(async () => ({ value: 'fresh' }));

    const result = await withOfflineCacheResult(cache, 'k', fetchFresh, { ttlMs: -1 });

    assert.equal(result.source, 'network');
    assert.deepEqual(result.data, { value: 'fresh' });
    assert.equal(fetchFresh.mock.calls.length, 1);
  });

  it('never short-circuits on the network-only pull-to-refresh path (fallback: false)', async () => {
    const cache = newCache();
    await cache.put('k', { value: 'cached' });
    const fetchFresh = mock.fn(async () => ({ value: 'fresh' }));

    const result = await withOfflineCacheResult(cache, 'k', fetchFresh, {
      ttlMs: 60_000,
      fallback: false,
    });

    assert.equal(result.source, 'network');
    assert.equal(fetchFresh.mock.calls.length, 1);
  });

  it('deduplicates concurrent background revalidations for the same key', async () => {
    const cache = newCache();
    await cache.put('k', { value: 'cached' });
    const response = Promise.withResolvers<{ value: string }>();
    const fetchFresh = mock.fn(() => response.promise);
    await Promise.all([
      withOfflineCacheResult(cache, 'k', fetchFresh, { ttlMs: 60_000 }),
      withOfflineCacheResult(cache, 'k', fetchFresh, { ttlMs: 60_000 }),
    ]);
    assert.equal(fetchFresh.mock.calls.length, 1);
    response.resolve({ value: 'revalidated' });
    await flush();
  });

  it('swallows a failed background revalidation and keeps the stale value cached', async () => {
    const cache = newCache();
    await cache.put('k', { value: 'cached' });
    const fetchFresh = mock.fn(async () => {
      throw ApiError.offline();
    });

    const result = await withOfflineCacheResult(cache, 'k', fetchFresh, { ttlMs: 60_000 });
    assert.equal(result.source, 'cache');

    await flush();

    assert.deepEqual(await cache.get('k'), { value: 'cached' });
  });

  it('removes the cached entry when a background revalidation hits an invalidateOn status', async () => {
    const cache = newCache();
    await cache.put('k', { value: 'cached' });
    const fetchFresh = mock.fn(async () => {
      throw ApiError.http(401, 'Unauthorized.');
    });

    await withOfflineCacheResult(cache, 'k', fetchFresh, { ttlMs: 60_000, invalidateOn: [401] });

    await flush();

    assert.equal(await cache.get('k'), null);
  });

  it('swallows an aborted background revalidation without touching the cache', async () => {
    const cache = newCache();
    await cache.put('k', { value: 'cached' });
    const abortError = new Error('aborted');
    abortError.name = 'AbortError';
    const fetchFresh = mock.fn(async () => {
      throw abortError;
    });

    await withOfflineCacheResult(cache, 'k', fetchFresh, { ttlMs: 60_000 });
    await flush();

    assert.deepEqual(await cache.get('k'), { value: 'cached' });
  });

  it('allows a new background revalidation after the previous one settles', async () => {
    const cache = newCache();
    await cache.put('k', { value: 'cached' });
    const fetchFresh = mock.fn(async () => ({ value: 'revalidated' }));

    await withOfflineCacheResult(cache, 'k', fetchFresh, { ttlMs: 60_000 });
    await flush();
    await withOfflineCacheResult(cache, 'k', fetchFresh, { ttlMs: 60_000 });
    await flush();

    assert.equal(fetchFresh.mock.calls.length, 2);
  });
});


it('does not repersist private data from a background revalidation after purge', async () => {
  const cache = newCache();
  const key = 'private:member-a:conversation';
  await cache.put(key, { value: 'cached' });
  const response = Promise.withResolvers<{ value: string }>();
  const result = await withOfflineCacheResult(cache, key, () => response.promise, { ttlMs: 60_000 });
  assert.equal(result.source, 'cache');
  await cache.purgePrefix('private:');
  response.resolve({ value: 'old session' });
  await response.promise; // Background continuation queues its write before this read.
  assert.equal(await cache.get(key), null);
});

it('late unauthorized responses do not delete a new session snapshot', async () => {
  const cache = newCache();
  const key = 'private:member-a:conversation';
  const response = Promise.withResolvers<never>();
  const pending = withOfflineCacheResult(cache, key, () => response.promise, { invalidateOn: [401] });
  const rejected = assert.rejects(pending, ApiError);
  await cache.purgePrefix('private:');
  await cache.put(key, { value: 'new session' });
  response.reject(new ApiError(401, 'Unauthorized'));
  await rejected;
  assert.deepEqual(await cache.get(key), { value: 'new session' });
});

it('new-session SWR can start while an invalidated same-key refresh is pending', async () => {
  const cache = newCache();
  const key = 'private:member-a:conversation';
  await cache.put(key, { value: 'old cached' });
  const old = Promise.withResolvers<{ value: string }>();
  await withOfflineCacheResult(cache, key, () => old.promise, { ttlMs: 60_000 });
  await cache.purgePrefix('private:');
  await cache.put(key, { value: 'new cached' });
  const fresh = Promise.withResolvers<{ value: string }>();
  let calls = 0;
  await withOfflineCacheResult(cache, key, () => { calls++; return fresh.promise; }, { ttlMs: 60_000 });
  assert.equal(calls, 1);
  old.resolve({ value: 'old refreshed' });
  await old.promise;
  fresh.resolve({ value: 'new refreshed' });
  await fresh.promise;
  assert.deepEqual(await cache.get(key), { value: 'new refreshed' });
});

for (const mode of ['swr', 'offline'] as const) {
  it(`does not cross a purge between cache-read completion and ${mode} continuation`, async () => {
    const readFinished = Promise.withResolvers<void>();
    const resume = Promise.withResolvers<void>();
    class PausedReadCache extends ContentCache {
      override async read<T>(key: string, lease?: import('./contentCache.ts').CacheLease) {
        const record = await super.read<T>(key, lease);
        readFinished.resolve();
        await resume.promise;
        return record;
      }
    }
    const storage = createMemoryStorage();
    const cache = new PausedReadCache({ storage });
    const key = 'private:member-a:conversation';
    await cache.put(key, { value: 'private cached' });
    const response = Promise.withResolvers<{ value: string }>();
    const pending = withOfflineCacheResult(cache, key,
      mode === 'swr' ? () => response.promise : () => Promise.reject(ApiError.offline()),
      mode === 'swr' ? { ttlMs: 60_000 } : {});
    const expected = mode === 'offline' ? assert.rejects(pending, ApiError) : pending;
    await readFinished.promise;
    await cache.purgePrefix('private:');
    resume.resolve();
    response.resolve({ value: 'old response' });
    await expected;
    assert.equal(await storage.getItem(cache.entryKey(key)), null);
  });
}

for (const mode of ['swr', 'offline'] as const) {
  it(`rechecks ownership after the cached-result helper resolves (${mode})`, async () => {
    let purge: Promise<void> | undefined;
    class CompletionBoundaryCache extends ContentCache {
      override read<T>(_key: string, _lease?: import('./contentCache.ts').CacheLease) {
        // The first microtask precedes the helper's continuation. The second
        // invalidates after its check but before the caller resumes.
        queueMicrotask(() => queueMicrotask(() => { purge = this.purgePrefix('private:'); }));
        return Promise.resolve({ payload: { value: 'private cached' } as T, cachedAt: new Date().toISOString() });
      }
    }
    const cache = new CompletionBoundaryCache({ storage: createMemoryStorage() });
    const pending = withOfflineCacheResult(cache, 'private:member-a:conversation',
      mode === 'swr' ? async () => ({ value: 'network' }) : async () => { throw ApiError.offline(); },
      mode === 'swr' ? { ttlMs: 60_000 } : {});
    if (mode === 'offline') {
      await assert.rejects(pending, ApiError);
    } else {
      const result = await pending;
      assert.equal(result.source, 'network');
      assert.deepEqual(result.data, { value: 'network' });
    }
    assert.ok(purge);
    await purge;
  });
}
