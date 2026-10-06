# Runs an opt-in member-account create/external-login probe against the local SQL Express mirror.
# Requires both ConnectionStrings__QueenZoneLegacy and RUN_MEMBER_ACCOUNT_PROBE=true.
#
# Example:
#   $env:ConnectionStrings__QueenZoneLegacy = "Server=localhost\SQLEXPRESS;Database=queenzone_legacy_sync;Integrated Security=True;TrustServerCertificate=True"
#   $env:RUN_MEMBER_ACCOUNT_PROBE = "true"
#   .\scripts\Probe-MemberAccounts.ps1

param(
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($env:ConnectionStrings__QueenZoneLegacy)) {
    Write-Error "ConnectionStrings__QueenZoneLegacy is not set."
}

if ($env:RUN_MEMBER_ACCOUNT_PROBE -ne "true") {
    Write-Error "Set RUN_MEMBER_ACCOUNT_PROBE=true to run mutable member-account checks against the configured database."
}

& "$PSScriptRoot/Assert-SqlExpressMirrorConnection.ps1" `
    -ConnectionString $env:ConnectionStrings__QueenZoneLegacy

dotnet test tests/QueenZone.Web.Tests/QueenZone.Web.Tests.csproj `
    --configuration $Configuration `
    --logger "trx;LogFilePrefix=member-accounts" `
    --results-directory "$PSScriptRoot/../test-results/legacy-probes" `
    --filter "FullyQualifiedName~EfMemberAccountLiveProbeTests"
