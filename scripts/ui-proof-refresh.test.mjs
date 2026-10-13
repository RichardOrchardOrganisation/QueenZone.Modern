import { test } from 'node:test';
import assert from 'node:assert/strict';
import { refreshUiProof } from './refresh-ui-proof.mjs';
import { checkEvidenceGate } from './ui-proof-gate.mjs';

const sha = 'a'.repeat(40);
function fixture(payload) {
  const calls = [];
  const pull = { number: 7, state: 'open', head: { sha }, body: '## Verification\nweb.home.index', labels: [] };
  const values = { pulls: [pull], files: [{ filename: 'src/QueenZone.Web/Pages/Index.cshtml' }], comments: [],
    runs: [{ id: 10, path: '.github/workflows/ci.yml', event: 'pull_request', head_sha: sha }],
    jobs: [{ id: 20, name: 'e2e-test', status: 'completed', conclusion: 'failure' }], commits: [{ sha }] };
  const paginate = async method => values[method];
  paginate.iterator = () => (async function* () { yield { data: values.files }; })();
  const github = { paginate, rest: {
    pulls: { get: async () => ({ data: pull }), list: 'pulls', listFiles: 'files' },
    repos: { compareCommitsWithBasehead: 'commits' },
    issues: { listComments: 'comments', createComment: async p => calls.push(['create', p]), updateComment: async p => calls.push(['update', p]) },
    actions: { listWorkflowRunsForRepo: 'runs', listJobsForWorkflowRun: 'jobs', reRunJobForWorkflowRun: async p => calls.push(['rerun', p]) },
  } };
  return { github, context: { repo: { owner: 'o', repo: 'r' }, payload }, core: { info: () => {}, setFailed: message => calls.push(['fail', message]) },
    root: '.', discover: async () => [{ feature: 'web.home.index', platform: 'web', url: 'https://github.com/o/r/actions/runs/10/artifacts/2' }], calls, pull, values };
}

test('device completion publishes links and refreshes only the cheap gate', async () => {
  const f = fixture({ workflow_run: { path: '.github/workflows/mobile-device-smoke.yml', head_sha: sha } });
  await refreshUiProof(f);
  assert.deepEqual(f.calls.map(c => c[0]), ['create', 'rerun']);
  assert.match(f.calls[0][1].body, /actions\/runs\/10\/artifacts\/2/);
  assert.equal(f.calls[1][1].job_id, 20);
});
test('CI completion publishes links without a rerun loop', async () => {
  const f = fixture({ workflow_run: { path: '.github/workflows/ci.yml', head_sha: sha } });
  await refreshUiProof(f);
  assert.deepEqual(f.calls.map(c => c[0]), ['create']);
  f.values.comments = [{ id: 8, user: { login: 'github-actions[bot]' }, body: f.calls[0][1].body }];
  f.calls.length = 0;
  await refreshUiProof(f);
  assert.deepEqual(f.calls, []);
});
test('waiver removal refreshes an already successful gate', async () => {
  const f = fixture({ action: 'unlabeled', pull_request: { number: 7 } });
  f.values.jobs[0].conclusion = 'success';
  await refreshUiProof(f);
  assert.deepEqual(f.calls.map(c => c[0]), ['create', 'rerun']);
});
test('old completions and head changes never publish as current proof', async () => {
  const old = fixture({ workflow_run: { path: '.github/workflows/ci.yml', head_sha: 'b'.repeat(40) } });
  await refreshUiProof(old);
  assert.deepEqual(old.calls, []);
  const changed = fixture({ pull_request: { number: 7 } });
  let reads = 0;
  changed.github.rest.pulls.get = async () => ({ data: ++reads >= 3 ? { ...changed.pull, head: { sha: 'b'.repeat(40) } } : changed.pull });
  await refreshUiProof(changed);
  assert.deepEqual(changed.calls, []);
});
test('merge queue evaluates constituent PRs and fails when identity is unavailable', async () => {
  const f = fixture({ merge_group: { base_sha: 'b'.repeat(40), head_sha: 'c'.repeat(40) } });
  f.pull.labels = [{ name: 'no-ui-verification' }];
  f.pull.body = 'Verification-skip-reason: reviewed fixture-only change';
  await checkEvidenceGate({ ...f, producerResult: 'success' });
  assert.deepEqual(f.calls, []);
  f.values.commits = [];
  await checkEvidenceGate({ ...f, producerResult: 'success' });
  assert.match(f.calls[0][1], /Cannot resolve merge-group/);
});
test('a waiver never masks browser failures; stale PR jobs cannot pass', async () => {
  const f = fixture({ pull_request: { number: 7, head: { sha: 'b'.repeat(40) } } });
  await checkEvidenceGate({ ...f, producerResult: 'failure' });
  assert.match(f.calls[0][1], /browser tests did not pass/);
  f.calls.length = 0;
  await checkEvidenceGate({ ...f, producerResult: 'success' });
  assert.match(f.calls[0][1], /head changed/);
});
