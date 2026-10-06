import { test } from 'node:test';
import assert from 'node:assert/strict';
import { loadFilerFiles, repoRootFrom } from './config.mjs';
import {
  buildComment,
  buildIssue,
  buildLogComment,
  buildMarker,
  escapeMarkdown,
  formatPlanSummary,
  planSummaryRows,
  labelsFor,
  safeTitle,
} from './templates.mjs';

const { config } = loadFilerFiles(repoRootFrom());
const candidate = {
  source: 'review',
  keys: ['review:csharp.regex-timeout'],
  title: '[review] csharp.regex-timeout (2 PRs)',
  area: 'web',
  evidence: [{ url: 'https://example.test/pr/10', text: '#10 file' }],
  count: 2,
  firstSeen: '2026-09-20T00:00:00Z',
  lastSeen: '2026-09-22T00:00:00Z',
  level: 'L2',
  proposedCheck: 'Add an analyzer for regex timeouts.',
};

test('marker is the last line of a filed issue', () => {
  const issue = buildIssue({ candidate, config, previousIssue: 88, loop: 'gardener' });
  const lines = issue.body.trim().split('\n');
  assert.equal(lines.at(-1), buildMarker(candidate));
  assert.match(issue.body, /Previously closed as #88/);
  assert.match(issue.body, /https:\/\/example\.test\/pr\/10/);
  assert.deepEqual(issue.labels, ['gardener', 'guardrail']);
  assert.ok(!issue.labels.includes('proposed-check'));
});

test('telemetry labels skip proposed-check and can mark needs-triage', () => {
  assert.deepEqual(
    labelsFor({ ...candidate, area: 'unknown', source: 'sentry', keys: ['sentry:1'] }, config, 'telemetry'),
    ['bug', 'from-telemetry', 'from-sentry', 'needs-triage'],
  );
  assert.deepEqual(
    labelsFor({
      ...candidate,
      area: 'news',
      source: 'telemetry',
      keys: ['sentry:1', 'ai:req:/news/{id}:5xx'],
    }, config, 'telemetry'),
    ['bug', 'from-telemetry', 'from-sentry', 'from-appinsights', 'news'],
  );
});

test('telemetry issue body carries AC4 fields and the AC5 capture-proof command', () => {
  const issue = buildIssue({
    candidate: {
      source: 'sentry',
      keys: ['sentry:555'],
      title: '[sentry] TypeError',
      area: 'news',
      featureId: 'web.news.detail',
      captureProof: 'pwsh -File .cursor/skills/verify-queenzone/scripts/control-queenzone.ps1 capture-proof -Feature web.news.detail',
      evidence: [{ url: 'https://sentry.io/issues/555', text: 'Sentry issue' }],
      count: 4,
      firstSeen: '2026-09-27T09:00:00Z',
      lastSeen: '2026-09-27T10:00:00Z',
      release: 'mobile@1.2.3',
      frames: ['src/QueenZone.Mobile/src/screens/news/NewsStoryScreen.tsx:40 in load'],
    },
    config,
    loop: 'telemetry',
  });
  assert.match(issue.body, /root cause is fixed/i);
  assert.match(issue.body, /7 days after deploy/);
  assert.match(issue.body, /Event count: 4/);
  assert.match(issue.body, /mobile\\@1\.2\.3/);
  assert.match(issue.body, /NewsStoryScreen/);
  assert.match(issue.body, /capture-proof -Feature web\.news\.detail/);
  assert.match(issue.body, /<!-- qz-filer v=1 keys=sentry:555 source=sentry -->/);
  assert.deepEqual(issue.labels, ['bug', 'from-telemetry', 'from-sentry', 'news']);
});

test('comments and log mention stay silent-friendly', () => {
  const comment = buildComment({ candidate, kind: 'update' });
  assert.match(comment, /Count is now \*\*2\*\*/);
  assert.match(comment, /<!-- qz-filer v=1 -->/);
  const log = buildLogComment({
    create: [{ candidate }],
    reopen: [],
    mention: '@richardorchard',
  });
  assert.match(log, /@richardorchard/);
  const silent = formatPlanSummary({ create: [], comment: [], reopen: [], skipped: [], expiredIgnores: [] });
  assert.match(silent, /Silent run/);
});

test('evidence markdown escapes mentions, closing keywords and link breakout', () => {
  assert.match(escapeMarkdown('Fixes #99 and @octocat'), /ticket 99/);
  assert.doesNotMatch(escapeMarkdown('Fixes #99 and @octocat'), /Fixes #99/);
  assert.match(escapeMarkdown('Fixes #99 and @octocat'), /\\@octocat/);
  assert.equal(escapeMarkdown('see [click](https://evil.example)'), 'see \\[click\\]\\(https://evil.example\\)');

  const issue = buildIssue({
    candidate: {
      ...candidate,
      source: 'sonar',
      evidence: [
        { url: 'https://example.test/sonar', text: 'Fixes #12 @admin [break](https://evil.example)' },
        { url: 'javascript:alert(1)', text: 'CI step Compile' },
        { text: 'Skip analyzer + Skip = true' },
      ],
    },
    config,
    loop: 'gardener',
  });
  assert.doesNotMatch(issue.body, /Fixes #12/);
  assert.match(issue.body, /ticket 12/);
  assert.ok(issue.body.includes('\\@admin'));
  assert.ok(issue.body.includes('\\[break\\]'));
  assert.doesNotMatch(issue.body, /javascript:alert/);
  assert.match(issue.body, /Skip analyzer \+ Skip = true/);
});

test('safeTitle redacts telemetry text and neutralises mentions', () => {
  const title = safeTitle('[sentry] user@example.com @admin Bearer eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.aaa.bbb');
  assert.doesNotMatch(title, /user@example\.com/);
  assert.doesNotMatch(title, /eyJhbGci/);
  assert.match(title, /\\\[email\\\]/);
  assert.match(title, /\\@admin/);
  assert.match(title, /\\\[sentry\\\]/);
});

test('issue title, storm list, log comment, and will-create lines redact external text', () => {
  const leaky = {
    source: 'sentry',
    keys: ['sentry:99'],
    title: '[sentry] leak user@example.com @oncall Password=hunter2',
    area: 'news',
    evidence: [{ text: 'token ghp_abcdefghijklmnopqrstuvwxyz1234 from 203.0.113.10' }],
    count: 3,
    firstSeen: '2026-09-27T09:00:00Z',
    lastSeen: '2026-09-27T10:00:00Z',
    frames: ['src/app.ts:1 in load user@example.com'],
    release: 'mobile@1.0.0',
  };
  const issue = buildIssue({ candidate: leaky, config, loop: 'telemetry' });
  assert.doesNotMatch(issue.title, /user@example\.com/);
  assert.doesNotMatch(issue.title, /hunter2/);
  assert.match(issue.title, /\\\[email\\\]/);
  assert.match(issue.title, /Password=\\\[secret\\\]/);
  assert.doesNotMatch(issue.body, /ghp_/);
  assert.doesNotMatch(issue.body, /203\.0\.113\.10/);
  assert.match(issue.body, /\\\[token\\\]/);
  assert.match(issue.body, /\\\[ip\\\]/);

  const storm = buildIssue({
    candidate: {
      ...leaky,
      storm: true,
      title: 'Telemetry storm 2026-09-26',
      keys: ['storm:telemetry:2026-09-26'],
      source: 'telemetry',
      stormCandidates: [leaky],
    },
    config,
    loop: 'telemetry',
  });
  assert.match(storm.body, /Ranked signals/);
  assert.doesNotMatch(storm.body, /user@example\.com/);
  assert.match(storm.body, /\\\[email\\\]/);

  const log = buildLogComment({
    create: [{ candidate: leaky }],
    reopen: [{ issueNumber: 8, candidate: leaky }],
    mention: '@richardorchard',
  });
  assert.match(log, /@richardorchard/);
  assert.doesNotMatch(log, /user@example\.com/);
  assert.match(log, /create: .*\\\[email\\\]/);

  const summary = formatPlanSummary({
    create: [{ candidate: leaky }],
    comment: [],
    reopen: [],
    skipped: [],
    expiredIgnores: [],
  });
  assert.match(summary, /will create:/);
  assert.doesNotMatch(summary, /user@example\.com/);
  assert.doesNotMatch(summary, /hunter2/);
});

test('plan summary writes one row per action with redacted titles', () => {
  const leaky = {
    source: 'sentry',
    keys: ['sentry:99'],
    shortId: 'QUEENZONE-MOBILE-E',
    title: '[sentry] leak user@example.com @oncall',
    count: 3,
    userCount: 2,
    lastSeen: '2026-09-27T10:00:00Z',
    release: 'mobile@1.0.0',
    evidence: [{ url: 'https://sentry.io/issues/99', text: 'Sentry issue' }],
  };
  const appInsights = {
    source: 'appinsights',
    keys: ['ai:exc:NullRef'],
    title: '[appinsights] boom',
    count: 5,
    userCount: 0,
    lastSeen: '2026-09-27T10:01:00Z',
    release: 'web',
  };
  const plan = {
    create: [{ candidate: leaky }],
    comment: [{ candidate: leaky, issueNumber: 2147, kind: 'update' }],
    reopen: [{ candidate: leaky, issueNumber: 88 }],
    skipped: [
      { candidate: leaky, reason: 'comment-cooldown', issue: 2147 },
      { candidate: leaky, reason: 'ignored' },
      { candidate: leaky, reason: 'cap' },
      { candidate: leaky, reason: 'storm' },
      { candidate: leaky, reason: 'invalid' },
      { candidate: leaky, reason: 'check-gap' },
      { candidate: leaky, reason: 'closed-not-planned', issue: 12, suggestIgnore: true },
      { candidate: appInsights, reason: 'cap', issue: 9 },
    ],
    expiredIgnores: [],
  };
  const rows = planSummaryRows(plan);
  assert.equal(rows.length, 11);
  assert.deepEqual(rows.map((row) => row.action), [
    'filed',
    'updated',
    'reopened',
    'deduped',
    'ignored',
    'skipped:cap',
    'skipped:storm',
    'skipped:invalid',
    'skipped:check-gap',
    'skipped:closed-not-planned',
    'deduped',
  ]);
  assert.equal(rows[0].source, 'sentry');
  assert.equal(rows[0].id, 'QUEENZONE-MOBILE-E');
  assert.equal(rows[0].users, 2);
  assert.doesNotMatch(rows[0].title, /user@example\.com/);
  assert.match(rows[0].title, /\\\[email\\\]/);
  assert.match(rows[0].link, /https:\/\/sentry\.io\/issues\/99/);
  assert.equal(rows[1].link.includes('#2147'), true);
  assert.equal(rows.at(-1).source, 'appinsights');
  assert.equal(rows.at(-1).id, 'ai:exc:NullRef');

  const dry = planSummaryRows(plan, { dryRun: true });
  assert.deepEqual(dry.map((row) => row.action), [
    'would file',
    'would update',
    'would reopen',
    'would dedupe',
    'would ignore',
    'would skip:cap',
    'would skip:storm',
    'would skip:invalid',
    'would skip:check-gap',
    'would skip:closed-not-planned',
    'would dedupe',
  ]);

  const summary = formatPlanSummary(plan, { dryRun: true });
  assert.match(summary, /\| source \| id \| title \|/);
  assert.match(summary, /would file/);
  assert.doesNotMatch(summary, /user@example\.com/);
});

test('summary table cells escape backslashes before pipes', () => {
  const summary = formatPlanSummary({
    create: [{
      candidate: {
        source: 'appinsights',
        keys: ['ai:exc:a|b\\c'],
        title: 'plain',
        count: 1,
      },
    }],
    comment: [],
    reopen: [],
    skipped: [],
    expiredIgnores: [],
  });
  assert.match(summary, /ai:exc:a\\\|b\\\\c/);
});

test('Sentry query 4xx is a dedicated step-summary error line', () => {
  const summary = formatPlanSummary(
    { create: [], comment: [], reopen: [], skipped: [], expiredIgnores: [] },
    {
      warnings: [
        'sentry: Sentry query rejected: is:unresolved is:escalating lastSeen:-28h (HTTP 400): unknown filter',
      ],
    },
  );
  assert.match(summary, /Sentry query rejected: is:unresolved is:escalating lastSeen:-28h \(HTTP 400\)/);
  assert.doesNotMatch(summary, /Silent run/);
});

test('azure collect warnings are visible in the plan summary', () => {
  const silent = formatPlanSummary(
    { create: [], comment: [], reopen: [], skipped: [], expiredIgnores: [] },
    { warnings: ['azure: azure-graph-failed'] },
  );
  assert.match(silent, /warnings: azure: azure-graph-failed/);
  assert.match(silent, /not a clean zero-alert no-op/);
  assert.doesNotMatch(silent, /Silent run/);
});

test('storm issue lists the ranked signals', () => {
  const issue = buildIssue({
    candidate: {
      ...candidate,
      storm: true,
      title: 'Gardener storm 2026-09-26',
      keys: ['storm:gardener:2026-09-26'],
      source: 'gardener',
      stormCandidates: [candidate],
    },
    config,
    loop: 'gardener',
  });
  assert.match(issue.body, /Ranked signals/);
  assert.match(issue.body, /csharp\.regex-timeout/);
});
