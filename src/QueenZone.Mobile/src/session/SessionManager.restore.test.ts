import assert from 'node:assert/strict';
import { it } from 'node:test';
import { harness, settle, tokens, profile, deferred, ApiError, TokenEndpointError } from '../test/sessionManager.ts';
import type { StoredSession } from './tokenStore';

it("finishes restore with no stored session as signed out", async () => {
  const h = harness();
  assert.equal(h.manager.getSnapshot().status, 'restoring');
  await h.manager.restore();
  assert.deepEqual(h.session(), { isSignedIn: false, isRestoring: false, displayName: null, accessToken: null, profile: null });
});

it("finishes restore and reports an unexpected credential read failure in %s", async () => {
  for (const appEnv of ['development', 'production']) {
    const h = harness(); h.ports.config.appEnv = appEnv;
    const error = new Error('credential storage failed');
    h.ports.tokenStore.readStoredSession.impl = async () => { throw error; };
    await h.manager.restore();
    assert.equal(h.manager.getSnapshot().status, 'signedOut');
    assert.deepEqual(h.ports.logger.captureException.calls, [[error, { tags: { operation: 'session.restore.credentials' } }]]);
    assert.equal(h.ports.tokenStore.clearStoredSession.calls.length, 0);
    assert.equal(h.ports.oauth.refreshAccessToken.calls.length, 0);
    assert.equal(h.ports.cacheAndDownloads.reconcileDownloads.calls.length, 0);
  }
});

it("ignores a credential read failure after %s supersedes restore", async () => {
  for (const action of ['sign-in', 'sign-out', 'unmount']) {
    const h = harness(); const read = deferred<StoredSession | null>();
    h.ports.tokenStore.readStoredSession.impl = () => read.promise;
    const restoring = h.manager.restore(); await settle();
    let signingIn: Promise<void> | undefined;
    if (action === 'sign-in') signingIn = h.manager.signIn('Google');
    else if (action === 'sign-out') await h.manager.signOut();
    else h.manager.stop();
    read.reject(new Error('stale read failure'));
    await restoring; await signingIn; await settle();
    assert.equal(h.ports.logger.captureException.calls.length, 0);
    assert.equal(h.ports.oauth.refreshAccessToken.calls.length, 0);
    assert.equal(h.ports.tokenStore.clearStoredSession.calls.length, action === 'sign-out' ? 1 : 0);
    assert.equal(h.ports.cacheAndDownloads.reconcileDownloads.calls.length, action === 'sign-in' ? 1 : 0);
    if (action === 'sign-in') assert.equal(h.session().profile?.memberId, 'member-1');
    if (action === 'sign-out') assert.equal(h.manager.getSnapshot().status, 'signedOut');
  }
});

it("single-flights repeated foreground restore attempts while the keychain retry is pending", async () => {
  const h = harness(); const read = deferred<StoredSession | null>(); const refresh = deferred<ReturnType<typeof tokens>>();
  h.ports.tokenStore.readStoredSession.once.push(async () => { throw new Error('User interaction is not allowed'); });
  h.ports.tokenStore.readStoredSession.impl = () => read.promise;
  h.ports.oauth.refreshAccessToken.impl = () => refresh.promise;
  await h.manager.restore(); assert.equal(h.session().isRestoring, true);
  h.manager.onAppActive(); h.manager.onAppActive(); await settle();
  assert.equal(h.ports.tokenStore.readStoredSession.calls.length, 2);
  read.resolve(h.stored({ expiresAt: h.clock.now() - 1 })); await settle();
  assert.equal(h.session().isRestoring, false); assert.equal(h.session().accessToken, 'access-token');
  assert.equal(h.session().profile, null);
  h.manager.onAppActive(); refresh.resolve(tokens({ accessToken: 'next' })); await settle();
  assert.equal(h.session().profile?.memberId, 'member-1');
  assert.equal(h.ports.oauth.refreshAccessToken.calls.length, 1);
  assert.equal(h.ports.tokenStore.clearStoredSession.calls.length, 0);
  assert.equal(h.ports.cacheAndDownloads.reconcileDownloads.calls.length, 1);
});

