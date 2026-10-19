import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { execFileSync, spawnSync } from 'node:child_process';
import { mkdtempSync, readFileSync, existsSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { test } from 'node:test';
import { GATES, evaluateRun } from './check-gates.mjs';
import { resolveDevTestedSha } from './write-gate-proof.mjs';

const writer = fileURLToPath(new URL('./write-gate-proof.mjs', import.meta.url));

// Real local Git repositories exercise resolution and shallow-history failures;
// these synthetic proofs are never uploaded as hosted DEV evidence.
test('DEV proof credits only the served commit in complete and shallow histories', () => {
  const root = mkdtempSync(path.join(tmpdir(), 'qz-dev-proof-'));
  const git = (...args) => execFileSync('git', args, { cwd: root, encoding: 'utf8', stdio: ['ignore', 'pipe', 'pipe'] }).trim();
  try {
    git('init', '-b', 'main');
    git('config', 'user.name', 'Proof test');
    git('config', 'user.email', 'proof@example.invalid');
    git('commit', '--allow-empty', '-m', 'served');
    const served = git('rev-parse', 'HEAD');
    assert.equal(resolveDevTestedSha(served, ` ${served.toUpperCase()} `), served);
    git('commit', '--allow-empty', '-m', 'tip');
    const tip = git('rev-parse', 'HEAD');
    // Hex-named refs must not redirect an object stamp to a different commit.
    git('branch', served.slice(0, 7), tip);
    git('checkout', '-b', 'side', served);
    git('commit', '--allow-empty', '-m', 'unrelated');
    const side = git('rev-parse', 'HEAD');
    git('checkout', 'main');
    const run = (stamp, cwd = root, sha = tip) => {
      const out = path.join(cwd, 'proof.json');
      rmSync(out, { force: true });
      const result = spawnSync(process.execPath, [writer, '--stamp', stamp, '--sha', sha, '--out', out], {
        cwd, encoding: 'utf8', env: { ...process.env, GITHUB_STEP_SUMMARY: path.join(root, 'summary.md') },
      });
      return { ...result, out };
    };
    for (const stamp of [tip.slice(0, 7), tip, ` ${tip.toUpperCase()} `]) {
      const result = run(stamp);
      assert.equal(result.status, 0, result.stderr);
      assert.deepEqual(JSON.parse(readFileSync(result.out)), { tested_sha: tip, full_suite: true });
      assert.match(result.stdout, /tip proof written/);
    }
    for (const stamp of [served.slice(0, 7), served]) {
      const result = run(stamp);
      assert.equal(result.status, 0, result.stderr);
      assert.deepEqual(JSON.parse(readFileSync(result.out)), { tested_sha: served, full_suite: true });
      assert.match(result.stdout, /no tip proof/);
      assert.match(readFileSync(path.join(root, 'summary.md'), 'utf8'), /no tip proof/);
    }
    const blob = execFileSync('git', ['hash-object', '-w', '--stdin'], { cwd: root, input: 'not a commit', encoding: 'utf8' }).trim();
    for (const stamp of [blob, blob.slice(0, 7), '', 'abcdef0', '--help', 'abc', `x${served.slice(0, 7)}`, `${served.slice(0, 7)}x`, side, side.slice(0, 7)]) {
      const result = run(stamp);
      assert.notEqual(result.status, 0, stamp);
      assert.equal(existsSync(result.out), false, stamp);
      if (stamp === '') assert.match(result.stderr, /missing data-build-version/);
      else if (['--help', 'abc', `x${served.slice(0, 7)}`, `${served.slice(0, 7)}x`].includes(stamp)) assert.match(result.stderr, /Invalid DEV build stamp/);
    }
    // A deploy racing ahead of the run must fail closed, never credit its old tip.
    const ahead = run(tip.slice(0, 7), root, served);
    assert.notEqual(ahead.status, 0);
    assert.equal(existsSync(ahead.out), false);
    // Build a real seven-hex collision deterministically, without a large history.
    const tree = git('rev-parse', 'HEAD^{tree}');
    const seen = new Map();
    let collision;
    for (let i = 0; i < 200_000; i += 1) {
      const body = `tree ${tree}\nauthor Proof <proof@example.invalid> 1 +0000\ncommitter Proof <proof@example.invalid> 1 +0000\n\ncollision ${i}\n`;
      const hash = createHash('sha1').update(`commit ${Buffer.byteLength(body)}\0${body}`).digest('hex');
      const prefix = hash.slice(0, 7);
      if (seen.has(prefix)) { collision = [prefix, seen.get(prefix), body]; break; }
      seen.set(prefix, body);
    }
    assert.ok(collision, 'fixture must contain an ambiguous object prefix');
    for (const body of collision.slice(1)) execFileSync('git', ['hash-object', '-t', 'commit', '-w', '--stdin'], { cwd: root, input: body, stdio: ['pipe', 'pipe', 'pipe'] });
    const collisionShas = collision.slice(1).map(body => createHash('sha1').update(`commit ${Buffer.byteLength(body)}\0${body}`).digest('hex'));
    // Both colliding commits are ancestors: picking either would wrongly pass.
    const mergeTip = git('commit-tree', tree, '-p', collisionShas[0], '-p', collisionShas[1], '-p', tip, '-m', 'collision ancestors');
    const ambiguous = run(collision[0], root, mergeTip);
    assert.notEqual(ambiguous.status, 0);
    assert.equal(existsSync(ambiguous.out), false);
    const shallow = path.join(root, 'shallow');
    git('clone', '--depth=1', `file://${root}`, shallow);
    const missing = run(served.slice(0, 7), shallow);
    assert.notEqual(missing.status, 0);
    assert.match(missing.stderr, /Unknown DEV build stamp/);
    assert.equal(existsSync(missing.out), false);
    execFileSync('git', ['fetch', '--unshallow', 'origin'], { cwd: shallow, stdio: 'pipe' });
    const restored = run(served.slice(0, 7), shallow);
    assert.equal(restored.status, 0, restored.stderr);
    assert.equal(JSON.parse(readFileSync(restored.out)).tested_sha, served);
  } finally {
    rmSync(root, { recursive: true, force: true });
  }
});

test('every production gate rejects an older served proof even with a green exact-tip job', async () => {
  const tip = 'a'.repeat(40);
  const served = 'b'.repeat(40);
  const context = { maxAgeHours: 72, now: () => Date.parse('2026-10-19T12:00:00Z') };
  const run = {
    id: 1, head_sha: tip,
    jobs: [{ name: 'gate-proof', status: 'completed', conclusion: 'success', completed_at: '2026-10-19T11:00:00Z' }],
    proof: { tested_sha: served, full_suite: true },
  };
  assert.equal(GATES.length, 3);
  for (const gate of GATES) {
    const stale = await evaluateRun(gate, run, tip, context);
    assert.equal(stale.ok, false, gate.id);
    assert.match(stale.error, /tested_sha/);
    assert.equal((await evaluateRun(gate, { ...run, proof: { tested_sha: tip, full_suite: true } }, tip, context)).ok, true);
  }
});
