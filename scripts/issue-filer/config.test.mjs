import { test } from 'node:test';
import assert from 'node:assert/strict';
import {
  areaForFile,
  loadFilerFiles,
  repoRootFrom,
  validateConfig,
  validateFilerFiles,
  validateFindingRules,
  validateIgnore,
} from './config.mjs';

test('repo config, ignore list, and finding-rules validate', () => {
  const result = validateFilerFiles(repoRootFrom());
  assert.deepEqual(result.errors, []);
  assert.equal(result.config.caps.gardener.maxIssues, 2);
  assert.equal(result.config.caps.telemetry.maxIssues, 3);
  assert.equal(result.config.labels.guardrail, 'guardrail');
  assert.ok(Array.isArray(result.ignore.entries));
  assert.ok(Array.isArray(result.findingRules));
});

test('area mapping uses feature-map prefixes', () => {
  const { config } = loadFilerFiles(repoRootFrom());
  assert.equal(areaForFile('src/QueenZone.Web/Pages/News/Index.cshtml', config.areas), 'news');
  assert.equal(areaForFile('src/QueenZone.Mobile/src/screens/photos/PhotoViewerScreen.tsx', config.areas), 'photos');
  assert.equal(areaForFile('scripts/issue-filer/run.mjs', config.areas), 'scripts');
  assert.equal(areaForFile('nope.txt', config.areas), 'unknown');
});

test('validators reject broken documents', () => {
  assert.ok(validateConfig({}).length > 0);
  assert.ok(validateIgnore({ entries: [{ match: {}, reason: '' }] }).length > 0);
  assert.ok(validateFindingRules([{ id: 'bad', title: 'x', level: 'L2', check: null }]).length > 0);
  assert.deepEqual(validateFindingRules([]), []);
});
