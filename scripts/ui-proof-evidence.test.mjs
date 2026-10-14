import { test } from 'node:test';
import assert from 'node:assert/strict';
import { discoverEvidence } from './ui-proof-evidence.mjs';

const sha = 'a'.repeat(40);
function fixture({ run = {}, artifact = {}, manifest = {}, jobs } = {}) {
  const calls = [];
  const data = {
    runs: [{ id: 10, path: '.github/workflows/ci.yml', event: 'pull_request', head_sha: sha, run_attempt: 1, ...run }],
    jobs: jobs || [{ id: 3, name: 'Web browser tests', conclusion: 'success', run_attempt: 1 }],
    artifacts: [{ id: 20, name: 'ui-proof-web-10', size_in_bytes: 100, expired: false, ...artifact }],
  };
  const github = { paginate: async method => data[method], rest: { actions: {
    listWorkflowRunsForRepo: 'runs', listJobsForWorkflowRun: 'jobs', listWorkflowRunArtifacts: 'artifacts',
    downloadArtifact: async params => { calls.push(params); return { data: Buffer.from('fixture') }; },
  } } };
  const decode = () => ({ headSha: sha, runId: '10', attempt: '1', job: 'web-browser-tests', entries: [{ feature: 'web.home.index', platform: 'web', fixtures: ['SmokeTests'] }], ...manifest });
  return { github, repo: { owner: 'o', repo: 'r' }, headSha: sha, root: '.', decode, calls };
}

test('verified artifact yields a canonical PR link', async () => {
  const evidence = await discoverEvidence(fixture());
  assert.equal(evidence.length, 1);
  assert.equal(evidence[0].url, 'https://github.com/o/r/actions/runs/10/artifacts/20');
});
test('stale, expired, foreign, failed and corrupt evidence cannot pass', async () => {
  for (const options of [
    { run: { head_sha: 'b'.repeat(40) } }, { run: { path: '.github/workflows/unrelated.yml' } },
    { run: { event: 'push' } }, { artifact: { expired: true } }, { artifact: { size_in_bytes: 0 } },
    { manifest: { headSha: 'b'.repeat(40) } }, { manifest: { runId: 999 } }, { manifest: { job: 'unrelated' } }, { manifest: { attempt: 9 } },
    { jobs: [{ name: 'Web browser tests', conclusion: 'failure', run_attempt: 1 }] },
    { jobs: [{ name: 'Unrelated', conclusion: 'success', run_attempt: 1 }] },
  ]) assert.deepEqual(await discoverEvidence(fixture(options)), [], JSON.stringify(options));
  const invalid = fixture(); invalid.decode = () => { throw new Error('corrupt zip'); };
  assert.deepEqual(await discoverEvidence(invalid), []);
});
test('gate-only rerun retains producer evidence but a newer failed producer invalidates it', async () => {
  assert.equal((await discoverEvidence(fixture({ run: { run_attempt: 2 } }))).length, 1);
  const options = { run: { run_attempt: 2 }, jobs: [
    { id: 1, name: 'Web browser tests', conclusion: 'success', run_attempt: 1 },
    { id: 2, name: 'Web browser tests', conclusion: 'failure', run_attempt: 2 },
  ] };
  assert.deepEqual(await discoverEvidence(fixture(options)), []);
});
test('gate-only rerun keeps evidence when GitHub carries the producer into the new attempt', async () => {
  const carried = { run: { run_attempt: 2 }, jobs: [
    { id: 1, name: 'Web browser tests', conclusion: 'success', run_attempt: 1, started_at: '2026-10-14T07:15:43Z' },
    { id: 2, name: 'Web browser tests', conclusion: 'success', run_attempt: 2, started_at: '2026-10-14T07:15:43Z' },
  ] };
  assert.equal((await discoverEvidence(fixture(carried))).length, 1);
  const rerun = { run: { run_attempt: 2 }, jobs: [
    { id: 1, name: 'Web browser tests', conclusion: 'success', run_attempt: 1, started_at: '2026-10-14T07:15:43Z' },
    { id: 2, name: 'Web browser tests', conclusion: 'success', run_attempt: 2, started_at: '2026-10-14T07:40:00Z' },
  ] };
  assert.deepEqual(await discoverEvidence(fixture(rerun)), []);
});
