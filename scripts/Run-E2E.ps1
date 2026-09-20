#Requires -Version 5.1
<#
.SYNOPSIS
  Publish, start, and run the Playwright E2E suite (or attach to an existing app / live site).

.DESCRIPTION
  Single entry point for local and CI Playwright runs. Replaces the inline publish → start →
  poll /health → test → stop sequence previously embedded in .github/workflows/ci.yml.

  Modes:
    Deterministic — ASPNETCORE_ENVIRONMENT=Testing (in-memory), TestCategory=Deterministic
    RealData      — ASPNETCORE_ENVIRONMENT=E2E (SQL Express mirror), TestCategory=RealData.
                  Applies pending EF migrations to Express before start (sync/skip_sync can
                  leave modern tables such as QuizSprintRuns missing).
    LiveSite      — no local app; E2E_READONLY=true; TestCategory=RealData&TestCategory=ReadOnly

  Windows starts the published exe via WMI Win32_Process.Create so the process is not tied to a
  runner Job Object (Start-Process children die when a CI step ends). macOS launches via
  `dotnet <dll>` because the native app host cannot find tool-managed SDK installs.

.PARAMETER Mode
  Deterministic | RealData | LiveSite

.PARAMETER BaseUrl
  App URL. Defaults to http://127.0.0.1:5099. Required (and must not be localhost) for LiveSite.

.PARAMETER Configuration
  Build configuration (default Release).

.PARAMETER NoBuild
  Skip building the E2E project; pass --no-build to dotnet test.

.PARAMETER NoRestore
  Skip restore; pass --no-restore to build/publish/test.

.PARAMETER SkipAppStart
  Do not publish or start a local app; attach to whatever is already listening at -BaseUrl.
  Ignored for LiveSite (which never starts an app).

.PARAMETER CategoryFilter
  Optional substring narrowing the run to fully-qualified test names containing it (dotnet test
  `FullyQualifiedName~<value>`), combined with the mode's TestCategory filter. Lets a nightly
  on-demand run re-run a single failing area instead of the whole suite. Ignored when empty.

.EXAMPLE
  powershell -File ./scripts/Run-E2E.ps1 -Mode Deterministic

.EXAMPLE
  $env:ConnectionStrings__QueenZoneLegacy = "Server=localhost\SQLEXPRESS;Database=queenzone_legacy_sync;Integrated Security=True;TrustServerCertificate=True"
  pwsh -File ./scripts/Run-E2E.ps1 -Mode RealData

.EXAMPLE
  pwsh -File ./scripts/Run-E2E.ps1 -Mode LiveSite -BaseUrl https://www.queenzone.org
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet("Deterministic", "RealData", "LiveSite")]
    [string] $Mode,

    [Parameter()]
    [string] $BaseUrl = "http://127.0.0.1:5099",

    [Parameter()]
    [string] $Configuration = "Release",

    [Parameter()]
    [switch] $NoBuild,

    [Parameter()]
    [switch] $NoRestore,

    [Parameter()]
    [switch] $SkipAppStart,

    [Parameter()]
    [string] $CategoryFilter = ""
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
Set-Location $repoRoot

$e2eProject = "tests/QueenZone.Web.E2E/QueenZone.Web.E2E.csproj"
$webProject = "src/QueenZone.Web/QueenZone.Web.csproj"
$publishDir = Join-Path $repoRoot "e2e-app"
$logPath = Join-Path $repoRoot "e2e-app.log"
$errPath = Join-Path $repoRoot "e2e-app.err.log"
$appPidFile = Join-Path $repoRoot "e2e-app.pid"
$cmdPidFile = Join-Path $repoRoot "e2e-app-cmd.pid"
$playwrightScript = Join-Path $repoRoot "tests/QueenZone.Web.E2E/bin/$Configuration/net10.0/playwright.ps1"
$artifactDirSetting = $env:E2E_ARTIFACT_DIR
if ([string]::IsNullOrWhiteSpace($artifactDirSetting)) {
    $artifactDirSetting = "test-results/e2e"
}
$artifactDir = if ([System.IO.Path]::IsPathRooted($artifactDirSetting)) {
    $artifactDirSetting
}
else {
    Join-Path $repoRoot $artifactDirSetting
}
$adminEmail = "admin@test.local"

