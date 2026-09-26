#!/usr/bin/env node
/**
 * Merge QueenZone.Mobile Jest + Node coverage and enforce documented floors.
 *
 * #871 Option A: npm test coverage ≠ #869 contracts ≠ #872 Maestro smoke.
 * Union-by-file (do not sum overlapping reports). Fail closed on missing or
 * malformed reports. Thresholds: scripts/mobile-coverage-floors.json.
 */
import { existsSync, globSync, mkdirSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { spawnSync } from 'node:child_process';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const scriptDir = path.dirname(fileURLToPath(import.meta.url));
const defaultRepoRoot = path.resolve(scriptDir, '..');

export const COVERABLE_GLOB = 'src/QueenZone.Mobile/src/**/*.{ts,tsx}';

export function toPosix(value) {
  return String(value).replace(/\\/g, '/');
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

  const marker = 'src/QueenZone.Mobile/';
  const markerAt = candidate.toLowerCase().indexOf(marker.toLowerCase());
  if (markerAt >= 0) {
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
      return `src/QueenZone.Mobile/${fromMobile}`;
    }

    for (const source of sources) {
      const joined = toPosix(path.posix.join(toPosix(source).replace(/\/$/, ''), fromMobile));
      const sourceMarkerAt = joined.toLowerCase().indexOf(marker.toLowerCase());
      if (sourceMarkerAt >= 0) {
        return joined.slice(sourceMarkerAt);
      }
    }
  }

  return candidate;
}

export function isCoverableRepoPath(repoPath) {
  const posix = toPosix(repoPath);
  if (!/^src\/QueenZone\.Mobile\/src\/.+\.(ts|tsx)$/.test(posix)) {
    return false;
  }
  if (posix.endsWith('.d.ts')) {
    return false;
  }
  if (/\.test\.(ts|tsx)$/.test(posix)) {
    return false;
  }
  if (posix.includes('/src/test/')) {
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
    for (const [line, hits] of coverage.lines) {
      if (target.lines.has(line)) {
        addHit(target.lines, line, hits);
      }
    }
    for (const [key, hits] of coverage.statements) {
      const line = Number(String(key).split(':')[0]);
      for (const existing of target.statements.keys()) {
        if (Number(String(existing).split(':')[0]) === line) {
          addHit(target.statements, existing, hits);
        }
      }
    }
    for (const [key, hits] of coverage.functions) {
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
    for (const [key, hits] of coverage.branches) {
      if (target.branches.has(key)) {
        addHit(target.branches, key, hits);
      }
    }
  }

  return merged;
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

    for (const hits of coverage.lines.values()) {
      totals.lines.total += 1;
      if (hits > 0) {
        totals.lines.covered += 1;
      }
    }
    for (const hits of coverage.branches.values()) {
      totals.branches.total += 1;
      if (hits > 0) {
        totals.branches.covered += 1;
      }
    }
    for (const hits of coverage.functions.values()) {
      totals.functions.total += 1;
      if (hits > 0) {
        totals.functions.covered += 1;
      }
    }
    for (const hits of coverage.statements.values()) {
      totals.statements.total += 1;
      if (hits > 0) {
        totals.statements.covered += 1;
      }
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
    if (line.startsWith('FN:')) {
      const [fnLine, ...nameParts] = line.slice(3).split(',');
      currentFn = `${Number(fnLine)}:${nameParts.join(',') || 'fn'}`;
      addHit(current.functions, currentFn, 0);
      continue;
    }
    if (line.startsWith('FNDA:')) {
      const [hits, ...nameParts] = line.slice(5).split(',');
      const name = nameParts.join(',') || 'fn';
      const existing = [...current.functions.keys()].find((key) => key.endsWith(`:${name}`));
      addHit(current.functions, existing ?? `0:${name}`, Number(hits));
      continue;
    }
    if (line.startsWith('DA:')) {
      const [number, hits] = line.slice(3).split(',').map(Number);
      addHit(current.lines, number, hits);
      addHit(current.statements, String(number), hits);
      continue;
    }
    if (line.startsWith('BRDA:')) {
      const [number, block, branch, rawHits] = line.slice(5).split(',');
      const hits = rawHits === '-' ? 0 : Number(rawHits);
      addHit(current.branches, `${number}:${block}:${branch}`, hits);
    }
  }

  return store;
}

function decodeXml(value) {
  return String(value)
    .replace(/&lt;/g, '<')
    .replace(/&gt;/g, '>')
    .replace(/&quot;/g, '"')
    .replace(/&apos;/g, "'")
    .replace(/&amp;/g, '&');
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

    for (const method of block.matchAll(/<method\s+name="([^"]+)"[^>]*hits="(\d+)"[\s\S]*?<line number="(\d+)"/g)) {
      addHit(coverage.functions, `${Number(method[3])}:${decodeXml(method[1])}`, Number(method[2]));
    }

    for (const line of block.matchAll(/<line\s+([^>]+)\/>/g)) {
      const attrs = line[1];
      const number = Number(/number="(\d+)"/.exec(attrs)?.[1]);
      const hits = Number(/hits="(\d+)"/.exec(attrs)?.[1]);
      if (!Number.isFinite(number)) {
        throw new Error(`Malformed Cobertura report: line is missing a number in ${repoPath}.`);
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

  if (store.size === 0) {
    throw new Error('Malformed Cobertura report: no class entries.');
  }

  return store;
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
    cwd: path.join(repoRoot, 'src/QueenZone.Mobile'),
  })
    .map((relative) => toPosix(`src/QueenZone.Mobile/${relative}`))
    .filter((repoPath) => isCoverableRepoPath(repoPath))
    .sort();
}

