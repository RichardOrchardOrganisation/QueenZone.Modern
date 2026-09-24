[CmdletBinding()]
param(
    [string]$SubscriptionId = "610e3b3a-028d-4f1b-ac1d-a5567a4f8b9d",
    [string]$StateResourceGroup = "Queenzone-IaC-RG",
    [string]$StateStorageAccount = "queenzonetfstate",
    [string]$StateContainer = "tfstate",
    [string]$WorkloadResourceGroup = "Queenzone-RG",
    [string]$GitHubRepository = "richardorchard/QueenZone.Modern",
    [string]$OidcOwnerId = "",
    [string]$OidcRepositoryId = "",
    [string]$PlanFederatedCredentialName = "github-opentofu-plan",
    [string]$ApplyFederatedCredentialName = "github-opentofu-apply",
    [string]$BackendConfig = (Join-Path $PSScriptRoot "../backend/production.backend.hcl")
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

. (Join-Path $PSScriptRoot "Resolve-GitHubOidcRepositorySegment.ps1")
$OidcRepositorySegment = Resolve-GitHubOidcRepositorySegment -GitHubRepository $GitHubRepository -OidcOwnerId $OidcOwnerId -OidcRepositoryId $OidcRepositoryId

function Invoke-AzJson {
    param([Parameter(Mandatory)][string[]]$Arguments)

    $output = & az @Arguments --output json
    if ($LASTEXITCODE -ne 0) {
        throw "az failed with exit code $LASTEXITCODE."
    }

    return ($output -join "`n") | ConvertFrom-Json
}

function Invoke-GhJson {
    param([Parameter(Mandatory)][string[]]$Arguments)

    $output = & gh @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "gh failed with exit code $LASTEXITCODE."
    }

    return ($output -join "`n") | ConvertFrom-Json
}

function Assert-WorkloadIdentity {
    param(
        [Parameter(Mandatory)][string]$DisplayName,
        [Parameter(Mandatory)][string]$FederatedCredentialName,
        [Parameter(Mandatory)][string]$EnvironmentName,
        [Parameter(Mandatory)][hashtable]$ExpectedRoles
    )

    $applications = @(Invoke-AzJson @("ad", "app", "list", "--filter", "displayName eq '$DisplayName'"))
    if ($applications.Count -ne 1) {
        throw "Expected exactly one Entra application named '$DisplayName'."
    }

    $application = $applications[0]
    $servicePrincipal = Invoke-AzJson @("ad", "sp", "show", "--id", $application.appId)
    $credentials = @(Invoke-AzJson @("ad", "app", "federated-credential", "list", "--id", $application.id))
    $expectedSubject = "repo:$OidcRepositorySegment`:environment:$EnvironmentName"
    if (-not ($credentials | Where-Object {
                $_.name -eq $FederatedCredentialName -and
                $_.issuer -eq "https://token.actions.githubusercontent.com" -and
                $_.subject -eq $expectedSubject -and
                @($_.audiences).Count -eq 1 -and $_.audiences[0] -eq "api://AzureADTokenExchange"
            })) {
        throw "The expected GitHub OIDC credential is missing from '$DisplayName'."
    }

    $assignments = @(Invoke-AzJson @("role", "assignment", "list", "--assignee-object-id", $servicePrincipal.id, "--all"))
    foreach ($role in $ExpectedRoles.GetEnumerator()) {
        if (-not ($assignments | Where-Object { $_.roleDefinitionName -eq $role.Key -and $_.scope -eq $role.Value })) {
            throw "'$DisplayName' is missing role '$($role.Key)' at '$($role.Value)'."
        }
    }

    $forbidden = $assignments | Where-Object { $_.roleDefinitionName -in @("Owner", "User Access Administrator", "Role Based Access Control Administrator") }
    if ($forbidden) {
        throw "'$DisplayName' has a forbidden privileged role assignment."
    }
}

$account = Invoke-AzJson @("account", "show")
if ($account.id -ne $SubscriptionId) {
    throw "Azure CLI is using subscription '$($account.id)', expected '$SubscriptionId'."
}