$script:StartedApp = $false
$script:AppPid = $null
$script:CmdPid = $null
$script:MacProcess = $null
$script:MacStdoutTask = $null
$script:MacStderrTask = $null

function Test-IsWindowsOS {
    if ($null -ne (Get-Variable -Name IsWindows -ErrorAction SilentlyContinue)) {
        return [bool]$IsWindows
    }
    return $true
}

function Test-IsMacOS {
    if ($null -ne (Get-Variable -Name IsMacOS -ErrorAction SilentlyContinue)) {
        return [bool]$IsMacOS
    }
    return $false
}

function Test-IsLocalHostUrl {
    param([string] $Url)

    if ([string]::IsNullOrWhiteSpace($Url)) {
        return $true
    }

    try {
        $uri = [Uri]$Url
    }
    catch {
        throw "BaseUrl must be an absolute http(s) URL. Received: '$Url'"
    }

    if (-not $uri.IsAbsoluteUri -or ($uri.Scheme -ne "http" -and $uri.Scheme -ne "https")) {
        throw "BaseUrl must be an absolute http(s) URL. Received: '$Url'"
    }

    $hostName = $uri.Host
    return (
        $hostName -eq "localhost" -or
        $hostName -eq "127.0.0.1" -or
        $hostName -eq "::1" -or
        $hostName -eq "[::1]"
    )
}

function Stop-StrayQueenZoneWeb {
    if (Test-IsWindowsOS) {
        Get-Process -Name "QueenZone.Web" -ErrorAction SilentlyContinue |
            Stop-Process -Force -ErrorAction SilentlyContinue
        return
    }

    if (Test-IsMacOS) {
        & pkill -f QueenZone.Web 2>$null
        # pkill exit 1 = no match; ignore
        return
    }

    Get-Process -ErrorAction SilentlyContinue |
        Where-Object { $_.ProcessName -like "*QueenZone.Web*" } |
        Stop-Process -Force -ErrorAction SilentlyContinue
}

function Wait-ForAppReady {
    param(
        [string] $HealthUrl,
        [Nullable[int]] $ProcessId,
        [int] $MaxAttempts = 60,
        [int] $SleepSeconds = 2
    )

    Write-Host "Waiting for web app at $HealthUrl ..."
    for ($i = 1; $i -le $MaxAttempts; $i++) {
        if ($null -ne $ProcessId) {
            $proc = Get-Process -Id $ProcessId -ErrorAction SilentlyContinue
            if (-not $proc) {
                Write-Host "App process (PID $ProcessId) exited unexpectedly after $($i * $SleepSeconds)s"
                break
            }
        }

        try {
            $response = Invoke-WebRequest -Uri $HealthUrl -UseBasicParsing -TimeoutSec 2
            if ($response.StatusCode -eq 200) {
                Write-Host "App is up after $($i * $SleepSeconds)s"
                return
            }
        }
        catch {
            Write-Host "Attempt ${i}: $($_.Exception.Message)"
        }

        Start-Sleep -Seconds $SleepSeconds
    }

    Write-Host "App failed to start in time."
    if (Test-Path $logPath) { Get-Content $logPath -ErrorAction SilentlyContinue }
    if (Test-Path $errPath) { Get-Content $errPath -ErrorAction SilentlyContinue }
    throw "App failed to become ready at $HealthUrl"
}