export function assertProductionFilesPresent(store, repoRoot) {
  const missing = listCoverableProductionFiles(repoRoot).filter((repoPath) => !store.has(repoPath));
  if (missing.length > 0) {
    const sample = missing.slice(0, 20).join('\n  ');
    throw new Error(
      `Coverage report is missing ${missing.length} production file(s). collectCoverageFrom must include src/**/*.{ts,tsx}.\n  ${sample}`,
    );
  }
}

export function getChangedLines({ repoRoot, baseRef, headRef, paths }) {
  if (!baseRef) {
    return { skipped: 'No base ref supplied; skipping changed-line coverage gate.', lines: new Map() };
  }

  let resolved = baseRef;
  if (!/^origin\//.test(baseRef)) {
    const remoteRef = `origin/${baseRef}`;
    const probe = spawnSync('git', ['rev-parse', '--verify', '--quiet', remoteRef], { cwd: repoRoot });
    if (probe.status === 0) {
      resolved = remoteRef;
    }
  }

  const available = spawnSync('git', ['rev-parse', '--verify', '--quiet', resolved], { cwd: repoRoot });
  if (available.status !== 0) {
    return { skipped: `Base ref '${baseRef}' is not available locally; skipping changed-line coverage gate.`, lines: new Map() };
  }

  const diff = spawnSync(
    'git',
    ['diff', '--unified=0', '--no-color', `${resolved}...${headRef}`, '--', ...paths],
    { cwd: repoRoot, encoding: 'utf8' },
  );
  if (diff.status !== 0) {
    throw new Error(`Unable to calculate changed lines against '${resolved}'.`);
  }

  const changed = new Map();
  let currentFile = null;
  for (const line of diff.stdout.split(/\r?\n/)) {
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
      skipped: 'Changed mobile TypeScript/TSX lines do not overlap coverable lines in the merged report.',
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
    throw new Error(`Malformed mobile coverage floors file: globalLine must be a number.`);
  }
  if (floors.globalBranch != null && typeof floors.globalBranch !== 'number') {
    throw new Error(`Malformed mobile coverage floors file: globalBranch must be a number or null.`);
  }
  if (typeof floors.changedLine !== 'number') {
    throw new Error(`Malformed mobile coverage floors file: changedLine must be a number.`);
  }

  return floors;
}

