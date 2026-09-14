<#
.SYNOPSIS
    Keeps every copy of the design token CSS files in sync with the canonical
    source at design/tokens/*.css.

.DESCRIPTION
    design/tokens/*.css is the canonical, production-ready design token set
    (see design/README.md). It is duplicated into:
      - src/QueenZone.Web/wwwroot/design-system/tokens/ (the shipped copy,
        minified at build time)
      - each design/design_handoff_*/tokens/ folder (included in handoff
        packages so their reference CSS reads standalone)

    Nothing enforces these copies stay identical to the canonical source, so
    this script is the single place that performs the copy. Run it after
    editing any file under design/tokens/. Use -Check in CI to fail when a
    copy has drifted instead of silently shipping stale tokens.

.PARAMETER Check
    Compare copies against the canonical source without writing anything.
    Exits non-zero and lists every out-of-date file if any copy differs.

.EXAMPLE
    pwsh ./scripts/Sync-DesignTokens.ps1
    pwsh ./scripts/Sync-DesignTokens.ps1 -Check
#>
[CmdletBinding()]
param(
    [switch]$Check
)

$ErrorActionPreference = 'Stop'

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..')
$sourceDir = Join-Path $repoRoot 'design/tokens'

if (-not (Test-Path $sourceDir)) {
    throw "Canonical token source not found: $sourceDir"
}

$targetDirs = @(
    (Join-Path $repoRoot 'src/QueenZone.Web/wwwroot/design-system/tokens')
)
$targetDirs += Get-ChildItem -Path (Join-Path $repoRoot 'design') -Directory -Filter 'design_handoff_*' |
    ForEach-Object { Join-Path $_.FullName 'tokens' } |
    Where-Object { Test-Path $_ }

$mismatches = @()
$updated = @()

foreach ($targetDir in $targetDirs) {
    $existingFiles = Get-ChildItem -Path $targetDir -Filter '*.css' -File
    foreach ($targetFile in $existingFiles) {
        $sourceFile = Join-Path $sourceDir $targetFile.Name
        if (-not (Test-Path $sourceFile)) {
            continue
        }

        $sourceContent = Get-Content -Raw -LiteralPath $sourceFile
        $targetContent = Get-Content -Raw -LiteralPath $targetFile.FullName

        if ($sourceContent -ne $targetContent) {
            $relativeTarget = [System.IO.Path]::GetRelativePath($repoRoot, $targetFile.FullName)
            if ($Check) {
                $mismatches += $relativeTarget
            } else {
                Set-Content -LiteralPath $targetFile.FullName -Value $sourceContent -NoNewline
                $updated += $relativeTarget
            }
        }
    }
}

if ($Check) {
    if ($mismatches.Count -gt 0) {
        Write-Host "Design token copies have drifted from design/tokens/ (canonical source):"
        $mismatches | ForEach-Object { Write-Host "  - $_" }
        Write-Host "Run 'pwsh ./scripts/Sync-DesignTokens.ps1' to fix."
        exit 1
    }
    Write-Host "All design token copies match design/tokens/."
    exit 0
}

if ($updated.Count -gt 0) {
    Write-Host "Synced design token copies from design/tokens/:"
    $updated | ForEach-Object { Write-Host "  - $_" }
} else {
    Write-Host "All design token copies already match design/tokens/."
}
