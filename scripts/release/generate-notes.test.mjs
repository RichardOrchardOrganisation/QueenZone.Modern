import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import path from 'node:path';
import { test } from 'node:test';
import { fileURLToPath } from 'node:url';
import {
  PATH_CATEGORY_LABELS,
  UNRELEASED_NAME,
  UNRELEASED_TAG,
  appendCloses,
  applyTitleLabels,
  compareProdTags,
  dryRunNotes,
  extractPullNumbers,
  generateReleaseBody,
  formatCloses,
  isLatestProdTag,
  isProdTag,
  labelsFromTitle,
  listProdTags,
  parseCliArgs,
  previousProdTag,
  publishTagRelease,
  tagFromRef,
  titleLabelsToApply,
  upsertUnreleasedDraft,
} from './generate-notes.mjs';

const repoRoot = fileURLToPath(new URL('../..', import.meta.url));

function readRepo(relativePath) {
  return readFileSync(path.join(repoRoot, relativePath), 'utf8');
}

function fakeGithub({
  tags = ['v2026.10.15.1', 'unreleased', 'v1.2.3', 'v2026.10.22.1'],
  notes = '* Ship notes by @dev in https://github.com/org/repo/pull/10\n',
  releases = [],
  pulls = {},
  labels = {},
  created = [],
  updated = [],
  deleted = [],
  addedLabels = [],
} = {}) {
  return {
    async listTagNames() {
      return tags;
    },
    async generateNotes(payload) {
      return { name: payload.tag_name, body: notes, payload };
    },
    async closingIssuesForPulls(numbers) {
      const map = new Map();
      for (const number of numbers) {
        map.set(number, number === 10 ? [2379] : []);
      }
      return map;
    },
    async findReleaseByTag(tag) {
      return releases.find((release) => release.tag_name === tag && !release.draft) || null;
    },
    async findUnreleasedDrafts() {
      return releases.filter((release) => release.draft && (release.name === UNRELEASED_NAME || release.tag_name === UNRELEASED_TAG));
    },
    async createRelease(payload) {
      created.push(payload);
      return { id: 99, ...payload };
    },
    async updateRelease(id, payload) {
      updated.push({ id, ...payload });
      return { id, ...payload };
    },
    async deleteRelease(id) {
      deleted.push(id);
    },
    async deleteUnreleasedDrafts() {
      const drafts = await this.findUnreleasedDrafts();
      for (const draft of drafts) {
        deleted.push(draft.id);
      }
      return drafts.length;
    },
    async getPull(number) {
      return pulls[number] || { labels: [] };
    },
    async getLabel(name) {
      if (!labels[name]) {
        const error = new Error('not found');
        error.status = 404;
        throw error;
      }
      return labels[name];
    },
    async createLabel(payload) {
      labels[payload.name] = payload;
      return payload;
    },
    async addLabels(number, names) {
      addedLabels.push({ number, names });
    },
    created,
    updated,
    deleted,
    addedLabels,
  };
}

test('previous prod tag picks the latest older vYYYY.MM.DD.N and ignores non-prod tags', () => {
  const tags = ['unreleased', 'v1.2.3', 'v2026.10.15.1', 'v2026.10.22.1', 'v2026.10.22.2', 'v2026.10.22'];
  assert.equal(previousProdTag(tags, 'v2026.10.22.1'), 'v2026.10.15.1');
  assert.equal(previousProdTag(tags, 'v2026.10.22.2'), 'v2026.10.22.1');
  assert.equal(previousProdTag(tags, null), 'v2026.10.22.2');
  assert.equal(previousProdTag(['unreleased', 'v1.0.0'], 'v2026.10.22.1'), null);
  assert.deepEqual(listProdTags(tags), ['v2026.10.15.1', 'v2026.10.22.1', 'v2026.10.22.2']);
  assert.ok(compareProdTags('v2026.10.22.1', 'v2026.10.22.2') < 0);
  assert.equal(isLatestProdTag(tags, 'v2026.10.22.2'), true);
  assert.equal(isLatestProdTag(tags, 'v2026.10.15.1'), false);
  assert.equal(isProdTag('v2026.10.22.1'), true);
  assert.equal(isProdTag('v2026.10.22'), false);
  assert.throws(() => previousProdTag(tags, 'unreleased'), /Not a prod tag/);
});

