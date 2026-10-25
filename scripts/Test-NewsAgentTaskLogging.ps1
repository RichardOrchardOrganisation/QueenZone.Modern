#Requires -Version 5.1
# Runs the real wrappers in child PowerShell processes with a fake worker and
# fake secret loader. No network, database, or production credentials are used.
$ErrorActionPreference = 'Stop'
$testRoot = Join-Path ([IO.Path]::GetTempPath()) ('qz-news-logging-' + [guid]::NewGuid().ToString('N'))
$savedPath = $env:PATH
$savedExit = $env:QZ_TEST_WORKER_EXIT
$savedStartup = $env:QZ_TEST_STARTUP_FAIL

try {
    $scriptsDirectory = Join-Path $testRoot 'scripts'
    $logDirectory = Join-Path $testRoot 'logs'
    New-Item -ItemType Directory -Path $scriptsDirectory, $logDirectory | Out-Null
    foreach ($name in @('Invoke-LoggedNewsAgentTask.ps1', 'Run-NewsAgentDiscovery.ps1', 'Process-NewsAgentRunRequests.ps1')) {
        Copy-Item -LiteralPath (Join-Path $PSScriptRoot $name) -Destination $scriptsDirectory
    }
    Set-Content (Join-Path $scriptsDirectory 'Import-NewsAgentProductionConnection.ps1') @'
if ($env:QZ_TEST_STARTUP_FAIL -eq '1') { throw 'Fixture secret loader unavailable' }
Write-Host 'Fixture configuration loaded'
'@
    Set-Content (Join-Path $testRoot 'dotnet.cmd') @'
@echo off
echo Fixture worker stdout: %*
echo Fixture worker stderr 1>&2
exit /b %QZ_TEST_WORKER_EXIT%
'@
    $env:PATH = "$testRoot;$savedPath"
    $engine = (Get-Process -Id $PID).Path

    function Invoke-FixtureTask([string]$ScriptName, [string]$ExtraArguments) {
        $startInfo = New-Object System.Diagnostics.ProcessStartInfo
        $startInfo.FileName = $engine
        $fixtureScript = Join-Path $scriptsDirectory $ScriptName
        $startInfo.Arguments = "-NoProfile -File `"$fixtureScript`" -LogDirectory `"$logDirectory`" $ExtraArguments"
        $startInfo.UseShellExecute = $false
        $startInfo.RedirectStandardOutput = $true
        $startInfo.RedirectStandardError = $true
        $process = [Diagnostics.Process]::Start($startInfo)
        try {
            $stdout = $process.StandardOutput.ReadToEndAsync()
            $stderr = $process.StandardError.ReadToEndAsync()
            if (-not $process.WaitForExit(30000)) {
                $process.Kill()
                throw 'Fixture task timed out'
            }
            [void]$stdout.GetAwaiter().GetResult()
            [void]$stderr.GetAwaiter().GetResult()
            return $process.ExitCode
        }
        finally { $process.Dispose() }
    }

    $oldDiscovery = Join-Path $logDirectory 'discovery-old.log'
    $oldRequests = Join-Path $logDirectory 'requests-old.log'
    $otherLog = Join-Path $logDirectory 'unrelated.log'
    foreach ($path in @($oldDiscovery, $oldRequests, $otherLog)) {
        Set-Content $path 'old'
        (Get-Item $path).LastWriteTimeUtc = [DateTime]::UtcNow.AddDays(-31)
    }

    $env:QZ_TEST_WORKER_EXIT = '0'
    $env:QZ_TEST_STARTUP_FAIL = '0'
    if ((Invoke-FixtureTask 'Run-NewsAgentDiscovery.ps1' '-Scheduled') -ne 0) { throw 'Success exit code lost' }
    $discoveryLog = Get-ChildItem $logDirectory -Filter 'discovery-*.log' | Select-Object -First 1
    $content = Get-Content -Raw $discoveryLog.FullName
    foreach ($expected in @('Fixture configuration loaded', 'Fixture worker stdout', 'Fixture worker stderr', 'discover-news --scheduled', 'exitCode=0', 'durationSeconds=')) {
        if (-not $content.Contains($expected)) { throw "Missing transcript content: $expected" }
    }
    if (Test-Path $oldDiscovery) { throw 'Expired discovery log retained' }
    if (-not (Test-Path $oldRequests) -or -not (Test-Path $otherLog)) { throw 'Unrelated log removed' }

    $env:QZ_TEST_WORKER_EXIT = '7'
    if ((Invoke-FixtureTask 'Process-NewsAgentRunRequests.ps1' '-RunnerId fixture-runner') -ne 7) { throw 'Failure exit code lost' }
    $requestLog = Get-ChildItem $logDirectory -Filter 'requests-*.log' | Select-Object -First 1
    $content = Get-Content -Raw $requestLog.FullName
    if (-not $content.Contains('process-news-requests --runner-id fixture-runner') -or -not $content.Contains('exitCode=7')) { throw 'Failed worker diagnostics missing' }

    $env:QZ_TEST_STARTUP_FAIL = '1'
    if ((Invoke-FixtureTask 'Process-NewsAgentRunRequests.ps1' '') -ne 1) { throw 'Startup failure must exit 1' }
    $logs = @(Get-ChildItem $logDirectory -Filter 'requests-*.log')
    if ($logs.Count -ne 2) { throw 'Runs must have separate transcripts' }
    $startupLog = $logs | Sort-Object LastWriteTimeUtc -Descending | Select-Object -First 1
    $content = Get-Content -Raw $startupLog.FullName
    if (-not $content.Contains('Fixture secret loader unavailable') -or -not $content.Contains('exitCode=1')) { throw 'Startup failure diagnostics missing' }
    if ($content.Contains('Fixture worker stdout')) { throw 'Worker ran after startup failure' }
    Write-Output 'News task logging tests passed: stdout/stderr, arguments, exits, startup failure, retention, separate runs.'
}
finally {
    $env:PATH = $savedPath
    $env:QZ_TEST_WORKER_EXIT = $savedExit
    $env:QZ_TEST_STARTUP_FAIL = $savedStartup
    # This directory is the unique fixture root created above, never a repo path.
    $expectedPrefix = Join-Path ([IO.Path]::GetTempPath()) 'qz-news-logging-'
    if (-not [IO.Path]::GetFullPath($testRoot).StartsWith($expectedPrefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Refusing cleanup outside the fixture directory'
    }
    if (Test-Path -LiteralPath $testRoot) { Remove-Item -LiteralPath $testRoot -Recurse -Force }
}
