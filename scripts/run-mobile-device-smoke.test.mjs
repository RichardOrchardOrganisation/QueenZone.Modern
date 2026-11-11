// Guards the iOS false pass from run 41408627952: a fatal `set -u` error
// before Maestro started was reported as step success.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync, mkdtempSync, writeFileSync } from 'node:fs';
import { spawnSync } from 'node:child_process';
import os from 'node:os';
import path from 'node:path';

const script = readFileSync(new URL('./run-mobile-device-smoke.sh', import.meta.url), 'utf8');

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

test('Maestro invocation records preserve the original failure and recovery separately', () => {
  const dir = mkdtempSync(path.join(os.tmpdir(), 'qz-maestro-attempts-'));
  const result = spawnSync('bash', ['-c', `
set -uo pipefail
results_dir="$1"
maestro_console_log="$results_dir/maestro-console.log"
maestro_args=(test trial.yaml)
maestro_invocation=0
maestro() { return "$maestro_exit"; }
${extractFunction('run_maestro_once')}
maestro_exit=17
run_maestro_once
test "$?" = 17 || exit 1
maestro_exit=0
run_maestro_once
`, 'bash', dir], { encoding: 'utf8' });

  assert.equal(result.status, 0, result.stderr);
  assert.deepEqual(readFileSync(path.join(dir, 'harness.log'), 'utf8').trim().split('\n'), [
    'maestro_invocation=1 phase=start',
    'maestro_invocation=1 phase=end exit=17',
    'maestro_invocation=2 phase=start',
    'maestro_invocation=2 phase=end exit=0',
  ]);
});
