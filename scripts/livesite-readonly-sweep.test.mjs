import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { spawnSync } from 'node:child_process';
import { test } from 'node:test';
import { fileURLToPath } from 'node:url';
import path from 'node:path';

const repoRoot = fileURLToPath(new URL('..', import.meta.url));
const workflow = readFileSync(
  path.join(repoRoot, '.github/workflows/livesite-readonly-sweep.yml'),
  'utf8',
);
const deploy = readFileSync(
  path.join(repoRoot, '.github/workflows/deploy.yml'),
  'utf8',
);

test('Wait-DeployQuiet self-test covers active, recent, quiet, and bound-hit', () => {
  const result = spawnSync('bash', [path.join(repoRoot, 'scripts/Wait-DeployQuiet.sh'), '--self-test'], {
    encoding: 'utf8',
    cwd: repoRoot,
  });
  assert.equal(result.error, undefined, result.stderr);
  assert.equal(result.status, 0, result.stderr + result.stdout);
  assert.match(result.stderr, /PASS active/);
  assert.match(result.stderr, /PASS recent/);
  assert.match(result.stderr, /PASS quiet/);
  assert.match(result.stderr, /PASS bound-hit-stdout/);
  assert.match(result.stderr, /PASS pending/);
  assert.match(result.stderr, /PASS requested/);
  assert.match(result.stderr, /Wait-DeployQuiet self-test passed/);
});

test('Wait-DeployQuiet curl has a bound and retries, and treats any unfinished status as active', () => {
  const script = readFileSync(path.join(repoRoot, 'scripts/Wait-DeployQuiet.sh'), 'utf8');
  assert.match(script, /--max-time 30/);
  assert.match(script, /--retry 2/);
  assert.match(script, /\.status != "completed"/);
});

test('live-site sweep uses a deploy-quiet gate instead of sharing deploy.yml concurrency', () => {
  assert.match(workflow, /cron: "23 6 \* \* \*"/);
  assert.match(workflow, /deploy-quiet-gate:/);
  assert.match(workflow, /ignore_deploy_gate/);
  assert.match(workflow, /needs\.deploy-quiet-gate\.outputs\.run == 'true'/);
  assert.match(workflow, /actions: read/);
  assert.match(workflow, /Wait-DeployQuiet\.sh/);
  assert.match(workflow, /title=Production \/warmup not ready/);
  assert.match(workflow, /group: livesite-readonly-sweep/);
  assert.doesNotMatch(workflow, /prod-site/);
  assert.doesNotMatch(workflow, /group: deploy-/);
  assert.match(workflow, /Do not share a concurrency group with deploy\.yml/);
});

test('deploy.yml concurrency is unchanged by the live-site sweep lock', () => {
  assert.match(deploy, /group: deploy-\$\{\{ github\.ref \}\}/);
  assert.doesNotMatch(deploy, /livesite-readonly-sweep/);
  assert.doesNotMatch(deploy, /deploy-quiet-gate/);
});
