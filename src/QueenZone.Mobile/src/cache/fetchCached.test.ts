import assert from 'node:assert/strict';
import { register } from 'node:module';
import { afterEach, describe, it, mock } from 'node:test';
import { pathToFileURL } from 'node:url';
import { ApiError } from '../api/errors.ts';

const fetchJsonMock = mock.fn<(...args: unknown[]) => Promise<unknown>>();
(globalThis as { __qzFetchJsonMock?: typeof fetchJsonMock }).__qzFetchJsonMock = fetchJsonMock;

register(
  `data:text/javascript,${encodeURIComponent(`
    export async function resolve(specifier, context, nextResolve) {
      if (specifier === '../api/client') {
        return {
          url: 'data:text/javascript,export async function fetchJson(...args){return globalThis.__qzFetchJsonMock(...args)}',
          shortCircuit: true,
        };
      }
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
const { fetchJsonWithOfflineCache, fetchJsonWithOfflineCacheResult } = await import('./fetchCached.ts');

describe('fetchJsonWithOfflineCache', () => {
  afterEach(() => {
    fetchJsonMock.mock.resetCalls();
  });

  it('writes a successful fetch through to the injected cache', async () => {
    const cache = new ContentCache({ storage: createMemoryStorage() });
    fetchJsonMock.mock.mockImplementation(async () => ({ id: 42, title: 'fresh' }));

    const result = await fetchJsonWithOfflineCache('/content/news/42', {
      cacheKey: 'news:42',
      cache,
      accessToken: 'tok',
    });

    assert.deepEqual(result, { id: 42, title: 'fresh' });
    assert.deepEqual(await cache.get('news:42'), { id: 42, title: 'fresh' });
    assert.equal(fetchJsonMock.mock.calls.length, 1);
    assert.deepEqual(fetchJsonMock.mock.calls[0]?.arguments, ['/content/news/42', { accessToken: 'tok' }]);
  });

  it('serves the cached payload when fetchJson throws an offline ApiError', async () => {
    const cache = new ContentCache({ storage: createMemoryStorage() });
    await cache.put('news:42', { id: 42, title: 'cached' });
    fetchJsonMock.mock.mockImplementation(async () => {
      throw ApiError.offline();
    });

    const result = await fetchJsonWithOfflineCache('/content/news/42', {
      cacheKey: 'news:42',
      cache,
    });

    assert.deepEqual(result, { id: 42, title: 'cached' });
    assert.equal(fetchJsonMock.mock.calls.length, 1);
  });

  it('serves the cached payload when fetchJson times out', async () => {
    const cache = new ContentCache({ storage: createMemoryStorage() });
    await cache.put('news:42', { id: 42, title: 'cached' });
    fetchJsonMock.mock.mockImplementation(async () => {
      throw ApiError.timeout();
    });

    const result = await fetchJsonWithOfflineCache('/content/news/42', {
      cacheKey: 'news:42',
      cache,
    });

    assert.deepEqual(result, { id: 42, title: 'cached' });
  });

  it('shares one in-flight promise across concurrent callers with the same cacheKey', async () => {
    const cache = new ContentCache({ storage: createMemoryStorage() });
    let release!: (value: { id: number; title: string }) => void;
    fetchJsonMock.mock.mockImplementation(
      () =>
        new Promise((resolve) => {
          release = resolve;
        }),
    );

    const first = fetchJsonWithOfflineCache('/content/news/42', { cacheKey: 'news:42', cache });
    const second = fetchJsonWithOfflineCache('/content/news/42', { cacheKey: 'news:42', cache });
    const viaResult = fetchJsonWithOfflineCacheResult('/content/news/42', { cacheKey: 'news:42', cache });

    assert.equal(fetchJsonMock.mock.calls.length, 1);

    release({ id: 42, title: 'shared' });
    const [fromFirst, fromSecond, fromResult] = await Promise.all([first, second, viaResult]);

    assert.deepEqual(fromFirst, { id: 42, title: 'shared' });
    assert.deepEqual(fromSecond, { id: 42, title: 'shared' });
    assert.deepEqual(fromResult.data, { id: 42, title: 'shared' });
    assert.equal(fetchJsonMock.mock.calls.length, 1);
  });

  it('does not share in-flight fetches across different cacheKeys', async () => {
    const cache = new ContentCache({ storage: createMemoryStorage() });
    const releases: ((value: { id: number }) => void)[] = [];
    fetchJsonMock.mock.mockImplementation(
      () =>
        new Promise((resolve) => {
          releases.push(resolve);
        }),
    );

    const first = fetchJsonWithOfflineCache('/content/news/42', { cacheKey: 'news:42', cache });
    const second = fetchJsonWithOfflineCache('/content/news/43', { cacheKey: 'news:43', cache });

    assert.equal(fetchJsonMock.mock.calls.length, 2);
    releases[0]!({ id: 42 });
    releases[1]!({ id: 43 });
    assert.deepEqual(await Promise.all([first, second]), [{ id: 42 }, { id: 43 }]);
  });

  it('clears a completed entry so a later call refetches', async () => {
    const cache = new ContentCache({ storage: createMemoryStorage() });
    fetchJsonMock.mock.mockImplementation(async () => ({ id: 42, title: 'fresh' }));

    await fetchJsonWithOfflineCache('/content/news/42', { cacheKey: 'news:42', cache });
    await fetchJsonWithOfflineCache('/content/news/42', { cacheKey: 'news:42', cache });

    assert.equal(fetchJsonMock.mock.calls.length, 2);
  });

  it('clears a failed entry so a later call can refetch', async () => {
    const cache = new ContentCache({ storage: createMemoryStorage() });
    fetchJsonMock.mock.mockImplementation(async () => {
      throw ApiError.http(500, 'The server had a problem.');
    });

    await assert.rejects(
      () => fetchJsonWithOfflineCache('/content/news/42', { cacheKey: 'news:42', cache }),
      (err: unknown) => err instanceof ApiError && err.status === 500,
    );

    fetchJsonMock.mock.mockImplementation(async () => ({ id: 42, title: 'recovered' }));
    const result = await fetchJsonWithOfflineCache('/content/news/42', { cacheKey: 'news:42', cache });

    assert.deepEqual(result, { id: 42, title: 'recovered' });
    assert.equal(fetchJsonMock.mock.calls.length, 2);
  });

  it('shares one rejected in-flight promise, then allows a later refetch', async () => {
    const cache = new ContentCache({ storage: createMemoryStorage() });
    let rejectFetch!: (err: ApiError) => void;
    fetchJsonMock.mock.mockImplementation(
      () =>
        new Promise((_, reject) => {
          rejectFetch = reject;
        }),
    );

    const first = fetchJsonWithOfflineCache('/content/news/42', { cacheKey: 'news:42', cache });
    const second = fetchJsonWithOfflineCache('/content/news/42', { cacheKey: 'news:42', cache });
    assert.equal(fetchJsonMock.mock.calls.length, 1);

    rejectFetch(ApiError.http(503, 'Unavailable.'));
    await assert.rejects(first, (err: unknown) => err instanceof ApiError && err.status === 503);
    await assert.rejects(second, (err: unknown) => err instanceof ApiError && err.status === 503);

    fetchJsonMock.mock.mockImplementation(async () => ({ id: 42, title: 'retry' }));
    const retried = await fetchJsonWithOfflineCache('/content/news/42', { cacheKey: 'news:42', cache });
    assert.deepEqual(retried, { id: 42, title: 'retry' });
    assert.equal(fetchJsonMock.mock.calls.length, 2);
  });
});


it('does not share a previous session flight after purge, even for the same member key', async () => {
  const cache = new ContentCache({ storage: createMemoryStorage() });
  const response = Promise.withResolvers<{ value: string }>();
  fetchJsonMock.mock.mockImplementationOnce(() => response.promise);
  fetchJsonMock.mock.mockImplementation(async () => ({ value: 'new session' }));
  const old = fetchJsonWithOfflineCache('/private', { cacheKey: 'private:member-a', cache });
  await cache.purgePrefix('private:');
  const fresh = await fetchJsonWithOfflineCache('/private', { cacheKey: 'private:member-a', cache });
  assert.deepEqual(fresh, { value: 'new session' });
  response.resolve({ value: 'old session' });
  await old;
  assert.deepEqual(await cache.get('private:member-a'), { value: 'new session' });
});

it('does not share requests between separate cache owners', async () => {
  const a = new ContentCache({ storage: createMemoryStorage() });
  const b = new ContentCache({ storage: createMemoryStorage() });
  const response = Promise.withResolvers<{ value: string }>();
  fetchJsonMock.mock.mockImplementationOnce(() => response.promise);
  fetchJsonMock.mock.mockImplementation(async () => ({ value: 'second cache' }));
  const first = fetchJsonWithOfflineCache('/item', { cacheKey: 'same', cache: a });
  assert.deepEqual(await fetchJsonWithOfflineCache('/item', { cacheKey: 'same', cache: b }), { value: 'second cache' });
  response.resolve({ value: 'first cache' });
  await first;
});
