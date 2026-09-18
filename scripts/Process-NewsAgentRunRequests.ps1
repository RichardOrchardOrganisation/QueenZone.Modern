#Requires -Version 5.1
<#
.SYNOPSIS
    Checks for one web-queued news agent request and processes it locally.

.DESCRIPTION
    Intended for Windows Task Scheduler. The command contacts the shared database
    using the worker's local configuration, records a runner heartbeat, and claims at
    most one pending request.

    Request kinds:
    - ScheduledGathering: source fetch + triage (same as discover-news --scheduled)
    - UrlIngestion: fetch one admin-submitted public URL, triage, optional draft only
      when the admin explicitly requested draft generation

    AI drafts otherwise remain explicit per-candidate editor actions in QueenZone admin.
    Nothing is published to public /news automatically.

.EXAMPLE
    .\scripts\Process-NewsAgentRunRequests.ps1

.EXAMPLE
    .\scripts\Process-NewsAgentRunRequests.ps1 -RunnerId "news-pc"
#>
[CmdletBinding()]
param(
    [string]$RunnerId = $env:COMPUTERNAME
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..')
Set-Location $repoRoot
. (Join-Path $PSScriptRoot 'Import-NewsAgentProductionConnection.ps1')

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    Write-Error 'dotnet SDK not found. Install from https://dotnet.microsoft.com/download'
}

if ([string]::IsNullOrWhiteSpace($RunnerId)) {
    $RunnerId = 'windows-news-runner'
}

$workerArgs = @(
    'run',
    '--project',
    'src/QueenZone.NewsAgent.Worker',
    '--',
    'process-news-requests',
    '--runner-id',
    $RunnerId
)

Write-Host "QueenZone queued news requests: runner $RunnerId"
& dotnet @workerArgs
exit $LASTEXITCODE
