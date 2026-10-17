#!/usr/bin/env node
/**
 * Fail when GitHub Actions workflow blobs are stored with CRLF (#2343 class 1).
 *
 * `.gitattributes` already sets `.github/workflows/*.yml eol=lf`, but
 * Dependabot API commits skip attribute normalisation (#2305). The working
 * tree after checkout can look like LF while the blob is still CRLF, so this
 * check reads git index blobs, not the smudged worktree.
 *
 *   node scripts/check-eol.mjs
 */
import { existsSync } from 'node:fs';
import { execFileSync } from 'node:child_process';
import path from 'node:path';
import { pathToFileURL } from 'node:url';

const knownGitExecutables = [
  '/usr/bin/git',
  '/usr/local/bin/git',
  '/opt/homebrew/bin/git',
  String.raw`C:\Program Files\Git\cmd\git.exe`,
  String.raw`C:\Program Files\Git\mingw64\bin\git.exe`,
];

export const WORKFLOW_GLOBS = Object.freeze(['.github/workflows/*.yml', '.github/workflows/*.yaml']);
export const RENORMALIZE_HINT = 'run git add --renormalize .github/workflows';

/** An absolute git path (GIT_EXECUTABLE, else a standard install). */
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
    throw new Error(
      `git not found. Set GIT_EXECUTABLE to an absolute path or install git at ${knownGitExecutables.join(', ')}.`,
    );
  }
  return found;
}

export function toPosix(filePath) {
  return String(filePath || '').replaceAll('\\', '/');
}

export function hasCrlf(bytes) {
  const buffer = Buffer.isBuffer(bytes) ? bytes : Buffer.from(bytes ?? '');
  return buffer.includes(0x0d);
}

export function formatDiagnostic(filePath) {
  return `QZ-EOL: ${toPosix(filePath)} has CRLF; ${RENORMALIZE_HINT}`;
}

export function isWorkflowPath(filePath) {
  const posix = toPosix(filePath);
  if (!posix.startsWith('.github/workflows/')) {
    return false;
  }
  const rest = posix.slice('.github/workflows/'.length);
  return rest.length > 0 && !rest.includes('/') && (rest.endsWith('.yml') || rest.endsWith('.yaml'));
}

function gitOutput(git, root, args, options = {}) {
  return execFileSync(git, ['-C', root, ...args], {
    stdio: ['ignore', 'pipe', 'pipe'],
    maxBuffer: 20 * 1024 * 1024,
    ...options,
  });
}

export function listWorkflowFiles(root, git = resolveGitExecutable()) {
  const raw = gitOutput(git, root, ['ls-files', '-z', '--', ...WORKFLOW_GLOBS]);
  return raw
    .toString('utf8')
    .split('\0')
    .filter(Boolean)
    .map(toPosix)
    .filter(isWorkflowPath)
    .sort((left, right) => left.localeCompare(right));
}

export function readIndexBlob(root, filePath, git = resolveGitExecutable()) {
  return gitOutput(git, root, ['cat-file', 'blob', `:${toPosix(filePath)}`]);
}

export function findCrlfWorkflows(root, git = resolveGitExecutable()) {
  return listWorkflowFiles(root, git).filter((filePath) => hasCrlf(readIndexBlob(root, filePath, git)));
}

export function main({
  root = process.cwd(),
  git = resolveGitExecutable(),
  log = console.log,
  error = console.error,
} = {}) {
  const offenders = findCrlfWorkflows(root, git);
  if (offenders.length === 0) {
    log('Workflow blobs are LF.');
    return 0;
  }
  for (const filePath of offenders) {
    error(formatDiagnostic(filePath));
  }
  return 1;
}

if (import.meta.url === pathToFileURL(process.argv[1] ?? '').href) {
  process.exitCode = main();
}
