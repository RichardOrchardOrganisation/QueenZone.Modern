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
