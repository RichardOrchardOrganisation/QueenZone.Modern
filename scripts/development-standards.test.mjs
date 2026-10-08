// Adoption contract for RichardOrchardOrganisation/development-standards (#2116).
// docs/architecture/development-standards.md records why each managed file
// is adopted, kept local, or deliberately absent.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { existsSync, readFileSync, statSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { loadPolicy } from './check-suppressions.mjs';
import { configuredFloorsPath, loadFloors } from './Test-MobileCoverageGate.mjs';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const read = (relative) => readFileSync(path.join(root, relative), 'utf8');
const json = (relative) => JSON.parse(read(relative));
const BEGIN = '<!-- development-standards:begin -->';
const END = '<!-- development-standards:end -->';

test('lock pins a recoverable standards commit and the adoption record names it', () => {
  const lock = json('development-standards.lock.json');
  assert.equal(lock.repository, 'https://github.com/RichardOrchardOrganisation/development-standards');
  assert.match(lock.commit, /^[a-f0-9]{40}$/);
  assert.match(lock.version, /^\d+\.\d+\.\d+$/);
  const record = read('docs/architecture/development-standards.md');
  assert.ok(record.includes(lock.commit), 'adoption record must name the locked commit');
  assert.ok(record.includes(lock.version), 'adoption record must name the locked kit version');
  assert.ok(existsSync(path.join(root, 'docs/development-standards-LICENSE')), 'kit attribution is retained');
});

test('AGENTS.md has exactly one managed standards section for the updater', () => {
  const guide = read('AGENTS.md');
  assert.equal(guide.split(BEGIN).length, 2, 'one begin marker');
  assert.equal(guide.split(END).length, 2, 'one end marker');
  assert.ok(guide.indexOf(BEGIN) < guide.indexOf(END));
});

test('development-standards.json describes the real solution, floors, and UI surfaces', () => {
  const config = json('development-standards.json');
  assert.equal(config.version, 1);
  assert.ok(statSync(path.join(root, config.dotnet.solution)).isFile());
  for (const floor of [config.dotnet.globalLine, config.dotnet.changedLine]) {
    assert.ok(Number.isFinite(floor) && floor > 0 && floor <= 100, `C# floor ${floor}`);
  }
  const mobile = json(path.join(config.typescript.projectRoot, 'package.json'));
  for (const script of ['typecheck', 'lint', 'test:coverage']) {
    assert.ok(mobile.scripts?.[script], `verify.mjs typescript profile needs npm run ${script}`);
  }
  assert.ok(config.uiPaths.length > 0, 'an empty uiPaths list is not an adoption workaround here');
  for (const uiPath of config.uiPaths) {
    assert.ok(uiPath.endsWith('/') && statSync(path.join(root, uiPath)).isDirectory(), uiPath);
  }
});

test('each numeric floor has one source', () => {
  const config = json('development-standards.json');
  assert.equal(configuredFloorsPath(root), path.join(root, config.typescript.floors));
  const floors = loadFloors(configuredFloorsPath(root));
  for (const key of ['globalLine', 'globalBranch', 'changedLine']) {
    assert.ok(Number.isFinite(floors[key]), `mobile ${key}`);
  }
  for (const workflow of ['.github/workflows/ci.yml', 'scripts/verify.mjs']) {
    const text = read(workflow);
    assert.doesNotMatch(text, /-GlobalLineThreshold\s+\d|-ChangedLineThreshold\s+\d/, `${workflow} repeats a C# floor`);
  }
});

test('managed kit files that would compete with QueenZone sources stay absent', () => {
  // config/feature-map.json -> docs/feature-map/; config/typescript-coverage.json ->
  // scripts/mobile-coverage-floors.json; Test-TypeScriptCoverageGate.mjs -> Test-MobileCoverageGate.mjs.
  for (const relative of ['config/feature-map.json', 'config/typescript-coverage.json', 'scripts/Test-TypeScriptCoverageGate.mjs']) {
    assert.equal(existsSync(path.join(root, relative)), false, `${relative} would be a second source of truth`);
  }
});

test('suppression policy skips generated native projects and needs (#NN) issue links', () => {
  const policy = loadPolicy(root);
  assert.deepEqual([...policy.skippedPaths].sort(), ['src/QueenZone.Mobile/android', 'src/QueenZone.Mobile/ios']);
  assert.equal(policy.issueLink.test('reason (#7)'), false);
  assert.equal(policy.issueLink.test('reason (#1801)'), true);
});

test('verify.mjs only invokes scripts that exist', () => {
  const referenced = [...read('scripts/verify.mjs').matchAll(/'(scripts\/[\w.-]+)'/g)].map((match) => match[1]);
  assert.ok(referenced.includes('scripts/Test-MobileCoverageGate.mjs'));
  for (const relative of referenced) {
    assert.ok(existsSync(path.join(root, relative)), relative);
  }
});
