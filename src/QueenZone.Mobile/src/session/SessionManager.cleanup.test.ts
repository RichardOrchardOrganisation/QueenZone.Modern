import assert from 'node:assert/strict';
import { it } from 'node:test';
import { harness, settle, tokens, profile, deferred, confirmation, cleanup, ContentCache, createMemoryStorage, conversationCacheKey, forumTopicCacheKey, privateMemberCachePrefix } from '../test/sessionManager.ts';
import type { StoredSession } from './tokenStore';

it("signs out locally even when remote logout never completes", async () => {
  const h = harness(); await h.restore();
  h.ports.push.clearPushRegistration.impl = () => new Promise(() => { });
  h.ports.oauth.logoutRemote.impl = () => new Promise(() => { });
  h.ports.oauth.revokeRefreshToken.impl = () => new Promise(() => { });
  await h.manager.signOut(); await settle();
  assert.equal(h.manager.getSnapshot().status, 'signedOut'); assert.equal(h.ports.tokenStore.clearStoredSession.calls.length, 1);
});

it("does not call remote logout when already signed out", async () => {
  const h = harness(); await h.manager.restore(); await h.manager.signOut(); await settle();
  assert.equal(h.ports.push.clearPushRegistration.calls.length, 0);
  assert.equal(h.ports.oauth.logoutRemote.calls.length, 0); assert.equal(h.ports.oauth.revokeRefreshToken.calls.length, 0);
  assert.equal(h.ports.tokenStore.clearStoredSession.calls.length, 1);
});

it("signs out in memory when SecureStore delete fails", async () => {
  const h = harness(); await h.restore();
  h.ports.tokenStore.clearStoredSession.impl = async () => { throw new Error('secure-store-unavailable'); };
  await h.manager.signOut(); await settle();
  assert.equal(h.manager.getSnapshot().status, 'signedOut'); assert.equal(cleanup(h).hasCurrentSession, false);
});

it("retries a locked session write when the app becomes active", async () => {
  const h = harness(); await h.restore();
  for (let i = 0; i < 1; i++) h.ports.tokenStore.writeStoredSession.once.push(async () => { throw new Error('User interaction is not allowed'); });
  h.clock.advance(120_000); h.ports.oauth.refreshAccessToken.impl = async () => tokens({ accessToken: 'next', refreshToken: 'next-refresh' });
  await h.manager.ensureAccessToken(); assert.equal(h.session().accessToken, 'next');
  for (let i = 0; i < 1; i++) { h.manager.onAppActive(); await settle(); }
  assert.equal(h.ports.tokenStore.writeStoredSession.calls.length, 1 + 1);
  assert.equal(h.ports.tokenStore.writeStoredSession.calls.at(-1)?.[0].refreshToken, 'next-refresh');
  assert.equal(h.ports.tokenStore.clearStoredSession.calls.length, 0);
});

it("retries a failed non-lock session write when the app becomes active", async () => {
  const h = harness(); await h.restore();
  for (let i = 0; i < 2; i++) h.ports.tokenStore.writeStoredSession.once.push(async () => { throw new Error('SecureStore staging write could not be confirmed'); });
  h.clock.advance(120_000); h.ports.oauth.refreshAccessToken.impl = async () => tokens({ accessToken: 'next', refreshToken: 'next-refresh' });
  await h.manager.ensureAccessToken(); assert.equal(h.session().accessToken, 'next');
  for (let i = 0; i < 2; i++) { h.manager.onAppActive(); await settle(); }
  assert.equal(h.ports.tokenStore.writeStoredSession.calls.length, 2 + 1);
  assert.equal(h.ports.tokenStore.writeStoredSession.calls.at(-1)?.[0].refreshToken, 'next-refresh');
  assert.equal(h.ports.tokenStore.clearStoredSession.calls.length, 0);
});

it("purges conversations on sign-out and leaves the public forum cache", async () => {
  const h = harness(); const storage = createMemoryStorage(); const cache = new ContentCache({ storage });
  await cache.put(conversationCacheKey('member-1', 'c1'), { body: 'secret from A' });
  await cache.put(forumTopicCacheKey(1002), { id: 1002 });
  h.ports.cacheAndDownloads.purgePrivateContentCache.impl = async member => { await cache.purgePrefix(privateMemberCachePrefix(member!)); };
  await h.restore();
  await h.manager.signOut();
  await settle();
  assert.equal(await cache.get(conversationCacheKey('member-1', 'c1')), null);
  assert.equal(await cache.get(conversationCacheKey('member-2', 'c1')), null);
  assert.deepEqual(await cache.get(forumTopicCacheKey(1002)), { id: 1002 });
  assert.equal(h.ports.cacheAndDownloads.purgePrivateContentCache.calls[0][0], 'member-1');
});

