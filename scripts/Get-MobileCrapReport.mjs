#!/usr/bin/env node
/**
 * Rank QueenZone.Mobile functions by CRAP (Change Risk Anti-Patterns) score.
 *
 * CRAP(f) = comp(f)^2 * (1 - cov(f))^3 + comp(f)
 *
 * Mobile counterpart of scripts/Get-CrapReport.ps1 (#1979). Istanbul has no
 * complexity, so comp comes from ESLint's core `complexity` rule (reported at
 * max 0, so every function is listed) and cov comes from Jest's
 * coverage-final.json, overlaid with Node test-runner lcov hits by line — the
 * same Jest-universe overlay Test-MobileCoverageGate.mjs uses.
 *
 * Each Istanbul statement belongs to the innermost function that contains it,
 * matching ESLint, which scores nested functions on their own. Anonymous
 * callbacks report as `<parent>::lambda`, like C# lambdas in the .NET report.
 *
 * Report-only: writes crap-report.csv and crap-summary.md, exits 0 however
 * many functions exceed --threshold.
 *
 *   node scripts/Get-MobileCrapReport.mjs
 *   node scripts/Get-MobileCrapReport.mjs --complexity eslint.json --output out/
 */
import { existsSync, mkdirSync, readFileSync, writeFileSync } from 'node:fs';
import { createRequire } from 'node:module';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { isCoverableRepoPath, toRepoPath } from './Test-MobileCoverageGate.mjs';

const scriptDir = path.dirname(fileURLToPath(import.meta.url));
const defaultRepoRoot = path.resolve(scriptDir, '..');
const COMPLEXITY_MESSAGE = /has a complexity of (\d+)/;

export function crapScore(complexity, coverage) {
  return complexity ** 2 * (1 - coverage) ** 3 + complexity;
}

function comparePosition(a, b) {
  return a.line - b.line || a.column - b.column;
}

function contains(loc, position) {
  return comparePosition(loc.start, position) <= 0 && comparePosition(position, loc.end) <= 0;
}

function span(loc) {
  return (loc.end.line - loc.start.line) * 100000 + (loc.end.column - loc.start.column);
}

// Istanbul's fnMap loc is the function *body* and decl is the name (or the
// node start when anonymous). ESLint reports the function *head*: the
// `function`/`async` keyword, the `=>` token, or a method/property key. A
// function owns a head that sits between its decl and its body. Otherwise the
// head precedes the decl on the same line — a named declaration's identifier
// after `function`, or `key: () => ...` — and the first such function wins.
// Parameter defaults (`isCurrent = () => true`) never own the outer head,
// because their decl..body range ends before it.
function functionForHead(functions, head) {
  let owner = null;
  let sameLine = null;
  for (const fn of functions) {
    if (comparePosition(head, fn.loc.start) >= 0) {
      continue;
    }
    if (comparePosition(fn.decl.start, head) <= 0) {
      if (owner === null || comparePosition(fn.decl.start, owner.decl.start) > 0) {
        owner = fn;
      }
    } else if (
      fn.decl.start.line === head.line &&
      (sameLine === null || comparePosition(fn.decl.start, sameLine.decl.start) < 0)
    ) {
      sameLine = fn;
    }
  }
  return owner ?? sameLine;
}

function innermost(functions, position) {
  let best = null;
  for (const fn of functions) {
    if (contains(fn.loc, position) && (best === null || span(fn.loc) < span(best.loc))) {
      best = fn;
    }
  }
  return best;
}

/** Line hits from a Node lcov report, keyed by repo path. */
export function parseLcovLineHits(contents, { repoRoot = defaultRepoRoot } = {}) {
  const hits = new Map();
  let current = null;
  for (const rawLine of contents.split(/\r?\n/)) {
    const line = rawLine.trim();
    if (line.startsWith('SF:')) {
      const repoPath = toRepoPath(line.slice(3), [], repoRoot);
      current = hits.get(repoPath) ?? new Map();
      hits.set(repoPath, current);
    } else if (current && line.startsWith('DA:')) {
      const [number, count] = line.slice(3).split(',').map(Number);
      current.set(number, (current.get(number) ?? 0) + count);
    }
  }
  return hits;
}

