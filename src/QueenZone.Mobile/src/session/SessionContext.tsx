import {
  createContext,
  memo,
  useCallback,
  useContext,
  useEffect,
  useMemo,
  useRef,
  useState,
  type ReactNode,
} from 'react';
import { Alert, AppState, Linking } from 'react-native';
import { addNetworkStateListener } from 'expo-network';
import * as Notifications from 'expo-notifications';
import { getAppConfig } from '../config/appConfig';
import { ApiError, configureAuthenticatedGetRecovery, fetchJson } from '../api/client';
import { fallbackProfileLimits, parseMemberProfile, type MemberProfile } from '../api/me';
import type { AuthTokens } from '../api/auth';
import { clearPushRegistration, refreshPushRegistration, syncPushRegistration } from '../notifications';
import {
  logoutRemote,
  refreshAccessToken,
  revokeRefreshToken,
  signInWithPassword as requestPasswordTokens,
  signInWithProvider,
} from './oauth';
import {
  isSmokeAuthEnabled,
  parseSmokeAuthAccessToken,
  smokeAuthExpiresInSeconds,
  smokeAuthRefreshPlaceholder,
} from './smokeAuth';
import { purgePrivateContentCache } from '../cache';
import { purgeAllDownloads, reconcileDownloads } from '../downloads/manager';
import { isTransientRefreshFailure } from './refreshFailure';
import { resolvePushMemberId } from '../notifications/pushMemberId';
import {
  configureOfflineQueueAuth,
  countPendingOfflineItems,
  prepareOfflineQueueDiscard,
  invalidateOfflineQueueFlush,
  flushOfflineQueue,
} from '../offlineQueue';
import {
  developmentSessionRestoreTimeoutMs,
  isSessionRestoreTimeoutError,
  sessionRestoreTimeoutLabel,
  withTimeout,
} from './restoreTimeout';
import {
  clearStoredSession,
  isKeychainLockedError,
  readStoredSession,
  writeStoredIdentityShell,
  writeStoredSession,
  type StoredIdentityShell,
  type StoredSession,
} from './tokenStore';

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

type SessionContextValue = Session & SessionActions;

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

const SessionStateContext = createContext<Session | undefined>(undefined);
const SessionActionsContext = createContext<SessionActions | undefined>(undefined);

const SessionProviderChildren = memo(function SessionProviderChildren({
  children,
}: {
  children: ReactNode;
}) {
  return children;
});

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

function smokeAuthAllowed(): boolean {
  const config = getAppConfig();
  return isSmokeAuthEnabled({
    dev: typeof __DEV__ !== 'undefined' ? __DEV__ : false,
    appEnv: config.appEnv,
    smokeEmbed: config.smokeEmbed,
  });
}

function releaseSmokeEmbedEnabled(): boolean {
  const config = getAppConfig();
  return config.appEnv === 'development' && config.smokeEmbed === true;
}

/**
 * iOS launches the whole app for a BGTask (the home widget refresh, #990) and
 * suspends it as soon as that task completes. A /token rotation started there
 * can land on the server while the app never receives or persists the new
 * grant; the next launch then replays the spent token outside the server's
 * reuse grace window, and reuse detection revokes every grant the member has.
 * Only rotate from the foreground. iOS reports `unknown` before the first
 * activation of a normal launch, so only a definite `background` defers.
 */
function appIsInBackground(): boolean {
  return AppState.currentState === 'background';
}

