#!/usr/bin/env node
/**
 * Weekly Expo alignment helpers (#2419).
 *
 *   node scripts/expo-weekly-update.mjs align
 *   node scripts/expo-weekly-update.mjs check-pin [--json]
 *   node scripts/expo-weekly-update.mjs write-pr-body --before-package <file> --before-lock <file> [--pin-json <file>] [--out <file>]
 *
 * The pin guard fails unless src/QueenZone.Mobile still overrides
 * expo-modules-core to 58.0.24 and the lockfile resolves that version at
 * the single top-level node_modules/expo-modules-core key, with no nested
 * copy under expo/. A newer range declared by upstream expo is recorded,
 * not treated as a pin failure (#2407).
 */
import { execFileSync } from 'node:child_process';
import { readFileSync, writeFileSync } from 'node:fs';
import path from 'node:path';
import { pathToFileURL } from 'node:url';

export const PINNED_EXPO_MODULES_CORE = '58.0.24';
export const MOBILE_DIR = 'src/QueenZone.Mobile';
export const TOP_LEVEL_LOCK_KEY = 'node_modules/expo-modules-core';
export const EXPO_LOCK_KEY = 'node_modules/expo';
export const NESTED_EXPO_LOCK_KEY = 'node_modules/expo/node_modules/expo-modules-core';
export const TOKEN_SECRET_NAME = 'EXPO_WEEKLY_PR_TOKEN';
export const WEEKLY_BRANCH = 'deps/expo-weekly';
export const PIN_ISSUE = '#2407';
export const WORKFLOW_PATH = '.github/workflows/expo-weekly-update.yml';

const EXACT_HIGHER_BAR = new Set([
  'expo',
  'react-native-screens',
  'react-native-gesture-handler',
  'react-native-safe-area-context',
  'react-native-reanimated',
]);

export function isHigherBarPackage(name) {
  const pkg = String(name || '');
  return EXACT_HIGHER_BAR.has(pkg) || pkg.startsWith('expo-modules-');
}

export function lockPackageName(lockKey) {
  const posix = String(lockKey || '').replaceAll('\\', '/');
  const marker = 'node_modules/';
  const index = posix.lastIndexOf(marker);
  if (index === -1) {
    return '';
  }
  return posix.slice(index + marker.length);
}

export function nestedExpoModulesCoreKeys(packages = {}) {
  return Object.keys(packages)
    .filter((key) => key !== TOP_LEVEL_LOCK_KEY && lockPackageName(key) === 'expo-modules-core')
    .sort((left, right) => left.localeCompare(right));
}

export function parseSemver(value) {
  const match = String(value || '').match(/(\d+)\.(\d+)\.(\d+)/);
  if (!match) {
    return null;
  }
  return { major: Number(match[1]), minor: Number(match[2]), patch: Number(match[3]) };
}

export function compareSemver(left, right) {
  const first = typeof left === 'string' ? parseSemver(left) : left;
  const second = typeof right === 'string' ? parseSemver(right) : right;
  if (!first || !second) {
    return 0;
  }
  return first.major - second.major || first.minor - second.minor || first.patch - second.patch;
}

export function isRangeNewerThanPin(range, pin = PINNED_EXPO_MODULES_CORE) {
  const minimum = parseSemver(range);
  return Boolean(minimum && compareSemver(minimum, pin) > 0);
}

export function readJson(filePath) {
  return JSON.parse(readFileSync(filePath, 'utf8'));
}

export function inspectPin({ packageJson, lockfile } = {}) {
  const override = packageJson?.overrides?.['expo-modules-core'];
  const resolved = lockfile?.packages?.[TOP_LEVEL_LOCK_KEY]?.version;
  const nested = nestedExpoModulesCoreKeys(lockfile?.packages);
  const declaredRange = lockfile?.packages?.[EXPO_LOCK_KEY]?.dependencies?.['expo-modules-core'] ?? null;
  const errors = [];
  if (override !== PINNED_EXPO_MODULES_CORE) {
    errors.push(
      `package.json overrides.expo-modules-core must stay ${PINNED_EXPO_MODULES_CORE} until ${PIN_ISSUE} (found ${override ?? 'missing'}).`,
    );
  }
  if (resolved !== PINNED_EXPO_MODULES_CORE) {
    errors.push(
      `package-lock.json must resolve ${TOP_LEVEL_LOCK_KEY} to ${PINNED_EXPO_MODULES_CORE} (found ${resolved ?? 'missing'}).`,
    );
  }
  if (nested.length > 0) {
    errors.push(`package-lock.json must not nest expo-modules-core under expo/ (found ${nested.join(', ')}).`);
  }
  return {
    ok: errors.length === 0,
    override: override ?? null,
    resolved: resolved ?? null,
    nested,
    declaredRange,
    declaredRangeNewerThanPin: isRangeNewerThanPin(declaredRange),
    errors,
  };
}

