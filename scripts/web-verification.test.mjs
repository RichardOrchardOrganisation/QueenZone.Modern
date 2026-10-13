import assert from 'node:assert/strict';
import { test } from 'node:test';
import { mkdtempSync, mkdirSync, writeFileSync, readFileSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { spawnPwsh } from './test-helpers/pwsh.mjs';

const repo = fileURLToPath(new URL('../', import.meta.url));
for (const scenario of ['pass', 'second-fails', 'zero', 'skipped', 'missing-report', 'no-specs', 'screenshot-fails', 'throws']) {
  test(`web proof: ${scenario}`, () => {
    const dir = mkdtempSync(join(tmpdir(), 'qz-proof-'));
    try {
      const bin = join(dir, 'tests/QueenZone.Web.E2E/bin/Release/net10.0');
      const proofDir = join(dir, 'proof');
      mkdirSync(bin, { recursive: true });
      mkdirSync(proofDir);
      writeFileSync(join(bin, 'playwright.ps1'), `
if ($env:QZ_PROOF_SCENARIO -eq 'screenshot-fails') { $global:LASTEXITCODE = 1; return }
Set-Content -LiteralPath $args[-1] -Value 'fixture screenshot'
$global:LASTEXITCODE = 0
`);
      // A stale successful report must never conceal a missing new report.
      writeFileSync(join(proofDir, 'Second.trx'), '<TestRun><Counters total="1" passed="1" /></TestRun>');
      const harness = join(dir, 'harness.ps1');
      writeFileSync(harness, `
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $env:QZ_SOURCE 'scripts/FeatureMap.ps1')
. (Join-Path $env:QZ_SOURCE 'scripts/WebVerification.ps1')
function git { $global:LASTEXITCODE = 0; if ($args -contains 'rev-parse') { 'fixture-sha' } }
$script:calls = @()
function dotnet {
    $script:calls += @{ arguments = $args; url = $env:E2E_BASE_URL; artifacts = $env:E2E_ARTIFACT_DIR; readOnly = $env:E2E_READONLY }
    $logger = $args[[array]::IndexOf($args, '--logger') + 1]
    $file = $logger.Replace('trx;LogFileName=', '')
    $total = 2; $passed = 2
    $global:LASTEXITCODE = 0
    if ($file -eq 'Second.trx') {
        switch ($env:QZ_PROOF_SCENARIO) {
            'second-fails' { $passed = 1; $global:LASTEXITCODE = 1 }
            'zero' { $total = 0; $passed = 0 }
            'skipped' { $passed = 1 }
            'missing-report' { return }
            'throws' { throw 'fixture runner unavailable' }
        }
    }
    Set-Content -LiteralPath (Join-Path $env:E2E_ARTIFACT_DIR $file) -Value "<TestRun xmlns='http://microsoft.com/schemas/VisualStudio/TeamTest/2010'><ResultSummary><Counters total='$total' passed='$passed'/></ResultSummary></TestRun>"
}
$env:E2E_BASE_URL = 'https://must-not-be-used.invalid'
$env:E2E_ARTIFACT_DIR = 'original-artifacts'
$env:E2E_READONLY = 'true'
$specs = @('tests/QueenZone.Web.E2E/First.cs', 'tests/QueenZone.Web.E2E/Second.cs')
if ($env:QZ_PROOF_SCENARIO -eq 'no-specs') { $specs = @() }
$entry = [pscustomobject]@{ id = 'web.fixture'; url = '/search'; specs = $specs }
$proof = Invoke-QueenZoneWebProof -RepoRoot $env:QZ_FIXTURE -Entry $entry -BaseUrl 'http://127.0.0.1:5201' -ProofDir (Join-Path $env:QZ_FIXTURE 'proof')
Write-QueenZoneWebProof -Proof $proof -ProofDir (Join-Path $env:QZ_FIXTURE 'proof')
@{ calls = $script:calls; url = $env:E2E_BASE_URL; artifacts = $env:E2E_ARTIFACT_DIR; readOnly = $env:E2E_READONLY } | ConvertTo-Json -Depth 8 | Set-Content (Join-Path $env:QZ_FIXTURE 'calls.json')
`);
      const run = spawnPwsh(['-File', harness], { encoding: 'utf8', env: { ...process.env, QZ_SOURCE: repo, QZ_FIXTURE: dir, QZ_PROOF_SCENARIO: scenario } });
      assert.equal(run.status, 0, `${run.stdout}\n${run.stderr}`);
      const proof = JSON.parse(readFileSync(join(proofDir, 'proof.json'), 'utf8').replace(/^\uFEFF/, ''));
      const calls = JSON.parse(readFileSync(join(dir, 'calls.json'), 'utf8').replace(/^\uFEFF/, ''));
      assert.equal(proof.automated, scenario === 'pass' ? 'PASS' : scenario === 'no-specs' ? 'NOT RUN' : 'FAIL');
      assert.equal(proof.visualInspection, 'NOT RUN');
      assert.equal(proof.screenshot, scenario === 'screenshot-fails' ? 'FAIL' : 'PASS');
      assert.equal(calls.url, 'https://must-not-be-used.invalid');
      assert.equal(calls.artifacts, 'original-artifacts');
      assert.equal(calls.readOnly, 'true');
      assert.equal(calls.calls.length, scenario === 'no-specs' ? 0 : 2);
      for (const [index, call] of calls.calls.entries()) {
        assert.equal(call.url, 'http://127.0.0.1:5201');
        assert.equal(call.artifacts, proofDir);
        assert.equal(call.readOnly, 'false');
        assert.ok(call.arguments.includes(`(FullyQualifiedName~QueenZone.Web.E2E.${index === 0 ? 'First' : 'Second'}.)&TestCategory=Deterministic`));
      }
      const markdown = readFileSync(join(proofDir, 'proof.md'), 'utf8');
      assert.match(markdown, /## Verification/);
      assert.match(markdown, /visual inspection NOT RUN/);
      assert.match(markdown, /Not verified: interactive changed journey/);
    } finally {
      rmSync(dir, { recursive: true, force: true });
    }
  });
}

function runProofHelper(body, files = {}) {
  const dir = mkdtempSync(join(tmpdir(), 'qz-proof-helper-'));
  try {
    for (const [name, content] of Object.entries(files)) {
      writeFileSync(join(dir, name), content);
    }
    const harness = join(dir, 'harness.ps1');
    writeFileSync(harness, `
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $env:QZ_SOURCE 'scripts/WebVerification.ps1')
${body}
`);
    const run = spawnPwsh(['-File', harness], {
      encoding: 'utf8',
      env: { ...process.env, QZ_SOURCE: repo, QZ_FIXTURE: dir },
    });
    assert.equal(run.status, 0, `${run.stdout}\n${run.stderr}`);
    return JSON.parse(run.stdout.replace(/^\uFEFF/, ''));
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
}

test('proof route helper preserves literal paths and resolves every Testing placeholder', () => {
  const routes = runProofHelper(`
@(
    Resolve-QueenZoneWebProofRoute -UrlPath '/search?q=Queen'
    Resolve-QueenZoneWebProofRoute -UrlPath ''
    Resolve-QueenZoneWebProofRoute -UrlPath '/{slug}/{picId}/{id}/{pageNumber}/{topicId}/{statusCode?}/{*path}/{id}'
) | ConvertTo-Json
`);
  assert.deepEqual(routes, [
    '/search?q=Queen',
    '',
    '/brian-may/101/1003/1/1002/404/index/1003',
  ]);
});

test('proof route helper rejects unresolved placeholders after substitution', () => {
  const error = runProofHelper(`
try { Resolve-QueenZoneWebProofRoute -UrlPath '/{id}/{unknown}' }
catch { $_.Exception.Message | ConvertTo-Json }
`);
  assert.equal(error, 'Unresolved route parameters in /1003/{unknown}; add an explicit Testing fixture before capturing proof.');
});

for (const { name, xml, code = 0, total = 0, passed = 0, result = 'FAIL' } of [
  { name: 'missing report' },
  { name: 'missing counters', xml: '<TestRun />' },
  { name: 'empty run', xml: '<TestRun><Counters total="0" passed="0" /></TestRun>' },
  { name: 'skipped or failed tests', xml: '<TestRun><Counters total="3" passed="2" /></TestRun>', total: 3, passed: 2 },
  { name: 'nonzero exit despite passing report', xml: '<TestRun><Counters total="2" passed="2" /></TestRun>', code: 7, total: 2, passed: 2 },
  { name: 'passing report', xml: '<TestRun><Counters total="2" passed="2" /></TestRun>', total: 2, passed: 2, result: 'PASS' },
  { name: 'namespaced passing report', xml: '<TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010"><ResultSummary><Counters total="5" passed="5" /></ResultSummary></TestRun>', total: 5, passed: 5, result: 'PASS' },
]) {
  test(`proof TRX helper: ${name}`, () => {
    const actual = runProofHelper(`
Read-QueenZoneWebProofTestResult -Trx (Join-Path $env:QZ_FIXTURE 'fixture.trx') -Spec 'tests/Fixture.cs' -Code ${code} | ConvertTo-Json
`, xml === undefined ? {} : { 'fixture.trx': xml });
    assert.deepEqual(actual, { spec: 'tests/Fixture.cs', result, total, passed, exitCode: code });
  });
}

for (const xml of ['<TestRun>', '<TestRun><Counters total="invalid" passed="1" /></TestRun>']) {
  test(`proof TRX helper preserves parsing errors: ${xml}`, () => {
    const actual = runProofHelper(`
$trx = Join-Path $env:QZ_FIXTURE 'fixture.trx'
# Compare the helper error to the original parsing operations on this PowerShell version.
try {
    [xml] $report = Get-Content -Raw -LiteralPath $trx
    $counters = $report.SelectSingleNode("//*[local-name()='Counters']")
    $total = [int] $counters.total
} catch { $expectedError = $_.Exception.Message }
try {
    Read-QueenZoneWebProofTestResult -Trx $trx -Spec 'tests/Fixture.cs' -Code 0
} catch { @{ expected = $expectedError; actual = $_.Exception.Message } | ConvertTo-Json }
`, { 'fixture.trx': xml });
    assert.ok(actual.expected);
    assert.equal(actual.actual, actual.expected);
  });
}

for (const { screenshot, results, expected } of [
  { screenshot: 'PASS', results: [], expected: 'NOT RUN' },
  { screenshot: 'FAIL', results: [], expected: 'FAIL' },
  { screenshot: 'PASS', results: ['PASS', 'PASS'], expected: 'PASS' },
  { screenshot: 'PASS', results: ['PASS', 'FAIL'], expected: 'FAIL' },
  { screenshot: 'FAIL', results: ['PASS'], expected: 'FAIL' },
]) {
  test(`proof outcome helper: screenshot ${screenshot}, tests ${results.join(',') || 'none'}`, () => {
    const actual = runProofHelper(`
$results = @(${results.map((result) => `'${result}'`).join(',')})
$proof = [ordered]@{
    screenshot = '${screenshot}'
    tests = @($results | ForEach-Object { @{ result = $_ } })
    automated = 'NOT RUN'
    detail = ''
}
$output = @(Set-QueenZoneWebProofOutcome -Proof $proof -Specs $results)
@{ proof = $proof; outputCount = $output.Count } | ConvertTo-Json -Depth 5
`);
    assert.equal(actual.outputCount, 0);
    assert.equal(actual.proof.automated, expected);
    assert.equal(actual.proof.screenshot, screenshot);
    assert.deepEqual(actual.proof.tests, results.map((result) => ({ result })));
    assert.equal(actual.proof.detail, results.length === 0
      ? 'No mapped behavioural tests. Screenshot capture alone does not verify this feature.'
      : 'Every mapped fixture must execute at least one deterministic test, with no failures or skips.');
  });
}
