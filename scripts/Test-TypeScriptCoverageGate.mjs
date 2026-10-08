#!/usr/bin/env node
/**
 * Merge TypeScript Jest + Node coverage and enforce documented floors.
 *
 * npm test coverage ≠ consumer contracts ≠ device smoke.
 * Union-by-file (do not sum overlapping reports). Fail closed on missing or
 * malformed reports. Thresholds: the typescript.floors file named in
 * development-standards.json (config/typescript-coverage.json by default).
 */
import { existsSync, globSync, mkdirSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { spawnSync } from 'node:child_process';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const scriptDir = path.dirname(fileURLToPath(import.meta.url));
// The kit installs this script in <repo>/scripts, so it finds its repository from any working directory.
const defaultRepoRoot = path.resolve(scriptDir, '..');

/** STANDARDS_TS_PROJECT, else typescript.projectRoot in development-standards.json, else app. */
export function resolveProjectRoot(repoRoot = defaultRepoRoot, env = process.env) {
  let configured = env.STANDARDS_TS_PROJECT || undefined;
  if (configured === undefined) {
    const configPath = path.join(repoRoot, 'development-standards.json');
    configured = existsSync(configPath) ? JSON.parse(readFileSync(configPath, 'utf8')).typescript?.projectRoot : undefined;
  }
  const value = configured ?? 'app';
  if (typeof value !== 'string' || !value || path.isAbsolute(value) || value.split(/[\\/]/).includes('..')) {
    throw new Error(`Invalid TypeScript project root: ${JSON.stringify(value)}.`);
  }
  return value.replaceAll('\\', '/').replace(/\/$/, '');
}

const projectRoot = resolveProjectRoot();
const projectPrefix = projectRoot === '.' ? '' : `${projectRoot}/`;

export const COVERABLE_GLOB = `${projectPrefix}src/**/*.{ts,tsx}`;

const knownGitExecutables = [
  '/usr/bin/git',
  '/usr/local/bin/git',
  '/opt/homebrew/bin/git',
  String.raw`C:\Program Files\Git\cmd\git.exe`,
  String.raw`C:\Program Files\Git\mingw64\bin\git.exe`,
];

/** An absolute git path (GIT_EXECUTABLE, else a standard install), so a writable PATH entry cannot supply git. */
export function resolveGitExecutable(env = process.env, exists = existsSync) {
  const configured = env.GIT_EXECUTABLE;
  if (configured) {
    if (!path.isAbsolute(configured)) {
      throw new Error('GIT_EXECUTABLE must be an absolute path.');
    }
    return configured;
  }
  const found = knownGitExecutables.find((candidate) => exists(candidate));
  if (!found) {
    throw new Error(`git not found. Set GIT_EXECUTABLE to an absolute path or install git at ${knownGitExecutables.join(', ')}.`);
  }
  return found;
}

export function toPosix(value) {
  return String(value).replaceAll('\\', '/');
}

export function stripFileUrl(value) {
  let normalized = toPosix(value);
  if (normalized.startsWith('file://')) {
    normalized = decodeURIComponent(normalized.slice('file://'.length));
    if (/^\/[A-Za-z]:\//.test(normalized)) {
      normalized = normalized.slice(1);
    }
  }
  return normalized;
}

export function toRepoPath(filePath, sources = [], repoRoot = defaultRepoRoot) {
  const posixRoot = toPosix(path.resolve(repoRoot)).replace(/\/$/, '');
  let candidate = stripFileUrl(filePath);

  const marker = projectPrefix;
  const markerAt = candidate.toLowerCase().indexOf(marker.toLowerCase());
  if (marker && markerAt >= 0) {
    return candidate.slice(markerAt);
  }

  const isAbsolute = candidate.startsWith('/') || /^[A-Za-z]:\//.test(candidate);
  if (isAbsolute) {
    const relative = toPosix(path.relative(posixRoot, candidate));
    if (relative && !relative.startsWith('..') && !path.isAbsolute(relative)) {
      return relative;
    }
  } else {
    const fromMobile = candidate.replace(/^\.\//, '');
    if (fromMobile.startsWith('src/')) {
      return `${projectPrefix}${fromMobile}`;
    }

    for (const source of sources) {
      const joined = toPosix(path.posix.join(toPosix(source).replace(/\/$/, ''), fromMobile));
      const sourceMarkerAt = joined.toLowerCase().indexOf(marker.toLowerCase());
      if (marker && sourceMarkerAt >= 0) {
        return joined.slice(sourceMarkerAt);
      }
    }
  }

  return candidate;
}

export function isCoverableRepoPath(repoPath) {
  const posix = toPosix(repoPath);
  if (!posix.startsWith(`${projectPrefix}src/`) || !/\.(ts|tsx)$/.test(posix)) {
    return false;
  }
  if (posix.endsWith('.d.ts')) {
    return false;
  }
  if (/\.test\.(ts|tsx)$/.test(posix)) {
    return false;
  }
  if (posix.startsWith(`${projectPrefix}src/test/`)) {
    return false;
  }
  return true;
}

function addHit(map, key, hits) {
  map.set(key, (map.get(key) ?? 0) + hits);
}

export function createFileCoverage() {
  return {
    lines: new Map(),
    branches: new Map(),
    functions: new Map(),
    statements: new Map(),
  };
}

export function createStore() {
  return new Map();
}

function fileCoverage(store, repoPath) {
  if (!store.has(repoPath)) {
    store.set(repoPath, createFileCoverage());
  }
  return store.get(repoPath);
}

function cloneFileCoverage(coverage) {
  const clone = createFileCoverage();
  for (const [line, hits] of coverage.lines) {
    clone.lines.set(line, hits);
  }
  for (const [key, hits] of coverage.branches) {
    clone.branches.set(key, hits);
  }
  for (const [key, hits] of coverage.functions) {
    clone.functions.set(key, hits);
  }
  for (const [key, hits] of coverage.statements) {
    clone.statements.set(key, hits);
  }
  return clone;
}

export function mergeStores(stores) {
  const merged = createStore();
  for (const store of stores) {
    for (const [repoPath, coverage] of store) {
      const target = fileCoverage(merged, repoPath);
      for (const [line, hits] of coverage.lines) {
        addHit(target.lines, line, hits);
      }
      for (const [key, hits] of coverage.branches) {
        addHit(target.branches, key, hits);
      }
      for (const [key, hits] of coverage.functions) {
        addHit(target.functions, key, hits);
      }
      for (const [key, hits] of coverage.statements) {
        addHit(target.statements, key, hits);
      }
    }
  }
  return merged;
}

function overlayKnownHits(target, overlay) {
  for (const [key, hits] of overlay) {
    if (target.has(key)) addHit(target, key, hits);
  }
}

function overlayStatementHits(target, statements) {
  for (const [key, hits] of statements) {
    const line = Number(String(key).split(':')[0]);
    for (const existing of target.statements.keys()) {
      if (Number(String(existing).split(':')[0]) === line) {
        addHit(target.statements, existing, hits);
      }
    }
  }
}

function overlayFunctionHits(target, functions) {
  for (const [key, hits] of functions) {
    const separator = key.indexOf(':');
    const line = Number(key.slice(0, separator));
    const name = key.slice(separator + 1);
    const existing = [...target.functions.keys()].find(
      (candidate) => candidate.endsWith(`:${name}`) || Number(candidate.slice(0, candidate.indexOf(':'))) === line,
    );
    if (existing) {
      addHit(target.functions, existing, hits);
    }
  }
}

/**
 * Overlay Node/V8 hits onto the Jest/Istanbul coverable universe.
 * V8 emits a DA row for almost every physical line; Istanbul only instruments
 * statements. A naive union of those line sets inflates global % because
 * well-tested files gain extra covered V8 lines while untested screens keep
 * only Istanbul's smaller uncovered set. Hits still union by (file, line).
 */
export function overlayHits(base, overlay) {
  const merged = createStore();
  for (const [repoPath, coverage] of base) {
    merged.set(repoPath, cloneFileCoverage(coverage));
  }

  for (const [repoPath, coverage] of overlay) {
    if (!merged.has(repoPath)) {
      merged.set(repoPath, cloneFileCoverage(coverage));
      continue;
    }

    const target = merged.get(repoPath);
    overlayKnownHits(target.lines, coverage.lines);
    overlayStatementHits(target, coverage.statements);
    overlayFunctionHits(target, coverage.functions);
    overlayKnownHits(target.branches, coverage.branches);
  }

  return merged;
}

function accumulateMetric(total, hits) {
  for (const hit of hits) {
    total.total += 1;
    if (hit > 0) total.covered += 1;
  }
}

export function summarizeStore(store, { coverableOnly = true } = {}) {
  const totals = {
    lines: { covered: 0, total: 0 },
    branches: { covered: 0, total: 0 },
    functions: { covered: 0, total: 0 },
    statements: { covered: 0, total: 0 },
  };

  for (const [repoPath, coverage] of store) {
    if (coverableOnly && !isCoverableRepoPath(repoPath)) {
      continue;
    }

    for (const name of ['lines', 'branches', 'functions', 'statements']) {
      accumulateMetric(totals[name], coverage[name].values());
    }
  }

  return {
    lines: metric(totals.lines),
    branches: metric(totals.branches),
    functions: metric(totals.functions),
    statements: metric(totals.statements),
  };
}

export function metric({ covered, total }) {
  const pct = total === 0 ? 100 : Math.round((covered / total) * 10000) / 100;
  return { covered, total, pct };
}

export function parseLcov(contents, { sources = [], repoRoot = defaultRepoRoot } = {}) {
  const store = createStore();
  let current = null;
  let currentFn = null;

  for (const rawLine of contents.split(/\r?\n/)) {
    const line = rawLine.trim();
    if (line.startsWith('SF:')) {
      const repoPath = toRepoPath(line.slice(3), sources, repoRoot);
      current = fileCoverage(store, repoPath);
      currentFn = null;
      continue;
    }
    if (!current) {
      continue;
    }
    switch (line.slice(0, line.indexOf(':'))) {
      case 'FN': {
        const [fnLine, ...nameParts] = line.slice(3).split(',');
        currentFn = `${Number(fnLine)}:${nameParts.join(',') || 'fn'}`;
        addHit(current.functions, currentFn, 0);
        break;
      }
      case 'FNDA': {
        const [hits, ...nameParts] = line.slice(5).split(',');
        const name = nameParts.join(',') || 'fn';
        const existing = [...current.functions.keys()].find((key) => key.endsWith(`:${name}`));
        addHit(current.functions, existing ?? `0:${name}`, Number(hits));
        break;
      }
      case 'DA': {
        const [number, hits] = line.slice(3).split(',').map(Number);
        addHit(current.lines, number, hits);
        addHit(current.statements, String(number), hits);
        break;
      }
      case 'BRDA': {
        const [number, block, branch, rawHits] = line.slice(5).split(',');
        const hits = rawHits === '-' ? 0 : Number(rawHits);
        addHit(current.branches, `${number}:${block}:${branch}`, hits);
        break;
      }
    }
  }

  return store;
}

function decodeXml(value) {
  return String(value)
    .replaceAll('&lt;', '<')
    .replaceAll('&gt;', '>')
    .replaceAll('&quot;', '"')
    .replaceAll('&apos;', "'")
    .replaceAll('&amp;', '&');
}

function readCoberturaMethods(block, coverage) {
  for (const methodBlock of block.split('<method ').slice(1)) {
    const headerEnd = methodBlock.indexOf('>');
    const header = methodBlock.slice(0, headerEnd);
    const name = /name="([^"]+)"/.exec(header)?.[1];
    const hits = /hits="(\d+)"/.exec(header)?.[1];
    const line = /<line number="(\d+)"/.exec(methodBlock)?.[1];
    if (headerEnd !== -1 && name && hits && line) {
      addHit(coverage.functions, `${Number(line)}:${decodeXml(name)}`, Number(hits));
    }
  }
}

