import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import path from 'node:path';
import { describe, it } from 'node:test';
import { fileURLToPath } from 'node:url';

const mobileRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');
const repoRoot = path.resolve(mobileRoot, '../..');

const packageJson = JSON.parse(
  readFileSync(path.join(mobileRoot, 'package.json'), 'utf8'),
) as {
  dependencies?: Record<string, string>;
  expo?: { install?: { exclude?: string[] } };
};

const dependabot = readFileSync(path.join(repoRoot, '.github/dependabot.yml'), 'utf8');
const ci = readFileSync(path.join(repoRoot, '.github/workflows/ci.yml'), 'utf8');
const sentryInit = readFileSync(path.join(mobileRoot, 'src/config/sentry.ts'), 'utf8');

describe('Sentry 8 on Expo SDK 58 (#1782 PR 2)', () => {
  it('pins @sentry/react-native to the SDK 58 bundled ~8.28 line', () => {
    assert.equal(packageJson.dependencies?.['@sentry/react-native'], '~8.28.0');
  });

  it('includes @sentry/react-native in Expo version validation', () => {
    assert.ok(!packageJson.expo?.install?.exclude?.includes('@sentry/react-native'));
  });

  it('lifts the Dependabot ignore for @sentry/react-native', () => {
    assert.doesNotMatch(dependabot, /dependency-name:\s*"@sentry\/react-native"/);
    assert.match(dependabot, /#1782/);
  });

  it('keeps unsigned CI builds from calling sentry-cli upload', () => {
    const android = ci.split('name: Assemble debug APK')[1] ?? '';
    const ios = ci.split('name: Build for iOS Simulator')[1] ?? '';
    assert.match(android, /SENTRY_DISABLE_AUTO_UPLOAD:\s*"true"/);
    assert.match(ios, /SENTRY_DISABLE_AUTO_UPLOAD:\s*"true"/);
  });

  it('uses the Sentry 8 xcframework install and requires Xcode 16.4+ / iOS 16.4 (Expo.podspec)', () => {
    assert.match(ci, /SENTRY_USE_XCFRAMEWORK:\s*"1"/);
    assert.doesNotMatch(ci, /SENTRY_USE_XCFRAMEWORK:\s*["']0["']/);
    assert.match(ci, /IOS_CI_XCODE_MIN:\s*"16\.4"/);
    assert.match(ci, /scripts\/select-ios-ci-xcode\.sh/);
    assert.match(ci, /Sentry 8 floor 16\.4/);
    assert.match(ci, /Sentry 8 floor 15\.1/);
    const appJson = JSON.parse(
      readFileSync(path.join(mobileRoot, 'app.json'), 'utf8'),
    ) as { expo: { ios?: { deploymentTarget?: string } } };
    assert.equal(appJson.expo.ios?.deploymentTarget, '16.4');
  });

  it('keeps the reviewed v7 JS init APIs (no v8 rename)', () => {
    assert.match(sentryInit, /Sentry\.init\(/);
    assert.match(sentryInit, /enableAutoSessionTracking:\s*true/);
    assert.match(sentryInit, /tracesSampleRate:/);
    assert.match(sentryInit, /integrations:\s*\[navigationIntegration\]/);
    assert.match(sentryInit, /Sentry\.reactNavigationIntegration\(\)/);
    assert.match(sentryInit, /Sentry\.addBreadcrumb\(/);
    assert.match(sentryInit, /Sentry\.captureException\(/);
  });
});
