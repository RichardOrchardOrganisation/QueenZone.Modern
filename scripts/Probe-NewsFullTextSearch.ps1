# Read-only news full-text search probe against the SQL Express mirror.
# Requires ConnectionStrings__QueenZoneLegacy and RUN_NEWS_FTS_PROBE=true.
param([string]$Configuration = "Release")

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($env:ConnectionStrings__QueenZoneLegacy)) {
    Write-Error "ConnectionStrings__QueenZoneLegacy is not set."
}

if ($env:RUN_NEWS_FTS_PROBE -ne "true") {
    Write-Error "Set RUN_NEWS_FTS_PROBE=true to run the news full-text search probe."
}

& "$PSScriptRoot/Assert-SqlExpressMirrorConnection.ps1" `
    -ConnectionString $env:ConnectionStrings__QueenZoneLegacy

dotnet test tests/QueenZone.Web.Tests/QueenZone.Web.Tests.csproj `
    --configuration $Configuration `
    --filter "FullyQualifiedName~EfNewsFullTextSearchLiveProbeTests"
