import { execFileSync } from 'node:child_process';
import { mkdtempSync, readFileSync, writeFileSync } from 'node:fs';
import path from 'node:path';
import { tmpdir } from 'node:os';
import { fileURLToPath } from 'node:url';
import { test } from 'node:test';
import assert from 'node:assert/strict';
import {
  TOKEN_SECRET_NAME,
  WEEKLY_BRANCH,
  WORKFLOW_PATH,
  alignCommands,
  buildWeeklyPrBody,
  changedHigherBarPackages,
  compareSemver,
  expoCliPath,
  inspectPin,
  inspectPinFromDir,
  isHigherBarPackage,
  lockPackageName,
  lockfileVersionChanges,
  main,
  nestedExpoModulesCoreKeys,
  packageJsonVersionChanges,
  parseArgs,
  parseSemver,
  versionSatisfiesRange,
} from './expo-weekly-update.mjs';

const repoRoot = fileURLToPath(new URL('..', import.meta.url));
const workflow = readFileSync(path.join(repoRoot, WORKFLOW_PATH), 'utf8');
const dependabot = readFileSync(path.join(repoRoot, '.github/dependabot.yml'), 'utf8');
const ci = readFileSync(path.join(repoRoot, '.github/workflows/ci.yml'), 'utf8');
const scriptsTests = readFileSync(path.join(repoRoot, '.github/workflows/scripts-tests.yml'), 'utf8');

function workflowJob(name) {
  const startRe = new RegExp(`^  ${name}:$`, 'm');
  const start = startRe.exec(workflow);
  assert.ok(start, `missing workflow job ${name}`);
  const nextRe = /^  [a-z][-\w]*:$/gm;
  nextRe.lastIndex = start.index + start[0].length;
  const next = nextRe.exec(workflow);
  return next ? workflow.slice(start.index, next.index) : workflow.slice(start.index);
}

function pinPackageJson(override = '58.0.24') {
  return { overrides: { 'expo-modules-core': override } };
}

