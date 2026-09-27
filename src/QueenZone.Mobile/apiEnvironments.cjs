/**
 * Shared API environment defaults for app.config (CommonJS) and the RN app.
 * Keep types/tests in src/QueenZone.Mobile/src/config/environments.ts wrapping this module.
 * @type {const}
 */
const defaultApiBaseUrls = {
  development: 'http://localhost:5146',
  staging: 'https://dev.queenzone.org',
  production: 'https://www.queenzone.org',
};

/**
 * @param {string | undefined | null} raw
 * @returns {'development' | 'staging' | 'production'}
 */
function resolveAppEnvironment(raw) {
  const value = (raw ?? '').trim().toLowerCase();
  if (!value) {
    return 'development';
  }
  if (value === 'prod') {
    return 'production';
  }
  if (value === 'stage') {
    return 'staging';
  }
  if (value === 'dev') {
    return 'development';
  }
  if (value === 'development' || value === 'staging' || value === 'production') {
    return value;
  }
  throw new Error(
    `Unknown app environment "${raw}". Use development, staging, or production (or EXPO_PUBLIC_API_BASE_URL alone).`,
  );
}

/**
 * @param {string} raw
 * @returns {string}
 */
function normalizeApiBaseUrl(raw) {
  let trimmed = raw.trim();
  while (trimmed.endsWith('/')) {
    trimmed = trimmed.slice(0, -1);
  }
  if (!trimmed) {
    throw new Error('API base URL must not be empty.');
  }

  let url;
  try {
    url = new URL(trimmed);
  } catch {
    throw new Error(`API base URL is not a valid absolute URL: "${raw}"`);
  }

  if (url.protocol !== 'http:' && url.protocol !== 'https:') {
    throw new Error(`API base URL must be http(s): "${raw}"`);
  }

  return url.origin;
}

/**
 * @param {{ appEnv: 'development' | 'staging' | 'production', override?: string | null }} input
 * @returns {string}
 */
function resolveApiBaseUrl({ appEnv, override }) {
  const raw = override?.trim() ? override.trim() : defaultApiBaseUrls[appEnv];
  return normalizeApiBaseUrl(raw);
}

/**
 * @param {string} apiBaseUrl
 * @param {string} platform
 * @returns {string}
 */
function rewriteLoopbackForAndroid(apiBaseUrl, platform) {
  if (platform !== 'android') {
    return apiBaseUrl;
  }

  const url = new URL(apiBaseUrl);
  if (url.hostname === 'localhost' || url.hostname === '127.0.0.1') {
    url.hostname = '10.0.2.2';
  }
  return url.origin;
}

/**
 * User-facing store Version prefix (CFBundleShortVersionString / versionName).
 * Flip 0.1 → 1.0 (or 1.1) here; the next publish is `{prefix}.{run}` on
 * TestFlight, Play, and Home. Do not hand-edit app.json per upload.
 *
 * @type {string}
 */
const marketingVersionPrefix = '1.0';

/**
 * @param {string | undefined | null} prefix
 * @returns {string}
 */
function assertMarketingVersionPrefix(prefix) {
  const value = (prefix ?? '').trim();
  if (!/^[0-9]+\.[0-9]+$/.test(value)) {
    throw new Error(
      `Marketing version prefix must be N.N (two integers, got "${prefix}").`,
    );
  }
  return value;
}

/**
 * Store Version / Home string.
 *
 * Publish (run number present): `{prefix}.{run}` e.g. `0.1.847`.
 * Local / unsigned CI / Maestro (no run number): `{prefix}.0` e.g. `0.1.0`.
 *
 * @param {{ prefix?: string | null, runNumber?: string | number | null }} [input]
 * @returns {string}
 */
function resolveMarketingVersion(input = {}) {
  const prefix = assertMarketingVersionPrefix(
    input.prefix ?? marketingVersionPrefix,
  );
  const raw = `${input.runNumber ?? ''}`.trim();
  if (!raw) {
    return `${prefix}.0`;
  }
  if (!/^[0-9]+$/.test(raw) || Number(raw) < 1) {
    throw new Error(
      `Marketing version run number must be a positive integer (got "${input.runNumber}").`,
    );
  }
  return `${prefix}.${String(Number(raw))}`;
}

/**
 * iOS CFBundleVersion / Expo `ios.buildNumber`.
 *
 * App Store Connect rejects uploads when CFBundleVersion is not greater than
 * the previously uploaded build. Expo writes a literal build number into
 * Info.plist at prebuild time, so CI must set IOS_BUILD_NUMBER (or
 * GITHUB_RUN_NUMBER) before `expo prebuild` — passing CURRENT_PROJECT_VERSION
 * to xcodebuild alone does not change a hardcoded Info.plist value.
 *
 * @param {{ override?: string | null, githubRunNumber?: string | null, fallback?: string | null }} [input]
 * @returns {string}
 */
function resolveIosBuildNumber(input = {}) {
  const candidates = [input.override, input.githubRunNumber, input.fallback, '1'];
  for (const raw of candidates) {
    const value = (raw ?? '').trim();
    if (!value) {
      continue;
    }
    if (!/^[0-9]+$/.test(value) || Number(value) < 1) {
      throw new Error(
        `iOS build number must be a positive integer (got "${raw}").`,
      );
    }
    // Normalize leading zeros (Apple accepts numeric strings; keep canonical form).
    return String(Number(value));
  }
  return '1';
}

module.exports = {
  defaultApiBaseUrls,
  marketingVersionPrefix,
  normalizeApiBaseUrl,
  resolveApiBaseUrl,
  resolveAppEnvironment,
  resolveIosBuildNumber,
  resolveMarketingVersion,
  rewriteLoopbackForAndroid,
};
