import type { AuthTokens } from '../api/auth';
import { ApiError } from '../api/errors';
import { fallbackProfileLimits, type MemberProfile } from '../api/me';
import type { StoredSession, StoredIdentityShell } from './tokenStore';
import { createExternalStore } from '../cache/externalStore';
import { resolvePushMemberId } from '../notifications/pushMemberId';
import { isTransientRefreshFailure } from './refreshFailure';
import { isSmokeAuthEnabled, smokeAuthExpiresInSeconds, smokeAuthRefreshPlaceholder } from './smokeAuth';
import { developmentSessionRestoreTimeoutMs, isSessionRestoreTimeoutError, sessionRestoreTimeoutLabel } from './restoreTimeout';

export type Session = {
  isSignedIn: boolean;
  isRestoring: boolean;
  displayName: string | null;
  accessToken: string | null;
  profile: MemberProfile | null;
};

export type SessionActions = {
  signIn: (provider: string) => Promise<void>;
  signInWithPassword: (email: string, password: string) => Promise<void>;
  signOut: () => Promise<void>;
  refreshProfile: () => Promise<MemberProfile | null>;
  ensureAccessToken: () => Promise<string | null>;
  setAccessToken: (accessToken: string | null) => void;
  applySmokeSession: (accessToken: string) => Promise<boolean>;
};

type CleanupJob = {
  generation: number;
  memberId: string | null;
  credentials: boolean;
  operationScoped: boolean;
  run: () => Promise<void>;
  running: boolean;
  reported: boolean;
};

const pendingSendInspectionTimeoutMs = 5_000;

export type SessionEvent =
  | { type: 'cleanupFailed'; hasCurrentSession: boolean; retry: () => void }
  | { type: 'confirmSignOut'; inspectionFailed: boolean; confirm: () => void; cancel: () => void };

export type SessionPorts = {
  tokenStore: {
    readStoredSession: typeof import('./tokenStore').readStoredSession;
    writeStoredSession: typeof import('./tokenStore').writeStoredSession;
    writeStoredIdentityShell: typeof import('./tokenStore').writeStoredIdentityShell;
    clearStoredSession: typeof import('./tokenStore').clearStoredSession;
    isKeychainLockedError: typeof import('./tokenStore').isKeychainLockedError;
  };
  oauth: {
    signInWithProvider: typeof import('./oauth').signInWithProvider;
    signInWithPassword: typeof import('./oauth').signInWithPassword;
    refreshAccessToken: typeof import('./oauth').refreshAccessToken;
    logoutRemote: typeof import('./oauth').logoutRemote;
    revokeRefreshToken: typeof import('./oauth').revokeRefreshToken;
    loadProfile: (accessToken: string) => Promise<MemberProfile | null>;
  };
  push: {
    syncPushRegistration: typeof import('../notifications').syncPushRegistration;
    refreshPushRegistration: typeof import('../notifications').refreshPushRegistration;
    clearPushRegistration: typeof import('../notifications').clearPushRegistration;
  };
  cacheAndDownloads: {
    purgePrivateContentCache: typeof import('../cache').purgePrivateContentCache;
    purgeAllDownloads: typeof import('../downloads/manager').purgeAllDownloads;
    reconcileDownloads: typeof import('../downloads/manager').reconcileDownloads;
  };
  offlineQueue: {
    configureOfflineQueueAuth: typeof import('../offlineQueue').configureOfflineQueueAuth;
    countPendingOfflineItems: typeof import('../offlineQueue').countPendingOfflineItems;
    prepareOfflineQueueDiscard: typeof import('../offlineQueue').prepareOfflineQueueDiscard;
    invalidateOfflineQueueFlush: typeof import('../offlineQueue').invalidateOfflineQueueFlush;
    flushOfflineQueue: typeof import('../offlineQueue').flushOfflineQueue;
  };
  logger: { captureException: (error: unknown, context?: { tags: { operation: string } }) => unknown };
  clock: { now: () => number; setTimeout: (callback: () => void, ms: number) => ReturnType<typeof setTimeout>; clearTimeout: (timer: ReturnType<typeof setTimeout>) => void };
  config: { apiBaseUrl: string; appEnv: string; smokeEmbed?: boolean; isDev: boolean };
  isInBackground: () => boolean;
  onEvent: (event: SessionEvent) => void;
};

