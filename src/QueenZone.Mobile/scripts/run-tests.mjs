/**
 * Discover all .test.ts and .test.tsx files under src/.
 * Pure tests (.test.ts) run on Node's test runner. Component tests (.test.tsx)
 * run on Jest + jest-expo. A self-check writes unlisted probes of both kinds
 * so new tests do not need a package.json path list (see #870 / #833).
 * Pass --coverage (or COLLECT_COVERAGE=1) to write Jest + Node reports for
 * scripts/Test-MobileCoverageGate.mjs. Contracts and Maestro stay out.
 */
import { existsSync, globSync, mkdirSync, readFileSync, rmSync, statSync, writeFileSync } from 'node:fs';
import { spawnSync } from 'node:child_process';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const mobileRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const srcDir = path.join(mobileRoot, 'src');
const jestBin = path.join(mobileRoot, 'node_modules', 'jest', 'bin', 'jest.js');
const collectCoverage =
  process.argv.includes('--coverage') || process.env.COLLECT_COVERAGE === '1';
const coverageRoot = path.join(mobileRoot, 'coverage');
const jestCoverageDir = path.join(coverageRoot, 'jest');
const nodeCoverageDir = path.join(coverageRoot, 'node');
const nodeLcovPath = path.join(nodeCoverageDir, 'lcov.info');

function toPosix(rel) {
  return path.join('src', rel).split(path.sep).join('/');
}

function discover(pattern) {
  return globSync(pattern, { cwd: srcDir }).map(toPosix).sort();
}

function runNodeTest(files, { coverage = false } = {}) {
  const args = [
    '--experimental-strip-types',
    '--disable-warning=MODULE_TYPELESS_PACKAGE_JSON',
    '--test',
  ];

  if (coverage) {
    mkdirSync(nodeCoverageDir, { recursive: true });
    args.push(
      '--experimental-test-coverage',
      '--test-coverage-include=src/**',
      '--test-coverage-exclude=**/*.test.ts',
      '--test-coverage-exclude=**/*.test.tsx',
      '--test-coverage-exclude=src/test/**',
      '--test-reporter=spec',
      '--test-reporter=lcov',
      '--test-reporter-destination=stdout',
      `--test-reporter-destination=${nodeLcovPath}`,
    );
  }

  args.push(...files);

  const result = spawnSync(process.execPath, args, { cwd: mobileRoot, stdio: 'inherit' });
  return result.status ?? 1;
}

function runJest(files, { coverage = false } = {}) {
  const args = [jestBin, '--ci', '--watchAll=false', '--runInBand'];
  if (process.argv.includes('--no-watchman')) {
    args.push('--watchman=false');
  }
  if (coverage) {
    mkdirSync(jestCoverageDir, { recursive: true });
    args.push('--coverage', `--coverageDirectory=${jestCoverageDir}`);
  }
  args.push('--', ...files);

  const result = spawnSync(process.execPath, args, { cwd: mobileRoot, stdio: 'inherit' });
  return result.status ?? 1;
}

function assertCoverageReports(kind) {
  if (kind === 'node') {
    if (!existsSync(nodeLcovPath) || statSync(nodeLcovPath).size === 0) {
      console.error(`Coverage collection failed: missing or empty ${path.relative(mobileRoot, nodeLcovPath)}.`);
      return false;
    }

    const lcov = readFileSync(nodeLcovPath, 'utf8');
    if (!/^SF:/m.test(lcov)) {
      console.error(`Coverage collection failed: ${path.relative(mobileRoot, nodeLcovPath)} has no SF: records.`);
      return false;
    }

    return true;
  }

  const cobertura = path.join(jestCoverageDir, 'cobertura-coverage.xml');
  const istanbul = path.join(jestCoverageDir, 'coverage-final.json');
  if (!existsSync(cobertura) && !existsSync(istanbul)) {
    console.error(
      `Coverage collection failed: missing Jest Cobertura/Istanbul report under ${path.relative(mobileRoot, jestCoverageDir)}.`,
    );
    return false;
  }

  return true;
}

