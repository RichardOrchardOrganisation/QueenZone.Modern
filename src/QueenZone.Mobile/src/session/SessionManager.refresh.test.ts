import assert from 'node:assert/strict';
import { it } from 'node:test';
import { harness, settle, tokens, profile, deferred, ApiError, TokenEndpointError } from '../test/sessionManager.ts';

it("refreshes an expired stored token", async () => {
  const h = harness(); await h.restore({ expiresAt: h.clock.now() - 1 });
  assert.equal(h.session().accessToken, 'next');
  assert.deepEqual(h.ports.oauth.refreshAccessToken.calls, [['http://qz.test', 'refresh-token']]);
});

it("applies a cached identity shell before an expired-access refresh resolves", async () => {
  const h = harness(); const refresh = deferred<ReturnType<typeof tokens>>();
  h.ports.oauth.refreshAccessToken.impl = () => refresh.promise;
  const restoring = h.restore({ expiresAt: h.clock.now() - 1, identity: { displayName: 'Freddie', memberId: 'member-1' } });
  await settle();
  assert.equal(h.session().isRestoring, false); assert.equal(h.session().displayName, 'Freddie');
  assert.equal(h.session().accessToken, 'access-token');
  const first = h.manager.ensureAccessToken(); const second = h.manager.ensureAccessToken();
  assert.equal(first, second); assert.equal(h.ports.oauth.refreshAccessToken.calls.length, 1);
  refresh.resolve(tokens({ accessToken: 'next' })); await restoring;
  assert.deepEqual(await Promise.all([first, second]), ['next', 'next']);
  assert.equal(h.session().displayName, 'Freddie'); assert.equal(h.ports.tokenStore.clearStoredSession.calls.length, 0);
});

it("single-flights concurrent ensureAccessToken callers onto one /token", async () => {
  const h = harness(); const refresh = deferred<ReturnType<typeof tokens>>();
  h.ports.oauth.refreshAccessToken.impl = () => refresh.promise;
  const restoring = h.restore({ expiresAt: h.clock.now() - 1, identity: { displayName: 'Freddie', memberId: 'member-1' } });
  await settle();
  assert.equal(h.session().isRestoring, false); assert.equal(h.session().displayName, 'Freddie');
  assert.equal(h.session().accessToken, 'access-token');
  const first = h.manager.ensureAccessToken(); const second = h.manager.ensureAccessToken();
  assert.equal(first, second); assert.equal(h.ports.oauth.refreshAccessToken.calls.length, 1);
  refresh.resolve(tokens({ accessToken: 'next' })); await restoring;
  assert.deepEqual(await Promise.all([first, second]), ['next', 'next']);
  assert.equal(h.session().displayName, 'Freddie'); assert.equal(h.ports.tokenStore.clearStoredSession.calls.length, 0);
});

it("clears local state when refresh fails", async () => {
  const h = harness(); h.ports.oauth.refreshAccessToken.impl = async () => { throw new Error('expired'); };
  await h.restore({ expiresAt: h.clock.now() - 1, identity: { displayName: 'Freddie', memberId: 'member-1' } });
  assert.equal(h.manager.getSnapshot().status, 'signedOut'); assert.equal(h.session().displayName, null);
  assert.equal(h.ports.tokenStore.clearStoredSession.calls.length, 1);
});

it("clears local state on a definite invalid_grant refresh failure", async () => {
  const h = harness(); h.ports.oauth.refreshAccessToken.impl = async () => { throw new Error('invalid_grant'); };
  await h.restore({ expiresAt: h.clock.now() - 1, identity: { displayName: 'Freddie', memberId: 'member-1' } });
  assert.equal(h.manager.getSnapshot().status, 'signedOut'); assert.equal(h.session().displayName, null);
  assert.equal(h.ports.tokenStore.clearStoredSession.calls.length, 1);
});

it("still signs out on a dead grant from the token endpoint", async () => {
  const h = harness(); h.ports.oauth.refreshAccessToken.impl = async () => { throw new TokenEndpointError(400, 'invalid_grant', 'invalid_grant'); };
  await h.restore({ expiresAt: h.clock.now() - 1, identity: { displayName: 'Freddie', memberId: 'member-1' } });
  assert.equal(h.manager.getSnapshot().status, 'signedOut'); assert.equal(h.session().displayName, null);
  assert.equal(h.ports.tokenStore.clearStoredSession.calls.length, 1);
});

it("keeps local member identity when an expired refresh fails offline", async () => {
  const h = harness(); h.ports.oauth.refreshAccessToken.impl = async () => { throw new TypeError('Network request failed'); };
  await h.restore({ expiresAt: h.clock.now() - 1, identity: { displayName: 'Freddie', memberId: 'member-1' } });
  assert.equal(h.session().accessToken, 'access-token'); assert.equal(h.session().displayName, 'Freddie');
  assert.equal(h.ports.tokenStore.clearStoredSession.calls.length, 0);
});

