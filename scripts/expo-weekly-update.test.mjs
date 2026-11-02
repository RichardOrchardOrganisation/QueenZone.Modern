import { mkdtempSync, readFileSync, writeFileSync } from 'node:fs';
import path from 'node:path';
import { tmpdir } from 'node:os';
import { fileURLToPath } from 'node:url';
import { test } from 'node:test';
import assert from 'node:assert/strict';
import {
  PINNED_EXPO_MODULES_CORE,
  TOKEN_SECRET_NAME,
  WEEKLY_BRANCH,
  WORKFLOW_PATH,
  buildWeeklyPrBody,
  changedHigherBarPackages,
  compareSemver,
  inspectPin,
  inspectPinFromDir,
  isHigherBarPackage,
  isRangeNewerThanPin,
  lockPackageName,
  lockfileVersionChanges,
  main,
  nestedExpoModulesCoreKeys,
  packageJsonVersionChanges,
  parseArgs,
  parseSemver,
} from './expo-weekly-update.mjs';

const repoRoot = fileURLToPath(new URL('..', import.meta.url));
const workflow = readFileSync(path.join(repoRoot, WORKFLOW_PATH), 'utf8');
const dependabot = readFileSync(path.join(repoRoot, '.github/dependabot.yml'), 'utf8');
const ci = readFileSync(path.join(repoRoot, '.github/workflows/ci.yml'), 'utf8');

function pinPackageJson(override = PINNED_EXPO_MODULES_CORE) {
  return { overrides: { 'expo-modules-core': override } };
}

function pinLockfile({
  resolved = PINNED_EXPO_MODULES_CORE,
  declaredRange = '~58.0.24',
  nested = false,
  topLevel = true,
} = {}) {
  const packages = {
    'node_modules/expo': { dependencies: { 'expo-modules-core': declaredRange } },
  };
  if (topLevel) {
    packages['node_modules/expo-modules-core'] = { version: resolved };
  }
  if (nested) {
    packages['node_modules/expo/node_modules/expo-modules-core'] = { version: '58.0.28' };
  }
  return { packages };
}

test('lockPackageName reads the leaf package from a lockfile key', () => {
  assert.equal(lockPackageName('node_modules/expo-modules-core'), 'expo-modules-core');
  assert.equal(lockPackageName('node_modules/expo/node_modules/expo-modules-core'), 'expo-modules-core');
  assert.equal(lockPackageName('node_modules/@expo/cli'), '@expo/cli');
  assert.equal(lockPackageName(''), '');
});

test('isHigherBarPackage matches expo, expo-modules-*, and the Fabric-sensitive natives', () => {
  assert.equal(isHigherBarPackage('expo'), true);
  assert.equal(isHigherBarPackage('expo-modules-core'), true);
  assert.equal(isHigherBarPackage('expo-modules-autolinking'), true);
  assert.equal(isHigherBarPackage('react-native-screens'), true);
  assert.equal(isHigherBarPackage('react-native-gesture-handler'), true);
  assert.equal(isHigherBarPackage('react-native-safe-area-context'), true);
  assert.equal(isHigherBarPackage('react-native-reanimated'), true);
  assert.equal(isHigherBarPackage('expo-image'), false);
  assert.equal(isHigherBarPackage('react-native-svg'), false);
});

test('semver helpers treat the first version in a range as the floor', () => {
  assert.deepEqual(parseSemver('~58.0.28'), { major: 58, minor: 0, patch: 28 });
  assert.equal(compareSemver('58.0.28', '58.0.24'), 4);
  assert.equal(isRangeNewerThanPin('~58.0.28'), true);
  assert.equal(isRangeNewerThanPin('~58.0.24'), false);
  assert.equal(isRangeNewerThanPin('~58.0.20'), false);
  assert.equal(isRangeNewerThanPin('not-a-range'), false);
});

