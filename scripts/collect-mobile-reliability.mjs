import { execFileSync } from 'node:child_process';
import { existsSync, writeFileSync } from 'node:fs';
import { isAbsolute } from 'node:path';
import { pathToFileURL } from 'node:url';

const repository = 'RichardOrchardOrganisation/QueenZone.Modern';

export function resolveGhExecutable(env = process.env, exists = existsSync) {
  if (env.GH_EXECUTABLE) {
    if (!isAbsolute(env.GH_EXECUTABLE)) throw new Error('GH_EXECUTABLE must be an absolute path.');
    return env.GH_EXECUTABLE;
  }
  const executable = ['/usr/bin/gh', '/usr/local/bin/gh', '/opt/homebrew/bin/gh',
    String.raw`C:\Program Files\GitHub CLI\gh.exe`].find(candidate => exists(candidate));
  if (!executable) throw new Error('Set GH_EXECUTABLE to the absolute path of your trusted GitHub CLI installation.');
  return executable;
}

// Metadata cannot prove a first-pass Maestro result: the harness can recover
// within one Actions job. Leave that field unknown until artifacts are reviewed.
export function measurement(run, job, attempt) {
  if (!/^Mobile (Android|iOS) (device smoke|extended device suite|P0 release suite)$/.test(job.name)) return null;
  const start = Date.parse(job.started_at);
  const end = Date.parse(job.completed_at);
  return {
    runId: run.id, attempt, sha: run.head_sha, branch: run.head_branch,
    event: run.event, jobId: job.id, job: job.name, url: job.html_url,
    runner: job.runner_name ?? null, labels: job.labels ?? [],
    conclusion: job.conclusion ?? 'pending',
    durationSeconds: job.status === 'completed' && job.conclusion !== 'skipped'
      && Number.isFinite(start) && Number.isFinite(end) && end >= start ? (end - start) / 1000 : null,
    failedSteps: (job.steps ?? []).filter(step => ['failure', 'timed_out'].includes(step.conclusion)).map(step => step.name),
    firstMaestroAttempt: 'unknown', recovery: 'unknown', failureClass: 'unclassified',
    // Smoke job names also cover feature proof dispatches; do not infer suite.
    suite: 'requires-input-review', feature: 'requires-input-review',
  };
}

export function summarize(rows) {
  const groups = new Map();
  for (const row of rows) {
    const key = JSON.stringify([row.sha, row.job]);
    const group = groups.get(key) ?? { sha: row.sha, job: row.job, success: 0, failure: 0, other: 0, skipped: 0 };
    if (row.conclusion === 'success') group.success++;
    else if (['failure', 'timed_out'].includes(row.conclusion)) group.failure++;
    else if (row.conclusion === 'skipped') group.skipped++;
    else group.other++;
    groups.set(key, group);
  }
  return [...groups.values()];
}

function collectAttempt(run, attempt, api) {
  const rows = [];
  for (let page = 1; ; page++) {
    const result = api(`actions/runs/${run.id}/attempts/${attempt}/jobs?per_page=100&page=${page}`);
    if (!Array.isArray(result.jobs)) throw new Error(`Missing jobs for ${run.id}, attempt ${attempt}`);
    rows.push(...result.jobs.map(job => measurement(run, job, attempt)).filter(Boolean));
    if (result.jobs.length < 100) return rows;
  }
}

export function collect(runIds, api) {
  const rows = [];
  for (const id of new Set(runIds)) {
    if (!/^\d+$/.test(String(id))) throw new Error(`Invalid run ID: ${id}`);
    const run = api(`actions/runs/${id}`);
    if (run.path !== '.github/workflows/mobile-device-smoke.yml') throw new Error(`Run ${id} is not mobile-device-smoke.yml`);
    if (!Number.isInteger(run.run_attempt) || run.run_attempt < 1) throw new Error(`Run ${id} has no valid attempt count`);
    for (let attempt = 1; attempt <= run.run_attempt; attempt++) {
      rows.push(...collectAttempt(run, attempt, api));
    }
  }
  return { schemaVersion: 1, collectedAt: new Date().toISOString(), repository,
    interpretation: 'Diagnostic job-attempt inventory, not a controlled flake rate. First Maestro attempts require artifact review. Skips and unknowns are never passes.',
    rows, summary: summarize(rows) };
}

if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href) {
  const [output, ...ids] = process.argv.slice(2);
  if (!output || ids.length === 0) throw new Error('Usage: node scripts/collect-mobile-reliability.mjs OUTPUT.json RUN_ID [RUN_ID ...]');
  const gh = resolveGhExecutable();
  const report = collect(ids, endpoint => JSON.parse(execFileSync(gh, ['api', `repos/${repository}/${endpoint}`], { encoding: 'utf8', maxBuffer: 20 * 1024 * 1024 })));
  writeFileSync(output, `${JSON.stringify(report, null, 2)}\n`, { flag: 'wx' });
  console.log(`Recorded ${report.rows.length} device job attempts in ${output}. First-pass results require artifact review.`);
}
