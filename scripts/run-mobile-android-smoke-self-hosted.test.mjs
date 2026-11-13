import { test } from 'node:test';
import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { chmodSync, existsSync, mkdirSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

// #2503: self-hosted Android smoke must provision the API 36 arm64 system
// image before avdmanager, and label a boot/AVD failure as infra.
const scriptsDir = path.dirname(fileURLToPath(import.meta.url));
const repoRoot = path.resolve(scriptsDir, '..');
const scriptName = 'run-mobile-android-smoke-self-hosted.sh';
const preinstallName = 'preinstall-android-emulator.sh';

function writeExec(file, body) {
  mkdirSync(path.dirname(file), { recursive: true });
  writeFileSync(file, body);
  chmodSync(file, 0o755);
}

function setup() {
  const tmp = mkdtempSync(path.join(os.tmpdir(), 'qz-android-smoke-'));
  const root = path.join(tmp, 'repo');
  const sdk = path.join(tmp, 'sdk');
  const home = path.join(tmp, 'home');
  const bin = path.join(tmp, 'bin');
  const state = path.join(tmp, 'state');
  const apk = path.join(tmp, 'app-release.apk');
  for (const dir of [root, sdk, home, bin, state, path.join(root, 'scripts'), path.join(root, 'src/QueenZone.Mobile')]) {
    mkdirSync(dir, { recursive: true });
  }
  writeFileSync(apk, 'apk');
  writeFileSync(path.join(root, 'scripts', scriptName), readFileSync(path.join(scriptsDir, scriptName)));
  chmodSync(path.join(root, 'scripts', scriptName), 0o755);
  writeFileSync(path.join(root, 'scripts', preinstallName), readFileSync(path.join(scriptsDir, preinstallName)));
  chmodSync(path.join(root, 'scripts', preinstallName), 0o755);
  writeExec(
    path.join(root, 'scripts/run-mobile-device-smoke.sh'),
    `#!/usr/bin/env bash
echo "maestro-stub: $*"
exit 0
`,
  );

  writeExec(
    path.join(bin, 'sdkmanager'),
    `#!/usr/bin/env bash
set -u
sdk_root=""
for arg in "\$@"; do
  case "\$arg" in
    --sdk_root=*) sdk_root="\${arg#--sdk_root=}" ;;
    --licenses) echo "licenses accepted" >> "${state}/calls"; exit 0 ;;
  esac
done
echo "\$*" >> "${state}/calls"
img="\$sdk_root/system-images/android-36/google_apis/arm64-v8a"
if [ "\${FAKE_IMAGE_MODE:-ok}" = "missing" ]; then
  mkdir -p "\$img"
  echo "partial" > "\$img/partial.bin"
  exit 0
fi
mkdir -p "\$img"
printf 'Pkg.Desc=Google APIs ARM 64 v8a System Image\\nAndroidVersion.ApiLevel=36\\n' > "\$img/source.properties"
echo "system-image" > "\$img/system.img"
exit 0
`,
  );

  writeExec(
    path.join(bin, 'avdmanager'),
    `#!/usr/bin/env bash
echo "avdmanager $*" >> "${state}/calls"
echo "created AVD"
exit 0
`,
  );

  writeExec(
    path.join(bin, 'adb'),
    `#!/usr/bin/env bash
echo "adb $*" >> "${state}/calls"
case "\$1" in
  devices) exit 0 ;;
  emu)
    rm -f "${state}/emulator-alive"
    exit 0
    ;;
  get-state)
    if [ -f "${state}/emulator-alive" ]; then echo device; exit 0; fi
    echo offline
    exit 1
    ;;
  shell)
    if [ -f "${state}/emulator-alive" ] && [ "\${2:-}" = "getprop" ]; then echo 1; exit 0; fi
    exit 1
    ;;
  *) exit 0 ;;
esac
`,
  );

  const emulatorBodyOk = `#!/usr/bin/env bash
echo "emulator $*" >> "${state}/calls"
if [ "\$1" = "-list-avds" ]; then
  if [ -f "${state}/avds" ]; then cat "${state}/avds"; fi
  exit 0
fi
# Stay alive long enough for the boot loop to see adb report ready.
touch "${state}/emulator-alive"
sleep 30 &
sleep_pid=$!
trap 'rm -f "${state}/emulator-alive"; kill "$sleep_pid" 2>/dev/null || true' EXIT
wait "$sleep_pid"
`;

  const emulatorBodyBootFail = `#!/usr/bin/env bash
echo "emulator $*" >> "${state}/calls"
if [ "\$1" = "-list-avds" ]; then
  if [ -f "${state}/avds" ]; then cat "${state}/avds"; fi
  exit 0
fi
echo "INFO         | Android emulator version 37.1.11.0 (build_id 15917651) (CL:N/A)"
echo "INFO         | Found systemPath $sdk/system-images/android-36/google_apis/arm64-v8a/"
echo "ERROR        | No initial system image for this configuration!"
exit 1
`;

  writeExec(path.join(bin, 'emulator'), emulatorBodyOk);

  const env = {
    ...process.env,
    HOME: home,
    PATH: `${bin}:${process.env.PATH}`,
    ANDROID_HOME: sdk,
    ANDROID_SDK_ROOT: sdk,
    ANDROID_EMULATOR_INSTALL_BACKOFF_SECONDS: '0',
    QZ_SDKMANAGER: path.join(bin, 'sdkmanager'),
    FAKE_IMAGE_MODE: 'ok',
  };

  const run = () => spawnSync('bash', [path.join(root, 'scripts', scriptName), apk], {
    cwd: root,
    encoding: 'utf8',
    env,
    timeout: 20000,
  });

  return {
    tmp,
    root,
    sdk,
    home,
    bin,
    state,
    env,
    run,
    writeEmulator: (body) => writeExec(path.join(bin, 'emulator'), body),
    emulatorOk: emulatorBodyOk,
    emulatorBootFail: emulatorBodyBootFail,
    calls: () => (existsSync(path.join(state, 'calls')) ? readFileSync(path.join(state, 'calls'), 'utf8') : ''),
    imageDir: path.join(sdk, 'system-images/android-36/google_apis/arm64-v8a'),
    results: path.join(root, 'src/QueenZone.Mobile/maestro-results'),
    cleanup: () => rmSync(tmp, { recursive: true, force: true }),
  };
}

test('installs the API 36 arm64 system image, verifies system.img, then creates the AVD', () => {
  const s = setup();
  try {
    const r = s.run();
    assert.equal(r.status, 0, r.stdout + r.stderr);
    assert.match(s.calls(), /--install system-images;android-36;google_apis;arm64-v8a/);
    assert.doesNotMatch(s.calls(), /--install emulator system-images/);
    assert.equal(existsSync(path.join(s.imageDir, 'system.img')), true);
    assert.match(s.calls(), /avdmanager create avd/);
    assert.match(s.calls(), /--package system-images;android-36;google_apis;arm64-v8a/);
    assert.equal(existsSync(path.join(s.results, 'android-boot-failure')), false);
    assert.equal(existsSync(path.join(s.results, 'android-install-failure')), false);
  } finally {
    s.cleanup();
  }
});

test('skips sdkmanager install when the system image is already complete', () => {
  const s = setup();
  try {
    mkdirSync(s.imageDir, { recursive: true });
    writeFileSync(path.join(s.imageDir, 'source.properties'), 'AndroidVersion.ApiLevel=36\n');
    writeFileSync(path.join(s.imageDir, 'system.img'), 'system-image');
    const r = s.run();
    assert.equal(r.status, 0, r.stdout + r.stderr);
    assert.match(r.stdout, /already installed/);
    assert.doesNotMatch(s.calls(), /--install system-images;android-36/);
  } finally {
    s.cleanup();
  }
});

test('labels an exhausted system image install as install failure and does not create the AVD', () => {
  const s = setup();
  s.env.FAKE_IMAGE_MODE = 'missing';
  try {
    const r = s.run();
    assert.equal(r.status, 1, r.stdout + r.stderr);
    assert.match(r.stderr, /Android emulator install failure/);
    assert.match(r.stderr, /Not a selector or assertion miss/);
    const marker = readFileSync(path.join(s.results, 'android-install-failure'), 'utf8');
    assert.match(marker, /^class=install_failure$/m);
    assert.match(marker, /system-images;android-36;google_apis;arm64-v8a/);
    assert.equal(existsSync(path.join(s.results, 'android-boot-failure')), false);
    assert.doesNotMatch(s.calls(), /avdmanager create avd/);
  } finally {
    s.cleanup();
  }
});

test('labels an emulator exit with no initial system image as infra and writes android-boot-failure', () => {
  const s = setup();
  try {
    s.writeEmulator(s.emulatorBootFail);
    const r = s.run();
    assert.equal(r.status, 1, r.stdout + r.stderr);
    assert.match(r.stderr, /infra: Android smoke boot_failure/);
    assert.match(r.stderr, /No initial system image for this configuration/);
    const marker = readFileSync(path.join(s.results, 'android-boot-failure'), 'utf8');
    assert.match(marker, /^class=boot_failure$/m);
    assert.match(readFileSync(path.join(s.results, 'emulator-self-hosted.log'), 'utf8'), /No initial system image/);
    assert.doesNotMatch(r.stderr, /selector or assertion miss/);
  } finally {
    s.cleanup();
  }
});
