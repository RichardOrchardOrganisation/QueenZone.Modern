import assert from 'node:assert/strict';
import { readFileSync, mkdtempSync, writeFileSync, rmSync, realpathSync } from 'node:fs';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { spawnSync } from 'node:child_process';
import { test } from 'node:test';

const workflow = readFileSync(new URL('../.github/workflows/nightly-legacy-checks.yml', import.meta.url), 'utf8');
const probes = [
  ['PrivateMessaging', 'RUN_PRIVATE_MESSAGE_PROBE'],
  ['ForumWrites', 'RUN_FORUM_WRITE_PROBE'],
  ['AdminNewsLegacyWrites', 'RUN_LEGACY_WRITE_PROBE'],
  ['ContentSubmissions', 'RUN_CONTENT_SUBMISSION_PROBE'],
  ['MemberAccounts', 'RUN_MEMBER_ACCOUNT_PROBE'],
  ['NewsAgentUrlIngestion', 'RUN_NEWS_AGENT_URL_INGESTION_PROBE'],
];

for (const [probe, flag] of probes) {
  test(`${probe} forwards TRX arguments and preserves a failing test result offline`, () => {
    const directory = mkdtempSync(path.join(tmpdir(), 'qz-probe-report-'));
    try {
      const source = readFileSync(new URL(`./Probe-${probe}.ps1`, import.meta.url), 'utf8');
      writeFileSync(path.join(directory, `Probe-${probe}.ps1`), source);
      // Run a copy with a stub guard and a function named dotnet: no real SQL,
      // native test runner, host, credential import, or process cleanup is used.
      writeFileSync(path.join(directory, 'Assert-SqlExpressMirrorConnection.ps1'), 'param([string]$ConnectionString)\n');
      const command = `
        function dotnet { Write-Output ('CAPTURE=' + (ConvertTo-Json -Compress -InputObject @($args))); $global:LASTEXITCODE = 7 }
        & './Probe-${probe}.ps1'
        exit $LASTEXITCODE
      `;
      const result = spawnSync('pwsh', ['-NoProfile', '-Command', command], {
        cwd: directory, encoding: 'utf8', timeout: 15000,
        env: { ...process.env, [flag]: 'true', ConnectionStrings__QueenZoneLegacy: 'Server=localhost\\SQLEXPRESS;Database=queenzone_legacy_sync;Integrated Security=True' },
      });
      assert.equal(result.error, undefined);
      assert.notEqual(result.status, 0, 'A failed test must remain a failure.');
      const captured = result.stdout.split(/\r?\n/).find((line) => line.startsWith('CAPTURE='));
      assert.ok(captured, result.stderr);
      const args = JSON.parse(captured.slice('CAPTURE='.length));
      assert.equal(args[0], 'test');
      assert.match(args[args.indexOf('--logger') + 1], /^trx;LogFilePrefix=.+/);
      assert.equal(path.resolve(args[args.indexOf('--results-directory') + 1]), path.resolve(realpathSync(directory), '../test-results/legacy-probes'));
      assert.ok(args.includes('--filter'));
    } finally {
      rmSync(directory, { recursive: true, force: true });
    }
  });
}

test('E2E forwards TRX options before NUnit settings without starting a host', () => {
  const source = readFileSync(new URL('./Run-E2E.ps1', import.meta.url), 'utf8');
  const start = source.indexOf('    $testArgs = @(');
  const end = source.indexOf('    & dotnet @testArgs', start);
  assert.ok(start > 0 && end > start);
  const result = spawnSync('pwsh', ['-NoProfile', '-Command', `
    $e2eProject = 'unused.csproj'; $Configuration = 'Release'; $Mode = 'RealData'
    $testFilter = 'TestCategory=RealData'; $artifactDir = 'test-results/e2e'
    $NoBuild = $false; $NoRestore = $false; $repoRoot = 'offline-root'
    ${source.slice(start, end)}
    Write-Output ('CAPTURE=' + (ConvertTo-Json -Compress -InputObject @($testArgs)))
  `], { encoding: 'utf8', timeout: 15000 });
  assert.equal(result.status, 0, result.stderr);
  const args = JSON.parse(result.stdout.split(/\r?\n/).find((line) => line.startsWith('CAPTURE=')).slice(8));
  assert.equal(args[args.indexOf('--logger') + 1], 'trx;LogFilePrefix=e2e-RealData');
  assert.equal(path.normalize(args[args.indexOf('--results-directory') + 1]), path.join('offline-root', 'test-results/local-trx/e2e'));
  assert.notEqual(args[args.indexOf('--results-directory') + 1], 'test-results/e2e');
  assert.ok(args.indexOf('--logger') < args.indexOf('--'));
  assert.equal(args.at(-1), 'NUnit.NumberOfTestWorkers=1');
});

