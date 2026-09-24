#Requires -Version 5.1
<#
.SYNOPSIS
    Runs the QueenZone maintenance worker (retention purges and gallery orphan sweep).

.DESCRIPTION
    Wraps `dotnet run --project src/QueenZone.Maintenance.Worker -- run-jobs <selector>` for
    Task Scheduler on the operator machine. The production web app no longer runs these jobs
    (#1677).

    Loads the production SQL and blob connections from Bitwarden Secrets Manager on every run
    (see docs/agent-bitwarden-secrets.md). Use -SampleData to run against in-memory sample data
    without Bitwarden.

    See docs/architecture/maintenance-jobs-scheduling.md for Task Scheduler setup.

.EXAMPLE
    .\scripts\Run-MaintenanceJobs.ps1 -Schedule six-hourly

.EXAMPLE
    .\scripts\Run-MaintenanceJobs.ps1 -Schedule gallery-orphan-sweep -SampleData
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet(
        'six-hourly',
        'daily',
        'all',
        'member-account-deletion',
        'gallery-orphan-sweep',
        'private-message-report-purge',
        'fan-performance-submission-purge')]
    [string]$Schedule,

    [switch]$SampleData
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..')
Set-Location $repoRoot

if ($SampleData) {
    # Both unset means the worker uses in-memory sample data and storage.
    $env:ConnectionStrings__QueenZoneLegacy = ''
    $env:ConnectionStrings__BlobStorage = ''
}
else {
    . (Join-Path $PSScriptRoot 'Import-NewsAgentProductionConnection.ps1') -IncludeBlobStorage
}

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    Write-Error 'dotnet SDK not found. Install from https://dotnet.microsoft.com/download'
}

$workerArgs = @('run', '--project', 'src/QueenZone.Maintenance.Worker', '-c', 'Release', '--', 'run-jobs', $Schedule)

Write-Host "QueenZone maintenance jobs: dotnet $($workerArgs -join ' ')"
& dotnet @workerArgs
exit $LASTEXITCODE
