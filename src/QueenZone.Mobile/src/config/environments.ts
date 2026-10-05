/**
 * Per-environment API base URL defaults (#793).
 *
 * Implementation lives in `apiEnvironments.cjs` so Expo's `app.config.ts`
 * (CommonJS load) and the RN app share one source of truth.
 *
 * Aligns with ASP.NET environments: Development / Staging / Production.
 * Local QueenZone.Web defaults to http://localhost:5146 (launchSettings "http").
 */
// eslint-disable-next-line @typescript-eslint/no-require-imports -- CJS loaded by Expo app.config and shared with the RN app.
const impl = require('../../apiEnvironments.cjs') as {
  defaultApiBaseUrls: Record<AppEnvironment, string>;
  marketingVersionPrefix: string;
  resolveAppEnvironment: (raw: string | null) => AppEnvironment;
  resolveApiBaseUrl: (input: ResolveApiBaseUrlInput) => string;
  normalizeApiBaseUrl: (raw: string) => string;
  resolveIosBuildNumber: (input?: ResolveIosBuildNumberInput) => string;
  resolveMarketingVersion: (input?: ResolveMarketingVersionInput) => string;
  rewriteLoopbackForAndroid: (apiBaseUrl: string, platform: string) => string;
};

export type AppEnvironment = 'development' | 'staging' | 'production';

export type ResolveApiBaseUrlInput = {
  appEnv: AppEnvironment;
  override?: string | null;
};

export type ResolveIosBuildNumberInput = {
  override?: string | null;
  githubRunNumber?: string | null;
  fallback?: string | null;
};

export type ResolveMarketingVersionInput = {
  prefix?: string | null;
  runNumber?: string | number | null;
};

export const defaultApiBaseUrls = impl.defaultApiBaseUrls;
export const marketingVersionPrefix = impl.marketingVersionPrefix;
export const resolveAppEnvironment = impl.resolveAppEnvironment;
export const resolveApiBaseUrl = impl.resolveApiBaseUrl;
export const normalizeApiBaseUrl = impl.normalizeApiBaseUrl;
export const resolveIosBuildNumber = impl.resolveIosBuildNumber;
export const resolveMarketingVersion = impl.resolveMarketingVersion;
export const rewriteLoopbackForAndroid = impl.rewriteLoopbackForAndroid;