function readCoberturaLines(block, coverage, repoPath) {
  for (const lineBlock of block.split(/<line\s+/).slice(1)) {
    const tagEnd = lineBlock.indexOf('/>');
    if (tagEnd === -1) {
      throw new Error(`Malformed Cobertura report: line tag is not closed in ${repoPath}.`);
    }
    const attrs = lineBlock.slice(0, tagEnd);
    const number = Number(/number="(\d+)"/.exec(attrs)?.[1]);
    const hits = Number(/hits="(\d+)"/.exec(attrs)?.[1]);
    if (!Number.isFinite(number)) {
      throw new TypeError(`Malformed Cobertura report: line is missing a number in ${repoPath}.`);
    }
    addHit(coverage.lines, number, hits);
    addHit(coverage.statements, String(number), hits);

    const condition = /condition-coverage="[^"(]*\((\d+)\/(\d+)\)"/.exec(attrs);
    if (condition) {
      const covered = Number(condition[1]);
      const total = Number(condition[2]);
      for (let index = 0; index < total; index += 1) {
        addHit(coverage.branches, `${number}:c:${index}`, index < covered ? 1 : 0);
      }
    }
  }
}

export function parseCobertura(contents, { repoRoot = defaultRepoRoot } = {}) {
  const sources = [...contents.matchAll(/<source>([^<]*)<\/source>/g)].map((match) => decodeXml(match[1]));
  const store = createStore();
  const classBlocks = contents.split(/<class\s+/).slice(1);

  for (const block of classBlocks) {
    const filenameMatch = block.match(/filename="([^"]+)"/);
    if (!filenameMatch) {
      throw new Error('Malformed Cobertura report: class is missing filename.');
    }
    const repoPath = toRepoPath(decodeXml(filenameMatch[1]), sources, repoRoot);
    const coverage = fileCoverage(store, repoPath);

    readCoberturaMethods(block, coverage);
    readCoberturaLines(block, coverage, repoPath);
  }

  if (store.size === 0) {
    throw new Error('Malformed Cobertura report: no class entries.');
  }

  return store;
}