it("clears an expired grant without a cached member when foreground refresh is rejected", async () => {
  const h = harness(); h.ports.oauth.refreshAccessToken.impl = async () => { throw new TokenEndpointError(400, 'invalid_grant', 'Expired'); };
  await h.restore({ expiresAt: h.clock.now() - 1 });
  assert.equal(h.manager.getSnapshot().status, 'signedOut');
  assert.equal(h.session().profile, null);
  assert.equal(h.ports.tokenStore.clearStoredSession.calls.length, 1);
  assert.equal(h.ports.oauth.refreshAccessToken.calls.length, 1);
  assert.equal(h.ports.cacheAndDownloads.reconcileDownloads.calls.length, 0);
});

it("clears an expired stored session when refresh yields no token or cached member", async () => {
  const h = harness(); await h.restore({ accessToken: '', refreshToken: '', expiresAt: h.clock.now() - 1 });
  assert.equal(h.manager.getSnapshot().status, 'signedOut');
  assert.equal(h.ports.tokenStore.clearStoredSession.calls.length, 1);
  assert.equal(h.ports.oauth.refreshAccessToken.calls.length, 0);
  assert.equal(h.ports.cacheAndDownloads.reconcileDownloads.calls.length, 0);
});

it("clears a grant that expires while the restore profile request is pending", async () => {
  const h = harness(); const me = deferred<ReturnType<typeof profile> | null>();
  h.ports.oauth.loadProfile.impl = () => me.promise;
  const restoring = h.restore(); await settle(); h.clock.advance(120_000);
  me.reject(ApiError.http(401, 'Unauthorized')); await restoring;
  assert.equal(h.manager.getSnapshot().status, 'signedOut');
  assert.equal(h.ports.tokenStore.clearStoredSession.calls.length, 1);
  assert.equal(h.ports.oauth.refreshAccessToken.calls.length, 0);
});

it("ignores a superseded restore profile (reject=%s)", async () => {
  for (const reject of [false, true]) {
    const h = harness(); const me = deferred<ReturnType<typeof profile>>();
    h.ports.oauth.loadProfile.impl = () => me.promise;
    const restoring = h.restore(); await settle(); await h.manager.signOut();
    if (reject) me.reject(ApiError.http(401, 'Unauthorized')); else me.resolve(profile());
    await restoring;
    assert.equal(h.manager.getSnapshot().status, 'signedOut');
    assert.equal(h.session().profile, null);
    assert.equal(h.ports.tokenStore.clearStoredSession.calls.length, 1);
    assert.equal(h.ports.oauth.refreshAccessToken.calls.length, 0);
    assert.equal(h.ports.cacheAndDownloads.reconcileDownloads.calls.length, 0);
  }
});

it("ignores stored credentials resolved after sign-out", async () => {
  const h = harness(); const read = deferred<StoredSession | null>();
  h.ports.tokenStore.readStoredSession.impl = () => read.promise;
  const restoring = h.manager.restore(); await settle(); await h.manager.signOut();
  read.resolve(h.stored({ expiresAt: h.clock.now() - 1, identity: { displayName: 'Old identity', memberId: 'member-1' } }));
  await restoring; await settle();
  assert.equal(h.manager.getSnapshot().status, 'signedOut'); assert.equal(h.session().profile, null);
  assert.equal(h.ports.cacheAndDownloads.reconcileDownloads.calls.length, 0);
  assert.equal(h.ports.oauth.refreshAccessToken.calls.length, 0);
  assert.equal(h.ports.tokenStore.clearStoredSession.calls.length, 1);
});

it("does not restore a delayed stored identity after sign-out", async () => {
  const h = harness(); const read = deferred<StoredSession | null>();
  h.ports.tokenStore.readStoredSession.impl = () => read.promise;
  const restoring = h.manager.restore(); await settle(); await h.manager.signOut();
  read.resolve(h.stored({ expiresAt: h.clock.now() + 60_000, identity: { displayName: 'Old identity', memberId: 'member-1' } }));
  await restoring; await settle();
  assert.equal(h.manager.getSnapshot().status, 'signedOut'); assert.equal(h.session().profile, null);
  assert.equal(h.ports.cacheAndDownloads.reconcileDownloads.calls.length, 0);
  assert.equal(h.ports.oauth.refreshAccessToken.calls.length, 0);
  assert.equal(h.ports.tokenStore.clearStoredSession.calls.length, 1);
});

