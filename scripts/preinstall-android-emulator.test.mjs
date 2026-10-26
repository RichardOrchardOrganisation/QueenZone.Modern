import { test } from 'node:test';
import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { chmodSync, existsSync, mkdirSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

// #2351: hosted journeys emulator install must retry a corrupt system-image
// download and label an exhausted install as an install failure.
const scriptsDir = path.dirname(fileURLToPath(import.meta.url));
const script = path.join(scriptsDir, 'preinstall-android-emulator.sh');
const workflow = readFileSync(new URL('../.github/workflows/mobile-device-smoke.yml', import.meta.url), 'utf8');
const journeysJob = workflow.slice(
  workflow.indexOf('  mobile-android-journeys:'),
  workflow.indexOf('  mobile-android-release:'),
);

const fakeSdkmanager = `#!/usr/bin/env bash
set -u
sdk_root=""
for arg in "$@"; do
  case "$arg" in
    --sdk_root=*) sdk_root="\${arg#--sdk_root=}" ;;
    --licenses) exit 0 ;;
  esac
done
count_file="$FAKE_STATE/count"
n=$(( $(cat "$count_file" 2>/dev/null || echo 0) + 1 ))
echo "$n" > "$count_file"
echo "$*" >> "$FAKE_STATE/calls"
img="$sdk_root/system-images/android-34/google_apis/x86_64"
corrupt() {
  mkdir -p "$img" "$HOME/.android/cache"
  echo "partial" > "$img/partial.bin"
  echo "stale" > "$HOME/.android/cache/sdkbin-1_deadbeef"
  echo "Warning: An error occurred while preparing SDK package Google APIs Intel x86_64 Atom System Image: Error on ZipFile unknown archive."
  exit 0
}
if [ "$FAKE_MODE" = "always-corrupt" ]; then corrupt; fi
if [ "$FAKE_MODE" = "corrupt-then-ok" ] && [ "$n" -lt 2 ]; then corrupt; fi
if [ "$FAKE_MODE" = "corrupt-then-ok" ] && [ -e "$img/partial.bin" ]; then
  echo "partial download was not cleared" >&2; exit 3
fi
mkdir -p "$img" "$sdk_root/emulator"
printf 'Pkg.Desc=Google APIs Intel x86_64 Atom System Image\\nAndroidVersion.ApiLevel=34\\n' > "$img/source.properties"
echo "img" > "$img/system.img"
printf '#!/bin/sh\\n' > "$sdk_root/emulator/emulator"; chmod +x "$sdk_root/emulator/emulator"
echo "Pkg.Revision=36.1.9" > "$sdk_root/emulator/source.properties"
exit 0
`;

function setup(mode) {
  const tmp = mkdtempSync(path.join(os.tmpdir(), 'qz-emu-install-'));
  const sdk = path.join(tmp, 'sdk');
  const home = path.join(tmp, 'home');
  const state = path.join(tmp, 'state');
  const results = path.join(tmp, 'results');
  for (const dir of [sdk, home, state]) mkdirSync(dir, { recursive: true });
  const fake = path.join(tmp, 'sdkmanager');
  writeFileSync(fake, fakeSdkmanager);
  chmodSync(fake, 0o755);
  const run = () => spawnSync('bash', [script, '--results-dir', results], {
    encoding: 'utf8',
    env: {
      ...process.env,
      HOME: home,
      ANDROID_SDK_ROOT: sdk,
      ANDROID_HOME: sdk,
      QZ_SDKMANAGER: fake,
      FAKE_MODE: mode,
      FAKE_STATE: state,
      ANDROID_EMULATOR_INSTALL_BACKOFF_SECONDS: '0',
    },
  });
  const calls = () => (existsSync(path.join(state, 'calls')) ? readFileSync(path.join(state, 'calls'), 'utf8').trim().split('\n') : []);
  return { tmp, sdk, home, results, run, calls, cleanup: () => rmSync(tmp, { recursive: true, force: true }) };
}

test('retries a corrupt system-image download after clearing the partial tree and ~/.android cache', () => {
  const s = setup('corrupt-then-ok');
  try {
    const r = s.run();
    assert.equal(r.status, 0, r.stdout + r.stderr);
    assert.match(r.stdout, /attempt 1\/3/);
    assert.match(r.stdout, /attempt 2\/3/);
    assert.doesNotMatch(r.stdout, /attempt 3\/3/);
    assert.match(r.stdout, /Installed system-images;android-34;google_apis;x86_64 and emulator on attempt 2/);
    assert.equal(existsSync(path.join(s.sdk, 'system-images/android-34/google_apis/x86_64/partial.bin')), false);
    assert.equal(existsSync(path.join(s.home, '.android/cache')), false);
    assert.equal(existsSync(path.join(s.results, 'android-install-failure')), false);
    assert.match(s.calls()[0], /--install emulator system-images;android-34;google_apis;x86_64 --channel=0/);
  } finally {
    s.cleanup();
  }
});

test('labels an exhausted emulator install as install failure, never a selector miss', () => {
  const s = setup('always-corrupt');
  try {
    const r = s.run();
    assert.equal(r.status, 1, r.stdout + r.stderr);
    assert.equal(s.calls().length, 3);
    assert.match(r.stderr, /::error::Android emulator install failure: .*after 3 attempts/);
    assert.match(r.stderr, /Not a selector or assertion miss/);
    assert.doesNotMatch(r.stdout, /::error::/);
    const marker = readFileSync(path.join(s.results, 'android-install-failure'), 'utf8');
    assert.match(marker, /^class=install_failure$/m);
    assert.match(marker, /^package=system-images;android-34;google_apis;x86_64$/m);
    assert.match(readFileSync(path.join(s.results, 'harness.log'), 'utf8'), /android_failure_class=install_failure/);
  } finally {
    s.cleanup();
  }
});

test('skips sdkmanager when a complete image and emulator are already present (cache hit)', () => {
  const s = setup('ok');
  try {
    assert.equal(s.run().status, 0);
    const before = s.calls().length;
    const r = s.run();
    assert.equal(r.status, 0, r.stdout + r.stderr);
    assert.match(r.stdout, /already installed/);
    assert.equal(s.calls().length, before);
  } finally {
    s.cleanup();
  }
});

test('journeys job preinstalls and caches the system image before the emulator runner', () => {
  const restore = journeysJob.indexOf('actions/cache/restore@');
  const install = journeysJob.indexOf('./scripts/preinstall-android-emulator.sh');
  const save = journeysJob.indexOf('actions/cache/save@');
  const runner = journeysJob.indexOf('reactivecircus/android-emulator-runner@');
  assert.ok(restore !== -1 && install !== -1 && save !== -1 && runner !== -1);
  assert.ok(restore < install && install < save && save < runner);
  assert.match(journeysJob, /key: android-sysimg-\$\{\{ runner\.os \}\}-api\$\{\{ env\.ANDROID_EMULATOR_API_LEVEL \}\}-\$\{\{ env\.ANDROID_EMULATOR_TARGET \}\}-\$\{\{ env\.ANDROID_EMULATOR_ARCH \}\}/);
  assert.match(journeysJob, /if: steps\.android-emulator-install\.outcome == 'success'/);
});

test('journeys failure label distinguishes install/boot failure from a selector miss', () => {
  const failStep = journeysJob.slice(journeysJob.indexOf('- name: Fail Android journeys after unrecovered failure'));
  const install = failStep.indexOf('android-install-failure');
  const booted = failStep.indexOf('android-emulator-booted');
  const selector = failStep.indexOf('::error::Android journeys failed on a selector or assertion miss.');
  assert.ok(install !== -1 && booted !== -1 && selector !== -1);
  assert.ok(install < selector && booted < selector, 'install/boot check must come before the selector-miss fallback');
  assert.match(failStep, /emulator install failure/);
  assert.equal((journeysJob.match(/android-emulator-booted/g) ?? []).length >= 3, true);
  const classify = journeysJob.slice(
    journeysJob.indexOf('- name: Classify Android journeys transport death'),
    journeysJob.indexOf('- name: Retry Android device journeys'),
  );
  assert.ok(classify.indexOf('android-emulator-booted') < classify.indexOf('attempt was a selector or assertion miss'));
});
