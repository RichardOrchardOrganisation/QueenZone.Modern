import { mkdtempSync, mkdirSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { loadFilerFiles, repoRootFrom } from './config.mjs';
import { loadExisting, main, parseArgs, resolveIngestFindingsPath, runFiler } from './run.mjs';
import { DEFAULT_INGEST_FINDINGS } from './sources/review.mjs';

const { config, ignore, findingRules } = loadFilerFiles(repoRootFrom());
const now = new Date('2026-09-26T08:00:00Z');

function fakeGithub() {
  const calls = [];
  return {
    calls,
    async listIssuesByLabel() {
      return [];
    },
    async listIssueComments() {
      return [];
    },
    async createIssue(issue) {
      calls.push(['createIssue', issue]);
      return { number: 99, ...issue };
    },
    async comment(number, body) {
      calls.push(['comment', number, body]);
    },
    async reopen(number) {
      calls.push(['reopen', number]);
    },
    async addLabels(number, labels) {
      calls.push(['addLabels', number, labels]);
    },
    async updateIssue(number, payload) {
      calls.push(['updateIssue', number, payload]);
    },
    async ensureLabel(name) {
      calls.push(['ensureLabel', name]);
    },
    async findOpenIssueByTitle() {
      return { number: 7, title: 'Issue filer log' };
    },
    async listPullRequests() {
      return [];
    },
    async listReviewComments() {
      return [];
    },
    async listReviews() {
      return [];
    },
    async listWorkflowRuns() {
      return [];
    },
    async listJobsForRun() {
      return [];
    },
  };
}

test('parseArgs defaults and rejects bad values', () => {
  assert.deepEqual(parseArgs([]), {
    dryRun: false,
    validate: false,
    lookbackDays: 7,
    lookbackHours: null,
    maxIssues: 2,
    loop: 'gardener',
    ingestFindings: null,
  });
  assert.equal(parseArgs(['--loop', 'telemetry', '--lookback-hours', '2']).lookbackHours, 2);
  assert.throws(() => parseArgs(['--lookback-hours', '0']), /lookback-hours/);
  assert.equal(parseArgs(['--dry-run', '--lookback-days', '60', '--max-issues', '3']).lookbackDays, 60);
  assert.equal(parseArgs(['--ingest-findings']).ingestFindings, DEFAULT_INGEST_FINDINGS);
  assert.equal(parseArgs(['--ingest-findings', 'tmp/findings.json', '--dry-run']).ingestFindings, 'tmp/findings.json');
  assert.throws(() => parseArgs(['--loop', 'nope']), /gardener or telemetry/);
});

test('validate mode checks the committed JSON files', async () => {
  const lines = [];
  const code = await main(['--validate'], {
    root: repoRootFrom(),
    stdout: (line) => lines.push(String(line)),
  });
  assert.equal(code, 0);
  assert.match(lines.join('\n'), /valid/);
});

test('dry-run with work still writes nothing', async () => {
  const github = fakeGithub();
  const lines = [];
  const result = await runFiler({
    root: repoRootFrom(),
    dryRun: true,
    now,
    github,
    config,
    ignore,
    findingRules,
    collectors: [
      async () => [{
        source: 'review',
        keys: ['review:csharp.regex-timeout'],
        rule: 'csharp.regex-timeout',
        title: '[review] csharp.regex-timeout (2 PRs)',
        area: 'web',
        evidence: [{ url: 'https://example.test/1', text: '#1' }],
        count: 2,
        firstSeen: '2026-09-20T00:00:00Z',
        lastSeen: '2026-09-22T00:00:00Z',
        level: 'L2',
      }],
    ],
    existing: [],
    stdout: (line) => lines.push(String(line)),
  });
  assert.equal(result.plan.create.length, 1);
  assert.equal(result.wrote, false);
  assert.equal(github.calls.length, 0);
  assert.match(lines.join('\n'), /will create/);
});

test('empty plan is silent: no issue, no comment, no mention', async () => {
  const github = fakeGithub();
  const result = await runFiler({
    root: repoRootFrom(),
    dryRun: false,
    now,
    github,
    config,
    ignore,
    findingRules,
    collectors: [async () => []],
    existing: [],
    stdout: () => {},
  });
  assert.equal(result.wrote, false);
  assert.equal(github.calls.length, 0);
});

test('live plan files, comments the log, and does not mention on empty writes', async () => {
  const github = fakeGithub();
  const result = await runFiler({
    root: repoRootFrom(),
    dryRun: false,
    now,
    github,
    config,
    ignore,
    findingRules,
    collectors: [
      async () => [{
        source: 'review',
        keys: ['review:csharp.regex-timeout'],
        title: '[review] csharp.regex-timeout (2 PRs)',
        area: 'web',
        evidence: [],
        count: 2,
        level: 'L2',
      }],
    ],
    existing: [],
    stdout: () => {},
  });
  assert.equal(result.wrote, true);
  assert.ok(github.calls.some((call) => call[0] === 'createIssue'));
  const log = github.calls.find((call) => call[0] === 'comment' && call[1] === 7);
  assert.match(log[2], /@richardorchard/);
  assert.ok(!github.calls.some((call) => call[0] === 'comment' && call[1] === 1802));
});

test('loadExisting keeps a quiet open issue older than 90 days', async () => {
  const calls = [];
  const quietOpen = {
    number: 12,
    title: 'old gardener',
    body: '<!-- qz-filer v=1 keys=review:csharp.regex-timeout source=review -->',
    state: 'open',
    createdAt: '2026-01-01T00:00:00Z',
    updatedAt: '2026-04-01T00:00:00Z',
    user: 'github-actions[bot]',
    labels: ['gardener'],
  };
  const staleClosed = {
    number: 13,
    title: 'ancient closed',
    body: '<!-- qz-filer v=1 keys=review:other source=review -->',
    state: 'closed',
    closedAt: '2026-01-01T00:00:00Z',
    createdAt: '2026-01-01T00:00:00Z',
    updatedAt: '2026-09-20T00:00:00Z',
    user: 'github-actions[bot]',
    labels: ['gardener'],
  };
  const github = {
    async listIssuesByLabel(label, query = {}) {
      calls.push({ label, query });
      if (label !== 'gardener') {
        return [];
      }
      if (query.state === 'open') {
        return [quietOpen];
      }
      return [staleClosed];
    },
    async listIssueComments() {
      return [];
    },
  };
  const existing = await loadExisting(github, config, now);
  const openQueries = calls.filter((item) => item.query.state === 'open');
  const closedQueries = calls.filter((item) => item.query.state === 'closed');
  assert.ok(openQueries.length > 0);
  assert.ok(openQueries.every((item) => item.query.since == null));
  assert.ok(closedQueries.every((item) => item.query.since));
  assert.ok(existing.some((issue) => issue.number === 12));
  assert.ok(!existing.some((issue) => issue.number === 13));

  const result = await runFiler({
    root: repoRootFrom(),
    dryRun: true,
    now,
    github,
    config,
    ignore,
    findingRules,
    collectors: [
      async () => [{
        source: 'review',
        keys: ['review:csharp.regex-timeout'],
        title: '[review] csharp.regex-timeout (2 PRs)',
        area: 'web',
        evidence: [],
        count: 2,
        level: 'L2',
      }],
    ],
    existing,
    stdout: () => {},
  });
  assert.equal(result.plan.create.length, 0);
  assert.equal(result.plan.comment[0].issueNumber, 12);
});

test('60-day lookback can read a backfill file without classifying comments', async () => {
  const root = mkdtempSync(path.join(tmpdir(), 'filer-backfill-'));
  mkdirSync(path.join(root, '.github', 'issue-filer', 'backfill'), { recursive: true });
  writeFileSync(
    path.join(root, '.github', 'issue-filer', 'backfill', 'review-findings-60d.json'),
    JSON.stringify({
      findings: [
        {
          level: 'L2',
          rule: 'csharp.regex-timeout',
          repeat: 'yes',
          file: 'src/QueenZone.Web/Services/NewsSlugService.cs:1',
          verdict: 'blocking',
          pr: 1,
          url: 'https://example.test/1',
          at: '2026-08-01T00:00:00Z',
        },
        {
          level: 'L2',
          rule: 'csharp.regex-timeout',
          repeat: 'yes',
          file: 'src/QueenZone.Web/Services/NewsSlugService.cs:2',
          verdict: 'blocking',
          pr: 2,
          url: 'https://example.test/2',
          at: '2026-08-02T00:00:00Z',
        },
      ],
    }),
  );
  const { collect } = await import('./sources/review.mjs');
  const weekly = await collect({
    config,
    findingRules: [],
    pulls: [],
    lookbackDays: 7,
    root,
    since: new Date('2026-09-19T00:00:00Z'),
  });
  assert.deepEqual(weekly, []);
  const backfill = await collect({
    config,
    findingRules: [],
    pulls: [],
    lookbackDays: 60,
    root,
    since: new Date('2026-07-28T00:00:00Z'),
  });
  assert.equal(backfill[0].count, 2);
});

test('default run does not ingest the committed 60-day findings file', async () => {
  const result = await runFiler({
    root: repoRootFrom(),
    dryRun: true,
    now,
    github: fakeGithub(),
    config,
    ignore,
    findingRules,
    collectors: [async () => []],
    existing: [],
    stdout: () => {},
  });
  assert.equal(result.plan.create.length, 0);
  assert.equal(result.candidates.length, 0);
});

test('ingest-findings reads the committed file and caps the top two review rules', async () => {
  const lines = [];
  const result = await runFiler({
    root: repoRootFrom(),
    dryRun: true,
    now,
    github: fakeGithub(),
    config,
    ignore,
    findingRules,
    ingestFindings: DEFAULT_INGEST_FINDINGS,
    collectors: [async () => []],
    existing: [],
    maxIssues: 2,
    stdout: (line) => lines.push(String(line)),
  });
  assert.equal(result.plan.create.length, 2);
  assert.equal(result.plan.create[0].candidate.rule, 'mobile.swallowed-error-state');
  assert.equal(result.plan.create[0].candidate.level, 'L1');
  assert.equal(result.plan.create[0].candidate.ingested, true);
  assert.equal(result.plan.create[1].candidate.rule, 'test.in-memory-not-sql');
  assert.equal(result.plan.create[1].candidate.level, 'L2');
  assert.ok(result.plan.skipped.some((item) => item.reason === 'cap'));
  assert.ok(!result.plan.create.some((item) => item.candidate.storm));
  assert.match(lines.join('\n'), /mobile\.swallowed-error-state/);
});

test('telemetry lookback hours set the collector since timestamp', async () => {
  let seenSince;
  await runFiler({
    root: repoRootFrom(),
    dryRun: true,
    now,
    lookbackHours: 2,
    loop: 'telemetry',
    github: fakeGithub(),
    config,
    ignore,
    findingRules,
    collectors: [
      async (ctx) => {
        seenSince = ctx.since;
        return [];
      },
    ],
    existing: [],
    stdout: () => {},
  });
  assert.equal(seenSince.toISOString(), '2026-09-26T06:00:00.000Z');
});

test('later correlated keys are appended to an existing marker', async () => {
  const github = fakeGithub();
  const existingBody = 'body\n<!-- qz-filer v=1 keys=sentry:1 source=sentry -->\n';
  await runFiler({
    root: repoRootFrom(),
    dryRun: false,
    now,
    loop: 'telemetry',
    github,
    config,
    ignore,
    findingRules,
    collectors: [
      async () => [{
        source: 'telemetry',
        keys: ['sentry:1', 'ai:req:/news/{id}:5xx'],
        title: '[sentry] news 500',
        area: 'news',
        evidence: [],
        count: 3,
        level: 'L2',
      }],
    ],
    existing: [{
      number: 44,
      title: 'old sentry',
      body: existingBody,
      state: 'open',
      createdAt: '2026-09-20T00:00:00Z',
      updatedAt: '2026-09-20T00:00:00Z',
      user: 'github-actions[bot]',
      labels: ['from-telemetry'],
    }],
    stdout: () => {},
  });
  const update = github.calls.find((call) => call[0] === 'updateIssue');
  assert.ok(update);
  assert.match(update[2].body, /keys=sentry:1,ai:req:\/news\/\{id\}:5xx/);
});

test('listIssuesByLabel throw blocks create', async () => {
  const github = fakeGithub();
  github.listIssuesByLabel = async () => {
    throw new Error('label lookup failed');
  };
  await assert.rejects(() => runFiler({
    root: repoRootFrom(),
    dryRun: false,
    now,
    github,
    config,
    ignore,
    findingRules,
    loop: 'telemetry',
    collectors: [
      async () => [{
        source: 'sentry',
        keys: ['sentry:9'],
        title: '[sentry] leak user@example.com',
        area: 'news',
        evidence: [],
        count: 2,
        level: 'L2',
      }],
    ],
    stdout: () => {},
  }), /label lookup failed/);
  assert.ok(!github.calls.some((call) => call[0] === 'createIssue'));
});

test('telemetry dry-run will-create lines use redacted titles', async () => {
  const lines = [];
  await runFiler({
    root: repoRootFrom(),
    dryRun: true,
    now,
    loop: 'telemetry',
    github: fakeGithub(),
    config,
    ignore,
    findingRules,
    collectors: [
      async () => [{
        source: 'sentry',
        keys: ['sentry:9'],
        title: '[sentry] leak user@example.com Bearer eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.aaa.bbb',
        area: 'news',
        evidence: [],
        count: 2,
        level: 'L2',
      }],
    ],
    existing: [],
    stdout: (line) => lines.push(String(line)),
  });
  const text = lines.join('\n');
  assert.match(text, /will create:/);
  assert.doesNotMatch(text, /user@example\.com/);
  assert.doesNotMatch(text, /eyJhbGci/);
  assert.match(text, /\\\[email\\\]/);
});

test('telemetry collect failure writes the plan then exits non-zero without filing', async () => {
  const github = fakeGithub();
  const lines = [];
  const warnings = ['sentry: Sentry issues failed: 400: Boolean statements containing "OR" or "AND" are not supported in this search'];
  const result = await runFiler({
    root: repoRootFrom(),
    dryRun: false,
    now,
    loop: 'telemetry',
    github,
    config,
    ignore,
    findingRules,
    warnings,
    collectors: [
      async () => [{
        source: 'appinsights',
        keys: ['ai:exc:SqlException'],
        title: '[appinsights] should not file',
        area: 'news',
        evidence: [],
        count: 2,
        level: 'L2',
      }],
    ],
    existing: [],
    stdout: (line) => lines.push(String(line)),
  });
  assert.equal(result.collectFailed, true);
  assert.equal(result.wrote, false);
  assert.match(lines.join('\n'), /warnings: sentry: Sentry issues failed: 400/);
  assert.ok(!github.calls.some((call) => call[0] === 'createIssue'));

  const mainLines = [];
  const mainGithub = fakeGithub();
  const code = await main(['--loop', 'telemetry', '--lookback-hours', '2'], {
    root: repoRootFrom(),
    github: mainGithub,
    token: 'x',
    repository: 'org/repo',
    owner: 'org',
    repo: 'repo',
    warnings: ['azure: azure-graph-failed'],
    collectors: [async () => []],
    stdout: (line) => mainLines.push(String(line)),
  });
  assert.equal(code, 1);
  assert.match(mainLines.join('\n'), /azure-graph-failed/);
  assert.ok(!mainGithub.calls.some((call) => call[0] === 'createIssue'));
});

test('telemetry zero-result collect still exits zero', async () => {
  const lines = [];
  const code = await main(['--loop', 'telemetry', '--lookback-hours', '2', '--dry-run'], {
    root: repoRootFrom(),
    github: fakeGithub(),
    token: 'x',
    repository: 'org/repo',
    owner: 'org',
    repo: 'repo',
    collectors: [async () => []],
    stdout: (line) => lines.push(String(line)),
  });
  assert.equal(code, 0);
  assert.match(lines.join('\n'), /Silent run: nothing to file/);
});

test('missing ingest-findings file fails closed', () => {
  assert.throws(
    () => resolveIngestFindingsPath(repoRootFrom(), 'scripts/issue-filer/missing-findings.json'),
    /Ingest findings file not found/,
  );
});