it("keeps the session when the per-account limiter rate limits a refresh", async () => {
  const h = harness(); h.ports.oauth.refreshAccessToken.impl = async () => { throw new TokenEndpointError(429, 'temporarily_unavailable', 'Too many attempts. Try again later.'); };
  await h.restore({ expiresAt: h.clock.now() - 1, identity: { displayName: 'Freddie', memberId: 'member-1' } });
  assert.equal(h.session().accessToken, 'access-token'); assert.equal(h.session().displayName, 'Freddie');
  assert.equal(h.ports.tokenStore.clearStoredSession.calls.length, 0);
});

it("keeps the session when the token endpoint is down", async () => {
  const h = harness(); h.ports.oauth.refreshAccessToken.impl = async () => { throw new TokenEndpointError(503, '', 'Could not complete sign-in.'); };
  await h.restore({ expiresAt: h.clock.now() - 1, identity: { displayName: 'Freddie', memberId: 'member-1' } });
  assert.equal(h.session().accessToken, 'access-token'); assert.equal(h.session().displayName, 'Freddie');
  assert.equal(h.ports.tokenStore.clearStoredSession.calls.length, 0);
});

it("keeps the session when mobile auth is unconfigured after a deploy", async () => {
  const h = harness(); h.ports.oauth.refreshAccessToken.impl = async () => { throw new TokenEndpointError(400, 'temporarily_unavailable', 'Mobile auth is not configured.'); };
  await h.restore({ expiresAt: h.clock.now() - 1, identity: { displayName: 'Freddie', memberId: 'member-1' } });
  assert.equal(h.session().accessToken, 'access-token'); assert.equal(h.session().displayName, 'Freddie');
  assert.equal(h.ports.tokenStore.clearStoredSession.calls.length, 0);
});

it("refreshes a locally valid token when /me returns 401", async () => {
  const h = harness(); h.ports.oauth.loadProfile.once.push(async () => { throw ApiError.http(401, 'Unauthorized'); });
  await h.restore(); assert.equal(h.session().accessToken, 'next'); assert.equal(h.session().displayName, 'Freddie');
  assert.deepEqual(h.ports.oauth.refreshAccessToken.calls, [['http://qz.test', 'refresh-token']]);
});

it("single-flights a refreshProfile 401 recovery with a concurrent refresh", async () => {
  const h = harness(); await h.restore(); const refresh = deferred<ReturnType<typeof tokens>>();
  h.ports.oauth.refreshAccessToken.impl = () => refresh.promise;
  h.ports.oauth.loadProfile.impl = async () => { throw ApiError.http(401, 'Unauthorized'); };
  const first = h.manager.refreshProfile(); const second = h.manager.refreshProfile(); await settle();
  assert.equal(h.ports.oauth.refreshAccessToken.calls.length, 1);
  h.ports.oauth.loadProfile.impl = async () => profile(); refresh.resolve(tokens({ accessToken: 'next' }));
  await Promise.all([first, second]); assert.equal(h.session().accessToken, 'next');
  assert.equal(h.ports.oauth.refreshAccessToken.calls.length, 1); assert.equal(h.ports.tokenStore.clearStoredSession.calls.length, 0);
});

it("refreshes an expired token from ensureAccessToken and app foreground", async () => {
  const h = harness(); await h.restore(); h.clock.advance(120_000); await h.manager.ensureAccessToken();
  assert.equal(h.session().accessToken, 'next'); h.clock.advance(60_000);
  h.ports.oauth.refreshAccessToken.impl = async () => tokens({ accessToken: 'foreground' });
  h.manager.onAppActive(); await settle(); assert.equal(h.session().accessToken, 'foreground');
});

it("defers an expired-token refresh on an iOS background launch until the app is active", async () => {
  const h = harness(); h.setAppState('background');
  await h.restore({ expiresAt: h.clock.now() - 1, identity: { displayName: 'Freddie', memberId: 'member-1' } });
  assert.equal(h.session().displayName, 'Freddie'); assert.equal(h.ports.oauth.refreshAccessToken.calls.length, 0);
  assert.equal(h.ports.oauth.loadProfile.calls.length, 0); assert.equal(h.ports.tokenStore.clearStoredSession.calls.length, 0);
  h.setAppState('active'); h.manager.onAppActive(); await settle(); assert.equal(h.session().accessToken, 'next');
  assert.equal(h.ports.oauth.refreshAccessToken.calls.length, 1);
});