test('inspectPin accepts the #2408 override and a single top-level 58.0.24 resolve', () => {
  const report = inspectPin({
    packageJson: pinPackageJson(),
    lockfile: pinLockfile({ declaredRange: '~58.0.28' }),
  });
  assert.equal(report.ok, true);
  assert.equal(report.resolved, PINNED_EXPO_MODULES_CORE);
  assert.equal(report.declaredRange, '~58.0.28');
  assert.equal(report.declaredRangeNewerThanPin, true);
  assert.deepEqual(report.errors, []);
});

test('inspectPin fails when the override, top-level resolve, or a nested expo/ copy is wrong', () => {
  const missingOverride = inspectPin({
    packageJson: { overrides: {} },
    lockfile: pinLockfile(),
  });
  assert.equal(missingOverride.ok, false);
  assert.match(missingOverride.errors[0], /overrides.expo-modules-core/);

  const wrongResolve = inspectPin({
    packageJson: pinPackageJson(),
    lockfile: pinLockfile({ resolved: '58.0.28' }),
  });
  assert.equal(wrongResolve.ok, false);
  assert.match(wrongResolve.errors[0], /node_modules\/expo-modules-core/);

  const nestedLock = pinLockfile({ nested: true });
  const nested = inspectPin({
    packageJson: pinPackageJson(),
    lockfile: nestedLock,
  });
  assert.equal(nested.ok, false);
  assert.deepEqual(nestedExpoModulesCoreKeys(nestedLock.packages), [
    'node_modules/expo/node_modules/expo-modules-core',
  ]);
  assert.match(nested.errors.join('\n'), /nest expo-modules-core under expo\//);

  const nestedOnly = inspectPin({
    packageJson: pinPackageJson(),
    lockfile: pinLockfile({ nested: true, topLevel: false }),
  });
  assert.equal(nestedOnly.ok, false);
  assert.match(nestedOnly.errors.join('\n'), /node_modules\/expo-modules-core/);
  assert.match(nestedOnly.errors.join('\n'), /nest expo-modules-core under expo\//);
});

test('inspectPinFromDir passes on the current mobile tree and notes the newer declared range', () => {
  const report = inspectPinFromDir(path.join(repoRoot, 'src/QueenZone.Mobile'));
  assert.equal(report.ok, true, report.errors.join('\n'));
  assert.equal(report.override, PINNED_EXPO_MODULES_CORE);
  assert.equal(report.resolved, PINNED_EXPO_MODULES_CORE);
  assert.deepEqual(report.nested, []);
  assert.equal(report.declaredRangeNewerThanPin, isRangeNewerThanPin(report.declaredRange));
});

test('package and lockfile change helpers name only version moves', () => {
  const packageChanges = packageJsonVersionChanges(
    { dependencies: { expo: '~58.0.21', leftover: '1.0.0' } },
    { dependencies: { expo: '~58.0.22' } },
  );
  assert.deepEqual(packageChanges, [
    { name: 'expo', from: '~58.0.21', to: '~58.0.22', section: 'dependencies' },
    { name: 'leftover', from: '1.0.0', to: null, section: 'dependencies' },
  ]);

  const lockChanges = lockfileVersionChanges(
    {
      packages: {
        'node_modules/expo': { version: '58.0.21' },
        'node_modules/expo/node_modules/nested': { version: '1.0.0' },
      },
    },
    {
      packages: {
        'node_modules/expo': { version: '58.0.22' },
        'node_modules/expo/node_modules/nested': { version: '2.0.0' },
      },
    },
  );
  assert.deepEqual(lockChanges, [{ name: 'expo', from: '58.0.21', to: '58.0.22', key: 'node_modules/expo' }]);
});

test('changedHigherBarPackages detects expo-modules-* even when only the lockfile moved', () => {
  const names = changedHigherBarPackages({
    beforePackage: { dependencies: { 'expo-image': '~58.0.1' } },
    afterPackage: { dependencies: { 'expo-image': '~58.0.2' } },
    beforeLock: { packages: { 'node_modules/expo-modules-core': { version: '58.0.24' } } },
    afterLock: { packages: { 'node_modules/expo-modules-core': { version: '58.0.25' } } },
  });
  assert.deepEqual(names, ['expo-modules-core']);
});

test('buildWeeklyPrBody always has Verification and Relates to #2407, and adds the higher-bar list only when those packages moved', () => {
  const routine = buildWeeklyPrBody({
    packageChanges: [{ name: 'expo-image', from: '~58.0.1', to: '~58.0.2', section: 'dependencies' }],
    higherBarPackages: [],
    pinReport: { declaredRangeNewerThanPin: false },
  });
  assert.match(routine, /^## Verification$/m);
  assert.match(routine, /^Relates to #2407$/m);
  assert.match(routine, /suite=journeys/);
  assert.doesNotMatch(routine, /mobile.crosswords.play/);
  assert.doesNotMatch(routine, /Changelog note/);

  const higher = buildWeeklyPrBody({
    packageChanges: [{ name: 'expo', from: '~58.0.21', to: '~58.0.22', section: 'dependencies' }],
    higherBarPackages: ['expo', 'react-native-screens'],
    pinReport: { declaredRangeNewerThanPin: true, declaredRange: '~58.0.28' },
  });
  assert.match(higher, /detected: expo, react-native-screens/);
  assert.match(higher, /suite=proof/);
  assert.match(higher, /feature=mobile.crosswords.play/);
  assert.match(higher, /platform=android/);
  assert.match(higher, /Changelog note \(Fabric \/ view-removal\)/);
  assert.match(higher, /~58.0.28/);
  assert.match(higher, /#2408 pin/);
});

test('check-pin --json and write-pr-body CLI write the expected artifacts', () => {
  const logs = [];
  const errors = [];
  const pinCode = main(['node', 'scripts/expo-weekly-update.mjs', 'check-pin', '--json', '--root', repoRoot], {
    log: (line) => logs.push(String(line)),
    error: (line) => errors.push(String(line)),
  });
  assert.equal(pinCode, 0, errors.join('\n'));
  const report = JSON.parse(logs.join('\n'));
  assert.equal(report.ok, true);

  const dir = mkdtempSync(path.join(tmpdir(), 'qz-expo-weekly-'));
  const beforePackage = path.join(dir, 'before-package.json');
  const beforeLock = path.join(dir, 'before-lock.json');
  const out = path.join(dir, 'body.md');
  writeFileSync(
    beforePackage,
    JSON.stringify({ dependencies: { expo: '~58.0.21', 'react-native-screens': '~4.30.0' } }),
  );
  writeFileSync(
    beforeLock,
    JSON.stringify({
      packages: {
        'node_modules/expo': { version: '58.0.21', dependencies: { 'expo-modules-core': '~58.0.24' } },
        'node_modules/expo-modules-core': { version: PINNED_EXPO_MODULES_CORE },
        'node_modules/react-native-screens': { version: '4.30.0' },
      },
    }),
  );
  const afterPackage = path.join(dir, 'package.json');
  const afterLock = path.join(dir, 'package-lock.json');
  writeFileSync(
    afterPackage,
    JSON.stringify({
      dependencies: { expo: '~58.0.22', 'react-native-screens': '~4.31.0' },
      overrides: { 'expo-modules-core': PINNED_EXPO_MODULES_CORE },
    }),
  );
  writeFileSync(
    afterLock,
    JSON.stringify({
      packages: {
        'node_modules/expo': { version: '58.0.22', dependencies: { 'expo-modules-core': '~58.0.28' } },
        'node_modules/expo-modules-core': { version: PINNED_EXPO_MODULES_CORE },
        'node_modules/react-native-screens': { version: '4.31.0' },
      },
    }),
  );
  const code = main(
    [
      'node',
      'scripts/expo-weekly-update.mjs',
      'write-pr-body',
      '--before-package',
      beforePackage,
      '--before-lock',
      beforeLock,
      '--after-package',
      afterPackage,
      '--after-lock',
      afterLock,
      '--out',
      out,
    ],
    { log() {}, error() {} },
  );
  assert.equal(code, 0);
  const body = readFileSync(out, 'utf8');
  assert.match(body, /detected: expo, react-native-screens/);
  assert.match(body, /^Relates to #2407$/m);
});

test('parseArgs and unknown commands stay strict', () => {
  const parsed = parseArgs(['node', 'script', 'check-pin', '--json', '--root', '/tmp']);
  assert.equal(parsed.command, 'check-pin');
  assert.equal(parsed.json, true);
  assert.equal(parsed.root, '/tmp');
  assert.throws(() => parseArgs(['node', 'script', 'check-pin', '--nope']), /Unknown argument/);
  const errors = [];
  assert.equal(main(['node', 'script', 'nope'], { error: (line) => errors.push(String(line)) }), 2);
  assert.match(errors[0], /Usage:/);
});

test('the weekly workflow uses a PAT, remote doctor, the pin script, and is not a required check', () => {
  assert.match(workflow, /cron: "0 21 \* \* 0"/);
  assert.match(workflow, /workflow_dispatch:/);
  assert.match(workflow, /npx expo install --fix/);
  assert.match(workflow, /npm install --no-audit/);
  assert.match(workflow, /npm run doctor/);
  assert.match(workflow, /do not set EXPO_OFFLINE/);
  assert.doesNotMatch(workflow, /EXPO_OFFLINE\s*=/);
  assert.match(workflow, new RegExp(TOKEN_SECRET_NAME));
  assert.match(workflow, /EXPO_WEEKLY_PR_TOKEN is not set/);
  assert.match(workflow, /GITHUB_TOKEN cannot/);
  assert.match(workflow, new RegExp(WEEKLY_BRANCH));
  assert.match(workflow, /scripts\/expo-weekly-update\.mjs check-pin --json/);
  assert.match(workflow, /uses: actions\/checkout@3d3c42e5aac5ba805825da76410c181273ba90b1/);
  assert.match(workflow, /uses: actions\/setup-node@949feb2413d6458794dcd2491c4babbbce0c15c1/);
  assert.match(workflow, /^permissions: \{\}$/m);
  assert.doesNotMatch(workflow, /workflow_run:/);
  assert.doesNotMatch(ci, /expo-weekly-update/);
  assert.doesNotMatch(ci, /needs:[\s\S]*expo-weekly/);
});

test('Dependabot no longer groups Expo and ignores its patch and minor version updates', () => {
  assert.doesNotMatch(dependabot, /^\s+expo-sdk:/m);
  assert.match(
    dependabot,
    /dependency-name: "expo"[\s\S]*?version-update:semver-major[\s\S]*?version-update:semver-minor[\s\S]*?version-update:semver-patch/,
  );
  assert.match(
    dependabot,
    /dependency-name: "expo-\*"[\s\S]*?version-update:semver-major[\s\S]*?version-update:semver-minor[\s\S]*?version-update:semver-patch/,
  );
  assert.match(
    dependabot,
    /dependency-name: "@expo\/\*"[\s\S]*?version-update:semver-major[\s\S]*?version-update:semver-minor[\s\S]*?version-update:semver-patch/,
  );
  assert.match(
    dependabot,
    /dependency-name: "react-native-screens"[\s\S]*?version-update:semver-major[\s\S]*?version-update:semver-minor[\s\S]*?version-update:semver-patch/,
  );
  assert.match(
    dependabot,
    /dependency-name: "react-native-gesture-handler"[\s\S]*?version-update:semver-major[\s\S]*?version-update:semver-minor[\s\S]*?version-update:semver-patch/,
  );
  assert.match(
    dependabot,
    /dependency-name: "react-native-safe-area-context"[\s\S]*?version-update:semver-major[\s\S]*?version-update:semver-minor[\s\S]*?version-update:semver-patch/,
  );
  assert.match(dependabot, /mobile-npm-security-minor-patch:/);
});