type Identity = Readonly<{
  displayName: string | null;
  profile: MemberProfile | null;
  refreshToken: string | null;
  expiresAt: number;
  memberId: string | null;
}>;
export type SessionSnapshot = Identity & (
  | { readonly status: 'restoring'; readonly accessToken: string | null }
  | { readonly status: 'signedOut'; readonly accessToken: null }
  | { readonly status: 'signedIn'; readonly accessToken: string }
);

export function sessionFromSnapshot(snapshot: SessionSnapshot): Session {
  return {
    isSignedIn: snapshot.accessToken !== null,
    isRestoring: snapshot.status === 'restoring',
    accessToken: snapshot.accessToken,
    displayName: snapshot.displayName,
    profile: snapshot.profile,
  };
}

const signedOut: Session = {
  isSignedIn: false,
  isRestoring: false,
  displayName: null,
  accessToken: null,
  profile: null,
};

function sessionFromAccessToken(
  accessToken: string | null | undefined,
  extras: Partial<Pick<Session, 'isRestoring' | 'displayName' | 'profile'>> = {},
): Session {
  const trimmed = accessToken?.trim() ?? '';
  const token = trimmed.length > 0 ? trimmed : null;
  return {
    isSignedIn: token !== null,
    isRestoring: extras.isRestoring ?? false,
    displayName: extras.displayName ?? null,
    accessToken: token,
    profile: extras.profile ?? null,
  };
}

export class SessionManager {
  private readonly ports: SessionPorts;
  private readonly store = createExternalStore();
  private snapshot: SessionSnapshot = Object.freeze({ status: 'restoring', accessToken: null, refreshToken: null, expiresAt: 0, memberId: null, displayName: null, profile: null });
  private generation = 0;
  private credentialWork: Promise<unknown> = Promise.resolve();
  private cleanupJobs = new Set<CleanupJob>();
  private refreshInFlight: Promise<string | null> | null = null;
  private pendingSessionWrite: { tokens: AuthTokens; resetIdentity: boolean } | null = null;
  private restoreInFlight = false;
  private lockedPending = false;
  private cancelled = false;

  constructor(ports: SessionPorts) { this.ports = ports; }

  subscribe = (listener: () => void) => this.store.subscribe('session', listener);
  getSnapshot = (): SessionSnapshot => this.snapshot;
  private get currentSession(): Session { return sessionFromSnapshot(this.snapshot); }
  private update(patch: Partial<Identity>) {
    this.snapshot = Object.freeze({ ...this.snapshot, ...patch });
  }
  private setSession(session: Session) {
    const previous = this.snapshot;
    const identity = { ...previous, displayName: session.displayName, profile: session.profile };
    this.snapshot = Object.freeze(session.isRestoring
      ? { ...identity, status: 'restoring', accessToken: session.accessToken }
      : session.accessToken !== null
        ? { ...identity, status: 'signedIn', accessToken: session.accessToken }
        : { ...identity, status: 'signedOut', accessToken: null });
    this.store.invalidate('session');
    if (!session.isRestoring && session.accessToken &&
      (previous.status === 'restoring' || previous.accessToken !== session.accessToken)) {
      void this.ports.offlineQueue.flushOfflineQueue();
    }
    if (session.accessToken && (previous.accessToken !== session.accessToken || previous.profile?.memberId !== session.profile?.memberId)) {
      this.onPushTokenChanged();
    }
  }
  smokeAuthAllowed = () => isSmokeAuthEnabled({ dev: this.ports.config.isDev, ...this.ports.config });
  private releaseSmokeEmbedEnabled = () => this.ports.config.appEnv === 'development' && this.ports.config.smokeEmbed === true;
  private withTimeout<T>(promise: Promise<T>, ms: number, label: string): Promise<T> {
    return new Promise((resolve, reject) => {
      const timer = this.ports.clock.setTimeout(() => reject(new Error(label)), ms);
      promise.then(value => { this.ports.clock.clearTimeout(timer); resolve(value); },
        error => { this.ports.clock.clearTimeout(timer); reject(error); });
    });
  }
  start = () => {
    this.cancelled = false;
    this.ports.offlineQueue.configureOfflineQueueAuth({
      getAccessToken: () => this.snapshot.accessToken,
      getMemberId: () => this.snapshot.accessToken ? resolvePushMemberId(this.snapshot.accessToken, this.snapshot.profile?.memberId) : null,
      refreshAccessToken: this.ensureAccessToken,
    });
    void this.restore();
  };
  stop = () => { this.cancelled = true; this.ports.offlineQueue.configureOfflineQueueAuth(null); };
  onAppActive = () => {
    if (this.lockedPending && !this.cancelled) void this.restore();
    this.retryPendingWrite();
    for (const job of this.cleanupJobs) this.runCleanup(job);
    if (this.snapshot.refreshToken) this.flushIfSignedIn();
    if (this.snapshot.accessToken) void this.ports.push.refreshPushRegistration(this.snapshot.accessToken, this.snapshot.profile?.memberId ?? null);
  };
  onNetworkOnline = () => this.flushIfSignedIn();
  onPushTokenChanged = () => {
    if (this.snapshot.accessToken) void this.ports.push.syncPushRegistration(this.snapshot.accessToken, this.snapshot.profile?.memberId ?? null);
  };
  refresh = (): Promise<string | null> => this.refreshWithStoredGrant();
  // Native writes cannot be cancelled. Deletion follows an in-flight write and
  // precedes any newer grant, even when the earlier operation fails.
  private serializeCredentials = <T,>(work: () => Promise<T>): Promise<T> => {
    const next = this.credentialWork.then(work, work);
    this.credentialWork = next.catch(() => { });
    return next;
  };

