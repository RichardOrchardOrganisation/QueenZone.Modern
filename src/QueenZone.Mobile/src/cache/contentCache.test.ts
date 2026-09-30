import assert from 'node:assert/strict';
import { register } from 'node:module';
import { describe, it } from 'node:test';
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

const { CONTENT_CACHE_SCHEMA_VERSION, ContentCache } = await import('./contentCache.ts');
const { withOfflineCache, withOfflineCacheResult } = await import('./withOfflineCache.ts');
const { createMemoryStorage } = await import('./storage.ts');
const {
  conversationCacheKey,
  forumTopicCacheKey,
  forumTopicPostsCacheKey,
  PRIVATE_CACHE_KEY_PREFIX,
  privateMemberCachePrefix,
} = await import('./keys.ts');
const { invalidateIncompatiblePostPages, pagedTailIncompatible } = await import('./pagedCache.ts');

function pagedPosts(
  ids: number[],
  page: number,
  totalCount: number,
  totalPages: number,
) {
  return {
    items: ids.map((id) => ({ id })),
    page,
    pageSize: 15,
    totalCount,
    totalPages,
  };
}

describe('ContentCache', () => {
  it('stores and returns payloads', async () => {
    const cache = new ContentCache({ storage: createMemoryStorage() });
    await cache.put('news:1', { id: 1, title: 'Hello' });
    assert.deepEqual(await cache.get('news:1'), { id: 1, title: 'Hello' });
  });

  it('returns provenance from read()', async () => {
    const cache = new ContentCache({ storage: createMemoryStorage() });
    const cachedAt = await cache.put('news:1', { id: 1 });
    const record = await cache.read<{ id: number }>('news:1');
    assert.ok(record);
    assert.deepEqual(record.payload, { id: 1 });
    assert.equal(record.cachedAt, cachedAt);
  });

  it('returns null for missing keys', async () => {
    const cache = new ContentCache({ storage: createMemoryStorage() });
    assert.equal(await cache.get('missing'), null);
  });

  it('evicts least-recently-accessed entries when over max', async () => {
    const cache = new ContentCache({ storage: createMemoryStorage(), maxEntries: 2 });
    await cache.put('a', { n: 1 });
    await cache.put('b', { n: 2 });
    // Touch `a` so `b` is older for eviction after a third put.
    assert.deepEqual(await cache.get('a'), { n: 1 });
    await cache.put('c', { n: 3 });

    assert.equal(await cache.size(), 2);
    assert.deepEqual(await cache.get('a'), { n: 1 });
    assert.deepEqual(await cache.get('c'), { n: 3 });
    assert.equal(await cache.get('b'), null);
  });

  it('drops corrupt entries', async () => {
    const storage = createMemoryStorage({
      'qz:content:bad': 'not-json',
    });
    const cache = new ContentCache({ storage });
    assert.equal(await cache.get('bad'), null);
    assert.equal(await storage.getItem('qz:content:bad'), null);
  });

  it('drops old-schema and unversioned envelopes', async () => {
    const storage = createMemoryStorage({
      'qz:content:legacy': JSON.stringify({
        accessedAt: '2024-01-01T00:00:00.000Z',
        accessSeq: 1,
        cachedAt: '2024-01-01T00:00:00.000Z',
        payloadJson: '{"id":1}',
      }),
      'qz:content:v0': JSON.stringify({
        schemaVersion: 0,
        accessedAt: '2024-01-01T00:00:00.000Z',
        accessSeq: 1,
        cachedAt: '2024-01-01T00:00:00.000Z',
        payloadJson: '{"id":1}',
      }),
    });
    const cache = new ContentCache({ storage, schemaVersion: CONTENT_CACHE_SCHEMA_VERSION });
    assert.equal(await cache.get('legacy'), null);
    assert.equal(await cache.get('v0'), null);
    assert.equal(await storage.getItem('qz:content:legacy'), null);
    assert.equal(await storage.getItem('qz:content:v0'), null);
  });

  it('clears all prefixed entries', async () => {
    const storage = createMemoryStorage({ other: 'keep' });
    const cache = new ContentCache({ storage });
    await cache.put('news:1', { id: 1 });
    await cache.clear();
    assert.equal(await cache.size(), 0);
    assert.equal(await storage.getItem('other'), 'keep');
  });

  it('purges a logical key prefix and leaves other namespaces', async () => {
    const cache = new ContentCache({ storage: createMemoryStorage() });
    await cache.put(forumTopicCacheKey(1), { id: 1 });
    await cache.put(conversationCacheKey('member-a', 'c1'), { id: 'c1' });
    await cache.put(conversationCacheKey('member-b', 'c2'), { id: 'c2' });

    await cache.purgePrefix(PRIVATE_CACHE_KEY_PREFIX);

    assert.deepEqual(await cache.get(forumTopicCacheKey(1)), { id: 1 });
    assert.equal(await cache.get(conversationCacheKey('member-a', 'c1')), null);
    assert.equal(await cache.get(conversationCacheKey('member-b', 'c2')), null);
  });
});

