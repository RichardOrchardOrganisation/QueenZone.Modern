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
 * same Jest-universe overlay Test-TypeScriptCoverageGate.mjs uses.
 *
 * Each Istanbul statement belongs to the innermost function that contains it,
 * matching ESLint, which scores nested functions on their own. Anonymous
 * callbacks report as `<parent>::lambda`, like C# lambdas in the .NET report.
 *
 * Writes crap-report.csv and crap-summary.md. With --baseline it also runs the
 * CRAP ratchet, same rules as Get-CrapReport.ps1: a function above --threshold
 * that is not in the baseline is new debt; a baselined one whose score rose by
 * more than 0.1 got worse. --enforce fails on either. Every --baseline run writes
 * crap-baseline.proposed.json, which only lowers or drops entries;
 * --write-baseline applies it (or creates the baseline when missing).
 *
 *   node scripts/Get-MobileCrapReport.mjs
 *   node scripts/Get-MobileCrapReport.mjs --baseline config/crap-baseline.mobile.json --enforce
 *   node scripts/Get-MobileCrapReport.mjs --baseline config/crap-baseline.mobile.json --write-baseline
 */
import { appendFileSync, existsSync, mkdirSync, readFileSync, writeFileSync } from 'node:fs';
import { createRequire } from 'node:module';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { isCoverableRepoPath, toRepoPath } from './Test-TypeScriptCoverageGate.mjs';

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

// Scores are rounded to 0.1, so a rise within this is rounding, not a regression.
const RATCHET_TOLERANCE = 0.1;

/** `File|Method`; anonymous callbacks already share `<parent>::lambda`, so the key survives reordering. */
export function baselineKey(row) {
  return `${row.File}|${row.Method}`;
}

/** Highest score per baseline key (several lambdas in one parent share a key). */
export function methodScores(rows) {
  const scores = new Map();
  for (const row of rows) {
    const key = baselineKey(row);
    scores.set(key, Math.max(scores.get(key) ?? Number.NEGATIVE_INFINITY, row.Crap));
  }
  return scores;
}

export function compareBaseline(scores, baseline, threshold) {
  const result = { added: [], worse: [], improved: [] };
  for (const [key, score] of [...scores].sort(([a], [b]) => a.localeCompare(b))) {
    if (!baseline.has(key)) {
      if (score > threshold) result.added.push({ key, score });
    } else if (score > baseline.get(key) + RATCHET_TOLERANCE) {
      result.worse.push({ key, baseline: baseline.get(key), score });
    }
  }
  for (const [key, recorded] of [...baseline].sort(([a], [b]) => a.localeCompare(b))) {
    const score = scores.get(key);
    if (score === undefined || score < recorded - RATCHET_TOLERANCE) {
      result.improved.push({ key, baseline: recorded, score: score ?? null });
    }
  }
  return result;
}

/**
 * Ratchet only: keep baselined keys still above the threshold at min(baseline, current)
 * and never add a key. With no baseline yet, record every function above the threshold.
 */
export function proposeBaseline(scores, baseline, threshold) {
  const keys = baseline ? [...baseline.keys()] : [...scores.keys()];
  const proposed = new Map();
  for (const key of keys.toSorted((a, b) => a.localeCompare(b))) {
    const score = scores.get(key);
    if (score !== undefined && score > threshold) {
      proposed.set(key, baseline ? Math.min(baseline.get(key), score) : score);
    }
  }
  return proposed;
}

export function readBaseline(file) {
  const parsed = JSON.parse(readFileSync(file, 'utf8'));
  return new Map(Object.entries(parsed.hotspots ?? {}).map(([key, score]) => [key, Number(score)]));
}

export function renderBaseline(hotspots, threshold) {
  const document = {
    description:
      'CRAP ratchet baseline. Functions above the threshold that already existed. Lower or remove entries; never raise by hand. See AGENTS.md (Change risk).',
    threshold,
    hotspots: Object.fromEntries(hotspots),
  };
  return `${JSON.stringify(document, null, 2)}\n`;
}

export function renderRatchetMarkdown(comparison, { baselinePath, threshold }) {
  const lines = ['', `### CRAP ratchet (\`${baselinePath}\`)`, ''];
  if (comparison.added.length === 0 && comparison.worse.length === 0) {
    lines.push(`No new or worsened hotspots above ${threshold}.`);
  }
  for (const item of comparison.added) {
    lines.push(`- **New hotspot:** \`${item.key}\` scores ${item.score}. Add tests or split it until it is at most ${threshold}.`);
  }
  for (const item of comparison.worse) {
    lines.push(`- **Worse:** \`${item.key}\` rose from ${item.baseline} to ${item.score}.`);
  }
  if (comparison.improved.length > 0) {
    lines.push(
      '',
      `${comparison.improved.length} baselined hotspot(s) improved or were removed. Commit \`crap-baseline.proposed.json\` from this report as \`${baselinePath}\` to lock in the gain.`,
    );
  }
  return `${lines.join('\n')}\n`;
}

function runRatchet(rows, args, threshold) {
  const baselinePath = args.baseline;
  const hasBaseline = existsSync(baselinePath);
  if (!hasBaseline && !args['write-baseline']) {
    throw new Error(`CRAP baseline '${baselinePath}' does not exist. Create it with --write-baseline.`);
  }
  const scores = methodScores(rows);
  const existing = hasBaseline ? readBaseline(baselinePath) : null;
  const proposed = proposeBaseline(scores, existing, threshold);
  writeFileSync(path.join(args.output, 'crap-baseline.proposed.json'), renderBaseline(proposed, threshold));
  if (args['write-baseline']) {
    writeFileSync(baselinePath, renderBaseline(proposed, threshold));
    console.log(`Wrote ${baselinePath} (${proposed.size} hotspot(s)).`);
  }

  // A first --write-baseline run compares against what it just recorded, not an empty list.
  const comparison = compareBaseline(scores, existing ?? proposed, threshold);
  const markdown = renderRatchetMarkdown(comparison, { baselinePath, threshold });
  appendFileSync(path.join(args.output, 'crap-summary.md'), markdown);
  console.log(markdown);
  return comparison;
}

const BOOLEAN_FLAGS = new Set(['enforce', 'write-baseline']);

function parseArgs(argv) {
  const args = {
    repoRoot: defaultRepoRoot,
    reports: path.join(defaultRepoRoot, 'src/QueenZone.Mobile/coverage'),
    output: path.join(defaultRepoRoot, 'src/QueenZone.Mobile/coverage/crap'),
    complexity: null,
    baseline: null,
    threshold: '30',
    top: '20',
  };
  for (let index = 0; index < argv.length; index += 1) {
    const name = argv[index].startsWith('--') ? argv[index].slice(2) : null;
    if (name && BOOLEAN_FLAGS.has(name)) {
      args[name] = true;
    } else if (name && argv[index + 1]) {
      args[name] = argv[index + 1];
      index += 1;
    }
  }
  if ((args.enforce || args['write-baseline']) && !args.baseline) {
    throw new Error('--enforce and --write-baseline require --baseline.');
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

  if (args.baseline) {
    const comparison = runRatchet(rows, args, threshold);
    const violations = comparison.added.length + comparison.worse.length;
    if (args.enforce && violations > 0) {
      throw new Error(
        `CRAP ratchet failed: ${comparison.added.length} new and ${comparison.worse.length} worsened hotspot(s) above ${threshold}. See crap-summary.md.`,
      );
    }
  }
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