  private cleanupIsSafe = (job: CleanupJob): boolean => {
    if (job.operationScoped || job.generation === this.generation) {
      return true;
    }
    if (job.credentials || !job.memberId) {
      return false;
    }
    return this.snapshot.memberId !== job.memberId &&
      (!this.currentSession.accessToken || this.snapshot.memberId !== null);
  };

  private runCleanup = (job: CleanupJob) => {
    if (job.running || !this.cleanupIsSafe(job)) {
      return;
    }
    job.running = true;
    // Invoke immediately: cache/download invalidation happens before their first
    // await, so a new session cannot start while old in-flight work is still live.
    const work = (async () => { await job.run(); })();
    void work.then(
      () => {
        job.running = false;
        this.cleanupJobs.delete(job);
      },
      () => {
        job.running = false;
        if (!job.reported) {
          job.reported = true;
          this.ports.onEvent({ type: 'cleanupFailed', hasCurrentSession: Boolean(this.snapshot.accessToken), retry: () => this.runCleanup(job) });
        }
      },
    );
  };

  private queueCleanup = (
    generation: number,
    memberId: string | null,
    credentials: boolean,
    run: (isCurrent: () => boolean) => Promise<void>,
    operationScoped = false,
  ) => {
    const job: CleanupJob = {
      generation, memberId, credentials, operationScoped, running: false, reported: false,
      run: () => run(() => this.cleanupIsSafe(job)),
    };
    this.cleanupJobs.add(job);
    this.runCleanup(job);
  };

  private cleanupPrivateData = (memberId: string | null, generation: number) => {
    this.queueCleanup(generation, memberId, false, () => this.ports.cacheAndDownloads.purgePrivateContentCache(memberId));
    this.queueCleanup(generation, memberId, false, (isCurrent) => this.ports.cacheAndDownloads.purgeAllDownloads(memberId, isCurrent, this.generation === generation));
  };

  private beginSessionGeneration = () => {
    this.generation += 1;
    this.ports.offlineQueue.invalidateOfflineQueueFlush();
    this.pendingSessionWrite = null;
    this.refreshInFlight = null;
    return this.generation;
  };

  private applyTokenState = (
    tokens: { accessToken: string; refreshToken: string; expiresAt: number },
    extras: Partial<Pick<Session, 'displayName' | 'profile'>> = {},
  ) => {
    this.update({ refreshToken: tokens.refreshToken, expiresAt: tokens.expiresAt });
    const current = this.currentSession;
    const next = sessionFromAccessToken(tokens.accessToken, {
      displayName: extras.displayName !== undefined ? extras.displayName : current.displayName,
      profile: extras.profile !== undefined ? extras.profile : current.profile,
    });
    this.setSession(next);
  };