it("purges member A conversations when session restore establishes member B", async () => {
  const h = harness(); const storage = createMemoryStorage(); const cache = new ContentCache({ storage });
  await cache.put(conversationCacheKey('member-1', 'c1'), { body: 'secret from A' });
  await cache.put(forumTopicCacheKey(1002), { id: 1002 });
  h.ports.cacheAndDownloads.purgePrivateContentCache.impl = async member => { await cache.purgePrefix(privateMemberCachePrefix(member!)); };
  await h.restore();
  h.ports.oauth.loadProfile.impl = async () => profile({ memberId: 'member-2', displayName: 'Brian' }); await h.manager.signIn('Google');
  await settle();
  assert.equal(await cache.get(conversationCacheKey('member-1', 'c1')), null);
  assert.equal(await cache.get(conversationCacheKey('member-2', 'c1')), null);
  assert.deepEqual(await cache.get(forumTopicCacheKey(1002)), { id: 1002 });
  assert.equal(h.ports.cacheAndDownloads.purgePrivateContentCache.calls[0][0], 'member-1');
});

it("reports failed private cache erasure and retries it without deleting the public cache", async () => {
  const h = harness(); const storage = createMemoryStorage(); const cache = new ContentCache({ storage });
  await cache.put(conversationCacheKey('member-1', 'c1'), { body: 'secret from A' });
  await cache.put(forumTopicCacheKey(1002), { id: 1002 });
  h.ports.cacheAndDownloads.purgePrivateContentCache.impl = async member => { await cache.purgePrefix(privateMemberCachePrefix(member!)); };
  await h.restore();
  const remove = storage.multiRemove;
  let failed = false;
  storage.multiRemove = async keys => { if (!failed) { failed = true; throw new Error('disk failure'); } await remove(keys); };
  await h.manager.signOut(); await settle(); assert.equal(h.manager.getSnapshot().status, 'signedOut');
  cleanup(h).retry();
  await settle();
  assert.equal(await cache.get(conversationCacheKey('member-1', 'c1')), null);
  assert.equal(await cache.get(conversationCacheKey('member-2', 'c1')), null);
  assert.deepEqual(await cache.get(forumTopicCacheKey(1002)), { id: 1002 });
  assert.equal(h.ports.cacheAndDownloads.purgePrivateContentCache.calls[0][0], 'member-1');
});

it("signs out and retries a failed download manifest %s", async () => {
  for (const failure of ['read', 'delete']) {
    const h = harness(); await h.restore();
    h.ports.cacheAndDownloads.purgeAllDownloads.once.push(async () => { throw new Error(`manifest ${failure} failed`); });
    await h.manager.signOut(); await settle(); assert.equal(h.manager.getSnapshot().status, 'signedOut');
    assert.equal(h.ports.tokenStore.clearStoredSession.calls.length, 1);
    const event = cleanup(h); assert.equal(event.hasCurrentSession, false); assert.equal(JSON.stringify(event).includes('manifest'), false);
    event.retry(); await settle(); assert.equal(h.ports.cacheAndDownloads.purgeAllDownloads.calls.length, 2);
    assert.equal(h.manager.getSnapshot().status, 'signedOut');
  }
});

it("does not await stalled download cleanup or remote revocation before resetting auth", async () => {
  const h = harness(); await h.restore(); const purge = deferred<void>();
  h.ports.cacheAndDownloads.purgeAllDownloads.impl = () => purge.promise;
  h.ports.oauth.logoutRemote.impl = () => new Promise(() => { });
  await h.manager.signOut(); await settle(); assert.equal(h.manager.getSnapshot().status, 'signedOut');
  assert.equal(h.ports.tokenStore.clearStoredSession.calls.length, 1);
  const [member, isCurrent, allowUnscoped] = h.ports.cacheAndDownloads.purgeAllDownloads.calls[0];
  assert.equal(member, 'member-1'); assert.equal(isCurrent?.(), true); assert.equal(allowUnscoped, true);
  h.ports.oauth.signInWithProvider.impl = async () => tokens({ accessToken: 'new-account' });
  h.ports.oauth.loadProfile.impl = async () => profile({ memberId: 'member-2', displayName: 'Brian' });
  await h.manager.signIn('Google'); purge.resolve(); await settle();
  assert.equal(h.session().accessToken, 'new-account'); assert.equal(h.session().displayName, 'Brian');
  assert.equal(h.ports.tokenStore.clearStoredSession.calls.length, 1);
});

