#!/usr/bin/env node
/**
 * Reproducible proof that a development-standards update works against
 * QueenZone's adopted layout (#2116).
 *
 *   node scripts/development-standards-update-proof.mjs --standards /path/to/development-standards
 *
 * The standards clone must contain the commit in development-standards.lock.json.
 * Nothing real is modified: the script clones the kit and the committed HEAD of
 * this repository into a temporary directory, adds local fixture commits on top
 * of the locked kit commit (no upstream release is touched), and runs the kit's
 * own scripts/update.mjs against the QueenZone clone. Commit local edits first;
 * uncommitted work is not part of the proof.
 *
 * Scenarios: a compatible update (shared doc, managed AGENTS section, new managed
 * file, config key, and a QueenZone-customised script) applies and keeps local
 * paths, floors, maps, and the suppression baseline; repeating it is a no-op; an
 * overlapping change is refused with no file or lock changes; --keep-local
 * resolves that conflict; and a changed shared floor default is refused for review
 * (reviewKeys) even though QueenZone's C# floors equal the kit defaults.
 */
import { createHash } from 'node:crypto';
import { existsSync, mkdtempSync, readdirSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { spawnSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';

const repoRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const BEGIN = '<!-- development-standards:begin -->';
const END = '<!-- development-standards:end -->';
const DELETED = ['config/feature-map.json', 'config/typescript-coverage.json', 'scripts/Test-TypeScriptCoverageGate.mjs'];
// Files the compatible fixture does not touch: QueenZone-owned, kept local, or taken verbatim from the kit.
const UNTOUCHED = [
  'config/suppression-baseline.json',
  'scripts/check-feature-map.mjs',
  'scripts/check-pr-verification.mjs',
  'scripts/check-suppressions.mjs',
  'scripts/Test-CoverageGate.ps1',
  'scripts/Get-CrapReport.ps1',
  'scripts/mobile-coverage-floors.json',
  'docs/feature-map/README.md',
  '.github/pull_request_template.md',
];
const proofVersion = (version, n) => `${version}-proof.${n}`;
const IDENTITY = ['-c', 'user.name=Standards Update Proof', '-c', 'user.email=proof@example.invalid', '-c', 'commit.gpgsign=false'];

let failures = 0;

function log(line = '') {
  console.log(line);
}

const byText = (left, right) => left.localeCompare(right);

function check(label, condition, detail = '') {
  if (!condition) failures += 1;
  const suffix = !condition && detail ? ` — ${detail}` : '';
  log(`${condition ? 'PASS' : 'FAIL'} ${label}${suffix}`);
}

function run(command, args, cwd) {
  const result = spawnSync(command, args, { cwd, encoding: 'utf8', maxBuffer: 50 * 1024 * 1024 });
  if (result.error) throw result.error;
  return { status: result.status, output: `${result.stdout}${result.stderr}`.trim() };
}

function git(cwd, ...args) {
  const result = run('git', [...IDENTITY, ...args], cwd);
  if (result.status !== 0) throw new Error(`git ${args.join(' ')} failed in ${cwd}:\n${result.output}`);
  return result.output;
}

const normalized = (text) => text.replaceAll('\r\n', '\n');
const readText = (root, relative) => {
  const file = path.join(root, relative);
  return existsSync(file) ? normalized(readFileSync(file, 'utf8')) : null;
};

function editText(root, relative, change) {
  const file = path.join(root, relative);
  const before = normalized(readFileSync(file, 'utf8'));
  const after = change(before);
  if (after === before) throw new Error(`Fixture edit did not change ${relative}.`);
  writeFileSync(file, after);
}

function replaceOnce(text, from, to) {
  if (text.split(from).length !== 2) throw new Error(`Fixture anchor not found exactly once: ${from}`);
  return text.replace(from, to);
}

function treeHashes(root) {
  const hashes = new Map();
  const walk = (directory) => {
    for (const entry of readdirSync(directory, { withFileTypes: true })) {
      if (entry.name === '.git') continue;
      const full = path.join(directory, entry.name);
      if (entry.isDirectory()) walk(full);
      else hashes.set(path.relative(root, full).replaceAll('\\', '/'), createHash('sha256').update(readFileSync(full)).digest('hex'));
    }
  };
  walk(root);
  return hashes;
}

function changedPaths(before, after) {
  const names = new Set([...before.keys(), ...after.keys()]);
  return [...names].filter((name) => before.get(name) !== after.get(name)).sort(byText);
}

function outsideManagedSection(guide) {
  return `${guide.slice(0, guide.indexOf(BEGIN))}${guide.slice(guide.indexOf(END) + END.length)}`;
}

function update(kit, project, ...args) {
  const result = run(process.execPath, ['scripts/update.mjs', '--target', project, ...args], kit);
  log(`$ node scripts/update.mjs --target <queenzone> ${args.join(' ')}`.trimEnd());
  for (const line of result.output.split('\n')) log(`    ${line.replace(project, '<queenzone>')}`);
  log(`    (exit ${result.status})`);
  return result;
}

/** Commit a fixture version on top of the locked kit commit; returns its SHA. */
function fixtureVersion(kit, base, version, edits) {
  git(kit, 'checkout', '--quiet', '--detach', base);
  for (const edit of edits) edit(kit);
  editText(kit, 'package.json', (text) => text.replace(/"version": "[^"]+"/, `"version": "${version}"`));
  git(kit, 'add', '--all');
  git(kit, 'commit', '--quiet', '-m', `Proof fixture ${version} (not a release)`);
  return git(kit, 'rev-parse', 'HEAD');
}

function cloneProject(workspace, name) {
  const project = path.join(workspace, name);
  git(workspace, 'clone', '--quiet', '--no-hardlinks', repoRoot, project);
  return project;
}

function parseArgs(argv) {
  const at = argv.indexOf('--standards');
  if (at < 0 || !argv[at + 1]) {
    throw new Error('Usage: node scripts/development-standards-update-proof.mjs --standards /path/to/development-standards');
  }
  return { standards: path.resolve(argv[at + 1]), keep: argv.includes('--keep') };
}

function main() {
  const { standards, keep } = parseArgs(process.argv.slice(2));
  const lock = JSON.parse(readFileSync(path.join(repoRoot, 'development-standards.lock.json'), 'utf8'));
  const workspace = mkdtempSync(path.join(tmpdir(), 'qz-standards-proof-'));
  try {
    const kit = path.join(workspace, 'kit');
    git(workspace, 'clone', '--quiet', '--no-hardlinks', standards, kit);
    git(kit, 'cat-file', '-e', `${lock.commit}^{commit}`);
    const head = git(repoRoot, 'rev-parse', 'HEAD');
    log(`QueenZone commit under test: ${head}`);
    log(`Locked standards: ${lock.commit} (v${lock.version})`);
    log();

    // 1. Compatible update.
    const compatible = fixtureVersion(kit, lock.commit, proofVersion(lock.version, 1), [
      (root) => editText(root, 'docs/testing.md', (text) => `${text}\nProof fixture: a shared testing rule added upstream.\n`),
      (root) => editText(root, 'templates/AGENTS.fragment.md', (text) => `${text.trimEnd()}\n- Proof fixture: a shared agent rule added upstream.\n`),
      // Away from QueenZone's one-line adapter in verify.mjs, so it three-way merges.
      (root) => editText(root, 'scripts/verify.mjs', (text) =>
        replaceOnce(text, "  run(process.execPath, ['scripts/check-feature-map.mjs']);\n", "  run(process.execPath, ['scripts/check-feature-map.mjs']);\n  console.log('Proof fixture: verification finished.');\n")),
      (root) => editText(root, 'development-standards.json', (text) => replaceOnce(text, '"version": 1,\n', '"version": 1,\n  "proofFixtureKey": true,\n')),
      (root) => {
        writeFileSync(path.join(root, 'docs/proof-fixture.md'), '# Proof fixture\n\nA new managed policy document.\n');
        editText(root, 'installation-manifest.json', (text) =>
          replaceOnce(text, '  "files": [\n', '  "files": [\n    {\n      "source": "docs/proof-fixture.md",\n      "target": "docs/proof-fixture.md"\n    },\n'));
      },
    ]);
    log(`## 1. Compatible update ${lock.commit.slice(0, 7)} -> ${compatible.slice(0, 7)}`);
    const project = cloneProject(workspace, 'queenzone');
    const beforeConfig = JSON.parse(readText(project, 'development-standards.json'));
    const beforeGuide = readText(project, 'AGENTS.md');
    const beforeLocal = new Map(UNTOUCHED.map((name) => [name, readText(project, name)]));
    const beforeMaps = treeHashes(path.join(project, 'docs/feature-map'));
    const beforeHashes = treeHashes(project);
    const preview = update(kit, project, '--dry-run');
    check('dry-run succeeds and writes nothing', preview.status === 0 && changedPaths(beforeHashes, treeHashes(project)).length === 0);
    const applied = update(kit, project);
    check('update applies', applied.status === 0, applied.output);
    const lockAfter = JSON.parse(readText(project, 'development-standards.lock.json'));
    check('lock advances to the fixture commit and version', lockAfter.commit === compatible && lockAfter.version === proofVersion(lock.version, 1));
    const config = JSON.parse(readText(project, 'development-standards.json'));
    check('config merges the upstream key', config.proofFixtureKey === true);
    check('solution path, floors, mobile floors file, and uiPaths survive',
      JSON.stringify({ ...config, proofFixtureKey: undefined }) === JSON.stringify(beforeConfig));
    for (const [name, text] of beforeLocal) check(`unchanged: ${name}`, readText(project, name) === text);
    check('canonical feature maps are untouched', changedPaths(beforeMaps, treeHashes(path.join(project, 'docs/feature-map'))).length === 0);
    for (const name of DELETED) check(`deliberately absent file stays absent: ${name}`, readText(project, name) === null);
    const guide = readText(project, 'AGENTS.md');
    check('AGENTS.md project guidance outside the markers is unchanged', outsideManagedSection(guide) === outsideManagedSection(beforeGuide));
    check('AGENTS.md managed section gains the upstream rule', guide.includes('- Proof fixture: a shared agent rule added upstream.'));
    const runner = readText(project, 'scripts/verify.mjs');
    check('customised verify.mjs three-way merges: upstream line plus the local mobile-gate adapter',
      runner.includes('Proof fixture: verification finished.') && runner.includes("'scripts/Test-MobileCoverageGate.mjs'"));
    check('shared doc updated and new managed file added',
      readText(project, 'docs/testing.md').includes('Proof fixture: a shared testing rule') && readText(project, 'docs/proof-fixture.md') !== null);
    const touched = changedPaths(beforeHashes, treeHashes(project));
    log(`    changed files: ${touched.join(', ')}`);
    check('only managed paths and the lock changed', touched.every((name) =>
      ['AGENTS.md', 'development-standards.json', 'development-standards.lock.json', 'docs/proof-fixture.md', 'docs/testing.md', 'scripts/verify.mjs'].includes(name)));
    log();

    log('## 2. Repeat the same version');
    const afterFirst = treeHashes(project);
    const repeat = update(kit, project);
    check('repeated update is a no-op', repeat.status === 0 && changedPaths(afterFirst, treeHashes(project)).length === 0);
    log();

    // 3. Overlapping change: refused with no writes, including clean files.
    const conflicting = fixtureVersion(kit, lock.commit, proofVersion(lock.version, 2), [
      (root) => editText(root, 'development-standards.json', (text) => replaceOnce(text, '"solution": "YourProject.sln"', '"solution": "Example.sln"')),
      (root) => editText(root, 'config/typescript-coverage.json', (text) => replaceOnce(text, '"globalLine": 90', '"globalLine": 91')),
      // QueenZone keeps its own PR checker (keptLocal), so any upstream edit overlaps it.
      (root) => editText(root, 'scripts/check-pr-verification.mjs', (text) => `// Proof fixture: upstream checker change.\n${text}`),
      (root) => editText(root, 'docs/testing.md', (text) => `${text}\nProof fixture: a clean change that must not be written during a conflict.\n`),
    ]);
    log(`## 3. Conflicting update ${lock.commit.slice(0, 7)} -> ${conflicting.slice(0, 7)}`);
    const conflictProject = cloneProject(workspace, 'queenzone-conflict');
    const beforeConflict = treeHashes(conflictProject);
    const refused = update(kit, conflictProject);
    check('conflict exits nonzero', refused.status !== 0);
    check('conflict names the overlapping config line', refused.output.includes('CONFLICT: development-standards.json'));
    check('conflict names the edited file QueenZone deliberately removed', refused.output.includes('CONFLICT: config/typescript-coverage.json'));
    check('conflict names the upstream edit to the kept-local PR checker', refused.output.includes('CONFLICT: scripts/check-pr-verification.mjs'));
    check('no target file or lock changed, including docs/testing.md', changedPaths(beforeConflict, treeHashes(conflictProject)).length === 0);
    log();

    log('## 4. Reviewed --keep-local resolution');
    const keepPaths = ['development-standards.json', 'config/typescript-coverage.json', 'scripts/check-pr-verification.mjs'];
    const keptBefore = new Map(keepPaths.map((name) => [name, readText(conflictProject, name)]));
    const kept = update(kit, conflictProject, ...keepPaths.flatMap((name) => ['--keep-local', name]));
    const keptLock = JSON.parse(readText(conflictProject, 'development-standards.lock.json'));
    check('keep-local update applies', kept.status === 0, kept.output);
    for (const [name, text] of keptBefore) check(`kept QueenZone version: ${name}`, readText(conflictProject, name) === text);
    check('clean change from the same version applies', readText(conflictProject, 'docs/testing.md').includes('must not be written during a conflict'));
    check('lock records the kept paths', keptLock.commit === conflicting && JSON.stringify([...keptLock.keptLocal].sort(byText)) === JSON.stringify([...keepPaths].sort(byText)));
    log();

    // 5. QueenZone's C# floors equal the kit defaults; reviewKeys must stop a default change reaching them.
    log('## 5. Shared floor default changes are refused for review');
    const changedDefault = fixtureVersion(kit, lock.commit, proofVersion(lock.version, 3), [
      (root) => editText(root, 'development-standards.json', (text) => replaceOnce(text, '"changedLine": 70', '"changedLine": 80')),
    ]);
    log(`### changedLine 70 -> 80 (${lock.commit.slice(0, 7)} -> ${changedDefault.slice(0, 7)})`);
    const changedProject = cloneProject(workspace, 'queenzone-changed-default');
    const beforeChanged = treeHashes(changedProject);
    const changedResult = update(kit, changedProject);
    check('a kit changedLine default change is refused, naming the floor, with no writes',
      changedResult.status !== 0 && changedResult.output.includes('dotnet.changedLine 70 -> 80') && changedPaths(beforeChanged, treeHashes(changedProject)).length === 0);
    const globalDefault = fixtureVersion(kit, lock.commit, proofVersion(lock.version, 4), [
      (root) => editText(root, 'development-standards.json', (text) => replaceOnce(text, '"globalLine": 91', '"globalLine": 95')),
    ]);
    log(`### globalLine 91 -> 95 (${lock.commit.slice(0, 7)} -> ${globalDefault.slice(0, 7)})`);
    const globalProject = cloneProject(workspace, 'queenzone-global-default');
    const globalResult = update(kit, globalProject);
    check('a kit globalLine default change is refused',
      globalResult.status !== 0 && globalResult.output.includes('CONFLICT: development-standards.json'));
    log('    Floors are QueenZone policy: adopt a new value by editing development-standards.json deliberately.');
  } finally {
    if (keep) log(`\nFixtures kept at ${workspace}`);
    else rmSync(workspace, { recursive: true, force: true });
  }
  log();
  log(failures ? `${failures} check(s) failed.` : 'All update-proof checks passed.');
  process.exitCode = failures ? 1 : 0;
}

try {
  main();
} catch (error) {
  console.error(error.message);
  process.exitCode = 1;
}