/** Complexity per function head position from ESLint JSON results, keyed by repo path. */
export function parseEslintComplexity(results, { repoRoot = defaultRepoRoot } = {}) {
  const byFile = new Map();
  for (const result of results) {
    const repoPath = toRepoPath(result.filePath, [], repoRoot);
    for (const message of result.messages ?? []) {
      const match = message.ruleId === 'complexity' ? COMPLEXITY_MESSAGE.exec(message.message) : null;
      // Class field initializers and static blocks are not Istanbul functions.
      if (!match || /^Class (field initializer|static block)/.test(message.message)) {
        continue;
      }
      const entries = byFile.get(repoPath) ?? [];
      // ESLint columns are 1-based; Istanbul columns are 0-based.
      entries.push({ line: message.line, column: message.column - 1, complexity: Number(match[1]) });
      byFile.set(repoPath, entries);
    }
  }
  return byFile;
}

function displayName(fn, functions) {
  if (!fn.name.startsWith('(anonymous')) {
    return fn.name;
  }
  // Parent range runs from decl to body end so parameter defaults count as inside it.
  let parent = null;
  for (const candidate of functions) {
    if (
      candidate !== fn &&
      !candidate.name.startsWith('(anonymous') &&
      contains({ start: candidate.decl.start, end: candidate.loc.end }, fn.loc.start) &&
      (parent === null || span(candidate.loc) < span(parent.loc))
    ) {
      parent = candidate;
    }
  }
  return `${parent?.name ?? '<module>'}::lambda`;
}

/** Returns how many ESLint heads had no matching Istanbul function. */
function assignComplexity(functions, entries) {
  let unmatched = 0;
  for (const entry of entries) {
    const fn = functionForHead(functions, entry);
    if (fn) {
      fn.complexity = Math.max(fn.complexity, entry.complexity);
    } else {
      unmatched += 1;
    }
  }
  return unmatched;
}

function assignStatements(functions, file, lineHits) {
  for (const [id, loc] of Object.entries(file.statementMap ?? {})) {
    const fn = innermost(functions, loc.start);
    if (!fn) {
      continue;
    }
    fn.statements += 1;
    if (Number(file.s?.[id] ?? 0) > 0 || (lineHits?.get(loc.start.line) ?? 0) > 0) {
      fn.covered += 1;
    }
  }
}

function toRow(fn, functions, repoPath) {
  const coverage = fn.covered / fn.statements;
  return {
    Crap: Math.round(crapScore(fn.complexity, coverage) * 10) / 10,
    Complexity: fn.complexity,
    LineCoverage: Math.round(coverage * 1000) / 10,
    Lines: fn.statements,
    Method: displayName(fn, functions),
    Class: path.posix.basename(repoPath).replace(/\.(ts|tsx)$/, ''),
    File: repoPath,
    Line: fn.loc.start.line,
  };
}

function scoreFile(file, repoPath, complexity, nodeHits) {
  const functions = Object.values(file.fnMap ?? {}).map((fn) => ({
    name: fn.name ?? '(anonymous)',
    decl: fn.decl ?? fn.loc,
    loc: fn.loc,
    complexity: 0,
    statements: 0,
    covered: 0,
  }));
  const unmatched = assignComplexity(functions, complexity.get(repoPath) ?? []);
  assignStatements(functions, file, nodeHits.get(repoPath));
  const rows = functions
    .filter((fn) => fn.statements > 0 && fn.complexity > 0)
    .map((fn) => toRow(fn, functions, repoPath));
  return { rows, unmatched };
}

/**
 * Score every function in Jest's coverage-final.json. `complexity` is the
 * output of parseEslintComplexity, `nodeHits` of parseLcovLineHits.
 */
export function getCrapRows({ istanbul, complexity, nodeHits = new Map(), repoRoot = defaultRepoRoot }) {
  const rows = [];
  let unmatched = 0;

  for (const [filePath, file] of Object.entries(istanbul)) {
    const repoPath = toRepoPath(file.path ?? filePath, [], repoRoot);
    if (isCoverableRepoPath(repoPath)) {
      const scored = scoreFile(file, repoPath, complexity, nodeHits);
      rows.push(...scored.rows);
      unmatched += scored.unmatched;
    }
  }

  rows.sort(
    (a, b) => b.Crap - a.Crap || b.Complexity - a.Complexity || a.File.localeCompare(b.File) || a.Line - b.Line,
  );
  return { rows, unmatched };
}

function csvCell(value) {
  return `"${String(value).replaceAll('"', '""')}"`;
}

export function renderCsv(rows) {
  const columns = ['Crap', 'Complexity', 'LineCoverage', 'Lines', 'Method', 'Class', 'File', 'Line'];
  return [columns.map(csvCell).join(','), ...rows.map((row) => columns.map((c) => csvCell(row[c])).join(','))].join('\n') + '\n';
}

