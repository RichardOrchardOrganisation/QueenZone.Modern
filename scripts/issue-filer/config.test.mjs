import { mkdtempSync, mkdirSync, writeFileSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import path from 'node:path';
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
  validateReviewChecklist,
  readJson,
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

const today = '2026-11-18';
const checklistRule = { id: 'test.weak-contract-assertion', title: 'Weak assertion', level: 'L2', check: null };

function checklistItem(overrides = {}) {
  return {
    id: checklistRule.id,
    paths: ['tests/**'],
    check: 'Assert the contract.',
    source: { type: 'repeated-finding', ref: checklistRule.id },
    automatedCheck: null,
    owner: 'richardorchard',
    expires: '2027-05-18',
    ...overrides,
  };
}

function checklistErrors(overrides = {}, rules = [checklistRule]) {
  return validateReviewChecklist({ version: 1, items: [checklistItem(overrides)] }, rules, today);
}

test('seed-shaped checklist and committed seed validate with a fixed UTC date', () => {
  assert.deepEqual(checklistErrors(), []);
  const root = repoRootFrom();
  assert.deepEqual(validateReviewChecklist(
    readJson(path.join(root, 'config', 'review-checklist.json')),
    loadFilerFiles(root).findingRules,
    today,
  ), []);
});

test('checklist requires a nonempty owner', () => {
  for (const owner of [undefined, null, '', '  ', 42]) {
    assert.match(checklistErrors({ owner }).join('\n'), /owner must be a nonempty string/);
  }
});

test('checklist requires a real UTC calendar expiry date', () => {
  for (const expires of [undefined, null, '', 20270518, '2027-5-18', '2027-02-29', '2026-04-31',
    '2027-00-01', '2027-13-01', '2027-05-00', '2027-05-18T00:00:00Z']) {
    assert.match(checklistErrors({ expires }).join('\n'), /expires must be a UTC calendar date/);
  }
  assert.deepEqual(checklistErrors({ expires: '2028-02-29' }), []);
});

test('checklist expires only after its expiry day', () => {
  assert.match(checklistErrors({ expires: '2026-11-17' }).join('\n'), /expires is expired/);
  assert.deepEqual(checklistErrors({ expires: today }), []);
});

test('repeated findings require registered refs and matching ids', () => {
  assert.match(checklistErrors({}, []).join('\n'), /source.ref is not a registered finding rule/);
  assert.match(checklistErrors({ id: 'test.other' }).join('\n'), /id must equal source.ref/);
  assert.deepEqual(checklistErrors({ id: 'ci.runner', source: { type: 'incident', ref: '#2518' },
    automatedCheck: 'scripts/check-playwright-ubuntu.mjs' }), []);
});

test('checklist and registry checks must agree by id or repeated-finding ref', () => {
  for (const automatedCheck of [null, undefined]) {
    for (const check of [null, undefined]) {
      assert.deepEqual(checklistErrors({ automatedCheck }, [{ ...checklistRule, check }]), []);
    }
  }
  assert.deepEqual(checklistErrors({ automatedCheck: 'check.mjs' }, [{ ...checklistRule, check: 'check.mjs' }]), []);
  for (const [automatedCheck, check] of [['check.mjs', null], [null, 'check.mjs'], ['one.mjs', 'two.mjs']]) {
    assert.match(checklistErrors({ automatedCheck }, [{ ...checklistRule, check }]).join('\n'), /automatedCheck disagrees/);
  }
  assert.match(checklistErrors({ source: { type: 'incident', ref: '#123' }, automatedCheck: 'check.mjs' }).join('\n'), /automatedCheck disagrees/);
  assert.match(checklistErrors({ id: 'test.other', automatedCheck: 'check.mjs' }).join('\n'), /automatedCheck disagrees/);
});

test('checklist rejects invalid document shapes and duplicate ids', () => {
  for (const document of [null, [], {}, { version: '1', items: [] }, { version: 2, items: [] },
    { version: 1 }, { version: 1, items: {} }]) {
    assert.match(validateReviewChecklist(document, [], today).join('\n'), /requires numeric version 1 and an items array/);
  }
  for (const item of [null, [], 'item']) {
    assert.match(validateReviewChecklist({ version: 1, items: [item] }, [], today).join('\n'), /must be an object/);
  }
  assert.match(validateReviewChecklist({ version: 1, items: [checklistItem(), checklistItem()] },
    [checklistRule], today).join('\n'), /id is a duplicate/);
});

test('checklist rejects missing or invalid kit fields', () => {
  for (const field of ['id', 'check']) {
    for (const value of [undefined, null, '', ' ', 1]) {
      assert.match(checklistErrors({ [field]: value }).join('\n'), new RegExp(`${field} must be a nonempty string`));
    }
  }
  for (const paths of [undefined, null, [], 'tests/**', [''], [' '], [1]]) {
    assert.match(checklistErrors({ paths }).join('\n'), /paths must be a nonempty array/);
  }
  for (const source of [undefined, null, [], {}, { type: 'unknown', ref: '#1' },
    { type: 'incident' }, { type: 'incident', ref: '' }, { type: 'incident', ref: 1 }]) {
    assert.match(checklistErrors({ source }).join('\n'), /source requires a valid type and nonempty ref/);
  }
  for (const automatedCheck of [1, false, [], {}]) {
    assert.match(checklistErrors({ automatedCheck }).join('\n'), /automatedCheck must be a string or null/);
  }
});

test('file validation reports missing or malformed checklist alongside existing errors', (t) => {
  const root = mkdtempSync(path.join(tmpdir(), 'checklist-validation-'));
  t.after(() => rmSync(root, { recursive: true, force: true }));
  const dir = path.join(root, '.github', 'issue-filer');
  mkdirSync(dir, { recursive: true });
  const loaded = loadFilerFiles(repoRootFrom());
  for (const [name, value] of [['config', {}], ['ignore', loaded.ignore], ['finding-rules', loaded.findingRules]]) {
    writeFileSync(path.join(dir, `${name}.json`), JSON.stringify(value));
  }
  mkdirSync(path.join(root, 'config'));
  for (const content of [undefined, '{invalid JSON', JSON.stringify({ version: 2, items: [] }),
    JSON.stringify({ version: 1, items: [checklistItem({ expires: '2026-11-17' })] })]) {
    if (content !== undefined) writeFileSync(path.join(root, 'config', 'review-checklist.json'), content);
    const { errors } = validateFilerFiles(root, today);
    assert.ok(errors.some((error) => error.includes('caps.gardener')));
    assert.ok(errors.some((error) => error.includes('review-checklist')));
  }
});
