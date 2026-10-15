# Shared implementation for the web proof runner. No host or browser starts when dot-sourced.
function New-QueenZoneWebProof {
    param([string] $RepoRoot, $Entry, [string] $BaseUrl, [string] $Detail = "")
    [ordered]@{
        feature = $Entry.id
        sha = Get-QueenZoneHeadSha -RepoRoot $RepoRoot
        workingTreeDirty = [bool] (& git -C $RepoRoot status --porcelain)
        environment = "Testing"
        url = $BaseUrl
        platform = "web / Chromium"
        command = "pwsh -File scripts/Verify-Web.ps1 -Feature $($Entry.id)"
        automated = "NOT RUN"
        screenshot = "NOT RUN"
        tests = @()
        visualInspection = "NOT RUN"
        detail = $Detail
    }
}

function Resolve-QueenZoneWebProofRoute {
    param([string] $UrlPath)
    foreach ($pair in @(
        @{ From = "{slug}"; To = "brian-may" },
        @{ From = "{picId}"; To = "101" },
        @{ From = "{id}"; To = "1003" },
        @{ From = "{pageNumber}"; To = "1" },
        @{ From = "{topicId}"; To = "1002" },
        @{ From = "{statusCode?}"; To = "404" },
        @{ From = "{*path}"; To = "index" }
    )) { $urlPath = $urlPath.Replace($pair.From, $pair.To) }
    if ($urlPath -match "\{") { throw "Unresolved route parameters in $urlPath; add an explicit Testing fixture before capturing proof." }
    return $urlPath
}

function Read-QueenZoneWebProofTestResult {
    param([string] $Trx, $Spec, $Code)
    $status = "FAIL"
    $total = 0
    $passed = 0
    if (Test-Path $trx) {
        [xml] $report = Get-Content -Raw -LiteralPath $trx
        $counters = $report.SelectSingleNode("//*[local-name()='Counters']")
        if ($null -ne $counters) {
            $total = [int] $counters.total
            $passed = [int] $counters.passed
            if ($code -eq 0 -and $total -gt 0 -and $passed -eq $total) { $status = "PASS" }
        }
    }
    return [ordered]@{ spec = $spec; result = $status; total = $total; passed = $passed; exitCode = $code }
}

function Set-QueenZoneWebProofOutcome {
    param($Proof, $Specs)
    $proof.automated = if ($proof.screenshot -eq "FAIL" -or @($proof.tests | Where-Object result -ne "PASS").Count -gt 0) { "FAIL" }
        elseif ($specs.Count -eq 0) { "NOT RUN" } else { "PASS" }
    $proof.detail = if ($specs.Count -eq 0) { "No mapped behavioural tests. Screenshot capture alone does not verify this feature." }
        else { "Every mapped fixture must execute at least one deterministic test, with no failures or skips." }
}

function Assert-QueenZoneAdminProofHost {
    param([string] $BaseUrl, [string] $Environment)
    if (([uri] $BaseUrl).Host -cne '127.0.0.1' -or $Environment -cne 'Testing' -or
        ($null -ne $env:ASPNETCORE_ENVIRONMENT -and $env:ASPNETCORE_ENVIRONMENT -cne 'Testing')) {
        throw "Admin proof requires the local Testing host at exactly 127.0.0.1."
    }
}

function Invoke-QueenZoneWebProofSpec {
    param([string] $Project, [string] $ProofDir, [string] $Spec)
    $name = [IO.Path]::GetFileNameWithoutExtension($Spec)
    if ($name -notmatch '^[A-Za-z_][A-Za-z0-9_]*$') { throw "Invalid mapped specification: $Spec" }
    $trx = Join-Path $ProofDir "$name.trx"
    # Never accept a stale report from a previous invocation.
    if (Test-Path $trx) { Remove-Item -LiteralPath $trx }
    & dotnet test $Project --no-build --no-restore --configuration Release --filter "(FullyQualifiedName~QueenZone.Web.E2E.$name.)&TestCategory=Deterministic" --logger "trx;LogFileName=$name.trx" --results-directory $ProofDir -- NUnit.NumberOfTestWorkers=1 | Out-Host
    return Read-QueenZoneWebProofTestResult -Trx $trx -Spec $Spec -Code $LASTEXITCODE
}

