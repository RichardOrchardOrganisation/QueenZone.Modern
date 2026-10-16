/**
 * Crash/error monitoring bootstrap (#855).
 *
 * DSN is public-by-design (Sentry DSNs are not secrets). Prefer the value
 * baked into Expo `extra` at prebuild (`getAppConfig().sentryDsn`) so
 * published Android/iOS bundles still initialize when Metro does not inherit
 * `EXPO_PUBLIC_SENTRY_DSN`. Fall back to that env var for local Metro.
 * Sentry stays disabled — init() is a no-op — until a DSN is configured.
 *
 * Performance tracing needs its own opt-in on top of the DSN: without a
 * tracesSampleRate and the react-native tracing integration, Sentry only
 * ever reports errors/crashes — the Performance/Traces views stay empty
 * even with a valid DSN. `navigationIntegration` is exported so App.tsx can
 * register the NavigationContainer ref and get route-change spans.
 *
 * Sentry React Native 8 (#1782 PR 1) keeps the v7 JS surface we use
 * (`init`, `wrap`, `reactNavigationIntegration`, `addBreadcrumb`,
 * `captureException`). Native floors are iOS 15.1+ (Sentry 8 / RN 0.86
 * `min_ios_version_supported`) and Xcode 16.4+. Expo module pods still
 * declare `:ios => '16.4'` in `Expo.podspec` and raise themselves; that
 * is not the app target. Symbol upload uses sentry-cli 3.
 */
import * as Sentry from '@sentry/react-native';
import { isExpoFetchCanceled, isLostConnectionMessage } from '../api/errors';
import { getAppConfig } from './appConfig';

export const navigationIntegration = Sentry.reactNavigationIntegration();

export function reportApiFailure(event: {
  kind: string;
  status: number;
  method: string;
  path: string;
  cause?: unknown;
}): void {
  const causeMessage = event.cause instanceof Error ? event.cause.message : undefined;
  Sentry.addBreadcrumb({
    category: 'api',
    type: 'http',
    level: event.kind === 'http' && event.status < 500 ? 'warning' : 'error',
    data: {
      kind: event.kind,
      status: event.status,
      method: event.method,
      path: event.path,
      ...(causeMessage ? { cause: causeMessage } : {}),
    },
  });

  if (event.method === 'GET') {
    return;
  }

  // Expo fetch cancel / lost-connection on an aborted write is dying-process
  // noise, not an error-level issue (#1201). Keep capturing real write
  // TypeError: Network request failed and timeouts.
  if (isExpoFetchCanceled(event.cause) || isLostConnectionMessage(event.cause)) {
    return;
  }

  if (event.kind !== 'offline' && event.kind !== 'timeout' && event.kind !== 'local-file') {
    return;
  }

  Sentry.captureException(event.cause instanceof Error ? event.cause : new Error(`api ${event.kind}`), {
    extra: {
      kind: event.kind,
      status: event.status,
      method: event.method,
      path: event.path,
    },
  });
}

export function initSentry(): void {
  const { sentryDsn, appEnv } = getAppConfig();
  if (!sentryDsn) {
    return;
  }

  const tracesSampleRate = Number(
    process.env.EXPO_PUBLIC_SENTRY_TRACES_SAMPLE_RATE ?? '1.0',
  );

  Sentry.init({
    dsn: sentryDsn,
    environment: appEnv,
    enableAutoSessionTracking: true,
    tracesSampleRate: Number.isFinite(tracesSampleRate) ? tracesSampleRate : 1.0,
    integrations: [navigationIntegration],
  });
}
