import { pathToFileURL } from 'node:url';
import { createGitHubClient, parseRepository } from './github-client.mjs';
import { runFiler, writeStepSummary } from './run.mjs';

export const QUEUE_AGE_MINUTES = 60;
const WORKFLOW = 'deploy-dev.yml';
const STATUSES = new Set(['queued', 'in_progress', 'pending', 'waiting', 'requested']);

function timestamp(value) {
  const parsed = Date.parse(value);
  return Number.isFinite(parsed) ? parsed : null;
}

function assigned(job) {
  return Boolean(job.runner_id || job.runner_name);
}

export function classifyDeployment(run, jobs, approvals, now, ageMinutes = QUEUE_AGE_MINUTES) {
  if (run.status === 'completed') return { kind: 'completed' };
  if (jobs.some((job) => job.status === 'in_progress')) return { kind: 'active-job' };
  if (approvals.length) return { kind: 'environment-approval' };
  const unfinished = jobs.filter((job) => job.status !== 'completed');
  if (unfinished.some(assigned)) return { kind: 'assigned-job' };
  if (!unfinished.length) {
    return { kind: jobs.length ? 'run-status-lag' : 'no-job-record' };
  }
  if (!['queued', 'in_progress'].includes(run.status)
      || unfinished.some((job) => job.status !== 'queued')) return { kind: 'unknown-wait' };
  const created = timestamp(run.created_at);
  const ages = unfinished.map((job) => {
    // started_at is populated for an unassigned queued job by Actions (37320201364).
    // If absent, updated_at is conservative: a later run update restarts the clock.
    const queued = timestamp(job.started_at || run.updated_at);
    if (queued == null || created == null) return null;
    const start = Math.max(created, queued);
    return start > now.getTime() ? null : (now.getTime() - start) / 60_000;
  });
  if (ages.some((age) => age == null)) return { kind: 'unknown-age' };
  const age = Math.min(...ages);
  return { kind: age >= ageMinutes ? 'aged-unassigned-job' : 'normal-queue', ageMinutes: Math.floor(age) };
}

export function queueCandidates(records, now, repository) {
  const observations = records.map(({ run, jobs, approvals }) => ({
    id: run.id, createdAt: run.created_at, status: run.status,
    ...classifyDeployment(run, jobs, approvals, now),
  }));
  for (const item of observations) {
    // Only this known main-ref workflow is monitored. GitHub does not expose a
    // run's concurrency group: a pending successor is a possible wait, not proof.
    if (item.kind === 'no-job-record' && item.status === 'pending'
        && observations.some((other) => other.id !== item.id
          && other.kind !== 'completed' && timestamp(other.createdAt) < timestamp(item.createdAt))) {
      item.kind = 'possible-concurrency-wait';
    }
  }
  const blockers = observations.filter((item) => item.kind === 'aged-unassigned-job');
  const candidates = blockers.length ? [{
    source: 'ci', keys: ['deployment-queue:deploy-dev:main:unassigned'],
    title: '[ci] Dev deployment has an aged unassigned queued job', area: 'infra',
    count: blockers.length, firstSeen: blockers.map((item) => item.createdAt).sort()[0],
    lastSeen: now.toISOString(), level: 'L2',
    proposedCheck: 'Inspect the linked job, approvals and runner capacity before any operator action. Do not cancel an active deployment or automatically redeploy.',
    evidence: blockers.slice(0, 20).map((item) => ({
      url: `https://github.com/${repository}/actions/runs/${item.id}`,
      text: `Run ${item.id}: all unfinished jobs queued and unassigned; no pending approval; conservative queue age ${item.ageMinutes} minutes (threshold ${QUEUE_AGE_MINUTES}). Root cause unproven.`,
    })),
  }] : [];
  return { observations, candidates };
}