function readIstanbulFile(file, coverage) {
  const statementMap = file.statementMap ?? {};
  const statements = file.s ?? {};
  for (const [id, hits] of Object.entries(statements)) {
    const loc = statementMap[id]?.start ?? {};
    const key = `${loc.line ?? id}:${loc.column ?? 0}`;
    addHit(coverage.statements, key, Number(hits));
    if (Number.isFinite(loc.line)) {
      addHit(coverage.lines, loc.line, Number(hits));
    }
  }

  const fnMap = file.fnMap ?? {};
  const functions = file.f ?? {};
  for (const [id, hits] of Object.entries(functions)) {
    const fn = fnMap[id] ?? {};
    const line = fn.decl?.start?.line ?? fn.loc?.start?.line ?? 0;
    addHit(coverage.functions, `${line}:${fn.name ?? id}`, Number(hits));
  }

  const branchMap = file.branchMap ?? {};
  const branches = file.b ?? {};
  for (const [id, hitsList] of Object.entries(branches)) {
    const branch = branchMap[id] ?? {};
    const line = branch.loc?.start?.line ?? branch.line ?? 0;
    const values = Array.isArray(hitsList) ? hitsList : [hitsList];
    values.forEach((hits, index) => {
      addHit(coverage.branches, `${line}:${id}:${index}`, Number(hits));
    });
  }
}

export function parseIstanbulJson(contents, { repoRoot = defaultRepoRoot } = {}) {
  let parsed;
  try {
    parsed = JSON.parse(contents);
  } catch (error) {
    throw new Error(`Malformed Istanbul coverage-final.json: ${error.message}`);
  }

  if (!parsed || typeof parsed !== 'object' || Array.isArray(parsed)) {
    throw new Error('Malformed Istanbul coverage-final.json: expected a file map.');
  }

  const store = createStore();
  for (const [filePath, file] of Object.entries(parsed)) {
    if (!file || typeof file !== 'object') {
      throw new Error(`Malformed Istanbul coverage-final.json: missing file data for ${filePath}.`);
    }
    const repoPath = toRepoPath(file.path ?? filePath, [], repoRoot);
    const coverage = fileCoverage(store, repoPath);

    readIstanbulFile(file, coverage);
  }

  if (store.size === 0) {
    throw new Error('Malformed Istanbul coverage-final.json: no file entries.');
  }

  return store;
}

export function loadSuiteReport(suiteDir, kind, repoRoot) {
  if (!existsSync(suiteDir)) {
    throw new Error(`Missing ${kind} coverage directory '${suiteDir}'.`);
  }

  if (kind === 'node') {
    const lcovPath = path.join(suiteDir, 'lcov.info');
    if (!existsSync(lcovPath)) {
      throw new Error(`Missing Node coverage report '${lcovPath}'.`);
    }
    const contents = readFileSync(lcovPath, 'utf8');
    if (!contents.trim() || !/^SF:/m.test(contents)) {
      throw new Error(`Malformed Node lcov report '${lcovPath}'.`);
    }
    return parseLcov(contents, { repoRoot });
  }

  const istanbulPath = path.join(suiteDir, 'coverage-final.json');
  if (existsSync(istanbulPath)) {
    return parseIstanbulJson(readFileSync(istanbulPath, 'utf8'), { repoRoot });
  }

  const coberturaPath = path.join(suiteDir, 'cobertura-coverage.xml');
  if (existsSync(coberturaPath)) {
    return parseCobertura(readFileSync(coberturaPath, 'utf8'), { repoRoot });
  }

  throw new Error(`Missing Jest coverage report under '${suiteDir}' (expected coverage-final.json or cobertura-coverage.xml).`);
}

