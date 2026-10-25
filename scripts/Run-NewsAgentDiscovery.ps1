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
    [switch]$Force,
    [string]$LogDirectory = (Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'QueenZone\Logs\NewsAgent')
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

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

& (Join-Path $PSScriptRoot 'Invoke-LoggedNewsAgentTask.ps1') -TaskName discovery -WorkerArguments $workerArgs -LogDirectory $LogDirectory
exit $LASTEXITCODE