function Start-AppWindows {
    param(
        [string] $AppDir,
        [string] $EnvironmentName,
        [string] $Urls,
        [string] $ConnectionString
    )

    # Start-Process makes the app a child of this process / CI step Job Object and it can be
    # killed when the step ends. Launch via WMI so it survives across CI steps and matches the
    # hard-won behaviour previously inline in ci.yml.
    $exePath = Join-Path $AppDir "QueenZone.Web.exe"
    if (-not (Test-Path $exePath)) {
        throw "Published app not found: $exePath"
    }

    $envAssignments = @(
        "set ASPNETCORE_ENVIRONMENT=$EnvironmentName",
        "set ASPNETCORE_URLS=$Urls",
        "set ASPNETCORE_CONTENTROOT=$AppDir"
    )
    if (-not [string]::IsNullOrWhiteSpace($ConnectionString)) {
        $envAssignments += "set ConnectionStrings__QueenZoneLegacy=$ConnectionString"
    }

    $setBlock = ($envAssignments -join "&&")
    $cmdLine = "cmd.exe /c `"$setBlock&&`"$exePath`" > `"$logPath`" 2> `"$errPath`"`""
    $result = Invoke-CimMethod -ClassName Win32_Process -MethodName Create -Arguments @{
        CommandLine      = $cmdLine
        CurrentDirectory = $AppDir
    }
    if ($result.ReturnValue -ne 0) {
        throw "Failed to start app via WMI, return code $($result.ReturnValue)"
    }

    $script:CmdPid = [int]$result.ProcessId
    $script:CmdPid | Out-File -Encoding ascii $cmdPidFile

    # $CmdPid is cmd.exe's PID, not QueenZone.Web.exe's. Find the real child so Stop can
    # target the actual listener.
    $appPid = $script:CmdPid
    for ($i = 1; $i -le 10; $i++) {
        $child = Get-CimInstance Win32_Process -Filter "ParentProcessId=$($script:CmdPid)" -ErrorAction SilentlyContinue |
            Select-Object -First 1
        if ($child) {
            $appPid = [int]$child.ProcessId
            break
        }
        Start-Sleep -Milliseconds 500
    }

    $script:AppPid = $appPid
    $script:AppPid | Out-File -Encoding ascii $appPidFile
    $script:StartedApp = $true
    Write-Host "Started app: cmd.exe PID $($script:CmdPid), QueenZone.Web.exe PID $($script:AppPid)"
}

function Start-AppMacOS {
    param(
        [string] $AppDir,
        [string] $EnvironmentName,
        [string] $Urls,
        [string] $ConnectionString
    )

    # Launch the framework-dependent DLL through dotnet on PATH. The native app host only
    # searches DOTNET_ROOT and standard install locations, so it cannot find SDKs installed in
    # a user-local or tool-managed directory even when `dotnet` works.
    $dllPath = Join-Path $AppDir "QueenZone.Web.dll"
    if (-not (Test-Path $dllPath)) {
        throw "Published app not found: $dllPath"
    }

    if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
        throw "Required command 'dotnet' was not found on PATH."
    }

    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = "dotnet"
    $psi.Arguments = "`"$dllPath`""
    $psi.WorkingDirectory = $AppDir
    $psi.UseShellExecute = $false
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    $psi.CreateNoWindow = $true
    $psi.EnvironmentVariables["ASPNETCORE_ENVIRONMENT"] = $EnvironmentName
    $psi.EnvironmentVariables["ASPNETCORE_URLS"] = $Urls
    $psi.EnvironmentVariables["ASPNETCORE_CONTENTROOT"] = $AppDir
    if (-not [string]::IsNullOrWhiteSpace($ConnectionString)) {
        $psi.EnvironmentVariables["ConnectionStrings__QueenZoneLegacy"] = $ConnectionString
    }

    $proc = New-Object System.Diagnostics.Process
    $proc.StartInfo = $psi
    [void]$proc.Start()

    # Drain logs asynchronously so the process does not block on full pipe buffers.
    $script:MacStdoutTask = $proc.StandardOutput.ReadToEndAsync()
    $script:MacStderrTask = $proc.StandardError.ReadToEndAsync()
    $script:MacProcess = $proc
    $script:AppPid = [int]$proc.Id
    $script:AppPid | Out-File -Encoding ascii $appPidFile
    $script:StartedApp = $true
    Write-Host "Started app via dotnet: QueenZone.Web.dll PID $($script:AppPid)"
}

function Save-MacAppLogs {
    if ($null -eq $script:MacStdoutTask -and $null -eq $script:MacStderrTask) {
        return
    }

    try {
        $outText = ""
        $errText = ""
        if ($null -ne $script:MacStdoutTask) {
            if (-not $script:MacStdoutTask.Wait(5000)) {
                Write-Warning "Timed out waiting for local app stdout capture."
            }
            if ($script:MacStdoutTask.IsCompleted) {
                $outText = $script:MacStdoutTask.Result
            }
        }
        if ($null -ne $script:MacStderrTask) {
            if (-not $script:MacStderrTask.Wait(2000)) {
                Write-Warning "Timed out waiting for local app stderr capture."
            }
            if ($script:MacStderrTask.IsCompleted) {
                $errText = $script:MacStderrTask.Result
            }
        }
        Set-Content -Path $logPath -Value $outText -Encoding utf8 -ErrorAction SilentlyContinue
        Set-Content -Path $errPath -Value $errText -Encoding utf8 -ErrorAction SilentlyContinue
    }
    catch {
        # ignore log capture failures
    }
}

