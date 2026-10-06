import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { classifyDeployment, queueCandidates, createDeploymentMetadataClient, monitorDeployments } from './deployment-queue.mjs';
import { planFilings } from './core.mjs';
import { loadFilerFiles, repoRootFrom } from './config.mjs';
import { buildMarker } from './templates.mjs';

const now = new Date('2026-10-06T06:00:00Z');
const repository = 'example/project';
const run = { id: 123, status: 'queued', created_at: '2026-10-05T13:52:44Z', updated_at: '2026-10-05T13:53:18Z',
  head_branch: 'main', path: '.github/workflows/deploy-dev.yml', run_attempt: 1 };
const jobs = [{ id: 1, status: 'completed', conclusion: 'success' },
  { id: 2, status: 'queued', started_at: '2026-10-05T13:53:18Z', runner_id: 0, runner_name: '' }];
const classify = (r = run, j = jobs, approvals = []) => classifyDeployment(r, j, approvals, now);

test('historical holder shape reports aged unassigned job without claiming its root cause', () => {
  assert.equal(classify().kind, 'aged-unassigned-job');
  const { candidates } = queueCandidates([{ run, jobs, approvals: [] }], now, repository);
  assert.equal(candidates.length, 1);
  assert.equal(candidates[0].count, 1);
  assert.match(candidates[0].evidence[0].text, /Root cause unproven/);
});

test('active, assigned, approval and terminal states are never stale blockers', () => {
  assert.equal(classify(run, [{ status: 'in_progress' }]).kind, 'active-job');
  assert.equal(classify(run, [{ status: 'queued', runner_id: 17 }]).kind, 'assigned-job');
  assert.equal(classify(run, jobs, [{}]).kind, 'environment-approval');
  assert.equal(classify({ ...run, status: 'completed' }).kind, 'completed');
  assert.equal(classify({ ...run, status: 'waiting' }).kind, 'unknown-wait');
});

test('age starts at current queued phase, not old run creation; threshold is inclusive', () => {
  for (const [minutes, expected] of [[59, 'normal-queue'], [60, 'aged-unassigned-job']]) {
    const started_at = new Date(now.getTime() - minutes * 60_000).toISOString();
    assert.equal(classify(run, [{ status: 'queued', started_at }]).kind, expected);
  }
  assert.equal(classify(run, [{ status: 'queued', started_at: '2099-01-01' }]).kind, 'unknown-age');
  assert.equal(classify({ ...run, updated_at: null }, [{ status: 'queued' }]).kind, 'unknown-age');
  assert.equal(classify({ ...run, updated_at: now.toISOString() }, [{ status: 'queued' }]).kind, 'normal-queue');
});

test('pending no-job successor is a possible concurrency wait, not another blocker', () => {
  const { observations, candidates } = queueCandidates([
    { run, jobs, approvals: [] },
    { run: { ...run, id: 124, status: 'pending', created_at: '2026-10-06T05:00:00Z' }, jobs: [], approvals: [] },
  ], now, repository);
  assert.equal(observations[1].kind, 'possible-concurrency-wait');
  assert.equal(candidates[0].count, 1);
  assert.equal(classify(run, []).kind, 'no-job-record');
  assert.equal(classify(run, [{ status: 'completed' }]).kind, 'run-status-lag');
});

function response(data, status = 200, link = '') {
  return { ok: status === 200, status, headers: new Headers({ link }), json: async () => data };
}

test('metadata requests are GET-only, bounded and refresh terminal state', async () => {
  const urls = [];
  const client = createDeploymentMetadataClient({ token: 'fake-test-token', repository, fetchImpl: async (url, init) => {
    urls.push(url); assert.equal(init.method, 'GET');
    assert.doesNotMatch(url, /logs|cancel|rerun|dispatch/);
    if (url.includes('/workflows/')) return response({ workflow_runs: [run] });
    if (url.includes('/jobs?')) return response({ jobs });
    if (url.endsWith('/pending_deployments')) return response([]);
    return response({ ...run, status: 'completed' });
  } });
  const records = await client.collect();
  assert.equal(records.length, 1);
  assert.equal(urls.length, 8);
  assert.equal(queueCandidates(records, now, repository).candidates.length, 0);
});

