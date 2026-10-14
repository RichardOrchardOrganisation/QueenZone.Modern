import { visibleMarkdown } from './check-pr-verification.mjs';
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { mkdtempSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import path from 'node:path';
import {
  checkPullRequestVerification,
  evaluatePrVerification,
  extractVerificationSection,
  findFeatureIds,
  isIgnoredVerificationPath,
  isUiChangedPath,
  loadUiPaths,
  verificationSkipReason,
} from './check-pr-verification.mjs';
import { listUiSourcePaths, loadFeatureMap, repoRootFrom } from './check-feature-map.mjs';

const ids = new Set(['mobile.photos.viewer', 'web.home.index']);
const sources = new Set(['src/QueenZone.Mobile/src/screens/photos/PhotoViewerScreen.tsx']);

test('docs and tests are ignored, UI paths are not', () => {
  assert.equal(isIgnoredVerificationPath('docs/feature-map/README.md'), true);
  assert.equal(isIgnoredVerificationPath('src/QueenZone.Web/Pages/Index.cshtml.cs'), false);
  assert.equal(isIgnoredVerificationPath('tests/QueenZone.Web.Tests/HomeTests.cs'), true);
  assert.equal(isUiChangedPath('src/QueenZone.Mobile/src/screens/photos/PhotoViewerScreen.tsx', sources), true);
  assert.equal(isUiChangedPath('src/QueenZone.Web/Pages/Index.cshtml', sources), true);
  assert.equal(isUiChangedPath('scripts/check-feature-map.mjs', sources), false);
});

test('UI classification follows uiPaths in development-standards.json', () => {
  assert.deepEqual(loadUiPaths(), [
    'src/QueenZone.Mobile/src/screens/',
    'src/QueenZone.Mobile/src/navigation/',
    'src/QueenZone.Mobile/src/ui/',
    'src/QueenZone.Web/Pages/',
    'src/QueenZone.Web/Views/',
    'src/QueenZone.Web/wwwroot/',
  ]);
  for (const file of [
    'src/QueenZone.Mobile/src/navigation/RootNavigator.tsx',
    'src/QueenZone.Mobile/src/ui/Button.tsx',
    'src/QueenZone.Web/Views/Shared/_Layout.cshtml',
    'src/QueenZone.Web/wwwroot/css/site.css',
  ]) {
    assert.equal(isUiChangedPath(file, new Set()), true, file);
  }
  assert.equal(isUiChangedPath('src/QueenZone.Web/Api/Content/ContentApiModels.cs', new Set()), false);
  assert.equal(isUiChangedPath('src/QueenZone.Mobile/src/api/client.ts', new Set()), false);
  const custom = ['app/screens/', 'app/App.tsx'];
  assert.equal(isUiChangedPath('app/screens/Home.tsx', new Set(), custom), true);
  assert.equal(isUiChangedPath('app/App.tsx', new Set(), custom), true);
  assert.equal(isUiChangedPath('app/App.tsx.bak', new Set(), custom), false);
  assert.equal(isUiChangedPath('src/QueenZone.Web/Pages/Index.cshtml', new Set(), custom), false);
});

test('an empty or unsafe uiPaths list fails closed', () => {
  const root = mkdtempSync(path.join(tmpdir(), 'qz-ui-paths-'));
  try {
    for (const uiPaths of [[], undefined, ['../outside/'], ['/abs/'], ['src\\Pages\\']]) {
      writeFileSync(path.join(root, 'development-standards.json'), JSON.stringify({ version: 1, uiPaths }));
      assert.throws(() => loadUiPaths(root), /uiPaths/, JSON.stringify(uiPaths));
    }
  } finally {
    rmSync(root, { recursive: true, force: true });
  }
});

test('Verification section extracts declared feature IDs', () => {
  const section = extractVerificationSection(`## Summary\n\nHi\n\n## Verification\n\n- Feature ids: mobile.photos.viewer\n- Not verified: no emulator\n\n## Issues\n\nCloses #1\n`);
  assert.match(section, /mobile\.photos\.viewer/);
  assert.deepEqual(findFeatureIds(section, ids), ['mobile.photos.viewer']);
  assert.deepEqual(
    findFeatureIds('web.home.index and mobile.photos.viewer', new Set(['web.home.index', 'mobile.photos.viewer'])),
    ['mobile.photos.viewer', 'web.home.index'],
  );
});

test('UI PR without Verification fails', () => {
  const result = evaluatePrVerification({
    body: '## Summary\nchange\n',
    files: ['src/QueenZone.Web/Pages/Index.cshtml'],
    mapIds: ids,
    mobileSources: sources,
  });
  assert.equal(result.ok, false);
  assert.match(result.error, /## Verification/);
});

test('UI PR with id and Not verified fails', () => {
  const result = evaluatePrVerification({
    body: '## Verification\n\nFeature ids: mobile.photos.viewer\nNot verified: dispatch proof for mobile.photos.viewer\n',
    files: ['src/QueenZone.Mobile/src/screens/photos/PhotoViewerScreen.tsx'],
    mapIds: ids,
    mobileSources: sources,
  });
  assert.equal(result.ok, false);
  assert.deepEqual(result.ids, ['mobile.photos.viewer']);
});

test('opt-out needs the label and a skip reason', () => {
  const missingReason = evaluatePrVerification({
    body: '## Summary\nno ui\n',
    files: ['src/QueenZone.Web/wwwroot/css/site.css'],
    labels: ['no-ui-verification'],
    mapIds: ids,
    mobileSources: sources,
  });
  assert.equal(missingReason.ok, false);

  const skipped = evaluatePrVerification({
    body: 'Verification-skip-reason: token-only CSS rename, no visitor path change.\n',
    files: ['src/QueenZone.Web/wwwroot/css/site.css'],
    labels: ['no-ui-verification'],
    mapIds: ids,
    mobileSources: sources,
  });
  assert.equal(skipped.ok, true);
  assert.equal(skipped.skipped, true);
  assert.match(verificationSkipReason(skipped.skipReason ? `Verification-skip-reason: ${skipped.skipReason}` : ''), /token-only/);
});

test('Dependabot UI changes need evidence; non-UI docs PRs pass', () => {
  assert.equal(
    evaluatePrVerification({
      body: '',
      files: ['src/QueenZone.Web/Pages/Index.cshtml'],
      dependabot: true,
      mapIds: ids,
      mobileSources: sources,
    }).ok,
    false,
  );
  assert.equal(
    evaluatePrVerification({
      body: '',
      files: ['docs/feature-map/README.md', 'scripts/check-feature-map.mjs'],
      mapIds: ids,
      mobileSources: sources,
    }).ok,
    true,
  );
});

test('real feature map exposes the photo viewer source as UI', () => {
  const map = loadFeatureMap(repoRootFrom());
  const mobileSources = listUiSourcePaths(map);
  assert.ok(mobileSources.has('src/QueenZone.Mobile/src/screens/photos/PhotoViewerScreen.tsx'));
});

// pr-verification-check.yml imports this export through github-script; keep its
// ({ github, context, core }) read-only contract under test.
function fakeGitHub({ files, labels = [] }) {
  const calls = [];
  const github = {
    paginate: async (method, params) => {
      calls.push(['listFiles', params.pull_number]);
      return files.map(filename => ({ filename }));
    },
    rest: {
      pulls: { listFiles: () => {} },
      issues: {
        getLabel: async () => ({}),
        createLabel: async ({ name }) => calls.push(['createLabel', name]),
        addLabels: async ({ labels: added }) => calls.push(['addLabels', ...added]),
        removeLabel: async ({ name }) => calls.push(['removeLabel', name]),
      },
    },
  };
  const failures = [];
  const core = { info: () => {}, setFailed: (message) => failures.push(message) };
  const context = {
    repo: { owner: 'owner', repo: 'repo' },
    payload: { pull_request: { number: 7, body: '', labels: labels.map((name) => ({ name })), user: { login: 'dev' } } },
  };
  return { github, context, core, calls, failures };
}

test('read-only workflow export fails a UI PR without proof', async () => {
  const fake = fakeGitHub({ files: ['src/QueenZone.Web/Pages/Index.cshtml'] });
  const result = await checkPullRequestVerification(fake);
  assert.equal(result.ok, false);
  assert.deepEqual(fake.failures, ['UI PR is missing a ## Verification section.']);
  assert.deepEqual(fake.calls, [['listFiles', 7]]);
});

test('read-only workflow export passes a non-UI PR', async () => {
  const fake = fakeGitHub({ files: ['development-standards.json', 'docs/testing.md'], labels: ['needs-verification'] });
  const result = await checkPullRequestVerification(fake);
  assert.equal(result.ok, true);
  assert.deepEqual(fake.failures, []);
  assert.deepEqual(fake.calls, [['listFiles', 7]]);
});

const uiInput = { body: '## Verification\nFeature ids: web.home.index', files: ['src/QueenZone.Web/Pages/Index.cshtml'], mapIds: ids, headSha: 'a'.repeat(40) };
const webEvidence = { feature: 'web.home.index', platform: 'web', headSha: 'a'.repeat(40), url: 'https://github.com/o/r/actions/runs/1/artifacts/2' };
test('only validated evidence for the current head satisfies the gate', () => {
  assert.equal(evaluatePrVerification({ ...uiInput, evidence: [webEvidence] }).ok, true);
  for (const text of ['Not verified:', 'Not verified: no browser', 'https://example.com/screenshot.png', 'artifacts/proof/local.png']) {
    assert.equal(evaluatePrVerification({ ...uiInput, body: uiInput.body + '\n' + text }).ok, false);
  }
  for (const mutation of [{ headSha: 'b'.repeat(40) }, { feature: 'web.news.detail' }, { platform: 'android' }]) {
    assert.equal(evaluatePrVerification({ ...uiInput, evidence: [{ ...webEvidence, ...mutation }] }).ok, false);
  }
});
test('hidden or empty waiver reasons do not bypass evidence', () => {
  for (const reason of ['Verification-skip-reason:', '<!-- Verification-skip-reason: placeholder -->', '```\nVerification-skip-reason: hidden\n```']) {
    assert.equal(evaluatePrVerification({ ...uiInput, labels: ['no-ui-verification'], body: reason }).ok, false);
  }
  assert.equal(evaluatePrVerification({ ...uiInput, body: 'Verification-skip-reason: no browser' }).ok, false);
});
test('declared features must cover changed files and every mapped fixture', () => {
  const entries = [{ id: 'web.home.index', sources: ['src/QueenZone.Web/Pages/Index.cshtml'], specs: ['tests/SmokeTests.cs', 'tests/LayoutTests.cs'] }];
  assert.equal(evaluatePrVerification({ ...uiInput, entries, evidence: [{ ...webEvidence, fixtures: ['SmokeTests'] }] }).ok, false);
  assert.equal(evaluatePrVerification({ ...uiInput, entries, evidence: [{ ...webEvidence, fixtures: ['SmokeTests', 'LayoutTests'] }] }).ok, true);
  assert.equal(evaluatePrVerification({ ...uiInput, entries, body: '## Verification\nmobile.photos.viewer', evidence: [webEvidence] }).ok, false);
});
test('native mobile evidence requires both platforms', () => {
  const input = { ...uiInput, files: [...sources], body: '## Verification\nmobile.photos.viewer' };
  const android = { ...webEvidence, feature: 'mobile.photos.viewer', platform: 'android' };
  assert.equal(evaluatePrVerification({ ...input, evidence: [android] }).ok, false);
  assert.equal(evaluatePrVerification({ ...input, evidence: [android, { ...android, platform: 'ios' }] }).ok, true);
});

test('malformed comments and fences cannot supply an explicit waiver', () => {
  for (const body of [
    '<!--\nVerification-skip-reason: hidden unclosed comment',
    '```text\nVerification-skip-reason: hidden unclosed fence',
    '<!-- outer <!-- inner -->\n<!--\nVerification-skip-reason: hidden nested comment\n-->',
  ]) {
    assert.equal(evaluatePrVerification({ ...uiInput, labels: ['no-ui-verification'], body }).ok, false);
  }
});

test('hidden-text removal cannot reconstruct a comment delimiter', () => {
  assert.equal(visibleMarkdown('<!<!-- removed -->--'), '<!\n--');
  assert.equal(visibleMarkdown('before<!-- hidden -->after'), 'before\nafter');
});
