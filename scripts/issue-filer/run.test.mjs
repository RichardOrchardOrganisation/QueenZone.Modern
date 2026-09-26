import { mkdtempSync, mkdirSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { loadFilerFiles, repoRootFrom } from './config.mjs';
import { loadExisting, main, parseArgs, runFiler } from './run.mjs';

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
    maxIssues: 2,
    loop: 'gardener',
  });
  assert.equal(parseArgs(['--dry-run', '--lookback-days', '60', '--max-issues', '3']).lookbackDays, 60);
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
