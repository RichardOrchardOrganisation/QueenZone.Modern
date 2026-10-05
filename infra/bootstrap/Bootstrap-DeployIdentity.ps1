<#
Bootstraps the OIDC identity `deploy.yml` uses to write ARM Application
Settings (WEBSITE_WARMUP_PATH; also deletes WEBSITE_WARMUP_STATUSES and
WEBSITE_RUN_FROM_PACKAGE if either reappears live) on queenzone-prod (#666/#1272).

Sibling to Bootstrap-OpenTofuState.ps1, not an extension of it: this
identity is scoped to Website Contributor on one site, not Contributor on
the whole resource group, and carries no required reviewer — reusing
opentofu-apply here would put an approval gate on every routine deploy.
#>
[CmdletBinding(SupportsShouldProcess)]
param(
    [string]$SubscriptionId = "610e3b3a-028d-4f1b-ac1d-a5567a4f8b9d",
    [string]$WorkloadResourceGroup = "Queenzone-RG",
    [string]$WebAppName = "queenzone-prod",
    [string]$GitHubRepository = "RichardOrchardOrganisation/QueenZone.Modern",
    [string]$OidcOwnerId = "333232587",
    [string]$OidcRepositoryId = "1265145026",
    [string]$FederatedCredentialName = "github-org-prod-deploy",
    [string]$EnvironmentName = "prod-deploy"
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

. (Join-Path $PSScriptRoot "Resolve-GitHubOidcRepositorySegment.ps1")
. (Join-Path $PSScriptRoot "Ensure-WorkloadIdentity.ps1")
$OidcRepositorySegment = Resolve-GitHubOidcRepositorySegment -GitHubRepository $GitHubRepository -OidcOwnerId $OidcOwnerId -OidcRepositoryId $OidcRepositoryId

function Invoke-Native {
    param(
        [Parameter(Mandatory)][string]$FilePath,
        [Parameter(Mandatory)][string[]]$Arguments
    )

    $output = & $FilePath @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "$FilePath failed with exit code $LASTEXITCODE."
    }

    return $output
}

function Invoke-AzJson {
    param([Parameter(Mandatory)][string[]]$Arguments)

    $output = Invoke-Native -FilePath "az" -Arguments ($Arguments + @("--output", "json"))
    if ([string]::IsNullOrWhiteSpace(($output -join "`n"))) {
        return $null
    }

    return ($output -join "`n") | ConvertFrom-Json
}

function Try-AzJson {
    param([Parameter(Mandatory)][string[]]$Arguments)

    $output = & az @Arguments --output json 2>$null
    if ($LASTEXITCODE -ne 0) {
        return $null
    }

    if ([string]::IsNullOrWhiteSpace(($output -join "`n"))) {
        return $null
    }

    return ($output -join "`n") | ConvertFrom-Json
}

function Ensure-RoleAssignment {
    param(
        [Parameter(Mandatory)][string]$PrincipalObjectId,
        [Parameter(Mandatory)][ValidateSet("ServicePrincipal", "User")][string]$PrincipalType,
        [Parameter(Mandatory)][string]$Role,
        [Parameter(Mandatory)][string]$Scope
    )

    $existing = Invoke-AzJson @(
        "role", "assignment", "list",
        "--assignee-object-id", $PrincipalObjectId,
        "--role", $Role,
        "--scope", $Scope
    )

    if (@($existing).Count -gt 0) {
        return
    }

    if ($PSCmdlet.ShouldProcess("$PrincipalObjectId at $Scope", "Assign $Role")) {
        $null = Invoke-AzJson @(
            "role", "assignment", "create",
            "--assignee-object-id", $PrincipalObjectId,
            "--assignee-principal-type", $PrincipalType,
            "--role", $Role,
            "--scope", $Scope
        )
    }
}

function Set-DeployGitHubEnvironment {
    param(
        [Parameter(Mandatory)][string]$EnvironmentName,
        [Parameter(Mandatory)][string]$ClientId,
        [Parameter(Mandatory)][string]$TenantId,
        [Parameter(Mandatory)][string]$SubscriptionId
    )

    # No required reviewer here (unlike opentofu-apply) — routine deploys
    # should not need manual approval for an idempotent settings write.
    $environmentBody = @{
        wait_timer               = 0
        prevent_self_review      = $false
        reviewers                = @()
        deployment_branch_policy = @{
            protected_branches     = $true
            custom_branch_policies = $false
        }
    }

    if ($PSCmdlet.ShouldProcess("GitHub environment $EnvironmentName", "Configure protection and variables")) {
        $environmentFile = Join-Path ([System.IO.Path]::GetTempPath()) "$EnvironmentName-environment.json"
        try {
            $environmentBody | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $environmentFile -Encoding utf8NoBOM
            $null = Invoke-Native -FilePath "gh" -Arguments @(
                "api", "--method", "PUT",
                "repos/$GitHubRepository/environments/$EnvironmentName",
                "--input", $environmentFile
            )
        }
        finally {
            Remove-Item -LiteralPath $environmentFile -Force -ErrorAction SilentlyContinue
        }

        $variables = @{
            ARM_CLIENT_ID       = $ClientId
            ARM_SUBSCRIPTION_ID = $SubscriptionId
            ARM_TENANT_ID       = $TenantId
        }

        foreach ($entry in $variables.GetEnumerator()) {
            $null = Invoke-Native -FilePath "gh" -Arguments @(
                "variable", "set", $entry.Key,
                "--env", $EnvironmentName,
                "--repo", $GitHubRepository,
                "--body", $entry.Value
            )
        }
    }
}

$account = Invoke-AzJson @("account", "show")
if ($account.id -ne $SubscriptionId) {
    throw "Azure CLI is using subscription '$($account.id)', expected '$SubscriptionId'. Run az account set first."
}

if ($WhatIfPreference) {
    Write-Output "Would create or verify the deploy OIDC identity, Website Contributor role assignment on $WebAppName, and the $EnvironmentName GitHub environment."
    Write-Output "OIDC subject repository segment: $OidcRepositorySegment"
    Write-Output "Would not import or change any QueenZone application resource setting."
    return
}

$deployIdentity = Ensure-WorkloadIdentity -DisplayName "QueenZone Deploy" -FederatedCredentialName $FederatedCredentialName -EnvironmentName $EnvironmentName

$webApp = Invoke-AzJson @("webapp", "show", "--name", $WebAppName, "--resource-group", $WorkloadResourceGroup)
$siteScope = $webApp.id

Ensure-RoleAssignment -PrincipalObjectId $deployIdentity.PrincipalObjectId -PrincipalType ServicePrincipal -Role "Website Contributor" -Scope $siteScope

Set-DeployGitHubEnvironment -EnvironmentName $EnvironmentName -ClientId $deployIdentity.ClientId -TenantId $account.tenantId -SubscriptionId $SubscriptionId

Write-Output "Deploy identity bootstrap is configured for $WebAppName. No application resource setting was changed."