  private applyProfile = (accessToken: string, profile: MemberProfile | null, generation: number) => {
    if (generation !== this.generation || this.currentSession.accessToken !== accessToken) {
      return;
    }
    if (!profile) {
      // Keep the current identity shell when /me is unavailable.
      return;
    }

    const previousId = this.snapshot.memberId;
    const nextId = profile.memberId;
    if (previousId && nextId && previousId !== nextId) {
      this.cleanupPrivateData(previousId, generation);
    }
    this.update({ memberId: nextId });
    void this.ports.cacheAndDownloads.reconcileDownloads(nextId).catch(() => {
      // Offline reconcile can retry on the next launch.
    });
    if (!this.releaseSmokeEmbedEnabled() && this.pendingSessionWrite?.tokens.accessToken !== accessToken) {
      void this.serializeCredentials(async () => {
        if (generation !== this.generation || this.pendingSessionWrite?.tokens.accessToken === accessToken) {
          return;
        }
        await this.ports.tokenStore.writeStoredIdentityShell({
          displayName: profile.displayName,
          memberId: profile.memberId,
          avatarPath: profile.avatarPath,
        });
      }).catch(() => {
        // Token grant is already stored. A shell write miss only delays initials until /me succeeds.
      });
    }
    const next = sessionFromAccessToken(accessToken, {
      displayName: profile.displayName,
      profile,
    });
    this.setSession(next);
  };

  private applyTokens = async (tokens: AuthTokens, generation: number, resetIdentity = false): Promise<MemberProfile | null> => {
    if (generation !== this.generation) {
      return null;
    }
    // A refresh may replace a fresh grant whose identity reset failed. Carry
    // that obligation until a credential write actually completes it.
    const resetStoredIdentity = resetIdentity || this.pendingSessionWrite?.resetIdentity === true;
    let stored: StoredSession;
    try {
      const written = await this.serializeCredentials<StoredSession | null>(() => {
        if (generation !== this.generation) return Promise.resolve(null);
        return resetStoredIdentity ? this.ports.tokenStore.writeStoredSession(tokens, true) : this.ports.tokenStore.writeStoredSession(tokens);
      });
      if (!written || generation !== this.generation) {
        return null;
      }
      stored = written;
      this.pendingSessionWrite = null;
    } catch {
      if (generation !== this.generation) {
        return null;
      }
      // Keep the new grant in memory whatever the write failure (locked
      // Keychain or otherwise) and persist it on the next foreground. The
      // server has already spent the previous refresh token, so dropping this
      // one would replay a dead grant on the next refresh.
      this.pendingSessionWrite = { tokens, resetIdentity: resetStoredIdentity };
      stored = {
        ...tokens,
        expiresAt: this.ports.clock.now() + Math.max(tokens.expiresIn - 30, 30) * 1000,
      };
    }
    if (resetIdentity) {
      const previousMember = this.snapshot.memberId;
      this.update({ memberId: resolvePushMemberId(tokens.accessToken) });
      if (previousMember) {
        this.cleanupPrivateData(previousMember, generation);
      }
    }
    this.applyTokenState(stored, resetIdentity ? { displayName: null, profile: null } : {});
    const profile = await this.ports.oauth.loadProfile(tokens.accessToken);
    this.applyProfile(tokens.accessToken, profile, generation);
    return generation === this.generation ? profile : null;
  };

  private clearLocal = () => {
    const token = this.currentSession.accessToken;
    const memberId = this.snapshot.memberId ??
      (token ? resolvePushMemberId(token, this.currentSession.profile?.memberId) : null);
    const generation = this.beginSessionGeneration();
    this.update({ memberId: null, refreshToken: null, expiresAt: 0 });
    const next = { ...signedOut, isRestoring: false };
    this.setSession(next);

    // Authentication reset and the credential-deletion attempt never await
    // ancillary storage or the network. Queue deletion before another sign-in.
    this.queueCleanup(generation, memberId, true, () => this.serializeCredentials(this.ports.tokenStore.clearStoredSession));
    this.cleanupPrivateData(memberId, generation);
    return Promise.resolve();
  };

  private refreshWithStoredGrant = (): Promise<string | null> => {
    if (this.refreshInFlight) {
      return this.refreshInFlight;
    }

    const refresh = this.snapshot.refreshToken;
    if (!refresh) {
      return Promise.resolve(this.currentSession.accessToken);
    }

    if (this.ports.isInBackground()) {
      // Deferred, not failed: the foreground listener refreshes on `active`.
      return Promise.resolve(null);
    }

    const generation = this.generation;
    const flight = (async () => {
      let tokens: AuthTokens;
      try {
        tokens = await this.ports.oauth.refreshAccessToken(this.ports.config.apiBaseUrl, refresh);
      } catch (err) {
        if (generation === this.generation && !isTransientRefreshFailure(err)) {
          await this.clearLocal();
        }
        return null;
      }

      try {
        await this.applyTokens(tokens, generation);
      } catch {
        // The refresh grant itself succeeded — the access token is good. A follow-up
        // `/me` hiccup (a transient 401, an outage, ...) shouldn't sign the member out;
        // it just means the profile stays stale until it can be fetched successfully.
      }
      return generation === this.generation ? tokens.accessToken : null;
    })();

    this.refreshInFlight = flight;
    void flight.finally(() => {
      if (this.refreshInFlight === flight) {
        this.refreshInFlight = null;
      }
    });
    return flight;
  };

