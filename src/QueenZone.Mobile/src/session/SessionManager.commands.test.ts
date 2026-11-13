import assert from 'node:assert/strict';
import { it } from 'node:test';
import { harness, settle, tokens, profile, deferred } from '../test/sessionManager.ts';

it("applies the smoke session when Release smokeEmbed is baked", async () => {
  const h = harness(); h.ports.config.smokeEmbed = true; h.ports.config.isDev = false;
  h.ports.tokenStore.readStoredSession.impl = () => new Promise(() => { });
  await h.manager.restore(); assert.equal(h.manager.getSnapshot().status, 'signedOut');
  assert.equal(h.ports.tokenStore.readStoredSession.calls.length, 0);
  assert.equal(await h.manager.applySmokeSession('smoke-access'), true);
  assert.equal(h.session().accessToken, 'smoke-access');
  assert.equal(h.ports.tokenStore.writeStoredSession.calls.length, 0);
  assert.equal(h.ports.tokenStore.writeStoredIdentityShell.calls.length, 0);
  assert.deepEqual(h.ports.oauth.loadProfile.calls, [['smoke-access']]);
});

it("rejects the smoke session when __DEV__ is false", async () => {
  const h = harness(); h.ports.config.isDev = false; await h.manager.restore();
  assert.equal(await h.manager.applySmokeSession('smoke-access'), false);
  assert.equal(h.manager.getSnapshot().status, 'signedOut');
  assert.equal(h.ports.tokenStore.writeStoredSession.calls.length, 0);
});

it("signs in through the provider hop and can sign out", async () => {
  const h = harness(); await h.manager.restore(); await h.manager.signIn('Google'); await settle();
  assert.deepEqual(h.ports.oauth.signInWithProvider.calls, [['http://qz.test', 'Google']]);
  assert.deepEqual(h.ports.push.syncPushRegistration.calls.at(-1), ['access-token', 'member-1']);
  assert.deepEqual(h.ports.tokenStore.writeStoredIdentityShell.calls.at(-1), [{ displayName: 'Freddie', memberId: 'member-1', avatarPath: null }]);
  await h.manager.signOut(); await settle();
  assert.equal(h.manager.getSnapshot().status, 'signedOut');
  assert.deepEqual(h.ports.push.clearPushRegistration.calls, [['access-token', 'member-1']]);
  assert.equal(h.ports.oauth.logoutRemote.calls.length, 1); assert.equal(h.ports.oauth.revokeRefreshToken.calls.length, 1);
  assert.equal(h.ports.tokenStore.clearStoredSession.calls.length, 1);
});

it("signs in with email and password through the same token store as OAuth", async () => {
  const h = harness(); await h.manager.restore();
  h.ports.oauth.signInWithPassword.impl = async () => tokens({ accessToken: 'password-access' });
  await h.manager.signInWithPassword('reviewer@example.com', 'review-pass');
  assert.deepEqual(h.ports.oauth.signInWithPassword.calls, [['http://qz.test', 'reviewer@example.com', 'review-pass']]);
  assert.equal(h.ports.oauth.signInWithProvider.calls.length, 0);
  assert.deepEqual(h.ports.tokenStore.writeStoredSession.calls, [[tokens({ accessToken: 'password-access' }), true]]);
  assert.deepEqual(h.ports.push.syncPushRegistration.calls.at(-1), ['password-access', 'member-1']);
});

it("does not register for push before sign-in", async () => {
  const h = harness(); await h.manager.restore(); h.manager.onPushTokenChanged(); h.manager.onAppActive();
  assert.equal(h.ports.push.syncPushRegistration.calls.length, 0); assert.equal(h.ports.push.refreshPushRegistration.calls.length, 0);
});

it("registers for push when a stored session is restored", async () => {
  const h = harness(); await h.restore();
  assert.deepEqual(h.ports.push.syncPushRegistration.calls.at(-1), ['access-token', 'member-1']);
});