export function inspectPinFromDir(mobileDir) {
  return inspectPin({
    packageJson: readJson(path.join(mobileDir, 'package.json')),
    lockfile: readJson(path.join(mobileDir, 'package-lock.json')),
  });
}

function versionMap(section = {}) {
  return new Map(Object.entries(section));
}

export function packageJsonVersionChanges(before = {}, after = {}) {
  const changes = [];
  for (const section of ['dependencies', 'devDependencies']) {
    const previous = versionMap(before[section]);
    const next = versionMap(after[section]);
    const names = new Set([...previous.keys(), ...next.keys()]);
    for (const name of [...names].sort((left, right) => left.localeCompare(right))) {
      const from = previous.get(name);
      const to = next.get(name);
      if (from !== to) {
        changes.push({ name, from: from ?? null, to: to ?? null, section });
      }
    }
  }
  return changes;
}

export function lockfileVersionChanges(before = {}, after = {}) {
  const previous = before.packages ?? {};
  const next = after.packages ?? {};
  const keys = new Set([...Object.keys(previous), ...Object.keys(next)]);
  const changes = [];
  for (const key of [...keys].sort((left, right) => left.localeCompare(right))) {
    if (key.includes('/node_modules/')) {
      continue;
    }
    const name = lockPackageName(key);
    if (!name) {
      continue;
    }
    const from = previous[key]?.version;
    const to = next[key]?.version;
    if (from !== to) {
      changes.push({ name, from: from ?? null, to: to ?? null, key });
    }
  }
  return changes;
}

export function changedHigherBarPackages({ beforePackage, afterPackage, beforeLock, afterLock } = {}) {
  const names = new Set();
  for (const change of packageJsonVersionChanges(beforePackage, afterPackage)) {
    if (isHigherBarPackage(change.name)) {
      names.add(change.name);
    }
  }
  for (const change of lockfileVersionChanges(beforeLock, afterLock)) {
    if (isHigherBarPackage(change.name)) {
      names.add(change.name);
    }
  }
  return [...names].sort((left, right) => left.localeCompare(right));
}

function formatChangeLine(change) {
  return `- ${change.name}: ${change.from ?? '(absent)'} → ${change.to ?? '(removed)'}`;
}

export function buildWeeklyPrBody({
  packageChanges = [],
  higherBarPackages = [],
  pinReport = inspectPin(),
} = {}) {
  const lines = [
    '## Summary',
    '',
    'Weekly Expo SDK alignment via `npx expo install --fix` (#2419 option a).',
    '',
  ];
  if (packageChanges.length === 0) {
    lines.push('No direct `package.json` version changes; lockfile or override resolution still moved.', '');
  } else {
    lines.push('Changed `package.json` versions:', '', ...packageChanges.map(formatChangeLine), '');
  }
  lines.push(
    '## expo-modules-core pin',
    '',
    `The \`overrides\` pin \`expo-modules-core@${PINNED_EXPO_MODULES_CORE}\` is still present, and \`package-lock.json\` resolves a single top-level \`${TOP_LEVEL_LOCK_KEY}\` at ${PINNED_EXPO_MODULES_CORE}.`,
    '',
  );
  if (pinReport.declaredRangeNewerThanPin) {
    lines.push(
      `Upstream \`expo\` now declares \`expo-modules-core\` as \`${pinReport.declaredRange}\`, which is newer than the #2408 pin. Unpinning is decided on ${PIN_ISSUE}, not in this weekly PR.`,
      '',
    );
  }
  lines.push(
    '## Verification',
    '',
    'Routine proof (every weekly bump):',
    '',
    '- [ ] Required checks are green, including expo-doctor',
    '- [ ] Dispatched `mobile-device-smoke.yml` journeys run (`suite=journeys`, `platform=both`) completed with no `parentView`, `FATAL EXCEPTION`, or `SIGSEGV` lines',
    '',
  );
  if (higherBarPackages.length > 0) {
    lines.push(
      `Higher-bar proof (detected: ${higherBarPackages.join(', ')}):`,
      '',
      '- [ ] 10/10 crossword proof runs: `mobile-device-smoke.yml` with `suite=proof`, `feature=mobile.crosswords.play`, `platform=android`, zero Fabric crashes',
      '- [ ] Changelog note below on any Fabric or view-removal change',
      '',
      '### Changelog note (Fabric / view-removal)',
      '',
      '_Record any Fabric or view-removal change from the packages above, or write "none observed"._',
      '',
    );
  }
  lines.push('## Issue', '', `Relates to ${PIN_ISSUE}`, '');
  return lines.join('\n');
}

