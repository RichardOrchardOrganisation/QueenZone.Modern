import type { ConfigContext, ExpoConfig } from 'expo/config';

// CommonJS shared module — Expo loads app.config via require(), not Metro.
const {
  marketingVersionPrefix,
  resolveApiBaseUrl,
  resolveAppEnvironment,
  resolveIosBuildNumber,
  resolveMarketingVersion,
  // eslint-disable-next-line @typescript-eslint/no-require-imports -- CJS loaded by Expo, not Metro.
} = require('./apiEnvironments.cjs') as typeof import('./apiEnvironments.cjs');
const {
  filterExpoPluginsForSmokeEmbed,
  isSmokeEmbedEnabled,
  smokeEmbedAutolinking,
  // eslint-disable-next-line @typescript-eslint/no-require-imports -- CJS loaded by Expo, not Metro.
} = require('./plugins/smokeEmbed.cjs') as {
  filterExpoPluginsForSmokeEmbed: (
    plugins: ExpoConfig['plugins'],
    env?: Record<string, string | undefined>,
  ) => ExpoConfig['plugins'];
  isSmokeEmbedEnabled: (env?: Record<string, string | undefined>) => boolean;
  smokeEmbedAutolinking: () => { exclude: string[] };
};

/**
 * Dynamic Expo config: embeds appEnv + apiBaseUrl into `extra` so native and
 * JS builds can switch backends without code changes (#793).
 *
 * Override at start/build time:
 *   EXPO_PUBLIC_APP_ENV=staging|production|development
 *   EXPO_PUBLIC_API_BASE_URL=https://localhost:7162
 *   EXPO_PUBLIC_TELEMETRYDECK_APP_ID=<public TelemetryDeck app identifier>
 *   ANDROID_VERSION_CODE=<positive integer> (Play versionCode; see GITHUB_RUN_NUMBER)
 *   IOS_BUILD_NUMBER=<positive integer> (TestFlight CFBundleVersion; see GITHUB_RUN_NUMBER)
 *   GITHUB_RUN_NUMBER=<positive integer> (store Version `{prefix}.{run}` + integer build)
 */
