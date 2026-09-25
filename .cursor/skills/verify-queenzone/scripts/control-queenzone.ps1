#Requires -Version 7.0
<#
.SYNOPSIS
  Launch, doctor, and tear down an isolated QueenZone.Web Testing host for verify-queenzone.

.DESCRIPTION
  Starts QueenZone.Web with ASPNETCORE_ENVIRONMENT=Testing on a loopback port that is not
  the developer's 5146 profile or the Playwright E2E 5099 host. Records pid/url in
  .cursor/skills/verify-queenzone/.run/state.json. Cleanup kills only that process tree.
#>
[CmdletBinding()]
param(
    [Parameter(Position = 0, Mandatory = $true)]
    [ValidateSet("launch", "doctor", "url", "cleanup", "capture-proof")]
    [string] $Command,

    [int] $Port = 5199,

    [string] $Feature
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$SkillRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$RepoRoot = $null
$cursor = $SkillRoot
for ($i = 0; $i -lt 6; $i++) {
    $candidate = Split-Path -Parent $cursor
    if ([string]::IsNullOrWhiteSpace($candidate) -or $candidate -eq $cursor) {
        break
    }
    $cursor = $candidate
    if (Test-Path (Join-Path $cursor "QueenZone.sln")) {
        $RepoRoot = $cursor
        break
    }
}
if (-not $RepoRoot) {
    throw "Could not find QueenZone.sln above $SkillRoot."
}

. (Join-Path $RepoRoot "scripts/FeatureMap.ps1")

$RunDir = Join-Path $SkillRoot ".run"
$StatePath = Join-Path $RunDir "state.json"
$OutLogPath = Join-Path $RunDir "host.out.log"
$ErrLogPath = Join-Path $RunDir "host.err.log"
$ProjectPath = Join-Path $RepoRoot "src\QueenZone.Web\QueenZone.Web.csproj"
$SampleArticlePath = "/news/1003/queenzone-modernisation-begins"
$SampleArticleMarker = "QueenZone modernisation begins"

function Read-State {
    if (-not (Test-Path $StatePath)) {
        return $null
    }
    return Get-Content -Raw -Path $StatePath | ConvertFrom-Json
}

function Write-State {
    param($State)
    New-Item -ItemType Directory -Force -Path $RunDir | Out-Null
    ($State | ConvertTo-Json -Depth 5) | Set-Content -Path $StatePath -Encoding utf8
}

function Test-ProcessAlive {
    param([int] $ProcessId)
    try {
        $proc = Get-Process -Id $ProcessId -ErrorAction Stop
        return $null -ne $proc
    }
    catch {
        return $false
    }
}

function Get-PortOwnerPid {
    param([int] $ListenPort)
    $conn = Get-NetTCPConnection -LocalPort $ListenPort -State Listen -ErrorAction SilentlyContinue |
        Select-Object -First 1
    if ($conn) {
        return [int] $conn.OwningProcess
    }
    return $null
}

function Invoke-Health {
    param([string] $BaseUrl)
    $response = Invoke-WebRequest -Uri "$BaseUrl/health" -UseBasicParsing -TimeoutSec 5
    if ($response.StatusCode -ne 200) {
        throw "Health returned HTTP $($response.StatusCode)."
    }
    if ($response.Content -notmatch '"status"\s*:\s*"ok"') {
        throw "Health body was not { status: ok }: $($response.Content)"
    }
}

function Invoke-SampleArticle {
    param([string] $BaseUrl)
    $response = Invoke-WebRequest -Uri "$BaseUrl$SampleArticlePath" -UseBasicParsing -TimeoutSec 10
    if ($response.StatusCode -ne 200) {
        throw "Sample article returned HTTP $($response.StatusCode)."
    }
    if ($response.Content -notlike "*$SampleArticleMarker*") {
        throw "Sample article HTML did not contain '$SampleArticleMarker'. This host is not the Testing seed."
    }
}

function Stop-RecordedHost {
    param($State)
    if (-not $State) {
        return
    }
    $processId = [int] $State.pid
    if ($processId -gt 0 -and (Test-ProcessAlive -ProcessId $processId)) {
        & taskkill.exe /PID $processId /T /F | Out-Null
        Start-Sleep -Milliseconds 400
    }
}

switch ($Command) {
    "url" {
        $state = Read-State
        if (-not $state) {
            throw "No verification host is recorded. Run launch first."
        }
        Write-Output $state.url
        return
    }

    "cleanup" {
        $state = Read-State
        Stop-RecordedHost -State $state
        if (Test-Path $StatePath) {
            Remove-Item -Force $StatePath
        }
        Write-Output "Cleaned up verification host. Artifacts were left in place."
        return
    }

    "doctor" {
        $state = Read-State
        if (-not $state) {
            throw "Doctor failed: no .run/state.json. This skill has not launched a host."
        }
        $processId = [int] $state.pid
        if (-not (Test-ProcessAlive -ProcessId $processId)) {
            throw "Doctor failed: recorded pid $processId is not running."
        }
        $owner = Get-PortOwnerPid -ListenPort ([int] $state.port)
        if ($null -eq $owner) {
            throw "Doctor failed: nothing is listening on port $($state.port)."
        }
        if ($owner -ne $processId) {
            $children = Get-CimInstance Win32_Process -Filter "ParentProcessId=$processId" -ErrorAction SilentlyContinue
            $tree = @($processId) + @($children | ForEach-Object { [int] $_.ProcessId })
            if ($tree -notcontains $owner) {
                throw "Doctor failed: port $($state.port) is owned by pid $owner, not the recorded host $processId."
            }
        }
        Invoke-Health -BaseUrl $state.url
        Invoke-SampleArticle -BaseUrl $state.url
        Write-Output "Doctor ok: $($state.url) pid=$processId env=Testing sample=$SampleArticlePath"
        return
    }

    "launch" {
        $existing = Read-State
        if ($existing -and (Test-ProcessAlive -ProcessId ([int] $existing.pid))) {
            Invoke-Health -BaseUrl $existing.url
            Write-Output "Already running: $($existing.url) (pid $($existing.pid))"
            return
        }

        $owner = Get-PortOwnerPid -ListenPort $Port
        if ($null -ne $owner) {
            throw "Port $Port is already in use by pid $owner, which this skill did not start. Pass -Port with a free loopback port."
        }

        if ([string]::IsNullOrWhiteSpace($env:SIXLABORS_LICENSE_KEY)) {
            $import = Join-Path $RepoRoot "scripts\Import-SixLaborsLicense.ps1"
            if (Test-Path $import) {
                . $import
            }
        }

        New-Item -ItemType Directory -Force -Path $RunDir | Out-Null
        foreach ($log in @($OutLogPath, $ErrLogPath)) {
            if (Test-Path $log) {
                Remove-Item -Force $log
            }
        }

        $env:ASPNETCORE_ENVIRONMENT = "Testing"
        $env:ASPNETCORE_URLS = "http://127.0.0.1:$Port"
        Remove-Item Env:ConnectionStrings__QueenZoneLegacy -ErrorAction SilentlyContinue
        Remove-Item Env:ConnectionStrings__BlobStorage -ErrorAction SilentlyContinue

        $psi = @{
            FilePath               = "dotnet"
            ArgumentList           = @(
                "run",
                "--project", $ProjectPath,
                "--no-launch-profile"
            )
            WorkingDirectory       = $RepoRoot
            RedirectStandardOutput = $OutLogPath
            RedirectStandardError  = $ErrLogPath
            PassThru               = $true
        }
        $proc = Start-Process @psi

        $baseUrl = "http://127.0.0.1:$Port"
        $deadline = (Get-Date).AddMinutes(3)
        $ready = $false
        while ((Get-Date) -lt $deadline) {
            if (-not (Test-ProcessAlive -ProcessId $proc.Id)) {
                $tail = @(
                    if (Test-Path $OutLogPath) { Get-Content -Tail 20 $OutLogPath }
                    if (Test-Path $ErrLogPath) { Get-Content -Tail 20 $ErrLogPath }
                ) -join "`n"
                if ([string]::IsNullOrWhiteSpace($tail)) { $tail = "(no log)" }
                throw "Host process $($proc.Id) exited before becoming ready.`n$tail"
            }
            try {
                Invoke-Health -BaseUrl $baseUrl
                $ready = $true
                break
            }
            catch {
                Start-Sleep -Milliseconds 500
            }
        }
        if (-not $ready) {
            Stop-RecordedHost -State ([pscustomobject]@{ pid = $proc.Id })
            $tail = @(
                if (Test-Path $OutLogPath) { Get-Content -Tail 20 $OutLogPath }
                if (Test-Path $ErrLogPath) { Get-Content -Tail 20 $ErrLogPath }
            ) -join "`n"
            if ([string]::IsNullOrWhiteSpace($tail)) { $tail = "(no log)" }
            throw "Timed out waiting for $baseUrl/health.`n$tail"
        }

        Invoke-SampleArticle -BaseUrl $baseUrl

        Write-State ([pscustomobject]@{
                pid         = $proc.Id
                url         = $baseUrl
                port        = $Port
                environment = "Testing"
                startedAt   = (Get-Date).ToUniversalTime().ToString("o")
            })

        Write-Output "Launched $baseUrl (pid $($proc.Id))"
        return
    }

    "capture-proof" {
        if ([string]::IsNullOrWhiteSpace($Feature)) {
            throw "capture-proof requires -Feature <map-id> (for example web.home.index)."
        }
        $entries = Get-QueenZoneFeatureMap -RepoRoot $RepoRoot
        $mapped = Resolve-QueenZoneFeature -Entries $entries -IdOrAlias $Feature
        if (-not $mapped) {
            throw "Unknown feature '$Feature'."
        }
        $proofDir = Get-QueenZoneProofDir -RepoRoot $RepoRoot -FeatureId $mapped.id -Platform "web"
        $sha = Get-QueenZoneHeadSha -RepoRoot $RepoRoot
        $specs = @($mapped.specs | ForEach-Object { [string] $_ } | Where-Object { $_ })
        $playwright = @(
            (Join-Path $RepoRoot "tests/QueenZone.Web.E2E/bin/Release/net10.0/playwright.ps1"),
            (Join-Path $RepoRoot "tests/QueenZone.Web.E2E/bin/Debug/net10.0/playwright.ps1")
        ) | Where-Object { Test-Path $_ } | Select-Object -First 1

        $state = Read-State
        if (-not $state) {
            $detail = "NOT RUN: Testing host is not running, needs control-queenzone.ps1 launch then capture-proof -Feature $($mapped.id)"
            Write-QueenZoneProofMarkdown -Path (Join-Path $proofDir "proof.md") -FeatureId $mapped.id -Sha $sha -Platform "web" -Flows $specs -Result "NOT RUN" -Detail $detail
            Write-Output $detail
            exit 1
        }
        if (-not $playwright) {
            $detail = "NOT RUN: E2E playwright.ps1 is not built, needs dotnet build tests/QueenZone.Web.E2E/QueenZone.Web.E2E.csproj then playwright.ps1 install chromium"
            Write-QueenZoneProofMarkdown -Path (Join-Path $proofDir "proof.md") -FeatureId $mapped.id -Sha $sha -Platform "web" -Flows $specs -Result "NOT RUN" -Detail $detail
            Write-Output $detail
            exit 1
        }

        Invoke-Health -BaseUrl $state.url
        $urlPath = [string] $mapped.url
        foreach ($pair in @(
                @{ From = "{slug}"; To = "brian-may" },
                @{ From = "{picId}"; To = "101" },
                @{ From = "{id}"; To = "1003" },
                @{ From = "{pageNumber}"; To = "1" },
                @{ From = "{topicId}"; To = "1002" },
                @{ From = "{statusCode?}"; To = "404" },
                @{ From = "{*path}"; To = "index" }
            )) {
            $urlPath = $urlPath.Replace($pair.From, $pair.To)
        }
        $urlPath = [regex]::Replace($urlPath, "\{[^}]+\}", "1")
        $target = "$($state.url)$urlPath"
        $png = Join-Path $proofDir "screenshot.png"
        & $playwright screenshot --full-page $target $png
        $shotCode = $LASTEXITCODE
        $specResult = "skipped"
        if ($specs.Count -gt 0 -and $shotCode -eq 0) {
            $filter = ($specs | ForEach-Object { [IO.Path]::GetFileNameWithoutExtension($_) }) -join "|"
            & dotnet test (Join-Path $RepoRoot "tests/QueenZone.Web.E2E/QueenZone.Web.E2E.csproj") --filter "FullyQualifiedName~$($filter.Split('|')[0])" --configuration Release
            $specResult = if ($LASTEXITCODE -eq 0) { "PASS" } else { "FAIL" }
        }
        $result = if ($shotCode -eq 0 -and $specResult -ne "FAIL") { "PASS" } else { "FAIL" }
        $detail = "Screenshot of $target exited $shotCode. Spec $specResult."
        Write-QueenZoneProofMarkdown -Path (Join-Path $proofDir "proof.md") -FeatureId $mapped.id -Sha $sha -Platform "web" -Flows $specs -Result $result -Detail $detail
        Write-Output "$result : $detail"
        Write-Output "Proof: $proofDir"
        if ($result -ne "PASS") {
            exit 1
        }
        return
    }
}
