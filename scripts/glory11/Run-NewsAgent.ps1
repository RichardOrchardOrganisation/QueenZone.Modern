#Requires -Version 5.1
<#
.SYNOPSIS
    Fetches the latest prod-tag News Agent zip and runs the worker on Glory11.

.DESCRIPTION
    Looks up the newest GitHub Release whose tag matches vYYYY.MM.DD.N. When
    that version differs from the installed pointer, downloads newsagent-<tag>.zip,
    extracts it to <InstallRoot>\versions\<tag>, and retargets current.
    appsettings.Local.json stays in InstallRoot and is copied into each version.
    A failed download keeps the installed version and logs a warning.

    Do not hard-code a token. Pass -GitHubToken or set GH_TOKEN / GITHUB_TOKEN.

.EXAMPLE
    .\scripts\glory11\Run-NewsAgent.ps1 -InstallRoot C:\QueenZone\NewsAgent -LoadProductionConnection -- discover-news --scheduled
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$InstallRoot,

    [string[]]$WorkerArguments = @(),

    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]]$RemainingArguments = @(),

    [string]$GitHubToken,

    [string]$Repository,

    [switch]$LoadProductionConnection
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'NewsAgentVersion.ps1')

function Write-NewsAgentLog {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Message
    )

    $line = '{0:o} {1}' -f [DateTime]::UtcNow, $Message
    Write-Host $line
    $logFile = Join-Path $InstallRoot 'newsagent.log'
    $logDirectory = Split-Path -Parent $logFile
    if (-not (Test-Path -LiteralPath $logDirectory)) {
        New-Item -ItemType Directory -Path $logDirectory | Out-Null
    }

    Add-Content -LiteralPath $logFile -Value $line
}

function Copy-NewsAgentLocalConfig {
    param(
        [Parameter(Mandatory = $true)]
        [string]$VersionDirectory
    )

    $source = Join-Path $InstallRoot 'appsettings.Local.json'
    if (-not (Test-Path -LiteralPath $source)) {
        Write-NewsAgentLog 'No appsettings.Local.json in the install root; worker will use published settings and environment variables.'
        return
    }

    Copy-Item -LiteralPath $source -Destination (Join-Path $VersionDirectory 'appsettings.Local.json') -Force
}

function Set-NewsAgentCurrentPointer {
    param(
        [Parameter(Mandatory = $true)]
        [string]$VersionDirectory,
        [Parameter(Mandatory = $true)]
        [string]$Tag
    )

    $currentPath = Join-Path $InstallRoot 'current'
    if (Test-Path -LiteralPath $currentPath) {
        $item = Get-Item -LiteralPath $currentPath -Force
        if ($item.Attributes -band [System.IO.FileAttributes]::ReparsePoint) {
            cmd /c rmdir "$currentPath"
        }
        else {
            Remove-Item -LiteralPath $currentPath -Recurse -Force
        }
    }

    New-Item -ItemType Junction -Path $currentPath -Target $VersionDirectory | Out-Null
    Set-Content -LiteralPath (Join-Path $InstallRoot 'current.txt') -Value $Tag
}

function Get-QueenZoneReleases {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Repo,
        [string]$Token
    )

    $resolved = Get-QueenZoneGitHubToken -Token $Token
    $gh = Get-Command gh -ErrorAction SilentlyContinue
    if ($gh) {
        if (-not [string]::IsNullOrWhiteSpace($resolved)) {
            $env:GH_TOKEN = $resolved
        }

        $json = & gh api "repos/$Repo/releases?per_page=100"
        $ghExit = $LASTEXITCODE
        $text = ($json | Out-String).Trim()
        if ($ghExit -eq 0 -and -not [string]::IsNullOrWhiteSpace($text)) {
            return @($text | ConvertFrom-Json)
        }
    }

    if ([string]::IsNullOrWhiteSpace($resolved)) {
        throw 'A GitHub token is required (GitHubToken, GH_TOKEN, or GITHUB_TOKEN).'
    }

    $headers = @{
        Authorization           = "Bearer $resolved"
        Accept                  = 'application/vnd.github+json'
        'X-GitHub-Api-Version'  = '2022-11-28'
        'User-Agent'            = 'QueenZone-NewsAgent-Wrapper'
    }
    return @(Invoke-RestMethod -Uri "https://api.github.com/repos/$Repo/releases?per_page=100" -Headers $headers)
}

function Save-NewsAgentReleaseZip {
    param(
        [Parameter(Mandatory = $true)]
        $Release,
        [Parameter(Mandatory = $true)]
        [string]$ZipPath,
        [Parameter(Mandatory = $true)]
        [string]$Repo,
        [string]$Token
    )

    $tag = [string]$Release.tag_name
    $assetName = Get-NewsAgentAssetName -Tag $tag
    $resolved = Get-QueenZoneGitHubToken -Token $Token
    $gh = Get-Command gh -ErrorAction SilentlyContinue
    if ($gh) {
        if (-not [string]::IsNullOrWhiteSpace($resolved)) {
            $env:GH_TOKEN = $resolved
        }

        $directory = Split-Path -Parent $ZipPath
        & gh release download $tag --repo $Repo --pattern $assetName --dir $directory --clobber
        if ($LASTEXITCODE -ne 0) {
            throw "gh release download failed for $tag."
        }

        if (Test-Path -LiteralPath $ZipPath) {
            return
        }

        $found = Get-ChildItem -LiteralPath $directory -Filter '*.zip' | Select-Object -First 1
        if ($found) {
            Move-Item -LiteralPath $found.FullName -Destination $ZipPath -Force
            return
        }

        throw "Downloaded release $tag but $assetName was not found."
    }

    if ([string]::IsNullOrWhiteSpace($resolved)) {
        throw 'A GitHub token is required to download the News Agent zip.'
    }

    $asset = $null
    if ($Release.PSObject.Properties['assets']) {
        $asset = @($Release.assets) | Where-Object { $_.name -eq $assetName } | Select-Object -First 1
    }

    if ($null -eq $asset) {
        throw "Release $tag has no $assetName asset."
    }

    $headers = @{
        Authorization           = "Bearer $resolved"
        Accept                  = 'application/octet-stream'
        'X-GitHub-Api-Version'  = '2022-11-28'
        'User-Agent'            = 'QueenZone-NewsAgent-Wrapper'
    }
    Invoke-WebRequest -Uri $asset.url -Headers $headers -OutFile $ZipPath
}

