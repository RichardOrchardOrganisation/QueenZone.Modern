import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import path from 'node:path';
import { describe, it } from 'node:test';
import { fileURLToPath } from 'node:url';

const repoRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../../../..');
const play = readFileSync(
  path.join(repoRoot, '.github/workflows/publish-android-google-play.yml'),
  'utf8',
);
const testflight = readFileSync(
  path.join(repoRoot, '.github/workflows/publish-ios-testflight.yml'),
  'utf8',
);

describe('Play publish workflow', () => {
  it('defaults store builds to production and offers an explicit staging choice', () => {
    assert.match(play, /app_environment:[\s\S]*options:\s*\n\s*- production\s*\n\s*- staging[\s\S]*default: production/);
    assert.match(play, /EXPO_PUBLIC_APP_ENV: \$\{\{ inputs\.app_environment \}\}/);
  });

  it('does not inject 0.1.0-internal as versionName', () => {
    assert.doesNotMatch(play, /0\.1\.0-internal/);
    assert.doesNotMatch(play, /android\.injected\.version\.name/);
  });

  it('keeps the monotonic versionCode and passes GITHUB_RUN_NUMBER into prebuild', () => {
    assert.match(play, /android\.injected\.version\.code/);
    const prebuild = play.split('name: Generate Android project')[1] ?? '';
    assert.match(prebuild, /GITHUB_RUN_NUMBER: \$\{\{ github\.run_number \}\}/);
    assert.match(prebuild, /ANDROID_VERSION_CODE: \$\{\{ github\.run_number \}\}/);
  });

  it('uploads symbols with sentry-cli 3 and does not disable the xcframework install', () => {
    assert.match(play, /name: Confirm sentry-cli 3/);
    assert.match(play, /Expected sentry-cli 3\.x from @sentry\/react-native 8/);
    assert.match(play, /SENTRY_AUTH_TOKEN/);
    assert.match(play, /SENTRY_ORG/);
    assert.match(play, /SENTRY_PROJECT/);
    assert.doesNotMatch(play, /SENTRY_DISABLE_AUTO_UPLOAD/);
    assert.doesNotMatch(play, /SENTRY_USE_XCFRAMEWORK:\s*["']0["']/);
  });

  it('asserts AAB versionCode equals the run number before Play upload', () => {
    const verify = play.split('name: Verify Android App Bundle')[1] ?? '';
    const verifyBeforeUpload = verify.split('name: Upload to Google Play')[0] ?? '';
    assert.match(verifyBeforeUpload, /bundletool/);
    assert.match(verifyBeforeUpload, /dump manifest/);
    assert.match(verifyBeforeUpload, /android:versionCode/);
    assert.match(verifyBeforeUpload, /ANDROID_VERSION_CODE/);
    assert.match(verifyBeforeUpload, /AAB versionCode=/);
    assert.match(verifyBeforeUpload, /base\/assets\/app\.config/);
    assert.match(verifyBeforeUpload, /AAB API target=/);
    assert.match(verifyBeforeUpload, /expected_api_base_url/);
    assert.match(verifyBeforeUpload, /EXPO_PUBLIC_TELEMETRYDECK_APP_ID/);
    assert.match(verifyBeforeUpload, /extra\.telemetryDeckAppId/);
  });
});

describe('TestFlight publish workflow', () => {
  it('defaults store builds to production and offers an explicit staging choice', () => {
    assert.match(testflight, /app_environment:[\s\S]*options:\s*\n\s*- production\s*\n\s*- staging[\s\S]*default: production/);
    assert.match(testflight, /EXPO_PUBLIC_APP_ENV: \$\{\{ inputs\.app_environment \}\}/);
  });

  it('uploads symbols with sentry-cli 3 and uses the Sentry 8 xcframework install', () => {
    assert.match(testflight, /name: Confirm sentry-cli 3/);
    assert.match(testflight, /Expected sentry-cli 3\.x from @sentry\/react-native 8/);
    assert.match(testflight, /SENTRY_AUTH_TOKEN/);
    assert.match(testflight, /SENTRY_USE_XCFRAMEWORK:\s*"1"/);
    assert.doesNotMatch(testflight, /SENTRY_USE_XCFRAMEWORK:\s*["']0["']/);
    assert.doesNotMatch(testflight, /SENTRY_DISABLE_AUTO_UPLOAD/);
  });

  it('asserts CFBundleShortVersionString equals the baked marketing version', () => {
    assert.match(testflight, /CFBundleVersion/);
    assert.match(testflight, /CFBundleShortVersionString/);
    assert.match(testflight, /resolveMarketingVersion/);
    assert.match(testflight, /marketingVersionPrefix/);
    assert.match(testflight, /IPA API target=/);
    assert.match(testflight, /expected_api_base_url/);
    assert.match(testflight, /EXPO_PUBLIC_TELEMETRYDECK_APP_ID/);
    assert.match(testflight, /extra\.telemetryDeckAppId/);
  });
});
