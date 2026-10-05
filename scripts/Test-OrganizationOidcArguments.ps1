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

# Exercise the shared function with offline Azure stubs and WhatIf. No cloud writes.
. (Join-Path $PSScriptRoot "../infra/bootstrap/Ensure-WorkloadIdentity.ps1")
$OidcRepositorySegment = 'fixture'
$GitHubRepository = 'fixture'
$script:fixtureCredentials = @()
function Invoke-AzJson {
    param([string[]]$Arguments)
    if (($Arguments[0..2] -join ' ') -eq 'ad app list') { return [pscustomobject]@{ appId='client'; id='app' } }
    if (($Arguments[0..2] -join ' ') -eq 'ad app federated-credential') { return $script:fixtureCredentials }
    throw "Unexpected write or Azure command: $($Arguments -join ' ')"
}
function Try-AzJson {
    param([string[]]$Arguments)
    if (($Arguments -join ' ') -ne 'ad sp show --id client') { throw 'Unexpected service-principal lookup.' }
    return [pscustomobject]@{ id='principal' }
}
function Invoke-IdentityFixture {
    [CmdletBinding(SupportsShouldProcess)]
    param()
    Ensure-WorkloadIdentity -DisplayName 'fixture' -FederatedCredentialName 'fixture' -EnvironmentName 'fixture'
}
$result = Invoke-IdentityFixture -WhatIf
if ($result.ClientId -ne 'client' -or $result.PrincipalObjectId -ne 'principal') { throw 'WhatIf identity result changed.' }
$script:fixtureCredentials = @([pscustomobject]@{ name='fixture'; subject='repo:fixture:environment:fixture'; issuer=$issuer; audiences=@('api://AzureADTokenExchange') })
$result = Invoke-IdentityFixture
if ($result.ClientId -ne 'client') { throw 'Existing identity result changed.' }
$script:fixtureCredentials[0].subject = 'wrong'
$rejected = $false
try { Invoke-IdentityFixture | Out-Null } catch { $rejected = $_.Exception.Message -match 'different issuer or subject' }
if (-not $rejected) { throw 'Shared workload function accepted conflicting trust.' }
$script:fixtureCredentials = @(1..20 | ForEach-Object { [pscustomobject]@{ name="other-$_" } })
$rejected = $false
try { Invoke-IdentityFixture -WhatIf | Out-Null } catch { $rejected = $_.Exception.Message -match '20 federated-credential limit' }
if (-not $rejected) { throw 'Shared workload function accepted an exhausted credential list.' }
Write-Output 'PASS: shared workload identity preserves WhatIf, existing credentials, trust mismatch and credential limit (offline).'
