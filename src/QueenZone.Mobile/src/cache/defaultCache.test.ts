import assert from 'node:assert/strict';
import { register } from 'node:module';
import { afterEach, describe, it } from 'node:test';
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

const { ContentCache } = await import('./contentCache.ts');
const { getContentCache, purgePrivateContentCache, setContentCacheForTests } = await import(
  './defaultCache.ts'
);
const { createMemoryStorage } = await import('./storage.ts');
const {
  conversationCacheKey,
  DOWNLOAD_UI_CACHE_KEY_PREFIX,
  forumTopicCacheKey,
  NEWS_LIST_CACHE_KEY,
  PM_UNREAD_CACHE_KEY,
  PRIVATE_CACHE_KEY_PREFIX,
  downloadUiCachePrefix,
  privateMemberCachePrefix,
} = await import('./keys.ts');
const { getStoreVersion, resetExternalStoreForTests, subscribePrefix } = await import(
  './externalStore.ts'
);

describe('getContentCache', () => {
  afterEach(() => {
    setContentCacheForTests(null);
    resetExternalStoreForTests();
  });

  it('returns the same singleton after the first create', () => {
    setContentCacheForTests(null);
    const first = getContentCache();
    const second = getContentCache();
    assert.equal(first, second);
    assert.ok(first instanceof ContentCache);
  });

  it('lets tests replace and reset the singleton', async () => {
    const injected = new ContentCache({ storage: createMemoryStorage() });
    await injected.put('news:1', { id: 1 });
    setContentCacheForTests(injected);
    assert.equal(getContentCache(), injected);
    assert.deepEqual(await getContentCache().get('news:1'), { id: 1 });

    const replacement = new ContentCache({ storage: createMemoryStorage() });
    setContentCacheForTests(replacement);
    assert.equal(getContentCache(), replacement);
    assert.equal(await getContentCache().get('news:1'), null);

    setContentCacheForTests(null);
    const created = getContentCache();
    assert.notEqual(created, replacement);
    assert.notEqual(created, injected);
  });

  it('purges private conversation keys without dropping public forum cache', async () => {
    const injected = new ContentCache({ storage: createMemoryStorage() });
    await injected.put(conversationCacheKey('member-a', 'c1'), { id: 'c1' });
    await injected.put(forumTopicCacheKey(1), { id: 1 });
    setContentCacheForTests(injected);

    await purgePrivateContentCache();
    assert.equal(await injected.get(conversationCacheKey('member-a', 'c1')), null);
    assert.deepEqual(await injected.get(forumTopicCacheKey(1)), { id: 1 });
  });

  it('prefix-invalidates private and download-ui store keys on full purge', async () => {
    resetExternalStoreForTests();
    setContentCacheForTests(new ContentCache({ storage: createMemoryStorage() }));
    const heard: string[] = [];
    const stopPrivate = subscribePrefix(PRIVATE_CACHE_KEY_PREFIX, () => {
      heard.push('private');
    });
    const stopDownloads = subscribePrefix(DOWNLOAD_UI_CACHE_KEY_PREFIX, () => {
      heard.push('downloads');
    });
    const newsStart = getStoreVersion(NEWS_LIST_CACHE_KEY);
    const pmStart = getStoreVersion(PM_UNREAD_CACHE_KEY);

    await purgePrivateContentCache();

    assert.deepEqual(heard, ['private', 'downloads']);
    assert.equal(getStoreVersion(PM_UNREAD_CACHE_KEY), pmStart + 1);
    assert.equal(getStoreVersion(NEWS_LIST_CACHE_KEY), newsStart);
    stopPrivate();
    stopDownloads();
  });

  it('member-scoped purge invalidates that member prefix only', async () => {
    resetExternalStoreForTests();
    setContentCacheForTests(new ContentCache({ storage: createMemoryStorage() }));
    const heard: string[] = [];
    const stopMember = subscribePrefix(privateMemberCachePrefix('member-a'), () => {
      heard.push('member-a');
    });
    const stopOther = subscribePrefix(privateMemberCachePrefix('member-b'), () => {
      heard.push('member-b');
    });
    const stopDownloads = subscribePrefix(downloadUiCachePrefix('member-a'), () => {
      heard.push('downloads-a');
    });
    const pmStart = getStoreVersion(PM_UNREAD_CACHE_KEY);

    await purgePrivateContentCache('member-a');

    assert.deepEqual(heard, ['member-a', 'downloads-a']);
    assert.equal(getStoreVersion(PM_UNREAD_CACHE_KEY), pmStart);
    stopMember();
    stopOther();
    stopDownloads();
  });
});

it('invalidates pending private work immediately and reports failed disk purge', async () => {
  const storage = createMemoryStorage();
  const cache = new ContentCache({ storage: {
    ...storage,
    async getAllKeys() { throw new Error('device store unavailable'); },
  } });
  setContentCacheForTests(cache);
  const lease = cache.acquireLease(conversationCacheKey('member-a', 'c1'));
  const purging = purgePrivateContentCache('member-a');
  assert.equal(lease.current, false);
  await assert.rejects(purging, /device store unavailable/);
  lease.release();
});