function Stop-StartedApp {
    if (-not $script:StartedApp) {
        return
    }

    Write-Host "Stopping local E2E app..."

    if ($null -ne $script:AppPid) {
        Stop-Process -Id $script:AppPid -Force -ErrorAction SilentlyContinue
    }
    if ($null -ne $script:CmdPid) {
        Stop-Process -Id $script:CmdPid -Force -ErrorAction SilentlyContinue
    }
    if ($null -ne $script:MacProcess -and -not $script:MacProcess.HasExited) {
        try { $script:MacProcess.Kill() } catch { }
    }

    # Belt-and-suspenders: Start can fall back to cmd.exe's PID if child lookup misses the
    # real QueenZone.Web.exe, which orphans it. An orphaned QueenZone.Web.exe keeps its own
    # exe locked and breaks the *next* CI checkout with EPERM unlink. Kill by image name too.
    Stop-StrayQueenZoneWeb

    if (Test-IsWindowsOS) {
        for ($i = 1; $i -le 15; $i++) {
            if (-not (Get-Process -Name "QueenZone.Web" -ErrorAction SilentlyContinue)) {
                Write-Host "QueenZone.Web.exe confirmed stopped"
                break
            }
            Start-Sleep -Milliseconds 500
        }
    }
    elseif (Test-IsMacOS) {
        for ($i = 1; $i -le 15; $i++) {
            $still = & pgrep -f QueenZone.Web 2>$null
            if (-not $still) {
                Write-Host "QueenZone.Web confirmed stopped"
                break
            }
            Start-Sleep -Milliseconds 500
        }
        Save-MacAppLogs
    }

    $script:StartedApp = $false
}

function Invoke-DotNet {
    param([string[]] $Arguments)

    Write-Host ">> dotnet $($Arguments -join ' ')"
    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet $($Arguments[0]) failed with exit code $LASTEXITCODE"
    }
}

function Update-SqlExpressMirrorMigrations {
    Write-Host "Applying EF migrations to the SQL Express mirror (not production Azure SQL)."
    Write-Host "Sync/skip_sync can leave Express without modern tables such as QuizSprintRuns."
    Invoke-DotNet -Arguments @("tool", "restore")
    Invoke-DotNet -Arguments @(
        "ef", "database", "update",
        "--project", "src/QueenZone.Data/QueenZone.Data.csproj",
        "--startup-project", "src/QueenZone.Web/QueenZone.Web.csproj"
    )
}

# --- Mode resolution -----------------------------------------------------------

$BaseUrl = $BaseUrl.TrimEnd("/")
if ([string]::IsNullOrWhiteSpace($BaseUrl)) {
    throw "BaseUrl must be a non-empty http(s) URL."
}

$startsApp = $false
$aspNetEnvironment = $null
$testFilter = $null
$connectionString = $null

