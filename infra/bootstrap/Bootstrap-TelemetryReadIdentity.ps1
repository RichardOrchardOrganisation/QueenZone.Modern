<#
Bootstraps the OIDC identity the telemetry-triage workflow uses to read
fired App Insights / Log Analytics alerts (#1805).

Sibling to Bootstrap-DeployIdentity.ps1 and Bootstrap-OpenTofuState.ps1:
GitHub OIDC federated credentials are not managed in OpenTofu. This
script creates the Entra app, the immutable-subject federated credential,
the `telemetry-read` GitHub environment, Monitoring Reader on
Queenzone-RG, and Log Analytics Reader on queenzone-prod-law.

Richard runs this from an authorised workstation. It needs Entra
application-registration rights the OpenTofu apply identity does not
have. Do not create the Entra app from CI, and do not grant the apply
identity new Entra rights so these role assignments can move into
OpenTofu.
#>
[CmdletBinding(SupportsShouldProcess)]
param(
    [string]$SubscriptionId = "610e3b3a-028d-4f1b-ac1d-a5567a4f8b9d",
    [string]$WorkloadResourceGroup = "Queenzone-RG",
    [string]$LogAnalyticsWorkspaceName = "queenzone-prod-law",
    [string]$GitHubRepository = "RichardOrchardOrganisation/QueenZone.Modern", # pragma: allowlist secret
    [string]$OidcOwnerId = "333232587",
    [string]$OidcRepositoryId = "1265145026",
    [string]$FederatedCredentialName = "github-org-telemetry-read",
    [string]$EnvironmentName = "telemetry-read"
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

. (Join-Path $PSScriptRoot "Resolve-GitHubOidcRepositorySegment.ps1")
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

function Ensure-WorkloadIdentity {
    param(
        [Parameter(Mandatory)][string]$DisplayName,
        [Parameter(Mandatory)][string]$FederatedCredentialName,
        [Parameter(Mandatory)][string]$EnvironmentName
    )

    $applications = @(Invoke-AzJson @("ad", "app", "list", "--filter", "displayName eq '$DisplayName'"))
    if ($applications.Count -gt 1) {
        throw "More than one Entra application is named '$DisplayName'. Resolve the duplicate before continuing."
    }

    if ($applications.Count -eq 0) {
        if (-not $PSCmdlet.ShouldProcess($DisplayName, "Create Entra application")) {
            return $null
        }

        $application = Invoke-AzJson @("ad", "app", "create", "--display-name", $DisplayName)
    }
    else {
        $application = $applications[0]
    }

    $servicePrincipal = Try-AzJson @("ad", "sp", "show", "--id", $application.appId)
    if ($null -eq $servicePrincipal) {
        if ($PSCmdlet.ShouldProcess($DisplayName, "Create service principal")) {
            $servicePrincipal = Invoke-AzJson @("ad", "sp", "create", "--id", $application.appId)
        }
    }

    if ($null -eq $servicePrincipal) {
        throw "The service principal for '$DisplayName' was not created."
    }

    $subject = "repo:$OidcRepositorySegment`:environment:$EnvironmentName"
    $credential = @{
        name        = $FederatedCredentialName
        issuer      = "https://token.actions.githubusercontent.com"
        subject     = $subject
        audiences   = @("api://AzureADTokenExchange")
        description = "GitHub environment $EnvironmentName for $GitHubRepository"
    }

    $existingCredentials = @(Invoke-AzJson @("ad", "app", "federated-credential", "list", "--id", $application.id))
    $existingCredential = $existingCredentials | Where-Object { $_.name -eq $FederatedCredentialName } | Select-Object -First 1
    if ($null -ne $existingCredential -and
        ($existingCredential.subject -ne $subject -or $existingCredential.issuer -ne $credential.issuer -or
         @($existingCredential.audiences).Count -ne 1 -or $existingCredential.audiences[0] -ne "api://AzureADTokenExchange")) {
        throw "Federated credential '$FederatedCredentialName' exists with a different issuer or subject. Review it before changing trust."
    }

    if ($null -eq $existingCredential -and $existingCredentials.Count -ge 20) {
        throw "'$DisplayName' has reached the 20 federated-credential limit."
    }

    if ($null -eq $existingCredential -and $PSCmdlet.ShouldProcess($DisplayName, "Create GitHub OIDC federated credential")) {
        $credentialFile = Join-Path ([System.IO.Path]::GetTempPath()) "$FederatedCredentialName.json"
        try {
            $credential | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $credentialFile -Encoding utf8NoBOM
            $null = Invoke-AzJson @(
                "ad", "app", "federated-credential", "create",
                "--id", $application.id,
                "--parameters", $credentialFile
            )
        }
        finally {
            Remove-Item -LiteralPath $credentialFile -Force -ErrorAction SilentlyContinue
        }
    }

    return [pscustomobject]@{
        ClientId          = $application.appId
        PrincipalObjectId = $servicePrincipal.id
    }
}

function Set-TelemetryReadGitHubEnvironment {
    param(
        [Parameter(Mandatory)][string]$EnvironmentName,
        [Parameter(Mandatory)][string]$ClientId,
        [Parameter(Mandatory)][string]$TenantId,
        [Parameter(Mandatory)][string]$SubscriptionId
    )

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
    Write-Output "Would create or verify the telemetry-read OIDC identity, Monitoring Reader on $WorkloadResourceGroup, Log Analytics Reader on $LogAnalyticsWorkspaceName, and the $EnvironmentName GitHub environment."
    Write-Output "OIDC subject repository segment: $OidcRepositorySegment"
    Write-Output "Expected subject: repo:$OidcRepositorySegment`:environment:$EnvironmentName"
    Write-Output "Would not import or change any QueenZone application resource or OpenTofu state."
    return
}

$telemetryIdentity = Ensure-WorkloadIdentity -DisplayName "QueenZone Telemetry Read" -FederatedCredentialName $FederatedCredentialName -EnvironmentName $EnvironmentName

$resourceGroupScope = "/subscriptions/$SubscriptionId/resourceGroups/$WorkloadResourceGroup"
$workspace = Invoke-AzJson @(
    "monitor", "log-analytics", "workspace", "show",
    "--resource-group", $WorkloadResourceGroup,
    "--workspace-name", $LogAnalyticsWorkspaceName
)
$workspaceScope = $workspace.id

Ensure-RoleAssignment -PrincipalObjectId $telemetryIdentity.PrincipalObjectId -PrincipalType ServicePrincipal -Role "Monitoring Reader" -Scope $resourceGroupScope
Ensure-RoleAssignment -PrincipalObjectId $telemetryIdentity.PrincipalObjectId -PrincipalType ServicePrincipal -Role "Log Analytics Reader" -Scope $workspaceScope

Set-TelemetryReadGitHubEnvironment -EnvironmentName $EnvironmentName -ClientId $telemetryIdentity.ClientId -TenantId $account.tenantId -SubscriptionId $SubscriptionId

Write-Output "Telemetry-read identity bootstrap is configured for $LogAnalyticsWorkspaceName. No application resource or OpenTofu state was changed."
