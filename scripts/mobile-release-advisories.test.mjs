import assert from 'node:assert/strict';
import { mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { spawnSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';
import { test } from 'node:test';
import { evaluatePolicy } from '../src/QueenZone.Mobile/scripts/check-npm-advisories.mjs';

const root = fileURLToPath(new URL('../', import.meta.url));
const mobile = path.join(root, 'src/QueenZone.Mobile');
const validator = path.join(mobile, 'scripts/check-npm-advisories.mjs');
const allowlist = JSON.parse(readFileSync(path.join(mobile, 'npm-advisory-allowlist.json'), 'utf8'));
const bracesGhsa = 'GHSA-vfj7-8cjw-p6xm';
const auditFor = (ghsa = bracesGhsa, severity = 'high', packageName = 'braces') => ({
  vulnerabilities: {
    [packageName]: {
      severity,
      via: [{ name: packageName, severity, url: `https://github.com/advisories/${ghsa}` }],
    },
  },
});

test('withdrawn braces exception is absent and cannot block the next UTC day', () => {
  assert.equal(allowlist.advisories.some((entry) => entry.ghsa === bracesGhsa), false);
  for (const now of ['2026-10-31T23:59:59.999Z', '2026-11-01T00:00:00.000Z', '2026-11-01T08:00:00+08:00']) {
    assert.equal(evaluatePolicy({ audit: { vulnerabilities: {} }, allowlist, now: new Date(now) }).ok, true);
    const result = evaluatePolicy({ audit: auditFor(), allowlist, now: new Date(now) });
    assert.equal(result.ok, false);
    assert.match(result.failures.join('\n'), /Unallowlisted high GHSA-VFJ7-8CJW-P6XM/);
  }
});

// Historical fixture: expiry enforcement must survive removal of the committed exception.
const historicalAllowlist = { advisories: [{
  ghsa: bracesGhsa,
  package: 'braces',
  via: 'historical tooling fixture',
  exploitability: 'Historical exception for the release-policy regression test (#2016).',
  owner: 'QueenZone maintainers',
  expires: '2026-10-31',
}] };

test('expired entries fail even when the advisory is absent from the audit', () => {
  const audit = { vulnerabilities: {} };
  assert.equal(evaluatePolicy({ audit, allowlist: historicalAllowlist, now: new Date('2026-10-31T23:59:59.999Z') }).ok, true);
  for (const now of ['2026-11-01T00:00:00.000Z', '2026-11-01T08:00:00+08:00']) {
    assert.throws(
      () => evaluatePolicy({ audit, allowlist: historicalAllowlist, now: new Date(now) }),
      /Expired allowlist entry GHSA-VFJ7-8CJW-P6XM/,
    );
  }
});

function job(source, id) {
  const lines = source.split('\n');
  const start = lines.indexOf(`  ${id}:`);
  assert(start >= 0, `missing job ${id}`);
  const next = lines.findIndex((line, index) => index > start && /^  [\w-]+:/.test(line));
  return lines.slice(start, next < 0 ? undefined : next).join('\n');
}

function assertReleaseGate(source, publicationJob) {
  const preflight = job(source, 'preflight');
  const steps = preflight.split(/^      - /m).slice(1);
  const install = steps.findIndex((step) => /^        run: npm ci --no-audit$/m.test(step));
  const gates = steps.filter((step) => /^        run: node scripts\/check-npm-advisories\.mjs$/m.test(step));
  assert.equal(gates.length, 1, 'exactly one full-graph validator must run');
  const gateIndex = steps.indexOf(gates[0]);
  const remainingPreflight = steps.findIndex((step) => /^        run: npm run preflight$/m.test(step));
  assert(install >= 0 && install < gateIndex && gateIndex < remainingPreflight, 'install -> advisory gate -> preflight');
  assert.doesNotMatch(preflight, /continue-on-error:/, 'policy failure must fail the preflight job');
  assert.doesNotMatch(gates[0], /^        if:/m, 'the advisory check must be unconditional');
  const publication = job(source, publicationJob);
  assert.match(publication, /^    needs: preflight$/m);
  assert.match(publication, /^    if: always\(\) && github\.ref == 'refs\/heads\/main' && needs\.preflight\.result == 'success'$/m);
  assert.doesNotMatch(publication, /^    continue-on-error:/m);
  assert.match(preflight, /^          ref: \$\{\{ github\.sha \}\}$/m);
  assert.match(publication, /^          ref: \$\{\{ github\.sha \}\}$/m);
}

for (const [file, publicationJob] of [
  ['publish-ios-testflight.yml', 'publish-ios'],
  ['publish-android-google-play.yml', 'publish-android'],
]) {
  const source = readFileSync(path.join(root, '.github/workflows', file), 'utf8');
  test(`${file}: full-graph policy runs before preflight and publication requires its success`, () => {
    assertReleaseGate(source, publicationJob);
  });
  test(`${file}: contract rejects skipped/ignored gates and publication without preflight success`, () => {
    const run = '        run: node scripts/check-npm-advisories.mjs';
    for (const mutated of [
      source.replace(run, `${run}\n        continue-on-error: true`),
      source.replace(run, `${run}\n        if: false`),
      source.replace(run, `${run} --omit=dev`),
      source.replace(" && needs.preflight.result == 'success'", ''),
      source.replace('    needs: preflight', '    needs: []'),
    ]) {
      assert.throws(() => assertReleaseGate(mutated, publicationJob));
    }
  });
}

for (const [name, audit, now, expectedStatus, message, policy = allowlist] of [
  ['remaining approved advisory', auditFor('GHSA-mrhx-c7xc-m3w5', 'high', 'zod'), '2026-11-01', 0, /ALLOW expires 2026-12-31/],
  ['withdrawn advisory absent after deadline', { vulnerabilities: {} }, '2026-11-01', 0, /High\/critical advisories: 0/],
  ['braces advisory reappears', auditFor(), '2026-11-01', 1, /Unallowlisted high GHSA-VFJ7-8CJW-P6XM/],
  ['expired exception absent from audit', { vulnerabilities: {} }, '2026-11-01', 1, /Expired allowlist entry/, historicalAllowlist],
  ['different high advisory', auditFor('GHSA-aaaa-bbbb-cccc'), '2026-10-31', 1, /Unallowlisted high/],
  ['different critical advisory', auditFor('GHSA-aaaa-bbbb-cccc', 'critical'), '2026-10-31', 1, /Unallowlisted critical/],
  ['malformed audit', { error: { code: 'ECONNRESET' } }, '2026-10-31', 1, /Missing npm audit output/],
]) {
  test(`release validator process fails closed: ${name}`, (t) => {
    const dir = mkdtempSync(path.join(tmpdir(), 'qz-release-advisory-'));
    t.after(() => rmSync(dir, { recursive: true, force: true }));
    const auditPath = path.join(dir, 'audit.json');
    writeFileSync(auditPath, JSON.stringify(audit));
    const allowlistPath = path.join(dir, 'allowlist.json');
    writeFileSync(allowlistPath, JSON.stringify(policy));
    const result = spawnSync(process.execPath, [validator, '--audit-json', auditPath, '--allowlist', allowlistPath, '--now', now], {
      cwd: mobile,
      encoding: 'utf8',
    });
    assert.equal(result.error, undefined);
    assert.equal(result.status, expectedStatus, result.stdout + result.stderr);
    assert.match(result.stdout + result.stderr, message);
  });
}

test('release validator rejects missing audit output', () => {
  const result = spawnSync(process.execPath, [validator, '--audit-json', path.join(tmpdir(), 'qz-nonexistent-audit', 'audit.json')], {
    cwd: mobile,
    encoding: 'utf8',
  });
  assert.equal(result.status, 1);
  assert.match(result.stderr, /Missing npm audit output/);
});