it("does not rotate or sign out when /me returns 401 during a background launch", async () => {
  const h = harness(); h.setAppState('background'); h.ports.oauth.loadProfile.impl = async () => { throw ApiError.http(401, 'Unauthorized'); };
  await h.restore({ identity: { displayName: 'Freddie', memberId: 'member-1' } });
  assert.equal(h.session().accessToken, 'access-token'); assert.equal(h.session().profile?.memberId, 'member-1');
  assert.equal(h.session().isRestoring, false); assert.equal(h.ports.oauth.refreshAccessToken.calls.length, 0);
  assert.equal(h.ports.tokenStore.clearStoredSession.calls.length, 0); assert.deepEqual(h.ports.cacheAndDownloads.reconcileDownloads.calls, [['member-1']]);
});

it("does not rotate the grant from ensureAccessToken while backgrounded", async () => {
  const h = harness(); await h.restore(); h.setAppState('background'); h.clock.advance(120_000);
  assert.equal(await h.manager.ensureAccessToken(), null); assert.equal(h.ports.oauth.refreshAccessToken.calls.length, 0);
  assert.equal(h.session().isSignedIn, true); assert.equal(h.ports.tokenStore.clearStoredSession.calls.length, 0);
});

it("keeps a rotated grant in memory when persisting it fails for a non-lock reason", async () => {
  const h = harness(); await h.restore(); h.clock.advance(120_000);
  h.ports.tokenStore.writeStoredSession.once.push(async () => { throw new Error('SecureStore staging write could not be confirmed'); });
  h.ports.oauth.refreshAccessToken.impl = async () => tokens({ accessToken: 'next', refreshToken: 'next-refresh' });
  await h.manager.ensureAccessToken(); assert.equal(h.session().accessToken, 'next');
  h.clock.advance(2_000_000); h.ports.oauth.refreshAccessToken.impl = async () => tokens({ accessToken: 'third', refreshToken: 'third-refresh' });
  await h.manager.ensureAccessToken(); assert.equal(h.session().accessToken, 'third');
  assert.deepEqual(h.ports.oauth.refreshAccessToken.calls.at(-1), ['http://qz.test', 'next-refresh']);
  const writes = h.ports.tokenStore.writeStoredSession.calls.length; h.manager.onAppActive(); await settle();
  assert.equal(h.ports.tokenStore.writeStoredSession.calls.length, writes); assert.equal(h.ports.tokenStore.clearStoredSession.calls.length, 0);
});

it("stays signed in with the refreshed token when a background refresh still gets a 401 from /me", async () => {
  const h = harness(); await h.restore(); h.clock.advance(120_000);
  h.ports.oauth.loadProfile.impl = async () => { throw ApiError.http(401, 'Unauthorized'); };
  await h.manager.ensureAccessToken(); assert.equal(h.session().accessToken, 'next'); assert.equal(h.session().displayName, 'Freddie');
  assert.equal(h.ports.tokenStore.clearStoredSession.calls.length, 0);
});

it("keeps a cached identity shell when /me fails after a successful refresh", async () => {
  const h = harness(); const refresh = deferred<ReturnType<typeof tokens>>(); h.ports.oauth.refreshAccessToken.impl = () => refresh.promise;
  h.ports.oauth.loadProfile.impl = async () => null;
  const restoring = h.restore({ expiresAt: h.clock.now() - 1, identity: { displayName: 'Freddie', memberId: 'member-1', avatarPath: '/avatars/1.jpg' } });
  await settle(); assert.equal(h.session().displayName, 'Freddie'); refresh.resolve(tokens({ accessToken: 'next' })); await restoring;
  assert.equal(h.session().accessToken, 'next'); assert.equal(h.session().profile?.avatarPath, '/avatars/1.jpg');
  assert.equal(h.session().displayName, 'Freddie'); assert.equal(h.ports.tokenStore.clearStoredSession.calls.length, 0);
});

it("ignores a late refreshed grant after sign-out and a new account sign-in", async () => {
  const h = harness(); await h.restore(); const refresh = deferred<ReturnType<typeof tokens>>();
  h.ports.oauth.refreshAccessToken.impl = () => refresh.promise; h.clock.advance(120_000);
  const pending = h.manager.ensureAccessToken(); await settle(); assert.equal(h.ports.oauth.refreshAccessToken.calls.length, 1);
  await h.manager.signOut(); h.ports.oauth.signInWithProvider.impl = async () => tokens({ accessToken: 'new-account' });
  h.ports.oauth.loadProfile.impl = async () => profile({ memberId: 'member-2', displayName: 'Brian' });
  await h.manager.signIn('Google'); refresh.resolve(tokens({ accessToken: 'stale-access', refreshToken: 'stale-refresh' }));
  assert.equal(await pending, null); assert.equal(h.session().accessToken, 'new-account'); assert.equal(h.session().displayName, 'Brian');
  assert.equal(h.ports.tokenStore.writeStoredSession.calls.some(([grant]) => grant.accessToken === 'stale-access'), false);
});