  ensureAccessToken = (): Promise<string | null> => {
    const currentToken = this.currentSession.accessToken;
    if (currentToken && this.snapshot.expiresAt > this.ports.clock.now()) {
      return Promise.resolve(currentToken);
    }

    return this.refreshWithStoredGrant();
  };

  recoverRejectedAccessToken = (rejectedAccessToken: string): Promise<string | null> => {
    const current = this.currentSession.accessToken;
    if (current && current !== rejectedAccessToken) {
      return Promise.resolve(current);
    }
    if (!this.snapshot.refreshToken) {
      return Promise.resolve(null);
    }
    return this.refreshWithStoredGrant();
  };

  applySmokeSession = async (accessToken: string): Promise<boolean> => {
    if (!this.smokeAuthAllowed()) {
      return false;
    }

    const token = accessToken.trim();
    if (!token) {
      return false;
    }

    const generation = this.beginSessionGeneration();
    if (this.releaseSmokeEmbedEnabled()) {
      const previousMember = this.snapshot.memberId;
      this.update({ memberId: resolvePushMemberId(token) });
      if (previousMember) {
        this.cleanupPrivateData(previousMember, generation);
      }
      const expiresAt = this.ports.clock.now() + Math.max(smokeAuthExpiresInSeconds - 30, 30) * 1000;
      this.applyTokenState({
        accessToken: token,
        refreshToken: smokeAuthRefreshPlaceholder,
        expiresAt,
      }, { displayName: null, profile: null });
      const profile = await this.ports.oauth.loadProfile(token);
      this.applyProfile(token, profile, generation);
      return generation === this.generation;
    }

    await this.applyTokens({
      accessToken: token,
      refreshToken: smokeAuthRefreshPlaceholder,
      expiresIn: smokeAuthExpiresInSeconds,
    }, generation, true);
    return generation === this.generation;
  };

  refreshProfile = async () => {
    const generation = this.generation;
    const token = await this.ensureAccessToken();
    if (!token || generation !== this.generation) {
      return null;
    }

    try {
      const profile = await this.ports.oauth.loadProfile(token);
      this.applyProfile(token, profile, generation);
      return generation === this.generation ? profile : null;
    } catch (err) {
      if (generation !== this.generation || this.currentSession.accessToken !== token) {
        return null;
      }
      if (!(err instanceof ApiError) || err.status !== 401) {
        return this.currentSession.profile;
      }

      if (!this.snapshot.refreshToken) {
        await this.clearLocal();
        return null;
      }

      // Must go through the single-flight guard: calling refreshAccessToken
      // directly here could present the same rotating grant as a concurrent
      // ensureAccessToken, and the server treats a reused refresh token as theft
      // and revokes every grant the member has. refreshWithStoredGrant already
      // reloads the profile on success and clears local state on a dead grant.
      const next = await this.refreshWithStoredGrant();
      if (!next && !this.currentSession.accessToken) {
        return null;
      }

      return this.currentSession.profile;
    }
  };

  signIn = async (provider: string) => {
    const generation = this.beginSessionGeneration();
    const tokens = await this.ports.oauth.signInWithProvider(this.ports.config.apiBaseUrl, provider);
    await this.applyTokens(tokens, generation, true);
  };

  signInWithPassword = async (email: string, password: string) => {
    const generation = this.beginSessionGeneration();
    const tokens = await this.ports.oauth.signInWithPassword(this.ports.config.apiBaseUrl, email, password);
    await this.applyTokens(tokens, generation, true);
  };