describe('withOfflineCache', () => {
  it('writes through on successful fetch with network provenance', async () => {
    const cache = new ContentCache({ storage: createMemoryStorage() });
    const result = await withOfflineCacheResult(cache, 'news:1', async () => ({
      id: 1,
      title: 'fresh',
    }));
    assert.equal(result.source, 'network');
    assert.equal(typeof result.cachedAt, 'string');
    assert.deepEqual(result.data, { id: 1, title: 'fresh' });
    assert.deepEqual(await cache.get('news:1'), { id: 1, title: 'fresh' });
    assert.deepEqual(await withOfflineCache(cache, 'news:1', async () => ({ id: 1, title: 'fresh' })), {
      id: 1,
      title: 'fresh',
    });
  });

  it('serves the last snapshot when the network is unreachable', async () => {
    const cache = new ContentCache({ storage: createMemoryStorage() });
    const cachedAt = await cache.put('news:1', { id: 1, title: 'cached' });

    const result = await withOfflineCacheResult(cache, 'news:1', async () => {
      throw new ApiError(0, 'Unable to reach QueenZone. Check your connection and try again.');
    });

    assert.equal(result.source, 'cache');
    assert.equal(result.cachedAt, cachedAt);
    assert.deepEqual(result.data, { id: 1, title: 'cached' });
  });

  it('serves the last snapshot on timeout', async () => {
    const cache = new ContentCache({ storage: createMemoryStorage() });
    await cache.put('news:1', { id: 1, title: 'cached' });

    const result = await withOfflineCacheResult(cache, 'news:1', async () => {
      throw ApiError.timeout();
    });

    assert.equal(result.source, 'cache');
    assert.deepEqual(result.data, { id: 1, title: 'cached' });
  });

  it('does not fall back for HTTP errors', async () => {
    const cache = new ContentCache({ storage: createMemoryStorage() });
    await cache.put('news:1', { id: 1, title: 'cached' });

    await assert.rejects(
      () =>
        withOfflineCache(cache, 'news:1', async () => {
          throw new ApiError(404, 'Not found.');
        }),
      (err: unknown) => err instanceof ApiError && err.status === 404,
    );
    assert.deepEqual(await cache.get('news:1'), { id: 1, title: 'cached' });
  });

  it('does not fall back for malformed responses', async () => {
    const cache = new ContentCache({ storage: createMemoryStorage() });
    await cache.put('news:1', { id: 1, title: 'cached' });

    await assert.rejects(
      () =>
        withOfflineCache(cache, 'news:1', async () => {
          throw ApiError.malformed(200);
        }),
      (err: unknown) => err instanceof ApiError && err.kind === 'malformed',
    );
    assert.deepEqual(await cache.get('news:1'), { id: 1, title: 'cached' });
  });

  it('invalidates private keys on 401/403/404', async () => {
    const cache = new ContentCache({ storage: createMemoryStorage() });
    const key = conversationCacheKey('member-a', 'c1');
    await cache.put(key, { id: 'c1' });

    await assert.rejects(
      () =>
        withOfflineCache(
          cache,
          key,
          async () => {
            throw new ApiError(401, 'Sign in to continue.');
          },
          { invalidateOn: [401, 403, 404] },
        ),
      (err: unknown) => err instanceof ApiError && err.status === 401,
    );
    assert.equal(await cache.get(key), null);

    await cache.put(key, { id: 'c1' });
    await assert.rejects(
      () =>
        withOfflineCache(
          cache,
          key,
          async () => {
            throw new ApiError(403, 'Forbidden.');
          },
          { invalidateOn: [401, 403, 404] },
        ),
      (err: unknown) => err instanceof ApiError && err.status === 403,
    );
    assert.equal(await cache.get(key), null);

    await cache.put(key, { id: 'c1' });
    await assert.rejects(
      () =>
        withOfflineCache(
          cache,
          key,
          async () => {
            throw new ApiError(404, 'Not found.');
          },
          { invalidateOn: [401, 403, 404] },
        ),
      (err: unknown) => err instanceof ApiError && err.status === 404,
    );
    assert.equal(await cache.get(key), null);
  });

  it('invalidates a public topic on 404 only', async () => {
    const cache = new ContentCache({ storage: createMemoryStorage() });
    const key = forumTopicCacheKey(1002);
    await cache.put(key, { id: 1002 });

    await assert.rejects(
      () =>
        withOfflineCache(
          cache,
          key,
          async () => {
            throw new ApiError(404, 'Not found.');
          },
          { invalidateOn: [404] },
        ),
      (err: unknown) => err instanceof ApiError && err.status === 404,
    );
    assert.equal(await cache.get(key), null);
  });

  it('does not fall back when fallback is disabled (pull-to-refresh)', async () => {
    const cache = new ContentCache({ storage: createMemoryStorage() });
    await cache.put('news:1', { id: 1, title: 'cached' });

    await assert.rejects(
      () =>
        withOfflineCache(
          cache,
          'news:1',
          async () => {
            throw ApiError.offline();
          },
          { fallback: false },
        ),
      (err: unknown) => err instanceof ApiError && err.kind === 'offline',
    );
    assert.deepEqual(await cache.get('news:1'), { id: 1, title: 'cached' });
  });

  it('rethrows when offline and nothing is cached', async () => {
    const cache = new ContentCache({ storage: createMemoryStorage() });

    await assert.rejects(
      () =>
        withOfflineCache(cache, 'news:1', async () => {
          throw new ApiError(0, 'Unable to reach QueenZone.');
        }),
      (err: unknown) => err instanceof ApiError && err.status === 0,
    );
  });

  it('rethrows abort errors without reading the cache', async () => {
    const cache = new ContentCache({ storage: createMemoryStorage() });
    await cache.put('news:1', { id: 1, title: 'cached' });
    const abort = new Error('Aborted');
    abort.name = 'AbortError';

    await assert.rejects(
      () =>
        withOfflineCache(cache, 'news:1', async () => {
          throw abort;
        }),
      (err: unknown) => err instanceof Error && err.name === 'AbortError',
    );
  });
});

