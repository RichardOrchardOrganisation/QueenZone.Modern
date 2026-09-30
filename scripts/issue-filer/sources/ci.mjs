function failedStepName(job) {
  const steps = job.steps || [];
  const failed = steps.find((step) => step.conclusion === 'failure');
  return failed?.name || 'unknown-step';
}

function groupKey(run, job) {
  const workflow = run.name || run.path || 'workflow';
  const jobName = job.name || 'job';
  return `${workflow} / ${jobName} / ${failedStepName(job)}`;
}

export function groupFailedRuns(runs, jobsByRunId) {
  const byKey = new Map();
  for (const run of runs || []) {
    const jobs = (jobsByRunId[run.id] || []).filter((job) => job.conclusion === 'failure');
    if (jobs.length === 0) {
      continue;
    }
    for (const job of jobs) {
      const key = groupKey(run, job);
      const bucket = byKey.get(key) || [];
      bucket.push({ run, job });
      byKey.set(key, bucket);
    }
  }

  const candidates = [];
  for (const [key, group] of byKey) {
    const shas = new Set(group.map((item) => item.run.head_sha || item.run.headSha).filter(Boolean));
    if (group.length < 3 || shas.size < 2) {
      continue;
    }
    const seen = group.map((item) => item.run.created_at || item.run.createdAt || '').filter(Boolean).sort();
    candidates.push({
      source: 'ci',
      keys: [`ci:${key.replaceAll(' ', '_')}`],
      title: `[ci] ${key} (${group.length} runs)`,
      area: 'infra',
      evidence: group.slice(0, 20).map((item) => ({
        url: item.run.html_url || item.run.htmlUrl || '',
        text: `${item.run.name || 'run'} ${item.run.head_sha || ''}`.trim(),
      })),
      count: group.length,
      firstSeen: seen[0] || '',
      lastSeen: seen[seen.length - 1] || '',
      level: 'L2',
      proposedCheck: `Stabilize or quarantine CI step "${key}".`,
    });
  }
  return candidates.sort((left, right) => left.title.localeCompare(right.title));
}

function withinLookback(run, since) {
  if (!since) {
    return true;
  }
  return new Date(run.created_at || run.createdAt || 0) >= since;
}

export async function collect(ctx) {
  if (ctx.failedRuns) {
    return groupFailedRuns(ctx.failedRuns, ctx.jobsByRunId || {});
  }
  if (!ctx.github) {
    ctx.warnings?.push('ci: no GitHub client');
    return [];
  }
  try {
    const [mainFails, mergeFails] = await Promise.all([
      ctx.github.listWorkflowRuns({ status: 'failure', branch: 'main' }),
      ctx.github.listWorkflowRuns({ status: 'failure', event: 'merge_group' }),
    ]);
    const seen = new Set();
    const runs = [];
    for (const run of [...mainFails, ...mergeFails]) {
      if (seen.has(run.id) || !withinLookback(run, ctx.since)) {
        continue;
      }
      seen.add(run.id);
      runs.push(run);
    }
    const jobsByRunId = {};
    for (const run of runs) {
      jobsByRunId[run.id] = await ctx.github.listJobsForRun(run.id);
    }
    return groupFailedRuns(runs, jobsByRunId);
  } catch (error) {
    ctx.warnings?.push(`ci: ${error.message}`);
    return [];
  }
}
