import assert from 'node:assert/strict';
import { describe, it } from 'node:test';
import {
  createExternalStore,
  getPrefixVersion,
  getStoreVersion,
  invalidate,
  invalidatePrefix,
  resetExternalStoreForTests,
  subscribe,
  subscribePrefix,
} from './externalStore.ts';
import {
  DOWNLOAD_UI_CACHE_KEY_PREFIX,
  NEWS_CACHE_KEY_PREFIX,
  NEWS_LIST_CACHE_KEY,
  PM_UNREAD_CACHE_KEY,
  PRIVATE_CACHE_KEY_PREFIX,
  downloadUiCacheKey,
  downloadUiCachePrefix,
} from './keys.ts';

describe('createExternalStore', () => {
  it('starts versions at 0 and increments only the invalidated key', () => {
    const store = createExternalStore();
    assert.equal(store.getVersion('news:list'), 0);
    assert.equal(store.getVersion('messages:member:unread'), 0);
    store.invalidate('news:list');
    assert.equal(store.getVersion('news:list'), 1);
    assert.equal(store.getVersion('messages:member:unread'), 0);
  });

  it('notifies exact-key listeners and not ones that already unsubscribed', () => {
    const store = createExternalStore();
    const heard: number[] = [];
    const stop = store.subscribe('news:list', () => {
      heard.push(store.getVersion('news:list'));
    });
    store.invalidate('news:list');
    stop();
    const afterUnsubscribe = store.getVersion('news:list');
    store.invalidate('news:list');
    assert.deepEqual(heard, [afterUnsubscribe]);
    assert.equal(store.getVersion('news:list'), afterUnsubscribe + 1);
  });

  it('notifies prefix subscribers when a matching key is invalidated', () => {
    const store = createExternalStore();
    const newsHeard: number[] = [];
    const privateHeard: number[] = [];
    store.subscribePrefix('news:', () => {
      newsHeard.push(store.getPrefixVersion('news:'));
    });
    store.subscribePrefix('messages:member:', () => {
      privateHeard.push(store.getPrefixVersion('messages:member:'));
    });

    store.invalidate('news:list');
    store.invalidate('forum:topic:1');

    assert.equal(newsHeard.length, 1);
    assert.equal(privateHeard.length, 0);
    assert.equal(store.getPrefixVersion('news:'), 1);
  });

  it('notifies prefix subscribers on overlapping prefix invalidation', () => {
    const store = createExternalStore();
    const parentHeard: number[] = [];
    const childHeard: number[] = [];
    const siblingHeard: number[] = [];
    store.subscribePrefix('messages:member:', () => {
      parentHeard.push(store.getPrefixVersion('messages:member:'));
    });
    store.subscribePrefix('messages:member:abc:', () => {
      childHeard.push(store.getPrefixVersion('messages:member:abc:'));
    });
    store.subscribePrefix('news:', () => {
      siblingHeard.push(store.getPrefixVersion('news:'));
    });

    store.invalidatePrefix('messages:member:abc:');
    assert.equal(parentHeard.length, 1);
    assert.equal(childHeard.length, 1);
    assert.equal(siblingHeard.length, 0);

    store.invalidatePrefix('messages:member:');
    assert.equal(parentHeard.length, 2);
    assert.equal(childHeard.length, 2);
    assert.equal(siblingHeard.length, 0);
  });

  it('prefix-invalidates matching exact-key subscribers and versions', () => {
    const store = createExternalStore();
    const heard: number[] = [];
    store.subscribe('messages:member:unread', () => {
      heard.push(store.getVersion('messages:member:unread'));
    });
    store.subscribe('news:list', () => {
      throw new Error('news key must not hear a private prefix invalidate');
    });

    store.invalidatePrefix('messages:member:');
    assert.equal(heard.length, 1);
    assert.equal(store.getVersion('messages:member:unread'), 1);
    assert.equal(store.getVersion('news:list'), 0);
  });

  it('does not notify a prefix subscriber after unsubscribe', () => {
    const store = createExternalStore();
    let calls = 0;
    const stop = store.subscribePrefix('news:', () => {
      calls += 1;
    });
    store.invalidatePrefix('news:');
    stop();
    store.invalidate('news:list');
    store.invalidatePrefix('news:');
    assert.equal(calls, 1);
  });
});