describe('paged reconstruction', () => {
  it('reconstructs opened post pages in order without duplication', async () => {
    const cache = new ContentCache({ storage: createMemoryStorage() });
    await cache.put(forumTopicPostsCacheKey(1002, 1), pagedPosts([1, 2, 3], 1, 6, 2));
    await cache.put(forumTopicPostsCacheKey(1002, 2), pagedPosts([4, 5, 6], 2, 6, 2));

    const page1 = await cache.get<ReturnType<typeof pagedPosts>>(forumTopicPostsCacheKey(1002, 1));
    const page2 = await cache.get<ReturnType<typeof pagedPosts>>(forumTopicPostsCacheKey(1002, 2));
    const ids = [...(page1?.items ?? []), ...(page2?.items ?? [])].map((item) => item.id);
    assert.deepEqual(ids, [1, 2, 3, 4, 5, 6]);
    assert.equal(new Set(ids).size, ids.length);
  });

  it('invalidates an incompatible tail after a page-1 refresh', async () => {
    const cache = new ContentCache({ storage: createMemoryStorage() });
    await cache.put(forumTopicPostsCacheKey(1002, 1), pagedPosts([1, 2, 3], 1, 6, 2));
    await cache.put(forumTopicPostsCacheKey(1002, 2), pagedPosts([4, 5, 6], 2, 6, 2));

    const nextPage1 = pagedPosts([1, 2, 3], 1, 7, 3);
    assert.equal(pagedTailIncompatible(pagedPosts([1, 2, 3], 1, 6, 2), nextPage1), true);
    await invalidateIncompatiblePostPages(cache, 1002, nextPage1);
    await cache.put(forumTopicPostsCacheKey(1002, 1), nextPage1);

    assert.deepEqual(await cache.get(forumTopicPostsCacheKey(1002, 1)), nextPage1);
    assert.equal(await cache.get(forumTopicPostsCacheKey(1002, 2)), null);
  });

  it('keeps a compatible tail when page-1 tip and totalCount are unchanged', async () => {
    const cache = new ContentCache({ storage: createMemoryStorage() });
    const page1 = pagedPosts([1, 2, 3], 1, 6, 2);
    await cache.put(forumTopicPostsCacheKey(1002, 1), page1);
    await cache.put(forumTopicPostsCacheKey(1002, 2), pagedPosts([4, 5, 6], 2, 6, 2));

    await invalidateIncompatiblePostPages(cache, 1002, page1);
    assert.ok(await cache.get(forumTopicPostsCacheKey(1002, 2)));
  });
});

