# Measures end-user frontend performance for key public pages with Lighthouse.
# Advisory by default: prints budget comparisons without failing the process.
#
# Prerequisites:
#   - Node.js + npx (uses `npx lighthouse`)
#   - Chrome/Chromium available to chrome-launcher
#   - Target site reachable at -BaseUrl (local publish, `dotnet run`, or remote URL)
#
# Examples:
#   # Against an already-running local app:
#   .\scripts\Measure-FrontendPerformance.ps1 -BaseUrl http://127.0.0.1:5099
#
#   # Publish sample-data app, measure mobile+desktop, write results:
#   .\scripts\Measure-FrontendPerformance.ps1 -StartLocalApp -FormFactor both
#
#   # Production-style URL (no app start):
#   .\scripts\Measure-FrontendPerformance.ps1 -BaseUrl https://www.queenzone.org -FormFactor mobile
#
#   # Hard gate (optional; not used in CI yet):
#   .\scripts\Measure-FrontendPerformance.ps1 -FailOnBudget

param(
    [string]$BaseUrl = "http://127.0.0.1:5099",

    [string]$OutputDir = "",

    [ValidateSet("mobile", "desktop", "both")]
    [string]$FormFactor = "mobile",

    [string[]]$Paths = @(),

    [string]$BudgetsFile = "",

    [switch]$StartLocalApp,

    [switch]$FailOnBudget,

    [switch]$IncludeRepeatLoad,

    [int]$Runs = 1,

    [string]$Configuration = "Release",

    [string]$LighthousePackage = "lighthouse@13.4.0"
)

$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"
$script:MeasurementFailed = $false
$script:OverBudgetCount = 0

function Resolve-RepoRoot {
    $scriptDir = Split-Path -Parent $PSCommandPath
    return (Resolve-Path (Join-Path $scriptDir "..")).Path
}

function Ensure-Command {
    param([string]$Name)
    if (-not (Get-Command $Name -ErrorAction SilentlyContinue)) {
        throw "Required command '$Name' was not found on PATH."
    }
}

function Wait-ForUrl {
    param(
        [string]$Url,
        [int]$TimeoutSeconds = 60
    )

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        try {
            $response = Invoke-WebRequest -Uri $Url -UseBasicParsing -TimeoutSec 3
            if ($response.StatusCode -ge 200 -and $response.StatusCode -lt 500) {
                return
            }
        }
        catch {
            # keep polling
        }
        Start-Sleep -Seconds 1
    }

    throw "Timed out waiting for $Url"
}

function Get-DefaultPaths {
    return @(
        "/",
        "/news",
        "/news/1003/queenzone-modernisation-begins",
        "/forum",
        "/forum/1/the-music",
        "/forum/topic/1002/ranking-every-studio-album"
    )
}

function Get-MetricValue {
    param(
        [object]$Audits,
        [string]$AuditId,
        [string]$Property = "numericValue"
    )

    if ($null -eq $Audits) {
        return $null
    }

    $audit = $Audits.$AuditId
    if ($null -eq $audit) {
        return $null
    }

    return $audit.$Property
}

function Get-RequestCount {
    param([object]$Audits)

    $details = Get-MetricValue -Audits $Audits -AuditId "network-requests" -Property "details"
    if ($null -eq $details -or $null -eq $details.items) {
        $resourceSummary = Get-MetricValue -Audits $Audits -AuditId "resource-summary" -Property "details"
        if ($null -ne $resourceSummary -and $null -ne $resourceSummary.items) {
            $total = $resourceSummary.items | Where-Object { $_.resourceType -eq "total" } | Select-Object -First 1
            if ($null -ne $total) {
                return [int]$total.requestCount
            }
        }
        return $null
    }

    return @($details.items).Count
}

