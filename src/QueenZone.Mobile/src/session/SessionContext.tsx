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
  discardOfflineQueue,
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

export function SessionProvider({ children }: { children: ReactNode }) {
  const [session, setSession] = useState<Session>({ ...signedOut, isRestoring: true });
  const [refreshToken, setRefreshToken] = useState<string | null>(null);
  const [expiresAt, setExpiresAt] = useState(0);
  const sessionRef = useRef(session);
  const refreshTokenRef = useRef(refreshToken);
  const expiresAtRef = useRef(expiresAt);
  const memberIdRef = useRef<string | null>(null);
  const refreshInFlightRef = useRef<Promise<string | null> | null>(null);
  const pendingSessionWriteRef = useRef<AuthTokens | null>(null);
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
      setSession((current) => {
        const next = sessionFromAccessToken(tokens.accessToken, {
          displayName: extras.displayName !== undefined ? extras.displayName : current.displayName,
          profile: extras.profile !== undefined ? extras.profile : current.profile,
        });
        sessionRef.current = next;
        return next;
      });
    },
    [],
  );

  const applyProfile = useCallback((accessToken: string, profile: MemberProfile | null) => {
    if (!profile) {
      setSession((current) => {
        const next = sessionFromAccessToken(accessToken, {
          displayName: current.displayName,
          profile: current.profile,
        });
        sessionRef.current = next;
        return next;
      });
      return;
    }

    const previousId = memberIdRef.current;
    const nextId = profile.memberId;
    if (previousId && nextId && previousId !== nextId) {
      void purgePrivateContentCache(previousId);
      void purgeAllDownloads(previousId);
    }
    memberIdRef.current = nextId;
    void reconcileDownloads(nextId).catch(() => {
      // Offline reconcile can retry on the next launch.
    });
    if (!releaseSmokeEmbedEnabled()) {
      void writeStoredIdentityShell({
        displayName: profile.displayName,
        memberId: profile.memberId,
        avatarPath: profile.avatarPath,
      }).catch(() => {
        // Token grant is already stored. A shell write miss only delays initials until /me succeeds.
      });
    }
    setSession(() => {
      const next = sessionFromAccessToken(accessToken, {
        displayName: profile.displayName,
        profile,
      });
      sessionRef.current = next;
      return next;
    });
  }, []);

  const applyTokens = useCallback(
    async (tokens: AuthTokens): Promise<MemberProfile | null> => {
      let stored: StoredSession;
      try {
        stored = await writeStoredSession(tokens);
        pendingSessionWriteRef.current = null;
      } catch (error) {
        if (!isKeychainLockedError(error)) {
          throw error;
        }
        // Persist later — a locked Keychain must not unhandled-reject a background refresh.
        pendingSessionWriteRef.current = tokens;
        stored = {
          ...tokens,
          expiresAt: Date.now() + Math.max(tokens.expiresIn - 30, 30) * 1000,
        };
      }
      applyTokenState(stored);
      const profile = await loadProfile(tokens.accessToken);
      applyProfile(tokens.accessToken, profile);
      return profile;
    },
    [applyProfile, applyTokenState],
  );

  const clearLocal = useCallback(async () => {
    await purgeAllDownloads(memberIdRef.current);
    memberIdRef.current = null;
    await purgePrivateContentCache();
    try {
      await clearStoredSession();
    } catch {
      // In-memory sign-out still has to happen if SecureStore delete fails.
    }
    refreshTokenRef.current = null;
    expiresAtRef.current = 0;
    const next = { ...signedOut, isRestoring: false };
    sessionRef.current = next;
    setRefreshToken(null);
    setExpiresAt(0);
    setSession(next);
  }, []);

  const refreshWithStoredGrant = useCallback((): Promise<string | null> => {
    if (refreshInFlightRef.current) {
      return refreshInFlightRef.current;
    }

    const refresh = refreshTokenRef.current;
    if (!refresh) {
      return Promise.resolve(sessionRef.current.accessToken);
    }

    const flight = (async () => {
      let tokens: AuthTokens;
      try {
        tokens = await refreshAccessToken(getAppConfig().apiBaseUrl, refresh);
      } catch (err) {
        if (!isTransientRefreshFailure(err)) {
          await clearLocal();
        }
        return null;
      }

      try {
        await applyTokens(tokens);
      } catch {
        // The refresh grant itself succeeded — the access token is good. A follow-up
        // `/me` hiccup (a transient 401, an outage, ...) shouldn't sign the member out;
        // it just means the profile stays stale until it can be fetched successfully.
      }
      return tokens.accessToken;
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
      try {
        let stored: StoredSession | null;
        try {
          stored =
            getAppConfig().appEnv === 'development'
              ? await withTimeout(
                  readStoredSession(),
                  developmentSessionRestoreTimeoutMs,
                  sessionRestoreTimeoutLabel,
                )
              : await readStoredSession();
        } catch (error) {
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
        if (cancelled) {
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
        const pendingRefresh = stored.expiresAt <= Date.now() ? refreshWithStoredGrant() : null;

        applyTokenState(stored, {
          displayName: stored.identity?.displayName ?? null,
          profile: shell,
        });

        try {
          if (pendingRefresh) {
            const next = await pendingRefresh;
            if (!next && !cancelled && !sessionRef.current.accessToken && !memberIdRef.current) {
              await clearLocal();
            }
            return;
          }

          try {
            const profile = await loadProfile(stored.accessToken);
            if (!cancelled) {
              applyProfile(stored.accessToken, profile);
            }
          } catch (err) {
            if (cancelled) {
              return;
            }

            const canRetryRefresh =
              err instanceof ApiError && err.status === 401 && stored.expiresAt > Date.now();
            if (canRetryRefresh) {
              const next = await refreshWithStoredGrant();
              if (!next && !cancelled && !sessionRef.current.accessToken && !memberIdRef.current) {
                await clearLocal();
              }
              return;
            }

            if (err instanceof ApiError && err.status === 401) {
              await clearLocal();
            }
          }
        } catch {
          if (!cancelled) {
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
  }, [applyProfile, applyTokenState, clearLocal, refreshWithStoredGrant]);

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

    const retryLockedWrite = () => {
      const pending = pendingSessionWriteRef.current;
      if (!pending) {
        return;
      }
      void writeStoredSession(pending)
        .then(() => {
          if (pendingSessionWriteRef.current === pending) {
            pendingSessionWriteRef.current = null;
          }
        })
        .catch((error) => {
          if (!isKeychainLockedError(error)) {
            throw error;
          }
        });
    };

    const appState = AppState.addEventListener('change', (state) => {
      if (state === 'active') {
        retryLockedWrite();
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
  }, [ensureAccessToken]);

  const applySmokeSession = useCallback(
    async (accessToken: string): Promise<boolean> => {
      if (!smokeAuthAllowed()) {
        return false;
      }

      const token = accessToken.trim();
      if (!token) {
        return false;
      }

      if (releaseSmokeEmbedEnabled()) {
        const expiresAt = Date.now() + Math.max(smokeAuthExpiresInSeconds - 30, 30) * 1000;
        applyTokenState({
          accessToken: token,
          refreshToken: smokeAuthRefreshPlaceholder,
          expiresAt,
        });
        const profile = await loadProfile(token);
        applyProfile(token, profile);
        return true;
      }

      await applyTokens({
        accessToken: token,
        refreshToken: smokeAuthRefreshPlaceholder,
        expiresIn: smokeAuthExpiresInSeconds,
      });
      return true;
    },
    [applyProfile, applyTokenState, applyTokens],
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
    const token = await ensureAccessToken();
    if (!token) {
      return null;
    }

    try {
      const profile = await loadProfile(token);
      applyProfile(token, profile);
      return profile;
    } catch (err) {
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
      const tokens = await signInWithProvider(getAppConfig().apiBaseUrl, provider);
      await applyTokens(tokens);
    },
    [applyTokens],
  );

  const signInWithPassword = useCallback(
    async (email: string, password: string) => {
      const tokens = await requestPasswordTokens(getAppConfig().apiBaseUrl, email, password);
      await applyTokens(tokens);
    },
    [applyTokens],
  );

  const signOut = useCallback(async () => {
    // Current tokens/profile are read via sessionRef / refreshTokenRef so this
    // callback identity stays stable across token refresh and /me.
    const token = sessionRef.current.accessToken;
    const signedOutMemberId =
      (token ? resolvePushMemberId(token, sessionRef.current.profile?.memberId) : null) ??
      sessionRef.current.profile?.memberId ??
      null;
    const pending = await countPendingOfflineItems(signedOutMemberId);
    if (pending > 0) {
      const confirmed = await new Promise<boolean>((resolve) => {
        Alert.alert(
          'Discard pending sends?',
          'Messages and replies waiting to send will be deleted.',
          [
            { text: 'Cancel', style: 'cancel', onPress: () => resolve(false) },
            { text: 'Sign out', style: 'destructive', onPress: () => resolve(true) },
          ],
        );
      });
      if (!confirmed) {
        return;
      }
      await discardOfflineQueue(signedOutMemberId);
    }
    const refresh = refreshTokenRef.current;
    const apiBaseUrl = getAppConfig().apiBaseUrl;
    // Clear the device session first. Remote logout/revoke/push unregister
    // can hang (React Native fetch often ignores AbortSignal) or crash the
    // process; awaiting them first left the member signed in after a kill.
    await clearLocal();
    startRemoteSignOut({
      accessToken: token,
      refreshToken: refresh,
      apiBaseUrl,
      memberId: signedOutMemberId,
    });
  }, [clearLocal]);

  const setAccessToken = useCallback((accessToken: string | null) => {
    setSession((current) =>
      sessionFromAccessToken(accessToken, {
        isRestoring: current.isRestoring,
        displayName: current.displayName,
        profile: current.profile,
      }),
    );
  }, []);

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
      confirmationHint: 'Type DELETE to schedule deletion of the account.',
      requestedTitle: 'Account deletion scheduled',
      requestedMessage:
        'You have been signed out. You can sign back in and cancel deletion during the 30-day cooling-off period.',
      whatHappens: [],
    },
  };
}
