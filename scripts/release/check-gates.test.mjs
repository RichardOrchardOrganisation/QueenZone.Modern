import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { mkdtempSync, readFileSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { test } from 'node:test';
import { fileURLToPath } from 'node:url';
import {
  DEFAULT_MAX_AGE_HOURS,
  GATES,
  GATE_JOB_NAME,
  OVERRIDE_MIN_LENGTH,
  buildOverrideComment,
  checkGates,
  runOverrideComment,
  createStoredZip,
  evaluateChanges,
  evaluateGate,
  evaluateRun,
  extractGateProofFromZip,
  extractZipFiles,
  findNamedJob,
  formatDispatchCommands,
  formatFailure,
  isVersionTagRef,
  main,
  normalizeSha,
  parseCliArgs,
  parseGateProof,
  parseMaxAgeHours,
  validateOverride,
} from './check-gates.mjs';
import {
  DEV_PUBLIC_URL,
  buildGateProof,
  extractBuildStamp,
  parseWriteProofArgs,
  requireMatchingStamps,
  writeGateProofFromArgs,
} from './write-gate-proof.mjs';

const repoRoot = fileURLToPath(new URL('../..', import.meta.url));
const sha = 'aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa';
const other = 'bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb';
const proof = { tested_sha: sha, full_suite: true };

function readWorkflow(name) {
  return readFileSync(path.join(repoRoot, '.github/workflows', name), 'utf8');
}

function fakeGithub({ runs = {}, jobs = {}, proofs = {}, comments = [], resolved } = {}) {
  return {
    comments,
    async resolveSha(value) {
      return resolved || normalizeSha(value).padEnd(40, 'a').slice(0, 40);
    },
    async listWorkflowRuns(workflowFile, headSha) {
      return (runs[workflowFile] || []).filter((run) => run.head_sha === headSha);
    },
    async listJobs(runId) {
      return jobs[runId] || [];
    },
    async readGateProof(runId) {
      if (Object.prototype.hasOwnProperty.call(proofs, runId)) {
        return proofs[runId];
      }
      return proof;
    },
    async commentOnReleaseLog(body) {
      comments.push(body);
    },
  };
}

test('changes outputs fail closed when missing or unknown', () => {
  assert.throws(() => evaluateChanges('', 'false'), /Missing `changes` outputs/);
  assert.throws(() => evaluateChanges('true', undefined), /Missing `changes` outputs/);
  assert.throws(() => evaluateChanges('yes', 'false'), /Unknown changes.code/);
  assert.throws(() => evaluateChanges('false', 'maybe'), /Unknown changes.migrations/);
});

test('docs-, mobile- and workflow-only ranges do not require gates', () => {
  assert.deepEqual(evaluateChanges('false', 'false'), {
    needed: false,
    reason: 'Docs-, mobile- or workflow-only range; tip gates are not required.',
  });
  assert.equal(evaluateChanges('true', 'false').needed, true);
  assert.equal(evaluateChanges('false', 'true').needed, true);
});

function classifyPaths(paths) {
  const result = spawnSync(
    'bash',
    [path.join(repoRoot, 'scripts/classify-pipeline-changes.sh')],
    { input: `${paths.join('\n')}\n`, encoding: 'utf8' },
  );
  assert.equal(result.status, 0, result.stderr);
  const flags = {};
  for (const line of result.stdout.split('\n')) {
    const match = /^(code|migrations)=(true|false)$/.exec(line);
    if (match) {
      flags[match[1]] = match[2];
    }
  }
  return flags;
}

test('evaluateChanges skips exactly the ranges Deploy-dev skips', async () => {
  const skipRanges = [
    ['docs/architecture/testing-policy.md'],
    ['src/QueenZone.Mobile/App.tsx'],
    ['.github/workflows/deploy.yml'],
  ];
  for (const paths of skipRanges) {
    const flags = classifyPaths(paths);
    assert.equal(flags.code, 'false', paths.join(','));
    assert.equal(flags.migrations, 'false', paths.join(','));
    assert.equal(evaluateChanges(flags.code, flags.migrations).needed, false);
  }

  const deployFlags = classifyPaths(['src/QueenZone.Web/Program.cs']);
  assert.equal(deployFlags.code, 'true');
  assert.equal(evaluateChanges(deployFlags.code, deployFlags.migrations).needed, true);

  const skipped = await checkGates({
    sha,
    changesCode: classifyPaths(['docs/architecture/testing-policy.md']).code,
    changesMigrations: classifyPaths(['docs/architecture/testing-policy.md']).migrations,
    eventName: 'push',
    ref: 'refs/tags/v1',
  }, fakeGithub());
  assert.equal(skipped.skipped, true);
});

test('version tag refs and override validation follow Bob\'s rules', () => {
  assert.equal(isVersionTagRef('refs/tags/v2026.10.13.2'), true);
  assert.equal(isVersionTagRef('refs/heads/main'), false);
  assert.throws(
    () => validateOverride({ reason: 'x'.repeat(OVERRIDE_MIN_LENGTH), ref: 'refs/tags/v1', eventName: 'push' }),
    /Tag pushes cannot override/,
  );
  assert.throws(
    () => validateOverride({ reason: 'x'.repeat(OVERRIDE_MIN_LENGTH), ref: 'refs/heads/main', eventName: 'workflow_dispatch' }),
    /v\* tag refs only/,
  );
  assert.throws(
    () => validateOverride({ reason: 'too short', ref: 'refs/tags/v1', eventName: 'workflow_dispatch' }),
    /at least 20/,
  );
  const reason = 'Emergency rollback after a bad health check.';
  assert.equal(validateOverride({ reason, ref: 'refs/tags/v1.2.3', eventName: 'workflow_dispatch' }), reason);
});

test('override comment includes the reason without interpolating it into a command', () => {
  const reason = 'Ship the previous tag because DEV journey is blocked on snapshot refresh.';
  const body = buildOverrideComment({
    reason,
    sha,
    ref: 'refs/tags/v2026.10.13.2',
    runUrl: 'https://github.com/org/repo/actions/runs/1',
    actor: 'richardorchard',
  });
  assert.match(body, /production/i);
  assert.match(body, new RegExp(sha));
  assert.match(body, /Reason:\r?\n/);
  assert.ok(body.includes(reason));
});

test('gate-proof artifact parsing rejects narrowed or malformed proofs', () => {
  assert.deepEqual(parseGateProof(JSON.stringify(proof)), proof);
  assert.throws(() => parseGateProof('{"tested_sha":"abc","full_suite":true}'), /40-character/);
  assert.throws(() => parseGateProof('{"tested_sha":"' + sha + '","full_suite":"true"}'), /boolean/);
});

test('zip round-trip extracts the gate-proof JSON', () => {
  const zip = createStoredZip('gate-proof.json', JSON.stringify(proof));
  assert.equal(extractZipFiles(zip).get('gate-proof.json').toString(), JSON.stringify(proof));
  assert.deepEqual(extractGateProofFromZip(zip), proof);
});

test('named job must have succeeded on the latest attempt', async () => {
  const now = () => Date.parse('2026-10-13T12:00:00Z');
  const context = { maxAgeHours: 72, now };
  const completed = {
    name: GATE_JOB_NAME,
    status: 'completed',
    conclusion: 'success',
    completed_at: '2026-10-13T11:00:00Z',
  };
  const skipped = {
    name: GATE_JOB_NAME,
    status: 'completed',
    conclusion: 'skipped',
    completed_at: '2026-10-13T11:00:00Z',
  };
  const ok = await evaluateRun(
    GATES[0],
    { id: 1, head_sha: sha, jobs: [completed], proof, updated_at: '2026-10-13T11:00:00Z' },
    sha,
    context,
  );
  assert.equal(ok.ok, true);
  const skippedRun = await evaluateRun(
    GATES[0],
    { id: 2, head_sha: sha, jobs: [skipped], proof, updated_at: '2026-10-13T11:00:00Z' },
    sha,
    context,
  );
  assert.equal(skippedRun.ok, false);
  assert.match(skippedRun.error, /skipped/);
  const ancestor = await evaluateRun(
    GATES[0],
    { id: 3, head_sha: other, jobs: [completed], proof, updated_at: '2026-10-13T11:00:00Z' },
    sha,
    context,
  );
  assert.equal(ancestor.ok, false);
  assert.match(ancestor.error, /is not/);
  const stale = await evaluateRun(
    GATES[0],
    { id: 4, head_sha: sha, jobs: [{ ...completed, completed_at: '2026-10-01T00:00:00Z' }], proof },
    sha,
    context,
  );
  assert.equal(stale.ok, false);
  assert.match(stale.error, /older than 72h/);
  const narrowed = await evaluateRun(
    GATES[0],
    { id: 5, head_sha: sha, jobs: [completed], proof: { tested_sha: sha, full_suite: false } },
    sha,
    context,
  );
  assert.equal(narrowed.ok, false);
  assert.match(narrowed.error, /full suite/);
  const mismatch = await evaluateRun(
    GATES[0],
    { id: 6, head_sha: sha, jobs: [completed], proof: { tested_sha: other, full_suite: true } },
    sha,
    context,
  );
  assert.equal(mismatch.ok, false);
  assert.match(mismatch.error, /tested_sha/);
});

test('findNamedJob ignores a successful run whose gate job is missing', () => {
  assert.equal(findNamedJob([{ name: 'ui-e2e-realdata', conclusion: 'success' }]), null);
  assert.equal(findNamedJob([{ name: GATE_JOB_NAME, conclusion: 'success' }]).name, GATE_JOB_NAME);
});

test('checkGates skips when changes says no migrate or deploy', async () => {
  const result = await checkGates({
    sha,
    changesCode: 'false',
    changesMigrations: 'false',
    eventName: 'push',
    ref: 'refs/tags/v1',
  }, fakeGithub());
  assert.equal(result.skipped, true);
});

test('checkGates fails closed on an ungated SHA and prints dispatch commands', async () => {
  await assert.rejects(
    () => checkGates({
      sha: 'bfa09386',
      changesCode: 'true',
      changesMigrations: 'true',
      eventName: 'push',
      ref: 'refs/tags/v2026.10.13.2',
    }, fakeGithub({ resolved: 'bfa09386aaaaaaaaaaaaaaaaaaaaaaaaaaaaaa' })),
    (error) => {
      assert.match(error.message, /Production tip gates failed/);
      assert.match(error.message, /gh workflow run nightly-legacy-checks.yml --ref main/);
      assert.match(error.message, /gh workflow run dev-member-auth-e2e.yml --ref main/);
      assert.match(error.message, /gh workflow run dev-journey-e2e.yml --ref main/);
      assert.doesNotMatch(error.message, /skip_sync=true/);
      return true;
    },
  );
});

test('checkGates requires exact SHA runs with a successful latest gate-proof job', async () => {
  const now = () => Date.parse('2026-10-13T12:00:00Z');
  const run = {
    id: 99,
    head_sha: sha,
    head_branch: 'main',
    created_at: '2026-10-13T11:00:00Z',
    updated_at: '2026-10-13T11:30:00Z',
  };
  const github = fakeGithub({
    resolved: sha,
    runs: {
      'nightly-legacy-checks.yml': [run],
      'dev-member-auth-e2e.yml': [{ ...run, id: 100 }],
      'dev-journey-e2e.yml': [{ ...run, id: 101 }],
    },
    jobs: {
      99: [{ name: GATE_JOB_NAME, status: 'completed', conclusion: 'success', completed_at: '2026-10-13T11:30:00Z' }],
      100: [{ name: GATE_JOB_NAME, status: 'completed', conclusion: 'success', completed_at: '2026-10-13T11:30:00Z' }],
      101: [{ name: GATE_JOB_NAME, status: 'completed', conclusion: 'success', completed_at: '2026-10-13T11:30:00Z' }],
    },
  });
  const result = await checkGates({
    sha,
    changesCode: 'true',
    changesMigrations: 'false',
    eventName: 'push',
    ref: 'refs/tags/v1',
    now,
  }, github);
  assert.equal(result.ok, true);
  assert.equal(result.sha, sha);
});

test('evaluateGate judges only the newest run for the SHA', async () => {
  const now = () => Date.parse('2026-10-13T12:00:00Z');
  const olderGreen = {
    id: 10,
    head_sha: sha,
    head_branch: 'main',
    created_at: '2026-10-13T10:00:00Z',
  };
  const newerRed = {
    id: 11,
    head_sha: sha,
    head_branch: 'main',
    created_at: '2026-10-13T11:00:00Z',
  };
  const github = fakeGithub({
    resolved: sha,
    runs: {
      'nightly-legacy-checks.yml': [olderGreen, newerRed],
    },
    jobs: {
      10: [{ name: GATE_JOB_NAME, status: 'completed', conclusion: 'success', completed_at: '2026-10-13T10:30:00Z' }],
      11: [{ name: GATE_JOB_NAME, status: 'completed', conclusion: 'failure', completed_at: '2026-10-13T11:30:00Z' }],
    },
    proofs: {
      10: proof,
      11: proof,
    },
  });
  const verdict = await evaluateGate(GATES[0], sha, { maxAgeHours: 72, now }, github);
  assert.equal(verdict.ok, false);
  assert.match(verdict.error, /11/);
  assert.match(verdict.error, /failure/);
  assert.doesNotMatch(verdict.error, /run 10 /);
});

test('a conclusion=success run whose jobs were all skipped does not count', async () => {
  const now = () => Date.parse('2026-10-13T12:00:00Z');
  const run = {
    id: 7,
    head_sha: sha,
    head_branch: 'main',
    conclusion: 'success',
    created_at: '2026-10-13T11:00:00Z',
  };
  const github = fakeGithub({
    resolved: sha,
    runs: {
      'nightly-legacy-checks.yml': [run],
      'dev-member-auth-e2e.yml': [],
      'dev-journey-e2e.yml': [],
    },
    jobs: {
      7: [{ name: GATE_JOB_NAME, status: 'completed', conclusion: 'skipped', completed_at: '2026-10-13T11:00:00Z' }],
    },
  });
  await assert.rejects(
    () => checkGates({
      sha,
      changesCode: 'true',
      changesMigrations: 'true',
      eventName: 'push',
      ref: 'refs/tags/v1',
      now,
    }, github),
    /skipped on the latest attempt/,
  );
});

test('workflow_dispatch from main is rejected unless gate_only', async () => {
  await assert.rejects(
    () => checkGates({
      sha,
      changesCode: 'true',
      changesMigrations: 'false',
      eventName: 'workflow_dispatch',
      ref: 'refs/heads/main',
    }, fakeGithub()),
    /must use a v\* tag ref/,
  );
  const skipped = await checkGates({
    sha,
    changesCode: 'false',
    changesMigrations: 'false',
    eventName: 'workflow_dispatch',
    ref: 'refs/heads/main',
    gateOnly: true,
  }, fakeGithub());
  assert.equal(skipped.skipped, true);
});

test('CLI parsing supports --sha and gate_only', () => {
  assert.deepEqual(parseCliArgs(['--sha', 'bfa09386', '--gate-only']), {
    sha: 'bfa09386',
    gateOnly: true,
    validateOverride: false,
    commentOverride: false,
  });
  assert.equal(parseMaxAgeHours(undefined), DEFAULT_MAX_AGE_HOURS);
  assert.equal(parseMaxAgeHours('48'), 48);
  assert.throws(() => parseMaxAgeHours('0'), /positive/);
  assert.match(formatDispatchCommands(), /nightly-legacy-checks.yml/);
  assert.match(formatFailure(['nightly: missing']), /Production tip gates failed/);
});

test('main --validate-override and --comment-override read the reason from env', async () => {
  const reason = 'Need to redeploy the previous tag after a health regression.';
  const comments = [];
  const env = {
    GATE_OVERRIDE_REASON: reason,
    GITHUB_REF: 'refs/tags/v2026.10.13.2',
    GITHUB_EVENT_NAME: 'workflow_dispatch',
    GITHUB_SHA: sha,
    GITHUB_ACTOR: 'richardorchard',
  };
  const validated = await main(['--validate-override'], env);
  assert.equal(validated.ok, true);

  const directory = mkdtempSync(path.join(tmpdir(), 'qz-gate-summary-'));
  try {
    const summary = path.join(directory, 'summary.md');
    const github = fakeGithub({ comments });
    await runOverrideComment({
      reason,
      ref: 'refs/tags/v2026.10.13.2',
      eventName: 'workflow_dispatch',
      sha,
      runUrl: 'https://github.com/org/repo/actions/runs/1',
      actor: 'dinesh',
      summaryPath: summary,
    }, github);
    assert.equal(comments.length, 1);
    assert.ok(comments[0].includes(reason));
    assert.ok(readFileSync(summary, 'utf8').includes(reason));
  } finally {
    rmSync(directory, { recursive: true, force: true });
  }
});

test('DEV tested_sha comes from the live build stamp', async () => {
  assert.equal(extractBuildStamp('<div data-build-version="abcdef1"></div>'), 'abcdef1');
  assert.equal(requireMatchingStamps('abc', 'abc'), 'abc');
  assert.throws(() => requireMatchingStamps('abc', 'def'), /changed during the tests/);
  assert.equal(parseWriteProofArgs(['--full-suite', 'false']).fullSuite, false);
  assert.throws(() => buildGateProof({ testedSha: 'short', fullSuite: true }), /40-character/);
  const printed = await writeGateProofFromArgs(
    ['--print-stamp', '--require-match', sha.slice(0, 7)],
    { DEV_PUBLIC_URL },
    async () => ({ ok: true, text: async () => `<div data-build-version="${sha.slice(0, 7)}"></div>` }),
  );
  assert.equal(printed.stamp, sha.slice(0, 7));
});

test('DEV publish requires the exact commit and retains the freshly built fallback stamp', () => {
  const deploy = readWorkflow('deploy-dev.yml');
  const find = deploy.split('      - name: Find matching CI run')[1].split('      - name: Choose build stamp')[0];
  assert.match(find, /REQUIRE_EXACT_SHA: 'true'/);
  assert.match(find, /RELEASE_SHA: \$\{\{ steps\.sha\.outputs\.release_sha \}\}/);
  assert.match(find, /bash \.\/scripts\/Resolve-CiPublishRun\.sh/);
  const publish = deploy.split('      - name: Publish website from checkout')[1].split('      - name: Fetch Bitwarden')[0];
  assert.match(publish, /needs\.resolve-ci-run\.outputs\.found != 'true'/);
  assert.match(publish, /CHECKOUT_SHA=\$\(git rev-parse HEAD\)/);
  assert.match(publish, /SHORT="\$\{CHECKOUT_SHA:0:7\}"/);
  assert.match(publish, /dotnet publish[\s\S]*OverrideGitCommitShort="\$SHORT"/);
});

test('deploy.yml gate job sits after changes, fail-closed, with no polling', () => {
  const deploy = readWorkflow('deploy.yml');
  assert.match(deploy, /gate_override_reason:/);
  assert.match(deploy, /gate_only:/);
  assert.match(deploy, / {2}check-gates:/);
  assert.match(deploy, /needs: changes/);
  assert.match(deploy, /actions: read/);
  assert.match(deploy, /prod-gate-override/);
  assert.match(deploy, /issues: write/);
  assert.match(deploy, /GATE_OVERRIDE_REASON: \$\{{\s*inputs\.gate_override_reason\s*\}\}/);
  assert.doesNotMatch(deploy, /run:[\s\S]{0,200}\$\{{\s*inputs\.gate_override_reason/);
  assert.doesNotMatch(deploy.split('check-gates:')[1].split('\n  ')[0], /sleep |poll/i);
  assert.match(deploy, /node scripts\/release\/check-gates\.mjs/);
  assert.match(deploy, /inputs\.gate_only != true/);
  assert.match(deploy, /needs\.check-gates\.result == 'success'/);
  assert.match(deploy, /#2222|#2291|RELEASE_LOG_ISSUE|issues\/2222/);
  assert.match(deploy, /Re-run failed jobs/);
});

test('DEV e2e workflows keep their crons and trigger after a successful deploy-dev', () => {
  for (const file of ['dev-member-auth-e2e.yml', 'dev-journey-e2e.yml']) {
    const workflow = readWorkflow(file);
    assert.match(workflow, /cron: "0 7 \* \* \*"/);
    assert.match(workflow, /workflow_run:/);
    assert.match(workflow, /Deploy dev/);
    assert.match(workflow, /types:\r?\n\s+- completed/);
    assert.match(workflow, /workflow_run\.conclusion == 'success'/);
    assert.match(workflow, /workflow_run\.head_branch == 'main'/);
    assert.match(workflow, /workflow_run\.event == 'push'/);
    assert.match(workflow, / {2}gate-proof:/);
    assert.match(workflow, /write-gate-proof\.mjs --stamp/);
    assert.match(workflow, /GITHUB_TOKEN: \$\{{\s*secrets\.GITHUB_TOKEN\s*\}\}/);
    assert.match(workflow, /GITHUB_REPOSITORY: \$\{{\s*github\.repository\s*\}\}/);
    assert.match(workflow, /id: proof/);
    assert.match(workflow, /steps\.proof\.outputs\.wrote == 'true'/);
    assert.match(workflow, /--print-stamp/);
    assert.match(workflow, /--require-match/);
    assert.match(workflow, /name: gate-proof/);
    assert.match(workflow, /outputs\.stamp != ''/);
    // Checkout depth is workflow configuration, not an implementation detail:
    // the proof job must have ancestors even on a fresh hosted runner (#2354).
    assert.match(workflow.split('  gate-proof:')[1], /fetch-depth: 0/);
    assert.doesNotMatch(workflow.split('jobs:')[0], /id-token:\s*write/);
    assert.match(workflow, /permissions:\r?\n\s+contents: read\r?\n\s+id-token: write/);
  }
});

test('nightly writes gate-proof and does not count skip_sync or category_filter as a full suite', () => {
  const nightly = readWorkflow('nightly-legacy-checks.yml');
  assert.match(nightly, / {2}gate-proof:/);
  assert.match(nightly, /name: gate-proof/);
  assert.match(nightly, /write-gate-proof\.mjs/);
  assert.match(nightly, /GATE_FULL_SUITE/);
  assert.match(nightly, /inputs\.skip_sync/);
  assert.match(nightly, /inputs\.category_filter/);
  assert.match(nightly, /needs: \[sync-legacy-db, apply-ef-migrations-mirror, legacy-read-probes, legacy-write-probes, ui-e2e-realdata, residue-check\]/);
});

test('runbook and environment docs exist and are linked', () => {
  const runbook = readFileSync(path.join(repoRoot, 'docs/release/README.md'), 'utf8');
  for (const heading of [
    '## Scope',
    '## Three tip gates',
    '## Tag',
    '## Deploy',
    '## OpenTofu',
    '## Post-deploy health',
    '## Log it',
    '## Rollback',
    '## Roles',
  ]) {
    assert.match(runbook, new RegExp(heading));
  }
  assert.match(runbook, /#2170/);
  assert.doesNotMatch(runbook, /#2173/);
  assert.match(runbook, /gate_override_reason/);
  assert.match(runbook, /prod-gate-override/);
  assert.match(runbook, /richardorchard/);
  assert.match(runbook, /Prevent self-review/);
  assert.match(runbook, /Do not dispatch an old tag/);
  assert.doesNotMatch(runbook, /gh workflow run deploy\.yml --ref vPREVIOUS/);
  const agents = readFileSync(path.join(repoRoot, 'AGENTS.md'), 'utf8');
  const readme = readFileSync(path.join(repoRoot, 'README.md'), 'utf8');
  const testing = readFileSync(path.join(repoRoot, 'docs/architecture/testing-policy.md'), 'utf8');
  const environments = readFileSync(path.join(repoRoot, 'docs/architecture/github-environments.md'), 'utf8');
  for (const text of [agents, readme, testing, environments]) {
    assert.match(text, /docs\/release\/README\.md/);
  }
  assert.match(environments, /prod-gate-override/);
});

const githubArtifactZip = readFileSync(
  path.join(path.dirname(fileURLToPath(import.meta.url)), 'fixtures', 'github-artifact-gate-proof.zip'),
);

test('real GitHub artifact zip (data descriptor, deflate) extracts gate-proof.json', () => {
  assert.equal(githubArtifactZip.readUInt16LE(6) & 0x8, 0x8, 'fixture should set data-descriptor flag');
  assert.equal(githubArtifactZip.readUInt32LE(18), 0, 'fixture local header size should be zero');
  assert.deepEqual(extractGateProofFromZip(githubArtifactZip), {
    tested_sha: 'e0f6ad4648c44fc080d5d12b6fb99091ae2a2483',
    full_suite: true,
  });
});

test('stored zip without data descriptor still extracts', () => {
  const zip = createStoredZip('gate-proof.json', '{"a":1}');
  assert.equal(zip.readUInt16LE(6) & 0x8, 0);
  assert.equal(extractZipFiles(zip).get('gate-proof.json').toString(), '{"a":1}');
});

test('zip with corrupted data fails CRC check', () => {
  const zip = Buffer.from(createStoredZip('gate-proof.json', '{"a":1}'));
  zip[30 + 'gate-proof.json'.length] ^= 0xff;
  assert.throws(() => extractZipFiles(zip), /CRC mismatch/);
});

test('unsupported compression is rejected', () => {
  const zip = Buffer.from(createStoredZip('gate-proof.json', '{"a":1}'));
  const central = zip.indexOf(Buffer.from([0x50, 0x4b, 0x01, 0x02]));
  zip.writeUInt16LE(12, central + 10);
  assert.throws(() => extractZipFiles(zip), /Unsupported zip compression 12/);
});

test('non-zip input is rejected', () => {
  assert.throws(() => extractZipFiles(Buffer.from('not a zip')), /end of central directory/);
});
