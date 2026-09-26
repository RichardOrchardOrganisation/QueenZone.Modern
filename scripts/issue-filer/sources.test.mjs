import { readFileSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { loadFilerFiles, repoRootFrom } from './config.mjs';
import { collect as collectCi, groupFailedRuns } from './sources/ci.mjs';
import { collect as collectReview } from './sources/review.mjs';
import { collect as collectSonar, groupSonarIssues } from './sources/sonar.mjs';
import { collect as collectSuppressions, parseSuppressionDiff } from './sources/suppressions.mjs';

const here = path.dirname(fileURLToPath(import.meta.url));
const { config } = loadFilerFiles(repoRootFrom());

function loadFixture(name) {
  return JSON.parse(readFileSync(path.join(here, 'fixtures', name), 'utf8'));
}

test('review source groups tags by rule and reports malformed tags', async () => {
  const fixture = loadFixture('review-comments.json');
  const malformed = [];
  const candidates = await collectReview({
    config,
    findingRules: [],
    malformed,
    pulls: fixture.pulls,
    issueComments: fixture.issueComments,
    reviewComments: fixture.reviewComments,
    reviews: fixture.reviews,
    since: new Date('2026-09-01T00:00:00Z'),
    lookbackDays: 7,
  });
  const timeout = candidates.find((item) => item.rule === 'csharp.regex-timeout');
  assert.ok(timeout);
  assert.equal(timeout.count, 2);
  assert.equal(timeout.source, 'review');
  assert.equal(timeout.level, 'L2');
  assert.ok(timeout.keys.includes('review:csharp.regex-timeout'));
  assert.ok(malformed.some((item) => item.fields.rule === 'not-a-rule'));
  const docs = candidates.find((item) => item.rule === 'docs.agents-md-drift');
  assert.equal(docs.count, 1);
});

test('review source does not read the 60-day backfill on a weekly lookback', async () => {
  const candidates = await collectReview({
    config,
    findingRules: [],
    pulls: [],
    lookbackDays: 7,
    root: repoRootFrom(),
    since: new Date('2026-09-19T00:00:00Z'),
  });
  assert.deepEqual(candidates, []);
});

test('sonar fixture groups by rule', () => {
  const fixture = loadFixture('sonar-search.json');
  const candidates = groupSonarIssues(fixture.issues, { config });
  const complexity = candidates.find((item) => item.rule === 'javascript:S3776');
  assert.equal(complexity.count, 2);
  assert.equal(complexity.keys[0], 'sonar:javascript:S3776');
  assert.equal(candidates.find((item) => item.rule === 'javascript:S1135').count, 1);
});

test('sonar collect uses the injected search and swallows API errors', async () => {
  const fixture = loadFixture('sonar-search.json');
  const found = await collectSonar({
    config,
    since: new Date('2026-09-19T00:00:00Z'),
    sonarSearch: async () => fixture.issues,
  });
  assert.equal(found.length, 2);
  const warnings = [];
  const failed = await collectSonar({
    config,
    warnings,
    since: new Date('2026-09-19T00:00:00Z'),
    sonarSearch: async () => {
      throw new Error('boom');
    },
  });
  assert.deepEqual(failed, []);
  assert.match(warnings[0], /boom/);
});

test('ci source files only recurring failures on two SHAs', () => {
  const fixture = loadFixture('failed-runs.json');
  const candidates = groupFailedRuns(fixture.runs, fixture.jobsByRunId);
  assert.equal(candidates.length, 1);
  assert.match(candidates[0].title, /Run tests/);
  assert.equal(candidates[0].count, 3);
  assert.equal(candidates[0].source, 'ci');
});

test('ci collect can use fixture runs through ctx', async () => {
  const fixture = loadFixture('failed-runs.json');
  const candidates = await collectCi({
    failedRuns: fixture.runs,
    jobsByRunId: fixture.jobsByRunId,
  });
  assert.equal(candidates[0].count, 3);
});

test('suppressions parse added lines from a git log patch', () => {
  const patch = readFileSync(path.join(here, 'fixtures', 'suppression.diff'), 'utf8');
  const candidates = parseSuppressionDiff(patch, config.suppressionPatterns, { config });
  const pragma = candidates.find((item) => item.keys[0] === 'suppressions:pragma-warning-disable');
  assert.equal(pragma.count, 2);
  assert.equal(pragma.area, 'web');
  const nosonar = candidates.find((item) => item.keys[0] === 'suppressions:nosonar');
  assert.equal(nosonar.count, 1);
});

test('suppressions collect reads ctx.patch', async () => {
  const patch = readFileSync(path.join(here, 'fixtures', 'suppression.diff'), 'utf8');
  const candidates = await collectSuppressions({ config, patch, since: new Date('2026-09-19T00:00:00Z') });
  assert.ok(candidates.some((item) => item.keys[0] === 'suppressions:pragma-warning-disable'));
});
