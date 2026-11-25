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

const maestroDriverTimeoutLine =
  'iOS driver not ready in time, consider increasing timeout by configuring MAESTRO_DRIVER_STARTUP_TIMEOUT env variable';

test('iOS driver timeout matcher uses the real Maestro console text', () => {
  const fn = extractFunction('ios_driver_failed_before_flow');
  assert.match(script, /ios_driver_timeout_re="iOS driver not ready in time"/);
  const dir = mkdtempSync(path.join(os.tmpdir(), 'qz-ios-driver-'));
  writeFileSync(path.join(dir, 'maestro-console.log'), `${maestroDriverTimeoutLine}\n`);
  const hit = bash(`set -euo pipefail; results_dir="${dir}"; maestro_console_log="${dir}/maestro-console.log"; ios_driver_timeout_re="iOS driver not ready in time"; ${fn}\nios_driver_failed_before_flow`);
  assert.equal(hit.status, 0, hit.stderr);
  writeFileSync(path.join(dir, 'junit.xml'), '<testsuite><testcase name="a"></testcase></testsuite>');
  const miss = bash(`set -euo pipefail; results_dir="${dir}"; maestro_console_log="${dir}/maestro-console.log"; ios_driver_timeout_re="iOS driver not ready in time"; ${fn}\nios_driver_failed_before_flow`);
  assert.equal(miss.status, 1);
});

test('ios leftover driver matcher requires the job UDID', () => {
  const fn = extractFunction('ios_driver_process_is_leftover');
  const run = (args, udid = 'AAAA') => bash(`set -euo pipefail; ${fn}\nios_driver_process_is_leftover ${JSON.stringify(args)} "${udid}"`);
  assert.equal(run('xcodebuild test-without-building -destination id=AAAA maestro-ios-driver').status, 0);
  assert.equal(run('xcuitest.installer.LocalXCTestInstaller -destination id=AAAA').status, 0);
  assert.equal(run('xcuitest.installer.LocalXCTestInstaller').status, 1);
  assert.equal(run('xcodebuild -workspace QueenZone.xcworkspace -scheme QueenZone').status, 1);
  assert.equal(run('xcodebuild -destination id=AAAA build', 'AAAA').status, 0);
  assert.equal(run('xcodebuild test-without-building -destination id=BBBB maestro-ios-driver', 'AAAA').status, 1);
  assert.equal(run('maestro --device BBBB test flow.yaml', 'AAAA').status, 1);
});

