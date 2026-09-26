# Shared feature-map reader for verify control scripts (#1800 / #1799).
function Get-QueenZoneRepoRoot {
    param([string] $Start)
    $cursor = $Start
    for ($i = 0; $i -lt 8; $i++) {
        if (Test-Path (Join-Path $cursor "QueenZone.sln")) {
            return $cursor
        }
        $parent = Split-Path -Parent $cursor
        if ([string]::IsNullOrWhiteSpace($parent) -or $parent -eq $cursor) {
            break
        }
        $cursor = $parent
    }
    throw "Could not find QueenZone.sln above $Start."
}

function Get-QueenZoneFeatureMap {
    param([string] $RepoRoot)
    $entries = @()
    foreach ($surface in @("mobile", "web")) {
        $dir = Join-Path $RepoRoot "docs/feature-map/$surface"
        if (-not (Test-Path $dir)) {
            continue
        }
        Get-ChildItem -Path $dir -Filter *.json | ForEach-Object {
            $doc = Get-Content -Raw -Path $_.FullName | ConvertFrom-Json
            foreach ($entry in @($doc.entries)) {
                $entries += $entry
            }
        }
    }
    return $entries
}

function Resolve-QueenZoneFeature {
    param($Entries, [string] $IdOrAlias)
    $needle = [string] $IdOrAlias
    foreach ($entry in $Entries) {
        if ([string] $entry.id -eq $needle) {
            return $entry
        }
    }
    foreach ($entry in $Entries) {
        foreach ($alias in @($entry.aliases)) {
            if ([string] $alias -eq $needle) {
                return $entry
            }
        }
        $drive = $entry.drive
        if ($drive -is [string] -and $drive -eq $needle) {
            return $entry
        }
        if ($drive -and $drive.PSObject -and $drive.PSObject.Properties.Name -contains $needle) {
            return $entry
        }
    }
    return $null
}

function Get-QueenZoneDriveFlow {
    param($Entry, [string] $FlowName)
    if (-not $Entry) {
        return $null
    }
    $drive = $Entry.drive
    if ($drive -and $drive.PSObject -and $drive.PSObject.Properties.Name -contains $FlowName) {
        return [string] $drive.$FlowName
    }
    $flows = @($Entry.flows)
    if ($drive -is [string] -and $drive -eq $FlowName -and $flows.Count -gt 0) {
        return [string] $flows[0]
    }
    foreach ($alias in @($Entry.aliases)) {
        if ([string] $alias -eq $FlowName -and $flows.Count -gt 0) {
            return [string] $flows[0]
        }
    }
    if ($flows.Count -gt 0) {
        return [string] $flows[0]
    }
    return $null
}

function Get-QueenZoneProofDir {
    param(
        [string] $RepoRoot,
        [string] $FeatureId,
        [string] $Platform
    )
    $stamp = (Get-Date).ToUniversalTime().ToString("yyyyMMdd-HHmmss")
    $dir = Join-Path $RepoRoot "artifacts/proof/$FeatureId/$Platform/$stamp"
    New-Item -ItemType Directory -Force -Path $dir | Out-Null
    return $dir
}

function Write-QueenZoneProofMarkdown {
    param(
        [string] $Path,
        [string] $FeatureId,
        [string] $Sha,
        [string] $Platform,
        [string[]] $Flows,
        [string] $Result,
        [string] $Detail
    )
    $flowList = if ($Flows -and $Flows.Count -gt 0) { ($Flows | ForEach-Object { "- $_" }) -join "`n" } else { "- (none)" }
    @(
        "# Proof",
        "",
        "- Feature: $FeatureId",
        "- SHA: $Sha",
        "- Platform: $Platform",
        "- Result: $Result",
        "",
        "## Flows / specs",
        $flowList,
        "",
        "## Detail",
        $Detail,
        ""
    ) -join "`n" | Set-Content -Path $Path -Encoding utf8
}

function Get-QueenZoneHeadSha {
    param([string] $RepoRoot)
    try {
        $sha = & git -C $RepoRoot rev-parse HEAD
        if ($LASTEXITCODE -eq 0 -and -not [string]::IsNullOrWhiteSpace($sha)) {
            return $sha.Trim()
        }
    }
    catch {
    }
    return "unknown"
}