function Get-BudgetForPath {
    param(
        [object]$Budgets,
        [string]$FormFactorName,
        [string]$Path
    )

    $defaults = $Budgets.defaults.$FormFactorName
    $override = $null
    if ($null -ne $Budgets.paths -and $null -ne $Budgets.paths.$Path) {
        $override = $Budgets.paths.$Path
    }

    $result = [ordered]@{
        lcpMs                = $defaults.lcpMs
        cls                  = $defaults.cls
        totalByteWeight      = $defaults.totalByteWeight
        requestCount         = $defaults.requestCount
        serverResponseTimeMs = $defaults.serverResponseTimeMs
        label                = $Path
    }

    if ($null -ne $override) {
        foreach ($key in @("lcpMs", "cls", "totalByteWeight", "requestCount", "serverResponseTimeMs", "label")) {
            if ($null -ne $override.$key) {
                $result[$key] = $override.$key
            }
        }
    }

    return [pscustomobject]$result
}

function Format-Bytes {
    param([Nullable[double]]$Bytes)
    if ($null -eq $Bytes) {
        return "n/a"
    }
    if ($Bytes -ge 1MB) {
        return ("{0:N2} MB" -f ($Bytes / 1MB))
    }
    if ($Bytes -ge 1KB) {
        return ("{0:N1} KB" -f ($Bytes / 1KB))
    }
    return ("{0:N0} B" -f $Bytes)
}

function Compare-Metric {
    param(
        [string]$Name,
        [Nullable[double]]$Actual,
        [Nullable[double]]$Budget,
        [switch]$LowerIsBetter
    )

    if ($null -eq $Actual -or $null -eq $Budget) {
        return [pscustomobject]@{
            Name     = $Name
            Actual   = $Actual
            Budget   = $Budget
            Status   = "skipped"
            Within   = $true
        }
    }

    $within = if ($LowerIsBetter) { $Actual -le $Budget } else { $Actual -ge $Budget }
    return [pscustomobject]@{
        Name   = $Name
        Actual = $Actual
        Budget = $Budget
        Status = $(if ($within) { "ok" } else { "over" })
        Within = $within
    }
}

function Move-LighthouseReports {
    param([string]$BasePath, [string]$JsonPath, [string]$HtmlPath, [int]$ExitCode)

    $producedJson = "$basePath.report.json"
    $producedHtml = "$basePath.report.html"
    if (-not (Test-Path $producedJson)) {
        if (Test-Path "$basePath.json") {
            $producedJson = "$basePath.json"
            $producedHtml = "$basePath.html"
        }
        elseif (Test-Path $basePath) {
            # Single-output mode may write the bare path with no extension.
            $producedJson = $basePath
        }
        else {
            throw "Lighthouse did not produce JSON report at $basePath.report.json (exit $ExitCode)"
        }
    }

    if ($producedJson -ne $JsonPath) {
        Move-Item -Force $producedJson $JsonPath
    }
    if ((Test-Path $producedHtml) -and $producedHtml -ne $HtmlPath) {
        Move-Item -Force $producedHtml $HtmlPath
    }

}

