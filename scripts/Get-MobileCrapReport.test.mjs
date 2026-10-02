import { mkdirSync, mkdtempSync, readFileSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { test } from 'node:test';
import assert from 'node:assert/strict';
import {
  crapScore,
  getCrapRows,
  main,
  parseEslintComplexity,
  parseLcovLineHits,
  renderCsv,
  renderSummaryMarkdown,
} from './Get-MobileCrapReport.mjs';

const repoRoot = path.resolve('/repo');
const filePath = path.join(repoRoot, 'src/QueenZone.Mobile/src/screens/Sample.tsx');
const repoPath = 'src/QueenZone.Mobile/src/screens/Sample.tsx';

function loc(startLine, startColumn, endLine, endColumn) {
  return { start: { line: startLine, column: startColumn }, end: { line: endLine, column: endColumn } };
}

// export function Sample(props) {            line 1, body 1:31..12:1
//   if (props.a) { ... }                      statements 2, 3 (covered)
//   const items = props.list.map((x) => {     arrow head `=>` at 4:37, body 4:40..7:3
//     if (x) return 1;                        statement 5 (uncovered)
//     return 2;                               statement 6 (uncovered)
//   });
//   return items;                             statement 8 (covered)
// }
// function untested(a, b) {                   line 14, body 14:24..20:1
//   ...                                       statements 15, 16 (uncovered)
// }
function sampleIstanbul() {
  return {
    [filePath]: {
      path: filePath,
      fnMap: {
        0: { name: 'Sample', decl: loc(1, 16, 1, 22), loc: loc(1, 31, 12, 1) },
        1: { name: '(anonymous_1)', decl: loc(4, 31, 4, 32), loc: loc(4, 40, 7, 3) },
        2: { name: 'untested', decl: loc(14, 9, 14, 17), loc: loc(14, 24, 20, 1) },
      },
      f: { 0: 1, 1: 0, 2: 0 },
      statementMap: {
        0: loc(2, 2, 2, 20),
        1: loc(3, 4, 3, 20),
        2: loc(4, 2, 7, 5),
        3: loc(5, 4, 5, 20),
        4: loc(6, 4, 6, 13),
        5: loc(8, 2, 8, 15),
        6: loc(15, 2, 15, 20),
        7: loc(16, 2, 16, 20),
      },
      s: { 0: 1, 1: 1, 2: 1, 3: 0, 4: 0, 5: 1, 6: 0, 7: 0 },
    },
  };
}

// ESLint reports at the function head, 1-based columns.
function sampleEslint() {
  return [
    {
      filePath,
      messages: [
        { ruleId: 'complexity', line: 1, column: 8, message: "Function 'Sample' has a complexity of 3. Maximum allowed is 0." },
        { ruleId: 'complexity', line: 4, column: 37, message: 'Arrow function has a complexity of 2. Maximum allowed is 0.' },
        { ruleId: 'complexity', line: 14, column: 1, message: "Function 'untested' has a complexity of 6. Maximum allowed is 0." },
        { ruleId: 'complexity', line: 30, column: 3, message: 'Class field initializer has a complexity of 2. Maximum allowed is 0.' },
        { ruleId: 'no-unused-vars', line: 2, column: 1, message: "'x' is defined but never used." },
      ],
    },
  ];
}

test('crapScore matches the CRAP formula boundaries', () => {
  assert.equal(crapScore(5, 0), 30);
  assert.equal(crapScore(30, 1), 30);
  assert.equal(crapScore(6, 0), 42);
});

test('parseEslintComplexity keeps complexity messages and converts to 0-based columns', () => {
  const complexity = parseEslintComplexity(sampleEslint(), { repoRoot });
  assert.deepEqual(complexity.get(repoPath), [
    { line: 1, column: 7, complexity: 3 },
    { line: 4, column: 36, complexity: 2 },
    { line: 14, column: 0, complexity: 6 },
  ]);
});

test('getCrapRows matches ESLint heads to Istanbul bodies and scores innermost statements', () => {
  const { rows, unmatched } = getCrapRows({
    istanbul: sampleIstanbul(),
    complexity: parseEslintComplexity(sampleEslint(), { repoRoot }),
    repoRoot,
  });

  assert.equal(unmatched, 0);
  assert.deepEqual(
    rows.map((row) => [row.Method, row.Complexity, row.LineCoverage, row.Crap]),
    [
      ['untested', 6, 0, 42],
      ['Sample::lambda', 2, 0, 6],
      ['Sample', 3, 100, 3],
    ],
  );
  assert.equal(rows[0].File, repoPath);
  assert.equal(rows[0].Class, 'Sample');
});

test('a parameter-default arrow does not steal its enclosing function head', () => {
  // export async function upsert(          head `async` at 1:7, name at 1:22
  //   entry,
  //   isCurrent = () => true,              default arrow: decl 3:14, body 3:20..3:24
  // ) {                                    body 4:3..9:1
  const istanbul = {
    [filePath]: {
      path: filePath,
      fnMap: {
        0: { name: 'upsert', decl: loc(1, 22, 1, 28), loc: loc(4, 3, 9, 1) },
        1: { name: '(anonymous_1)', decl: loc(3, 14, 3, 15), loc: loc(3, 20, 3, 24) },
      },
      statementMap: { 0: loc(3, 20, 3, 24), 1: loc(5, 2, 5, 20) },
      s: { 0: 1, 1: 1 },
    },
  };
  const eslint = [
    {
      filePath,
      messages: [
        { ruleId: 'complexity', line: 1, column: 8, message: "Async function 'upsert' has a complexity of 4. Maximum allowed is 0." },
        { ruleId: 'complexity', line: 3, column: 17, message: 'Arrow function has a complexity of 1. Maximum allowed is 0.' },
      ],
    },
  ];
  const { rows, unmatched } = getCrapRows({ istanbul, complexity: parseEslintComplexity(eslint, { repoRoot }), repoRoot });
  assert.equal(unmatched, 0);
  assert.deepEqual(
    rows.map((row) => [row.Method, row.Complexity]),
    [
      ['upsert', 4],
      ['upsert::lambda', 1],
    ],
  );
});

test('a named callback on the same line does not steal an arrow head', () => {
  // const onPress = () => list.map(function render(item) { ... });
  //   outer arrow: decl 1:16, `=>` head 1:19, body 1:22..1:70
  //   render: decl (name) 1:40, `function` head 1:31, body 1:53..1:68
  const istanbul = {
    [filePath]: {
      path: filePath,
      fnMap: {
        0: { name: 'onPress', decl: loc(1, 16, 1, 17), loc: loc(1, 22, 1, 70) },
        1: { name: 'render', decl: loc(1, 40, 1, 46), loc: loc(1, 53, 1, 68) },
      },
      statementMap: { 0: loc(1, 22, 1, 70), 1: loc(1, 55, 1, 66) },
      s: { 0: 1, 1: 0 },
    },
  };
  const eslint = [
    {
      filePath,
      messages: [
        { ruleId: 'complexity', line: 1, column: 20, message: 'Arrow function has a complexity of 2. Maximum allowed is 0.' },
        { ruleId: 'complexity', line: 1, column: 32, message: "Function 'render' has a complexity of 7. Maximum allowed is 0." },
      ],
    },
  ];
  const { rows } = getCrapRows({ istanbul, complexity: parseEslintComplexity(eslint, { repoRoot }), repoRoot });
  assert.deepEqual(
    rows.map((row) => [row.Method, row.Complexity]),
    [
      ['render', 7],
      ['onPress', 2],
    ],
  );
});

test('an arrow stored in an object property matches its key head', () => {
  //   getItem: (key) => storage.getItem(key),   ESLint "Method 'getItem'" head at key 1:4;
  //                                             arrow decl 1:13, body 1:22..1:42
  const istanbul = {
    [filePath]: {
      path: filePath,
      fnMap: { 0: { name: '(anonymous_1)', decl: loc(1, 13, 1, 14), loc: loc(1, 22, 1, 42) } },
      statementMap: { 0: loc(1, 22, 1, 42) },
      s: { 0: 0 },
    },
  };
  const eslint = [
    {
      filePath,
      messages: [{ ruleId: 'complexity', line: 1, column: 5, message: "Method 'getItem' has a complexity of 3. Maximum allowed is 0." }],
    },
  ];
  const { rows, unmatched } = getCrapRows({ istanbul, complexity: parseEslintComplexity(eslint, { repoRoot }), repoRoot });
  assert.equal(unmatched, 0);
  assert.deepEqual(rows.map((row) => [row.Method, row.Complexity, row.Crap]), [['<module>::lambda', 3, 12]]);
});

test('Node lcov hits cover statements on the same line', () => {
  const lcov = [`SF:${filePath}`, 'DA:15,1', 'DA:16,2', 'end_of_record'].join('\n');
  const nodeHits = parseLcovLineHits(lcov, { repoRoot });
  const { rows } = getCrapRows({
    istanbul: sampleIstanbul(),
    complexity: parseEslintComplexity(sampleEslint(), { repoRoot }),
    nodeHits,
    repoRoot,
  });
  const untested = rows.find((row) => row.Method === 'untested');
  assert.equal(untested.LineCoverage, 100);
  assert.equal(untested.Crap, 6);
});

test('getCrapRows skips test files and heads with no instrumented function', () => {
  const testFile = path.join(repoRoot, 'src/QueenZone.Mobile/src/screens/Sample.test.tsx');
  const istanbul = { ...sampleIstanbul(), [testFile]: { ...sampleIstanbul()[filePath], path: testFile } };
  const eslint = [
    ...sampleEslint(),
    { filePath, messages: [{ ruleId: 'complexity', line: 40, column: 1, message: "Function 'late' has a complexity of 9. Maximum allowed is 0." }] },
  ];
  const { rows, unmatched } = getCrapRows({ istanbul, complexity: parseEslintComplexity(eslint, { repoRoot }), repoRoot });
  assert.equal(rows.length, 3);
  assert.equal(unmatched, 1);
});

test('renderCsv and renderSummaryMarkdown report the scored rows', () => {
  const { rows } = getCrapRows({
    istanbul: sampleIstanbul(),
    complexity: parseEslintComplexity(sampleEslint(), { repoRoot }),
    repoRoot,
  });
  const csv = renderCsv(rows).trim().split('\n');
  assert.equal(csv[0], '"Crap","Complexity","LineCoverage","Lines","Method","Class","File","Line"');
  assert.equal(csv.length, 4);

  const summary = renderSummaryMarkdown(rows, { threshold: 30, top: 2 });
  assert.match(summary, /Functions above 30: 1 \(1 with no coverage\)/);
  assert.match(summary, /`untested`/);
  assert.doesNotMatch(summary, /`Sample`/);
});

test('main reads coverage and precomputed ESLint output and writes both reports', async () => {
  const root = mkdtempSync(path.join(tmpdir(), 'mobile-crap-'));
  const mobileFile = path.join(root, 'src/QueenZone.Mobile/src/screens/Sample.tsx');
  const istanbul = { [mobileFile]: { ...sampleIstanbul()[filePath], path: mobileFile } };
  const eslint = sampleEslint().map((result) => ({ ...result, filePath: mobileFile }));

  const reports = path.join(root, 'coverage');
  mkdirSync(path.join(reports, 'jest'), { recursive: true });
  writeFileSync(path.join(reports, 'jest', 'coverage-final.json'), JSON.stringify(istanbul));
  const eslintPath = path.join(root, 'eslint.json');
  writeFileSync(eslintPath, JSON.stringify(eslint));
  const output = path.join(root, 'out');

  const rows = await main(['--repoRoot', root, '--reports', reports, '--complexity', eslintPath, '--output', output]);
  assert.equal(rows.length, 3);
  assert.match(readFileSync(path.join(output, 'crap-report.csv'), 'utf8'), /"untested"/);
  assert.match(readFileSync(path.join(output, 'crap-summary.md'), 'utf8'), /Functions scored: 3/);
});

test('main fails closed when Jest coverage is missing', async () => {
  const root = mkdtempSync(path.join(tmpdir(), 'mobile-crap-missing-'));
  await assert.rejects(main(['--repoRoot', root, '--reports', path.join(root, 'coverage')]), /Missing Jest coverage/);
});