export function listCoverableProductionFiles(repoRoot) {
  return globSync('src/**/*.{ts,tsx}', {
    cwd: path.join(repoRoot, projectRoot),
  })
    .map((relative) => toPosix(`${projectPrefix}${relative}`))
    .filter((repoPath) => isCoverableRepoPath(repoPath))
    .sort();
}

export function assertProductionFilesPresent(store, repoRoot) {
  const production = listCoverableProductionFiles(repoRoot);
  if (!production.length) throw new Error('No production TypeScript files found; check typescript.projectRoot (or STANDARDS_TS_PROJECT).');
  const missing = production.filter((repoPath) => !store.has(repoPath));
  if (missing.length > 0) {
    const sample = missing.slice(0, 20).join('\n  ');
    throw new Error(
      `Coverage report is missing ${missing.length} production file(s). collectCoverageFrom must include src/**/*.{ts,tsx}.\n  ${sample}`,
    );
  }
}

function parseChangedDiff(contents) {
  const changed = new Map();
  let currentFile = null;
  for (const line of contents.split(/\r?\n/)) {
    const fileMatch = /^\+\+\+ b\/(.+)$/.exec(line);
    if (fileMatch) {
      currentFile = toPosix(fileMatch[1]);
      if (!changed.has(currentFile)) {
        changed.set(currentFile, new Set());
      }
      continue;
    }
    if (!currentFile) {
      continue;
    }
    const hunk = /^@@ -\d+(?:,\d+)? \+(\d+)(?:,(\d+))? @@/.exec(line);
    if (!hunk) {
      continue;
    }
    const start = Number(hunk[1]);
    const count = hunk[2] ? Number(hunk[2]) : 1;
    const lines = changed.get(currentFile);
    for (let offset = 0; offset < count; offset += 1) {
      lines.add(start + offset);
    }
  }

  return changed;
}

export function getChangedLines({ repoRoot, baseRef, headRef, paths }) {
  if (!baseRef) {
    throw new Error('Changed-line coverage requires a base ref.');
  }
  const git = resolveGitExecutable();

  let resolved = baseRef;
  if (!baseRef.startsWith('origin/')) {
    const remoteRef = `origin/${baseRef}`;
    const probe = spawnSync(git, ['rev-parse', '--verify', '--quiet', remoteRef], { cwd: repoRoot });
    if (probe.status === 0) {
      resolved = remoteRef;
    }
  }

  const available = spawnSync(git, ['rev-parse', '--verify', '--quiet', `${resolved}^{commit}`], { cwd: repoRoot });
  if (available.status !== 0) {
    throw new Error(`Base ref '${baseRef}' is not available locally.`);
  }

  const diff = spawnSync(
    git,
    ['diff', '--unified=0', '--no-color', `${resolved}...${headRef}`, '--', ...paths],
    { cwd: repoRoot, encoding: 'utf8' },
  );
  if (diff.status !== 0) {
    throw new Error(`Unable to calculate changed lines against '${resolved}'.`);
  }

  const changed = parseChangedDiff(diff.stdout);

  return { skipped: null, lines: changed, resolved };
}

export function evaluateChangedLines(store, changedLines) {
  let coverable = 0;
  let covered = 0;
  const uncovered = [];

  for (const [file, lineNumbers] of changedLines) {
    const posix = toPosix(file);
    if (!isCoverableRepoPath(posix) || !store.has(posix)) {
      continue;
    }
    const coverage = store.get(posix);
    for (const lineNumber of lineNumbers) {
      if (!coverage.lines.has(lineNumber)) {
        continue;
      }
      coverable += 1;
      if (coverage.lines.get(lineNumber) > 0) {
        covered += 1;
      } else {
        uncovered.push(`${posix}:${lineNumber}`);
      }
    }
  }

  if (coverable === 0) {
    return {
      skipped: 'Changed TypeScript/TSX lines do not overlap coverable lines in the merged report.',
      metric: metric({ covered: 0, total: 0 }),
      uncovered: [],
    };
  }

  return {
    skipped: null,
    metric: metric({ covered, total: coverable }),
    uncovered,
  };
}

/** Floors file named by typescript.floors in development-standards.json. */
export function configuredFloorsPath(repoRoot = defaultRepoRoot) {
  const configPath = path.join(repoRoot, 'development-standards.json');
  if (!existsSync(configPath)) {
    throw new Error(`Missing development-standards.json under '${repoRoot}'.`);
  }
  const floors = JSON.parse(readFileSync(configPath, 'utf8')).typescript?.floors;
  if (typeof floors !== 'string' || !floors) {
    throw new Error('Configure typescript.floors in development-standards.json.');
  }
  return path.join(repoRoot, floors);
}

export function loadFloors(floorsPath) {
  if (!existsSync(floorsPath)) {
    throw new Error(`Missing mobile coverage floors file '${floorsPath}'.`);
  }

  let floors;
  try {
    floors = JSON.parse(readFileSync(floorsPath, 'utf8'));
  } catch (error) {
    throw new Error(`Malformed mobile coverage floors file '${floorsPath}': ${error.message}`);
  }

  if (typeof floors.globalLine !== 'number') {
    throw new TypeError('Malformed mobile coverage floors file: globalLine must be a number.');
  }
  if (floors.globalBranch != null && typeof floors.globalBranch !== 'number') {
    throw new Error(`Malformed mobile coverage floors file: globalBranch must be a number or null.`);
  }
  if (typeof floors.changedLine !== 'number') {
    throw new TypeError('Malformed mobile coverage floors file: changedLine must be a number.');
  }

  return floors;
}

function escapeXml(value) {
  return String(value)
    .replaceAll('&', '&amp;')
    .replaceAll('<', '&lt;')
    .replaceAll('>', '&gt;')
    .replaceAll('"', '&quot;');
}

function escapeHtml(value) {
  return escapeXml(value).replaceAll("'", '&#39;');
}