function pinLockfile({
  resolved = '58.0.30',
  declaredRange = '~58.0.30',
  nested = false,
  topLevel = true,
  includeDeclaredRange = true,
} = {}) {
  const packages = {
    'node_modules/expo': includeDeclaredRange
      ? { dependencies: { 'expo-modules-core': declaredRange } }
      : {},
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

test('versionSatisfiesRange understands tilde, caret, and exact versions', () => {
  assert.deepEqual(parseSemver('~58.0.30'), { major: 58, minor: 0, patch: 30 });
  assert.equal(compareSemver('58.0.30', '58.0.24'), 6);
  assert.equal(versionSatisfiesRange('58.0.30', '~58.0.30'), true);
  assert.equal(versionSatisfiesRange('58.0.31', '~58.0.30'), true);
  assert.equal(versionSatisfiesRange('58.0.29', '~58.0.30'), false);
  assert.equal(versionSatisfiesRange('58.1.0', '~58.0.30'), false);
  assert.equal(versionSatisfiesRange('58.0.30', '^58.0.30'), true);
  assert.equal(versionSatisfiesRange('58.1.0', '^58.0.30'), true);
  assert.equal(versionSatisfiesRange('59.0.0', '^58.0.30'), false);
  assert.equal(versionSatisfiesRange('58.0.30', '58.0.30'), true);
  assert.equal(versionSatisfiesRange('58.0.31', '58.0.30'), false);
  assert.equal(versionSatisfiesRange('58.0.30', ''), false);
  assert.equal(versionSatisfiesRange('', '~58.0.30'), false);
  assert.equal(versionSatisfiesRange('58.0.30', 'not-a-range'), false);
});

test('inspectPin accepts no override when the resolved version satisfies expo\'s declared range (#2407 unpin)', () => {
  const report = inspectPin({
    packageJson: { overrides: {} },
    lockfile: pinLockfile({ resolved: '58.0.30', declaredRange: '~58.0.30' }),
  });
  assert.equal(report.ok, true);
  assert.equal(report.override, null);
  assert.equal(report.resolved, '58.0.30');
  assert.equal(report.declaredRange, '~58.0.30');
  assert.equal('declaredRangeNewerThanPin' in report, false);
  assert.deepEqual(report.errors, []);
});

test('inspectPin fails when the override returns, the top-level resolve is missing, or expo/ nests a copy', () => {
  const pinned = inspectPin({
    packageJson: pinPackageJson(),
    lockfile: pinLockfile({ resolved: '58.0.24', declaredRange: '~58.0.30' }),
  });
  assert.equal(pinned.ok, false);
  assert.match(pinned.errors[0], /must not override expo-modules-core/);

  const nestedLock = pinLockfile({ resolved: '58.0.30', nested: true });
  const nested = inspectPin({
    packageJson: { overrides: {} },
    lockfile: nestedLock,
  });
  assert.equal(nested.ok, false);
  assert.deepEqual(nestedExpoModulesCoreKeys(nestedLock.packages), [
    'node_modules/expo/node_modules/expo-modules-core',
  ]);
  assert.match(nested.errors.join('\n'), /nest expo-modules-core under expo\//);

  const nestedOnly = inspectPin({
    packageJson: {},
    lockfile: pinLockfile({ nested: true, topLevel: false }),
  });
  assert.equal(nestedOnly.ok, false);
  assert.match(nestedOnly.errors.join('\n'), /node_modules\/expo-modules-core/);
  assert.match(nestedOnly.errors.join('\n'), /nest expo-modules-core under expo\//);
});

test('inspectPin fails when the resolved version is outside expo\'s declared range or the range is missing', () => {
  const stale = inspectPin({
    packageJson: {},
    lockfile: pinLockfile({ resolved: '58.0.24', declaredRange: '~58.0.30' }),
  });
  assert.equal(stale.ok, false);
  assert.match(stale.errors.join('\n'), /satisfies expo's declared range ~58\.0\.30/);
  assert.match(stale.errors.join('\n'), /found 58\.0\.24/);

  const missingRange = inspectPin({
    packageJson: {},
    lockfile: pinLockfile({ resolved: '58.0.30', includeDeclaredRange: false }),
  });
  assert.equal(missingRange.ok, false);
  assert.match(missingRange.errors.join('\n'), /declared expo-modules-core range/);
});

test('inspectPinFromDir passes on the current mobile tree with no override and a satisfying resolve', () => {
  const report = inspectPinFromDir(path.join(repoRoot, 'src/QueenZone.Mobile'));
  assert.equal(report.ok, true, report.errors.join('\n'));
  assert.equal(report.override, null);
  assert.ok(report.resolved);
  assert.ok(report.declaredRange);
  assert.equal(versionSatisfiesRange(report.resolved, report.declaredRange), true);
  assert.deepEqual(report.nested, []);
  assert.equal('declaredRangeNewerThanPin' in report, false);
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
    pinReport: { resolved: '58.0.30', declaredRange: '~58.0.30' },
  });
  assert.match(routine, /^## Verification$/m);
  assert.match(routine, /^Relates to #2407$/m);
  assert.match(routine, /suite=journeys/);
  assert.match(routine, /satisfies expo's declared range `~58\.0\.30`/);
  assert.doesNotMatch(routine, /mobile.crosswords.play/);
  assert.doesNotMatch(routine, /Changelog note/);
  assert.doesNotMatch(routine, /#2408 pin/);

  const higher = buildWeeklyPrBody({
    packageChanges: [{ name: 'expo', from: '~58.0.21', to: '~58.0.22', section: 'dependencies' }],
    higherBarPackages: ['expo', 'react-native-screens'],
    pinReport: { resolved: '58.0.30', declaredRange: '~58.0.30' },
  });
  assert.match(higher, /detected: expo, react-native-screens/);
  assert.match(higher, /suite=proof/);
  assert.match(higher, /feature=mobile.crosswords.play/);
  assert.match(higher, /platform=android/);
  assert.match(higher, /Changelog note \(Fabric \/ view-removal\)/);
  assert.match(higher, /satisfies expo's declared range `~58\.0\.30`/);
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
  assert.ok(report.declaredRange);
  assert.equal(versionSatisfiesRange(report.resolved, report.declaredRange), true);

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
        'node_modules/expo': { version: '58.0.21', dependencies: { 'expo-modules-core': '~58.0.30' } },
        'node_modules/expo-modules-core': { version: '58.0.30' },
        'node_modules/react-native-screens': { version: '4.30.0' },
      },
    }),
  );
  const afterPackage = path.join(dir, 'package.json');
  const afterLock = path.join(dir, 'package-lock.json');
  writeFileSync(
    afterPackage,
    JSON.stringify({
      dependencies: { expo: '~58.0.23', 'react-native-screens': '~4.31.0' },
    }),
  );
  writeFileSync(
    afterLock,
    JSON.stringify({
      packages: {
        'node_modules/expo': { version: '58.0.23', dependencies: { 'expo-modules-core': '~58.0.30' } },
        'node_modules/expo-modules-core': { version: '58.0.30' },
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
  assert.match(body, /satisfies expo's declared range `~58\.0\.30`/);
});

test('align runs npm ci, the lockfile-local expo CLI, then a full npm install with scripts disabled', () => {
  const calls = [];
  const code = main(['node', 'scripts/expo-weekly-update.mjs', 'align', '--root', '/repo'], {
    execFile(command, argv, options) {
      calls.push({ command, argv, cwd: options.cwd, env: options.env });
    },
    env: { PATH: '/bin', EXPO_WEEKLY_PR_TOKEN: 'must-not-leak-into-requirement' },
  });
  assert.equal(code, 0);
  assert.deepEqual(alignCommands('/repo/src/QueenZone.Mobile'), [
    ['npm', ['ci', '--no-audit', '--ignore-scripts'], '/repo/src/QueenZone.Mobile'],
    [expoCliPath('/repo/src/QueenZone.Mobile'), ['install', '--fix'], '/repo/src/QueenZone.Mobile'],
    ['npm', ['install', '--no-audit', '--ignore-scripts'], '/repo/src/QueenZone.Mobile'],
  ]);
  assert.deepEqual(
    calls.map((call) => [call.command, ...call.argv]),
    [
      ['npm', 'ci', '--no-audit', '--ignore-scripts'],
      [expoCliPath('/repo/src/QueenZone.Mobile'), 'install', '--fix'],
      ['npm', 'install', '--no-audit', '--ignore-scripts'],
    ],
  );
  assert.equal(calls[0].env.npm_config_ignore_scripts, 'true');
  assert.equal(calls[1].env.npm_config_ignore_scripts, 'true');
  assert.equal(calls[0].env.EXPO_WEEKLY_PR_TOKEN, undefined);
  assert.equal(calls[0].env.GH_TOKEN, undefined);
});

test('excuse-doctor never excuses findings now that the pin is gone', () => {
  const dir = mkdtempSync(path.join(tmpdir(), 'qz-expo-excuse-'));
  const pinJson = path.join(dir, 'pin.json');
  const doctorLog = path.join(dir, 'doctor.log');
  const checkJson = path.join(dir, 'check.json');
  writeFileSync(pinJson, JSON.stringify({ declaredRangeNewerThanPin: true }));
  writeFileSync(
    doctorLog,
    [
      '✖ Check packages match versions required by installed Expo SDK',
      '  expo-modules-core@58.0.24 - expected ~58.0.28',
      '',
    ].join('\n'),
  );
  writeFileSync(checkJson, JSON.stringify([{ packageName: 'expo-modules-core' }]));
  const errors = [];
  const code = main(
    [
      'node',
      'scripts/expo-weekly-update.mjs',
      'excuse-doctor',
      '--pin-json',
      pinJson,
      '--doctor-log',
      doctorLog,
      '--check-json',
      checkJson,
    ],
    {
      log() {},
      error: (line) => errors.push(String(line)),
    },
  );
  assert.equal(code, 1);
  assert.match(errors.join('\n'), /no doctor findings are excused/);
});

test('parseArgs and unknown commands stay strict', () => {
  const parsed = parseArgs([
    'node',
    'script',
    'excuse-doctor',
    '--json',
    '--root',
    '/tmp',
    '--doctor-log',
    '/tmp/doctor.log',
    '--check-json',
    '/tmp/check.json',
  ]);
  assert.equal(parsed.command, 'excuse-doctor');
  assert.equal(parsed.json, true);
  assert.equal(parsed.root, '/tmp');
  assert.equal(parsed.doctorLog, '/tmp/doctor.log');
  assert.equal(parsed.checkJson, '/tmp/check.json');
  assert.throws(() => parseArgs(['node', 'script', 'check-pin', '--nope']), /Unknown argument/);
  const errors = [];
  assert.equal(main(['node', 'script', 'nope'], { error: (line) => errors.push(String(line)) }), 2);
  assert.match(errors[0], /Usage:/);
});

test('the weekly workflow uses a PAT, remote doctor, the pin script, and is not a required check', () => {
  assert.match(workflow, /cron: "0 21 \* \* 0"/);
  assert.match(workflow, /workflow_dispatch:/);
  assert.match(workflow, /scripts\/expo-weekly-update\.mjs align/);
  assert.match(workflow, /npm run doctor/);
  assert.match(workflow, /scripts\/expo-weekly-update\.mjs excuse-doctor/);
  assert.doesNotMatch(workflow, /\bnpx\b/);
  assert.doesNotMatch(workflow, /npm ci|npm install/);
  assert.match(workflow, /do not set EXPO_OFFLINE/);
  assert.doesNotMatch(workflow, /EXPO_OFFLINE\s*=/);
  assert.match(workflow, new RegExp(TOKEN_SECRET_NAME));
  assert.match(workflow, /EXPO_WEEKLY_PR_TOKEN is not set/);
  assert.match(workflow, /EXPO_WEEKLY_PR_TOKEN is present\./);
  assert.doesNotMatch(workflow, /length \$\{#/);
  assert.match(workflow, /GITHUB_TOKEN cannot/);
  assert.match(workflow, new RegExp(WEEKLY_BRANCH));
  assert.match(workflow, /scripts\/expo-weekly-update\.mjs check-pin --json/);
  assert.match(workflow, /uses: actions\/checkout@3d3c42e5aac5ba805825da76410c181273ba90b1/);
  assert.match(workflow, /uses: actions\/setup-node@949feb2413d6458794dcd2491c4babbbce0c15c1/);
  assert.match(workflow, /^permissions: \{\}$/m);
  assert.doesNotMatch(workflow, /workflow_run:/);
  assert.doesNotMatch(ci, /expo-weekly-update/);
  assert.doesNotMatch(ci, /needs:[\s\S]*expo-weekly/);
  assert.match(scriptsTests, /node --test \$\(find scripts -name '\*\.test\.mjs' \| sort\)/);
  assert.doesNotMatch(scriptsTests, /Test Expo weekly update helpers/);
  assert.doesNotMatch(scriptsTests, /node --test scripts\/expo-weekly-update\.test\.mjs/);
  const foundTests = execFileSync('find', ['scripts', '-name', '*.test.mjs'], {
    cwd: repoRoot,
    encoding: 'utf8',
  });
  assert.match(foundTests.replaceAll('\\', '/'), /(^|\/)scripts\/expo-weekly-update\.test\.mjs$/m);
});

test('the weekly workflow isolates the write PAT from npm and closes a stale PR', () => {
  const align = workflowJob('align');
  const publish = workflowJob('publish-pr');
  assert.match(align, /contents: read/);
  assert.doesNotMatch(align, /actions: write/);
  assert.match(align, /npm_config_ignore_scripts: "true"/);
  assert.match(align, /persist-credentials: false/);
  assert.doesNotMatch(align, /secrets\.EXPO_WEEKLY_PR_TOKEN/);
  assert.doesNotMatch(align, /^\s+token:/m);
  assert.doesNotMatch(workflow, /fetch-depth:\s*0/);
  assert.match(publish, /needs: align/);
  assert.match(publish, /contents: read/);
  assert.doesNotMatch(publish, /actions: read/);
  assert.match(publish, /persist-credentials: false/);
  assert.match(publish, /secrets\.EXPO_WEEKLY_PR_TOKEN/);
  assert.match(publish, /isCrossRepository == false/);
  assert.match(publish, /author\.login == \$login/);
  assert.match(publish, /gh pr close/);
  assert.match(publish, /closing the stale weekly PR/);
  assert.match(publish, /gh api user/);
  assert.match(publish, /users\.noreply\.github\.com/);
  assert.doesNotMatch(workflow, /github-actions\[bot\]/);
  assert.doesNotMatch(publish, /\bnpm\b/);
  assert.doesNotMatch(publish, /\bnpx\b/);
  assert.doesNotMatch(publish, /node scripts\/expo-weekly-update/);
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