  signOut = async () => {
    const generation = this.generation;
    const token = this.currentSession.accessToken;
    const signedOutMemberId = this.snapshot.memberId ??
      (token ? resolvePushMemberId(token, this.currentSession.profile?.memberId) : null);
    let discardPending = false;
    let inspectionFailed = false;
    try {
      discardPending = await this.withTimeout(
        this.ports.offlineQueue.countPendingOfflineItems(signedOutMemberId),
        pendingSendInspectionTimeoutMs,
        'pending-send-inspection-timeout',
      ) > 0;
    } catch {
      // Unknown is not an empty queue. Ask before discarding anything.
      inspectionFailed = true;
      discardPending = true;
    }
    if (generation !== this.generation) {
      return;
    }
    if (discardPending) {
      const confirmed = await new Promise<boolean>((resolve) => {
        this.ports.onEvent({ type: 'confirmSignOut', inspectionFailed, confirm: () => resolve(true), cancel: () => resolve(false) });
      });
      if (!confirmed || generation !== this.generation) {
        return;
      }
    }
    const refresh = this.snapshot.refreshToken;
    const apiBaseUrl = this.ports.config.apiBaseUrl;
    // Capture confirmed discard intent before changing sessions. Its retry only
    // targets pre-intent operations, including after a same-member sign-in.
    const discard = discardPending ? this.ports.offlineQueue.prepareOfflineQueueDiscard(signedOutMemberId) : null;
    void this.clearLocal();
    if (discard) {
      this.queueCleanup(this.generation, signedOutMemberId, false, discard, true);
    }
    this.startRemoteSignOut({ accessToken: token, refreshToken: refresh, apiBaseUrl, memberId: signedOutMemberId });
  };

  setAccessToken = (accessToken: string | null) => {
    this.beginSessionGeneration();
    const current = this.currentSession;
    const next = sessionFromAccessToken(accessToken, {
      isRestoring: current.isRestoring,
      displayName: current.displayName,
      profile: current.profile,
    });
    this.setSession(next);
  };

  private readCredentials = async (isCurrent: () => boolean) => {
    try {
      const stored =
        this.ports.config.appEnv === 'development'
          ? await this.withTimeout(
            this.serializeCredentials(this.ports.tokenStore.readStoredSession),
            developmentSessionRestoreTimeoutMs,
            sessionRestoreTimeoutLabel,
          )
          : await this.serializeCredentials(this.ports.tokenStore.readStoredSession);
      this.lockedPending = false;
      return stored;
    } catch (error) {
      if (!isCurrent()) {
        return null;
      }
      if (this.ports.tokenStore.isKeychainLockedError(error)) {
        // A locked read is not sign-out. Retry when the app becomes active.
        this.lockedPending = true;
        return null;
      }
      this.lockedPending = false;
      if (!isSessionRestoreTimeoutError(error)) {
        this.ports.logger.captureException(error, { tags: { operation: 'session.restore.credentials' } });
      }
      // Preserve the stored grant for a later launch, but always finish restoring.
      this.setSession({ ...signedOut, isRestoring: false });
      return null;
    }
  };

  private finishRestoreRefresh = async (pending: Promise<string | null>, isCurrent: () => boolean) => {
    const next = await pending;
    if (!next && isCurrent() && !this.currentSession.accessToken && !this.snapshot.memberId) {
      await this.clearLocal();
    }
  };

  private restoreProfile = async (stored: StoredSession, generation: number, isCurrent: () => boolean) => {
    try {
      const profile = await this.ports.oauth.loadProfile(stored.accessToken);
      if (isCurrent()) {
        this.applyProfile(stored.accessToken, profile, generation);
      }
    } catch (error) {
      if (!isCurrent()) {
        return;
      }
      if (!(error instanceof ApiError) || error.status !== 401) {
        return;
      }
      if (stored.expiresAt <= this.ports.clock.now()) {
        await this.clearLocal();
        return;
      }
      if (!this.ports.isInBackground()) {
        await this.finishRestoreRefresh(this.refreshWithStoredGrant(), isCurrent);
      }
    }
  };

