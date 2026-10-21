import { test } from 'node:test';
import assert from 'node:assert/strict';
import { chmodSync, existsSync, mkdirSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { spawnSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';

const script = fileURLToPath(new URL('./choose-ios-runner.sh', import.meta.url));
const hosted = '["macos-26"]';
const selfHosted = '["self-hosted","macOS","ARM64","ios-build"]';

function runner(overrides = {}) {
  return {
    id: 1,
    name: 'richards-mac-queenzone',
    status: 'online',
    busy: false,
    labels: [
      { name: 'self-hosted' },
      { name: 'macOS' },
      { name: 'ARM64' },
      { name: 'ios-build' },
      { name: 'queenzone' },
    ],
    ...overrides,
  };
}

function ghStub() {
  return `#!/usr/bin/env bash
set -euo pipefail
echo "$*" >> "$FAKE_STATE/gh-args"
if [[ "\${GH_FAIL:-}" == "1" ]]; then
  echo "api failed" >&2
  exit 2
fi
if [[ "$*" == *"/orgs/"* ]]; then
  printf '%s' "\${GH_ORG_PAGES:-}"
  exit 0
fi
printf '%s' "\${GH_PAGES:-}"
`;
}

function runChooser({ token = 'token', env = {}, pages = [], orgPages = [{ runners: [] }] } = {}) {
  const tmp = mkdtempSync(path.join(tmpdir(), 'qz-choose-ios-'));
  const output = path.join(tmp, 'github-output');
  writeFileSync(output, '');
  const bin = path.join(tmp, 'bin');
  mkdirSync(bin);
  writeFileSync(path.join(bin, 'gh'), ghStub());
  chmodSync(path.join(bin, 'gh'), 0o755);
  const result = spawnSync('bash', [script], {
    encoding: 'utf8',
    env: {
      PATH: `${bin}:${process.env.PATH}`,
      GITHUB_OUTPUT: output,
      GITHUB_REPOSITORY: 'example/queenzone',
      GH_TOKEN: token,
      FAKE_STATE: tmp,
      GH_PAGES: pages.map((page) => JSON.stringify(page)).join('\n'),
      GH_ORG_PAGES: orgPages.map((page) => JSON.stringify(page)).join('\n'),
      ...env,
    },
  });
  const outputs = Object.fromEntries(
    readFileSync(output, 'utf8')
      .trim()
      .split('\n')
      .filter(Boolean)
      .map((line) => {
        const index = line.indexOf('=');
        return [line.slice(0, index), line.slice(index + 1)];
      }),
  );
  const ghArgsPath = path.join(tmp, 'gh-args');
  const ghArgs = existsSync(ghArgsPath) ? readFileSync(ghArgsPath, 'utf8') : '';
  rmSync(tmp, { recursive: true, force: true });
  return { result, outputs, ghArgs };
}

function lastAssignment(outputs, key) {
  return outputs[key];
}

test('fails closed to hosted macos-26 when the runner-list token is missing', () => {
  const { result, outputs } = runChooser({ token: '' });
  assert.equal(result.status, 0, result.stderr);
  assert.equal(lastAssignment(outputs, 'runs_on'), hosted);
  assert.equal(lastAssignment(outputs, 'runner_class'), 'github-hosted macos-26');
  assert.match(result.stdout, /Runner-list token is unavailable/);
});

test('FORCE_HOSTED skips an idle Mac', () => {
  const { result, outputs, ghArgs } = runChooser({
    env: { FORCE_HOSTED: 'true' },
    pages: [{ runners: [runner()] }],
  });
  assert.equal(result.status, 0, result.stderr);
  assert.equal(lastAssignment(outputs, 'runs_on'), hosted);
  assert.equal(lastAssignment(outputs, 'runs_on_secondary'), hosted);
  assert.match(result.stdout, /FORCE_HOSTED=true/);
  assert.equal(ghArgs, '');
});

test('sibling Android claim skips an idle Mac', () => {
  const { result, outputs, ghArgs } = runChooser({
    env: { MAC_CLAIMED_BY_SIBLING: 'true' },
    pages: [{ runners: [runner()] }],
  });
  assert.equal(result.status, 0, result.stderr);
  assert.equal(lastAssignment(outputs, 'runs_on'), hosted);
  assert.equal(lastAssignment(outputs, 'runs_on_secondary'), hosted);
  assert.match(result.stdout, /sibling Android job/);
  assert.equal(ghArgs, '');
});

test('API failure fails open to hosted macos-26 and logs both probes', () => {
  const { result, outputs, ghArgs } = runChooser({ env: { GH_FAIL: '1' } });
  assert.equal(result.status, 0, result.stderr);
  assert.equal(lastAssignment(outputs, 'runs_on'), hosted);
  assert.match(result.stdout, /Probe repo GET \/repos\/example\/queenzone\/actions\/runners/);
  assert.match(result.stdout, /Probe org GET \/orgs\/example\/actions\/runners/);
  assert.match(result.stdout, /No runners parsed from repo or org APIs/);
  assert.match(ghArgs, /\/repos\/example\/queenzone\/actions\/runners/);
  assert.match(ghArgs, /\/orgs\/example\/actions\/runners/);
});

test('online idle ios-build runner is selected', () => {
  const { result, outputs } = runChooser({
    pages: [{ runners: [runner()] }],
  });
  assert.equal(result.status, 0, result.stderr + result.stdout);
  assert.equal(lastAssignment(outputs, 'runs_on'), selfHosted);
  assert.equal(lastAssignment(outputs, 'runs_on_secondary'), hosted);
  assert.equal(lastAssignment(outputs, 'runner_class'), 'self-hosted ios-build');
  assert.match(result.stdout, /An online, idle ios-build runner is available \(richards-mac-queenzone\)/);
});

test('org-only idle ios-build runner is selected when the repo list is empty', () => {
  const { result, outputs, ghArgs } = runChooser({
    pages: [{ runners: [] }],
    orgPages: [{ runners: [runner({ id: 9 })] }],
  });
  assert.equal(result.status, 0, result.stderr + result.stdout);
  assert.equal(lastAssignment(outputs, 'runs_on'), selfHosted);
  assert.match(ghArgs, /\/repos\/example\/queenzone\/actions\/runners/);
  assert.match(ghArgs, /\/orgs\/example\/actions\/runners/);
  assert.match(result.stdout, /Probe org GET \/orgs\/example\/actions\/runners\?per_page=100 exit=0/);
});

test('busy or offline ios-build runners stay on hosted macos-26 and log why', () => {
  for (const overrides of [{ busy: true }, { status: 'offline' }]) {
    const { result, outputs } = runChooser({
      pages: [{ runners: [runner(overrides)] }],
    });
    assert.equal(result.status, 0, result.stderr + result.stdout);
    assert.equal(lastAssignment(outputs, 'runs_on'), hosted, JSON.stringify(overrides));
    assert.match(result.stdout, /reject=/);
    assert.match(result.stdout, /none online, idle, and labeled ios-build/);
  }
});

test('paginated runner lists still see an idle ios-build runner', () => {
  const { result, outputs } = runChooser({
    pages: [{ runners: [] }, { runners: [runner()] }],
  });
  assert.equal(result.status, 0, result.stderr + result.stdout);
  assert.equal(lastAssignment(outputs, 'runs_on'), selfHosted);
});
