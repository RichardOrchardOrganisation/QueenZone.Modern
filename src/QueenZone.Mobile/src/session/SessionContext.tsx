import {
  createContext,
  memo,
  useContext,
  useEffect,
  useMemo,
  useState,
  useSyncExternalStore,
  type ReactNode,
} from 'react';
import { Alert, AppState, Linking } from 'react-native';
import { addNetworkStateListener } from 'expo-network';
import * as Notifications from 'expo-notifications';
import * as Sentry from '@sentry/react-native';
import { getAppConfig } from '../config/appConfig';
import { ApiError, configureAuthenticatedGetRecovery, fetchJson } from '../api/client';
import { parseMemberProfile, type MemberProfile } from '../api/me';
import { clearPushRegistration, refreshPushRegistration, syncPushRegistration } from '../notifications';
import {
  logoutRemote,
  refreshAccessToken,
  revokeRefreshToken,
  signInWithPassword as requestPasswordTokens,
  signInWithProvider,
} from './oauth';
import { parseSmokeAuthAccessToken } from './smokeAuth';
import { purgePrivateContentCache } from '../cache';
import { purgeAllDownloads, reconcileDownloads } from '../downloads/manager';
import {
  configureOfflineQueueAuth,
  countPendingOfflineItems,
  prepareOfflineQueueDiscard,
  invalidateOfflineQueueFlush,
  flushOfflineQueue,
} from '../offlineQueue';
import {
  clearStoredSession,
  isKeychainLockedError,
  readStoredSession,
  writeStoredIdentityShell,
  writeStoredSession,
} from './tokenStore';

import { SessionManager, sessionFromSnapshot, type Session, type SessionActions, type SessionEvent } from './SessionManager';
export type { Session, SessionActions } from './SessionManager';

const SessionStateContext = createContext<Session | undefined>(undefined);
const SessionActionsContext = createContext<SessionActions | undefined>(undefined);

const SessionProviderChildren = memo(function SessionProviderChildren({
  children,
}: {
  children: ReactNode;
}) {
  return children;
});

type SessionContextValue = Session & SessionActions;

function showSessionEvent(event: SessionEvent) {
  if (event.type === 'cleanupFailed') {
    Alert.alert(
      'Sign-out cleanup incomplete',
      `${event.hasCurrentSession ? 'Your current session is unchanged.' : 'You are signed out in this app.'} Saved sign-in or offline data may remain on this device. Retry cleanup before closing the app or signing in again. A restart could restore a session or pending sends.`,
      [{ text: 'OK' }, { text: 'Retry cleanup', onPress: event.retry }],
    );
    return;
  }
  Alert.alert(
    event.inspectionFailed ? 'Unable to check pending sends' : 'Discard pending sends?',
    event.inspectionFailed
      ? 'Pending messages, replies and crossword saves could not be checked. Sign out and discard pending changes when storage is available?'
      : 'Messages, replies and crossword saves waiting to sync will be deleted.',
    [
      { text: 'Cancel', style: 'cancel', onPress: event.cancel },
      { text: 'Sign out', style: 'destructive', onPress: event.confirm },
    ],
    { cancelable: true, onDismiss: event.cancel },
  );
}

export function SessionProvider({ children }: { children: ReactNode }) {
  const [manager] = useState(() => new SessionManager({
    tokenStore: { readStoredSession, writeStoredSession, writeStoredIdentityShell, clearStoredSession, isKeychainLockedError },
    oauth: { signInWithProvider, signInWithPassword: requestPasswordTokens, refreshAccessToken, logoutRemote, revokeRefreshToken, loadProfile },
    push: { syncPushRegistration, refreshPushRegistration, clearPushRegistration },
    cacheAndDownloads: { purgePrivateContentCache, purgeAllDownloads, reconcileDownloads },
    offlineQueue: { configureOfflineQueueAuth, countPendingOfflineItems, prepareOfflineQueueDiscard, invalidateOfflineQueueFlush, flushOfflineQueue },
    logger: { captureException: (error, context) => Sentry.captureException(error, context) },
    clock: { now: () => Date.now(), setTimeout: (callback, ms) => setTimeout(callback, ms), clearTimeout: timer => clearTimeout(timer) },
    config: { ...getAppConfig(), isDev: typeof __DEV__ !== 'undefined' ? __DEV__ : false },
    // Only definite background defers rotation; iOS can report unknown on launch.
    isInBackground: () => AppState.currentState === 'background',
    onEvent: showSessionEvent,
  }));
  const snapshot = useSyncExternalStore(manager.subscribe, manager.getSnapshot);
  const session = useMemo(() => sessionFromSnapshot(snapshot), [snapshot]);
  const actions = useMemo<SessionActions>(() => ({
    signIn: manager.signIn,
    signInWithPassword: manager.signInWithPassword,
    signOut: manager.signOut,
    refreshProfile: manager.refreshProfile,
    ensureAccessToken: manager.ensureAccessToken,
    setAccessToken: manager.setAccessToken,
    applySmokeSession: manager.applySmokeSession,
  }), [manager]);

  useEffect(() => {
    configureAuthenticatedGetRecovery(manager.recoverRejectedAccessToken);
    manager.start();
    const appState = AppState.addEventListener('change', state => {
      if (state === 'active') manager.onAppActive();
    });
    const network = addNetworkStateListener(state => {
      if (state.isInternetReachable === true || state.isConnected === true) manager.onNetworkOnline();
    });
    const push = Notifications.addPushTokenListener(manager.onPushTokenChanged);
    return () => {
      manager.stop();
      configureAuthenticatedGetRecovery(null);
      appState.remove();
      network.remove();
      push.remove();
    };
  }, [manager]);

  useEffect(() => {
    if (!manager.smokeAuthAllowed()) return;
    const handleUrl = (url: string | null | undefined) => {
      const token = url ? parseSmokeAuthAccessToken(url) : null;
      if (token) void manager.applySmokeSession(token);
    };
    const subscription = Linking.addEventListener('url', ({ url }) => handleUrl(url));
    void Linking.getInitialURL().then(handleUrl);
    return () => subscription.remove();
  }, [manager]);

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
