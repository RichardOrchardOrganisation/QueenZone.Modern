#Requires -Version 5.1
<#
.SYNOPSIS
    Runs the QueenZone news discovery worker for manual or scheduled execution.

.DESCRIPTION
    Wraps `dotnet run --project src/QueenZone.NewsAgent.Worker -- discover-news` with
    common flag presets. For Task Scheduler, use -Scheduled for source gathering and
    triage only. Draft generation remains an explicit editor action.
    (or environment variables) provides the database connection and OpenRouter key.

    See docs/architecture/news-agent-scheduling.md for Task Scheduler and Azure setup.

.EXAMPLE
    .\scripts\Run-NewsAgentDiscovery.ps1 -Scheduled

.EXAMPLE
    .\scripts\Run-NewsAgentDiscovery.ps1 -FetchOnly -SeedSources
#>
[CmdletBinding()]
param(
    [switch]$Scheduled,
    [switch]$SeedSources,
    [switch]$FetchOnly,
    [switch]$TriageOnly,
    [switch]$DraftOnly,
    [switch]$DryRun,
    [switch]$Force
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..')
Set-Location $repoRoot
. (Join-Path $PSScriptRoot 'Import-NewsAgentProductionConnection.ps1')

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    Write-Error 'dotnet SDK not found. Install from https://dotnet.microsoft.com/download'
}

$workerArgs = @('run', '--project', 'src/QueenZone.NewsAgent.Worker', '--', 'discover-news')

if ($Scheduled) {
    $workerArgs += '--scheduled'
}
if ($SeedSources) {
    $workerArgs += '--seed-sources'
}
if ($FetchOnly) {
    $workerArgs += '--fetch-only'
}
if ($TriageOnly) {
    $workerArgs += '--triage-only'
}
if ($DraftOnly) {
    $workerArgs += '--draft-only'
}
if ($DryRun) {
    $workerArgs += '--dry-run'
}
if ($Force) {
    $workerArgs += '--force'
}

if (-not $Scheduled -and -not $SeedSources -and -not $FetchOnly -and -not $TriageOnly -and -not $DraftOnly) {
  Write-Error 'Specify -Scheduled, -FetchOnly, -TriageOnly, -DraftOnly, or at least -SeedSources.'
}

Write-Host "QueenZone news discovery: dotnet $($workerArgs -join ' ')"
& dotnet @workerArgs
exit $LASTEXITCODE
