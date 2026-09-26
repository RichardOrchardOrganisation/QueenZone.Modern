#!/usr/bin/env node
/**
 * Shared issue filer CLI (#1804).
 *
 *   node scripts/issue-filer/run.mjs --validate
 *   node scripts/issue-filer/run.mjs --loop gardener --lookback-days 7 --max-issues 2 --dry-run
 */
import { writeFileSync } from 'node:fs';
import path from 'node:path';
import { pathToFileURL } from 'node:url';
import { loadFilerFiles, repoRootFrom, validateFilerFiles } from './config.mjs';
import { planFilings, unregisteredRules } from './core.mjs';
import { isFilerComment } from './finding.mjs';
import { createGitHubClient, parseRepository } from './github-client.mjs';
import { collect as collectCi } from './sources/ci.mjs';
import { collect as collectReview } from './sources/review.mjs';
import { collect as collectSonar, defaultSonarSearch } from './sources/sonar.mjs';
import { collect as collectSuppressions } from './sources/suppressions.mjs';
import {
  buildComment,
  buildIssue,
  buildLogComment,
  buildRankedReport,
  formatPlanSummary,
} from './templates.mjs';

const LABEL_META = {
  gardener: { color: '0e8a16', description: 'Filed by the weekly gardener.' },
  guardrail: { color: '5319e7', description: 'Repeat finding that should become an L1/L2 check.' },
  regression: { color: 'd93f0b', description: 'Closed completed issue that recurred within 30 days.' },
};

const DEFAULT_COLLECTORS = {
  gardener: [collectReview, collectSonar, collectCi, collectSuppressions],
  telemetry: [],
};

export function parseArgs(argv) {
  const args = {
    dryRun: false,
    validate: false,
    lookbackDays: 7,
    maxIssues: 2,
    loop: 'gardener',
  };
  const list = [...argv];
  while (list.length > 0) {
    const flag = list.shift();
    if (flag === '--dry-run') {
      args.dryRun = true;
    } else if (flag === '--validate') {
      args.validate = true;
    } else if (flag === '--lookback-days') {
      args.lookbackDays = Number(list.shift());
    } else if (flag === '--max-issues') {
      args.maxIssues = Number(list.shift());
    } else if (flag === '--loop') {
      args.loop = list.shift();
    } else {
      throw new Error(`Unknown argument: ${flag}`);
    }
  }
  if (!Number.isFinite(args.lookbackDays) || args.lookbackDays < 1) {
    throw new Error('--lookback-days must be a positive number');
  }
  if (!Number.isFinite(args.maxIssues) || args.maxIssues < 1) {
    throw new Error('--max-issues must be a positive number');
  }
  if (args.loop !== 'gardener' && args.loop !== 'telemetry') {
    throw new Error('--loop must be gardener or telemetry');
  }
  return args;
}

function uniqueIssues(lists) {
  const byNumber = new Map();
  for (const issue of lists.flat()) {
    if (!issue?.number || issue.pullRequest) {
      continue;
    }
    byNumber.set(issue.number, issue);
  }
  return [...byNumber.values()];
}

export async function loadExisting(github, config, now) {
  const lookbackDays = config.match?.closedLookbackDays ?? 90;
  const since = new Date(now.getTime() - lookbackDays * 24 * 60 * 60 * 1000);
  const labels = [
    config.labels.gardener,
    config.labels.guardrail,
    config.labels.regression,
    ...(config.labels.telemetry || []),
  ].filter(Boolean);
  const lists = [];
  for (const label of [...new Set(labels)]) {
    lists.push(await github.listIssuesByLabel(label, { state: 'open' }));
    lists.push(await github.listIssuesByLabel(label, { state: 'closed', since: since.toISOString() }));
  }
  const existing = uniqueIssues(lists).filter((issue) => {
    if (issue.state === 'open') {
      return true;
    }
    return issue.closedAt && new Date(issue.closedAt) >= since;
  });
  for (const issue of existing) {
    const comments = await github.listIssueComments(issue.number);
    const filerComments = comments.filter((comment) => isFilerComment(comment.body));
    const last = filerComments.at(-1);
    issue.lastFilerCommentAt = last?.created_at || last?.createdAt || null;
  }
  return existing;
}

async function ensureKnownLabels(github, config) {
  const names = [config.labels.gardener, config.labels.guardrail, config.labels.regression];
  for (const name of names) {
    const meta = LABEL_META[name] || { color: 'ededed', description: name };
    await github.ensureLabel(name, meta.color, meta.description);
  }
}

async function postLog(github, config, plan) {
  let log = await github.findOpenIssueByTitle(config.logIssueTitle);
  if (!log) {
    log = await github.createIssue({
      title: config.logIssueTitle,
      body: 'Running log for the shared issue filer. Pin this issue. The gardener comments here only when it files or reopens something.\n',
      labels: [config.labels.gardener],
    });
  }
  await github.comment(log.number, buildLogComment({
    create: plan.create,
    reopen: plan.reopen,
    mention: config.mention,
  }));
}

