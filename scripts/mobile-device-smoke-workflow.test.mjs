import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';

const smoke = readFileSync(new URL('../.github/workflows/mobile-device-smoke.yml', import.meta.url), 'utf8');
const chooser = readFileSync(new URL('../.github/workflows/choose-ios-runner.yml', import.meta.url), 'utf8');
const ci = readFileSync(new URL('../.github/workflows/ci.yml', import.meta.url), 'utf8');

function job(source, name) {
  const start = source.indexOf(`  ${name}:`);
  assert.ok(start !== -1, `missing job ${name}`);
  const next = source.slice(start + 3).search(/\n  [a-z0-9-]+:/);
  return next === -1 ? source.slice(start) : source.slice(start, start + 3 + next);
}

const iosSmoke = job(smoke, 'mobile-ios-smoke');
const iosJourneys = job(smoke, 'mobile-ios-journeys');
const androidSmoke = job(smoke, 'mobile-android-smoke');
const androidRelease = job(smoke, 'mobile-android-release');
const androidJourneys = job(smoke, 'mobile-android-journeys');
const choose = job(smoke, 'choose-ios-runner');

test('iOS smoke and journeys use the shared chooser instead of hard-wired macos-26', () => {
  assert.match(iosSmoke, /fromJSON\(needs\.choose-ios-runner\.outputs\.runs_on\)/);
  assert.match(iosJourneys, /runs_on_secondary/);
  for (const block of [iosSmoke, iosJourneys]) {
    assert.match(block, /needs: choose-ios-runner/);
    assert.doesNotMatch(block, /^    runs-on: macos-26$/m);
    assert.match(block, /needs\.choose-ios-runner\.result == 'success'/);
    assert.match(block, /Delete job DerivedData/);
    assert.match(block, /RUNNER_NAME: \$\{\{ runner\.name \}\}/);
    assert.doesNotMatch(block, /\$\{\{ runner\.name \}\}.*GITHUB_STEP_SUMMARY|GITHUB_STEP_SUMMARY.*\$\{\{ runner\.name \}\}/);
  }
  assert.match(choose, /uses: \.\/\.github\/workflows\/choose-ios-runner\.yml/);
  assert.match(choose, /mac_claimed_by_sibling:/);
  assert.match(choose, /force_hosted:/);
  assert.match(smoke, /force_hosted_ios:/);
  assert.match(iosJourneys, /github\.event\.inputs\.suite == 'both' && needs\.choose-ios-runner\.outputs\.runs_on_secondary/);
});

test('ci.yml reuses the same choose-ios-runner workflow', () => {
  const ciChooser = job(ci, 'mobile-ios-runner');
  const ciIos = job(ci, 'mobile-ios');
  assert.match(ciChooser, /uses: \.\/\.github\/workflows\/choose-ios-runner\.yml/);
  assert.match(ci, /fromJSON\(needs\.mobile-ios-runner\.outputs\.runs_on\)/);
  assert.match(chooser, /scripts\/choose-ios-runner\.sh/);
  assert.match(chooser, /BITWARDEN_IOS_RUNNER_SECRETS/);
  assert.match(chooser, /IOS_RUNNER_ADMIN_TOKEN/);
  assert.match(chooser, /runs_on_secondary/);
  assert.match(ciIos, /scripts\/select-ios-ci-xcode\.sh/);
  assert.match(ciIos, /derivedDataPath "\$DERIVED"/);
  assert.match(ciIos, /Delete job DerivedData/);
  assert.doesNotMatch(ciIos, /^\s+sudo xcode-select/m);
  assert.doesNotMatch(ciIos, /-derivedDataPath build$/m);
});

test('chooser treats same-run Android Mac jobs as a sibling claim', () => {
  assert.match(choose, /github\.event\.schedule == '0 4 \* \* 1-5'/);
  assert.match(choose, /github\.event\.inputs\.suite == 'release'/);
  assert.match(choose, /github\.event\.inputs\.platform == 'android'/);
  assert.doesNotMatch(choose, /0 16 \* \* 1-5/);
});

test('Android Mac isolation stays on the dedicated AVD and port', () => {
  assert.match(androidSmoke, /runs-on: \[self-hosted, macOS, ARM64, queenzone\]/);
  assert.match(androidSmoke, /ANDROID_SMOKE_AVD: QueenZone_CI_Smoke_API_36/);
  assert.match(androidSmoke, /ANDROID_SMOKE_EMULATOR_PORT: "5558"/);
  assert.match(androidRelease, /runs-on: \[self-hosted, macOS, ARM64, queenzone\]/);
  assert.match(androidJourneys, /runs-on: ubuntu-latest/);
});

test('both iOS jobs are self-hosted safe for Xcode, simulators, and caches', () => {
  for (const block of [iosSmoke, iosJourneys]) {
    assert.match(block, /scripts\/select-ios-ci-xcode\.sh/);
    assert.match(block, /scripts\/ios-ci-simulator\.sh create/);
    assert.match(block, /scripts\/ios-ci-simulator\.sh cleanup/);
    assert.match(block, /Configure self-hosted tool paths/);
    assert.match(block, /Verify self-hosted CocoaPods/);
    assert.match(block, /derivedDataPath/);
    assert.match(block, /CODE_SIGNING_ALLOWED=NO/);
    assert.doesNotMatch(block, /sudo xcode-select/);
    assert.doesNotMatch(block, /simctl erase all/);
    assert.doesNotMatch(block, /simctl shutdown all/);
    assert.doesNotMatch(block, /simctl delete all/);
    assert.doesNotMatch(block, /adb /);
  }
});

test('device smoke iOS jobs stay non-required soak jobs', () => {
  assert.match(smoke, /Not a required PR check/);
  assert.doesNotMatch(smoke, /required-on-main/);
});