it("ignores a late profile after sign-out and does not queue its identity write", async () => {
  const h = harness(); await h.restore(); const me = deferred<ReturnType<typeof profile>>(); h.ports.oauth.loadProfile.impl = () => me.promise;
  const pending = h.manager.refreshProfile(); await settle(); await h.manager.signOut();
  const writes = h.ports.tokenStore.writeStoredIdentityShell.calls.length;
  me.resolve(profile({ displayName: 'Stale identity' })); assert.equal(await pending, null); await settle();
  assert.equal(h.manager.getSnapshot().status, 'signedOut'); assert.equal(h.ports.tokenStore.writeStoredIdentityShell.calls.length, writes);
});

it('a stale rejected refresh cannot clear a newer grant or its refresh flight', async () => {
  const h = harness(); await h.restore(); const oldRefresh = deferred<ReturnType<typeof tokens>>();
  h.ports.oauth.refreshAccessToken.impl = () => oldRefresh.promise;
  const old = h.manager.refresh(); await h.manager.signOut(); await h.manager.signIn('Google');
  const newRefresh = deferred<ReturnType<typeof tokens>>(); h.ports.oauth.refreshAccessToken.impl = () => newRefresh.promise;
  const current = h.manager.refresh(); oldRefresh.reject(new Error('invalid_grant'));
  assert.equal(await old, null); assert.equal(h.manager.refresh(), current);
  newRefresh.resolve(tokens({ accessToken: 'current' })); assert.equal(await current, 'current');
  assert.equal(h.session().accessToken, 'current'); assert.equal(h.ports.tokenStore.clearStoredSession.calls.length, 1);
});

it('recovers rejected GET tokens through the same flight and reuses an already rotated token', async () => {
  const h = harness(); await h.manager.restore();
  assert.equal(await h.manager.recoverRejectedAccessToken('missing'), null);
  await h.manager.signIn('Google'); const refresh = deferred<ReturnType<typeof tokens>>();
  h.ports.oauth.refreshAccessToken.impl = () => refresh.promise;
  const first = h.manager.recoverRejectedAccessToken('access-token');
  const second = h.manager.recoverRejectedAccessToken('access-token'); assert.equal(first, second);
  refresh.resolve(tokens({ accessToken: 'next' })); assert.equal(await first, 'next');
  assert.equal(await h.manager.recoverRejectedAccessToken('access-token'), 'next');
  assert.equal(h.ports.oauth.refreshAccessToken.calls.length, 1);
});

it('unknown app state does not defer refresh and timeout errors retain the member', async () => {
  for (const error of [ApiError.timeout(), ApiError.offline(), ApiError.http(500, 'unavailable')]) {
    const h = harness(); h.setAppState('unknown'); h.ports.oauth.refreshAccessToken.impl = async () => { throw error; };
    await h.restore({ expiresAt: h.clock.now() - 1, identity: { memberId: 'member-1', displayName: 'Freddie' } });
    assert.equal(h.ports.oauth.refreshAccessToken.calls.length, 1);
    assert.equal(h.session().displayName, 'Freddie'); assert.equal(h.ports.tokenStore.clearStoredSession.calls.length, 0);
  }
});

it('refreshProfile handles missing grants, non-auth failures and expired rejected grants', async () => {
  const h = harness(); await h.manager.restore(); assert.equal(await h.manager.refreshProfile(), null);
  h.manager.setAccessToken('manual'); h.ports.oauth.loadProfile.impl = async () => { throw ApiError.http(401, 'Unauthorized'); };
  assert.equal(await h.manager.refreshProfile(), null); assert.equal(h.manager.getSnapshot().status, 'signedOut');
  h.ports.oauth.loadProfile.impl = async () => profile(); await h.manager.signIn('Google');
  h.ports.oauth.loadProfile.impl = async () => { throw new Error('profile unavailable'); };
  assert.deepEqual(await h.manager.refreshProfile(), profile());
  h.ports.oauth.loadProfile.impl = async () => { throw ApiError.http(401, 'Unauthorized'); };
  h.ports.oauth.refreshAccessToken.impl = async () => { throw new Error('invalid_grant'); };
  assert.equal(await h.manager.refreshProfile(), null); assert.equal(h.manager.getSnapshot().status, 'signedOut');
});

it('invalidates a profile waiting for refresh when sign-out intervenes', async () => {
  const h = harness(); await h.restore(); h.clock.advance(120_000);
  const refresh = deferred<ReturnType<typeof tokens>>(); h.ports.oauth.refreshAccessToken.impl = () => refresh.promise;
  const pending = h.manager.refreshProfile(); await h.manager.signOut(); refresh.resolve(tokens());
  assert.equal(await pending, null); assert.equal(h.manager.getSnapshot().status, 'signedOut');
});