it("preserves pending-send consent when inspection fails (confirm=%s)", async () => {
  for (const confirm of [false, true]) {
    const h = harness(); await h.restore(); h.ports.offlineQueue.countPendingOfflineItems.impl = async () => { throw new Error('inspection failed'); };
    const signingOut = h.manager.signOut(); await settle(); const event = confirmation(h);
    assert.equal(event.inspectionFailed, true); assert.equal(h.session().isSignedIn, true);
    assert.equal(h.ports.tokenStore.clearStoredSession.calls.length, 0);
    if (confirm) event.confirm(); else event.cancel(); await signingOut; await settle();
    assert.equal(h.session().isSignedIn, !confirm); assert.equal(h.discard.calls.length, confirm ? 1 : 0);
    assert.equal(h.ports.tokenStore.clearStoredSession.calls.length, confirm ? 1 : 0);
  }
});

it("preserves known pending-send confirmation (confirm=%s)", async () => {
  for (const confirm of [false, true]) {
    const h = harness(); await h.restore(); h.ports.offlineQueue.countPendingOfflineItems.impl = async () => 2;
    const signingOut = h.manager.signOut(); await settle(); const event = confirmation(h);
    assert.equal(event.inspectionFailed, false); assert.equal(h.session().isSignedIn, true);
    assert.equal(h.ports.tokenStore.clearStoredSession.calls.length, 0);
    if (confirm) event.confirm(); else event.cancel(); await signingOut; await settle();
    assert.equal(h.session().isSignedIn, !confirm); assert.equal(h.discard.calls.length, confirm ? 1 : 0);
    assert.equal(h.ports.tokenStore.clearStoredSession.calls.length, confirm ? 1 : 0);
  }
});

it("does not block auth reset on confirmed pending-send deletion failure", async () => {
  const h = harness(); await h.restore(); h.ports.offlineQueue.countPendingOfflineItems.impl = async () => 1;
  h.discard.impl = async () => { throw new Error('queue deletion failed'); };
  const signingOut = h.manager.signOut(); await settle(); confirmation(h).confirm(); await signingOut; await settle();
  assert.equal(h.manager.getSnapshot().status, 'signedOut'); assert.equal(h.ports.tokenStore.clearStoredSession.calls.length, 1);
  assert.equal(cleanup(h).hasCurrentSession, false);
});

it("orders a pending credential write, sign-out deletion, and a new account write", async () => {
  const h = harness(); await h.restore(); const write = deferred<StoredSession>(); const order: string[] = [];
  h.ports.tokenStore.writeStoredSession.once.push(async () => { order.push('old-write'); return write.promise; });
  h.ports.tokenStore.clearStoredSession.impl = async () => { order.push('delete'); };
  const oldSignIn = h.manager.signIn('Google'); await settle(); assert.deepEqual(order, ['old-write']);
  await h.manager.signOut(); assert.equal(h.manager.getSnapshot().status, 'signedOut');
  h.ports.oauth.signInWithProvider.impl = async () => tokens({ accessToken: 'new-account' });
  h.ports.oauth.loadProfile.impl = async () => profile({ memberId: 'member-2', displayName: 'Brian' });
  h.ports.tokenStore.writeStoredSession.impl = async grant => { order.push('new-write'); return h.stored(grant); };
  const newSignIn = h.manager.signIn('Google'); await settle(); assert.deepEqual(order, ['old-write']);
  write.resolve(h.stored()); await Promise.all([oldSignIn, newSignIn]);
  assert.deepEqual(order, ['old-write', 'delete', 'new-write']); assert.equal(h.session().accessToken, 'new-account'); assert.equal(h.session().displayName, 'Brian');
});

it("does not retry an old failed credential deletion after a new session is stored", async () => {
  const h = harness(); await h.restore(); h.ports.tokenStore.clearStoredSession.once.push(async () => { throw new Error('locked keychain'); });
  await h.manager.signOut(); await settle(); const event = cleanup(h);
  h.ports.oauth.signInWithProvider.impl = async () => tokens({ accessToken: 'new-account' });
  h.ports.oauth.loadProfile.impl = async () => profile({ memberId: 'member-2' }); await h.manager.signIn('Google');
  event.retry(); h.manager.onAppActive(); await settle();
  assert.equal(h.ports.tokenStore.clearStoredSession.calls.length, 1); assert.equal(h.session().accessToken, 'new-account');
});