export default ({ config }: ConfigContext): ExpoConfig => {
  const appEnv = resolveAppEnvironment(
    process.env.EXPO_PUBLIC_APP_ENV ?? process.env.APP_ENV,
  );
  const apiBaseUrl = resolveApiBaseUrl({
    appEnv,
    override: process.env.EXPO_PUBLIC_API_BASE_URL,
  });
  const iosBuildNumber = resolveIosBuildNumber({
    override: process.env.IOS_BUILD_NUMBER,
    githubRunNumber: process.env.GITHUB_RUN_NUMBER,
    fallback: config.ios?.buildNumber,
  });
  // Same monotonic run-number scheme as ios.buildNumber (#1078 / #1195).
  // Expo writes this into the generated Gradle project at prebuild; leaving
  // it unset produced versionCode 1 and Play rejected the reused integer.
  const androidVersionCode = Number(
    resolveIosBuildNumber({
      override: process.env.ANDROID_VERSION_CODE,
      githubRunNumber: process.env.GITHUB_RUN_NUMBER,
      fallback:
        config.android?.versionCode != null
          ? String(config.android.versionCode)
          : undefined,
    }),
  );
  const version = resolveMarketingVersion({
    prefix: marketingVersionPrefix,
    runNumber: process.env.GITHUB_RUN_NUMBER,
  });
  const smokeEmbed = isSmokeEmbedEnabled(process.env);
  // Metro inlines EXPO_PUBLIC_* at bundle time. Constants.expoConfig.extra can
  // still be empty on the first JS tick of an iOS Release embed (#1387).
  if (smokeEmbed && !process.env.EXPO_PUBLIC_SMOKE_EMBED) {
    process.env.EXPO_PUBLIC_SMOKE_EMBED = '1';
  }
  return {
    ...config,
    name: config.name ?? 'QueenZone',
    slug: config.slug ?? 'queenzone-mobile',
    version,
    android: {
      ...(typeof config.android === 'object' && config.android !== null
        ? config.android
        : {}),
      googleServicesFile: './google-services.json',
      versionCode: androidVersionCode,
    },
    ios: {
      ...(typeof config.ios === 'object' && config.ios !== null ? config.ios : {}),
      buildNumber: iosBuildNumber,
      config: {
        ...(typeof config.ios?.config === 'object' && config.ios.config !== null
          ? config.ios.config
          : {}),
        // QueenZone uses only exempt platform HTTPS; declaring this prevents
        // every TestFlight upload from pausing for export-compliance answers.
        usesNonExemptEncryption: false,
      },
    },
    extra: {
      ...(typeof config.extra === 'object' && config.extra !== null ? config.extra : {}),
      appEnv,
      apiBaseUrl,
      buildNumber: iosBuildNumber,
      buildTimestampUtc: process.env.BUILD_TIMESTAMP_UTC,
      buildRevision: process.env.BUILD_REVISION,
      // Baked at prebuild into EXConstants so published JS bundles still
      // initialize Sentry when Metro does not inherit EXPO_PUBLIC_SENTRY_DSN.
      sentryDsn: (process.env.EXPO_PUBLIC_SENTRY_DSN ?? '').trim() || undefined,
      // Public identifier only. Unset keeps product analytics disabled.
      telemetryDeckAppId:
        (process.env.EXPO_PUBLIC_TELEMETRYDECK_APP_ID ?? '').trim() || undefined,
      // Baked so Release smoke/journeys can still accept queenzone://smoke-auth
      // while store Release (no flag) stays closed (#1322).
      smokeEmbed: smokeEmbed || undefined,
    },
    plugins: [
      // app.json registers @sentry/react-native/expo; keep a single configured
      // copy here so org/project env still apply and the plugin is not doubled.
      // Smoke embed also drops expo-dev-client so Release (and Debug) launch
      // is the app, not the development-server launcher (#1225 / #1322).
      ...(filterExpoPluginsForSmokeEmbed(
        (config.plugins ?? []).filter((plugin) => {
          const name = Array.isArray(plugin) ? plugin[0] : plugin;
          return name !== '@sentry/react-native/expo' && name !== '@sentry/react-native';
        }),
      ) ?? []),
      // CNG: android/ is generated in CI. Force WorkManager 2.8.1 so
      // react-native-android-widget's work-runtime does not clash with a
      // transitive work-runtime-ktx 2.7.1 (duplicate OneTimeWorkRequestKt).
      './plugins/withAndroidWorkRuntimeAlignment.cjs',
      // iOS expo-audio 57.0.4: currentStatus must not call currentDate() (#1234).
      './plugins/withExpoAudioIosCurrentOffsetFromLive.cjs',
      // Xcode/iOS 27 requires UIScene; Expo SDK 57 still generates the legacy
      // AppDelegate-owned UIWindow and the resulting app traps at launch.
      './plugins/withIosSceneLifecycle.cjs',
      // After expo-media-library: drop READ_MEDIA_* so add-only save does not
      // request photo/video/audio read at install (#1230 / #1232).
      './plugins/withAndroidAddOnlyPhotos.cjs',
      ...(smokeEmbed ? ['./plugins/smokeEmbed.cjs'] : []),
      // After expo-widgets writes ExpoWidgetsTarget: render On This Day in
      // SwiftUI so the gallery/home snapshot is never a Release EmptyView.
      './plugins/withIosOnThisDayNativeWidget.cjs',
      [
        // Auth token is deliberately NOT passed here — sentry-cli picks up
        // SENTRY_AUTH_TOKEN from the build environment directly (Gradle /
        // Xcode build phase), so it never lands in the generated,
        // gitignored sentry.properties file. See publish-*.yml workflows.
        // CI unsigned builds set SENTRY_DISABLE_AUTO_UPLOAD=true so missing
        // org/token does not fail the Bundle RN script (#857).
        '@sentry/react-native/expo',
        {
          ...(process.env.SENTRY_ORG ? { organization: process.env.SENTRY_ORG } : {}),
          ...(process.env.SENTRY_PROJECT ? { project: process.env.SENTRY_PROJECT } : {}),
        },
      ],
      [
        // ADR 0014: direct APNs/FCM, no EAS. Expo SDK 57 always generates
        // aps-environment=development at prebuild; Xcode changes it to
        // production when archiving with the App Store distribution profile.
        'expo-notifications',
        {
          icon: './assets/ic-notification.png',
          color: '#B89A4A',
          defaultChannel: 'default',
        },
      ],
    ],
    ...(smokeEmbed ? { autolinking: smokeEmbedAutolinking() } : {}),
  } as ExpoConfig;
};
