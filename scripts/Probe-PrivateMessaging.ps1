# Runs an opt-in private-messaging IDENTITY SortKey + write-lock probe against the local SQL Express mirror.
# Requires both ConnectionStrings__QueenZoneLegacy and RUN_PRIVATE_MESSAGE_PROBE=true.
#
# Example:
#   $env:ConnectionStrings__QueenZoneLegacy = "Server=localhost\SQLEXPRESS;Database=queenzone_legacy_sync;Integrated Security=True;TrustServerCertificate=True"
#   $env:RUN_PRIVATE_MESSAGE_PROBE = "true"
#   .\scripts\Probe-PrivateMessaging.ps1

param(
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($env:ConnectionStrings__QueenZoneLegacy)) {
    Write-Error "ConnectionStrings__QueenZoneLegacy is not set."
}

if ($env:RUN_PRIVATE_MESSAGE_PROBE -ne "true") {
    Write-Error "Set RUN_PRIVATE_MESSAGE_PROBE=true to run mutable private-messaging checks against the configured database."
}

& "$PSScriptRoot/Assert-SqlExpressMirrorConnection.ps1" `
    -ConnectionString $env:ConnectionStrings__QueenZoneLegacy

dotnet test tests/QueenZone.Web.Tests/QueenZone.Web.Tests.csproj `
    --configuration $Configuration `
    --logger "trx;LogFilePrefix=private-messaging" `
    --results-directory "$PSScriptRoot/../test-results/legacy-probes" `
    --filter "FullyQualifiedName~EfPrivateMessageLiveProbeTests"