it("offers Cancel when pending-send inspection stalls instead of assuming no sends", async () => {
  const h = harness(); await h.restore(); h.ports.offlineQueue.countPendingOfflineItems.impl = () => new Promise(() => { });
  const signingOut = h.manager.signOut(); await settle(); h.clock.advance(4_999); await settle(); assert.equal(h.events.length, 0);
  h.clock.advance(1); await settle(); const event = confirmation(h); assert.equal(event.inspectionFailed, true);
  assert.equal(h.ports.tokenStore.clearStoredSession.calls.length, 0); event.cancel(); await signingOut;
  assert.equal(h.session().isSignedIn, true); assert.equal(h.discard.calls.length, 0);
});

it("retries failed credential deletion while the same sign-out is current", async () => {
  const h = harness(); await h.restore(); h.ports.tokenStore.clearStoredSession.once.push(async () => { throw new Error('locked keychain'); });
  await h.manager.signOut(); await settle(); cleanup(h).retry(); await settle();
  assert.equal(h.ports.tokenStore.clearStoredSession.calls.length, 2); assert.equal(await h.manager.ensureAccessToken(), null);
  assert.equal(h.manager.getSnapshot().status, 'signedOut');
});

it("defers the new identity shell until its failed grant write is successfully retried", async () => {
  const h = harness(); await h.restore(); const writes = h.ports.tokenStore.writeStoredIdentityShell.calls.length;
  h.ports.tokenStore.writeStoredSession.once.push(async () => { throw new Error('fresh grant not stored'); });
  h.ports.oauth.signInWithProvider.impl = async () => tokens({ accessToken: 'new-account' });
  h.ports.oauth.loadProfile.impl = async () => profile({ memberId: 'member-2', displayName: 'Brian' });
  await h.manager.signIn('Google'); await settle(); assert.equal(h.session().displayName, 'Brian');
  assert.equal(h.ports.tokenStore.writeStoredIdentityShell.calls.length, writes);
  h.manager.onAppActive(); await settle();
  assert.deepEqual(h.ports.tokenStore.writeStoredIdentityShell.calls.at(-1), [{ displayName: 'Brian', memberId: 'member-2', avatarPath: null }]);
  assert.equal(h.ports.tokenStore.writeStoredSession.calls.at(-1)?.[1], true);
});

it("retries confirmed operation-scoped discard after same-member sign-in", async () => {
  const h = harness(); await h.restore(); h.ports.offlineQueue.countPendingOfflineItems.impl = async () => 1;
  h.discard.once.push(async () => { throw new Error('queue deletion failed'); });
  const signingOut = h.manager.signOut(); await settle(); confirmation(h).confirm(); await signingOut; await settle();
  assert.deepEqual(h.ports.offlineQueue.prepareOfflineQueueDiscard.calls, [['member-1']]); const event = cleanup(h);
  h.ports.oauth.signInWithProvider.impl = async () => tokens({ accessToken: 'new-same-member-session' });
  await h.manager.signIn('Google'); event.retry(); await settle();
  assert.equal(h.discard.calls.length, 2); assert.equal(h.ports.offlineQueue.prepareOfflineQueueDiscard.calls.length, 1);
  assert.equal(h.session().accessToken, 'new-same-member-session');
});

it("carries a failed fresh identity reset into a replacing refresh grant", async () => {
  const h = harness(); await h.restore(); h.ports.tokenStore.writeStoredSession.once.push(async () => { throw new Error('old identity deletion failed'); });
  h.ports.oauth.signInWithProvider.impl = async () => tokens({ accessToken: 'fresh-account' });
  h.ports.oauth.loadProfile.impl = async () => profile({ memberId: 'member-2', displayName: 'Brian' });
  await h.manager.signIn('Google'); assert.equal(h.ports.tokenStore.writeStoredSession.calls.at(-1)?.[1], true);
  h.clock.advance(2_000_000); h.ports.oauth.refreshAccessToken.impl = async () => tokens({ accessToken: 'refreshed-account' });
  h.ports.oauth.loadProfile.impl = async () => null; await h.manager.ensureAccessToken();
  assert.deepEqual(h.ports.tokenStore.writeStoredSession.calls.at(-1), [tokens({ accessToken: 'refreshed-account' }), true]);
  assert.equal(h.session().accessToken, 'refreshed-account'); assert.equal(h.session().displayName, 'Brian');
});