export function SessionProvider({ children }: { children: ReactNode }) {
  const [session, setSession] = useState<Session>({ ...signedOut, isRestoring: true });
  const [refreshToken, setRefreshToken] = useState<string | null>(null);
  const [expiresAt, setExpiresAt] = useState(0);
  const sessionRef = useRef(session);
  const refreshTokenRef = useRef(refreshToken);
  const expiresAtRef = useRef(expiresAt);
  const memberIdRef = useRef<string | null>(null);
  const refreshInFlightRef = useRef<Promise<string | null> | null>(null);
  const pendingSessionWriteRef = useRef<{ tokens: AuthTokens; resetIdentity: boolean } | null>(null);
  const generationRef = useRef(0);
  const credentialWorkRef = useRef<Promise<unknown>>(Promise.resolve());
  const cleanupJobsRef = useRef(new Set<CleanupJob>());

  // Serialize writes and deletion. A native write already in progress cannot be
  // cancelled; sign-out's deletion must run after it and before any newer grant.
  const serializeCredentials = useCallback(<T,>(work: () => Promise<T>): Promise<T> => {
    const next = credentialWorkRef.current.then(work, work);
    credentialWorkRef.current = next.catch(() => {});
    return next;
  }, []);

  const cleanupIsSafe = useCallback((job: CleanupJob): boolean => {
    if (job.operationScoped || job.generation === generationRef.current) {
      return true;
    }
    if (job.credentials || !job.memberId) {
      return false;
    }
    return memberIdRef.current !== job.memberId &&
      (!sessionRef.current.accessToken || memberIdRef.current !== null);
  }, []);

  const runCleanup = useCallback(function retry(job: CleanupJob) {
    if (job.running || !cleanupIsSafe(job)) {
      return;
    }
    job.running = true;
    // Invoke immediately: cache/download invalidation happens before their first
    // await, so a new session cannot start while old in-flight work is still live.
    let work: Promise<void>;
    try {
      work = job.run();
    } catch {
      work = Promise.reject(new Error('Local cleanup failed'));
    }
    void work.then(
      () => {
        job.running = false;
        cleanupJobsRef.current.delete(job);
      },
      () => {
        job.running = false;
        if (!job.reported) {
          job.reported = true;
          Alert.alert(
            'Sign-out cleanup incomplete',
            `${sessionRef.current.accessToken ? 'Your current session is unchanged.' : 'You are signed out in this app.'} Saved sign-in or offline data may remain on this device. Retry cleanup before closing the app or signing in again. A restart could restore a session or pending sends.`,
            [
              { text: 'OK' },
              { text: 'Retry cleanup', onPress: () => retry(job) },
            ],
          );
        }
      },
    );
  }, [cleanupIsSafe]);

  const queueCleanup = useCallback((
    generation: number,
    memberId: string | null,
    credentials: boolean,
    run: (isCurrent: () => boolean) => Promise<void>,
    operationScoped = false,
  ) => {
    const job: CleanupJob = {
      generation, memberId, credentials, operationScoped, running: false, reported: false,
      run: () => run(() => cleanupIsSafe(job)),
    };
    cleanupJobsRef.current.add(job);
    runCleanup(job);
  }, [cleanupIsSafe, runCleanup]);

  const cleanupPrivateData = useCallback((memberId: string | null, generation: number) => {
    queueCleanup(generation, memberId, false, () => purgePrivateContentCache(memberId));
    queueCleanup(generation, memberId, false, (isCurrent) => purgeAllDownloads(memberId, isCurrent, generationRef.current === generation));
  }, [queueCleanup]);

  const beginSessionGeneration = useCallback(() => {
    generationRef.current += 1;
    invalidateOfflineQueueFlush();
    pendingSessionWriteRef.current = null;
    refreshInFlightRef.current = null;
    return generationRef.current;
  }, []);
  sessionRef.current = session;
  refreshTokenRef.current = refreshToken;
  expiresAtRef.current = expiresAt;

  const applyTokenState = useCallback(
    (
      tokens: { accessToken: string; refreshToken: string; expiresAt: number },
      extras: Partial<Pick<Session, 'displayName' | 'profile'>> = {},
    ) => {
      refreshTokenRef.current = tokens.refreshToken;
      expiresAtRef.current = tokens.expiresAt;
      setRefreshToken(tokens.refreshToken);
      setExpiresAt(tokens.expiresAt);
      const current = sessionRef.current;
      const next = sessionFromAccessToken(tokens.accessToken, {
        displayName: extras.displayName !== undefined ? extras.displayName : current.displayName,
        profile: extras.profile !== undefined ? extras.profile : current.profile,
      });
      sessionRef.current = next;
      setSession(next);
    },
    [],
  );

  const applyProfile = useCallback((accessToken: string, profile: MemberProfile | null, generation: number) => {
    if (generation !== generationRef.current || sessionRef.current.accessToken !== accessToken) {
      return;
    }
    if (!profile) {
      // Keep the current identity shell when /me is unavailable.
      return;
    }

    const previousId = memberIdRef.current;
    const nextId = profile.memberId;
    if (previousId && nextId && previousId !== nextId) {
      cleanupPrivateData(previousId, generation);
    }
    memberIdRef.current = nextId;
    void reconcileDownloads(nextId).catch(() => {
      // Offline reconcile can retry on the next launch.
    });
    if (!releaseSmokeEmbedEnabled() && pendingSessionWriteRef.current?.tokens.accessToken !== accessToken) {
      void serializeCredentials(async () => {
        if (generation !== generationRef.current || pendingSessionWriteRef.current?.tokens.accessToken === accessToken) {
          return;
        }
        await writeStoredIdentityShell({
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
    sessionRef.current = next;
    setSession(next);
  }, [cleanupPrivateData, serializeCredentials]);

  const applyTokens = useCallback(
    async (tokens: AuthTokens, generation: number, resetIdentity = false): Promise<MemberProfile | null> => {
      if (generation !== generationRef.current) {
        return null;
      }
      // A refresh may replace a fresh grant whose identity reset failed. Carry
      // that obligation until a credential write actually completes it.
      const resetStoredIdentity = resetIdentity || pendingSessionWriteRef.current?.resetIdentity === true;
      let stored: StoredSession;
      try {
        const written = await serializeCredentials(async () =>
          generation === generationRef.current
            ? (resetStoredIdentity ? writeStoredSession(tokens, true) : writeStoredSession(tokens))
            : null,
        );
        if (!written || generation !== generationRef.current) {
          return null;
        }
        stored = written;
        pendingSessionWriteRef.current = null;
      } catch {
        if (generation !== generationRef.current) {
          return null;
        }
        // Keep the new grant in memory whatever the write failure (locked
        // Keychain or otherwise) and persist it on the next foreground. The
        // server has already spent the previous refresh token, so dropping this
        // one would replay a dead grant on the next refresh.
        pendingSessionWriteRef.current = { tokens, resetIdentity: resetStoredIdentity };
        stored = {
          ...tokens,
          expiresAt: Date.now() + Math.max(tokens.expiresIn - 30, 30) * 1000,
        };
      }
      if (resetIdentity) {
        const previousMember = memberIdRef.current;
        memberIdRef.current = resolvePushMemberId(tokens.accessToken);
        if (previousMember) {
          cleanupPrivateData(previousMember, generation);
        }
      }
      applyTokenState(stored, resetIdentity ? { displayName: null, profile: null } : {});
      const profile = await loadProfile(tokens.accessToken);
      applyProfile(tokens.accessToken, profile, generation);
      return generation === generationRef.current ? profile : null;
    },
    [applyProfile, applyTokenState, cleanupPrivateData, serializeCredentials],
  );

  const clearLocal = useCallback(async () => {
    const token = sessionRef.current.accessToken;
    const memberId = memberIdRef.current ??
      (token ? resolvePushMemberId(token, sessionRef.current.profile?.memberId) : null);
    const generation = beginSessionGeneration();
    memberIdRef.current = null;
    refreshTokenRef.current = null;
    expiresAtRef.current = 0;
    const next = { ...signedOut, isRestoring: false };
    sessionRef.current = next;
    setRefreshToken(null);
    setExpiresAt(0);
    setSession(next);

    // Authentication reset and the credential-deletion attempt never await
    // ancillary storage or the network. Queue deletion before another sign-in.
    queueCleanup(generation, memberId, true, () => serializeCredentials(clearStoredSession));
    cleanupPrivateData(memberId, generation);
  }, [beginSessionGeneration, cleanupPrivateData, queueCleanup, serializeCredentials]);

  const refreshWithStoredGrant = useCallback((): Promise<string | null> => {
    if (refreshInFlightRef.current) {
      return refreshInFlightRef.current;
    }

    const refresh = refreshTokenRef.current;
    if (!refresh) {
      return Promise.resolve(sessionRef.current.accessToken);
    }

    if (appIsInBackground()) {
      // Deferred, not failed: the foreground listener refreshes on `active`.
      return Promise.resolve(null);
    }

    const generation = generationRef.current;
    const flight = (async () => {
      let tokens: AuthTokens;
      try {
        tokens = await refreshAccessToken(getAppConfig().apiBaseUrl, refresh);
      } catch (err) {
        if (generation === generationRef.current && !isTransientRefreshFailure(err)) {
          await clearLocal();
        }
        return null;
      }

      try {
        await applyTokens(tokens, generation);
      } catch {
        // The refresh grant itself succeeded — the access token is good. A follow-up
        // `/me` hiccup (a transient 401, an outage, ...) shouldn't sign the member out;
        // it just means the profile stays stale until it can be fetched successfully.
      }
      return generation === generationRef.current ? tokens.accessToken : null;
    })();

    refreshInFlightRef.current = flight;
    void flight.finally(() => {
      if (refreshInFlightRef.current === flight) {
        refreshInFlightRef.current = null;
      }
    });
    return flight;
  }, [applyTokens, clearLocal]);

  const ensureAccessToken = useCallback(async (): Promise<string | null> => {
    const currentToken = sessionRef.current.accessToken;
    if (currentToken && expiresAtRef.current > Date.now()) {
      return currentToken;
    }

    return refreshWithStoredGrant();
  }, [refreshWithStoredGrant]);

  const recoverRejectedAccessToken = useCallback(
    async (rejectedAccessToken: string): Promise<string | null> => {
      const current = sessionRef.current.accessToken;
      if (current && current !== rejectedAccessToken) {
        return current;
      }
      if (!refreshTokenRef.current) {
        return null;
      }
      return refreshWithStoredGrant();
    },
    [refreshWithStoredGrant],
  );

  useEffect(() => {
    configureAuthenticatedGetRecovery(recoverRejectedAccessToken);
    return () => configureAuthenticatedGetRecovery(null);
  }, [recoverRejectedAccessToken]);

  useEffect(() => {
    // A Release-embedded smoke binary is a fresh, isolated Testing harness.
    // Maestro clears its state/keychain before launch and injects the seeded
    // session later, so a SecureStore restore can only add simulator flakiness.
    if (releaseSmokeEmbedEnabled()) {
      const next = { ...signedOut, isRestoring: false };
      sessionRef.current = next;
      setSession(next);
      return;
    }

    let cancelled = false;
    let inFlight = false;
    let lockedPending = false;

    const restore = async () => {
      if (inFlight || cancelled) {
        return;
      }
      inFlight = true;
      const generation = generationRef.current;
      const isCurrent = () => !cancelled && generation === generationRef.current;
      try {
        let stored: StoredSession | null;
        try {
          stored =
            getAppConfig().appEnv === 'development'
              ? await withTimeout(
                  serializeCredentials(readStoredSession),
                  developmentSessionRestoreTimeoutMs,
                  sessionRestoreTimeoutLabel,
                )
              : await serializeCredentials(readStoredSession);
        } catch (error) {
          if (!isCurrent()) {
            return;
          }
          if (isSessionRestoreTimeoutError(error)) {
            // Simulator SecureStore can hang instead of resolving. Fail open so
            // Profile is not stuck on "Restoring your session…" (#1387).
            lockedPending = false;
            setSession({ ...signedOut, isRestoring: false });
            return;
          }
          if (isKeychainLockedError(error)) {
            // Keep isRestoring. A locked read is not sign-out and must not unhandled-reject.
            lockedPending = true;
            return;
          }
          throw error;
        }
        lockedPending = false;
        if (!isCurrent()) {
          return;
        }

        if (!stored) {
          setSession({ ...signedOut, isRestoring: false });
          return;
        }

        const shell = profileFromIdentityShell(stored.identity);
        if (stored.identity?.memberId) {
          memberIdRef.current = stored.identity.memberId;
          void reconcileDownloads(stored.identity.memberId).catch(() => {});
        }

        // Seed the grant and start a single-flight /token before the signed-in
        // shell is live so flushOfflineQueue / refreshProfile join this promise
        // instead of presenting the same single-use refresh token twice.
        refreshTokenRef.current = stored.refreshToken;
        expiresAtRef.current = stored.expiresAt;
        const expired = stored.expiresAt <= Date.now();
        const deferRefresh = appIsInBackground();
        const pendingRefresh = expired && !deferRefresh ? refreshWithStoredGrant() : null;

        applyTokenState(stored, {
          displayName: stored.identity?.displayName ?? null,
          profile: shell,
        });

        if (expired && deferRefresh) {
          // Background launch: keep the stored grant untouched. The foreground
          // listener refreshes it (and loads /me) when the member opens the app.
          return;
        }

        try {
          if (pendingRefresh) {
            const next = await pendingRefresh;
            if (!next && isCurrent() && !sessionRef.current.accessToken && !memberIdRef.current) {
              await clearLocal();
            }
            return;
          }

          try {
            const profile = await loadProfile(stored.accessToken);
            if (isCurrent()) {
              applyProfile(stored.accessToken, profile, generation);
            }
          } catch (err) {
            if (!isCurrent()) {
              return;
            }

            const canRetryRefresh =
              err instanceof ApiError && err.status === 401 && stored.expiresAt > Date.now();
            if (canRetryRefresh && appIsInBackground()) {
              return;
            }
            if (canRetryRefresh) {
              const next = await refreshWithStoredGrant();
              if (!next && isCurrent() && !sessionRef.current.accessToken && !memberIdRef.current) {
                await clearLocal();
              }
              return;
            }

            if (err instanceof ApiError && err.status === 401) {
              await clearLocal();
            }
          }
        } catch {
          if (isCurrent()) {
            await clearLocal();
          }
        }
      } finally {
        inFlight = false;
      }
    };

    void restore();

    const appState = AppState.addEventListener('change', (state) => {
      if (state === 'active' && lockedPending && !cancelled) {
        void restore();
      }
    });

    return () => {
      cancelled = true;
      appState.remove();
    };
  }, [applyProfile, applyTokenState, clearLocal, refreshWithStoredGrant, serializeCredentials]);

  useEffect(() => {
    configureOfflineQueueAuth({
      getAccessToken: () => sessionRef.current.accessToken,
      getMemberId: () =>
        sessionRef.current.accessToken
          ? resolvePushMemberId(sessionRef.current.accessToken, sessionRef.current.profile?.memberId)
          : null,
      refreshAccessToken: ensureAccessToken,
    });
    return () => configureOfflineQueueAuth(null);
  }, [ensureAccessToken]);

  useEffect(() => {
    const flushIfSignedIn = () => {
      if (!sessionRef.current.accessToken) {
        return;
      }
      void ensureAccessToken().then((token) => {
        if (token) {
          void flushOfflineQueue();
        }
      });
    };

    const retryPendingWrite = () => {
      const pending = pendingSessionWriteRef.current;
      if (!pending) {
        return;
      }
      const generation = generationRef.current;
      void serializeCredentials(async () => {
        if (generation === generationRef.current && pendingSessionWriteRef.current === pending) {
          if (pending.resetIdentity) {
            await writeStoredSession(pending.tokens, true);
          } else {
            await writeStoredSession(pending.tokens);
          }
        }
      }).then(() => {
          if (pendingSessionWriteRef.current === pending && generation === generationRef.current) {
            pendingSessionWriteRef.current = null;
            const current = sessionRef.current;
            const profile = current.profile;
            if (current.accessToken === pending.tokens.accessToken && profile) {
              void serializeCredentials(async () => {
                if (generation !== generationRef.current || sessionRef.current.accessToken !== current.accessToken) {
                  return;
                }
                await writeStoredIdentityShell({
                  displayName: profile.displayName,
                  memberId: profile.memberId,
                  avatarPath: profile.avatarPath,
                });
              }).catch(() => {});
            }
          }
        })
        .catch(() => {
          // Still pending; the next foreground or refresh tries again.
        });
    };

    const appState = AppState.addEventListener('change', (state) => {
      if (state === 'active') {
        retryPendingWrite();
        for (const job of cleanupJobsRef.current) {
          runCleanup(job);
        }
        if (refreshTokenRef.current) {
          flushIfSignedIn();
        }
      }
    });
    const network = addNetworkStateListener((state) => {
      if (state.isInternetReachable === true || state.isConnected === true) {
        flushIfSignedIn();
      }
    });
    return () => {
      appState.remove();
      network.remove();
    };
  }, [ensureAccessToken, runCleanup, serializeCredentials]);

  const applySmokeSession = useCallback(
    async (accessToken: string): Promise<boolean> => {
      if (!smokeAuthAllowed()) {
        return false;
      }

      const token = accessToken.trim();
      if (!token) {
        return false;
      }

      const generation = beginSessionGeneration();
      if (releaseSmokeEmbedEnabled()) {
        const previousMember = memberIdRef.current;
        memberIdRef.current = resolvePushMemberId(token);
        if (previousMember) {
          cleanupPrivateData(previousMember, generation);
        }
        const expiresAt = Date.now() + Math.max(smokeAuthExpiresInSeconds - 30, 30) * 1000;
        applyTokenState({
          accessToken: token,
          refreshToken: smokeAuthRefreshPlaceholder,
          expiresAt,
        }, { displayName: null, profile: null });
        const profile = await loadProfile(token);
        applyProfile(token, profile, generation);
        return generation === generationRef.current;
      }

      await applyTokens({
        accessToken: token,
        refreshToken: smokeAuthRefreshPlaceholder,
        expiresIn: smokeAuthExpiresInSeconds,
      }, generation, true);
      return generation === generationRef.current;
    },
    [applyProfile, applyTokenState, applyTokens, beginSessionGeneration, cleanupPrivateData],
  );

  useEffect(() => {
    if (!smokeAuthAllowed()) {
      return;
    }

    const handleUrl = (url: string | null) => {
      if (!url) {
        return;
      }
      const token = parseSmokeAuthAccessToken(url);
      if (token) {
        void applySmokeSession(token);
      }
    };

    const subscription = Linking.addEventListener('url', ({ url }) => handleUrl(url));
    void Linking.getInitialURL().then(handleUrl);
    return () => subscription.remove();
  }, [applySmokeSession]);

  const isSignedIn = session.isSignedIn;
  const accessToken = session.accessToken;
  const memberId = session.profile?.memberId ?? null;
  const isRestoring = session.isRestoring;

  useEffect(() => {
    if (!isRestoring && isSignedIn && accessToken) {
      void flushOfflineQueue();
    }
  }, [isRestoring, isSignedIn, accessToken]);

  // #850: request push permission and register the device once signed in
  // (not before) — never on cold start. Best-effort throughout; see
  // notifications/pushRegistration.ts. Pass memberId so a same-device
  // account switch re-registers (#1094).
  useEffect(() => {
    if (!isSignedIn || !accessToken) {
      return;
    }

    void syncPushRegistration(accessToken, memberId);

    const tokenSubscription = Notifications.addPushTokenListener(() => {
      void syncPushRegistration(accessToken, memberId);
    });

    const appStateSubscription = AppState.addEventListener('change', (state) => {
      if (state === 'active') {
        void refreshPushRegistration(accessToken, memberId);
      }
    });

    return () => {
      tokenSubscription.remove();
      appStateSubscription.remove();
    };
  }, [isSignedIn, accessToken, memberId]);

  const refreshProfile = useCallback(async () => {
    const generation = generationRef.current;
    const token = await ensureAccessToken();
    if (!token || generation !== generationRef.current) {
      return null;
    }

    try {
      const profile = await loadProfile(token);
      applyProfile(token, profile, generation);
      return generation === generationRef.current ? profile : null;
    } catch (err) {
      if (generation !== generationRef.current || sessionRef.current.accessToken !== token) {
        return null;
      }
      if (!(err instanceof ApiError) || err.status !== 401) {
        return sessionRef.current.profile;
      }

      if (!refreshTokenRef.current) {
        await clearLocal();
        return null;
      }

      // Must go through the single-flight guard: calling refreshAccessToken
      // directly here could present the same rotating grant as a concurrent
      // ensureAccessToken, and the server treats a reused refresh token as theft
      // and revokes every grant the member has. refreshWithStoredGrant already
      // reloads the profile on success and clears local state on a dead grant.
      const next = await refreshWithStoredGrant();
      if (!next && !sessionRef.current.accessToken) {
        return null;
      }

      return sessionRef.current.profile;
    }
  }, [applyProfile, clearLocal, ensureAccessToken, refreshWithStoredGrant]);

  const signIn = useCallback(
    async (provider: string) => {
      const generation = beginSessionGeneration();
      const tokens = await signInWithProvider(getAppConfig().apiBaseUrl, provider);
      await applyTokens(tokens, generation, true);
    },
    [applyTokens, beginSessionGeneration],
  );

  const signInWithPassword = useCallback(
    async (email: string, password: string) => {
      const generation = beginSessionGeneration();
      const tokens = await requestPasswordTokens(getAppConfig().apiBaseUrl, email, password);
      await applyTokens(tokens, generation, true);
    },
    [applyTokens, beginSessionGeneration],
  );

  const signOut = useCallback(async () => {
    const generation = generationRef.current;
    const token = sessionRef.current.accessToken;
    const signedOutMemberId = memberIdRef.current ??
      (token ? resolvePushMemberId(token, sessionRef.current.profile?.memberId) : null);
    let discardPending = false;
    let inspectionFailed = false;
    try {
      discardPending = await withTimeout(
        countPendingOfflineItems(signedOutMemberId),
        pendingSendInspectionTimeoutMs,
        'pending-send-inspection-timeout',
      ) > 0;
    } catch {
      // Unknown is not an empty queue. Ask before discarding anything.
      inspectionFailed = true;
      discardPending = true;
    }
    if (generation !== generationRef.current) {
      return;
    }
    if (discardPending) {
      const confirmed = await new Promise<boolean>((resolve) => {
        Alert.alert(
          inspectionFailed ? 'Unable to check pending sends' : 'Discard pending sends?',
          inspectionFailed
            ? 'Pending messages and replies could not be checked. Sign out and discard any pending sends when storage is available?'
            : 'Messages and replies waiting to send will be deleted.',
          [
            { text: 'Cancel', style: 'cancel', onPress: () => resolve(false) },
            { text: 'Sign out', style: 'destructive', onPress: () => resolve(true) },
          ],
          { cancelable: true, onDismiss: () => resolve(false) },
        );
      });
      if (!confirmed || generation !== generationRef.current) {
        return;
      }
    }
    const refresh = refreshTokenRef.current;
    const apiBaseUrl = getAppConfig().apiBaseUrl;
    // Capture confirmed discard intent before changing sessions. Its retry only
    // targets pre-intent operations, including after a same-member sign-in.
    const discard = discardPending ? prepareOfflineQueueDiscard(signedOutMemberId) : null;
    void clearLocal();
    if (discard) {
      queueCleanup(generationRef.current, signedOutMemberId, false, discard, true);
    }
    startRemoteSignOut({ accessToken: token, refreshToken: refresh, apiBaseUrl, memberId: signedOutMemberId });
  }, [clearLocal, queueCleanup]);

  const setAccessToken = useCallback((accessToken: string | null) => {
    beginSessionGeneration();
    const current = sessionRef.current;
    const next = sessionFromAccessToken(accessToken, {
      isRestoring: current.isRestoring,
      displayName: current.displayName,
      profile: current.profile,
    });
    sessionRef.current = next;
    setSession(next);
  }, [beginSessionGeneration]);

  const actions = useMemo<SessionActions>(
    () => ({
      signIn,
      signInWithPassword,
      applySmokeSession,
      signOut,
      refreshProfile,
      ensureAccessToken,
      setAccessToken,
    }),
    [applySmokeSession, ensureAccessToken, refreshProfile, setAccessToken, signIn, signInWithPassword, signOut],
  );

  return (
    <SessionActionsContext.Provider value={actions}>
      <SessionStateContext.Provider value={session}>
        <SessionProviderChildren>{children}</SessionProviderChildren>
      </SessionStateContext.Provider>
    </SessionActionsContext.Provider>
  );
}

export function useSession(): SessionContextValue {
  const session = useContext(SessionStateContext);
  const actions = useContext(SessionActionsContext);
  if (!session || !actions) {
    throw new Error('useSession must be used inside SessionProvider');
  }

  return { ...session, ...actions };
}

export function useSessionActions(): SessionActions {
  const actions = useContext(SessionActionsContext);
  if (!actions) {
    throw new Error('useSessionActions must be used inside SessionProvider');
  }

  return actions;
}

function startRemoteSignOut(input: {
  accessToken: string | null;
  refreshToken: string | null;
  apiBaseUrl: string;
  memberId: string | null;
}): void {
  const tasks: Promise<unknown>[] = [];
  if (input.accessToken) {
    tasks.push(clearPushRegistration(input.accessToken, input.memberId));
    tasks.push(logoutRemote(input.apiBaseUrl, input.accessToken));
  }
  if (input.refreshToken) {
    tasks.push(revokeRefreshToken(input.apiBaseUrl, input.refreshToken));
  }
  if (tasks.length === 0) {
    return;
  }

  void Promise.allSettled(tasks);
}

async function loadProfile(accessToken: string): Promise<MemberProfile | null> {
  try {
    return parseMemberProfile(await fetchJson('/me', { accessToken }));
  } catch (err) {
    if (err instanceof ApiError && err.status === 401) {
      throw err;
    }

    return null;
  }
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
