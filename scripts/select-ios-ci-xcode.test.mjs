import { test } from 'node:test';
import assert from 'node:assert/strict';
import { chmodSync, existsSync, mkdirSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { spawnSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';

const script = fileURLToPath(new URL('./select-ios-ci-xcode.sh', import.meta.url));
const source = readFileSync(script, 'utf8');

function setup() {
  const tmp = mkdtempSync(path.join(tmpdir(), 'qz-xcode-'));
  const apps = path.join(tmp, 'Applications');
  const bin = path.join(tmp, 'bin');
  mkdirSync(path.join(apps, 'Xcode.app', 'Contents', 'Developer'), { recursive: true });
  mkdirSync(path.join(apps, 'Xcode_26.4.app', 'Contents', 'Developer'), { recursive: true });
  mkdirSync(bin);
  writeFileSync(path.join(bin, 'xcode-select'), '#!/usr/bin/env bash\necho /old/Developer\n');
  writeFileSync(path.join(bin, 'xcodebuild'), '#!/usr/bin/env bash\necho Xcode 26.4\n');
  writeFileSync(
    path.join(bin, 'sudo'),
    '#!/usr/bin/env bash\necho "$*" >> "$FAKE_STATE/sudo-args"\n',
  );
  chmodSync(path.join(bin, 'xcode-select'), 0o755);
  chmodSync(path.join(bin, 'xcodebuild'), 0o755);
  chmodSync(path.join(bin, 'sudo'), 0o755);
  return { tmp, apps, bin, cleanup: () => rmSync(tmp, { recursive: true, force: true }) };
}

function runSelect({ runnerEnvironment, apps, bin, tmp }) {
  const envFile = path.join(tmp, 'github-env');
  writeFileSync(envFile, '');
  const result = spawnSync('bash', [script], {
    encoding: 'utf8',
    env: {
      PATH: `${bin}:${process.env.PATH}`,
      GITHUB_ENV: envFile,
      IOS_CI_XCODE_APPS_DIR: apps,
      IOS_CI_XCODE_SELECT: path.join(bin, 'xcode-select'),
      IOS_CI_XCODEBUILD: path.join(bin, 'xcodebuild'),
      IOS_CI_SUDO: path.join(bin, 'sudo'),
      RUNNER_ENVIRONMENT: runnerEnvironment,
      FAKE_STATE: tmp,
    },
  });
  return { result, envFile };
}

test('script never issues a global sudo xcode-select without a hosted guard', () => {
  assert.match(source, /runner_environment/);
  assert.match(source, /self-hosted/);
  assert.match(source, /DEVELOPER_DIR/);
});

test('self-hosted jobs set DEVELOPER_DIR and do not sudo xcode-select', () => {
  const s = setup();
  try {
    const { result, envFile } = runSelect({
      runnerEnvironment: 'self-hosted',
      apps: s.apps,
      bin: s.bin,
      tmp: s.tmp,
    });
    assert.equal(result.status, 0, result.stderr + result.stdout);
    assert.match(readFileSync(envFile, 'utf8'), /DEVELOPER_DIR=.*Xcode_26\.4\.app\/Contents\/Developer/);
    assert.equal(existsSync(path.join(s.tmp, 'sudo-args')), false);
    assert.match(result.stdout, /leaving it unchanged/);
  } finally {
    s.cleanup();
  }
});

test('hosted jobs may sudo xcode-select after setting DEVELOPER_DIR', () => {
  const s = setup();
  try {
    const { result, envFile } = runSelect({
      runnerEnvironment: 'github-hosted',
      apps: s.apps,
      bin: s.bin,
      tmp: s.tmp,
    });
    assert.equal(result.status, 0, result.stderr + result.stdout);
    assert.match(readFileSync(envFile, 'utf8'), /DEVELOPER_DIR=.*Xcode_26\.4\.app/);
    assert.match(readFileSync(path.join(s.tmp, 'sudo-args'), 'utf8'), /xcode-select -s /);
  } finally {
    s.cleanup();
  }
});

test('falls back to Xcode.app when no versioned app exists', () => {
  const s = setup();
  try {
    rmSync(path.join(s.apps, 'Xcode_26.4.app'), { recursive: true, force: true });
    const { result, envFile } = runSelect({
      runnerEnvironment: 'self-hosted',
      apps: s.apps,
      bin: s.bin,
      tmp: s.tmp,
    });
    assert.equal(result.status, 0, result.stderr + result.stdout);
    assert.match(readFileSync(envFile, 'utf8'), /Xcode\.app\/Contents\/Developer/);
  } finally {
    s.cleanup();
  }
});
