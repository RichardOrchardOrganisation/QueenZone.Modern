import assert from 'node:assert/strict';
import { execFileSync, spawnSync } from 'node:child_process';
import { mkdtempSync, mkdirSync, readFileSync, writeFileSync } from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { test } from 'node:test';
import { validateDevRedeploy } from './validate-dev-redeploy.mjs';

const root = fileURLToPath(new URL('../..', import.meta.url));
const sha = 'a'.repeat(40);
const valid = { FORCE_REDEPLOY: 'true', EXPECTED_MAIN_SHA: sha,
  GITHUB_EVENT_NAME: 'workflow_dispatch', GITHUB_REF: 'refs/heads/main',
  GITHUB_SHA: sha, CHECKOUT_SHA: sha, CURRENT_MAIN_SHA: sha };
const workflow = readFileSync(path.join(root, '.github/workflows/deploy-dev.yml'), 'utf8').replace(/\r\n/g, '\n');

test('explicit manual redeploy accepts only the exact expected main checkout', () => {
  assert.equal(validateDevRedeploy(valid), true);
});

test('ordinary dispatch and automatic push remain opt-out', () => {
  for (const event of ['workflow_dispatch', 'push']) {
    assert.equal(validateDevRedeploy({ GITHUB_EVENT_NAME: event }), false);
    assert.equal(validateDevRedeploy({ GITHUB_EVENT_NAME: event, FORCE_REDEPLOY: 'false' }), false);
  }
});

for (const key of ['GITHUB_SHA', 'CHECKOUT_SHA', 'CURRENT_MAIN_SHA']) {
  test(`rejects stale or mismatched ${key}`, () => {
    assert.throws(() => validateDevRedeploy({ ...valid, [key]: 'b'.repeat(40) }), /does not match/);
    assert.throws(() => validateDevRedeploy({ ...valid, [key]: '' }), /does not match/);
  });
}

test('rejects absent, abbreviated, malformed, and injected expected SHAs', () => {
  for (const expected of ['', 'a'.repeat(7), 'z'.repeat(40), 'A'.repeat(40), `${sha}\ncode=true`, '$(echo unsafe)']) {
    assert.throws(() => validateDevRedeploy({ ...valid, EXPECTED_MAIN_SHA: expected }), /full lowercase/);
  }
});

test('rejects forced pushes, non-main refs, invalid flags, and misleading unused expectations', () => {
  assert.throws(() => validateDevRedeploy({ ...valid, GITHUB_EVENT_NAME: 'push' }), /workflow_dispatch/);
  for (const ref of ['refs/tags/v1', 'refs/heads/other']) {
    assert.throws(() => validateDevRedeploy({ ...valid, GITHUB_REF: ref }), /on main/);
  }
  assert.throws(() => validateDevRedeploy({ ...valid, FORCE_REDEPLOY: 'yes' }), /true or false/);
  assert.throws(() => validateDevRedeploy({ ...valid, FORCE_REDEPLOY: 'false' }), /requires/);
});

test('CLI emits an enabling output only after successful validation', () => {
  const dir = mkdtempSync(path.join(os.tmpdir(), 'qz-redeploy-cli-'));
  const output = path.join(dir, 'output');
  writeFileSync(output, '');
  const script = path.join(root, 'scripts/release/validate-dev-redeploy.mjs');
  const run = (values) => spawnSync(process.execPath, [script], { encoding: 'utf8',
    env: { ...process.env, ...valid, GITHUB_OUTPUT: output, ...values } });
  const failed = run({ CURRENT_MAIN_SHA: 'b'.repeat(40) });
  assert.equal(failed.status, 1);
  assert.equal(readFileSync(output, 'utf8'), '');
  const passed = run({});
  assert.equal(passed.status, 0, passed.stderr);
  assert.equal(readFileSync(output, 'utf8'), 'force_redeploy=true\n');
});

function classifyFixture(changedPath, force) {
  const dir = mkdtempSync(path.join(os.tmpdir(), 'qz-redeploy-range-'));
  const git = (...args) => execFileSync('git', args, { cwd: dir, encoding: 'utf8', stdio: ['ignore', 'pipe', 'pipe'] }).trim();
  git('init', '-q');
  git('config', 'user.name', 'Redeploy fixture');
  git('config', 'user.email', 'fixture@example.invalid');
  mkdirSync(path.join(dir, 'scripts'), { recursive: true });
  for (const name of ['Resolve-DeployChangeRange.sh', 'classify-pipeline-changes.sh']) {
    writeFileSync(path.join(dir, 'scripts', name), readFileSync(path.join(root, 'scripts', name), 'utf8').replace(/\r\n/g, '\n'));
  }
  git('add', '.');
  git('commit', '-qm', 'base');
  mkdirSync(path.dirname(path.join(dir, changedPath)), { recursive: true });
  writeFileSync(path.join(dir, changedPath), 'fixture\n');
  git('add', '.');
  git('commit', '-qm', 'change');
  const head = git('rev-parse', 'HEAD');
  const step = workflow.split('      - name: Check for code changes\n')[1].split('\n  # Finds')[0];
  const body = step.split('        run: |\n')[1].split('\n').map(line => line.startsWith('          ') ? line.slice(10) : line).join('\n');
  const output = path.join(dir, 'output');
  const result = spawnSync('bash', ['-e', '-o', 'pipefail', '-c', body], { cwd: dir, encoding: 'utf8',
    env: { ...process.env, EVENT_NAME: 'workflow_dispatch', REF_TYPE: 'branch', BEFORE: '', SHA: head,
      FORCE_REDEPLOY: String(force), GITHUB_OUTPUT: output.replaceAll('\\', '/') } });
  assert.equal(result.status, 0, result.stderr);
  return Object.fromEntries(readFileSync(output, 'utf8').trim().split(/\r?\n/).map(line => line.split('=')));
}

test('actual workflow keeps ordinary docs dispatch skipped and enables explicit redeploy without migrations', () => {
  const normal = classifyFixture('docs/example.md', false);
  assert.equal(normal.code, 'false');
  assert.equal(normal.migrations, 'false');
  const forced = classifyFixture('docs/example.md', true);
  assert.deepEqual(forced, { ...normal, code: 'true' });
});

test('actual workflow preserves positive migration classification during forced redeploy', () => {
  const normal = classifyFixture('src/QueenZone.Data/Migrations/fixture.cs', false);
  const forced = classifyFixture('src/QueenZone.Data/Migrations/fixture.cs', true);
  assert.equal(normal.migrations, 'true');
  assert.deepEqual(forced, normal);
});

test('guard runs before Azure login and receives inputs through environment variables', () => {
  assert.ok(workflow.indexOf('node scripts/release/validate-dev-redeploy.mjs') < workflow.indexOf('- name: Azure login'));
  const guard = workflow.split('      - name: Validate explicit DEV redeploy')[1].split('      - name: Azure login')[0];
  assert.match(guard, /git ls-remote --exit-code origin refs\/heads\/main/);
  assert.doesNotMatch(guard.split('        run: |')[1], /\$\{\{.*inputs\./);
  assert.match(workflow, /FORCE_REDEPLOY: \$\{\{ steps\.redeploy\.outputs\.force_redeploy \}\}/);
  assert.match(workflow, /REQUIRE_EXACT_SHA: 'true'/);
});