test('appendCloses adds closing issue links per pull request line', () => {
  const body = [
    '## Features',
    '* Add notes by @dev in https://github.com/org/repo/pull/10',
    '* Unrelated by @dev in https://github.com/org/repo/pull/11',
    '**Full Changelog**: https://github.com/org/repo/compare/v2026.10.15.1...v2026.10.22.1',
  ].join('\n');
  const annotated = appendCloses(body, new Map([[10, [2379, 12]], [11, []]]));
  assert.match(annotated, /pull\/10 \(closes #12, #2379\)$/m);
  assert.match(annotated, /pull\/11$/m);
  assert.equal(extractPullNumbers(body).sort((left, right) => left - right).join(','), '10,11');
  assert.equal(formatCloses([2379, 12, 12]), '(closes #12, #2379)');
  assert.equal(appendCloses('* already https://github.com/org/repo/pull/10 (closes #1)', { 10: [2] }), '* already https://github.com/org/repo/pull/10 (closes #1)');
  assert.equal(appendCloses('* none', { 10: [1] }), '* none');
});

test('appendCloses trims trailing whitespace while preserving indentation and unrelated lines', () => {
  const line = '  * Fix https://github.com/org/repo/pull/10';
  const unrelated = '* No pull request here  \t';
  const body = `${line}${' \t\u00a0'.repeat(10_000)}\n${unrelated}`;
  assert.equal(appendCloses(body, { 10: [12] }), `${line} (closes #12)\n${unrelated}`);
});

test('title labels come from a conventional prefix unless a path category is present', () => {
  assert.deepEqual(labelsFromTitle('feat: add release notes'), ['feature']);
  assert.deepEqual(labelsFromTitle('feature/notes'), ['feature']);
  assert.deepEqual(labelsFromTitle('Fix the gate'), ['fix']);
  assert.deepEqual(labelsFromTitle('bug: crash on login'), ['fix']);
  assert.deepEqual(labelsFromTitle('Implement issue #2379'), []);
  assert.deepEqual(titleLabelsToApply('feat: mobile list', ['mobile']), []);
  assert.deepEqual(titleLabelsToApply('fix: workflow pin', ['infra']), []);
  assert.deepEqual(titleLabelsToApply('feat: already labelled', ['feature']), []);
  assert.deepEqual(titleLabelsToApply('feat: web change', []), ['feature']);
  assert.deepEqual(PATH_CATEGORY_LABELS, ['mobile', 'infra', 'dependencies']);
});

test('publish creates the tag release, appends closes, and can delete the Unreleased draft', async () => {
  const github = fakeGithub({
    releases: [{ id: 7, draft: true, name: UNRELEASED_NAME, tag_name: UNRELEASED_TAG }],
  });
  const result = await publishTagRelease(github, { tag: 'v2026.10.22.1', deleteUnreleased: true });
  assert.equal(result.previous, 'v2026.10.15.1');
  assert.match(result.body, /\(closes #2379\)/);
  assert.equal(github.created.length, 1);
  assert.equal(github.created[0].tag_name, 'v2026.10.22.1');
  assert.equal(github.created[0].draft, false);
  assert.equal(github.created[0].make_latest, 'true');
  assert.deepEqual(github.deleted, [7]);
});

test('backfill updates an existing tag release and keeps the Unreleased draft', async () => {
  const github = fakeGithub({
    releases: [
      { id: 3, draft: false, tag_name: 'v2026.10.15.1' },
      { id: 8, draft: true, name: UNRELEASED_NAME, tag_name: UNRELEASED_TAG },
    ],
  });
  const result = await publishTagRelease(github, { tag: 'v2026.10.15.1', deleteUnreleased: false });
  assert.equal(result.previous, null);
  assert.equal(github.updated[0].id, 3);
  assert.equal(github.updated[0].make_latest, 'false');
  assert.deepEqual(github.deleted, []);
  await assert.rejects(() => publishTagRelease(github, { tag: 'unreleased', deleteUnreleased: false }), /prod tag/);
});

test('draft upserts one Unreleased release from the latest prod tag', async () => {
  const github = fakeGithub({
    releases: [
      { id: 8, draft: true, name: UNRELEASED_NAME, tag_name: UNRELEASED_TAG },
      { id: 9, draft: true, name: UNRELEASED_NAME, tag_name: UNRELEASED_TAG },
    ],
  });
  const result = await upsertUnreleasedDraft(github);
  assert.equal(result.previous, 'v2026.10.22.1');
  assert.equal(github.updated[0].id, 8);
  assert.equal(github.updated[0].name, UNRELEASED_NAME);
  assert.equal(github.updated[0].draft, true);
  assert.equal(github.updated[0].target_commitish, 'main');
  assert.deepEqual(github.deleted, [9]);
});

test('closes append is best-effort when GraphQL fails', async () => {
  const warnings = [];
  const github = fakeGithub();
  github.closingIssuesForPulls = async () => {
    throw new Error('GraphQL 403');
  };
  const result = await generateReleaseBody(
    github,
    { tagName: 'v2026.10.22.1', previousTag: 'v2026.10.15.1' },
    { warn: (message) => warnings.push(message) },
  );
  assert.match(result.body, /pull\/10/);
  assert.doesNotMatch(result.body, /closes/);
  assert.match(warnings.join('\n'), /GraphQL 403/);
});

test('dry-run does not create a release', async () => {
  const github = fakeGithub();
  const result = await dryRunNotes(github, { tag: 'v2026.10.22.1' });
  assert.equal(result.previous, 'v2026.10.15.1');
  assert.match(result.body, /\(closes #2379\)/);
  assert.deepEqual(github.created, []);
  assert.equal(tagFromRef('refs/tags/v2026.10.22.1'), 'v2026.10.22.1');
  assert.deepEqual(parseCliArgs(['publish', '--tag', 'v2026.10.22.1', '--dry-run']), {
    command: 'publish',
    dryRun: true,
    tag: 'v2026.10.22.1',
  });
});

test('apply-title-labels creates missing labels and skips path-categorised PRs', async () => {
  const github = fakeGithub({ pulls: { 15: { labels: [] } } });
  const applied = await applyTitleLabels(github, { number: 15, title: 'feat: notes' });
  assert.deepEqual(applied.added, ['feature']);
  assert.deepEqual(github.addedLabels, [{ number: 15, names: ['feature'] }]);
  const skipped = await applyTitleLabels(github, { number: 16, title: 'feat: ios', existingLabels: [{ name: 'mobile' }] });
  assert.deepEqual(skipped.added, []);
});

test('release notes stay off deploy and CI gates', () => {
  const notes = readRepo('.github/workflows/release-notes.yml');
  const deploy = readRepo('.github/workflows/deploy.yml');
  const ci = readRepo('.github/workflows/ci.yml');
  const scripts = readRepo('.github/workflows/scripts-tests.yml');
  const release = readRepo('.github/release.yml');
  const labeler = readRepo('.github/labeler.yml');
  const labelerWorkflow = readRepo('.github/workflows/labeler.yml');

  assert.match(notes, /^name: Release notes\r?$/m);
  assert.match(notes, /permissions:\r?\n {2}contents: write/);
  assert.match(notes, / {4}permissions:\r?\n {6}contents: write\r?\n {6}pull-requests: read\r?\n {6}issues: read/);
  assert.doesNotMatch(notes, /id-token:/);
  assert.doesNotMatch(notes, /pull-requests: write|issues: write/);
  assert.match(notes, /group: \$\{\{ github\.event_name == 'push' && github\.ref == 'refs\/heads\/main'/);
  assert.match(notes, /cancel-in-progress: \$\{\{ github\.event_name == 'push' && github\.ref == 'refs\/heads\/main' \}\}/);
  assert.match(notes, /default: v2026\.10\.22\.1/);
  assert.match(notes, /node scripts\/release\/generate-notes\.mjs draft/);
  assert.match(notes, /node scripts\/release\/generate-notes\.mjs publish/);
  assert.match(notes, /actions\/checkout@3d3c42e5aac5ba805825da76410c181273ba90b1/);
  assert.match(notes, /actions\/setup-node@949feb2413d6458794dcd2491c4babbbce0c15c1/);
  assert.doesNotMatch(notes, /release-drafter/i);

  assert.doesNotMatch(deploy, /release-notes/);
  assert.doesNotMatch(ci, /release-notes/);
  assert.doesNotMatch(scripts, /Release notes/);

  assert.match(release, /title: Features/);
  assert.match(release, /title: Fixes/);
  assert.match(release, /title: Mobile/);
  assert.match(release, /title: Infra\/CI/);
  assert.match(release, /title: Dependencies/);
  assert.match(release, /title: Other[\s\S]*labels:\r?\n\s+- "\*"/);

  assert.match(labeler, /src\/QueenZone\.Mobile\/\*\*/);
  assert.match(labeler, /\.github\/\*\*/);
  assert.match(labeler, /infra\/\*\*/);
  assert.match(labelerWorkflow, /actions\/labeler@bf12e9b00b37c5c0ca2b87b79b2daf7891dbda13/);
  assert.match(labelerWorkflow, /apply-title-labels/);
});