describe('shared external store + keys.ts namespace', () => {
  it('exposes singleton helpers that reset for tests', () => {
    resetExternalStoreForTests();
    assert.equal(getStoreVersion(NEWS_LIST_CACHE_KEY), 0);
    const heard: string[] = [];
    const stopKey = subscribe(NEWS_LIST_CACHE_KEY, () => {
      heard.push('news');
    });
    const stopPrefix = subscribePrefix(PRIVATE_CACHE_KEY_PREFIX, () => {
      heard.push('private');
    });

    invalidate(NEWS_LIST_CACHE_KEY);
    invalidate(PM_UNREAD_CACHE_KEY);
    invalidatePrefix(NEWS_CACHE_KEY_PREFIX);

    assert.deepEqual(heard, ['news', 'private', 'news']);
    assert.equal(getStoreVersion(NEWS_LIST_CACHE_KEY), 2);
    assert.equal(getPrefixVersion(NEWS_CACHE_KEY_PREFIX), 2);
    assert.equal(getStoreVersion(PM_UNREAD_CACHE_KEY), 1);

    stopKey();
    stopPrefix();
    resetExternalStoreForTests();
    assert.equal(getStoreVersion(NEWS_LIST_CACHE_KEY), 0);
  });

  it('documents the #927 download-ui key seam without storing binaries', () => {
    assert.equal(downloadUiCachePrefix('member-a'), 'downloads:member:member-a:');
    assert.equal(
      downloadUiCacheKey('member-a', 'perf-1'),
      'downloads:member:member-a:performance:perf-1',
    );
    assert.ok(downloadUiCacheKey('member-a', 'perf-1').startsWith(DOWNLOAD_UI_CACHE_KEY_PREFIX));
    assert.ok(!downloadUiCacheKey('member-a', 'perf-1').startsWith(PRIVATE_CACHE_KEY_PREFIX));
  });
});

describe('listener preservation and prefix version contracts', () => {
  it('retains all listeners for one key and unsubscribes only the selected listener', () => {
    const store = createExternalStore();
    let first = 0;
    let second = 0;
    const stopFirst = store.subscribe('news:list', () => { first++; });
    store.subscribe('news:list', () => { second++; });
    store.invalidate('news:list');
    assert.equal(first, 1);
    assert.equal(second, 1);
    stopFirst();
    store.invalidate('news:list');
    assert.equal(first, 1);
    assert.equal(second, 2);
  });

  it('retains prefix listeners when another listener unsubscribes', () => {
    const store = createExternalStore();
    let first = 0;
    let second = 0;
    const stopFirst = store.subscribePrefix('news:', () => { first++; });
    store.subscribePrefix('news:', () => { second++; });
    store.invalidate('news:list');
    assert.equal(first, 1);
    assert.equal(second, 1);
    stopFirst();
    store.invalidatePrefix('news:');
    assert.equal(first, 1);
    assert.equal(second, 2);
  });

  it('counts both ancestor and descendant invalidations without counting unrelated prefixes', () => {
    const store = createExternalStore();
    store.invalidatePrefix('messages:member:abc:');
    assert.equal(store.getPrefixVersion('messages:member:'), 1);
    assert.equal(store.getPrefixVersion('messages:member:abc:'), 1);
    assert.equal(store.getPrefixVersion('news:'), 0);
    store.invalidatePrefix('messages:member:');
    assert.equal(store.getPrefixVersion('messages:member:'), 2);
    assert.equal(store.getPrefixVersion('messages:member:abc:'), 2);
    assert.equal(store.getPrefixVersion('news:'), 0);
  });
});