function escapeXml(value) {
  return String(value)
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
    .replace(/"/g, '&quot;');
}

function escapeHtml(value) {
  return escapeXml(value).replace(/'/g, '&#39;');
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

  return `<?xml version="1.0" ?>\n<!DOCTYPE coverage SYSTEM "http://cobertura.sourceforge.net/xml/coverage-04.dtd">\n<coverage lines-valid="${merged.lines.total}" lines-covered="${merged.lines.covered}" line-rate="${merged.lines.pct / 100}" branches-valid="${merged.branches.total}" branches-covered="${merged.branches.covered}" branch-rate="${merged.branches.pct / 100}" complexity="0" version="0.1">\n  <sources>\n${sourceXml}\n  </sources>\n  <packages>\n    <package name="QueenZone.Mobile" line-rate="${merged.lines.pct / 100}" branch-rate="${merged.branches.pct / 100}">\n      <classes>\n${classes.join('\n')}\n      </classes>\n    </package>\n  </packages>\n</coverage>\n`;
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
  <title>QueenZone.Mobile coverage</title>
  <style>
    body { font-family: ui-sans-serif, system-ui, sans-serif; margin: 2rem; color: #111; }
    table { border-collapse: collapse; width: 100%; }
    th, td { border-bottom: 1px solid #ddd; padding: 0.4rem 0.5rem; text-align: left; vertical-align: top; }
    code { font-size: 0.9em; }
    .muted { color: #555; }
  </style>
</head>
<body>
  <h1>QueenZone.Mobile coverage</h1>
  <p class="muted">npm test coverage (#871) ≠ #869 contracts ≠ #872 Maestro smoke. Merged totals union Jest and Node by file; they do not sum overlapping reports.</p>
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

  return `## QueenZone.Mobile coverage (#871)

\`npm test coverage\` ≠ \`#869 contracts\` ≠ \`#872 Maestro smoke\`. Contracts and device smoke stay out of these totals.

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
    reports: path.join(defaultRepoRoot, 'src/QueenZone.Mobile/coverage'),
    floors: path.join(defaultRepoRoot, 'scripts/mobile-coverage-floors.json'),
    merged: path.join(defaultRepoRoot, 'src/QueenZone.Mobile/coverage/merged'),
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

export function runGate(args) {
  const log = args.quiet ? () => {} : console.log.bind(console);
  const logError = args.quiet ? () => {} : console.error.bind(console);
  const jestDir = path.join(args.reports, 'jest');
  const nodeDir = path.join(args.reports, 'node');
  const floors = loadFloors(args.floors);

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
        paths: ['src/QueenZone.Mobile/src'],
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

  writeMergedReports({
    store: mergedStore,
    summary,
    destDir: args.merged,
    sources: [path.join(args.repoRoot, 'src/QueenZone.Mobile')],
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

  return summary;
}

function runSelfTest() {
  const tempRoot = path.join(defaultRepoRoot, 'src/QueenZone.Mobile/coverage/self-test');
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
      const b = toRepoPath('src\\api\\client.ts');
      const c = toRepoPath('C:/repo/src/QueenZone.Mobile/src/api/client.ts', [], 'C:/repo');
      const d = toRepoPath('/workspace/src/QueenZone.Mobile/src/api/client.ts', [], '/workspace');
      if (a !== 'src/QueenZone.Mobile/src/api/client.ts') {
        throw new Error(a);
      }
      if (b !== a || c !== a || d !== a) {
        throw new Error(`${a} / ${b} / ${c} / ${d}`);
      }
    });

    assert('exclusions stay narrow', () => {
      if (isCoverableRepoPath('src/QueenZone.Mobile/src/screens/home/HomeScreen.tsx') !== true) {
        throw new Error('screens must stay coverable');
      }
      if (isCoverableRepoPath('src/QueenZone.Mobile/src/api/client.test.tsx')) {
        throw new Error('tests must be excluded');
      }
      if (isCoverableRepoPath('src/QueenZone.Mobile/src/test/fixtures.ts')) {
        throw new Error('fixtures must be excluded');
      }
      if (isCoverableRepoPath('src/QueenZone.Mobile/contracts/consumer.test.ts')) {
        throw new Error('contracts must stay out');
      }
    });

    assert('union does not double-count overlapping lines', () => {
      const lcov = parseLcov('TN:\nSF:src/api/text.ts\nDA:2,1\nDA:3,0\nend_of_record\n');
      const cobertura = parseCobertura(`<?xml version="1.0"?><coverage><sources><source>/repo/src/QueenZone.Mobile</source></sources><packages><package><classes><class filename="src/api/text.ts"><lines><line number="2" hits="4"/><line number="4" hits="0"/></lines></class></classes></package></packages></coverage>`);
      const merged = mergeStores([lcov, cobertura]);
      const summary = summarizeStore(merged, { coverableOnly: false });
      if (summary.lines.total !== 3 || summary.lines.covered !== 1) {
        throw new Error(JSON.stringify(summary.lines));
      }
    });

    assert('overlay keeps the Jest coverable universe', () => {
      const jest = parseCobertura(`<?xml version="1.0"?><coverage><sources><source>/repo/src/QueenZone.Mobile</source></sources><packages><package><classes><class filename="src/api/text.ts"><lines><line number="2" hits="0"/><line number="3" hits="0"/></lines></class></classes></package></packages></coverage>`);
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
        '/tmp/fixture/src/QueenZone.Mobile/src/api/text.ts': {
          path: '/tmp/fixture/src/QueenZone.Mobile/src/api/text.ts',
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
        repoRoot: '/tmp/fixture',
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
          repoRoot: '/tmp/fixture',
          reports: path.join(fixtureRoot, 'coverage'),
          floors: path.join(tempRoot, 'floors-high.json'),
          merged: path.join(tempRoot, 'merged-fail'),
          skipProductionCheck: true,
          changedLines: new Map([['src/QueenZone.Mobile/src/api/text.ts', new Set([3])]]),
          quiet: true,
        });
      } catch (error) {
        failed = /Changed-line coverage/.test(error.message);
      }
      if (!failed) {
        throw new Error('expected changed-line failure');
      }
    });

    assert('changed-line gate skips when no coverable mobile lines changed', () => {
      const summary = runGate({
        repoRoot: '/tmp/fixture',
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

    console.log(cases.join('\n'));
    console.log('Test-MobileCoverageGate self-test passed.');
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