function Invoke-LighthouseRun {
    param(
        [string]$Url,
        [string]$FormFactorName,
        [string]$JsonPath,
        [string]$HtmlPath,
        [string]$WorkDir,
        [switch]$DisableStorageReset,
        [string]$Package
    )

    # Lighthouse appends .report.json / .report.html when multiple formats share one path.
    $basePath = $JsonPath
    if ($basePath.EndsWith(".report.json", [System.StringComparison]::OrdinalIgnoreCase)) {
        $basePath = $basePath.Substring(0, $basePath.Length - ".report.json".Length)
    }
    elseif ($basePath.EndsWith(".json", [System.StringComparison]::OrdinalIgnoreCase)) {
        $basePath = $basePath.Substring(0, $basePath.Length - ".json".Length)
    }

    $userDataDir = Join-Path $WorkDir ("chrome-user-data-" + [guid]::NewGuid().ToString("N").Substring(0, 8))
    $tmpDir = Join-Path $WorkDir "tmp"
    New-Item -ItemType Directory -Force -Path $userDataDir | Out-Null
    New-Item -ItemType Directory -Force -Path $tmpDir | Out-Null

    # Keep Chrome profile + temp under the results folder. On some Windows setups,
    # chrome-launcher fails with EPERM while deleting its temp dir after a successful run.
    $previousTemp = $env:TEMP
    $previousTmp = $env:TMP
    $env:TEMP = $tmpDir
    $env:TMP = $tmpDir

    $chromeFlags = "--headless=new --no-sandbox --disable-dev-shm-usage --user-data-dir=$userDataDir"
    $lighthouseArgs = @(
        "--yes",
        $Package,
        $Url,
        "--only-categories=performance",
        "--output=json",
        "--output=html",
        "--output-path=$basePath",
        "--chrome-flags=$chromeFlags",
        "--quiet"
    )

    if ($FormFactorName -eq "desktop") {
        $lighthouseArgs += "--preset=desktop"
    }
    else {
        # Default Lighthouse mobile emulation (slow 4G-class simulated throttling).
        $lighthouseArgs += "--form-factor=mobile"
        $lighthouseArgs += "--screenEmulation.mobile"
        $lighthouseArgs += "--throttling-method=simulate"
    }

    if ($DisableStorageReset) {
        $lighthouseArgs += "--disable-storage-reset"
    }

    Write-Information -InformationAction Continue "  lighthouse $Url ($FormFactorName$(if ($DisableStorageReset) { ', repeat' }))"
    try {
        & npx @lighthouseArgs
        $lhExit = $LASTEXITCODE
    }
    finally {
        $env:TEMP = $previousTemp
        $env:TMP = $previousTmp
    }

    Move-LighthouseReports -BasePath $basePath -JsonPath $JsonPath -HtmlPath $HtmlPath -ExitCode $lhExit

    $report = Get-Content -Raw -Path $JsonPath | ConvertFrom-Json
    if ($null -ne $report.runtimeError -and -not [string]::IsNullOrWhiteSpace([string]$report.runtimeError.code)) {
        throw "Lighthouse runtime error for $Url : $($report.runtimeError.code) - $($report.runtimeError.message)"
    }

    if ($lhExit -ne 0) {
        # Successful metrics with a non-zero exit usually mean Windows could not delete
        # chrome-launcher's temp directory (EPERM). Keep the report and continue.
        Write-Warning "Lighthouse exited with code $lhExit for $Url, but a usable report was written. Continuing."
    }

    # Best-effort cleanup of per-run Chrome profile (may fail if files are still locked).
    try {
        Remove-Item -Recurse -Force $userDataDir -ErrorAction SilentlyContinue
    }
    catch {
        # ignore
    }
}