function Install-NewsAgentVersion {
    param(
        [Parameter(Mandatory = $true)]
        $Release,
        [Parameter(Mandatory = $true)]
        [string]$Repo,
        [string]$Token
    )

    $tag = [string]$Release.tag_name
    $work = Join-Path ([System.IO.Path]::GetTempPath()) ('newsagent-' + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $work | Out-Null
    try {
        $zipPath = Join-Path $work (Get-NewsAgentAssetName -Tag $tag)
        Save-NewsAgentReleaseZip -Release $Release -ZipPath $zipPath -Repo $Repo -Token $Token
        $versionsRoot = Join-Path $InstallRoot 'versions'
        if (-not (Test-Path -LiteralPath $versionsRoot)) {
            New-Item -ItemType Directory -Path $versionsRoot | Out-Null
        }

        $destination = Join-Path $versionsRoot $tag
        if (Test-Path -LiteralPath $destination) {
            Remove-Item -LiteralPath $destination -Recurse -Force
        }

        New-Item -ItemType Directory -Path $destination | Out-Null
        Expand-Archive -LiteralPath $zipPath -DestinationPath $destination -Force
        Set-Content -LiteralPath (Join-Path $destination 'version.txt') -Value $tag
        Copy-NewsAgentLocalConfig -VersionDirectory $destination
        Set-NewsAgentCurrentPointer -VersionDirectory $destination -Tag $tag
        Write-NewsAgentLog "Installed News Agent $tag."
    }
    finally {
        Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue
    }
}

function Invoke-NewsAgentWorker {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Version,
        [string[]]$Arguments
    )

    $currentPath = Join-Path $InstallRoot 'current'
    if (-not (Test-Path -LiteralPath $currentPath)) {
        throw "No current News Agent install at $currentPath."
    }

    Copy-NewsAgentLocalConfig -VersionDirectory $currentPath
    $exe = Join-Path $currentPath 'QueenZone.NewsAgent.Worker.exe'
    $dll = Join-Path $currentPath 'QueenZone.NewsAgent.Worker.dll'
    Write-NewsAgentLog "Starting News Agent $Version $($Arguments -join ' ')"
    Push-Location $currentPath
    try {
        if (Test-Path -LiteralPath $exe) {
            & $exe @Arguments
        }
        elseif (Test-Path -LiteralPath $dll) {
            & dotnet $dll @Arguments
        }
        else {
            throw 'QueenZone.NewsAgent.Worker.exe (or .dll) was not found in the current install.'
        }

        if ($null -eq $LASTEXITCODE) {
            return 0
        }

        return [int]$LASTEXITCODE
    }
    finally {
        Pop-Location
    }
}

if (-not (Test-Path -LiteralPath $InstallRoot)) {
    New-Item -ItemType Directory -Path $InstallRoot | Out-Null
}
$InstallRoot = (Resolve-Path -LiteralPath $InstallRoot).Path

$runArguments = @()
if ($WorkerArguments -and $WorkerArguments.Count -gt 0) {
    $runArguments += $WorkerArguments
}
if ($RemainingArguments -and $RemainingArguments.Count -gt 0) {
    $runArguments += $RemainingArguments
}

if ($LoadProductionConnection) {
    $importScript = Join-Path (Split-Path -Parent $PSScriptRoot) 'Import-NewsAgentProductionConnection.ps1'
    & $importScript
}

$Repository = Resolve-QueenZoneRepository -Repository $Repository

$installed = Get-InstalledNewsAgentVersion -InstallRoot $InstallRoot
try {
    $releases = Get-QueenZoneReleases -Repo $Repository -Token $GitHubToken
    $latestRelease = Select-LatestQueenZoneProdRelease -Releases $releases
    if ($null -eq $latestRelease) {
        throw 'No GitHub Release with a prod tag vYYYY.MM.DD.N was found.'
    }

    $latest = [string]$latestRelease.tag_name
    if (Test-NewsAgentNeedsUpdate -InstalledVersion $installed -LatestVersion $latest) {
        Install-NewsAgentVersion -Release $latestRelease -Repo $Repository -Token $GitHubToken
        $installed = $latest
    }
    else {
        Write-NewsAgentLog "News Agent $installed is already current."
        Copy-NewsAgentLocalConfig -VersionDirectory (Join-Path $InstallRoot 'current')
    }
}
catch {
    Write-Warning "News Agent download/update failed: $($_.Exception.Message). Using the installed version."
    Write-NewsAgentLog "WARNING: update failed ($($_.Exception.Message)); using installed $installed."
}

if ([string]::IsNullOrWhiteSpace($installed)) {
    Write-NewsAgentLog 'No installed News Agent version is available.'
    exit 1
}

$exitCode = Invoke-NewsAgentWorker -Version $installed -Arguments $runArguments
Write-NewsAgentLog "News Agent $installed exited $exitCode"
exit $exitCode