function runSuites(suiteTs, suiteTsx) {
  if (collectCoverage) {
    rmSync(coverageRoot, { recursive: true, force: true });
    mkdirSync(coverageRoot, { recursive: true });
    console.log('Collecting coverage from the Node pure suite and the Jest component suite.');
  }

  const nodeStatus = suiteTs.length === 0 ? 0 : runNodeTest(suiteTs, { coverage: collectCoverage });
  if (nodeStatus !== 0) {
    return nodeStatus;
  }
  if (collectCoverage && suiteTs.length > 0 && !assertCoverageReports('node')) {
    return 1;
  }

  const jestStatus = suiteTsx.length === 0 ? 0 : runJest(suiteTsx, { coverage: collectCoverage });
  if (jestStatus !== 0) {
    return jestStatus;
  }
  if (collectCoverage && suiteTsx.length > 0 && !assertCoverageReports('jest')) {
    return 1;
  }
  if (collectCoverage && suiteTs.length === 0) {
    console.error('Coverage collection failed: Node *.test.ts suite is empty; refusing to drop that suite.');
    return 1;
  }
  if (collectCoverage && suiteTsx.length === 0) {
    console.error('Coverage collection failed: Jest *.test.tsx suite is empty; refusing to drop that suite.');
    return 1;
  }
  return 0;
}

const tsProbeRel = `src/discovery-probe-${process.pid}.test.ts`;
const tsxProbeRel = `src/discovery-probe-${process.pid}.test.tsx`;
const tsProbeAbs = path.join(mobileRoot, tsProbeRel);
const tsxProbeAbs = path.join(mobileRoot, tsxProbeRel);

let exitCode = 1;
try {
  writeFileSync(
    tsProbeAbs,
    `import assert from 'node:assert/strict';
import { describe, it } from 'node:test';

describe('automatic test discovery probe', () => {
  it('runs without being listed in package.json', () => {
    assert.equal(1 + 1, 2);
  });
});
`,
    'utf8',
  );
  writeFileSync(
    tsxProbeAbs,
    `describe('automatic component test discovery probe', () => {
  it('runs without being listed in package.json', () => {
    expect(1 + 1).toBe(2);
  });
});
`,
    'utf8',
  );

  const tsFiles = discover('**/*.test.ts');
  const tsxFiles = discover('**/*.test.tsx');

  if (!tsFiles.includes(tsProbeRel)) {
    console.error(`Discovery self-check failed: unlisted ${tsProbeRel} was not discovered.`);
  } else if (!tsxFiles.includes(tsxProbeRel)) {
    console.error(`Discovery self-check failed: unlisted ${tsxProbeRel} was not discovered.`);
  } else {
    const suiteTs = tsFiles.filter((file) => file !== tsProbeRel);
    const suiteTsx = tsxFiles.filter((file) => file !== tsxProbeRel);
    const probeTsStatus = runNodeTest([tsProbeRel]);
    const probeTsxStatus = runJest([tsxProbeRel]);
    if (probeTsStatus !== 0) {
      console.error('Discovery self-check failed: Node probe test did not pass.');
      exitCode = probeTsStatus;
    } else if (probeTsxStatus !== 0) {
      console.error('Discovery self-check failed: Jest probe test did not pass.');
      exitCode = probeTsxStatus;
    } else if (suiteTs.length === 0 && suiteTsx.length === 0) {
      console.error('No *.test.ts or *.test.tsx files found under src/.');
    } else {
      console.log(
        `Discovery self-check passed (${suiteTs.length} Node files, ${suiteTsx.length} Jest files; unlisted probes executed).`,
      );
      exitCode = runSuites(suiteTs, suiteTsx);
    }
  }
} finally {
  rmSync(tsProbeAbs, { force: true });
  rmSync(tsxProbeAbs, { force: true });
}

process.exit(exitCode);
