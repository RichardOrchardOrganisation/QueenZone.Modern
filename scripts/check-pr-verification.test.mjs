import { test } from 'node:test';
import assert from 'node:assert/strict';
import {
  evaluatePrVerification,
  extractVerificationSection,
  findFeatureIds,
  isIgnoredVerificationPath,
  isUiChangedPath,
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

test('Verification section requires an id and proof or Not verified', () => {
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

test('UI PR with id and Not verified passes', () => {
  const result = evaluatePrVerification({
    body: '## Verification\n\nFeature ids: mobile.photos.viewer\nNot verified: dispatch proof for mobile.photos.viewer\n',
    files: ['src/QueenZone.Mobile/src/screens/photos/PhotoViewerScreen.tsx'],
    mapIds: ids,
    mobileSources: sources,
  });
  assert.equal(result.ok, true);
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

test('Dependabot is exempt and non-UI docs PRs pass', () => {
  assert.equal(
    evaluatePrVerification({
      body: '',
      files: ['src/QueenZone.Web/Pages/Index.cshtml'],
      dependabot: true,
      mapIds: ids,
      mobileSources: sources,
    }).ok,
    true,
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