it("re-registers push after signing out and back in as a different member (#1094)", async () => {
  const h = harness(); await h.manager.restore();
  h.ports.oauth.signInWithProvider.impl = async () => tokens({ accessToken: 'token-a' });
  h.ports.oauth.loadProfile.impl = async () => profile({ memberId: 'member-a', displayName: 'Alice' });
  await h.manager.signIn('Google');
  assert.deepEqual(h.ports.push.syncPushRegistration.calls.at(-1), ['token-a', 'member-a']);
  await h.manager.signOut(); assert.deepEqual(h.ports.push.clearPushRegistration.calls, [['token-a', 'member-a']]);
  h.ports.oauth.signInWithProvider.impl = async () => tokens({ accessToken: 'token-b' });
  h.ports.oauth.loadProfile.impl = async () => profile({ memberId: 'member-b', displayName: 'Bob' });
  await h.manager.signIn('Google');
  assert.deepEqual(h.ports.push.syncPushRegistration.calls.at(-1), ['token-b', 'member-b']);
});

it("re-registers push when signing in as a different member without signing out first", async () => {
  const h = harness(); await h.manager.restore();
  h.ports.oauth.signInWithProvider.impl = async () => tokens({ accessToken: 'token-a' });
  h.ports.oauth.loadProfile.impl = async () => profile({ memberId: 'member-a', displayName: 'Alice' });
  await h.manager.signIn('Google');
  assert.deepEqual(h.ports.push.syncPushRegistration.calls.at(-1), ['token-a', 'member-a']);

  h.ports.oauth.signInWithProvider.impl = async () => tokens({ accessToken: 'token-b' });
  h.ports.oauth.loadProfile.impl = async () => profile({ memberId: 'member-b', displayName: 'Bob' });
  await h.manager.signIn('Google');
  assert.deepEqual(h.ports.push.syncPushRegistration.calls.at(-1), ['token-b', 'member-b']);
});

it("stays signed out when the provider hop is cancelled", async () => {
  const h = harness(); await h.manager.restore();
  h.ports.oauth.signInWithProvider.impl = async () => { throw new Error('Sign-in was cancelled.'); };
  await assert.rejects(h.manager.signIn('Google'), /cancelled/);
  assert.equal(h.manager.getSnapshot().status, 'signedOut'); assert.equal(h.ports.tokenStore.clearStoredSession.calls.length, 0);
});

it("applies a Debug smoke access token without the OAuth hop when appEnv is development", async () => {
  const h = harness(); await h.manager.restore(); assert.equal(await h.manager.applySmokeSession('smoke-access'), true);
  assert.deepEqual(h.ports.tokenStore.writeStoredSession.calls, [[{ accessToken: 'smoke-access', refreshToken: 'smoke-debug-no-refresh', expiresIn: 3600 }, true]]);
  assert.equal(h.ports.oauth.signInWithProvider.calls.length, 0);
});

it("rejects the smoke session when appEnv is %s", async () => {
  for (const appEnv of ['staging', 'production']) {
    const h = harness(); h.ports.config.appEnv = appEnv; h.ports.config.smokeEmbed = true; await h.manager.restore();
    assert.equal(await h.manager.applySmokeSession('smoke-access'), false);
    assert.equal(h.manager.getSnapshot().status, 'signedOut'); assert.equal(h.ports.tokenStore.writeStoredSession.calls.length, 0);
  }
});

it("derives isSignedIn from setAccessToken and stays signed out without a token", async () => {
  const h = harness(); await h.manager.restore();
  for (const value of [null, '', '   ']) { h.manager.setAccessToken(value); assert.equal(h.session().isSignedIn, false); assert.equal(h.session().accessToken, null); }
  h.manager.setAccessToken(' manual-token '); assert.equal(h.session().isSignedIn, true); assert.equal(h.session().accessToken, 'manual-token');
  h.manager.setAccessToken(null); assert.equal(h.session().isSignedIn, false);
});