export function writeMergedReports({ store, summary, destDir, sources }) {
  mkdirSync(destDir, { recursive: true });
  const htmlDir = path.join(destDir, 'html');
  mkdirSync(htmlDir, { recursive: true });

  writeFileSync(path.join(destDir, 'summary.json'), `${JSON.stringify(summary, null, 2)}\n`);
  writeFileSync(path.join(destDir, 'coverage.cobertura.xml'), renderCobertura(store, summary.merged, sources));
  writeFileSync(path.join(htmlDir, 'index.html'), renderHtml(summary, store));
}

function renderCobertura(store, merged, sources) {
  const sourceXml = sources.map((source) => `    <source>${escapeXml(source)}</source>`).join('\n');
  const classes = [];

  for (const [repoPath, coverage] of [...store.entries()].sort(([a], [b]) => a.localeCompare(b))) {
    if (!isCoverableRepoPath(repoPath)) {
      continue;
    }
    const lineXml = [...coverage.lines.entries()]
      .sort(([a], [b]) => a - b)
      .map(([number, hits]) => `            <line number="${number}" hits="${hits}" branch="false"/>`)
      .join('\n');
    const methodXml = [...coverage.functions.entries()]
      .sort(([a], [b]) => a.localeCompare(b))
      .map((entry) => {
        const [key, hits] = entry;
        const separator = key.indexOf(':');
        const line = key.slice(0, separator);
        const name = key.slice(separator + 1);
        return `            <method name="${escapeXml(name)}" hits="${hits}" signature="()V"><lines><line number="${line}" hits="${hits}"/></lines></method>`;
      })
      .join('\n');
    const fileLines = metricFromMap(coverage.lines);
    classes.push(
      `        <class name="${escapeXml(path.posix.basename(repoPath))}" filename="${escapeXml(repoPath)}" line-rate="${fileLines.pct / 100}" branch-rate="0">\n          <methods>\n${methodXml}\n          </methods>\n          <lines>\n${lineXml}\n          </lines>\n        </class>`,
    );
  }

  return `<?xml version="1.0" ?>\n<!DOCTYPE coverage SYSTEM "http://cobertura.sourceforge.net/xml/coverage-04.dtd">\n<coverage lines-valid="${merged.lines.total}" lines-covered="${merged.lines.covered}" line-rate="${merged.lines.pct / 100}" branches-valid="${merged.branches.total}" branches-covered="${merged.branches.covered}" branch-rate="${merged.branches.pct / 100}" complexity="0" version="0.1">\n  <sources>\n${sourceXml}\n  </sources>\n  <packages>\n    <package name="app" line-rate="${merged.lines.pct / 100}" branch-rate="${merged.branches.pct / 100}">\n      <classes>\n${classes.join('\n')}\n      </classes>\n    </package>\n  </packages>\n</coverage>\n`;
}

function metricFromMap(map) {
  let covered = 0;
  for (const hits of map.values()) {
    if (hits > 0) {
      covered += 1;
    }
  }
  return metric({ covered, total: map.size });
}

function renderHtml(summary, store) {
  const rows = [...store.entries()]
    .filter(([repoPath]) => isCoverableRepoPath(repoPath))
    .map(([repoPath, coverage]) => {
      const lines = metricFromMap(coverage.lines);
      const uncovered = [...coverage.lines.entries()]
        .filter(([, hits]) => hits === 0)
        .map(([line]) => line)
        .sort((a, b) => a - b)
        .slice(0, 30)
        .join(', ');
      return { repoPath, lines, uncovered };
    })
    .sort((a, b) => a.lines.pct - b.lines.pct || a.repoPath.localeCompare(b.repoPath));

  const fileRows = rows
    .map(
      (row) =>
        `<tr><td><code>${escapeHtml(row.repoPath)}</code></td><td>${row.lines.pct}%</td><td>${row.lines.covered}/${row.lines.total}</td><td>${escapeHtml(row.uncovered)}</td></tr>`,
    )
    .join('\n');

  const changed = (summary.changed.uncovered ?? []).slice(0, 40)
    .map((line) => `<li><code>${escapeHtml(line)}</code></li>`)
    .join('\n');

  return `<!DOCTYPE html>
<html lang="en">
<head>
  <meta charset="utf-8"/>
  <title>TypeScript coverage</title>
  <style>
    body { font-family: ui-sans-serif, system-ui, sans-serif; margin: 2rem; color: #111; }
    table { border-collapse: collapse; width: 100%; }
    th, td { border-bottom: 1px solid #ddd; padding: 0.4rem 0.5rem; text-align: left; vertical-align: top; }
    code { font-size: 0.9em; }
    .muted { color: #555; }
  </style>
</head>
<body>
  <h1>TypeScript coverage</h1>
  <p class="muted">npm test coverage  ≠ consumer contracts ≠ device smoke. Merged totals union Jest and Node by file; they do not sum overlapping reports.</p>
  <h2>Suite totals</h2>
  <table>
    <tr><th>Suite</th><th>Lines</th><th>Branches</th><th>Functions</th><th>Statements</th></tr>
    ${suiteRow('Jest (component/hook)', summary.jest)}
    ${suiteRow('Node (pure)', summary.node)}
    ${suiteRow('Merged (Jest universe + Node hits)', summary.merged)}
  </table>
  <h2>Floors</h2>
  <p>Line floor ${summary.floors.globalLine}% · Branch floor ${summary.floors.globalBranch ?? 'not enforced'} · Changed-line floor ${summary.floors.changedLine}%</p>
  <h2>Changed uncovered coverable lines</h2>
  ${changed ? `<ul>${changed}</ul>` : `<p>${escapeHtml(summary.changed.skipped ?? 'None')}</p>`}
  <h2>Files</h2>
  <table>
    <tr><th>File</th><th>Lines</th><th>Covered</th><th>Uncovered (first 30)</th></tr>
    ${fileRows}
  </table>
</body>
</html>
`;
}

function suiteRow(name, suite) {
  return `<tr><td>${escapeHtml(name)}</td><td>${suite.lines.pct}% (${suite.lines.covered}/${suite.lines.total})</td><td>${suite.branches.pct}% (${suite.branches.covered}/${suite.branches.total})</td><td>${suite.functions.pct}% (${suite.functions.covered}/${suite.functions.total})</td><td>${suite.statements.pct}% (${suite.statements.covered}/${suite.statements.total})</td></tr>`;
}