test('write_ios_driver_marker is infra and not a selector miss', () => {
  const fns = ['ios_sim_state', 'write_ios_driver_marker'].map(extractFunction).join('\n');
  const dir = mkdtempSync(path.join(os.tmpdir(), 'qz-ios-marker-'));
  const summary = path.join(dir, 'summary.md');
  const bin = fakeBin({
    xcrun: 'echo "    QueenZone-CI-iOS (AAAA-BBBB) (Booted)"',
  });
  const r = bash(`set -euo pipefail; PATH="${bin}:$PATH"; results_dir="${dir}"; IOS_SIM_UDID="AAAA-BBBB"; GITHUB_STEP_SUMMARY="${summary}"; ${fns}\nwrite_ios_driver_marker "driver-timeout-first"`);
  assert.equal(r.status, 0, r.stderr);
  assert.match(r.stderr, /infra: iOS smoke driver_failure: driver-timeout-first/);
  assert.doesNotMatch(r.stderr, /selector or assertion miss/);
  const marker = readFileSync(path.join(dir, 'ios-driver-failure'), 'utf8');
  assert.match(marker, /^class=ios_driver_failure$/m);
  assert.match(marker, /^reason=driver-timeout-first$/m);
  assert.match(marker, /^sim_state=Booted$/m);
  assert.match(readFileSync(summary, 'utf8'), /### infra: iOS smoke driver startup failure/);
});

function iosRecoverHarness(simState, { shutdownNoop = false } = {}) {
  const dir = mkdtempSync(path.join(os.tmpdir(), 'qz-ios-recover-'));
  writeFileSync(path.join(dir, 'sim-state'), simState);
  const bin = fakeBin({
    xcrun: `
echo "$*" >> "${dir}/xcrun-args"
case "$*" in
  "simctl list devices")
    echo "    QueenZone-CI-iOS (AAAA-BBBB-CCCC-DDDD-EEEEEEEEEEEE) ($(cat "${dir}/sim-state"))"
    ;;
  simctl\\ shutdown\\ *)
    if [ "${shutdownNoop ? '1' : '0'}" = "1" ]; then
      echo "shutdown ignored"
      exit 0
    fi
    echo Shutdown > "${dir}/sim-state"
    ;;
  simctl\\ boot\\ *)
    echo Booted > "${dir}/sim-state"
    ;;
  simctl\\ bootstatus\\ *)
    if grep -q Booted "${dir}/sim-state"; then
      echo "Device already booted, nothing to do."
    fi
    ;;
  *)
    echo "unexpected xcrun $*" >&2
    exit 3
    ;;
esac
`,
  });
  const fns = ['ios_sim_state', 'wait_ios_sim_shutdown', 'recover_ios_simulator_for_driver_retry'].map(extractFunction).join('\n');
  const run = () => bash(`set -euo pipefail; PATH="${bin}:$PATH"; QZ_IOS_SHUTDOWN_WAIT_SECONDS=1; kill_leftover_ios_driver_processes(){ echo killed-leftover; }; ${fns}\nrecover_ios_simulator_for_driver_retry "AAAA-BBBB-CCCC-DDDD-EEEEEEEEEEEE"`);
  return { dir, run };
}

test('iOS driver recover boots only after simctl reports Shutdown', () => {
  const s = iosRecoverHarness('Booted');
  const r = s.run();
  assert.equal(r.status, 0, r.stdout + r.stderr);
  assert.match(r.stdout, /killed-leftover/);
  assert.match(r.stdout, /reached Shutdown/);
  const args = readFileSync(path.join(s.dir, 'xcrun-args'), 'utf8');
  assert.match(args, /simctl shutdown AAAA-BBBB-CCCC-DDDD-EEEEEEEEEEEE/);
  assert.match(args, /simctl boot AAAA-BBBB-CCCC-DDDD-EEEEEEEEEEEE/);
  assert.match(args, /simctl bootstatus AAAA-BBBB-CCCC-DDDD-EEEEEEEEEEEE -b/);
  assert.ok(args.indexOf('simctl shutdown') < args.indexOf('simctl boot'));
});

test('iOS driver recover refuses attempt 2 on a leftover Booted Simulator', () => {
  const s = iosRecoverHarness('Booted', { shutdownNoop: true });
  const r = s.run();
  assert.equal(r.status, 1, r.stdout + r.stderr);
  assert.match(r.stderr, /Not starting Maestro attempt 2 on a leftover Booted Simulator/);
  const args = readFileSync(path.join(s.dir, 'xcrun-args'), 'utf8');
  assert.match(args, /simctl shutdown/);
  assert.doesNotMatch(args, /simctl boot /);
  assert.doesNotMatch(args, /already booted/);
});

test('iOS driver timeout is classified as infra instead of selector_miss', () => {
  assert.match(script, /android_failure_class="ios_driver_failure"/);
  assert.match(script, /The XCTest driver never started; this is not a selector or assertion miss/);
  assert.match(script, /infra: iOS smoke driver startup failure/);
  assert.match(script, /MAESTRO_DRIVER_STARTUP_TIMEOUT:-240000/);
  assert.match(script, /recover_ios_simulator_for_driver_retry/);
  assert.match(script, /write_ios_driver_marker "driver-timeout-first"/);
  assert.match(script, /write_ios_driver_marker "shutdown-still-booted"/);
  const driver = script.indexOf('android_failure_class="ios_driver_failure"');
  const selector = script.lastIndexOf('Maestro failing cause: selector or assertion miss');
  assert.ok(driver !== -1 && selector !== -1 && driver < selector);
});
