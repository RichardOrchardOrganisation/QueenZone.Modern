// Guards the iOS false pass from run 41408627952: a fatal `set -u` error
// before Maestro started was reported as step success.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { chmodSync, readFileSync, mkdtempSync, writeFileSync } from 'node:fs';
import { spawnSync } from 'node:child_process';
import os from 'node:os';
import path from 'node:path';

const script = readFileSync(new URL('./run-mobile-device-smoke.sh', import.meta.url), 'utf8');

const hasPgrep = spawnSync('bash', ['-c', 'command -v pgrep'], { encoding: 'utf8' }).status === 0;

function bash(src) {
  return spawnSync('bash', ['-c', src], { encoding: 'utf8' });
}

function extractFunction(name) {
  const start = script.indexOf(`${name}() {`);
  assert.ok(start >= 0, `${name} not found`);
  const end = script.indexOf('\n}\n', start);
  return script.slice(start, end + 3);
}

test('no bare "${proof_flows[*]}" expansion (unbound on macOS bash 3.2 when empty)', () => {
  assert.doesNotMatch(script, /[^+]\$\{proof_flows\[\*\]\}[^}]/);
  const line = script.split('\n').find((l) => l.startsWith('echo "Running Maestro'));
  const r = bash(`set -euo pipefail; flow=f; proof_flows=(); ${line}`);
  assert.equal(r.status, 0, r.stderr);
  assert.match(r.stdout, /Running Maestro \(f\)\./);
});

test('EXIT trap re-raises the original failing status', () => {
  const cleanup = extractFunction('cleanup');
  const stubs = 'stop_android_side_processes(){ :; }; collect_diagnostics(){ :; }; host_pid="";';
  const r = bash(`set -euo pipefail; ${stubs}\n${cleanup}\ntrap cleanup EXIT; false`);
  assert.equal(r.status, 1);
  const ok = bash(`set -euo pipefail; ${stubs}\n${cleanup}\ntrap cleanup EXIT; true`);
  assert.equal(ok.status, 0);
});

test('zero JUnit testcases means zero flows ran', () => {
  const fn = extractFunction('maestro_junit_testcases');
  const dir = mkdtempSync(path.join(os.tmpdir(), 'qz-junit-'));
  const empty = path.join(dir, 'empty.xml');
  const two = path.join(dir, 'two.xml');
  writeFileSync(empty, '<testsuites><testsuite name="x" tests="0"></testsuite></testsuites>');
  writeFileSync(two, '<testsuite><testcase name="a"></testcase><testcase name="b"/></testsuite>');
  const count = (f) => bash(`set -euo pipefail; ${fn}\nmaestro_junit_testcases "${f}"`).stdout.trim();
  assert.equal(count(path.join(dir, 'missing.xml')), '0');
  assert.equal(count(empty), '0');
  assert.equal(count(two), '2');
  assert.match(script, /records zero flows; the app never launched/);
});

function fakeBin(files) {
  const dir = mkdtempSync(path.join(os.tmpdir(), 'qz-bin-'));
  for (const [name, body] of Object.entries(files)) {
    const file = path.join(dir, name);
    writeFileSync(file, `#!/usr/bin/env bash\n${body}\n`);
    chmodSync(file, 0o755);
  }
  return dir;
}

test('cleanup stops the whole host process tree, not just the dotnet run wrapper', { skip: !hasPgrep && 'pgrep unavailable' }, () => {
  const fn = extractFunction('stop_process_tree');
  // Parent wrapper with a long-lived child, like `dotnet run` -> QueenZone.Web.
  const r = bash(`set -euo pipefail; ${fn}
bash -c 'sleep 300 & echo $! > "$1"; wait' _ "$TMPDIR_CHILD" &
parent=$!
for _ in 1 2 3 4 5 6 7 8 9 10; do [ -s "$TMPDIR_CHILD" ] && break; sleep 0.2; done
child=$(cat "$TMPDIR_CHILD")
stop_process_tree "$parent"
if kill -0 "$child" 2>/dev/null; then echo child-alive; exit 1; fi
echo stopped`.replace(/\$TMPDIR_CHILD/g, path.join(mkdtempSync(path.join(os.tmpdir(), 'qz-tree-')), 'child.pid')));
  assert.equal(r.status, 0, r.stdout + r.stderr);
  assert.match(r.stdout, /stopped/);
  assert.match(extractFunction('cleanup'), /stop_process_tree "\$host_pid"/);
});