export function parseArgs(argv) {
  const args = {
    command: argv[2] ?? '',
    json: false,
    root: process.cwd(),
    out: null,
    pinJson: null,
    beforePackage: null,
    beforeLock: null,
    afterPackage: null,
    afterLock: null,
  };
  for (let index = 3; index < argv.length; index += 1) {
    const flag = argv[index];
    const next = argv[index + 1];
    if (flag === '--json') {
      args.json = true;
      continue;
    }
    if (flag === '--root' && next) {
      args.root = next;
      index += 1;
      continue;
    }
    if (flag === '--out' && next) {
      args.out = next;
      index += 1;
      continue;
    }
    if (flag === '--pin-json' && next) {
      args.pinJson = next;
      index += 1;
      continue;
    }
    if (flag === '--before-package' && next) {
      args.beforePackage = next;
      index += 1;
      continue;
    }
    if (flag === '--before-lock' && next) {
      args.beforeLock = next;
      index += 1;
      continue;
    }
    if (flag === '--after-package' && next) {
      args.afterPackage = next;
      index += 1;
      continue;
    }
    if (flag === '--after-lock' && next) {
      args.afterLock = next;
      index += 1;
      continue;
    }
    throw new Error(`Unknown argument: ${flag}`);
  }
  return args;
}

function mobileDir(root) {
  return path.join(root, MOBILE_DIR);
}

export function expoCliPath(mobileRoot) {
  return path.join(mobileRoot, 'node_modules', '.bin', 'expo');
}

export function alignCommands(mobileRoot) {
  return Object.freeze([
    ['npm', ['ci', '--no-audit', '--ignore-scripts'], mobileRoot],
    [expoCliPath(mobileRoot), ['install', '--fix'], mobileRoot],
    ['npm', ['install', '--no-audit', '--ignore-scripts'], mobileRoot],
  ]);
}

export function runAlign(args, { execFile = execFileSync } = {}) {
  for (const [command, argv, cwd] of alignCommands(mobileDir(args.root))) {
    execFile(command, argv, { cwd, stdio: 'inherit' });
  }
  return 0;
}

export function runCheckPin(args, { log = console.log, error = console.error } = {}) {
  const report = inspectPinFromDir(mobileDir(args.root));
  if (args.json) {
    log(JSON.stringify(report, null, 2));
  } else if (report.ok) {
    log(`expo-modules-core pin ${PINNED_EXPO_MODULES_CORE} is intact.`);
    if (report.declaredRangeNewerThanPin) {
      log(`Upstream expo declares ${report.declaredRange}; record ${PIN_ISSUE} in the weekly PR body.`);
    }
  } else {
    for (const line of report.errors) {
      error(line);
    }
  }
  return report.ok ? 0 : 1;
}

export function runWritePrBody(args, { log = console.log } = {}) {
  const afterPackage = readJson(args.afterPackage ?? path.join(mobileDir(args.root), 'package.json'));
  const afterLock = readJson(args.afterLock ?? path.join(mobileDir(args.root), 'package-lock.json'));
  const beforePackage = args.beforePackage ? readJson(args.beforePackage) : afterPackage;
  const beforeLock = args.beforeLock ? readJson(args.beforeLock) : afterLock;
  const pinReport = args.pinJson ? readJson(args.pinJson) : inspectPin({ packageJson: afterPackage, lockfile: afterLock });
  const body = buildWeeklyPrBody({
    packageChanges: packageJsonVersionChanges(beforePackage, afterPackage),
    higherBarPackages: changedHigherBarPackages({
      beforePackage,
      afterPackage,
      beforeLock,
      afterLock,
    }),
    pinReport,
  });
  if (args.out) {
    writeFileSync(args.out, body);
  } else {
    log(body);
  }
  return 0;
}

export function main(argv = process.argv, io = {}) {
  const args = parseArgs(argv);
  if (args.command === 'align') {
    return runAlign(args, io);
  }
  if (args.command === 'check-pin') {
    return runCheckPin(args, io);
  }
  if (args.command === 'write-pr-body') {
    return runWritePrBody(args, io);
  }
  (io.error ?? console.error)(
    'Usage: node scripts/expo-weekly-update.mjs align | check-pin [--json] | write-pr-body --before-package <file> --before-lock <file>',
  );
  return 2;
}

if (import.meta.url === pathToFileURL(process.argv[1] ?? '').href) {
  process.exitCode = main();
}