function Initialize-QueenZoneWebProofScreenshot {
    param($Proof, [string] $Playwright, [string] $Screenshot, [bool] $IsAdmin)
    if ($IsAdmin) {
        # Only accept a screenshot produced by this spec run with the admin header.
        if (Test-Path $Screenshot) { Remove-Item -LiteralPath $Screenshot }
        return;
    }
    & $Playwright screenshot --browser chromium --full-page $Proof.url $Screenshot | Out-Host
    $Proof.screenshot = if ($LASTEXITCODE -eq 0 -and (Test-Path $Screenshot)) { "PASS" } else { "FAIL" }
}

function Invoke-QueenZoneWebProof {
    param([string] $RepoRoot, $Entry, [string] $BaseUrl, [string] $ProofDir)
    $proof = New-QueenZoneWebProof -RepoRoot $RepoRoot -Entry $Entry -BaseUrl $BaseUrl
    $project = Join-Path $RepoRoot "tests/QueenZone.Web.E2E/QueenZone.Web.E2E.csproj"
    $playwright = Join-Path $RepoRoot "tests/QueenZone.Web.E2E/bin/Release/net10.0/playwright.ps1"
    $isAdmin = $Entry.PSObject.Properties.Name -contains 'signIn' -and $Entry.signIn -eq 'admin'
    try {
        if ($isAdmin) { Assert-QueenZoneAdminProofHost -BaseUrl $BaseUrl -Environment $proof.environment }
        if (-not (Test-Path $playwright)) { throw "Build the Release E2E project and install Chromium, or run scripts/Verify-Web.ps1." }
        $urlPath = Resolve-QueenZoneWebProofRoute -UrlPath ([string] $Entry.url)
        $proof.url = "$BaseUrl$urlPath"
        $screenshot = Join-Path $ProofDir "screenshot.png"
        Initialize-QueenZoneWebProofScreenshot -Proof $proof -Playwright $playwright -Screenshot $screenshot -IsAdmin $isAdmin
        $specs = @($Entry.specs | Select-Object -Unique)
        $savedUrl = $env:E2E_BASE_URL
        $savedArtifacts = $env:E2E_ARTIFACT_DIR
        $savedReadOnly = $env:E2E_READONLY
        $savedAdminEmail = $env:E2E_ADMIN_EMAIL
        try {
            $env:E2E_BASE_URL = $BaseUrl
            $env:E2E_ARTIFACT_DIR = $ProofDir
            $env:E2E_READONLY = "false"
            if ($isAdmin) { $env:E2E_ADMIN_EMAIL = "admin@test.local" }
            foreach ($spec in $specs) {
                $proof.tests += Invoke-QueenZoneWebProofSpec -Project $project -ProofDir $ProofDir -Spec $spec
            }
        }
        finally {
            $env:E2E_BASE_URL = $savedUrl
            $env:E2E_ARTIFACT_DIR = $savedArtifacts
            $env:E2E_READONLY = $savedReadOnly
            $env:E2E_ADMIN_EMAIL = $savedAdminEmail
            if ($isAdmin) { $proof.screenshot = if (Test-Path $screenshot) { "PASS" } else { "FAIL" } }
        }
        Set-QueenZoneWebProofOutcome -Proof $proof -Specs $specs
    }
    catch {
        $proof.automated = "FAIL"
        $proof.detail = $_.Exception.Message
    }
    return $proof
}

function Write-QueenZoneWebProof {
    param($Proof, [string] $ProofDir)
    New-Item -ItemType Directory -Force -Path $ProofDir | Out-Null
    $Proof | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $ProofDir "proof.json") -Encoding utf8
    $testLines = @($Proof.tests | ForEach-Object { "- $($_.spec): $($_.result) ($($_.passed)/$($_.total) passed)" })
    if ($testLines.Count -eq 0) { $testLines = @("- Behavioural tests: NOT RUN") }
    $command = $Proof.command
    @(
        "## Verification", "",
        "- Feature ids: $($Proof.feature)",
        "- Command: $command",
        "- Platform(s): $($Proof.platform)",
        "- Commit: $($Proof.sha); working tree dirty: $($Proof.workingTreeDirty)",
        "- Environment: $($Proof.environment); URL: $($Proof.url)",
        "- Result: automated $($Proof.automated); screenshot capture $($Proof.screenshot); visual inspection NOT RUN",
        "- Proof: $ProofDir (upload artifacts and replace this local path with a reviewable link)",
        "- Not verified: interactive changed journey and visual inspection. Record browser tool, actions, viewport, observed outcome and evidence after inspection.",
        "", $Proof.detail, "", $testLines
    ) | Set-Content -LiteralPath (Join-Path $ProofDir "proof.md") -Encoding utf8
    Write-Output "Automated: $($Proof.automated). Visual inspection: NOT RUN. Proof: $ProofDir"
}
