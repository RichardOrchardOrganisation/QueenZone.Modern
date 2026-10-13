#Requires -Version 7.0
<#
.SYNOPSIS
  Build, launch and collect automated proof for one web feature (Windows host).
.DESCRIPTION
  Leaves visual inspection explicitly NOT RUN. Use -KeepRunning to drive the same host
  interactively afterwards, then run the control helper's cleanup command.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string] $Feature,
    [int] $Port = 5199,
    [switch] $KeepRunning
)
Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot "FeatureMap.ps1")
. (Join-Path $PSScriptRoot "WebVerification.ps1")
$entry = Resolve-QueenZoneFeature -Entries (Get-QueenZoneFeatureMap -RepoRoot $repoRoot) -IdOrAlias $Feature
if (-not $entry -or $entry.id -notlike "web.*") { throw "Unknown web feature '$Feature'." }
$proofDir = Get-QueenZoneProofDir -RepoRoot $repoRoot -FeatureId $entry.id -Platform "web"
$proof = New-QueenZoneWebProof -RepoRoot $repoRoot -Entry $entry -BaseUrl "http://127.0.0.1:$Port"
$control = Join-Path $repoRoot ".cursor/skills/verify-queenzone/scripts/control-queenzone.ps1"
$statePath = Join-Path $repoRoot ".cursor/skills/verify-queenzone/.run/state.json"
$ownsHost = $false
try {
    if (-not $IsWindows) { throw "The local host helper requires Windows. Use a Windows checkout or the CI Playwright workflow; do not claim local proof." }
    foreach ($tool in @("dotnet", "pwsh", "git")) { Get-Command $tool -ErrorAction Stop | Out-Null }
    if (Test-Path $statePath) { throw "A verification host is already recorded. Finish that run and use the control helper's cleanup before starting fresh." }
    if ([string]::IsNullOrWhiteSpace($env:SIXLABORS_LICENSE_KEY)) {
        . (Join-Path $repoRoot "scripts/Import-SixLaborsLicense.ps1")
    }
    & dotnet build (Join-Path $repoRoot "tests/QueenZone.Web.E2E/QueenZone.Web.E2E.csproj") --configuration Release | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "E2E Release build failed." }
    & pwsh -NoProfile -File (Join-Path $repoRoot "tests/QueenZone.Web.E2E/bin/Release/net10.0/playwright.ps1") install chromium | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "Playwright Chromium installation failed." }
    $ownsHost = $true
    & $control launch -Port $Port | Out-Host
    & $control doctor | Out-Host
    $proof = Invoke-QueenZoneWebProof -RepoRoot $repoRoot -Entry $entry -BaseUrl "http://127.0.0.1:$Port" -ProofDir $proofDir
}
catch { $proof.detail = $_.Exception.Message }
finally {
    if ($ownsHost -and (-not $KeepRunning -or $proof.automated -ne "PASS")) {
        try { & $control cleanup | Out-Host }
        catch { $proof.automated = "FAIL"; $proof.detail += " Host cleanup failed: $($_.Exception.Message)" }
    }
    $proof.command = "pwsh -File scripts/Verify-Web.ps1 -Feature $($entry.id) -Port $Port"
    if ($KeepRunning) { $proof.command += " -KeepRunning" }
    Write-QueenZoneWebProof -Proof $proof -ProofDir $proofDir
}
if ($proof.automated -ne "PASS") { exit 1 }
