<#
.SYNOPSIS
  Fails when QueenZone.Web.Tests builds a web host outside the fixture infrastructure.

.DESCRIPTION
  Greps Web.Tests sources for per-test host construction:
    WithWebHostBuilder(
    new QueenZoneWebApplicationFactory(
    QueenZoneWebApplicationFactory.WithServices(
    new WebApplicationFactory<Program>(

  Fixture infrastructure files are excluded. Remaining classes must appear in
  config/web-host-allowlist.txt as "ClassName: reason". That list can only shrink
  relative to origin/main (adding an entry needs architect or reviewer sign-off).

.PARAMETER TestsRoot
  Path to QueenZone.Web.Tests sources.

.PARAMETER AllowlistPath
  Path to the shrink-only allowlist.

.PARAMETER BaseRef
  Git ref whose allowlist is the maximum permitted set. Defaults to origin/main.
  The ref must be available locally; a missing ref or missing baseline text
  fails closed.

.PARAMETER SelfTest
  Run fixture assertions, then exit.
#>
[CmdletBinding()]
param(
    [string] $TestsRoot = "",
    [string] $AllowlistPath = "",
    [string] $BaseRef = "origin/main",
    [switch] $SelfTest
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$script:ForbiddenPatterns = @(
    'WithWebHostBuilder\s*\(',
    'new\s+QueenZoneWebApplicationFactory\s*\(',
    'QueenZoneWebApplicationFactory\.WithServices\s*\(',
    'new\s+WebApplicationFactory\s*<\s*Program\s*>\s*\('
)

$script:InfrastructureFileNames = [System.Collections.Generic.HashSet[string]]::new(
    [string[]]@(
        "QueenZoneWebApplicationFactory.cs",
        "EnvironmentWebApplicationFactories.cs",
        "WebHostVariants.cs",
        "WebHostVariants.Admin.cs",
        "WebHostVariants.Slice3d.cs",
        "WebHostVariantCache.cs",
        "InspectableBlobWebApplicationFactory.cs",
        "AdminEfWebTestHarness.cs",
        "ProductionHostFixture.cs"
    ),
    [StringComparer]::OrdinalIgnoreCase
)

function Get-RepoRoot {
    if ($PSScriptRoot) {
        return (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
    }

    return (Get-Location).Path
}

function Resolve-DefaultPaths {
    $root = Get-RepoRoot
    if ([string]::IsNullOrWhiteSpace($TestsRoot)) {
        $script:TestsRoot = Join-Path $root "tests/QueenZone.Web.Tests"
    }
    else {
        $script:TestsRoot = $TestsRoot
    }

    if ([string]::IsNullOrWhiteSpace($AllowlistPath)) {
        $script:AllowlistPath = Join-Path $root "config/web-host-allowlist.txt"
    }
    else {
        $script:AllowlistPath = $AllowlistPath
    }
}

function Read-Allowlist {
    param([string] $Path)

    $entries = [ordered]@{}
    if (-not (Test-Path -LiteralPath $Path)) {
        return $entries
    }

    $lineNumber = 0
    foreach ($line in Get-Content -LiteralPath $Path) {
        $lineNumber += 1
        $trimmed = $line.Trim()
        if ($trimmed.Length -eq 0 -or $trimmed.StartsWith("#")) {
            continue
        }

        $parts = $trimmed.Split(":", 2)
        if ($parts.Count -lt 2 -or [string]::IsNullOrWhiteSpace($parts[0]) -or [string]::IsNullOrWhiteSpace($parts[1])) {
            throw "Allowlist $Path line ${lineNumber}: expected 'ClassName: reason'."
        }

        $className = $parts[0].Trim()
        $reason = $parts[1].Trim()
        if ($entries.Contains($className)) {
            throw "Allowlist $Path lists '$className' more than once."
        }

        $entries[$className] = $reason
    }

    return $entries
}

function Get-OwningClassName {
    param(
        [string] $Text,
        [int] $MatchIndex
    )

    $prefix = $Text.Substring(0, $MatchIndex)
    $matches = [regex]::Matches($prefix, '(?m)^\s*(?:public|internal|private|protected)?\s*(?:sealed\s+|abstract\s+|partial\s+)*class\s+(\w+)')
    if ($matches.Count -eq 0) {
        return $null
    }

    return $matches[$matches.Count - 1].Groups[1].Value
}

function Get-HostConstructionHits {
    param([string] $Root)

    $hits = @()
    $files = Get-ChildItem -LiteralPath $Root -Filter *.cs -File
    foreach ($file in $files) {
        if ($script:InfrastructureFileNames.Contains($file.Name)) {
            continue
        }

        $text = [System.IO.File]::ReadAllText($file.FullName)
        foreach ($pattern in $script:ForbiddenPatterns) {
            foreach ($match in [regex]::Matches($text, $pattern)) {
                $className = Get-OwningClassName -Text $text -MatchIndex $match.Index
                if ([string]::IsNullOrWhiteSpace($className)) {
                    throw "$($file.Name) matches '$pattern' but no owning class was found."
                }

                $line = ($text.Substring(0, $match.Index) -split "`r?`n").Count
                $hits += [pscustomobject]@{
                    File = $file.Name
                    Class = $className
                    Line = $line
                    Pattern = $match.Value
                }
            }
        }
    }

    return $hits
}

function Assert-BaseRefAvailable {
    param([string] $Ref)

    if ([string]::IsNullOrWhiteSpace($Ref)) {
        throw "web-host-allowlist.txt baseline is missing: no -BaseRef was supplied. Fetch origin/main so the shrink-only ratchet can compare (git fetch origin main --depth=1)."
    }

    git rev-parse --verify --quiet "${Ref}^{commit}" 2>$null | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw ("web-host-allowlist.txt baseline is missing: git ref '{0}' is not available locally. Fetch origin/main so the shrink-only ratchet can compare (git fetch origin main --depth=1)." -f $Ref)
    }
}

function Get-BaselineAllowlistText {
    param([string] $Ref)

    git rev-parse --verify --quiet "${Ref}:config/web-host-allowlist.txt" 2>$null | Out-Null
    if ($LASTEXITCODE -ne 0) {
        return $null
    }

    return (git show "${Ref}:config/web-host-allowlist.txt" | Out-String)
}

function Test-AllowlistDidNotGrow {
    param(
        [System.Collections.IDictionary] $Current,
        [string] $BaselineText,
        [string] $Ref = "origin/main"
    )

    if ([string]::IsNullOrWhiteSpace($BaselineText)) {
        throw ("web-host-allowlist.txt baseline is missing for '{0}'. Fetch origin/main so the shrink-only ratchet can compare (git fetch origin main --depth=1)." -f $Ref)
    }

    $baselinePath = Join-Path ([System.IO.Path]::GetTempPath()) ("web-host-allowlist-baseline-{0}.txt" -f [guid]::NewGuid().ToString("N"))
    try {
        Set-Content -LiteralPath $baselinePath -Value $BaselineText.TrimEnd() -Encoding utf8
        $baseline = Read-Allowlist -Path $baselinePath
        $added = @($Current.Keys | Where-Object { -not $baseline.Contains($_) } | Sort-Object)
        if ($added.Count -gt 0) {
            throw ("web-host-allowlist.txt can only shrink. New entries need architect or reviewer sign-off: {0}" -f ($added -join ", "))
        }

        Write-Output ("web-host-allowlist.txt shrink-only check compared against {0} ({1} baseline classes, {2} current classes)." -f $Ref, $baseline.Count, $Current.Count)
    }
    finally {
        Remove-Item -LiteralPath $baselinePath -ErrorAction SilentlyContinue
    }
}

function Assert-SelfTestEqual {
    param($Actual, $Expected, [string] $Name)
    if ($Actual -ne $Expected) {
        throw "Self-test failed ($Name): expected '$Expected', got '$Actual'."
    }
}

function Invoke-WebHostConstructionSelfTest {
    $tempRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("web-host-guard-{0}" -f [guid]::NewGuid().ToString("N"))
    New-Item -ItemType Directory -Path $tempRoot | Out-Null
    try {
        Set-Content -LiteralPath (Join-Path $tempRoot "QueenZoneWebApplicationFactory.cs") -Value @"
public class QueenZoneWebApplicationFactory {
    public static QueenZoneWebApplicationFactory WithServices() => new QueenZoneWebApplicationFactory();
}
"@
        Set-Content -LiteralPath (Join-Path $tempRoot "AllowedHostTests.cs") -Value @"
public sealed class AllowedHostTests {
    public void One() {
        using var factory = new QueenZoneWebApplicationFactory();
    }
}
"@
        Set-Content -LiteralPath (Join-Path $tempRoot "BannedHostTests.cs") -Value @"
public sealed class BannedHostTests {
    public void One() {
        factory.WithWebHostBuilder(builder => { });
    }
}
"@

        $hits = @(Get-HostConstructionHits -Root $tempRoot)
        $classes = @($hits.Class | Sort-Object -Unique)
        Assert-SelfTestEqual ($classes -contains "AllowedHostTests") $true "allowed class detected"
        Assert-SelfTestEqual ($classes -contains "BannedHostTests") $true "banned class detected"
        Assert-SelfTestEqual ($classes -contains "QueenZoneWebApplicationFactory") $false "infrastructure excluded"

        $allowlistPath = Join-Path $tempRoot "allowlist.txt"
        Set-Content -LiteralPath $allowlistPath -Value @"
AllowedHostTests: documented private host
BannedHostTests: leftover conversion
"@
        $allowlist = Read-Allowlist -Path $allowlistPath
        $unlisted = @($hits.Class | Where-Object { -not $allowlist.Contains($_) } | Sort-Object -Unique)
        Assert-SelfTestEqual $unlisted.Count 0 "allowlisted hits pass"

        Set-Content -LiteralPath $allowlistPath -Value "AllowedHostTests: documented private host`n"
        $allowlist = Read-Allowlist -Path $allowlistPath
        $unlisted = @($hits.Class | Where-Object { -not $allowlist.Contains($_) } | Sort-Object -Unique)
        Assert-SelfTestEqual ($unlisted -join ",") "BannedHostTests" "unlisted class fails"

        $baseline = "AllowedHostTests: documented private host`nBannedHostTests: leftover conversion`n"
        Test-AllowlistDidNotGrow -Current $allowlist -BaselineText $baseline

        $grown = Read-Allowlist -Path $allowlistPath
        $grown["NewHostTests"] = "should not appear"
        $grew = $false
        try {
            Test-AllowlistDidNotGrow -Current $grown -BaselineText $baseline
        }
        catch {
            $grew = $_.Exception.Message -match "can only shrink"
        }
        Assert-SelfTestEqual $grew $true "allowlist growth is rejected"

        $missingBaseline = $false
        try {
            Test-AllowlistDidNotGrow -Current $allowlist -BaselineText $null
        }
        catch {
            $missingBaseline = $_.Exception.Message -match "baseline is missing"
        }
        Assert-SelfTestEqual $missingBaseline $true "missing baseline is rejected"

        $missingRef = $false
        try {
            Assert-BaseRefAvailable -Ref "origin/definitely-not-a-web-host-guard-ref"
        }
        catch {
            $missingRef = $_.Exception.Message -match "baseline is missing"
        }
        Assert-SelfTestEqual $missingRef $true "missing base ref is rejected"

        $className = Get-OwningClassName -Text "public sealed class OuterTests { void A() { WithWebHostBuilder(); } }" -MatchIndex 40
        Assert-SelfTestEqual $className "OuterTests" "owning class from match"
    }
    finally {
        Remove-Item -LiteralPath $tempRoot -Recurse -Force -ErrorAction SilentlyContinue
    }

    Write-Output "Test-WebHostConstruction.ps1 self-test passed."
}

if ($SelfTest) {
    Invoke-WebHostConstructionSelfTest
    return
}

Resolve-DefaultPaths

if (-not (Test-Path -LiteralPath $script:TestsRoot)) {
    throw "Tests root not found: $($script:TestsRoot)"
}

if (-not (Test-Path -LiteralPath $script:AllowlistPath)) {
    throw "Allowlist not found: $($script:AllowlistPath)"
}

$allowlist = Read-Allowlist -Path $script:AllowlistPath
Assert-BaseRefAvailable -Ref $BaseRef
$baseSha = (git rev-parse --verify --quiet $BaseRef)
Write-Output ("web-host-allowlist.txt baseline ref {0} resolved to {1}." -f $BaseRef, $baseSha)
$baselineText = Get-BaselineAllowlistText -Ref $BaseRef
if ([string]::IsNullOrWhiteSpace($baselineText)) {
    Write-Output ("web-host-allowlist.txt is not on {0} yet; shrink-only ratchet applies after this file merges to main." -f $BaseRef)
}
else {
    Test-AllowlistDidNotGrow -Current $allowlist -BaselineText $baselineText -Ref $BaseRef
}

$hits = @(Get-HostConstructionHits -Root $script:TestsRoot)
$violations = @($hits | Where-Object { -not $allowlist.Contains($_.Class) })
if ($violations.Count -gt 0) {
    $details = $violations | ForEach-Object { "$($_.File):$($_.Line) $($_.Class) $($_.Pattern)" }
    throw ("Web.Tests host construction is not allowlisted:`n{0}" -f ($details -join "`n"))
}

$stale = @($allowlist.Keys | Where-Object { $name = $_; -not ($hits.Class -contains $name) } | Sort-Object)
if ($stale.Count -gt 0) {
    throw ("web-host-allowlist.txt lists classes that no longer construct a host; remove them: {0}" -f ($stale -join ", "))
}

Write-Output ("Web.Tests host-construction guard passed ({0} allowlisted classes)." -f $allowlist.Count)
