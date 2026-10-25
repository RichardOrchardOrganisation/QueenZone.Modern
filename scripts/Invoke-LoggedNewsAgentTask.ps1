#Requires -Version 5.1
<#
.SYNOPSIS
    Persists console output and the exit status for a news worker invocation.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('discovery', 'requests')]
    [string]$TaskName,

    [Parameter(Mandatory = $true)]
    [string[]]$WorkerArguments,

    [string]$LogDirectory = (Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'QueenZone\Logs\NewsAgent')
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$exitCode = 1
$transcribing = $false
$started = [DateTime]::UtcNow

try {
    New-Item -ItemType Directory -Path $LogDirectory -Force | Out-Null
    $logPath = Join-Path $LogDirectory ("{0}-{1}-{2}.log" -f $TaskName, $started.ToString('yyyyMMddTHHmmssfffZ'), $PID)
    Start-Transcript -LiteralPath $logPath -NoClobber | Out-Null
    $transcribing = $true
    Write-Host "NewsAgent task=$TaskName startedUtc=$($started.ToString('o')) machine=$env:COMPUTERNAME log=$logPath"

    # Keep only this task's transcripts, with a bounded retention period.
    Get-ChildItem -LiteralPath $LogDirectory -Filter "$TaskName-*.log" -File |
        Where-Object { $_.LastWriteTimeUtc -lt $started.AddDays(-30) } |
        Remove-Item -Force

    $repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..')
    Set-Location $repoRoot
    . (Join-Path $PSScriptRoot 'Import-NewsAgentProductionConnection.ps1')

    if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
        throw 'dotnet SDK not found. Install from https://dotnet.microsoft.com/download'
    }

    Write-Host "QueenZone news worker: dotnet $($WorkerArguments -join ' ')"
    # Render native streams through PowerShell so transcripts also retain output
    # when Task Scheduler has no attached console. Windows PowerShell represents
    # native stderr as error records; those must not abort an otherwise valid run.
    $ErrorActionPreference = 'Continue'
    try {
        & dotnet @WorkerArguments 2>&1 | ForEach-Object { Write-Host $_ }
        $exitCode = $LASTEXITCODE
    }
    finally { $ErrorActionPreference = 'Stop' }
}
catch {
    # Startup failures (including Bitwarden/configuration) must also be retained.
    Write-Warning "NewsAgent task=$TaskName failed: $($_.Exception.Message)"
}
finally {
    $finished = [DateTime]::UtcNow
    Write-Host "NewsAgent task=$TaskName finishedUtc=$($finished.ToString('o')) durationSeconds=$(($finished - $started).TotalSeconds) exitCode=$exitCode"
    if ($transcribing) {
        Stop-Transcript | Out-Null
    }
}

exit $exitCode