it("keeps the restored token when profile reconciliation fails synchronously", async () => {
  const h = harness(); h.ports.cacheAndDownloads.reconcileDownloads.impl = () => { throw new Error('storage unavailable'); };
  await h.restore({});
  assert.equal(h.session().accessToken, 'access-token');
  assert.equal(h.session().isRestoring, false); assert.equal(h.session().profile, null);
  assert.equal(h.ports.tokenStore.clearStoredSession.calls.length, 0);
  assert.equal(h.ports.oauth.refreshAccessToken.calls.length, 0);
});

it("finishes restore when unexpected shell reconciliation fails synchronously", async () => {
  const h = harness(); h.ports.cacheAndDownloads.reconcileDownloads.impl = () => { throw new Error('storage unavailable'); };
  await h.restore({ identity: { displayName: 'Freddie', memberId: 'member-1' } });
  assert.equal(h.session().accessToken, null);
  assert.equal(h.session().isRestoring, false); assert.equal(h.session().profile, null);
  assert.equal(h.ports.tokenStore.clearStoredSession.calls.length, 1);
  assert.equal(h.ports.oauth.refreshAccessToken.calls.length, 0);
});

it("restores a valid stored access token and profile", async () => {
  const h = harness(); await h.restore();
  assert.equal(h.session().accessToken, 'access-token'); assert.deepEqual(h.session().profile, profile());
  assert.equal(h.session().isRestoring, false); assert.equal(h.ports.oauth.refreshAccessToken.calls.length, 0);
});

it("fail-opens a hung development restore so Profile is not stuck restoring", async () => {
  const h = harness(); const read = deferred<StoredSession | null>(); h.ports.tokenStore.readStoredSession.impl = () => read.promise;
  const restoring = h.manager.restore(); await settle(); h.clock.advance(4_999); await settle();
  assert.equal(h.session().isRestoring, true); h.clock.advance(1); await restoring;
  assert.equal(h.manager.getSnapshot().status, 'signedOut'); assert.equal(h.ports.tokenStore.clearStoredSession.calls.length, 0);
  assert.equal(h.ports.logger.captureException.calls.length, 0); assert.equal(h.clock.pendingTimers, 0);
  read.resolve(h.stored()); await settle(); assert.equal(h.manager.getSnapshot().status, 'signedOut');
});

it("does not sign out when a locked keychain blocks restore, and retries on active", async () => {
  const h = harness(); h.ports.tokenStore.readStoredSession.once.push(async () => { throw new Error('User interaction is not allowed'); });
  h.ports.tokenStore.readStoredSession.impl = async () => h.stored();
  await h.manager.restore(); assert.equal(h.session().isRestoring, true); assert.equal(h.ports.tokenStore.clearStoredSession.calls.length, 0);
  h.manager.onAppActive(); await settle(); assert.equal(h.session().displayName, 'Freddie');
  assert.equal(h.ports.tokenStore.readStoredSession.calls.length, 2); assert.equal(h.ports.tokenStore.clearStoredSession.calls.length, 0);
});

it("stays signed in when profile load fails", async () => {
  const h = harness(); h.ports.oauth.loadProfile.impl = async () => null; await h.restore();
  assert.equal(h.session().isSignedIn, true); assert.equal(h.session().displayName, null);
});

it("exposes the stored access token before profile load finishes", async () => {
  const h = harness(); const me = deferred<ReturnType<typeof profile>>(); h.ports.oauth.loadProfile.impl = () => me.promise;
  const restoring = h.restore(); await settle(); assert.equal(h.session().accessToken, 'access-token'); assert.equal(h.session().isRestoring, false);
  assert.equal(h.session().displayName, null); assert.equal(h.ports.oauth.refreshAccessToken.calls.length, 0);
  me.resolve(profile()); await restoring; assert.equal(h.session().displayName, 'Freddie');
});