describe('member isolation', () => {
  it('does not expose member A conversations to member B', async () => {
    const cache = new ContentCache({ storage: createMemoryStorage() });
    await cache.put(conversationCacheKey('member-a', 'c1'), { body: 'secret from A' });

    assert.equal(await cache.get(conversationCacheKey('member-b', 'c1')), null);
    assert.deepEqual(await cache.get(conversationCacheKey('member-a', 'c1')), {
      body: 'secret from A',
    });
  });
});


describe('cache lifecycle and maintenance boundaries (#1948, #1951)', () => {
  it('rejects a late private fetch after purge while a new same-member request can persist', async () => {
    const cache = new ContentCache({ storage: createMemoryStorage() });
    const key = conversationCacheKey('member-a', 'c1');
    const response = Promise.withResolvers<{ body: string }>();
    const pending = withOfflineCache(cache, key, () => response.promise);
    await cache.purgePrefix(PRIVATE_CACHE_KEY_PREFIX);
    await cache.put(key, { body: 'new session' });
    response.resolve({ body: 'old session' });
    await pending;
    assert.deepEqual(await cache.get(key), { body: 'new session' });
  });

  it('preserves public requests while invalidating only the previous member', async () => {
    const cache = new ContentCache({ storage: createMemoryStorage() });
    const a = cache.acquireLease(conversationCacheKey('member-a', 'c1'));
    const b = cache.acquireLease(conversationCacheKey('member-b', 'c1'));
    const publicLease = cache.acquireLease('news:1');
    await cache.purgePrefix(privateMemberCachePrefix('member-a'));
    assert.equal(a.current, false);
    assert.equal(b.current, true);
    assert.equal(publicLease.current, true);
    a.release(); b.release(); publicLease.release();
  });

  it('purge waits for an already-started storage write and removes it before new writes', async () => {
    const backing = createMemoryStorage();
    const started = Promise.withResolvers<void>();
    const release = Promise.withResolvers<void>();
    const cache = new ContentCache({ storage: {
      ...backing,
      async setItem(key, value) {
        if (key.includes('conversation') && value.includes('old')) {
          started.resolve();
          await release.promise;
        }
        await backing.setItem(key, value);
      },
    } });
    const key = conversationCacheKey('member-a', 'c1');
    const pending = cache.put(key, { body: 'old' });
    await started.promise;
    const purged = cache.purgePrefix(PRIVATE_CACHE_KEY_PREFIX);
    const fresh = cache.put(key, { body: 'new' });
    release.resolve();
    await Promise.all([pending, purged, fresh]);
    assert.deepEqual(await cache.get(key), { body: 'new' });
  });

  it('a delayed read cannot return purged content or resurrect its envelope', async () => {
    const backing = createMemoryStorage();
    const started = Promise.withResolvers<void>();
    const release = Promise.withResolvers<void>();
    let delay = false;
    const cache = new ContentCache({ storage: {
      ...backing,
      async getItem(key) {
        const raw = await backing.getItem(key);
        if (delay && key.includes('conversation')) {
          started.resolve();
          await release.promise;
        }
        return raw;
      },
    } });
    const key = conversationCacheKey('member-a', 'c1');
    await cache.put(key, { body: 'private' });
    delay = true;
    const read = cache.read(key);
    await started.promise;
    const purge = cache.purgePrefix(PRIVATE_CACHE_KEY_PREFIX);
    release.resolve();
    assert.equal(await read, null);
    await purge;
    assert.equal(await cache.size(), 0);
  });

  it('capacity insertion reads no payloads and a warm hit only reads its own payload', async () => {
    const backing = createMemoryStorage();
    let reads = 0;
    let writes = 0;
    let enumerations = 0;
    const storage = {
      ...backing,
      async getItem(key: string) { reads++; return backing.getItem(key); },
      async setItem(key: string, value: string) { writes++; return backing.setItem(key, value); },
      async getAllKeys() { enumerations++; return backing.getAllKeys(); },
    };
    const cache = new ContentCache({ storage });
    await Promise.all(Array.from({ length: 80 }, (_, i) => cache.put(`news:${i}`, { id: i })));
    reads = 0; writes = 0; enumerations = 0;
    await cache.put('news:80', { id: 80 });
    assert.equal(reads, 0);
    assert.equal(enumerations, 0);
    assert.equal(writes, 2);
    await cache.get('news:80');
    assert.equal(reads, 1);
    assert.equal(await cache.size(), 80);
    assert.equal(await cache.get('news:0'), null);
    reads = 0;
    const restored = new ContentCache({ storage });
    await restored.get('news:80');
    assert.equal(reads, 2); // One compact index and the requested payload.
  });

  it('recovers corrupt metadata and preserves migrated envelope LRU ordering', async () => {
    const storage = createMemoryStorage();
    const first = new ContentCache({ storage, maxEntries: 2 });
    await first.put('a', 1);
    await first.put('b', 2);
    await storage.setItem('qz:content:$lru-v1', 'broken');
    const restored = new ContentCache({ storage, maxEntries: 2 });
    await restored.put('c', 3);
    assert.equal(await restored.get('a'), null);
    assert.equal(await restored.get('b'), 2);
    assert.equal(await restored.get('c'), 3);
  });

  it('returns offline payload even when optional metadata persistence fails', async () => {
    const backing = createMemoryStorage();
    const first = new ContentCache({ storage: backing });
    await first.put('news:1', { id: 1 });
    const cache = new ContentCache({ storage: {
      ...backing,
      async setItem() { throw new Error('storage unavailable'); },
    } });
    assert.deepEqual(await withOfflineCache(cache, 'news:1', () => Promise.reject(ApiError.offline())), { id: 1 });
  });
});