function Convert-ReportToResult {
    param(
        [string]$JsonPath,
        [string]$Path,
        [string]$FormFactorName,
        [string]$LoadKind,
        [object]$Budgets,
        [int]$RunIndex
    )

    $report = Get-Content -Raw -Path $JsonPath | ConvertFrom-Json
    $audits = $report.audits
    $categories = $report.categories

    $lcpMs = Get-MetricValue -Audits $audits -AuditId "largest-contentful-paint"
    $cls = Get-MetricValue -Audits $audits -AuditId "cumulative-layout-shift"
    $fcpMs = Get-MetricValue -Audits $audits -AuditId "first-contentful-paint"
    $tbtMs = Get-MetricValue -Audits $audits -AuditId "total-blocking-time"
    $siMs = Get-MetricValue -Audits $audits -AuditId "speed-index"
    $totalBytes = Get-MetricValue -Audits $audits -AuditId "total-byte-weight"
    $serverMs = Get-MetricValue -Audits $audits -AuditId "server-response-time"
    $requestCount = Get-RequestCount -Audits $audits
    $score = $null
    if ($null -ne $categories.performance) {
        $score = [math]::Round([double]$categories.performance.score * 100, 0)
    }

    $budget = Get-BudgetForPath -Budgets $Budgets -FormFactorName $FormFactorName -Path $Path
    $comparisons = @(
        (Compare-Metric -Name "lcpMs" -Actual $lcpMs -Budget $budget.lcpMs -LowerIsBetter),
        (Compare-Metric -Name "cls" -Actual $cls -Budget $budget.cls -LowerIsBetter),
        (Compare-Metric -Name "totalByteWeight" -Actual $totalBytes -Budget $budget.totalByteWeight -LowerIsBetter),
        (Compare-Metric -Name "requestCount" -Actual $requestCount -Budget $budget.requestCount -LowerIsBetter),
        (Compare-Metric -Name "serverResponseTimeMs" -Actual $serverMs -Budget $budget.serverResponseTimeMs -LowerIsBetter)
    )

    $withinBudget = -not ($comparisons | Where-Object { -not $_.Within })

    return [ordered]@{
        path                 = $Path
        label                = $budget.label
        formFactor           = $FormFactorName
        loadKind             = $LoadKind
        run                  = $RunIndex
        performanceScore     = $score
        lcpMs                = if ($null -ne $lcpMs) { [math]::Round([double]$lcpMs, 0) } else { $null }
        cls                  = if ($null -ne $cls) { [math]::Round([double]$cls, 3) } else { $null }
        fcpMs                = if ($null -ne $fcpMs) { [math]::Round([double]$fcpMs, 0) } else { $null }
        tbtMs                = if ($null -ne $tbtMs) { [math]::Round([double]$tbtMs, 0) } else { $null }
        speedIndexMs         = if ($null -ne $siMs) { [math]::Round([double]$siMs, 0) } else { $null }
        totalByteWeight      = if ($null -ne $totalBytes) { [int][math]::Round([double]$totalBytes, 0) } else { $null }
        requestCount         = $requestCount
        serverResponseTimeMs = if ($null -ne $serverMs) { [math]::Round([double]$serverMs, 0) } else { $null }
        withinBudget         = $withinBudget
        budgetComparisons    = $comparisons
        reportJson           = $JsonPath
    }
}

# --- main ---

$repoRoot = Resolve-RepoRoot
Set-Location $repoRoot

Ensure-Command -Name "npx"
Ensure-Command -Name "node"

if ([string]::IsNullOrWhiteSpace($OutputDir)) {
    $stamp = Get-Date -Format "yyyy-MM-dd-HHmmss"
    $OutputDir = Join-Path $repoRoot "docs/performance/results/$stamp"
}
elseif (-not [System.IO.Path]::IsPathRooted($OutputDir)) {
    $OutputDir = Join-Path $repoRoot $OutputDir
}

if ([string]::IsNullOrWhiteSpace($BudgetsFile)) {
    $BudgetsFile = Join-Path $repoRoot "docs/performance/frontend-performance-budgets.json"
}
elseif (-not [System.IO.Path]::IsPathRooted($BudgetsFile)) {
    $BudgetsFile = Join-Path $repoRoot $BudgetsFile
}

if (-not (Test-Path $BudgetsFile)) {
    throw "Budgets file not found: $BudgetsFile"
}

$budgets = Get-Content -Raw -Path $BudgetsFile | ConvertFrom-Json

if ($Paths.Count -eq 0) {
    if ($null -ne $budgets.paths) {
        $Paths = @($budgets.paths.PSObject.Properties.Name)
    }
    else {
        $Paths = Get-DefaultPaths
    }
}
else {
    # Support -Paths /,/news and accidental single-string array forms from -File invocation.
    $expanded = New-Object System.Collections.Generic.List[string]
    foreach ($entry in $Paths) {
        foreach ($part in ($entry -split '[,;]+')) {
            $trimmed = $part.Trim().Trim("'").Trim('"')
            if (-not [string]::IsNullOrWhiteSpace($trimmed)) {
                $expanded.Add($trimmed) | Out-Null
            }
        }
    }
    $Paths = $expanded.ToArray()
}

$Paths = $Paths | ForEach-Object {
    if ($_ -eq "/") { $_ } else { "/" + $_.TrimStart("/") }
}

if ($BaseUrl -notmatch '^https?://') {
    throw "BaseUrl must be an absolute http(s) URL. Received: '$BaseUrl'"
}

