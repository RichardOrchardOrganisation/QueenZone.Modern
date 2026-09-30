#Requires -Version 7.0
<#
.SYNOPSIS
  Launch, doctor, drive, and tear down an isolated QueenZone mobile contract host.

.DESCRIPTION
  Starts QueenZone.Web with ASPNETCORE_ENVIRONMENT=Testing and
  QUEENZONE_MOBILE_CONTRACT_HOST=1 on a loopback port (default 5098).
  Records pid/url in .cursor/skills/verify-queenzone-mobile/.run/state.json.
  Cleanup kills only that process tree. Maestro drive is optional and fails
  closed when maestro or a device is missing.
#>
[CmdletBinding()]
param(
    [Parameter(Position = 0, Mandatory = $true)]
    [ValidateSet("launch", "doctor", "url", "drive", "cleanup", "capture-proof")]
    [string] $Command,

    [int] $Port = 5098,

    [string] $Flow = "news",

    [string] $Feature,

    [ValidateSet("android", "ios")]
    [string] $Platform = "android"
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
$FixturePath = Join-Path $RunDir "host.json"
$OutLogPath = Join-Path $RunDir "host.out.log"
$ErrLogPath = Join-Path $RunDir "host.err.log"
$ProjectPath = Join-Path $RepoRoot "src\QueenZone.Web\QueenZone.Web.csproj"
$NewsDetailPath = "/api/v1/content/news/1003"
$NewsTitle = "QueenZone modernisation begins"

function Resolve-MappedFlowEntry {
    param([string] $IdOrAlias)
    $entries = Get-QueenZoneFeatureMap -RepoRoot $RepoRoot
    $entry = Resolve-QueenZoneFeature -Entries $entries -IdOrAlias $IdOrAlias
    if (-not $entry) {
        throw "Unknown feature or drive alias '$IdOrAlias'. Use a docs/feature-map id (for example mobile.photos.viewer) or a drive alias such as news, photos, search."
    }
    return $entry
}

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
        $null = Get-Process -Id $ProcessId -ErrorAction Stop
        return $true
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

function Test-PortOwnedByHost {
    param([int] $ProcessId, [int] $ListenPort)
    $owner = Get-PortOwnerPid -ListenPort $ListenPort
    if ($null -eq $owner) {
        return $false
    }
    if ($owner -eq $ProcessId) {
        return $true
    }
    $children = Get-CimInstance Win32_Process -Filter "ParentProcessId=$ProcessId" -ErrorAction SilentlyContinue
    $tree = @($ProcessId) + @($children | ForEach-Object { [int] $_.ProcessId })
    return $tree -contains $owner
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

function Invoke-NewsDetail {
    param([string] $BaseUrl)
    $response = Invoke-WebRequest -Uri "$BaseUrl$NewsDetailPath" -UseBasicParsing -TimeoutSec 10
    if ($response.StatusCode -ne 200) {
        throw "News detail returned HTTP $($response.StatusCode)."
    }
    $json = $response.Content | ConvertFrom-Json
    if ($json.title -ne $NewsTitle) {
        throw "News 1003 title was '$($json.title)', expected '$NewsTitle'."
    }
}

function Read-Fixture {
    if (-not (Test-Path $FixturePath)) {
        throw "Contract fixture is missing at $FixturePath."
    }
    $fixture = Get-Content -Raw -Path $FixturePath | ConvertFrom-Json
    if ($fixture.environment -ne "Testing") {
        throw "Fixture environment is '$($fixture.environment)', expected Testing."
    }
    $token = [string] $fixture.member.accessToken
    if ([string]::IsNullOrWhiteSpace($token)) {
        throw "Fixture is missing member.accessToken."
    }
    return $fixture
}

function Get-LogTail {
    $tail = @(
        if (Test-Path $OutLogPath) { Get-Content -Tail 20 $OutLogPath }
        if (Test-Path $ErrLogPath) { Get-Content -Tail 20 $ErrLogPath }
    ) -join "`n"
    if ([string]::IsNullOrWhiteSpace($tail)) {
        return "(no log)"
    }
    return $tail
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
        if (Test-Path $FixturePath) {
            Remove-Item -Force $FixturePath
        }
        Write-Output "Cleaned up mobile contract host. Artifacts and the emulator were left in place."
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
        if (-not (Test-PortOwnedByHost -ProcessId $processId -ListenPort ([int] $state.port))) {
            throw "Doctor failed: port $($state.port) is not owned by the recorded host $processId."
        }
        Invoke-Health -BaseUrl $state.url
        $fixture = Read-Fixture
        $tokenLength = ([string] $fixture.member.accessToken).Length
        Invoke-NewsDetail -BaseUrl $state.url
        Write-Output "Doctor ok: $($state.url) pid=$processId env=Testing news=$NewsDetailPath tokenLength=$tokenLength"
        return
    }

    "drive" {
        $state = Read-State
        if (-not $state) {
            throw "Drive failed: launch the contract host first."
        }
        Invoke-Health -BaseUrl $state.url
        $fixture = Read-Fixture
        $mapped = Resolve-MappedFlowEntry -IdOrAlias $Flow
        $flowRel = Get-QueenZoneDriveFlow -Entry $mapped -FlowName $Flow
        if ([string]::IsNullOrWhiteSpace($flowRel)) {
            throw "Feature $($mapped.id) has no Maestro flow for '$Flow'."
        }
        $flowPath = Join-Path $RepoRoot $flowRel
        if (-not (Test-Path $flowPath)) {
            throw "Flow file missing: $flowRel"
        }
        $maestro = Get-Command maestro -ErrorAction SilentlyContinue
        if (-not $maestro) {
            throw "Drive failed: maestro is not on PATH. Install from https://docs.maestro.dev then rerun drive. Host is still up at $($state.url)."
        }
        $adb = Get-Command adb -ErrorAction SilentlyContinue
        if ($adb) {
            $devices = & adb devices
            if ($devices -notmatch "device$") {
                throw "Drive failed: no Android device in 'adb devices'. Boot Pixel_8_API_36 (or another API 36 emulator) and rerun. Host is still up."
            }
        }
        $results = Join-Path $RepoRoot "src\QueenZone.Mobile\maestro-results"
        New-Item -ItemType Directory -Force -Path $results | Out-Null
        if ($Flow -in @("auth", "attach", "unread")) {
            $token = [string] $fixture.member.accessToken
            $env:SMOKE_AUTH_URL = "queenzone://smoke-auth?accessToken=$([uri]::EscapeDataString($token))"
            Write-Output "SMOKE_AUTH_URL is set (length $($env:SMOKE_AUTH_URL.Length); token not printed)."
        }
        if ($Flow -eq "attach") {
            $env:ATTACH_TOPIC_ID = [string] $fixture.attachTopicId
            Write-Output "ATTACH_TOPIC_ID is set."
        }
        Write-Output "Running Maestro $flowRel"
        & maestro test $flowPath --format junit --output (Join-Path $results "junit.xml") --debug-output (Join-Path $results "debug") --flatten-debug-output
        if ($LASTEXITCODE -ne 0) {
            throw "Maestro exited $LASTEXITCODE. See $results"
        }
        $artifactDir = Join-Path $SkillRoot "artifacts\$Flow"
        New-Item -ItemType Directory -Force -Path $artifactDir | Out-Null
        if (Test-Path (Join-Path $results "junit.xml")) {
            Copy-Item (Join-Path $results "junit.xml") (Join-Path $artifactDir "junit.xml") -Force
        }
        Write-Output "Drive ok: $Flow ($($mapped.id)). Copied JUnit to $artifactDir"
        return
    }

    "capture-proof" {
        $featureId = if (-not [string]::IsNullOrWhiteSpace($Feature)) { $Feature } else { $Flow }
        $mapped = Resolve-MappedFlowEntry -IdOrAlias $featureId
        $proofDir = Get-QueenZoneProofDir -RepoRoot $RepoRoot -FeatureId $mapped.id -Platform $Platform
        $sha = Get-QueenZoneHeadSha -RepoRoot $RepoRoot
        $flows = @($mapped.flows | ForEach-Object { [string] $_ } | Where-Object { $_ })
        $namedCheck = "CI workflow mobile-device-smoke.yml suite=proof feature=$($mapped.id) ($Platform)"
        $reason = $null

        if ($Platform -eq "ios") {
            if ($IsLinux -or -not (Get-Command xcrun -ErrorAction SilentlyContinue)) {
                $reason = "iOS Simulator is impossible on this host"
            }
            else {
                $booted = & xcrun simctl list devices booted 2>$null
                if ($booted -notmatch "Booted") {
                    $reason = "no booted iOS Simulator"
                }
            }
        }
        else {
            $adb = Get-Command adb -ErrorAction SilentlyContinue
            if (-not $adb) {
                $reason = "adb is not on PATH and no Android emulator is reachable"
            }
            else {
                $devices = & adb devices
                if ($devices -notmatch "device$") {
                    $reason = "no Android emulator or device in adb devices"
                }
            }
        }

        if ($reason) {
            $detail = "NOT RUN: $reason, needs $namedCheck. Expo web is not accepted as mobile proof."
            Write-QueenZoneProofMarkdown -Path (Join-Path $proofDir "proof.md") -FeatureId $mapped.id -Sha $sha -Platform $Platform -Flows $flows -Result "NOT RUN" -Detail $detail
            Write-Output $detail
            Write-Output "Wrote $proofDir/proof.md"
            exit 1
        }

        $state = Read-State
        if (-not $state) {
            $detail = "NOT RUN: contract host is not running, needs control-queenzone-mobile.ps1 launch then $namedCheck"
            Write-QueenZoneProofMarkdown -Path (Join-Path $proofDir "proof.md") -FeatureId $mapped.id -Sha $sha -Platform $Platform -Flows $flows -Result "NOT RUN" -Detail $detail
            Write-Output $detail
            exit 1
        }
        Invoke-Health -BaseUrl $state.url
        $fixture = Read-Fixture
        $maestro = Get-Command maestro -ErrorAction SilentlyContinue
        if (-not $maestro) {
            $detail = "NOT RUN: maestro is not on PATH, needs $namedCheck"
            Write-QueenZoneProofMarkdown -Path (Join-Path $proofDir "proof.md") -FeatureId $mapped.id -Sha $sha -Platform $Platform -Flows $flows -Result "NOT RUN" -Detail $detail
            Write-Output $detail
            exit 1
        }
        if ($flows.Count -eq 0) {
            $detail = "NOT RUN: feature $($mapped.id) has no Maestro flows, needs a mapped flow before proof"
            Write-QueenZoneProofMarkdown -Path (Join-Path $proofDir "proof.md") -FeatureId $mapped.id -Sha $sha -Platform $Platform -Flows $flows -Result "NOT RUN" -Detail $detail
            Write-Output $detail
            exit 1
        }

        if ($flows -match "09-authenticated|10-forum-attach|12-masthead-unread") {
            $token = [string] $fixture.member.accessToken
            $env:SMOKE_AUTH_URL = "queenzone://smoke-auth?accessToken=$([uri]::EscapeDataString($token))"
        }
        if ($flows -match "10-forum-attach") {
            $env:ATTACH_TOPIC_ID = [string] $fixture.attachTopicId
        }

        $results = Join-Path $proofDir "maestro"
        New-Item -ItemType Directory -Force -Path $results | Out-Null
        $flowPaths = $flows | ForEach-Object { Join-Path $RepoRoot $_ }
        & maestro test @flowPaths --format junit --output (Join-Path $proofDir "junit.xml") --debug-output (Join-Path $results "debug") --flatten-debug-output
        $maestroCode = $LASTEXITCODE
        if ($Platform -eq "ios") {
            & xcrun simctl io booted screenshot (Join-Path $proofDir "screenshot.png")
        }
        else {
            $png = Join-Path $proofDir "screenshot.png"
            & adb exec-out screencap -p | Set-Content -Path $png -AsByteStream
        }
        $result = if ($maestroCode -eq 0) { "PASS" } else { "FAIL" }
        $detail = "Maestro exited $maestroCode for $($mapped.id) on $Platform."
        Write-QueenZoneProofMarkdown -Path (Join-Path $proofDir "proof.md") -FeatureId $mapped.id -Sha $sha -Platform $Platform -Flows $flows -Result $result -Detail $detail
        Write-Output "$result : $detail"
        Write-Output "Proof: $proofDir"
        if ($maestroCode -ne 0) {
            exit $maestroCode
        }
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
        foreach ($log in @($OutLogPath, $ErrLogPath, $FixturePath)) {
            if (Test-Path $log) {
                Remove-Item -Force $log
            }
        }

        $env:ASPNETCORE_ENVIRONMENT = "Testing"
        $env:QUEENZONE_MOBILE_CONTRACT_HOST = "1"
        $env:QUEENZONE_MOBILE_CONTRACT_FIXTURE = $FixturePath
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
                throw "Host process $($proc.Id) exited before becoming ready.`n$(Get-LogTail)"
            }
            try {
                if (Test-Path $FixturePath) {
                    Invoke-Health -BaseUrl $baseUrl
                    $ready = $true
                    break
                }
            }
            catch {
                Start-Sleep -Milliseconds 500
                continue
            }
            Start-Sleep -Milliseconds 500
        }
        if (-not $ready) {
            Stop-RecordedHost -State ([pscustomobject]@{ pid = $proc.Id })
            throw "Timed out waiting for $baseUrl/health and $FixturePath.`n$(Get-LogTail)"
        }

        $null = Read-Fixture
        Invoke-NewsDetail -BaseUrl $baseUrl

        Write-State ([pscustomobject]@{
                pid         = $proc.Id
                url         = $baseUrl
                port        = $Port
                environment = "Testing"
                fixture     = $FixturePath
                startedAt   = (Get-Date).ToUniversalTime().ToString("o")
            })

        Write-Output "Launched $baseUrl (pid $($proc.Id), contract fixture written)"
        return
    }
}
