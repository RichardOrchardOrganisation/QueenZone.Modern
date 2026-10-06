import { test } from 'node:test';
import assert from 'node:assert/strict';
import { mkdtempSync, readFileSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import {
  ANDROID_APP_PACKAGE,
  APP_CRASH,
  NO_CRASH,
  classifyAndroidAppCrash,
  classifyFromFiles,
  formatAppCrashReport,
  formatAppCrashSummary,
  mentionsOurPackage,
  parsePackageCrash,
  stripLogcatPrefix,
  writeCrashArtifacts,
} from './classify-android-app-crash.mjs';

const here = path.dirname(fileURLToPath(import.meta.url));
const fixtures = path.join(here, 'fixtures', 'android-app-crash');
const fatalLogcat = readFileSync(path.join(fixtures, 'fatal-signal-queenzone.logcat'), 'utf8');
const noCrashLogcat = readFileSync(path.join(fixtures, 'no-crash.logcat'), 'utf8');
const otherPackageLogcat = readFileSync(path.join(fixtures, 'other-package-fatal.logcat'), 'utf8');
const workflow = readFileSync(new URL('../.github/workflows/mobile-device-smoke.yml', import.meta.url), 'utf8');
const smokeScript = readFileSync(new URL('./run-mobile-device-smoke.sh', import.meta.url), 'utf8');

test('strips threadtime prefixes used by adb logcat -v threadtime', () => {
  const line =
    '10-05 23:10:02.407  5154  5154 F libc    : Fatal signal 11 (SIGSEGV), code 2 (SEGV_ACCERR), fault addr 0x7e0c76b0e000 in tid 5154 (ueenzone.mobile), pid 5154 (ueenzone.mobile)';
  assert.match(stripLogcatPrefix(line), /^Fatal signal 11 \(SIGSEGV\)/);
});

test('fixture Fatal signal for our package is APP_CRASH with signal and frames', () => {
  const result = classifyFromFiles({ logcat: path.join(fixtures, 'fatal-signal-queenzone.logcat') });
  assert.equal(result.class, APP_CRASH);
  assert.equal(result.package, ANDROID_APP_PACKAGE);
  assert.equal(result.signal, '11 (SIGSEGV)');
  assert.equal(result.faultCode, '2 (SEGV_ACCERR)');
  assert.equal(result.source, 'logcat');
  assert.ok(result.frames.length >= 4, 'expected top native frames');
  assert.match(result.frames[0], /android_unsafe_frame_pointer_chase/);
  assert.match(result.frames[2], /GuardedPoolAllocator::deallocate/);
  assert.match(result.frames.join('\n'), /libhermesvm\.so/);
  assert.match(formatAppCrashReport(result), /Maestro failing cause: APP_CRASH/);
  assert.match(formatAppCrashSummary(result), /## Android APP_CRASH/);
});

test('fixture without Fatal signal is not a crash, even when the package is present', () => {
  const result = classifyFromFiles({ logcat: path.join(fixtures, 'no-crash.logcat') });
  assert.equal(result.class, NO_CRASH);
  assert.equal(result.signal, null);
  assert.deepEqual(result.frames, []);
  assert.equal(formatAppCrashReport(result), '');
  assert.ok(mentionsOurPackage(noCrashLogcat));
});

test('Fatal signal for another package is not our APP_CRASH', () => {
  const parsed = parsePackageCrash(otherPackageLogcat);
  assert.equal(parsed, null);
  const result = classifyAndroidAppCrash({ logcat: otherPackageLogcat, processAlive: true });
  assert.equal(result.class, NO_CRASH);
});

test('process gone on a live device is APP_CRASH even without logcat frames', () => {
  const result = classifyAndroidAppCrash({ logcat: noCrashLogcat, processAlive: false });
  assert.equal(result.class, APP_CRASH);
  assert.equal(result.signal, 'process_gone');
  assert.equal(result.source, 'pidof');
  assert.deepEqual(result.frames, []);
});

test('unknown process state without a package crash is not APP_CRASH', () => {
  const result = classifyAndroidAppCrash({ logcat: noCrashLogcat, processAlive: 'unknown' });
  assert.equal(result.class, NO_CRASH);
});

test('crash buffer wins over a clean main logcat', () => {
  const result = classifyAndroidAppCrash({
    logcat: noCrashLogcat,
    crashBuffer: fatalLogcat,
    processAlive: true,
  });
  assert.equal(result.class, APP_CRASH);
  assert.equal(result.source, 'crash-buffer');
  assert.equal(result.signal, '11 (SIGSEGV)');
});

test('writeCrashArtifacts writes the marker, frames, and summary used by CI', () => {
  const directory = mkdtempSync(path.join(tmpdir(), 'qz-app-crash-'));
  try {
    const result = classifyAndroidAppCrash({ logcat: fatalLogcat });
    const paths = writeCrashArtifacts(result, directory);
    assert.ok(paths);
    const marker = readFileSync(paths.markerPath, 'utf8');
    assert.match(marker, /^class=APP_CRASH$/m);
    assert.match(marker, /^signal=11 \(SIGSEGV\)$/m);
    assert.match(marker, /^package=org\.queenzone\.mobile$/m);
    const frames = readFileSync(paths.framesPath, 'utf8');
    assert.match(frames, /android_unsafe_frame_pointer_chase/);
    const summary = readFileSync(paths.summaryPath, 'utf8');
    assert.match(summary, /## Android APP_CRASH/);
  } finally {
    rmSync(directory, { recursive: true, force: true });
  }
});

test('smoke script classifies APP_CRASH before a selector miss and does not retry it', () => {
  assert.match(smokeScript, /classify-android-app-crash\.mjs/);
  assert.match(smokeScript, /android-app-crash/);
  assert.match(smokeScript, /android_failure_class=APP_CRASH|android_failure_class="APP_CRASH"/);
  assert.match(smokeScript, /logcat -b crash/);
  assert.match(smokeScript, /! android_app_crashed \\\s*&& android_transport_died/);
});

test('journeys and smoke jobs report APP_CRASH separately and never retry it', () => {
  assert.match(workflow, /android-app-crash/);
  assert.match(workflow, /APP_CRASH/);
  assert.match(workflow, /Not retrying/);
  const classifyBlocks = workflow.split('Classify Android');
  assert.ok(classifyBlocks.length >= 3, 'expected smoke and journeys classify steps');
  for (const block of classifyBlocks.slice(1)) {
    const retryTrue = block.indexOf('retry=true');
    const crashMarker = block.indexOf('android-app-crash');
    assert.ok(crashMarker !== -1, 'classify step must inspect the crash marker');
    assert.ok(crashMarker < retryTrue, 'APP_CRASH must be checked before enabling a transport retry');
  }
});