it('retries private cleanup only when its captured owner is safe', async () => {
  for (const nextOwner of ['member-1', 'member-2', null]) {
    const h = harness(); await h.restore();
    h.ports.cacheAndDownloads.purgeAllDownloads.once.push(async () => { throw new Error('disk unavailable'); });
    await h.manager.signOut(); await settle(); const event = cleanup(h);
    const guard = h.ports.cacheAndDownloads.purgeAllDownloads.calls[0][1];
    assert.equal(guard?.(), true);
    h.ports.oauth.loadProfile.impl = async () => nextOwner ? profile({ memberId: nextOwner }) : null;
    await h.manager.signIn('Google');
    assert.equal(guard?.(), nextOwner === 'member-2');
    event.retry(); await settle();
    assert.equal(h.ports.cacheAndDownloads.purgeAllDownloads.calls.length, nextOwner === 'member-2' ? 2 : 1);
    if (nextOwner === 'member-2') assert.equal(h.ports.cacheAndDownloads.purgeAllDownloads.calls.at(-1)?.[2], false);
    assert.equal(h.session().isSignedIn, true);
  }
});

it('never retries unscoped old cleanup against a newer session', async () => {
  const h = harness(); await h.manager.restore();
  h.ports.cacheAndDownloads.purgeAllDownloads.once.push(async () => { throw new Error('disk unavailable'); });
  await h.manager.signOut(); await settle(); const event = cleanup(h);
  await h.manager.signIn('Google'); event.retry(); h.manager.onAppActive(); await settle();
  assert.equal(h.ports.cacheAndDownloads.purgeAllDownloads.calls.length, 1);
});

it('does not run an already running cleanup twice or report its failure twice', async () => {
  const h = harness(); await h.restore();
  h.ports.tokenStore.clearStoredSession.impl = async () => { throw new Error('locked'); };
  await h.manager.signOut(); await settle(); const event = cleanup(h);
  const retry = deferred<void>(); h.ports.tokenStore.clearStoredSession.impl = () => retry.promise;
  event.retry(); event.retry(); h.manager.onAppActive(); await settle();
  assert.equal(h.ports.tokenStore.clearStoredSession.calls.length, 2);
  retry.reject(new Error('still locked')); await settle(); assert.equal(h.events.length, 1);
});

it('ignores pending-send inspection and confirmation after a newer generation', async () => {
  for (const waitingForConfirmation of [false, true]) {
    const h = harness(); await h.restore(); const count = deferred<number>();
    h.ports.offlineQueue.countPendingOfflineItems.impl = () => count.promise;
    const signingOut = h.manager.signOut();
    if (waitingForConfirmation) { count.resolve(1); await settle(); }
    await h.manager.signIn('Google');
    if (waitingForConfirmation) confirmation(h).confirm(); else count.resolve(1);
    await signingOut;
    assert.equal(h.session().isSignedIn, true); assert.equal(h.ports.tokenStore.clearStoredSession.calls.length, 0);
    assert.equal(h.discard.calls.length, 0);
    if (!waitingForConfirmation) assert.equal(h.events.length, 0);
  }
});

it('does not write a stale retry or identity shell over a newer sign-in', async () => {
  const h = harness(); await h.restore();
  h.ports.tokenStore.writeStoredSession.once.push(async () => { throw new Error('locked'); });
  await h.manager.signIn('Google');
  const write = deferred<StoredSession>();
  h.ports.tokenStore.writeStoredSession.once.push(() => write.promise);
  h.manager.onAppActive(); await settle();
  const newer = h.manager.signInWithPassword('new@example.com', 'secret');
  write.resolve(h.stored()); await newer; await settle();
  assert.equal(h.session().isSignedIn, true);
  assert.equal(h.ports.tokenStore.writeStoredSession.calls.length, 3);
  const identityWrites = h.ports.tokenStore.writeStoredIdentityShell.calls.length;
  h.manager.onAppActive(); await settle();
  assert.equal(h.ports.tokenStore.writeStoredSession.calls.length, 3);
  assert.equal(h.ports.tokenStore.writeStoredIdentityShell.calls.length, identityWrites);
});
