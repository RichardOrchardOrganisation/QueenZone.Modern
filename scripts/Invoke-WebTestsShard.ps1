<#
.SYNOPSIS
  Runs one mixed CI shard of QueenZone.Web.Tests (plus optional small test projects).

.DESCRIPTION
  Used by .github/workflows/ci.yml and for local shard timing. See
  docs/architecture/testing-policy.md (CI test sharding) and issue #442.

  Local full-suite verification remains `dotnet test QueenZone.sln` — sharding is for
  CI wall-clock. Do not invent a unit-only vs WAF-only project split for speed.

.PARAMETER ShardIndex
  Zero-based shard index.

.PARAMETER ShardCount
  Total shards.

.PARAMETER Configuration
  Build configuration (default Release).

.PARAMETER CollectCoverage
  Collect Coverlet XPlat coverage into -ResultsDirectory.

.PARAMETER IncludeSmallProjects
  Also run Tools/Storage/NewsAgent test projects alongside the Web.Tests shard.

.PARAMETER SmallProjectsOnly
  Run only the Tools/Storage/NewsAgent test projects, skipping Web.Tests entirely.
  CI runs these in their own parallel job instead of piling them onto shard 0 —
  see the `small-projects-tests` job in .github/workflows/ci.yml and issue #496.

.PARAMETER ResultsDirectory
  Coverage / TRX results directory.

.PARAMETER NoBuild
  Pass --no-build to dotnet test (CI downloads build outputs from the build job).

.PARAMETER NoRestore
  Pass --no-restore to dotnet test.

.EXAMPLE
  pwsh -File ./scripts/Invoke-WebTestsShard.ps1 -ShardIndex 0 -ShardCount 4 -IncludeSmallProjects

.EXAMPLE
  pwsh -File ./scripts/Invoke-WebTestsShard.ps1 -ShardIndex 1 -ShardCount 4 -CollectCoverage
#>
[CmdletBinding()]
param(
    [Parameter()]
    [ValidateRange(0, 63)]
    [int] $ShardIndex = 0,

    [Parameter()]
    [ValidateRange(1, 64)]
    [int] $ShardCount = 4,

    [Parameter()]
    [string] $Configuration = "Release",

    [Parameter()]
    [switch] $CollectCoverage,

    [Parameter()]
    [switch] $IncludeSmallProjects,

    [Parameter()]
    [switch] $SmallProjectsOnly,

    [Parameter()]
    [string] $ResultsDirectory = "./TestResults",

    [Parameter()]
    [switch] $NoBuild,

    [Parameter()]
    [switch] $NoRestore
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
Set-Location $repoRoot

function Invoke-DotNetTest {
    param(
        [string] $Project,
        [string] $Filter = ""
    )

    $dotnetArgs = @(
        "test", $Project,
        "--configuration", $Configuration
    )
    if ($NoBuild) { $dotnetArgs += "--no-build" }
    if ($NoRestore) { $dotnetArgs += "--no-restore" }
    if (-not [string]::IsNullOrWhiteSpace($Filter)) {
        $dotnetArgs += @("--filter", $Filter)
    }
    if ($CollectCoverage) {
        $projectName = [System.IO.Path]::GetFileNameWithoutExtension($Project)
        $dotnetArgs += @(
            "--collect:XPlat Code Coverage",
            "--settings", "coverlet.runsettings",
            "--results-directory", $ResultsDirectory,
            "--logger", "trx;LogFilePrefix=$projectName-shard-$ShardIndex"
        )
    }

    Write-Host ">> dotnet $($dotnetArgs -join ' ')"
    & dotnet @dotnetArgs
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet test failed for $Project (exit $LASTEXITCODE)."
    }
}

if ($IncludeSmallProjects -or $SmallProjectsOnly) {
    $smallProjects = @(
        "tests/QueenZone.Tools.Tests/QueenZone.Tools.Tests.csproj",
        "tests/QueenZone.Storage.Tests/QueenZone.Storage.Tests.csproj",
        "tests/QueenZone.NewsAgent.Tests/QueenZone.NewsAgent.Tests.csproj",
        "tests/QueenZone.Search.Shared.Tests/QueenZone.Search.Shared.Tests.csproj"
    )
    foreach ($project in $smallProjects) {
        Invoke-DotNetTest -Project $project
    }
}

if ($SmallProjectsOnly) {
    Write-Host "Small test projects completed successfully."
    return
}

$filterScript = Join-Path $PSScriptRoot "Get-WebTestShardFilter.ps1"
$filter = & $filterScript -ShardIndex $ShardIndex -ShardCount $ShardCount
if ([string]::IsNullOrWhiteSpace($filter)) {
    throw "Empty shard filter for shard $ShardIndex / $ShardCount."
}

Write-Host "Web.Tests shard $ShardIndex / $ShardCount filter length=$($filter.Length)"

Invoke-DotNetTest `
    -Project "tests/QueenZone.Web.Tests/QueenZone.Web.Tests.csproj" `
    -Filter $filter

Write-Host "Shard $ShardIndex completed successfully."
