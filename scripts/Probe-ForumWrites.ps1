# Runs an opt-in modern forum thread/post write probe against the local SQL Express mirror.
# Requires both ConnectionStrings__QueenZoneLegacy and RUN_FORUM_WRITE_PROBE=true.
#
# Example:
#   $env:ConnectionStrings__QueenZoneLegacy = "Server=localhost\SQLEXPRESS;Database=queenzone_legacy_sync;Integrated Security=True;TrustServerCertificate=True"
#   $env:RUN_FORUM_WRITE_PROBE = "true"
#   .\scripts\Probe-ForumWrites.ps1

param(
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($env:ConnectionStrings__QueenZoneLegacy)) {
    Write-Error "ConnectionStrings__QueenZoneLegacy is not set."
}

if ($env:RUN_FORUM_WRITE_PROBE -ne "true") {
    Write-Error "Set RUN_FORUM_WRITE_PROBE=true to run mutable forum write checks against the configured database."
}

& "$PSScriptRoot/Assert-SqlExpressMirrorConnection.ps1" `
    -ConnectionString $env:ConnectionStrings__QueenZoneLegacy

dotnet test tests/QueenZone.Web.Tests/QueenZone.Web.Tests.csproj `
    --configuration $Configuration `
    --filter "FullyQualifiedName~EfForumWriteLiveProbeTests"

if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}