export function renderSummaryMarkdown(rows, { threshold = 30, top = 20 } = {}) {
  const over = rows.filter((row) => row.Crap > threshold);
  const uncovered = over.filter((row) => row.LineCoverage === 0);
  const lines = [
    '## Mobile CRAP score (Change Risk Anti-Patterns)',
    '',
    `CRAP = complexity^2 x (1 - statement coverage)^3 + complexity. Above ${threshold} is high change risk.`,
    '',
    `- Functions scored: ${rows.length}`,
    `- Functions above ${threshold}: ${over.length} (${uncovered.length} with no coverage)`,
    '',
    '| CRAP | Complexity | Coverage | Function | Location |',
    '| ---: | ---: | ---: | --- | --- |',
  ];
  for (const row of rows.slice(0, top)) {
    lines.push(`| ${row.Crap} | ${row.Complexity} | ${row.LineCoverage}% | \`${row.Method}\` | ${row.File}:${row.Line} |`);
  }
  return lines.join('\n') + '\n';
}

async function runEslintComplexity(mobileRoot) {
  const require = createRequire(path.join(mobileRoot, 'package.json'));
  let eslintModule;
  try {
    eslintModule = require('eslint');
  } catch {
    throw new Error(`ESLint is not installed under '${mobileRoot}'. Run npm ci there first, or pass --complexity.`);
  }
  const eslint = new eslintModule.ESLint({
    cwd: mobileRoot,
    overrideConfig: { rules: { complexity: ['warn', 0] } },
    ruleFilter: ({ ruleId }) => ruleId === 'complexity',
  });
  return eslint.lintFiles(['src']);
}

function parseArgs(argv) {
  const args = {
    repoRoot: defaultRepoRoot,
    reports: path.join(defaultRepoRoot, 'src/QueenZone.Mobile/coverage'),
    output: path.join(defaultRepoRoot, 'src/QueenZone.Mobile/coverage/crap'),
    complexity: null,
    threshold: '30',
    top: '20',
  };
  for (let index = 0; index < argv.length; index += 1) {
    const token = argv[index];
    if (token.startsWith('--') && argv[index + 1]) {
      args[token.slice(2)] = argv[index + 1];
      index += 1;
    }
  }
  return args;
}

export async function main(argv) {
  const args = parseArgs(argv);
  const threshold = Number(args.threshold);
  const istanbulPath = path.join(args.reports, 'jest', 'coverage-final.json');
  if (!existsSync(istanbulPath)) {
    throw new Error(`Missing Jest coverage '${istanbulPath}'. Run npm run test:coverage in src/QueenZone.Mobile first.`);
  }
  const istanbul = JSON.parse(readFileSync(istanbulPath, 'utf8'));

  const lcovPath = path.join(args.reports, 'node', 'lcov.info');
  const nodeHits = existsSync(lcovPath)
    ? parseLcovLineHits(readFileSync(lcovPath, 'utf8'), { repoRoot: args.repoRoot })
    : new Map();

  const eslintResults = args.complexity
    ? JSON.parse(readFileSync(args.complexity, 'utf8'))
    : await runEslintComplexity(path.join(args.repoRoot, 'src/QueenZone.Mobile'));
  const complexity = parseEslintComplexity(eslintResults, { repoRoot: args.repoRoot });
  if (complexity.size === 0) {
    throw new Error('ESLint reported no complexity results; check that the complexity rule ran.');
  }

  const { rows, unmatched } = getCrapRows({ istanbul, complexity, nodeHits, repoRoot: args.repoRoot });
  mkdirSync(args.output, { recursive: true });
  const csvPath = path.join(args.output, 'crap-report.csv');
  const mdPath = path.join(args.output, 'crap-summary.md');
  writeFileSync(csvPath, renderCsv(rows));
  writeFileSync(mdPath, renderSummaryMarkdown(rows, { threshold, top: Number(args.top) }));

  const over = rows.filter((row) => row.Crap > threshold).length;
  console.log(`Functions above CRAP ${threshold}: ${over} of ${rows.length}.`);
  if (unmatched > 0) {
    console.log(`${unmatched} ESLint complexity result(s) had no matching Istanbul function and were skipped.`);
  }
  console.log(`Wrote ${csvPath}`);
  console.log(`Wrote ${mdPath}`);
  return rows;
}

const invokedDirectly = process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url);
if (invokedDirectly) {
  try {
    await main(process.argv.slice(2));
  } catch (error) {
    console.error(error.message);
    process.exit(1);
  }
}