test('host start refuses a port that is already listening and names the owner', () => {
  const fn = extractFunction('assert_port_free');
  const busy = fakeBin({ lsof: 'echo "COMMAND PID USER"; echo "dotnet 4242 runner TCP 127.0.0.1:5098 (LISTEN)"' });
  const free = fakeBin({ lsof: 'exit 1' });
  const run = (bin) => bash(`set -euo pipefail; PATH="${bin}:$PATH"; ${fn}\nassert_port_free 5098`);
  const b = run(busy);
  assert.equal(b.status, 1);
  assert.match(b.stderr, /Port 5098 is already in use/);
  assert.match(b.stderr, /dotnet 4242/);
  assert.equal(run(free).status, 0);
  assert.match(extractFunction('start_host'), /assert_port_free "\$port"/);
});

test('local iOS runs refuse to guess between several booted Simulators', () => {
  const fn = extractFunction('assert_single_ios_target');
  const two = fakeBin({ xcrun: 'echo "-- iOS 26.1 --"; echo "    iPhone 16 (AAAA) (Booted)"; echo "    QueenZone-CI-iOS-1-job (BBBB) (Booted)"' });
  const one = fakeBin({ xcrun: 'echo "-- iOS 26.1 --"; echo "    iPhone 16 (AAAA) (Booted)"' });
  const run = (bin, udid = '') => bash(`set -euo pipefail; PATH="${bin}:$PATH"; IOS_SIM_UDID="${udid}"; ${fn}\nassert_single_ios_target`);
  const ambiguous = run(two);
  assert.equal(ambiguous.status, 1);
  assert.match(ambiguous.stderr, /More than one iOS Simulator is booted/);
  assert.equal(run(one).status, 0);
  assert.equal(run(two, 'AAAA').status, 0);
  assert.match(script, /assert_single_ios_target\n\s+echo "Installing \$app"/);
});

test('failure signature lands in the job summary', () => {
  const fn = extractFunction('write_failure_summary');
  const dir = mkdtempSync(path.join(os.tmpdir(), 'qz-summary-'));
  const summary = path.join(dir, 'summary.md');
  const sig = 'Maestro failing flow: QueenZone PR-bound device smoke\nMaestro failing selector: Element not found: Id matching regex: home-search';
  const r = bash(`set -euo pipefail; GITHUB_STEP_SUMMARY="${summary}"; ${fn}\nwrite_failure_summary ios smoke selector_miss "${sig}"`);
  assert.equal(r.status, 0, r.stderr);
  const md = readFileSync(summary, 'utf8');
  assert.match(md, /### Maestro failure: ios smoke/);
  assert.match(md, /- Class: `selector_miss`/);
  assert.match(md, /- Flow: `QueenZone PR-bound device smoke`/);
  assert.match(md, /- Selector: `Element not found: Id matching regex: home-search`/);
  const none = bash(`set -euo pipefail; GITHUB_STEP_SUMMARY="${summary}"; ${fn}\nwrite_failure_summary android journeys transport_death ""`);
  assert.equal(none.status, 0, none.stderr);
  assert.match(readFileSync(summary, 'utf8'), /No JUnit failure recorded/);
  const unset = bash(`set -euo pipefail; unset GITHUB_STEP_SUMMARY; ${fn}\nwrite_failure_summary ios smoke x y`);
  assert.equal(unset.status, 0, unset.stderr);
});