export function createDeploymentMetadataClient({ token, repository, fetchImpl = fetch }) {
  const { owner, repo } = parseRepository(repository);
  async function get(path) {
    const response = await fetchImpl(`https://api.github.com/repos/${owner}/${repo}/${path}`, {
      method: 'GET', signal: AbortSignal.timeout(20_000), headers: {
        Accept: 'application/vnd.github+json', Authorization: `Bearer ${token}`,
        'X-GitHub-Api-Version': '2022-11-28',
      },
    });
    if (!response.ok) throw new Error(`Deployment metadata unavailable (HTTP ${response.status}).`);
    if (/rel="next"/.test(response.headers.get('link') || '')) {
      throw new Error('Deployment metadata exceeds the bounded first page; inspection incomplete.');
    }
    return response.json();
  }
  function addRuns(runs, data) {
    if (!Array.isArray(data.workflow_runs) || data.total_count > data.workflow_runs.length) {
      throw new Error('Deployment run listing is malformed or truncated; inspection incomplete.');
    }
    for (const run of data.workflow_runs) {
      if (!Number.isSafeInteger(run.id) || run.id <= 0) throw new Error('Invalid run metadata; inspection incomplete.');
      if (run.head_branch === 'main' && run.path?.split('@')[0] === `.github/workflows/${WORKFLOW}`) runs.set(run.id, run);
    }
  }
  async function collectRecord(original) {
    // These reads are ordered: refresh status after jobs/approvals to avoid
    // reporting a holder that completed during inspection.
    const jobs = await get(`actions/runs/${original.id}/jobs?per_page=100`);
    const approvals = await get(`actions/runs/${original.id}/pending_deployments`);
    const run = await get(`actions/runs/${original.id}`);
    if (!Array.isArray(jobs.jobs) || jobs.total_count > jobs.jobs.length || !Array.isArray(approvals)
        || run.id !== original.id || !STATUSES.has(run.status) && run.status !== 'completed') {
      throw new Error('Deployment run detail is malformed or truncated; inspection incomplete.');
    }
    if (run.run_attempt !== original.run_attempt) throw new Error('Run attempt changed during inspection; inspection incomplete.');
    return { run, jobs: jobs.jobs, approvals };
  }
  return {
    async collect() {
      const pages = await Promise.all([...STATUSES].map((status) =>
        get(`actions/workflows/${WORKFLOW}/runs?branch=main&status=${status}&per_page=100`)));
      const runs = new Map();
      pages.forEach((data) => addRuns(runs, data));
      if (runs.size > 20) throw new Error('More than 20 unfinished dev runs; inspection incomplete.');
      return Promise.all([...runs.values()].map(collectRecord));
    },
  };
}

export async function monitorDeployments({ repository, token, dryRun = true, now = new Date(),
  metadata, github, stdout = console.log, summary = writeStepSummary, root } = {}) {
  const records = await (metadata || createDeploymentMetadataClient({ token, repository })).collect();
  const { observations, candidates } = queueCandidates(records, now, repository);
  // Fixed classifications and numeric IDs only: no run titles, environment
  // reviewers, runner names, logs, credentials or arbitrary API response bodies.
  const table = ['## Dev deployment queue', '', `Observed ${now.toISOString()}; threshold ${QUEUE_AGE_MINUTES} minutes.`, '',
    '| Run | Classification | Queue age (minutes) |', '| --- | --- | --- |',
    ...observations.map((item) => `| ${item.id} | ${item.kind} | ${item.ageMinutes ?? 'unknown'} |`),
    ...(observations.length ? [] : ['No unfinished dev deployment runs.'])].join('\n');
  stdout(table); summary(table);
  if (candidates.length) stdout('::warning title=Dev deployment queue::An aged unassigned queued job needs operator inspection. No automatic recovery is performed.');
  const { owner, repo } = parseRepository(repository);
  const result = await runFiler({ root, now, loop: 'telemetry', maxIssues: 1, dryRun,
    github: github || createGitHubClient({ token, owner, repo }),
    collectors: [async () => candidates], stdout, writeSummary: summary });
  return { observations, ...result };
}

if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href) {
  try {
    await monitorDeployments({ repository: process.env.GITHUB_REPOSITORY, token: process.env.GITHUB_TOKEN,
      dryRun: process.env.TELEMETRY_TRIAGE_FILE_ISSUES !== 'true' });
  } catch {
    // Never print an arbitrary upstream error or response body.
    console.error('Deployment queue inspection failed; no clean queue or filing is claimed.');
    process.exitCode = 1;
  }
}
