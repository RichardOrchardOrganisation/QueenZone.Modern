import { mkdirSync, mkdtempSync, readFileSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { test } from 'node:test';
import assert from 'node:assert/strict';
import {
  BASELINE_PATH,
  compareWithBaseline,
  countUnlinked,
  findSuppressions,
  formatBaseline,
  isScannedPath,
  issueLinkPattern,
  loadPolicy,
  listSourceFiles,
  main,
  readRepo,
  totalsByKind,
} from './check-suppressions.mjs';

test('findSuppressions finds each kind and marks issue-linked lines', () => {
  const text = [
    '// eslint-disable-next-line react-hooks/exhaustive-deps -- reason',
    '// eslint-disable-next-line react-hooks/exhaustive-deps -- removed by (#1821)',
    '#pragma warning disable CS8509',
    '#pragma warning restore CS8509',
    '[ExcludeFromCodeCoverage(Justification = "SQL Server only")]',
    '// @ts-ignore',
    '// @ts-expect-error see https://github.com/owner/repo/issues/42',
    'var x = 1; // NOSONAR',
    '[SuppressMessage("Style", "IDE0001")]',
    '<NuGetAuditSuppress Include="https://github.com/advisories/GHSA-x" />',
    '/* istanbul ignore next */',
    '// HACK: patch around the SDK',
    'const text = "HACK in a string is not a comment";',
  ].join('\n');

  const hits = findSuppressions(text);
  assert.deepEqual(
    hits.map((hit) => [hit.line, hit.kind, hit.linked]),
    [
      [1, 'eslint-disable', false],
      [2, 'eslint-disable', true],
      [3, 'pragma-warning-disable', false],
      [5, 'exclude-from-code-coverage', false],
      [6, 'ts-ignore', false],
      [7, 'ts-ignore', true],
      [8, 'nosonar', false],
      [9, 'suppress-message', false],
      [10, 'nuget-audit-suppress', false],
      [11, 'coverage-ignore', false],
      [12, 'hack-comment', false],
    ],
  );
});

test('findSuppressions does not treat pragmas or colours without an issue number as links', () => {
  const [hit] = findSuppressions('#pragma warning disable EF1003 // fixed SQL');
  assert.equal(hit.linked, false);
  assert.equal(findSuppressions('// eslint-disable-line -- see &#39;quote&#39;')[0].linked, false);
  assert.equal(findSuppressions('// eslint-disable-line -- color #333')[0].linked, false);
  assert.equal(findSuppressions('// eslint-disable-line -- removed by #1821')[0].linked, false);
});

test('findSuppressions splits CRLF the same as LF', () => {
  const crlf = findSuppressions('// eslint-disable-line\r\n#pragma warning disable CS1\r\n');
  assert.deepEqual(
    crlf.map((hit) => [hit.line, hit.kind, hit.linked]),
    [
      [1, 'eslint-disable', false],
      [2, 'pragma-warning-disable', false],
    ],
  );
});

test('isScannedPath keeps source files and skips generated, vendored, and doc paths', () => {
  assert.equal(isScannedPath('src/Example.Web/Program.cs'), true);
  assert.equal(isScannedPath('app/src/App.tsx'), true);
  assert.equal(isScannedPath('Directory.Build.props'), true);
  assert.equal(isScannedPath('src/Example.Data/Migrations/20260101_Init.Designer.cs'), false);
  assert.equal(isScannedPath('src/Example.Web/wwwroot/lib/jquery/jquery.js'), false);
  assert.equal(isScannedPath('src/Example.Web/wwwroot/js/site.min.js'), false);
  assert.equal(isScannedPath('design/tokens/colors.css'), false);
  assert.equal(isScannedPath('docs/suppressions.md'), false);
  assert.equal(isScannedPath('scripts/check-suppressions.mjs'), false);
  assert.equal(isScannedPath('src\\Example.Web\\Program.cs'), true);
});

test('countUnlinked counts only unlinked hits, sorted by file and kind', () => {
  const counts = countUnlinked([
    ['b.ts', '// eslint-disable-line\n// eslint-disable-line -- (#1234)'],
    ['a.cs', '[ExcludeFromCodeCoverage]\n#pragma warning disable CS1\n[ExcludeFromCodeCoverage]'],
    ['c.ts', 'const clean = true;'],
  ]);
  assert.deepEqual(counts, {
    'a.cs': { 'exclude-from-code-coverage': 2, 'pragma-warning-disable': 1 },
    'b.ts': { 'eslint-disable': 1 },
  });
  assert.deepEqual(Object.keys(counts), ['a.cs', 'b.ts']);
  assert.deepEqual(totalsByKind(counts), {
    'eslint-disable': 1,
    'exclude-from-code-coverage': 2,
    'pragma-warning-disable': 1,
  });
});

test('compareWithBaseline reports files that gained or lost suppressions', () => {
  const baseline = { 'a.cs': { 'pragma-warning-disable': 2 }, 'gone.ts': { 'eslint-disable': 1 } };
  const current = { 'a.cs': { 'pragma-warning-disable': 1 }, 'new.ts': { 'eslint-disable': 1 } };
  assert.deepEqual(compareWithBaseline(baseline, current), {
    added: [{ file: 'new.ts', kind: 'eslint-disable', was: 0, now: 1 }],
    removed: [
      { file: 'a.cs', kind: 'pragma-warning-disable', was: 2, now: 1 },
      { file: 'gone.ts', kind: 'eslint-disable', was: 1, now: 0 },
    ],
  });
  assert.deepEqual(compareWithBaseline(current, current), { added: [], removed: [] });
});

function makeRepo(files) {
  const root = mkdtempSync(path.join(tmpdir(), 'suppressions-'));
  for (const [file, text] of Object.entries(files)) {
    mkdirSync(path.dirname(path.join(root, file)), { recursive: true });
    writeFileSync(path.join(root, file), text);
  }
  return root;
}

test('listSourceFiles skips dot-directories and build output while retaining project source', () => {
  const root = makeRepo({
    'src/a.ts': 'a',
    'src/Example.Web/bin/Release/b.cs': 'b',
    'src/Example.Web/obj/c.cs': 'c',
    'app/node_modules/pkg/d.js': 'd',
    'app/android/app/e.js': 'e',
    'app/src/f.tsx': 'f',
    '.claude/worktrees/copy/src/g.ts': 'g',
    '.git/h.js': 'h',
    'Directory.Build.props': 'i',
  });
  assert.deepEqual(listSourceFiles(root).sort(), [
    'Directory.Build.props',
    'app/android/app/e.js',
    'app/src/f.tsx',
    'src/a.ts',
  ]);
});

test('readRepo reads only scanned files', () => {
  const root = makeRepo({ 'src/a.ts': 'a', 'docs/b.md': 'b' });
  assert.deepEqual(readRepo(root, ['src/a.ts', 'docs/b.md']), [['src/a.ts', 'a']]);
});

test('main --write then check passes, and a new unlinked suppression fails', () => {
  const root = makeRepo({
    'src/a.ts': '// eslint-disable-next-line no-console -- reason\n',
    'config/.keep': '',
  });
  const logs = [];
  const errors = [];
  const io = { root, log: (line) => logs.push(line), error: (line) => errors.push(line) };

  assert.equal(main(['--write'], io), 0);
  const baseline = JSON.parse(readFileSync(path.join(root, BASELINE_PATH), 'utf8'));
  assert.deepEqual(baseline.files, { 'src/a.ts': { 'eslint-disable': 1 } });
  assert.equal(formatBaseline(baseline.files), readFileSync(path.join(root, BASELINE_PATH), 'utf8'));

  assert.equal(main([], io), 0);

  writeFileSync(path.join(root, 'src/a.ts'), '// eslint-disable-next-line no-console -- reason\n// eslint-disable-line\n');
  assert.equal(main([], io), 1);
  assert.match(errors.join('\n'), /src\/a\.ts: eslint-disable 1 -> 2/);

  errors.length = 0;
  writeFileSync(path.join(root, 'src/a.ts'), '// eslint-disable-next-line no-console -- reason\n// eslint-disable-line -- (#1234)\n');
  assert.equal(main([], io), 0);

  writeFileSync(path.join(root, 'src/a.ts'), 'const clean = true;\n');
  assert.equal(main([], io), 1);
  assert.match(errors.join('\n'), /fewer suppressions than the baseline/);

  logs.length = 0;
  writeFileSync(path.join(root, 'src/a.ts'), '// eslint-disable-line\n');
  assert.equal(main(['--list'], io), 0);
  assert.deepEqual(logs, ['src/a.ts:1 [eslint-disable] // eslint-disable-line']);
});

test('project policy skips configured generated directories and sets the issue-link digits', () => {
  const root = makeRepo({
    'development-standards.json': JSON.stringify({ version: 1, suppressions: { skippedPaths: ['app/android'], minIssueDigits: 2 } }),
    'app/android/app/e.js': '// eslint-disable-line\n',
    'config/suppression-baseline.json': '{"files":{}}\n',
    'app/src/f.ts': '// eslint-disable-line -- reason (#7)\n// eslint-disable-line -- reason (#1234)\n',
  });
  const policy = loadPolicy(root);
  assert.deepEqual([...policy.skippedPaths], ['app/android']);
  assert.deepEqual(listSourceFiles(root, '', policy.skippedPaths).sort(), ['app/src/f.ts', 'config/suppression-baseline.json', 'development-standards.json']);
  assert.deepEqual(findSuppressions('// eslint-disable-line -- (#7)', policy).map((hit) => hit.linked), [false]);
  assert.deepEqual(findSuppressions('// eslint-disable-line -- (#7)').map((hit) => hit.linked), [true]);

  const logs = [];
  const io = { root, log: (line) => logs.push(line), error: () => {} };
  assert.equal(main(['--write'], io), 0);
  const baseline = JSON.parse(readFileSync(path.join(root, BASELINE_PATH), 'utf8'));
  assert.deepEqual(baseline.files, { 'app/src/f.ts': { 'eslint-disable': 1 } });
  logs.length = 0;
  assert.equal(main(['--list'], io), 0);
  assert.deepEqual(logs, ['app/src/f.ts:1 [eslint-disable] // eslint-disable-line -- reason (#7)']);
});

test('policy defaults without configuration and rejects unsafe or invalid settings', () => {
  assert.equal(loadPolicy(makeRepo({ 'src/a.ts': 'a' })).skippedPaths.size, 0);
  assert.equal(loadPolicy(makeRepo({ 'development-standards.json': '{"version":1}' })).issueLink.test('(#1)'), true);
  assert.equal(issueLinkPattern(3).test('(#12)'), false);
  assert.equal(issueLinkPattern(3).test('https://github.com/org/repo/issues/1'), true);
  for (const suppressions of [{ skippedPaths: ['../x'] }, { skippedPaths: ['/abs'] }, { skippedPaths: ['gen/'] }, { skippedPaths: 'gen' }, { minIssueDigits: 0 }, { minIssueDigits: 1.5 }]) {
    const root = makeRepo({ 'development-standards.json': JSON.stringify({ version: 1, suppressions }) });
    assert.throws(() => loadPolicy(root), /suppressions\./, JSON.stringify(suppressions));
  }
});
