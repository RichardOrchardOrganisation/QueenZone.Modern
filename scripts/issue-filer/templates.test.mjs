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
  labelsFor,
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
    labelsFor({ ...candidate, area: 'unknown', source: 'sentry' }, config, 'telemetry'),
    ['bug', 'from-telemetry', 'needs-triage'],
  );
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