$storage = Invoke-AzJson @("storage", "account", "show", "--resource-group", $StateResourceGroup, "--name", $StateStorageAccount)
if ($storage.allowBlobPublicAccess -ne $false -or $storage.allowSharedKeyAccess -ne $false -or $storage.minimumTlsVersion -ne "TLS1_2") {
    throw "The state storage account does not meet the public access, shared-key, or TLS controls."
}

$properties = Invoke-AzJson @(
    "storage", "account", "blob-service-properties", "show",
    "--resource-group", $StateResourceGroup,
    "--account-name", $StateStorageAccount
)
if ($properties.isVersioningEnabled -ne $true -or
    $properties.deleteRetentionPolicy.enabled -ne $true -or
    $properties.containerDeleteRetentionPolicy.enabled -ne $true) {
    throw "State versioning or soft-delete protection is disabled."
}

$containerId = "$($storage.id)/blobServices/default/containers/$StateContainer"
$container = Invoke-AzJson @("resource", "show", "--ids", $containerId, "--api-version", "2023-05-01")
if ($container.properties.publicAccess -notin @($null, "None")) {
    throw "State container '$StateContainer' is not private."
}

$locks = @(Invoke-AzJson @(
    "lock", "list",
    "--resource-group", $StateResourceGroup,
    "--resource-name", $StateStorageAccount,
    "--resource-type", "Microsoft.Storage/storageAccounts"
))
if (-not ($locks | Where-Object { $_.name -eq "protect-opentofu-state" -and $_.level -eq "CanNotDelete" })) {
    throw "The state storage account CanNotDelete lock is missing."
}

$workloadScope = "/subscriptions/$SubscriptionId/resourceGroups/$WorkloadResourceGroup"
Assert-WorkloadIdentity `
    -DisplayName "QueenZone OpenTofu Plan" `
    -FederatedCredentialName $PlanFederatedCredentialName `
    -EnvironmentName "opentofu-plan" `
    -ExpectedRoles @{
        "Storage Blob Data Contributor"                       = $containerId
        "Reader"                                              = $workloadScope
        "QueenZone OpenTofu Plan - App Service Config Reader" = $workloadScope
    }
Assert-WorkloadIdentity `
    -DisplayName "QueenZone OpenTofu Apply" `
    -FederatedCredentialName $ApplyFederatedCredentialName `
    -EnvironmentName "opentofu-apply" `
    -ExpectedRoles @{
        "Storage Blob Data Contributor" = $containerId
        "Contributor"                   = $workloadScope
    }

$planEnvironment = Invoke-GhJson @("api", "repos/$GitHubRepository/environments/opentofu-plan")
$applyEnvironment = Invoke-GhJson @("api", "repos/$GitHubRepository/environments/opentofu-apply")
foreach ($environment in @($planEnvironment, $applyEnvironment)) {
    if ($environment.deployment_branch_policy.protected_branches -ne $true -or
        $environment.deployment_branch_policy.custom_branch_policies -ne $false) {
        throw "GitHub environment '$($environment.name)' is not restricted to protected branches."
    }
}
if (-not ($applyEnvironment.protection_rules | Where-Object { $_.type -eq "required_reviewers" })) {
    throw "GitHub environment 'opentofu-apply' does not require approval."
}

$tofu = Get-Command tofu -ErrorAction SilentlyContinue
if ($null -eq $tofu) {
    Write-Warning "OpenTofu is not installed; Azure controls passed, but backend authentication was not exercised."
    return
}

$workDirectory = Join-Path ([System.IO.Path]::GetTempPath()) "queenzone-tofu-backend-test-$([guid]::NewGuid().ToString('N'))"
$null = New-Item -ItemType Directory -Path $workDirectory
try {
    @'
terraform {
  backend "azurerm" {}
}
'@ | Set-Content -LiteralPath (Join-Path $workDirectory "backend.tf") -Encoding utf8NoBOM

    & tofu "-chdir=$workDirectory" init -input=false "-backend-config=$BackendConfig"
    if ($LASTEXITCODE -ne 0) {
        throw "OpenTofu could not initialise the remote backend."
    }

    & tofu "-chdir=$workDirectory" state list
    if ($LASTEXITCODE -ne 0) {
        throw "OpenTofu could not read the remote state."
    }
}
finally {
    Remove-Item -LiteralPath $workDirectory -Recurse -Force -ErrorAction SilentlyContinue
}

Write-Output "OpenTofu state controls and local Entra backend access passed."