$formFactors = if ($FormFactor -eq "both") { @("mobile", "desktop") } else { @($FormFactor) }

New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null
$rawDir = Join-Path $OutputDir "raw"
New-Item -ItemType Directory -Force -Path $rawDir | Out-Null

$appProcess = $null
$appPidFile = Join-Path $OutputDir "local-app.pid"
$startedApp = $false
$stdoutTask = $null
$stderrTask = $null

try {
    if ($StartLocalApp) {
        Ensure-Command -Name "dotnet"
        $publishDir = Join-Path $repoRoot "perf-app"
        Write-Information -InformationAction Continue "Publishing web app to $publishDir ..."
        & dotnet publish (Join-Path $repoRoot "src/QueenZone.Web/QueenZone.Web.csproj") `
            --configuration $Configuration `
            --output $publishDir
        if ($LASTEXITCODE -ne 0) {
            throw "dotnet publish failed with exit code $LASTEXITCODE"
        }

        $exe = Join-Path $publishDir "QueenZone.Web.exe"
        if (-not (Test-Path $exe)) {
            $exe = Join-Path $publishDir "QueenZone.Web"
        }
        if (-not (Test-Path $exe)) {
            throw "Published app binary not found under $publishDir"
        }

        Write-Information -InformationAction Continue "Starting local app at $BaseUrl ..."
        $psi = New-Object System.Diagnostics.ProcessStartInfo
        $psi.FileName = $exe
        $psi.WorkingDirectory = $publishDir
        $psi.UseShellExecute = $false
        $psi.RedirectStandardOutput = $true
        $psi.RedirectStandardError = $true
        $psi.CreateNoWindow = $true
        $psi.EnvironmentVariables["ASPNETCORE_ENVIRONMENT"] = "Testing"
        $psi.EnvironmentVariables["ASPNETCORE_URLS"] = $BaseUrl
        $psi.EnvironmentVariables["ASPNETCORE_CONTENTROOT"] = $publishDir

        $appProcess = New-Object System.Diagnostics.Process
        $appProcess.StartInfo = $psi
        [void]$appProcess.Start()
        $appProcess.Id | Out-File -Encoding ascii $appPidFile
        $startedApp = $true

        # Drain logs asynchronously so the process does not block on full buffers.
        $stdoutTask = $appProcess.StandardOutput.ReadToEndAsync()
        $stderrTask = $appProcess.StandardError.ReadToEndAsync()

        Wait-ForUrl -Url ($BaseUrl.TrimEnd("/") + "/health") -TimeoutSeconds 90
        Write-Information -InformationAction Continue "Local app is ready."
    }

    # Health check even when the caller starts the app.
    try {
        Wait-ForUrl -Url ($BaseUrl.TrimEnd("/") + "/health") -TimeoutSeconds 15
    }
    catch {
        Write-Warning "Health endpoint not reachable at $BaseUrl/health. Continuing anyway; page runs may fail."
    }

    $results = New-Object System.Collections.Generic.List[object]

    foreach ($factor in $formFactors) {
        foreach ($path in $Paths) {
            $url = ($BaseUrl.TrimEnd("/") + $path)
            $safeName = ($path -replace '[^a-zA-Z0-9]+', '-').Trim('-')
            if ([string]::IsNullOrWhiteSpace($safeName)) {
                $safeName = "home"
            }

            for ($run = 1; $run -le $Runs; $run++) {
                $jsonPath = Join-Path $rawDir "$safeName-$factor-first-run$run.report.json"
                $htmlPath = Join-Path $rawDir "$safeName-$factor-first-run$run.report.html"
                # Use base path without extension; Invoke-LighthouseRun normalizes outputs.
                Invoke-LighthouseRun -Url $url -FormFactorName $factor -JsonPath $jsonPath -HtmlPath $htmlPath -WorkDir $rawDir -Package $LighthousePackage
                $result = Convert-ReportToResult -JsonPath $jsonPath -Path $path -FormFactorName $factor -LoadKind "first" -Budgets $budgets -RunIndex $run
                $results.Add([pscustomobject]$result) | Out-Null

                if ($IncludeRepeatLoad) {
                    $jsonPath2 = Join-Path $rawDir "$safeName-$factor-repeat-run$run.report.json"
                    $htmlPath2 = Join-Path $rawDir "$safeName-$factor-repeat-run$run.report.html"
                    Invoke-LighthouseRun -Url $url -FormFactorName $factor -JsonPath $jsonPath2 -HtmlPath $htmlPath2 -WorkDir $rawDir -DisableStorageReset -Package $LighthousePackage
                    $result2 = Convert-ReportToResult -JsonPath $jsonPath2 -Path $path -FormFactorName $factor -LoadKind "repeat" -Budgets $budgets -RunIndex $run
                    $results.Add([pscustomobject]$result2) | Out-Null
                }
            }
        }
    }

    $summary = [ordered]@{
        generatedAtUtc = (Get-Date).ToUniversalTime().ToString("o")
        baseUrl        = $BaseUrl
        formFactors    = $formFactors
        runs           = $Runs
        includeRepeat  = [bool]$IncludeRepeatLoad
        budgetsFile    = $BudgetsFile
        advisory       = $true
        failOnBudget   = [bool]$FailOnBudget
        results        = $results
    }

    $summaryJsonPath = Join-Path $OutputDir "summary.json"
    ($summary | ConvertTo-Json -Depth 8) | Set-Content -Path $summaryJsonPath -Encoding utf8

    # Markdown summary for easy before/after comparison in PRs.
    $md = New-Object System.Collections.Generic.List[string]
    $md.Add("# Frontend performance results") | Out-Null
    $md.Add("") | Out-Null
    $md.Add("- Generated (UTC): $($summary.generatedAtUtc)") | Out-Null
    $md.Add("- Base URL: ``$BaseUrl``") | Out-Null
    $md.Add("- Form factors: $($formFactors -join ', ')") | Out-Null
    $md.Add("- Runs per path: $Runs") | Out-Null
    $md.Add("- Repeat-load pass: $(if ($IncludeRepeatLoad) { 'yes' } else { 'no' })") | Out-Null
    $md.Add("- Budgets: advisory (FailOnBudget=$(if ($FailOnBudget) { 'true' } else { 'false' }))") | Out-Null
    $md.Add("") | Out-Null
    $md.Add("| Path | Factor | Load | Score | LCP (ms) | CLS | Transfer | Requests | TTFB (ms) | Budget |") | Out-Null
    $md.Add("| --- | --- | --- | ---: | ---: | ---: | ---: | ---: | ---: | --- |") | Out-Null

    $overBudgetCount = 0
    foreach ($r in $results) {
        if (-not $r.withinBudget) {
            $overBudgetCount++
            $budgetLabel = "OVER"
        }
        else {
            $budgetLabel = "ok"
        }
        $transfer = Format-Bytes $r.totalByteWeight
        $md.Add("| ``$($r.path)`` | $($r.formFactor) | $($r.loadKind) | $($r.performanceScore) | $($r.lcpMs) | $($r.cls) | $transfer | $($r.requestCount) | $($r.serverResponseTimeMs) | $budgetLabel |") | Out-Null
    }

    $md.Add("") | Out-Null
    $md.Add("## Budget detail") | Out-Null
    $md.Add("") | Out-Null
    foreach ($r in $results) {
        $md.Add("### ``$($r.path)`` ($($r.formFactor), $($r.loadKind), run $($r.run))") | Out-Null
        $md.Add("") | Out-Null
        $md.Add("| Metric | Actual | Budget | Status |") | Out-Null
        $md.Add("| --- | ---: | ---: | --- |") | Out-Null
        foreach ($c in $r.budgetComparisons) {
            $actualDisplay = $c.Actual
            $budgetDisplay = $c.Budget
            if ($c.Name -eq "totalByteWeight") {
                $actualDisplay = Format-Bytes $c.Actual
                $budgetDisplay = Format-Bytes $c.Budget
            }
            elseif ($c.Name -eq "cls" -and $null -ne $c.Actual) {
                $actualDisplay = ("{0:N3}" -f [double]$c.Actual)
            }
            elseif ($null -ne $c.Actual -and $c.Name -match 'Ms$') {
                $actualDisplay = [math]::Round([double]$c.Actual, 0)
            }
            $md.Add("| $($c.Name) | $actualDisplay | $budgetDisplay | $($c.Status) |") | Out-Null
        }
        $md.Add("") | Out-Null
        $md.Add("Raw report: ``$($r.reportJson)``") | Out-Null
        $md.Add("") | Out-Null
    }

    $md.Add("## How to compare") | Out-Null
    $md.Add("") | Out-Null
    $md.Add("1. Keep this ``summary.md`` / ``summary.json`` from the before run.") | Out-Null
    $md.Add("2. Re-run the same command after your change (same ``-BaseUrl``, form factor, and paths).") | Out-Null
    $md.Add("3. Diff LCP, CLS, transfer size, and request count. Prefer the median of multiple ``-Runs`` when variance is high.") | Out-Null
    $md.Add("4. Attach both summaries in the PR when the change is performance-related.") | Out-Null
    $md.Add("") | Out-Null
    $md.Add("See ``docs/performance/frontend-performance-checks.md`` for workflow details.") | Out-Null

    $summaryMdPath = Join-Path $OutputDir "summary.md"
    $md -join "`n" | Set-Content -Path $summaryMdPath -Encoding utf8

    Write-Information -InformationAction Continue ""
    Write-Information -InformationAction Continue "Wrote $summaryMdPath"
    Write-Information -InformationAction Continue "Wrote $summaryJsonPath"
    Write-Information -InformationAction Continue "Over-budget result rows: $overBudgetCount / $($results.Count)"
    $script:OverBudgetCount = $overBudgetCount

    if ($FailOnBudget -and $overBudgetCount -gt 0) {
        $script:MeasurementFailed = $true
        throw "One or more measurements exceeded documented budgets (FailOnBudget was set)."
    }
}
catch {
    $script:MeasurementFailed = $true
    throw
}
finally {
    if ($startedApp -and $null -ne $appProcess -and -not $appProcess.HasExited) {
        Write-Information -InformationAction Continue "Stopping local app (PID $($appProcess.Id))..."
        try {
            Stop-Process -Id $appProcess.Id -Force -ErrorAction Stop
        }
        catch {
            try { $appProcess.Kill() } catch { }
        }
    }
    elseif ($startedApp -and (Test-Path $appPidFile)) {
        $savedPid = Get-Content $appPidFile | Select-Object -First 1
        if ($savedPid) {
            Stop-Process -Id ([int]$savedPid) -Force -ErrorAction SilentlyContinue
        }
    }

    # Capture logs only after the process has been stopped so ReadToEnd can finish.
    if ($startedApp -and $null -ne $stdoutTask) {
        try {
            if (-not $stdoutTask.Wait(5000)) {
                Write-Warning "Timed out waiting for local app stdout capture."
            }
            if ($null -ne $stderrTask -and -not $stderrTask.Wait(2000)) {
                Write-Warning "Timed out waiting for local app stderr capture."
            }
            $outText = if ($stdoutTask.IsCompleted) { $stdoutTask.Result } else { "" }
            $errText = if ($null -ne $stderrTask -and $stderrTask.IsCompleted) { $stderrTask.Result } else { "" }
            $combined = "--- stdout ---`n$outText`n--- stderr ---`n$errText"
            Set-Content -Path (Join-Path $OutputDir "local-app.log") -Value $combined -Encoding utf8
        }
        catch {
            # ignore log capture failures
        }
    }
}

Write-Information -InformationAction Continue "Done."

# Native lighthouse exit codes can otherwise leak into the caller's $LASTEXITCODE.
if ($script:MeasurementFailed) {
    exit 1
}

exit 0