test('residue inspection waits for UI and migrated schema, including failed or cancelled probes', () => {
  const residue = workflow.split('  residue-check:')[1];
  assert.match(residue, /needs: \[apply-ef-migrations-mirror, legacy-write-probes, ui-e2e-realdata\]/);
  const condition = /^    if: (.+)$/m.exec(residue)?.[1];
  assert.ok(condition);
  // Evaluate the exact workflow condition over its dependency outcomes.
  const jsCondition = condition.replace('always()', 'true')
    .replaceAll('needs.apply-ef-migrations-mirror.result', 'migration')
    .replaceAll('needs.legacy-write-probes.result', 'writes');
  const enabled = new Function('migration', 'writes', `return ${jsCondition};`);
  for (const writes of ['success', 'failure', 'cancelled']) assert.equal(enabled('success', writes), true);
  for (const migration of ['failure', 'cancelled', 'skipped']) assert.equal(enabled(migration, 'failure'), false);
  assert.equal(enabled('success', 'skipped'), false);
  assert.match(residue, /ConnectionStrings__QueenZoneLegacy: "Server=localhost\\\\SQLEXPRESS;Database=queenzone_legacy_sync;Integrated Security=True;TrustServerCertificate=True"/);
  assert.ok(residue.indexOf('Assert-SqlExpressMirrorConnection.ps1') < residue.indexOf('dotnet test'));
  assert.match(residue, /--filter "FullyQualifiedName=QueenZone.Web.Tests.EfLegacyProbeResidueTests.Known_probe_and_web_test_markers_are_absent_when_check_enabled"/);
});

test('both OS suites retain coverage while shared-mirror writes run one suite at a time', () => {
  const suite = workflow.split('  ui-e2e-realdata:')[1].split('  residue-check:')[0];
  assert.match(suite, /max-parallel: 1/);
  assert.match(suite, /fail-fast: false/);
  assert.match(suite, /os: Windows/);
  assert.match(suite, /os: macOS/);
  assert.match(suite, /timeout-minutes: 45/);
  assert.match(suite, /needs.legacy-write-probes.result == 'success'/);
});

test('the residue connection guard accepts the initialized mirror and rejects other targets without connecting', () => {
  const guard = new URL('./Assert-SqlExpressMirrorConnection.ps1', import.meta.url).pathname;
  for (const [connection, status] of [
    ['Server=localhost\\SQLEXPRESS;Database=queenzone_legacy_sync;Integrated Security=True', 0],
    ['Server=localhost\\SQLEXPRESS;Database=wrong_database;Integrated Security=True', 1],
    ['Server=example.database.windows.net;Database=queenzone_legacy_sync;Integrated Security=True', 1],
  ]) {
    const result = spawnSync('pwsh', ['-NoProfile', '-File', guard, '-ConnectionString', connection], {
      encoding: 'utf8', timeout: 15000, env: { ...process.env, SQLEXPRESS_LAN_ADDRESS: '' },
    });
    assert.equal(result.status, status, result.stderr);
  }
});

test('reports are uploaded after failures with unique attempt names and test-only paths', () => {
  const uploads = workflow.split('      - name: Upload test reports').slice(1);
  assert.equal(uploads.length, 4);
  for (const upload of uploads) {
    const block = upload.split(/\n  [a-z]/)[0];
    assert.match(block, /if: always\(\)/);
    assert.match(block, /steps.safe-test-reports(?:-windows|-macos)?\.outcome == 'success'/);
    assert.match(block, /github.run_attempt/);
    assert.match(block, /path: test-results\/safe-reports\/results\.json/);
    assert.match(block, /retention-days: 14/);
    assert.doesNotMatch(block, /\.log|\.env|appsettings|continue-on-error/);
  }
});

