$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "../infra/bootstrap/Resolve-GitHubOidcRepositorySegment.ps1")

$old = Resolve-GitHubOidcRepositorySegment -GitHubRepository "richardorchard/QueenZone.Modern"
if ($old -ne "richardorchard/QueenZone.Modern") { throw "Legacy OIDC segment changed." }

$new = Resolve-GitHubOidcRepositorySegment `
    -GitHubRepository "RichardOrchardOrganisation/QueenZone.Modern" `
    -OidcOwnerId "333232587" -OidcRepositoryId "1265145026"
if ($new -ne "RichardOrchardOrganisation@333232587/QueenZone.Modern@1265145026") {
    throw "Immutable OIDC segment is incorrect."
}

foreach ($case in @(
    @{ GitHubRepository = "RichardOrchardOrganisation/QueenZone.Modern" },
    @{ GitHubRepository = "richardorchard/QueenZone.Modern"; OidcOwnerId = "333232587" },
    @{ GitHubRepository = "RichardOrchardOrganisation/QueenZone.Modern"; OidcOwnerId = "bad"; OidcRepositoryId = "1265145026" },
    @{ GitHubRepository = "RichardOrchardOrganisation/QueenZone.Modern/extra"; OidcOwnerId = "333232587"; OidcRepositoryId = "1265145026" }
)) {
    $rejected = $false
    try { $null = Resolve-GitHubOidcRepositorySegment @case }
    catch { $rejected = $true }
    if (-not $rejected) { throw "Unsafe OIDC arguments were accepted." }
}

Write-Output "OIDC repository segment self-test passed."
