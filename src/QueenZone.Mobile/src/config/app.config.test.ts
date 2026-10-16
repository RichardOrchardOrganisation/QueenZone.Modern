import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { describe, it } from 'node:test';

const source = readFileSync(new URL('../../app.config.ts', import.meta.url), 'utf8');

describe('app.config marketing version', () => {
  it('bakes resolveMarketingVersion into Expo version from GITHUB_RUN_NUMBER', () => {
    assert.match(source, /marketingVersionPrefix/);
    assert.match(source, /resolveMarketingVersion/);
    assert.match(
      source,
      /const version = resolveMarketingVersion\(\{\s*prefix: marketingVersionPrefix,\s*runNumber: process\.env\.GITHUB_RUN_NUMBER,\s*\}\)/,
    );
    assert.match(source, /slug: config\.slug \?\? 'queenzone-mobile',\s*version,/);
  });
});

describe('app.config Sentry Expo plugin', () => {
  it('registers the official @sentry/react-native/expo plugin', () => {
    const appJson = JSON.parse(
      readFileSync(new URL('../../app.json', import.meta.url), 'utf8'),
    ) as { expo: { plugins: unknown[] } };
    assert.ok(appJson.expo.plugins.includes('@sentry/react-native/expo'));
    assert.match(source, /'@sentry\/react-native\/expo'/);
  });

  it('pins the iOS deployment target at Expo 57 / Sentry 8 floor 16.4 (#1782)', () => {
    const appJson = JSON.parse(
      readFileSync(new URL('../../app.json', import.meta.url), 'utf8'),
    ) as { expo: { ios?: { deploymentTarget?: string } } };
    assert.equal(appJson.expo.ios?.deploymentTarget, '16.4');
  });
});

describe('app.config TelemetryDeck setting', () => {
  it('bakes the public App ID into Expo extra without a committed value', () => {
    assert.match(source, /process\.env\.EXPO_PUBLIC_TELEMETRYDECK_APP_ID/);
    assert.match(source, /telemetryDeckAppId:/);
  });
});

describe('app.config android versionCode', () => {
  it('bakes android.versionCode from GITHUB_RUN_NUMBER at prebuild', () => {
    assert.match(
      source,
      /const androidVersionCode = Number\(\s*resolveIosBuildNumber\(\{/,
    );
    assert.match(source, /override: process\.env\.ANDROID_VERSION_CODE/);
    assert.match(source, /githubRunNumber: process\.env\.GITHUB_RUN_NUMBER/);
    assert.match(source, /versionCode: androidVersionCode/);
  });
});

describe('Android home widget config', () => {
  it('has a picker preview and does not require an unregistered configuration screen', () => {
    type AndroidWidgetPlugin = [
      string,
      { widgets?: Record<string, unknown>[] },
    ];
    const appJson = JSON.parse(
      readFileSync(new URL('../../app.json', import.meta.url), 'utf8'),
    ) as {
      expo: {
        plugins: (string | AndroidWidgetPlugin)[];
      };
    };
    const plugin = appJson.expo.plugins.find(
      (candidate): candidate is AndroidWidgetPlugin =>
        Array.isArray(candidate) && candidate[0] === 'react-native-android-widget',
    );
    const widget = plugin?.[1].widgets?.find(
      (candidate) => candidate.name === 'OnThisDayWidget',
    );

    assert.equal(widget?.previewImage, './assets/android-widget-preview.png');
    assert.equal(widget?.widgetFeatures, undefined);

    const preview = readFileSync(
      new URL('../../assets/android-widget-preview.png', import.meta.url),
    );
    assert.deepEqual([...preview.subarray(1, 4)], [0x50, 0x4e, 0x47]);
    assert.equal(preview.readUInt32BE(16), 540);
    assert.equal(preview.readUInt32BE(20), 330);
  });
});