export async function runFiler(options = {}) {
  const root = options.root || repoRootFrom();
  const now = options.now instanceof Date ? options.now : new Date(options.now || Date.now());
  const loaded = options.config ? {
    config: options.config,
    ignore: options.ignore || { entries: [] },
    findingRules: options.findingRules || [],
  } : loadFilerFiles(root);
  const { config, ignore, findingRules } = loaded;
  const loop = options.loop || 'gardener';
  const lookbackDays = options.lookbackDays ?? 7;
  const maxIssues = options.maxIssues ?? config.caps[loop]?.maxIssues ?? 2;
  const since = new Date(now.getTime() - lookbackDays * 24 * 60 * 60 * 1000);
  const warnings = options.warnings || [];
  const malformed = options.malformed || [];
  const github = options.github;
  const collectors = options.collectors || DEFAULT_COLLECTORS[loop] || [];

  const ctx = {
    github,
    since,
    lookbackDays,
    config,
    findingRules,
    root,
    warnings,
    malformed,
    now,
    pulls: options.pulls,
    issueComments: options.issueComments,
    reviewComments: options.reviewComments,
    reviews: options.reviews,
    sonarIssues: options.sonarIssues,
    sonarSearch: options.sonarSearch,
    failedRuns: options.failedRuns,
    jobsByRunId: options.jobsByRunId,
    gitLogPatch: options.gitLogPatch,
    patch: options.patch,
  };

  const candidates = [];
  for (const collect of collectors) {
    candidates.push(...(await collect(ctx)));
  }

  const existing = options.existing || (github ? await loadExisting(github, config, now) : []);
  const plan = planFilings({
    candidates,
    existing,
    ignore,
    config,
    now,
    loop,
    maxIssues,
    findingRules,
  });
  const extras = {
    malformed,
    unregistered: unregisteredRules(candidates, findingRules),
    warnings,
  };
  const summary = formatPlanSummary(plan, extras);
  if (options.writeSummary) {
    options.writeSummary(summary);
  }
  (options.stdout || console.log)(summary);

  const wrote = plan.create.length + plan.comment.length + plan.reopen.length;
  if (options.dryRun) {
    return { plan, candidates, extras, wrote: false };
  }

  if (wrote === 0) {
    if (lookbackDays >= 60 && config.reportIssueNumber && github) {
      await github.comment(config.reportIssueNumber, buildRankedReport(candidates));
    }
    return { plan, candidates, extras, wrote: false };
  }

  if (!github) {
    throw new Error('GitHub client is required to write issues');
  }

  await ensureKnownLabels(github, config);

  for (const item of plan.create) {
    const issue = buildIssue({
      candidate: item.candidate,
      config,
      previousIssue: item.previousIssue,
      loop,
    });
    await github.createIssue(issue);
  }
  for (const item of plan.reopen) {
    await github.reopen(item.issueNumber);
    await github.addLabels(item.issueNumber, item.labels);
  }
  for (const item of plan.comment) {
    await github.comment(item.issueNumber, buildComment(item));
  }

  if (plan.create.length + plan.reopen.length > 0) {
    await postLog(github, config, plan);
  }

  if (lookbackDays >= 60 && config.reportIssueNumber) {
    await github.comment(config.reportIssueNumber, buildRankedReport(candidates));
  }

  return { plan, candidates, extras, wrote: true };
}

export function writeStepSummary(text, filePath = process.env.GITHUB_STEP_SUMMARY) {
  if (!filePath) {
    return;
  }
  writeFileSync(filePath, text, { flag: 'a' });
}

export async function main(argv = process.argv.slice(2), deps = {}) {
  const args = parseArgs(argv);
  const root = deps.root || repoRootFrom();
  if (args.validate) {
    const result = validateFilerFiles(root);
    if (result.errors.length > 0) {
      (deps.stderr || console.error)(result.errors.join('\n'));
      return 1;
    }
    (deps.stdout || console.log)('issue-filer config, ignore list, and finding-rules are valid.');
    return 0;
  }

  const token = deps.token ?? process.env.GITHUB_TOKEN;
  const repository = deps.repository ?? process.env.GITHUB_REPOSITORY;
  if (!deps.github && (!token || !repository)) {
    throw new Error('GITHUB_TOKEN and GITHUB_REPOSITORY are required unless --validate');
  }
  const { owner, repo } = deps.owner
    ? { owner: deps.owner, repo: deps.repo }
    : parseRepository(repository);
  const github = deps.github || createGitHubClient({ token, owner, repo, fetchImpl: deps.fetchImpl });
  const loaded = loadFilerFiles(root);
  const sonarSearch = deps.sonarSearch || ((query) => {
    const organization = process.env[loaded.config.sonar?.organizationEnv || 'SONAR_ORGANIZATION'] || '';
    const projectKey = process.env[loaded.config.sonar?.projectKeyEnv || 'SONAR_PROJECT_KEY'] || '';
    if (!projectKey) {
      const error = new Error('SONAR_PROJECT_KEY is not set');
      throw error;
    }
    return defaultSonarSearch({
      host: loaded.config.sonar?.host || 'https://sonarcloud.io',
      organization,
      projectKey,
      token: process.env.SONAR_TOKEN,
      fetchImpl: deps.fetchImpl || fetch,
      ...query,
    });
  });

  await runFiler({
    root,
    dryRun: args.dryRun,
    lookbackDays: args.lookbackDays,
    maxIssues: args.maxIssues,
    loop: args.loop,
    github,
    sonarSearch,
    writeSummary: deps.writeSummary || writeStepSummary,
    stdout: deps.stdout,
    config: loaded.config,
    ignore: loaded.ignore,
    findingRules: loaded.findingRules,
  });
  return 0;
}

const invokedDirectly = process.argv[1] && import.meta.url === pathToFileURL(path.resolve(process.argv[1])).href;
if (invokedDirectly) {
  main().then((code) => {
    if (code) {
      process.exitCode = code;
    }
  }).catch((error) => {
    console.error(error.message || error);
    process.exitCode = 1;
  });
}
