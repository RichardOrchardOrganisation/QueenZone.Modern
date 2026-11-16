import { mkdirSync, mkdtempSync, writeFileSync } from 'node:fs';
import { spawnSync } from 'node:child_process';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { test } from 'node:test';
import assert from 'node:assert/strict';
import {
  PIN_HINT,
  findRepoOffenders,
  findUbuntuLatestPlaywrightJobs,
  formatDiagnostic,
  jobInstallsPlaywright,
  jobRunsOn,
  listWorkflowJobs,
  listWorkflowSources,
  main,
  usesUbuntuLatest,
} from './check-playwright-ubuntu.mjs';
import { repoRootFrom } from './check-feature-map.mjs';

const script = fileURLToPath(new URL('./check-playwright-ubuntu.mjs', import.meta.url));

const playwrightInstallJob = `jobs:
  browser:
    name: Install Chromium
    runs-on: ubuntu-latest
    steps:
      - run: ./tests/QueenZone.Web.E2E/bin/Release/net10.0/playwright.ps1 install --with-deps chromium
`;

test('listWorkflowJobs splits top-level jobs including multiline if blocks', () => {
  const jobs = listWorkflowJobs(`jobs:
  first:
    if: |
      github.ref == 'refs/heads/main' &&
      true
    runs-on: ubuntu-24.04
  second:
    runs-on: ubuntu-latest
`);
  assert.deepEqual(
    jobs.map((job) => job.name),
    ['first', 'second'],
  );
  assert.match(jobs[0].body, /ubuntu-24.04/);
  assert.match(jobs[1].body, /ubuntu-latest/);
});

test('jobInstallsPlaywright requires both playwright and install as words', () => {
  assert.equal(
    jobInstallsPlaywright('run: playwright.ps1 install --with-deps chromium'),
    true,
  );
  assert.equal(jobInstallsPlaywright("Playwright's generated installer"), false);
  assert.equal(jobInstallsPlaywright('Upload Playwright failure artifacts'), false);
  assert.equal(jobInstallsPlaywright('npm install'), false);
});

test('usesUbuntuLatest matches scalar and list runners', () => {
  assert.equal(usesUbuntuLatest('ubuntu-latest'), true);
  assert.equal(usesUbuntuLatest('[ubuntu-latest]'), true);
  assert.equal(usesUbuntuLatest('ubuntu-24.04'), false);
  assert.equal(usesUbuntuLatest('[self-hosted, queenzone]'), false);
});

test('jobRunsOn reads a four-space runs-on line', () => {
  assert.equal(jobRunsOn('  browser:\n    runs-on: ubuntu-24.04\n'), 'ubuntu-24.04');
  assert.equal(
    jobRunsOn('  browser:\n    runs-on: [self-hosted, e2e]\n'),
    '[self-hosted, e2e]',
  );
});

test('findUbuntuLatestPlaywrightJobs flags ubuntu-latest Playwright installs', () => {
  assert.deepEqual(findUbuntuLatestPlaywrightJobs(playwrightInstallJob, 'ci.yml'), [
    { file: 'ci.yml', job: 'browser', runsOn: 'ubuntu-latest' },
  ]);
});

test('findUbuntuLatestPlaywrightJobs accepts a pinned Ubuntu 24.04 job', () => {
  const pinned = playwrightInstallJob.replace('ubuntu-latest', 'ubuntu-24.04');
  assert.deepEqual(findUbuntuLatestPlaywrightJobs(pinned, 'ci.yml'), []);
});

test('findUbuntuLatestPlaywrightJobs ignores Playwright jobs that are not on ubuntu-latest', () => {
  const selfHosted = `jobs:
  e2e:
    runs-on: [self-hosted, e2e]
    steps:
      - name: Run Playwright e2e tests
        run: ./scripts/Run-E2E.ps1 -Mode Deterministic
`;
  assert.deepEqual(findUbuntuLatestPlaywrightJobs(selfHosted, 'ci.yml'), []);
});

test('findUbuntuLatestPlaywrightJobs ignores ubuntu-latest jobs that do not install Playwright', () => {
  const proof = `jobs:
  gate-proof:
    runs-on: ubuntu-latest
    steps:
      - run: node scripts/release/write-gate-proof.mjs
`;
  assert.deepEqual(findUbuntuLatestPlaywrightJobs(proof, 'dev-journey-e2e.yml'), []);
});

test('formatDiagnostic names the job and the pin fix', () => {
  assert.equal(
    formatDiagnostic({ file: '.github/workflows/dev-journey-e2e.yml', job: 'dev-journey', runsOn: 'ubuntu-latest' }),
    `QZ-PLAYWRIGHT-UBUNTU: .github/workflows/dev-journey-e2e.yml job dev-journey installs Playwright on ubuntu-latest; ${PIN_HINT}`,
  );
});

test('tracked workflows do not install Playwright on ubuntu-latest', () => {
  assert.deepEqual(findRepoOffenders(repoRootFrom()), []);
  assert.equal(main({ root: repoRootFrom(), log() {}, error() {} }), 0);
});

test('hosted Playwright install jobs stay visible to the ubuntu-latest guard', () => {
  const expected = [
    ['.github/workflows/dev-journey-e2e.yml', 'dev-journey'],
    ['.github/workflows/dev-member-auth-e2e.yml', 'member-auth'],
    ['.github/workflows/refresh-dev-snapshot.yml', 'refresh'],
  ];
  const sources = listWorkflowSources(repoRootFrom());
  for (const [file, jobName] of expected) {
    const workflow = sources.find((entry) => entry.file === file);
    assert.ok(workflow, file);
    const job = listWorkflowJobs(workflow.source).find((entry) => entry.name === jobName);
    assert.ok(job, jobName);
    assert.equal(jobInstallsPlaywright(job.body), true, file);
    assert.equal(usesUbuntuLatest(jobRunsOn(job.body)), false, file);
    assert.match(jobRunsOn(job.body), /ubuntu-24\.04/);
  }
});

test('CLI fails closed on an ubuntu-latest Playwright install job', () => {
  const dir = mkdtempSync(path.join(tmpdir(), 'qz-pw-ubuntu-'));
  mkdirSync(path.join(dir, '.github', 'workflows'), { recursive: true });
  writeFileSync(path.join(dir, '.github', 'workflows', 'broken.yml'), playwrightInstallJob);
  const errors = [];
  assert.equal(main({ root: dir, log() {}, error: (line) => errors.push(line) }), 1);
  assert.deepEqual(errors, [
    `QZ-PLAYWRIGHT-UBUNTU: .github/workflows/broken.yml job browser installs Playwright on ubuntu-latest; ${PIN_HINT}`,
  ]);

  const result = spawnSync(process.execPath, [script], { cwd: dir, encoding: 'utf8' });
  assert.equal(result.status, 1);
  assert.match(result.stderr, /QZ-PLAYWRIGHT-UBUNTU: \.github\/workflows\/broken\.yml job browser/);
});
