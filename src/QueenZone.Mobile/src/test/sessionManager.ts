import assert from 'node:assert/strict';
import { register } from 'node:module';
import { pathToFileURL } from 'node:url';
import type { SessionPorts, SessionEvent } from '../session/SessionManager';
import type { StoredSession } from '../session/tokenStore';
import type { AuthTokens } from '../api/auth';
import type { MemberProfile } from '../api/me';

register(`data:text/javascript,${encodeURIComponent(`
  export async function resolve(specifier, context, nextResolve) {
    if (specifier.startsWith('.') && !/\\.(?:[cm]?[jt]s|json)$/.test(specifier)) {
      try { return await nextResolve(specifier + '.ts', context); }
      catch { return nextResolve(specifier, context); }
    }
    return nextResolve(specifier, context);
  }
`)}`, pathToFileURL('./'));

export const { SessionManager, sessionFromSnapshot } = await import('../session/SessionManager.ts');
export const { ApiError, TokenEndpointError } = await import('../api/errors.ts');
export const { authTokensFixture: tokens, memberProfileFixture: profile, deferred } = await import('./fixtures.ts');
export const { ContentCache } = await import('../cache/contentCache.ts');
export const { createMemoryStorage } = await import('../cache/storage.ts');
export const { conversationCacheKey, forumTopicCacheKey, privateMemberCachePrefix } = await import('../cache/keys.ts');

export function fake<Args extends unknown[], Result>(implementation: (...args: Args) => Result) {
  const calls: Args[] = [];
  const once: ((...args: Args) => Result)[] = [];
  const fn = Object.assign((...args: Args): Result => {
    calls.push(args);
    return (once.shift() ?? fn.impl)(...args);
  }, { calls, once, impl: implementation });
  return fn;
}

export class FakeClock {
  time = 1_700_000_000_000;
  private sequence = 0;
  private timers = new Map<number, { at: number; callback: () => void }>();
  now = () => this.time;
  setTimeout = (callback: () => void, ms: number): ReturnType<typeof setTimeout> => {
    const id = ++this.sequence;
    this.timers.set(id, { at: this.time + ms, callback });
    return id as unknown as ReturnType<typeof setTimeout>;
  };
  clearTimeout = (id: ReturnType<typeof setTimeout>) => { this.timers.delete(id as unknown as number); };
  advance(ms: number) {
    this.time += ms;
    for (const [id, timer] of this.timers) {
      if (timer.at <= this.time) { this.timers.delete(id); timer.callback(); }
    }
  }
  get pendingTimers() { return this.timers.size; }
}

// Drain only promise work; no real timers, sleeps or native runtime.
export async function settle() { for (let i = 0; i < 40; i++) await Promise.resolve(); }

export function harness() {
  const clock = new FakeClock();
  const events: SessionEvent[] = [];
  let appState = 'active';
  const tokenStore = {
    readStoredSession: fake(async (): Promise<StoredSession | null> => null),
    writeStoredSession: fake(async (grant: AuthTokens, _resetIdentity?: boolean): Promise<StoredSession> => ({ ...grant, expiresAt: clock.now() + 60_000 })),
    writeStoredIdentityShell: fake(async (_shell: Parameters<SessionPorts['tokenStore']['writeStoredIdentityShell']>[0]) => { }),
    clearStoredSession: fake(async () => { }),
    isKeychainLockedError: (error: unknown) => error instanceof Error && error.message === 'User interaction is not allowed',
  };
  const oauth = {
    signInWithProvider: fake(async (_url: string, _provider: string) => tokens()),
    signInWithPassword: fake(async (_url: string, _email: string, _password: string) => tokens()),
    refreshAccessToken: fake(async (_url: string, _refresh: string) => tokens({ accessToken: 'next' })),
    logoutRemote: fake(async (_url: string, _token: string) => { }),
    revokeRefreshToken: fake(async (_url: string, _token: string) => { }),
    loadProfile: fake(async (_token: string): Promise<MemberProfile | null> => profile()),
  };
  const push = {
    syncPushRegistration: fake(async (_token: string, _member?: string | null) => { }),
    refreshPushRegistration: fake(async (_token: string, _member?: string | null) => { }),
    clearPushRegistration: fake(async (_token: string, _member?: string | null) => { }),
  };
  const cacheAndDownloads = {
    purgePrivateContentCache: fake(async (_member?: string | null) => { }),
    purgeAllDownloads: fake(async (_member?: string | null, _isCurrent?: () => boolean, _allowUnscoped?: boolean) => { }),
    reconcileDownloads: fake(async (_member: string) => { }),
  };
  const discard = fake(async () => { });
  const offlineQueue = {
    configureOfflineQueueAuth: fake((_auth: Parameters<SessionPorts['offlineQueue']['configureOfflineQueueAuth']>[0]) => { }),
    countPendingOfflineItems: fake(async (_member?: string | null) => 0),
    prepareOfflineQueueDiscard: fake((_member?: string | null) => discard),
    invalidateOfflineQueueFlush: fake(() => { }),
    flushOfflineQueue: fake(async () => { }),
  };
  const logger = { captureException: fake((_error: unknown, _context?: { tags: { operation: string } }) => { }) };
  const ports = {
    tokenStore, oauth, push, cacheAndDownloads, offlineQueue, logger, clock,
    config: { apiBaseUrl: 'http://qz.test', appEnv: 'development', smokeEmbed: false as boolean, isDev: true as boolean },
    isInBackground: () => appState === 'background', onEvent: (event: SessionEvent) => { events.push(event); },
  } satisfies SessionPorts;
  const manager = new SessionManager(ports);
  const stored = (overrides: Partial<StoredSession> = {}): StoredSession => ({ ...tokens(), expiresAt: clock.now() + 60_000, ...overrides });
  async function restore(overrides: Partial<StoredSession> = {}) {
    tokenStore.readStoredSession.impl = async () => stored(overrides);
    await manager.restore();
    await settle();
  }
  return {
    manager, ports, clock, events, discard, stored, restore,
    setAppState: (value: string) => { appState = value; },
    session: () => sessionFromSnapshot(manager.getSnapshot()),
  };
}

export function confirmation(h: ReturnType<typeof harness>) {
  const event = h.events.at(-1);
  assert.equal(event?.type, 'confirmSignOut');
  if (event?.type !== 'confirmSignOut') throw new Error('Missing confirmation');
  return event;
}
export function cleanup(h: ReturnType<typeof harness>) {
  const event = h.events.at(-1);
  assert.equal(event?.type, 'cleanupFailed');
  if (event?.type !== 'cleanupFailed') throw new Error('Missing cleanup failure');
  return event;
}
