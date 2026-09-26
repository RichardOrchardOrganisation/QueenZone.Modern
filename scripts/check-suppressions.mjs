#!/usr/bin/env node
/**
 * Suppression ratchet (#1801).
 *
 * Node 24, no npm dependencies. Counts lint, analyzer, and coverage
 * suppressions in tracked source files and fails when a file gains one that
 * does not link an issue on the same line (`#1234` or a GitHub issue URL).
 * Linked suppressions are always allowed; unlinked ones must fit the
 * per-file counts in config/suppression-baseline.json.
 *
 * The check also fails when a file drops below its baseline, so removals
 * lower the ceiling instead of leaving room for the next copy.
 *
 *   node scripts/check-suppressions.mjs          # check against the baseline
 *   node scripts/check-suppressions.mjs --write  # rewrite the baseline
 *   node scripts/check-suppressions.mjs --list   # print every unlinked suppression
 *
 * Files are found by walking the working tree (build output, dependencies and dot-directories
 * are skipped), so untracked source files count too. The audit behind the baseline is
 * docs/architecture/workaround-audit.md.
 */
import { readdirSync, readFileSync, writeFileSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';

export const BASELINE_PATH = 'config/suppression-baseline.json';

/** Each kind is matched at most once per line. */
export const KINDS = {
  'eslint-disable': /eslint-disable(?:-next-line|-line)?\b/,
  'ts-ignore': /@ts-(?:ignore|expect-error|nocheck)\b/,
  'pragma-warning-disable': /#pragma\s+warning\s+disable\b/,
  nosonar: /\bNOSONAR\b/,
  'suppress-message': /\b(?:Unconditional)?SuppressMessage(?:Attribute)?\b/,
  'exclude-from-code-coverage': /\bExcludeFromCodeCoverage(?:Attribute)?\b/,
  'nuget-audit-suppress': /<NuGetAuditSuppress\b/,
  'coverage-ignore': /\b(?:istanbul|c8)\s+ignore\b/,
  'hack-comment': /(?:\/\/|\/\*|#|<!--|@\*)\s*(?:HACK|FIXME)\b/,
};

const ISSUE_LINK = /(?:^|[^\w&])#\d{2,}\b|github\.com\/[\w.-]+\/[\w.-]+\/issues\/\d+/;

const SOURCE_EXTENSIONS = new Set([
  '.cs',
  '.cshtml',
  '.csproj',
  '.props',
  '.targets',
  '.ts',
  '.tsx',
  '.js',
  '.jsx',
  '.mjs',
  '.cjs',
]);

const IGNORED_PATHS = [
  /(^|\/)node_modules\//,
  /(^|\/)Migrations\//, // EF-generated designer files carry their own pragmas.
  /(^|\/)wwwroot\/lib\//, // vendored third-party bundles
  /\.min\.js$/,
  /^design\//,
  /^designs\//,
  /^docs\//,
  /^scripts\/check-suppressions(?:\.test)?\.mjs$/,
];

/**
 * Build output, dependencies, and generated native projects. Skipped by name at any depth, along
 * with every dot-directory (.git, .github, .claude worktrees, .expo, ...).
 */
const SKIPPED_DIRECTORIES = new Set(['node_modules', 'bin', 'obj', 'TestResults', 'coverage', 'artifacts']);
const SKIPPED_DIRECTORY_PATHS = new Set(['src/QueenZone.Mobile/ios', 'src/QueenZone.Mobile/android']);

export function repoRootFrom(moduleUrl = import.meta.url) {
  return path.resolve(path.dirname(fileURLToPath(moduleUrl)), '..');
}

export function isScannedPath(relativePath) {
  const posix = relativePath.replaceAll('\\', '/');
  return SOURCE_EXTENSIONS.has(path.extname(posix)) && !IGNORED_PATHS.some((pattern) => pattern.test(posix));
}

/**
 * Returns one entry per suppression in `text`: `{ kind, line, linked, text }`.
 */
export function findSuppressions(text) {
  const found = [];
  const lines = String(text || '').split(/\r?\n/);
  lines.forEach((lineText, index) => {
    for (const [kind, pattern] of Object.entries(KINDS)) {
      if (pattern.test(lineText)) {
        found.push({ kind, line: index + 1, linked: ISSUE_LINK.test(lineText), text: lineText.trim() });
      }
    }
  });
  return found;
}

/** Unlinked counts as `{ path: { kind: count } }`, with sorted keys. */
export function countUnlinked(filesWithText) {
  const counts = {};
  for (const [file, text] of filesWithText) {
    for (const hit of findSuppressions(text)) {
      if (hit.linked) {
        continue;
      }
      counts[file] ??= {};
      counts[file][hit.kind] = (counts[file][hit.kind] ?? 0) + 1;
    }
  }
  return sortCounts(counts);
}

function sortCounts(counts) {
  const sorted = {};
  for (const file of Object.keys(counts).sort(compareText)) {
    sorted[file] = {};
    for (const kind of Object.keys(counts[file]).sort(compareText)) {
      sorted[file][kind] = counts[file][kind];
    }
  }
  return sorted;
}

/** Locale-independent order. */
function compareText(left, right) {
  if (left < right) {
    return -1;
  }
  return left > right ? 1 : 0;
}

/**
 * Compares current unlinked counts with the baseline.
 * `added` entries fail the check because a file gained an unlinked suppression.
 * `removed` entries fail too, so the baseline is lowered with --write.
 */
export function compareWithBaseline(baseline, current) {
  const added = [];
  const removed = [];
  const files = new Set([...Object.keys(baseline), ...Object.keys(current)]);
  for (const file of [...files].sort(compareText)) {
    const kinds = new Set([...Object.keys(baseline[file] ?? {}), ...Object.keys(current[file] ?? {})]);
    for (const kind of [...kinds].sort(compareText)) {
      const was = baseline[file]?.[kind] ?? 0;
      const now = current[file]?.[kind] ?? 0;
      if (now > was) {
        added.push({ file, kind, was, now });
      } else if (now < was) {
        removed.push({ file, kind, was, now });
      }
    }
  }
  return { added, removed };
}

export function totalsByKind(counts) {
  const totals = {};
  for (const kinds of Object.values(counts)) {
    for (const [kind, count] of Object.entries(kinds)) {
      totals[kind] = (totals[kind] ?? 0) + count;
    }
  }
  return Object.fromEntries(Object.entries(totals).sort(([a], [b]) => compareText(a, b)));
}

/**
 * Repo-relative POSIX paths of every file under `root`, minus build output and dot-directories.
 * Walks the tree instead of running `git ls-files`, so the check starts no external process.
 */
export function listSourceFiles(root, relativeDir = '') {
  const files = [];
  for (const entry of readdirSync(path.join(root, relativeDir), { withFileTypes: true })) {
    const relative = relativeDir ? `${relativeDir}/${entry.name}` : entry.name;
    if (entry.isDirectory()) {
      if (!entry.name.startsWith('.') && !SKIPPED_DIRECTORIES.has(entry.name) && !SKIPPED_DIRECTORY_PATHS.has(relative)) {
        files.push(...listSourceFiles(root, relative));
      }
    } else if (entry.isFile()) {
      files.push(relative);
    }
  }
  return files;
}

export function readRepo(root, files = listSourceFiles(root)) {
  return files
    .filter(isScannedPath)
    .sort(compareText)
    .map((file) => [file, readFileSync(path.join(root, file), 'utf8')]);
}

export function formatBaseline(counts) {
  const document = {
    $comment:
      'Unlinked suppressions per file (#1801). Generated by `node scripts/check-suppressions.mjs --write`; do not raise counts by hand. See docs/architecture/workaround-audit.md.',
    files: counts,
  };
  return `${JSON.stringify(document, null, 2)}\n`;
}

export function main(argv = process.argv.slice(2), { root = repoRootFrom(), log = console.log, error = console.error } = {}) {
  const filesWithText = readRepo(root);
  const current = countUnlinked(filesWithText);
  const baselineFile = path.join(root, BASELINE_PATH);

  if (argv.includes('--list')) {
    for (const [file, text] of filesWithText) {
      for (const hit of findSuppressions(text).filter((entry) => !entry.linked)) {
        log(`${file}:${hit.line} [${hit.kind}] ${hit.text}`);
      }
    }
    return 0;
  }

  if (argv.includes('--write')) {
    writeFileSync(baselineFile, formatBaseline(current));
    log(`Wrote ${BASELINE_PATH}: ${JSON.stringify(totalsByKind(current))}`);
    return 0;
  }

  const baseline = JSON.parse(readFileSync(baselineFile, 'utf8')).files ?? {};
  const { added, removed } = compareWithBaseline(baseline, current);
  log(`Unlinked suppressions: ${JSON.stringify(totalsByKind(current))}`);

  if (added.length > 0) {
    error('\nNew suppressions without a linked issue:');
    for (const entry of added) {
      error(`  ${entry.file}: ${entry.kind} ${entry.was} -> ${entry.now}`);
    }
    error(
      '\nFix the code instead, or put the issue that removes the suppression on the same line, e.g.\n' +
        '  // eslint-disable-next-line some/rule -- reason (#1234)\n' +
        'Run `node scripts/check-suppressions.mjs --list` to see each line. See docs/architecture/workaround-audit.md.',
    );
  }

  if (removed.length > 0) {
    error('\nThese files now have fewer suppressions than the baseline:');
    for (const entry of removed) {
      error(`  ${entry.file}: ${entry.kind} ${entry.was} -> ${entry.now}`);
    }
    error('\nRun `node scripts/check-suppressions.mjs --write` and commit config/suppression-baseline.json to lower it.');
  }

  return added.length > 0 || removed.length > 0 ? 1 : 0;
}

if (import.meta.url === pathToFileURL(process.argv[1] ?? '').href) {
  process.exitCode = main();
}