test('denied/truncated metadata aborts inspection instead of claiming a clean queue', async () => {
  for (const reply of [response({}, 403), response({}, 200, '<https://api.github.com/next>; rel="next"')]) {
    const client = createDeploymentMetadataClient({ token: 'fake', repository, fetchImpl: async () => reply });
    await assert.rejects(client.collect(), /unavailable|incomplete/);
  }
});

test('malformed pages, omitted jobs, oversized lists and changed attempts fail closed', async () => {
  for (const data of [{}, { workflow_runs: [], total_count: 1 },
    { workflow_runs: Array.from({ length: 21 }, (_, index) => ({ ...run, id: index + 1 })) }]) {
    const client = createDeploymentMetadataClient({ token: 'fake', repository, fetchImpl: async () => response(data) });
    await assert.rejects(client.collect(), /incomplete/);
  }
  for (const mode of ['jobs-missing', 'attempt-changed']) {
    const client = createDeploymentMetadataClient({ token: 'fake', repository, fetchImpl: async (url) => {
      if (url.includes('/workflows/')) return response({ workflow_runs: [run] });
      if (url.includes('/jobs?')) return response(mode === 'jobs-missing' ? {} : { jobs });
      if (url.endsWith('/pending_deployments')) return response([]);
      return response({ ...run, run_attempt: 2 });
    } });
    await assert.rejects(client.collect(), /incomplete/);
  }
});

test('collection failure never files a partial candidate set', async () => {
  const calls = [];
  await assert.rejects(monitorDeployments({ repository, now, dryRun: false,
    metadata: { collect: async () => { throw new Error('private upstream response'); } },
    github: { createIssue: async () => calls.push('write') }, stdout: () => calls.push('report'), summary: () => {} }));
  assert.deepEqual(calls, []);
});

test('dry-run report drops arbitrary metadata and calls no mutation', async () => {
  const lines = [];
  const github = { listIssuesByLabel: async () => [], listIssueComments: async () => [] };
  const result = await monitorDeployments({ repository, now, dryRun: true, github,
    metadata: { collect: async () => [{ run: { ...run, display_title: 'PRIVATE_PAYLOAD' },
      jobs: jobs.map((job) => ({ ...job, name: 'PRIVATE_PAYLOAD' })), approvals: [] }] },
    stdout: (line) => lines.push(line), summary: (line) => lines.push(line) });
  assert.equal(result.wrote, false);
  assert.equal(result.plan.create.length, 1);
  assert.doesNotMatch(lines.join('\n'), /PRIVATE_PAYLOAD|fake-test-token/);
});

test('stable key uses existing deduplication, cooldown and daily filing cap', () => {
  const { config, ignore, findingRules } = loadFilerFiles(repoRootFrom());
  const { candidates } = queueCandidates([{ run, jobs, approvals: [] }], now, repository);
  const options = { candidates, config, ignore, findingRules, now, loop: 'telemetry', maxIssues: 1 };
  const existing = { number: 99, state: 'open', labels: ['bug', 'from-telemetry'], user: 'github-actions[bot]',
    createdAt: now.toISOString(), updatedAt: now.toISOString(), lastFilerCommentAt: now.toISOString(),
    body: buildMarker(candidates[0]) };
  const repeat = planFilings({ ...options, existing: [existing] });
  assert.equal(repeat.create.length, 0);
  assert.equal(repeat.comment.length, 0);
  const capped = planFilings({ ...options, existing: [1, 2, 3].map((number) => ({ ...existing, number,
    body: buildMarker({ source: 'ci', keys: [`other:${number}`] }) })) });
  assert.equal(capped.create.length, 0);
});

test('hourly poll reuses existing permissions and leaves weekly/deploy protection intact', () => {
  const workflow = readFileSync(new URL('../../.github/workflows/gardener.yml', import.meta.url), 'utf8');
  assert.match(workflow, /cron: "23 \* \* \* \*"/);
  assert.match(workflow, /gardener:\s+if: github.event_name == 'workflow_dispatch' \|\| github.event.schedule == '0 0 \* \* 1'/);
  assert.doesNotMatch(workflow, /actions: write|deployments: write|bitwarden/);
  const deploy = readFileSync(new URL('../../.github/workflows/deploy-dev.yml', import.meta.url), 'utf8');
  assert.match(deploy, /cancel-in-progress: false/);
});
