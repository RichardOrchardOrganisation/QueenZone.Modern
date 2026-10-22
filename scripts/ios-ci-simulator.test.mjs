import { test } from 'node:test';
import assert from 'node:assert/strict';
import { chmodSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { spawnSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';

const script = fileURLToPath(new URL('./ios-ci-simulator.sh', import.meta.url));
const source = readFileSync(script, 'utf8');

const runtimesJson = JSON.stringify({
  runtimes: [
    {
      identifier: 'com.apple.CoreSimulator.SimRuntime.iOS-18-4',
      name: 'iOS 18.4',
      version: '18.4',
      isAvailable: true,
      supportedDeviceTypes: [
        { identifier: 'com.apple.CoreSimulator.SimDeviceType.iPhone-16', name: 'iPhone 16' },
      ],
    },
    {
      identifier: 'com.apple.CoreSimulator.SimRuntime.iOS-26-1',
      name: 'iOS 26.1',
      version: '26.1',
      isAvailable: true,
      supportedDeviceTypes: [
        { identifier: 'com.apple.CoreSimulator.SimDeviceType.iPhone-16', name: 'iPhone 16' },
      ],
    },
  ],
});

const deviceTypesJson = JSON.stringify({
  devicetypes: [
    {
      identifier: 'com.apple.CoreSimulator.SimDeviceType.iPhone-16',
      name: 'iPhone 16',
      productFamily: 'iPhone',
    },
  ],
});

test('simulator helper never erases or shuts down every device', () => {
  assert.doesNotMatch(source, /simctl erase all/);
  assert.doesNotMatch(source, /simctl shutdown all/);
  assert.doesNotMatch(source, /simctl delete all/);
  assert.doesNotMatch(source, /simctl list runtimes available/);
  assert.match(source, /simctl list -j/);
  assert.match(source, /simctl create/);
  assert.match(source, /simctl delete "\$\{udid\}"/);
});

function fakeXcrun(tmp, { bootstatusFails = false } = {}) {
  const bootstatus = bootstatusFails
    ? `"simctl bootstatus AAAAAAAA-BBBB-CCCC-DDDD-EEEEEEEEEEEE -b")
    echo "bootstatus failed" >&2
    exit 1
    ;;
  "simctl boot AAAAAAAA-BBBB-CCCC-DDDD-EEEEEEEEEEEE")
    ;;`
    : `"simctl boot AAAAAAAA-BBBB-CCCC-DDDD-EEEEEEEEEEEE"|"simctl bootstatus AAAAAAAA-BBBB-CCCC-DDDD-EEEEEEEEEEEE -b")
    ;;`;
  const bin = path.join(tmp, 'xcrun');
  writeFileSync(
    bin,
    `#!/usr/bin/env bash
set -euo pipefail
echo "$*" >> "$FAKE_STATE/xcrun-args"
case "$*" in
  "simctl list -j runtimes")
    cat <<'JSON'
${runtimesJson}
JSON
    ;;
  "simctl list -j devicetypes")
    cat <<'JSON'
${deviceTypesJson}
JSON
    ;;
  "--sdk iphonesimulator --show-sdk-version")
    echo "26.1"
    ;;
  simctl\\ create\\ *)
    echo "AAAAAAAA-BBBB-CCCC-DDDD-EEEEEEEEEEEE"
    ;;
  ${bootstatus}
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
    const args = readFileSync(path.join(tmp, 'xcrun-args'), 'utf8');
    assert.match(args, /simctl list -j runtimes/);
    assert.match(args, /simctl create QueenZone-CI-iOS-99-mobile-ios-smoke com.apple.CoreSimulator.SimDeviceType.iPhone-16 com.apple.CoreSimulator.SimRuntime.iOS-26-1/);
  } finally {
    rmSync(tmp, { recursive: true, force: true });
  }
});

test('create prefers the iOS runtime that matches the selected Xcode SDK', () => {
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
        IOS_CI_SIM_SDK: '18.4',
        FAKE_STATE: tmp,
      },
    });
    assert.equal(result.status, 0, result.stderr + result.stdout);
    assert.match(
      readFileSync(path.join(tmp, 'xcrun-args'), 'utf8'),
      /simctl create .* com.apple.CoreSimulator.SimRuntime.iOS-18-4/,
    );
  } finally {
    rmSync(tmp, { recursive: true, force: true });
  }
});

test('create writes IOS_SIM_UDID before bootstatus so always() cleanup can delete after a boot failure', () => {
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
        IOS_CI_XCRUN: fakeXcrun(tmp, { bootstatusFails: true }),
        FAKE_STATE: tmp,
      },
    });
    assert.notEqual(result.status, 0);
    assert.match(readFileSync(envFile, 'utf8'), /IOS_SIM_UDID=AAAAAAAA-BBBB-CCCC-DDDD-EEEEEEEEEEEE/);
    assert.match(source, /echo "IOS_SIM_UDID=\$\{udid\}" >> "\$\{GITHUB_ENV\}"[\s\S]*simctl bootstatus/);
  } finally {
    rmSync(tmp, { recursive: true, force: true });
  }
});

test('create fails when every iOS runtime is unavailable', () => {
  const tmp = mkdtempSync(path.join(tmpdir(), 'qz-sim-'));
  try {
    const envFile = path.join(tmp, 'github-env');
    writeFileSync(envFile, '');
    const bin = path.join(tmp, 'xcrun');
    writeFileSync(
      bin,
      `#!/usr/bin/env bash
set -euo pipefail
case "$*" in
  "simctl list -j runtimes")
    echo '{"runtimes":[{"identifier":"com.apple.CoreSimulator.SimRuntime.iOS-26-1","name":"iOS 26.1","version":"26.1","isAvailable":false}]}'
    ;;
  "simctl list -j devicetypes")
    echo '{"devicetypes":[]}'
    ;;
  "simctl list devices"|"simctl list runtimes")
    ;;
  *)
    echo "unexpected xcrun $*" >&2
    exit 3
    ;;
esac
`,
    );
    chmodSync(bin, 0o755);
    const result = spawnSync('bash', [script, 'create'], {
      encoding: 'utf8',
      env: {
        ...process.env,
        GITHUB_ENV: envFile,
        IOS_CI_XCRUN: bin,
      },
    });
    assert.notEqual(result.status, 0);
    assert.match(result.stderr + result.stdout, /No available iPhone Simulator runtime|No available iOS Simulator runtime/);
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
