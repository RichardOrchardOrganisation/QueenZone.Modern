$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "../infra/bootstrap/Resolve-GitHubOidcRepositorySegment.ps1")
. (Join-Path $PSScriptRoot "../infra/bootstrap/Assert-GitHubFederatedCredential.ps1")

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

$subject = 'repo:fixture:environment:fixture'
$issuer = 'https://token.actions.githubusercontent.com'
Assert-GitHubFederatedCredential -ExistingCredential $null -Subject $subject -Issuer $issuer -CredentialName 'fixture'
$good = [pscustomobject]@{ subject=$subject; issuer=$issuer; audiences=@('api://AzureADTokenExchange') }
Assert-GitHubFederatedCredential -ExistingCredential $good -Subject $subject -Issuer $issuer -CredentialName 'fixture'
foreach ($case in @(
    @{subject='wrong';issuer=$issuer;audiences=@('api://AzureADTokenExchange')},
    @{subject=$subject;issuer='wrong';audiences=@('api://AzureADTokenExchange')},
    @{subject=$subject;issuer=$issuer;audiences=@()},
    @{subject=$subject;issuer=$issuer;audiences=@('wrong')},
    @{subject=$subject;issuer=$issuer;audiences=@('api://AzureADTokenExchange','extra')}
)) {
    $failed=$false
    try { Assert-GitHubFederatedCredential -ExistingCredential ([pscustomobject]$case) -Subject $subject -Issuer $issuer -CredentialName 'fixture' }
    catch { $failed=$true; if ($_.Exception.Message -notlike "Federated credential 'fixture' exists*") { throw } }
    if (-not $failed) { throw 'Invalid trust accepted' }
}
Write-Output 'PASS: absent/matching credential accepted; all five trust mismatches rejected.'