it("ignores a sign-in response that arrives after sign-out", async () => {
  const h = harness(); await h.restore(); const login = deferred<ReturnType<typeof tokens>>(); h.ports.oauth.signInWithProvider.impl = () => login.promise;
  const signingIn = h.manager.signIn('Google'); await h.manager.signOut();
  login.resolve(tokens({ accessToken: 'late-login' })); await signingIn;
  assert.equal(h.manager.getSnapshot().status, 'signedOut'); assert.equal(h.ports.tokenStore.writeStoredSession.calls.length, 0);
});

it("clears the old identity during fresh sign-in while the new profile is delayed", async () => {
  const h = harness(); await h.restore(); const me = deferred<ReturnType<typeof profile>>(); h.ports.oauth.loadProfile.impl = () => me.promise;
  h.ports.oauth.signInWithProvider.impl = async () => tokens({ accessToken: 'new-account' });
  const signingIn = h.manager.signIn('Google'); await settle();
  assert.equal(h.session().accessToken, 'new-account'); assert.equal(h.session().displayName, null); assert.equal(h.session().profile, null);
  assert.equal(h.ports.tokenStore.writeStoredSession.calls.at(-1)?.[1], true);
  me.resolve(profile({ memberId: 'member-2', displayName: 'Brian' })); await signingIn; assert.equal(h.session().displayName, 'Brian');
});

it('publishes immutable snapshots with stable subscription and command identities', async () => {
  const h = harness(); const initial = h.manager.getSnapshot(); const signIn = h.manager.signIn;
  assert.equal(h.manager.getSnapshot(), initial); assert.equal(Object.isFrozen(initial), true);
  let notifications = 0; const unsubscribe = h.manager.subscribe(() => { notifications++; });
  await h.manager.restore(); assert.equal(notifications, 1); const signedOut = h.manager.getSnapshot();
  assert.notEqual(signedOut, initial); assert.equal(initial.status, 'restoring');
  await h.manager.signIn('Google'); assert.equal(h.manager.signIn, signIn); assert.equal(signedOut.status, 'signedOut');
  assert.equal(Object.isFrozen(h.manager.getSnapshot()), true);
  unsubscribe(); const before = notifications; h.manager.setAccessToken('manual'); assert.equal(notifications, before);
});

it('wires offline auth, network flush, foreground push refresh and teardown', async () => {
  const h = harness(); h.manager.start(); await settle();
  const auth = h.ports.offlineQueue.configureOfflineQueueAuth.calls[0][0]; assert.ok(auth);
  assert.equal(auth.getAccessToken(), null); assert.equal(auth.getMemberId(), null);
  h.manager.onNetworkOnline(); await settle(); assert.equal(h.ports.offlineQueue.flushOfflineQueue.calls.length, 0);
  await h.manager.signIn('Google'); assert.equal(auth.getMemberId(), 'member-1'); assert.equal(await auth.refreshAccessToken(), 'access-token');
  const count = h.ports.offlineQueue.flushOfflineQueue.calls.length;
  h.manager.onNetworkOnline(); await settle(); assert.equal(h.ports.offlineQueue.flushOfflineQueue.calls.length, count + 1);
  h.manager.onAppActive(); await settle(); assert.deepEqual(h.ports.push.refreshPushRegistration.calls, [['access-token', 'member-1']]);
  h.manager.stop(); assert.equal(h.ports.offlineQueue.configureOfflineQueueAuth.calls.at(-1)?.[0], null);
});

it('rejects empty smoke tokens and invalidates smoke profile completion after sign-out', async () => {
  for (const smokeEmbed of [false, true]) {
    const h = harness(); h.ports.config.smokeEmbed = smokeEmbed; await h.manager.restore();
    assert.equal(await h.manager.applySmokeSession('  '), false);
    const me = deferred<ReturnType<typeof profile>>(); h.ports.oauth.loadProfile.impl = () => me.promise;
    const applying = h.manager.applySmokeSession('smoke'); await settle(); await h.manager.signOut();
    me.resolve(profile()); assert.equal(await applying, false); assert.equal(h.manager.getSnapshot().status, 'signedOut');
  }
});
