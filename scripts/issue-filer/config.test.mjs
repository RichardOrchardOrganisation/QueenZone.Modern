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

function validIgnoreEntry(overrides = {}) {
  return {
    match: { source: 'sentry', key: 'sentry:1' },
    reason: 'tracked',
    expires: '2027-01-01',
    ...overrides,
  };
}

test('ignore ceilings accept omitted or positive integers and reject bad values', () => {
  assert.deepEqual(validateIgnore({ entries: [validIgnoreEntry()] }), []);
  assert.deepEqual(validateIgnore({ entries: [validIgnoreEntry({ maxUsers: 1 })] }), []);
  assert.deepEqual(validateIgnore({ entries: [validIgnoreEntry({ maxEvents: 3 })] }), []);
  assert.deepEqual(validateIgnore({ entries: [validIgnoreEntry({ maxUsers: 1, maxEvents: 2 })] }), []);

  for (const [field, value] of [
    ['maxUsers', 0],
    ['maxUsers', -1],
    ['maxUsers', 1.5],
    ['maxUsers', '1'],
    ['maxUsers', null],
    ['maxEvents', 0],
    ['maxEvents', -2],
    ['maxEvents', Number.NaN],
  ]) {
    const errors = validateIgnore({ entries: [validIgnoreEntry({ [field]: value })] });
    assert.ok(errors.some((error) => error.includes(`${field} must be a positive integer`)), String(value));
  }
});

test('repo ignore list includes a users ceiling on the watchdog entry', () => {
  const { ignore } = loadFilerFiles(repoRootFrom());
  const watchdog = ignore.entries.find((entry) => entry.match?.key === 'sentry:7775729576');
  assert.equal(watchdog.maxUsers, 1);
  assert.equal(watchdog.maxEvents, undefined);
  assert.equal(watchdog.expires, '2026-11-05');
  assert.match(watchdog.reason, /#2147/);
  assert.deepEqual(validateIgnore(ignore), []);
});
