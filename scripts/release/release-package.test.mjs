import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import test from 'node:test';
import { seal, verify, validateInputs } from './release-package.mjs';
const bytes = Buffer.from('exact release archive bytes');
const env = { RELEASE_SHA: 'a'.repeat(40), GITHUB_RUN_ID: '123', GITHUB_RUN_ATTEMPT: '2', ARTIFACT_ID: '456' };
const manifest = seal(bytes, env);
env.PACKAGE_SHA256 = manifest.package_sha256;
test('accepts bytes bound to exact commit, run, attempt and producer digest', () => assert.deepEqual(verify(bytes, manifest, env), manifest));
for (const field of ['RELEASE_SHA', 'GITHUB_RUN_ID', 'GITHUB_RUN_ATTEMPT', 'ARTIFACT_ID', 'PACKAGE_SHA256']) {
  test(`missing ${field} fails closed before download`, () => assert.throws(() => validateInputs({ ...env, [field]: '' })));
}
for (const [field, value] of [['release_sha', 'b'.repeat(40)], ['run_id', '124'], ['run_attempt', '1'], ['version', 2], ['package_sha256', 'c'.repeat(64)]]) {
  test(`rejects manifest ${field} mismatch`, () => assert.throws(() => verify(bytes, { ...manifest, [field]: value }, env)));
}
test('rejects corrupt bytes', () => assert.throws(() => verify(Buffer.from('corrupt'), manifest, env)));
test('rejects rehashed archive and manifest against immutable producer output', () => {
  const changed = Buffer.from('replaced');
  assert.throws(() => verify(changed, seal(changed, env), env));
});
test('rejects missing, extra and empty manifest/archive content', () => {
  assert.throws(() => verify(bytes, null, env));
  assert.throws(() => verify(bytes, { ...manifest, extra: true }, env));
  assert.throws(() => verify(Buffer.alloc(0), manifest, env));
});
const workflow = readFileSync(new URL('../../.github/workflows/deploy.yml', import.meta.url), 'utf8');
const job = name => workflow.split(`\n  ${name}:`)[1]?.split(/\n  [a-z][\w-]*:/)[0];
test('failed preparation blocks every production mutator before credentials', () => {
  for (const name of ['migrate', 'configure-app-settings', 'deploy']) {
    const body = job(name);
    assert.match(body, /needs: \[[^\]]*prepare-release/);
    assert.match(body, /needs\.prepare-release\.result == 'success'/);
    assert.ok(body.indexOf('Acquire and verify release package') < body.indexOf('Fetch Bitwarden'));
    assert.ok(body.indexOf('Recheck production proofs before credentials') < body.indexOf('Fetch Bitwarden'));
    assert.match(body, /if: needs.check-gates.result == 'success'/);
  }
});
test('preparation builds exact checkout with no production environment or identity', () => {
  const body = job('prepare-release');
  assert.doesNotMatch(body, /environment:|id-token:|Bitwarden|ARM_CLIENT_ID|publish-profile/);
  assert.match(body, /git rev-parse 'HEAD\^\{commit\}'/);
  assert.match(body, /OverrideGitCommitShort/);
  assert.match(body, /release-package-\$\{\{ github.run_id \}\}-\$\{\{ github.run_attempt \}\}/);
  assert.doesNotMatch(workflow, /Download publish artifact from CI run|name: web-publish-/);
});
test('production serialization and existing environment boundaries remain', () => {
  assert.match(workflow, /group: production-release/);
  assert.match(workflow, /cancel-in-progress: false/);
  assert.match(job('migrate'), /environment: prod-release-gated/);
  assert.match(job('configure-app-settings'), /environment: prod-deploy-gated/);
  assert.match(job('deploy'), /name: prod-release-gated/);
});
import { mkdtempSync, mkdirSync, writeFileSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { spawnSync } from 'node:child_process';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
test('CLI seals and verifies on disk, then rejects missing and altered archives', () => {
  const root = mkdtempSync(path.join(tmpdir(), 'qz-package-test-'));
  try {
    mkdirSync(path.join(root, 'release-input'));
    const archive = path.join(root, 'release-input/release-package.tar.gz');
    writeFileSync(archive, bytes);
    const cli = fileURLToPath(new URL('./release-package.mjs', import.meta.url));
    const run = command => spawnSync(process.execPath, [cli, command], {
      cwd: root, encoding: 'utf8', env: { ...process.env, ...env, GITHUB_OUTPUT: path.join(root, 'outputs') },
    });
    assert.equal(run('create').status, 0);
    assert.equal(run('verify').status, 0);
    writeFileSync(archive, 'altered bytes');
    assert.notEqual(run('verify').status, 0);
    rmSync(archive);
    assert.notEqual(run('verify').status, 0);
  } finally { rmSync(root, { recursive: true, force: true }); }
});