test('safe report export excludes private values, display names, logs and connection strings', () => {
  const directory = mkdtempSync(path.join(tmpdir(), 'qz-safe-reports-'));
  try {
    const fixture = `<TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">
      <TestDefinitions><UnitTest id="one"><TestMethod className="QueenZone.Web.Tests.ProbeTests" name="Fails_on_timeout"/></UnitTest></TestDefinitions>
      <Results><UnitTestResult testId="one" testName="PRIVATE_DISPLAY_SENTINEL" outcome="Failed" duration="00:00:02.5">
        <Output><ErrorInfo><Message>Execution Timeout Expired. Password=TOKEN_SENTINEL; private post PRIVATE_CONTENT_SENTINEL</Message><StackTrace>PRIVATE_STACK_SENTINEL</StackTrace></ErrorInfo><StdOut>PRIVATE_STDOUT_SENTINEL</StdOut></Output>
      </UnitTestResult><UnitTestResult testId="missing" testName="PRIVATE_UNKNOWN_SENTINEL" outcome="Passed" duration="00:00:01"/></Results>
    </TestRun>`;
    writeFileSync(path.join(directory, 'PRIVATE_PATH_SENTINEL.trx'), fixture);
    const output = path.join(directory, 'safe', 'results.json');
    const result = spawnSync('pwsh', ['-NoProfile', '-File', new URL('./Export-SafeTestReports.ps1', import.meta.url).pathname,
      '-ResultsDirectory', directory, '-OutputPath', output], { encoding: 'utf8', timeout: 15000 });
    assert.equal(result.status, 0, result.stderr);
    const text = readFileSync(output, 'utf8').replace(/^\uFEFF/, '');
    assert.doesNotMatch(text + result.stdout + result.stderr, /SENTINEL|Password|StdOut|StackTrace/);
    assert.deepEqual(JSON.parse(text), [
      { test: 'QueenZone.Web.Tests.ProbeTests.Fails_on_timeout', outcome: 'Failed', durationSeconds: 2.5, failureClass: 'SQL_TIMEOUT' },
      { test: 'unknown-test', outcome: 'Passed', durationSeconds: 1, failureClass: null },
    ]);
    writeFileSync(path.join(directory, 'PRIVATE_PATH_SENTINEL.trx'), '<!DOCTYPE x [<!ENTITY data SYSTEM "file:///PRIVATE_ENTITY_SENTINEL">]><TestRun>&data;</TestRun>');
    const rejected = spawnSync('pwsh', ['-NoProfile', '-File', new URL('./Export-SafeTestReports.ps1', import.meta.url).pathname,
      '-ResultsDirectory', directory, '-OutputPath', output], { encoding: 'utf8', timeout: 15000 });
    assert.notEqual(rejected.status, 0);
    assert.doesNotMatch(rejected.stdout + rejected.stderr, /PRIVATE_ENTITY_SENTINEL/);
  } finally {
    rmSync(directory, { recursive: true, force: true });
  }
});

test('safe report shell selection uses only contexts allowed by Actions', () => {
  const exports = workflow.split('      - name: Export safe test outcomes').slice(1);
  assert.equal(exports.length, 5);
  const shells = exports.map((step) => /^        shell: (.+)$/m.exec(step.split('      - name: Upload test reports')[0])[1].trim());
  assert.deepEqual(shells, [
    'pwsh', 'powershell', 'powershell', 'pwsh', 'powershell',
  ]);
  assert.ok(shells.every((shell) => !shell.includes('${{')));
  assert.match(exports[2], /matrix.os == 'Windows'/);
  assert.match(exports[3], /matrix.os == 'macOS'/);
  assert.match(workflow, /steps.safe-test-reports-windows.outcome == 'success' \|\| steps.safe-test-reports-macos.outcome == 'success'/);
});

test('raw E2E TRX stays outside existing browser-failure artifact uploads', () => {
  const ci = readFileSync(new URL('../.github/workflows/ci.yml', import.meta.url), 'utf8');
  for (const source of [workflow, ci]) {
    const uploads = [...source.matchAll(/- name: Upload Playwright failure artifacts[\s\S]*?retention-days: 1/g)];
    assert.ok(uploads.length > 0);
    for (const [block] of uploads) {
      assert.match(block, /test-results\/e2e\//);
      assert.doesNotMatch(block, /local-trx|legacy-probes|test-results\/\*|path: test-results\s/);
    }
  }
  const ui = workflow.split('  ui-e2e-realdata:')[1].split('  residue-check:')[0];
  assert.equal((ui.match(/-ResultsDirectory test-results\/local-trx\/e2e/g) ?? []).length, 2);
});
