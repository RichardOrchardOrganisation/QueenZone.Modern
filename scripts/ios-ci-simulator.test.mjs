import { test } from 'node:test';
import assert from 'node:assert/strict';
import { chmodSync, mkdirSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { spawnSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';

const script = fileURLToPath(new URL('./ios-ci-simulator.sh', import.meta.url));
const source = readFileSync(script, 'utf8');

test('simulator helper never erases or shuts down every device', () => {
  assert.doesNotMatch(source, /simctl erase all/);
  assert.doesNotMatch(source, /simctl shutdown all/);
  assert.doesNotMatch(source, /simctl delete all/);
  assert.match(source, /simctl create/);
  assert.match(source, /simctl delete "\$\{udid\}"/);
});

function fakeXcrun(tmp) {
  const bin = path.join(tmp, 'xcrun');
  writeFileSync(
    bin,
    `#!/usr/bin/env bash
set -euo pipefail
echo "$*" >> "$FAKE_STATE/xcrun-args"
case "$*" in
  "simctl list devicetypes available")
    echo "iPhone 16 (com.apple.CoreSimulator.SimDeviceType.iPhone-16)"
    ;;
  "simctl list runtimes available")
    echo "iOS 26.1 (26.1 - 23B77) - com.apple.CoreSimulator.SimRuntime.iOS-26-1"
    ;;
  simctl\\ create\\ *)
    echo "AAAAAAAA-BBBB-CCCC-DDDD-EEEEEEEEEEEE"
    ;;
  "simctl boot AAAAAAAA-BBBB-CCCC-DDDD-EEEEEEEEEEEE"|"simctl bootstatus AAAAAAAA-BBBB-CCCC-DDDD-EEEEEEEEEEEE -b")
    ;;
  "simctl shutdown AAAAAAAA-BBBB-CCCC-DDDD-EEEEEEEEEEEE"|"simctl delete AAAAAAAA-BBBB-CCCC-DDDD-EEEEEEEEEEEE")
    ;;
  *)
    echo "unexpected xcrun $*" >&2
    exit 3
    ;;
esac
`,
  );
  chmodSync(bin, 0o755);
  return bin;
}

test('create boots a job-owned simulator and records its UDID', () => {
  const tmp = mkdtempSync(path.join(tmpdir(), 'qz-sim-'));
  try {
    const envFile = path.join(tmp, 'github-env');
    writeFileSync(envFile, '');
    const result = spawnSync('bash', [script, 'create'], {
      encoding: 'utf8',
      env: {
        ...process.env,
        GITHUB_ENV: envFile,
        GITHUB_RUN_ID: '99',
        GITHUB_JOB: 'mobile-ios-smoke',
        IOS_CI_XCRUN: fakeXcrun(tmp),
        FAKE_STATE: tmp,
      },
    });
    assert.equal(result.status, 0, result.stderr + result.stdout);
    const env = readFileSync(envFile, 'utf8');
    assert.match(env, /IOS_SIM_UDID=AAAAAAAA-BBBB-CCCC-DDDD-EEEEEEEEEEEE/);
    assert.match(env, /IOS_SIM_NAME=QueenZone-CI-iOS-99-mobile-ios-smoke/);
    assert.match(readFileSync(path.join(tmp, 'xcrun-args'), 'utf8'), /simctl create QueenZone-CI-iOS-99-mobile-ios-smoke/);
  } finally {
    rmSync(tmp, { recursive: true, force: true });
  }
});

test('cleanup only deletes the job UDID', () => {
  const tmp = mkdtempSync(path.join(tmpdir(), 'qz-sim-'));
  try {
    const result = spawnSync('bash', [script, 'cleanup'], {
      encoding: 'utf8',
      env: {
        ...process.env,
        IOS_SIM_UDID: 'AAAAAAAA-BBBB-CCCC-DDDD-EEEEEEEEEEEE',
        IOS_CI_XCRUN: fakeXcrun(tmp),
        FAKE_STATE: tmp,
      },
    });
    assert.equal(result.status, 0, result.stderr + result.stdout);
    const args = readFileSync(path.join(tmp, 'xcrun-args'), 'utf8');
    assert.match(args, /simctl shutdown AAAAAAAA-BBBB-CCCC-DDDD-EEEEEEEEEEEE/);
    assert.match(args, /simctl delete AAAAAAAA-BBBB-CCCC-DDDD-EEEEEEEEEEEE/);
    assert.doesNotMatch(args, / all/);
  } finally {
    rmSync(tmp, { recursive: true, force: true });
  }
});
