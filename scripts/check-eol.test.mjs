import { mkdirSync, mkdtempSync, readFileSync, writeFileSync } from 'node:fs';
import { execFileSync, spawnSync } from 'node:child_process';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { test } from 'node:test';
import assert from 'node:assert/strict';
import {
  RENORMALIZE_HINT,
  findCrlfWorkflows,
  formatDiagnostic,
  hasCrlf,
  isWorkflowPath,
  main,
  resolveGitExecutable,
} from './check-eol.mjs';

const script = fileURLToPath(new URL('./check-eol.mjs', import.meta.url));
const git = resolveGitExecutable();

function gitIn(dir, args, options = {}) {
  return execFileSync(git, ['-C', dir, ...args], {
    encoding: 'utf8',
    stdio: ['ignore', 'pipe', 'pipe'],
    ...options,
  }).trim();
}

function initRepo() {
  const dir = mkdtempSync(path.join(tmpdir(), 'qz-eol-'));
  gitIn(dir, ['init', '-q']);
  gitIn(dir, ['config', 'user.name', 'EOL fixture']);
  gitIn(dir, ['config', 'user.email', 'eol@example.invalid']);
  gitIn(dir, ['config', 'core.autocrlf', 'false']);
  mkdirSync(path.join(dir, '.github', 'workflows'), { recursive: true });
  writeFileSync(path.join(dir, '.gitattributes'), '.github/workflows/*.yml text eol=lf\n');
  gitIn(dir, ['add', '.gitattributes']);
  gitIn(dir, ['commit', '-qm', 'attributes']);
  return dir;
}

/** Store bytes in the index without the text/eol clean filter, like a Dependabot API commit. */
function addBlobWithoutFilters(dir, filePath, bytes) {
  const sha = execFileSync(git, ['-C', dir, 'hash-object', '-w', '--stdin'], {
    input: bytes,
    stdio: ['pipe', 'pipe', 'pipe'],
  })
    .toString('utf8')
    .trim();
  gitIn(dir, ['update-index', '--add', '--cacheinfo', `100644,${sha},${filePath}`]);
}

test('hasCrlf treats any CR as a workflow line-ending defect', () => {
  assert.equal(hasCrlf(Buffer.from('name: ci\n')), false);
  assert.equal(hasCrlf(Buffer.from('name: ci\r\n')), true);
  assert.equal(hasCrlf(Buffer.from('name: ci\r\njobs:\n')), true);
  assert.equal(hasCrlf(Buffer.from('name: ci\r')), true);
  assert.equal(hasCrlf(Buffer.from('')), false);
});

test('formatDiagnostic names the file and the renormalize fix', () => {
  assert.equal(
    formatDiagnostic('.github/workflows/ci.yml'),
    `QZ-EOL: .github/workflows/ci.yml has CRLF; ${RENORMALIZE_HINT}`,
  );
  assert.equal(
    formatDiagnostic('.github\\workflows\\ci.yml'),
    'QZ-EOL: .github/workflows/ci.yml has CRLF; run git add --renormalize .github/workflows',
  );
});

test('isWorkflowPath matches only top-level workflow YAML', () => {
  assert.equal(isWorkflowPath('.github/workflows/ci.yml'), true);
  assert.equal(isWorkflowPath('.github/workflows/ci.yaml'), true);
  assert.equal(isWorkflowPath('.github/workflows/nested/ci.yml'), false);
  assert.equal(isWorkflowPath('.github/actions/setup/action.yml'), false);
  assert.equal(isWorkflowPath('scripts/check-eol.mjs'), false);
});

test('findCrlfWorkflows reads the git blob, not a smudged LF worktree (#2305)', () => {
  const dir = initRepo();
  const workflow = '.github/workflows/ci.yml';
  addBlobWithoutFilters(dir, workflow, Buffer.from('name: ci\r\non: push\r\n'));
  gitIn(dir, ['commit', '-qm', 'dependabot-style CRLF rewrite']);
  // Simulate a Linux checkout with eol=lf: worktree is LF, blob stays CRLF.
  writeFileSync(path.join(dir, workflow), 'name: ci\non: push\n');
  assert.equal(hasCrlf(execFileSync(git, ['-C', dir, 'cat-file', 'blob', `:${workflow}`])), true);
  assert.equal(hasCrlf(readFileSync(path.join(dir, workflow))), false);
  assert.deepEqual(findCrlfWorkflows(dir, git), [workflow]);

  const errors = [];
  assert.equal(main({ root: dir, git, log() {}, error: (line) => errors.push(line) }), 1);
  assert.deepEqual(errors, [`QZ-EOL: ${workflow} has CRLF; run git add --renormalize .github/workflows`]);
});

test('findCrlfWorkflows accepts LF workflow blobs', () => {
  const dir = initRepo();
  const workflow = '.github/workflows/ci.yml';
  addBlobWithoutFilters(dir, workflow, Buffer.from('name: ci\non: push\n'));
  gitIn(dir, ['commit', '-qm', 'lf workflow']);
  assert.deepEqual(findCrlfWorkflows(dir, git), []);
  assert.equal(main({ root: dir, git, log() {}, error() {} }), 0);
});

test('CLI fails closed on a CRLF workflow blob and prints QZ-EOL', () => {
  const dir = initRepo();
  addBlobWithoutFilters(dir, '.github/workflows/ci.yml', Buffer.from('name: ci\r\n'));
  gitIn(dir, ['commit', '-qm', 'crlf']);
  const result = spawnSync(process.execPath, [script], { cwd: dir, encoding: 'utf8' });
  assert.equal(result.status, 1);
  assert.match(result.stderr, /QZ-EOL: \.github\/workflows\/ci\.yml has CRLF; run git add --renormalize \.github\/workflows/);
});

test('resolveGitExecutable requires an absolute override', () => {
  assert.throws(() => resolveGitExecutable({ GIT_EXECUTABLE: 'git' }, () => false), /absolute path/);
  assert.equal(
    resolveGitExecutable({ GIT_EXECUTABLE: path.resolve('/opt/git/bin/git') }, () => false),
    path.resolve('/opt/git/bin/git'),
  );
});