it('private purge preserves public recency without forcing another payload scan', async () => {
  const backing = createMemoryStorage();
  let payloadReads = 0;
  const cache = new ContentCache({ maxEntries: 2, storage: {
    ...backing,
    async getItem(key) {
      if (!key.endsWith('$lru-v1')) payloadReads++;
      return backing.getItem(key);
    },
  } });
  await cache.put('public:a', 'a');
  await cache.put('public:b', 'b');
  await cache.get('public:a');
  payloadReads = 0;
  await cache.purgePrefix('private:');
  await cache.put('public:c', 'c');
  assert.equal(payloadReads, 0);
  assert.equal(await cache.get('public:a'), 'a');
  assert.equal(await cache.get('public:b'), null);
  assert.equal(await cache.get('public:c'), 'c');
});


it('recovers healthy offline payloads even when another entry cannot be read', async () => {
  const backing = createMemoryStorage();
  const original = new ContentCache({ storage: backing });
  await original.put('good', { value: 'available offline' });
  await original.put('bad', { value: 'unreadable' });
  await backing.removeItem('qz:content:$lru-v1');
  const cache = new ContentCache({ storage: {
    ...backing,
    async getItem(key) {
      if (key === 'qz:content:bad') throw new Error('one damaged row');
      return backing.getItem(key);
    },
  } });
  assert.deepEqual(await withOfflineCache(cache, 'good', () => Promise.reject(ApiError.offline())),
    { value: 'available offline' });
});