  restore = async () => {
    // Release-embedded smoke builds inject a seeded session after launch;
    // restoring SecureStore here only introduces simulator flakiness.
    if (this.releaseSmokeEmbedEnabled()) {
      this.setSession(signedOut);
      return;
    }
    if (this.restoreInFlight || this.cancelled) {
      return;
    }
    this.restoreInFlight = true;
    const generation = this.generation;
    const isCurrent = () => !this.cancelled && generation === this.generation;
    try {
      const stored = await this.readCredentials(isCurrent);
      if (!isCurrent() || this.lockedPending) {
        return;
      }
      if (!stored) {
        this.setSession({ ...signedOut, isRestoring: false });
        return;
      }

      const shell = profileFromIdentityShell(stored.identity);
      if (stored.identity?.memberId) {
        this.update({ memberId: stored.identity.memberId });
        void this.ports.cacheAndDownloads.reconcileDownloads(stored.identity.memberId).catch(() => { });
      }

      // Start the single-flight refresh before exposing the shell so consumers
      // join it instead of presenting the rotating grant twice.
      this.update({ refreshToken: stored.refreshToken, expiresAt: stored.expiresAt });
      const expired = stored.expiresAt <= this.ports.clock.now();
      const deferRefresh = this.ports.isInBackground();
      const pendingRefresh = expired && !deferRefresh ? this.refreshWithStoredGrant() : null;
      this.applyTokenState(stored, {
        displayName: stored.identity?.displayName ?? null,
        profile: shell,
      });
      if (expired && deferRefresh) {
        return;
      }
      if (pendingRefresh) {
        await this.finishRestoreRefresh(pendingRefresh, isCurrent);
      } else {
        await this.restoreProfile(stored, generation, isCurrent);
      }
    } catch {
      if (isCurrent()) {
        await this.clearLocal();
      }
    } finally {
      this.restoreInFlight = false;
    }
  };

  private flushIfSignedIn = () => {
    if (!this.currentSession.accessToken) {
      return;
    }
    void this.ensureAccessToken().then((token) => {
      if (token) {
        void this.ports.offlineQueue.flushOfflineQueue();
      }
    });
  };

  private retryPendingWrite = () => {
    const pending = this.pendingSessionWrite;
    if (!pending) {
      return;
    }
    const generation = this.generation;
    void (async () => {
      try {
        await this.serializeCredentials(async () => {
          if (generation !== this.generation || this.pendingSessionWrite !== pending) return;
          if (pending.resetIdentity) {
            await this.ports.tokenStore.writeStoredSession(pending.tokens, true);
          } else {
            await this.ports.tokenStore.writeStoredSession(pending.tokens);
          }
        });
        if (this.pendingSessionWrite !== pending || generation !== this.generation) return;
        this.pendingSessionWrite = null;
        const current = this.currentSession;
        const profile = current.profile;
        if (current.accessToken !== pending.tokens.accessToken || !profile) return;
        await this.serializeCredentials(async () => {
          if (generation !== this.generation || this.currentSession.accessToken !== current.accessToken) return;
          await this.ports.tokenStore.writeStoredIdentityShell({
            displayName: profile.displayName,
            memberId: profile.memberId,
            avatarPath: profile.avatarPath,
          });
        });
      } catch {
        // Failed token writes remain pending; an identity-shell failure leaves
        // the already persisted grant intact. Foreground can retry token writes.
      }
    })();
  };

  private startRemoteSignOut = (input: {
    accessToken: string | null;
    refreshToken: string | null;
    apiBaseUrl: string;
    memberId: string | null;
  }): void => {
    const tasks: Promise<unknown>[] = [];
    if (input.accessToken) {
      tasks.push(this.ports.push.clearPushRegistration(input.accessToken, input.memberId));
      tasks.push(this.ports.oauth.logoutRemote(input.apiBaseUrl, input.accessToken));
    }
    if (input.refreshToken) {
      tasks.push(this.ports.oauth.revokeRefreshToken(input.apiBaseUrl, input.refreshToken));
    }
    if (tasks.length === 0) {
      return;
    }

    void Promise.allSettled(tasks);
  };
}

function profileFromIdentityShell(identity: StoredIdentityShell | null | undefined): MemberProfile | null {
  if (!identity) {
    return null;
  }

  return {
    memberId: identity.memberId,
    email: '',
    displayName: identity.displayName,
    createdAt: '',
    lastLoginAt: null,
    hasAvatar: Boolean(identity.avatarPath),
    avatarPath: identity.avatarPath ?? null,
    avatarThumbPath: null,
    messagePrivacy: 'members',
    themePreference: 'system',
    linkedProviders: [],
    legacyLink: { kind: 'none', match: null, claimableMatches: [], unavailableMatches: [] },
    scheduledDeletionAt: null,
    limits: fallbackProfileLimits,
    deletion: {
      confirmationPhrase: 'DELETE',
      confirmationHint: 'Type DELETE to delete the account.',
      requestedTitle: 'Account deletion requested',
      requestedMessage:
        'Your account has been disabled and your personal data is being removed.',
      whatHappens: [],
    },
  };
}