switch ($Mode) {
    "Deterministic" {
        $startsApp = -not $SkipAppStart
        $aspNetEnvironment = "Testing"
        $testFilter = "TestCategory=Deterministic"
    }
    "RealData" {
        $startsApp = -not $SkipAppStart
        $aspNetEnvironment = "E2E"
        $testFilter = "TestCategory=RealData"
        $connectionString = $env:ConnectionStrings__QueenZoneLegacy
        if ([string]::IsNullOrWhiteSpace($connectionString)) {
            throw "RealData mode requires ConnectionStrings__QueenZoneLegacy (SQL Express mirror)."
        }
        & (Join-Path $PSScriptRoot "Assert-SqlExpressMirrorConnection.ps1") `
            -ConnectionString $connectionString
        Update-SqlExpressMirrorMigrations
    }
    "LiveSite" {
        $startsApp = $false
        $testFilter = "TestCategory=RealData&TestCategory=ReadOnly"
        $env:E2E_READONLY = "true"
        if (Test-IsLocalHostUrl -Url $BaseUrl) {
            throw "LiveSite mode refuses a localhost BaseUrl ('$BaseUrl'). Point -BaseUrl at the deployed site."
        }
        if ($SkipAppStart) {
            Write-Host "SkipAppStart is implied for LiveSite; ignoring the switch."
        }
    }
}

if (-not [string]::IsNullOrWhiteSpace($CategoryFilter)) {
    $testFilter = "$testFilter&FullyQualifiedName~$CategoryFilter"
}

Write-Host "Run-E2E mode=$Mode baseUrl=$BaseUrl filter=$testFilter startsApp=$startsApp"

# --- Build / browsers / publish ------------------------------------------------

# Clear leftovers from a previous failed run so publish/checkout are not blocked.
Stop-StrayQueenZoneWeb

if (-not $NoRestore) {
    Invoke-DotNet -Arguments @("restore", $e2eProject)
    if ($startsApp) {
        Invoke-DotNet -Arguments @("restore", $webProject)
    }
}

if (-not $NoBuild) {
    $buildArgs = @("build", $e2eProject, "--configuration", $Configuration)
    if ($NoRestore) { $buildArgs += "--no-restore" }
    Invoke-DotNet -Arguments $buildArgs
}

if (-not (Test-Path $playwrightScript)) {
    throw "Playwright installer not found at $playwrightScript. Build the E2E project first (omit -NoBuild)."
}

Write-Host "Installing Playwright Chromium via generated playwright.ps1 ..."
& $playwrightScript install chromium
if ($LASTEXITCODE -ne 0) {
    throw "playwright.ps1 install chromium failed with exit code $LASTEXITCODE"
}

if ($startsApp) {
    $publishArgs = @(
        "publish", $webProject,
        "--configuration", $Configuration,
        "--output", $publishDir
    )
    if ($NoRestore) { $publishArgs += "--no-restore" }
    Invoke-DotNet -Arguments $publishArgs
}

# --- Start / wait / test / stop ------------------------------------------------

$testExitCode = 0
try {
    if ($startsApp) {
        if (Test-IsWindowsOS) {
            Start-AppWindows `
                -AppDir $publishDir `
                -EnvironmentName $aspNetEnvironment `
                -Urls $BaseUrl `
                -ConnectionString $connectionString
        }
        elseif (Test-IsMacOS) {
            Start-AppMacOS `
                -AppDir $publishDir `
                -EnvironmentName $aspNetEnvironment `
                -Urls $BaseUrl `
                -ConnectionString $connectionString
        }
        else {
            throw "Unsupported OS for starting the local app. Use Windows or macOS, or pass -SkipAppStart."
        }

        Wait-ForAppReady -HealthUrl "$BaseUrl/health" -ProcessId $script:AppPid
    }
    elseif ($Mode -ne "LiveSite") {
        Write-Host "SkipAppStart: expecting an already-running app at $BaseUrl"
        Wait-ForAppReady -HealthUrl "$BaseUrl/health" -ProcessId $null -MaxAttempts 15 -SleepSeconds 1
    }

    $env:E2E_BASE_URL = $BaseUrl
    $env:E2E_ADMIN_EMAIL = $adminEmail
    $env:E2E_ARTIFACT_DIR = $artifactDir
    Write-Host "E2E artifact directory: $artifactDir"

    $testArgs = @(
        "test", $e2eProject,
        "--configuration", $Configuration,
        "--filter", $testFilter
    )
    if ($NoBuild) { $testArgs += "--no-build" }
    elseif ($NoRestore) { $testArgs += "--no-restore" }
    else { $testArgs += "--no-build" }

    if ($Mode -eq "RealData") {
        # One app + one SQL Express mirror: parallel RealData fixtures contend for connections and
        # amplify navigation/reindex timeouts (#568). Deterministic stays parallel.
        $testArgs += @("--", "NUnit.NumberOfTestWorkers=1")
    }
    elseif ($Mode -eq "LiveSite") {
        # Production is a single-instance B1 App Service. Cap to one test worker so the public
        # sweep uses at most ~2 browser contexts (main + mobile in the sitemap fixture) and
        # does not trip in-app rate limiting (AddQueenZoneRateLimiting).
        $testArgs += @("--", "NUnit.NumberOfTestWorkers=1")
        Write-Host "LiveSite: single NUnit worker (polite load against production)."
    }

    Write-Host ">> dotnet $($testArgs -join ' ')"
    & dotnet @testArgs
    $testExitCode = $LASTEXITCODE
    if ($testExitCode -ne 0) {
        throw "dotnet test failed with exit code $testExitCode"
    }

    Write-Host "Run-E2E completed successfully (mode=$Mode)."
}
finally {
    Stop-StartedApp
}

exit $testExitCode