it('authoritative HTTP removal succeeds even when optional key enumeration fails', async () => {
  const backing = createMemoryStorage();
  const original = new ContentCache({ storage: backing });
  const key = conversationCacheKey('member-a', 'c1');
  await original.put(key, { body: 'revoked' });
  let failEnumeration = true;
  const cache = new ContentCache({ storage: {
    ...backing,
    async getAllKeys() {
      if (failEnumeration) throw new Error('enumeration unavailable');
      return backing.getAllKeys();
    },
  } });
  await assert.rejects(withOfflineCache(cache, key,
    () => Promise.reject(new ApiError(403, 'Forbidden')), { invalidateOn: [403] }), ApiError);
  assert.equal(await backing.getItem(cache.entryKey(key)), null);
  failEnumeration = false;
  await assert.rejects(withOfflineCache(cache, key, () => Promise.reject(ApiError.offline())), ApiError);
});


it('serves a healthy offline payload when LRU enumeration is unavailable', async () => {
  const backing = createMemoryStorage();
  await new ContentCache({ storage: backing }).put('good', { value: 'cached' });
  const cache = new ContentCache({ storage: {
    ...backing,
    async getAllKeys() { throw new Error('index unavailable'); },
  } });
  assert.deepEqual(await withOfflineCache(cache, 'good', () => Promise.reject(ApiError.offline())), { value: 'cached' });
});


it('missing and corrupt reads remain null when metadata enumeration fails', async () => {
  const backing = createMemoryStorage({ 'qz:content:bad': 'broken' });
  const cache = new ContentCache({ storage: {
    ...backing,
    async getAllKeys() { throw new Error('index unavailable'); },
  } });
  assert.equal(await cache.get('missing'), null);
  assert.equal(await cache.get('bad'), null);
});

it('exact-key removal by a cold owner preserves other entries persisted recency', async () => {
  const storage = createMemoryStorage();
  const old = new ContentCache({ storage, maxEntries: 3 });
  await old.put('a', 'a');
  await old.put('b', 'b');
  await old.put('private', 'private');
  await old.get('a');
  const fresh = new ContentCache({ storage, maxEntries: 2 });
  await fresh.remove('private');
  await fresh.put('c', 'c');
  assert.equal(await fresh.get('a'), 'a');
  assert.equal(await fresh.get('b'), null);
});
