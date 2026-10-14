import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import test from 'node:test';
import { seal, verify } from './release-package.mjs';
const AsyncFunction = Object.getPrototypeOf(async function () {}).constructor;
function script(file, step) {
  const text = readFileSync(new URL(`../../.github/workflows/${file}`, import.meta.url), 'utf8');
  const lines = text.slice(text.indexOf(step)).split(/\r?\n/);
  const start = lines.findIndex(line => line.trim() === 'script: |') + 1;
  assert.ok(start > 0);
  const result = [];
  for (const line of lines.slice(start)) {
    if (line.trim() && !line.startsWith('            ')) break;
    result.push(line.slice(12));
  }
  return new AsyncFunction('github', 'context', 'core', result.join('\n'));
}
const context = { repo: { owner: 'owner', repo: 'repo' }, runId: 20, payload: { pull_request: { head: { ref: 'branch' } } } };
const core = { info() {}, warning() {} };
function api(artifacts, workflow = 'ci.yml') {
  const deleted = [];
  const github = {
    rest: { actions: {
      listArtifactsForRepo: 'all', listWorkflowRuns: 'runs', listWorkflowRunArtifacts: 'artifacts',
      async deleteArtifact({ artifact_id }) { deleted.push(artifact_id); artifacts.delete(artifact_id); },
    } },
    async paginate(method, args) {
      if (method === 'runs') {
        assert.equal(args.workflow_id, workflow);
        return [{ id: 10, run_number: 1 }, { id: 20, run_number: 2 }];
      }
      return [...artifacts.values()];
    },
  };
  return { github, deleted };
}
function fixtures() {
  const old = new Date(Date.now() - 10 * 3600000).toISOString();
  return new Map([
    [1, { id: 1, name: 'web-publish-38558303993', expired: false, created_at: old, size_in_bytes: 100 }],
    [2, { id: 2, name: 'release-package-123-1', expired: false, created_at: old, size_in_bytes: 100 }],
  ]);
}
test('actual eight-hour pruning script reproduces CI deletion race but cannot delete prepared release package', async () => {
  const artifacts = fixtures();
  const bytes = Buffer.from('prepared before approval wait');
  const env = { RELEASE_SHA: 'a'.repeat(40), GITHUB_RUN_ID: '123', GITHUB_RUN_ATTEMPT: '1', ARTIFACT_ID: '2' };
  const manifest = seal(bytes, env); env.PACKAGE_SHA256 = manifest.package_sha256;
  const { github, deleted } = api(artifacts);
  await script('prune-web-publish-artifacts.yml', 'name: Delete web-publish artifacts')(github, context, core);
  assert.deepEqual(deleted, [1]);
  assert.equal(artifacts.has(1), false); // Old release's selected artifact is gone.
  assert.equal(artifacts.has(2), true); // New release does not need the deleted CI zip.
  assert.deepEqual(verify(bytes, manifest, env), manifest);
});
test('actual superseded PR cleanup preserves release prefix even with adversarial mixed artifact listing', async () => {
  const artifacts = fixtures(); const { github, deleted } = api(artifacts);
  await script('prune-stale-pr-artifacts.yml', 'name: Delete artifacts from superseded')(github, context, core);
  assert.deepEqual(deleted, [1]);
  assert.equal(artifacts.has(2), true);
});
test('actual CI ephemeral cleanup only deletes own build/coverage artifacts', async () => {
  const artifacts = fixtures();
  artifacts.set(3, { id: 3, name: 'build-binaries-20' });
  artifacts.set(4, { id: 4, name: 'coverage-raw-20-shard-2' });
  const { github, deleted } = api(artifacts);
  await script('ci.yml', 'name: Delete build-binaries and coverage-raw')(github, context, core);
  assert.deepEqual(deleted, [3, 4]);
  assert.equal(artifacts.has(2), true);
});