export function renderSummaryMarkdown(summary) {
  const changed = summary.changed.skipped
    ? summary.changed.skipped
    : `${summary.changed.metric.pct}% (${summary.changed.metric.covered}/${summary.changed.metric.total})`;
  const uncovered = (summary.changed.uncovered ?? []).slice(0, 20);
  const extra = (summary.changed.uncovered?.length ?? 0) - uncovered.length;

  return `## TypeScript coverage

\`npm test coverage\` ≠ \`consumer contracts\` ≠ \`device smoke\`. Contracts and device smoke stay out of these totals.

| Suite | Lines | Branches | Functions | Statements |
| --- | --- | --- | --- | --- |
| Jest (component/hook) | ${fmt(summary.jest.lines)} | ${fmt(summary.jest.branches)} | ${fmt(summary.jest.functions)} | ${fmt(summary.jest.statements)} |
| Node (pure) | ${fmt(summary.node.lines)} | ${fmt(summary.node.branches)} | ${fmt(summary.node.functions)} | ${fmt(summary.node.statements)} |
| **Merged (Jest universe + Node hits)** | **${fmt(summary.merged.lines)}** | **${fmt(summary.merged.branches)}** | **${fmt(summary.merged.functions)}** | **${fmt(summary.merged.statements)}** |

| Metric | Floor (baseline) | Current |
| --- | --- | --- |
| Global line | ${summary.floors.globalLine}% | ${summary.merged.lines.pct}% |
| Global branch | ${summary.floors.globalBranch == null ? 'not enforced' : `${summary.floors.globalBranch}%`} | ${summary.merged.branches.pct}% |
| Changed-line | ${summary.floors.changedLine}% | ${changed} |

### Uncovered changed coverable lines

${uncovered.length === 0 ? (summary.changed.skipped ?? 'None') : uncovered.map((line) => `- \`${line}\``).join('\n')}${extra > 0 ? `\n- ...and ${extra} more.` : ''}
`;
}

function fmt(value) {
  return `${value.pct}% (${value.covered}/${value.total})`;
}

export function enforceFloors(summary) {
  const failures = [];
  if (summary.merged.lines.pct < summary.floors.globalLine) {
    failures.push(
      `Global line coverage ${summary.merged.lines.pct}% is below the required ${summary.floors.globalLine}%.`,
    );
  }
  if (summary.floors.globalBranch != null && summary.merged.branches.pct < summary.floors.globalBranch) {
    failures.push(
      `Global branch coverage ${summary.merged.branches.pct}% is below the required ${summary.floors.globalBranch}%.`,
    );
  }
  if (!summary.changed.skipped && summary.changed.metric.pct < summary.floors.changedLine) {
    failures.push(
      `Changed-line coverage ${summary.changed.metric.pct}% is below the required ${summary.floors.changedLine}%.`,
    );
  }
  return failures;
}

function parseArgs(argv) {
  const args = {
    repoRoot: defaultRepoRoot,
    reports: path.join(defaultRepoRoot, projectRoot, 'coverage'),
    floors: null,
    merged: path.join(defaultRepoRoot, projectRoot, 'coverage/merged'),
    baseRef: process.env.GITHUB_BASE_REF || 'origin/main',
    headRef: 'HEAD',
    selfTest: false,
    skipProductionCheck: false,
  };

  for (let index = 0; index < argv.length; index += 1) {
    const token = argv[index];
    if (token === '--self-test') {
      args.selfTest = true;
    } else if (token === '--skip-production-check') {
      args.skipProductionCheck = true;
    } else if (token.startsWith('--') && argv[index + 1]) {
      const key = token.slice(2).replace(/-([a-z])/g, (_, letter) => letter.toUpperCase());
      args[key] = argv[index + 1];
      index += 1;
    }
  }

  return args;
}

function collectCoverageSummary(args) {
  const jestDir = path.join(args.reports, 'jest');
  const nodeDir = path.join(args.reports, 'node');
  const floors = loadFloors(args.floors ?? configuredFloorsPath(args.repoRoot));

  const jestStore = loadSuiteReport(jestDir, 'jest', args.repoRoot);
  const nodeStore = loadSuiteReport(nodeDir, 'node', args.repoRoot);
  const mergedStore = overlayHits(jestStore, nodeStore);

  if (!args.skipProductionCheck) {
    assertProductionFilesPresent(mergedStore, args.repoRoot);
  }

  const changedDiff = args.changedLines
    ? { skipped: null, lines: args.changedLines }
    : getChangedLines({
        repoRoot: args.repoRoot,
        baseRef: args.baseRef,
        headRef: args.headRef,
        paths: [`${projectPrefix}src`],
      });
  const changed = changedDiff.skipped
    ? { skipped: changedDiff.skipped, metric: metric({ covered: 0, total: 0 }), uncovered: [] }
    : evaluateChangedLines(mergedStore, changedDiff.lines);

  const summary = {
    floors,
    jest: summarizeStore(jestStore),
    node: summarizeStore(nodeStore),
    merged: summarizeStore(mergedStore),
    changed,
  };

  return { mergedStore, summary };
}

function reportCoverageSummary(args, summary, mergedStore, log) {
  const { changed } = summary;

  writeMergedReports({
    store: mergedStore,
    summary,
    destDir: args.merged,
    sources: [path.join(args.repoRoot, projectRoot)],
  });

  const markdown = renderSummaryMarkdown(summary);
  if (process.env.GITHUB_STEP_SUMMARY && !args.quiet) {
    writeFileSync(process.env.GITHUB_STEP_SUMMARY, markdown, { flag: 'a' });
  }

  log(markdown);
  log(`Merged line coverage: ${summary.merged.lines.pct}% (${summary.merged.lines.covered}/${summary.merged.lines.total}) [Jest universe + Node hits]`);
  if (changed.skipped) {
    log(changed.skipped);
  } else {
    log(`Changed-line coverage: ${changed.metric.pct}% (${changed.metric.covered}/${changed.metric.total})`);
  }
}

function ensureCoverageFloors(summary, logError) {
  const { changed } = summary;

  const failures = enforceFloors(summary);
  if (failures.length > 0) {
    for (const failure of failures) {
      logError(failure);
    }
    if (changed.uncovered.length > 0) {
      logError('Uncovered changed lines:');
      for (const line of changed.uncovered.slice(0, 20)) {
        logError(`  ${line}`);
      }
    }
    const error = new Error(failures.join(' '));
    error.summary = summary;
    throw error;
  }
}

export function runGate(args) {
  const log = args.quiet ? () => {} : console.log.bind(console);
  const logError = args.quiet ? () => {} : console.error.bind(console);
  const { mergedStore, summary } = collectCoverageSummary(args);
  reportCoverageSummary(args, summary, mergedStore, log);
  ensureCoverageFloors(summary, logError);
  return summary;
}

function runSelfTest() {
  // Fixtures follow the configured project, so the self-test passes in any consuming repository.
  const sourceRoot = projectPrefix ? `/repo/${projectRoot}` : '/repo';
  const tempRoot = path.join(defaultRepoRoot, '.standards-test-tmp');
  rmSync(tempRoot, { recursive: true, force: true });
  mkdirSync(tempRoot, { recursive: true });

  const cases = [];
  const assert = (name, fn) => {
    try {
      fn();
      cases.push(`PASS ${name}`);
    } catch (error) {
      cases.push(`FAIL ${name}: ${error.message}`);
      throw error;
    }
  };

  try {
    assert('posix and windows paths normalize', () => {
      const a = toRepoPath('src/api/client.ts');
      const b = toRepoPath(String.raw`src\api\client.ts`);
      const c = toRepoPath(`C:/repo/${projectPrefix}src/api/client.ts`, [], 'C:/repo');
      const d = toRepoPath(`/workspace/${projectPrefix}src/api/client.ts`, [], '/workspace');
      if (a !== `${projectPrefix}src/api/client.ts`) {
        throw new Error(a);
      }
      if (b !== a || c !== a || d !== a) {
        throw new Error(`${a} / ${b} / ${c} / ${d}`);
      }
    });

    assert('exclusions stay narrow', () => {
      if (isCoverableRepoPath(`${projectPrefix}src/screens/home/HomeScreen.tsx`) !== true) {
        throw new Error('screens must stay coverable');
      }
      if (isCoverableRepoPath(`${projectPrefix}src/api/client.test.tsx`)) {
        throw new Error('tests must be excluded');
      }
      if (isCoverableRepoPath(`${projectPrefix}src/test/fixtures.ts`)) {
        throw new Error('fixtures must be excluded');
      }
      if (isCoverableRepoPath(`${projectPrefix}contracts/consumer.test.ts`)) {
        throw new Error('contracts must stay out');
      }
    });

    assert('union does not double-count overlapping lines', () => {
      const lcov = parseLcov('TN:\nSF:src/api/text.ts\nDA:2,1\nDA:3,0\nend_of_record\n');
      const cobertura = parseCobertura(`<?xml version="1.0"?><coverage><sources><source>${sourceRoot}</source></sources><packages><package><classes><class filename="src/api/text.ts"><lines><line number="2" hits="4"/><line number="4" hits="0"/></lines></class></classes></package></packages></coverage>`);
      const merged = mergeStores([lcov, cobertura]);
      const summary = summarizeStore(merged, { coverableOnly: false });
      if (summary.lines.total !== 3 || summary.lines.covered !== 1) {
        throw new Error(JSON.stringify(summary.lines));
      }
    });

    assert('Cobertura methods keep their own line and hit count', () => {
      const report = parseCobertura('<coverage><class filename="src/api/text.ts"><methods>' +
        '<method name="first" hits="2"><lines><line number="4" hits="2"/></lines></method>' +
        '<method name="second" hits="0"><lines><line number="9" hits="0"/></lines></method>' +
        '</methods></class></coverage>');
      const functions = report.get(`${projectPrefix}src/api/text.ts`)?.functions;
      if (functions?.get('4:first') !== 2 || functions?.get('9:second') !== 0) {
        throw new Error(JSON.stringify([...functions || []]));
      }
    });

    assert('overlay keeps the Jest coverable universe', () => {
      const jest = parseCobertura(`<?xml version="1.0"?><coverage><sources><source>${sourceRoot}</source></sources><packages><package><classes><class filename="src/api/text.ts"><lines><line number="2" hits="0"/><line number="3" hits="0"/></lines></class></classes></package></packages></coverage>`);
      const node = parseLcov('TN:\nSF:src/api/text.ts\nDA:2,3\nDA:3,0\nDA:40,1\nend_of_record\n');
      const merged = overlayHits(jest, node);
      const summary = summarizeStore(merged, { coverableOnly: false });
      if (summary.lines.total !== 2 || summary.lines.covered !== 1) {
        throw new Error(JSON.stringify(summary.lines));
      }
    });

    assert('missing reports fail closed', () => {
      const empty = path.join(tempRoot, 'empty');
      mkdirSync(empty, { recursive: true });
      let failed = false;
      try {
        loadSuiteReport(empty, 'jest', defaultRepoRoot);
      } catch (error) {
        failed = /Missing Jest coverage report/.test(error.message);
      }
      if (!failed) {
        throw new Error('expected missing Jest report to throw');
      }
    });

    assert('malformed reports fail closed', () => {
      let failed = false;
      try {
        parseIstanbulJson('{');
      } catch (error) {
        failed = /Malformed Istanbul/.test(error.message);
      }
      if (!failed) {
        throw new Error('expected malformed Istanbul JSON to throw');
      }
    });

    const fixtureRoot = path.join(tempRoot, 'fixture-repo');
    const jestDir = path.join(fixtureRoot, 'coverage/jest');
    const nodeDir = path.join(fixtureRoot, 'coverage/node');
    mkdirSync(jestDir, { recursive: true });
    mkdirSync(nodeDir, { recursive: true });
    writeFileSync(
      path.join(jestDir, 'coverage-final.json'),
      JSON.stringify({
        [`/fixture-repo/${projectPrefix}src/api/text.ts`]: {
          path: `/fixture-repo/${projectPrefix}src/api/text.ts`,
          statementMap: { 0: { start: { line: 2, column: 0 } }, 1: { start: { line: 3, column: 0 } } },
          s: { 0: 1, 1: 0 },
          fnMap: { 0: { name: 'toPlainText', decl: { start: { line: 2 } } } },
          f: { 0: 1 },
          branchMap: { 0: { loc: { start: { line: 2 } } } },
          b: { 0: [1, 0] },
        },
      }),
    );
    writeFileSync(
      path.join(nodeDir, 'lcov.info'),
      'TN:\nSF:src/api/text.ts\nFN:2,toPlainText\nFNDA:2,toPlainText\nDA:2,2\nDA:4,1\nBRDA:2,0,0,1\nBRDA:2,0,1,1\nend_of_record\n',
    );
    writeFileSync(
      path.join(tempRoot, 'floors.json'),
      JSON.stringify({ globalLine: 50, globalBranch: 40, changedLine: 70 }),
    );

    assert('gate publishes suite totals and enforces floors', () => {
      const summary = runGate({
        repoRoot: '/fixture-repo',
        reports: path.join(fixtureRoot, 'coverage'),
        floors: path.join(tempRoot, 'floors.json'),
        merged: path.join(tempRoot, 'merged-pass'),
        skipProductionCheck: true,
        changedLines: new Map(),
        quiet: true,
      });
      if (summary.jest.lines.total < 1 || summary.node.lines.total < 1) {
        throw new Error('both suites must publish totals');
      }
      if (summary.merged.lines.total !== 2) {
        throw new Error(`expected 2 Jest-universe lines, got ${summary.merged.lines.total}`);
      }
    });

    assert('changed-line gate fails on uncovered coverable lines', () => {
      writeFileSync(
        path.join(tempRoot, 'floors-high.json'),
        JSON.stringify({ globalLine: 1, globalBranch: null, changedLine: 70 }),
      );
      let failed = false;
      try {
        runGate({
          repoRoot: '/fixture-repo',
          reports: path.join(fixtureRoot, 'coverage'),
          floors: path.join(tempRoot, 'floors-high.json'),
          merged: path.join(tempRoot, 'merged-fail'),
          skipProductionCheck: true,
          changedLines: new Map([[`${projectPrefix}src/api/text.ts`, new Set([3])]]),
          quiet: true,
        });
      } catch (error) {
        failed = /Changed-line coverage/.test(error.message);
      }
      if (!failed) {
        throw new Error('expected changed-line failure');
      }
    });

    assert('changed-line gate skips when no coverable TypeScript lines changed', () => {
      const summary = runGate({
        repoRoot: '/fixture-repo',
        reports: path.join(fixtureRoot, 'coverage'),
        floors: path.join(tempRoot, 'floors.json'),
        merged: path.join(tempRoot, 'merged-skip'),
        skipProductionCheck: true,
        changedLines: new Map([['docs/architecture/testing-policy.md', new Set([1])]]),
        quiet: true,
      });
      if (!summary.changed.skipped) {
        throw new Error('expected skip');
      }
    });

    assert('default floors come from typescript.floors in development-standards.json', () => {
      const configRoot = path.join(tempRoot, 'configured-repo');
      mkdirSync(configRoot, { recursive: true });
      let failed = false;
      try {
        configuredFloorsPath(configRoot);
      } catch (error) {
        failed = /Missing development-standards\.json/.test(error.message);
      }
      if (!failed) {
        throw new Error('expected a missing project configuration to fail closed');
      }
      writeFileSync(path.join(configRoot, 'development-standards.json'), JSON.stringify({ version: 1, typescript: {} }));
      failed = false;
      try {
        configuredFloorsPath(configRoot);
      } catch (error) {
        failed = /Configure typescript\.floors/.test(error.message);
      }
      if (!failed) {
        throw new Error('expected a configuration without typescript.floors to fail closed');
      }
      writeFileSync(
        path.join(configRoot, 'development-standards.json'),
        JSON.stringify({ version: 1, typescript: { floors: 'config/floors.json' } }),
      );
      if (configuredFloorsPath(configRoot) !== path.join(configRoot, 'config/floors.json')) {
        throw new Error('expected the configured floors path relative to the repository root');
      }
    });

    assert('git runs from an absolute path, never a PATH lookup', () => {
      if (resolveGitExecutable({ GIT_EXECUTABLE: path.resolve('/opt/git/bin/git') }, () => false) !== path.resolve('/opt/git/bin/git')) {
        throw new Error('expected GIT_EXECUTABLE to win');
      }
      for (const env of [{ GIT_EXECUTABLE: 'git' }, {}]) {
        let failed = false;
        try {
          resolveGitExecutable(env, () => false);
        } catch {
          failed = true;
        }
        if (!failed) {
          throw new Error(`expected ${JSON.stringify(env)} to fail closed`);
        }
      }
      if (!path.isAbsolute(resolveGitExecutable({}, (candidate) => candidate === '/usr/bin/git'))) {
        throw new Error('expected a known absolute install');
      }
    });

    console.log(cases.join('\n'));
    console.log('Test-TypeScriptCoverageGate self-test passed.');
  } finally {
    rmSync(tempRoot, { recursive: true, force: true });
  }
}

const invokedDirectly = process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url);

if (invokedDirectly) {
  const args = parseArgs(process.argv.slice(2));
  try {
    if (args.selfTest) {
      runSelfTest();
    } else {
      runGate(args);
    }
  } catch (error) {
    console.error(error.message);
    process.exitCode = 1;
  }
}
