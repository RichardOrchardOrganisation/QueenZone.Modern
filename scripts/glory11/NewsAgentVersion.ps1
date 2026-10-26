#Requires -Version 5.1
# Version-compare and select helpers for the Glory11 News Agent wrapper (#2386).
# Prod tags match scripts/release/generate-notes.mjs (vYYYY.MM.DD.N).

Set-StrictMode -Version Latest

$script:QueenZoneProdTagPattern = '^v(\d{4})\.(\d{2})\.(\d{2})\.(\d+)$'

function Test-QueenZoneProdTag {
    param(
        [Parameter(Mandatory = $true)]
        [AllowEmptyString()]
        [string]$Name
    )

    return [bool][regex]::IsMatch($Name, $script:QueenZoneProdTagPattern)
}

function Compare-QueenZoneProdTag {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Left,
        [Parameter(Mandatory = $true)]
        [string]$Right
    )

    $leftMatch = [regex]::Match($Left, $script:QueenZoneProdTagPattern)
    $rightMatch = [regex]::Match($Right, $script:QueenZoneProdTagPattern)
    if (-not $leftMatch.Success -or -not $rightMatch.Success) {
        throw "Compare-QueenZoneProdTag requires prod tags vYYYY.MM.DD.N, got '$Left' and '$Right'."
    }

    foreach ($index in 1..4) {
        $delta = [int]$leftMatch.Groups[$index].Value - [int]$rightMatch.Groups[$index].Value
        if ($delta -ne 0) {
            return $delta
        }
    }

    return 0
}

function Get-NewsAgentAssetName {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Tag
    )

    return "newsagent-$Tag.zip"
}

function Resolve-NewsAgentDownloadedZip {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Directory,
        [Parameter(Mandatory = $true)]
        [string]$AssetName
    )

    $path = Join-Path $Directory $AssetName
    if (Test-Path -LiteralPath $path) {
        return $path
    }

    throw "Downloaded release is missing $AssetName."
}

function Get-NewsAgentWorkerArguments {
    param(
        [string[]]$Arguments
    )

    $list = @()
    if ($Arguments) {
        $list = @($Arguments)
    }

    if ($list.Count -gt 0 -and $list[0] -eq '--') {
        if ($list.Count -eq 1) {
            return @()
        }

        return @($list[1..($list.Count - 1)])
    }

    return $list
}

function Invoke-NewsAgentNativeCommand {
    param(
        [Parameter(Mandatory = $true)]
        [string]$FilePath,
        [string[]]$ArgumentList = @()
    )

    if ($null -eq $ArgumentList) {
        $ArgumentList = @()
    }

    & $FilePath @ArgumentList | Out-Host
    if ($null -eq $LASTEXITCODE) {
        return 0
    }

    return [int]$LASTEXITCODE
}

function Test-NewsAgentNeedsUpdate {
    param(
        [AllowEmptyString()]
        [string]$InstalledVersion,
        [Parameter(Mandatory = $true)]
        [string]$LatestVersion
    )

    if ([string]::IsNullOrWhiteSpace($InstalledVersion)) {
        return $true
    }

    return $InstalledVersion.Trim() -cne $LatestVersion.Trim()
}

function Get-InstalledNewsAgentVersion {
    param(
        [Parameter(Mandatory = $true)]
        [string]$InstallRoot
    )

    $currentVersion = Join-Path (Join-Path $InstallRoot 'current') 'version.txt'
    $pointerFile = Join-Path $InstallRoot 'current.txt'
    foreach ($path in @($currentVersion, $pointerFile)) {
        if (-not (Test-Path -LiteralPath $path)) {
            continue
        }

        $raw = Get-Content -LiteralPath $path -TotalCount 1
        if ($null -eq $raw) {
            continue
        }

        $value = ([string]$raw).Trim()
        if (-not [string]::IsNullOrWhiteSpace($value)) {
            return $value
        }
    }

    return $null
}

function Test-QueenZoneProdRelease {
    param(
        [Parameter(Mandatory = $true)]
        [object]$Release
    )

    if ($Release.PSObject.Properties['draft'] -and [bool]$Release.draft) {
        return $false
    }

    if ($Release.PSObject.Properties['prerelease'] -and [bool]$Release.prerelease) {
        return $false
    }

    if (-not $Release.PSObject.Properties['tag_name']) {
        return $false
    }

    return Test-QueenZoneProdTag -Name ([string]$Release.tag_name)
}

function Select-LatestQueenZoneProdRelease {
    param(
        [AllowEmptyCollection()]
        [object[]]$Releases
    )

    $latest = $null
    foreach ($release in @($Releases)) {
        if ($null -eq $release) {
            continue
        }

        if (-not (Test-QueenZoneProdRelease -Release $release)) {
            continue
        }

        if ($null -eq $latest -or (Compare-QueenZoneProdTag -Left ([string]$release.tag_name) -Right ([string]$latest.tag_name)) -gt 0) {
            $latest = $release
        }
    }

    return $latest
}

function Resolve-QueenZoneRepository {
    param(
        [string]$Repository
    )

    if (-not [string]::IsNullOrWhiteSpace($Repository)) {
        return $Repository.Trim()
    }

    if (-not [string]::IsNullOrWhiteSpace($env:GITHUB_REPOSITORY)) {
        return $env:GITHUB_REPOSITORY.Trim()
    }

    $gh = Get-Command gh -ErrorAction SilentlyContinue
    if ($gh) {
        $name = & gh repo view --json nameWithOwner --jq .nameWithOwner
        if ($LASTEXITCODE -eq 0 -and -not [string]::IsNullOrWhiteSpace([string]$name)) {
            return ([string]$name).Trim()
        }
    }

    throw 'Repository is required (-Repository or GITHUB_REPOSITORY, or run from a gh-authenticated clone).'
}

function Get-QueenZoneGitHubToken {
    param(
        [string]$Token
    )

    if (-not [string]::IsNullOrWhiteSpace($Token)) {
        return $Token
    }

    if (-not [string]::IsNullOrWhiteSpace($env:GH_TOKEN)) {
        return $env:GH_TOKEN
    }

    if (-not [string]::IsNullOrWhiteSpace($env:GITHUB_TOKEN)) {
        return $env:GITHUB_TOKEN
    }

    return $null
}
