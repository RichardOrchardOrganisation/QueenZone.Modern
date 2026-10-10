[CmdletBinding(SupportsShouldProcess)]
param(
    [string]$SubscriptionId = "610e3b3a-028d-4f1b-ac1d-a5567a4f8b9d",
    [string]$Location = "australiaeast",
    [string]$StateResourceGroup = "Queenzone-IaC-RG",
    [string]$StateStorageAccount = "queenzonetfstate",
    [string]$StateContainer = "tfstate",
    [string]$WorkloadResourceGroup = "Queenzone-RG",
    [string]$DevWorkloadResourceGroup = "Queenzone-Dev-RG",
    [string]$GitHubRepository = "RichardOrchardOrganisation/QueenZone.Modern",
    [string]$OidcOwnerId = "333232587",
    [string]$OidcRepositoryId = "1265145026",
    [string]$PlanFederatedCredentialName = "github-org-opentofu-plan",
    [string]$ApplyFederatedCredentialName = "github-org-opentofu-apply",
    [string]$ApplyReviewer = "richardorchard",
    [ValidateRange(7, 365)]
    [int]$RetentionDays = 30
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

. (Join-Path $PSScriptRoot "Resolve-GitHubOidcRepositorySegment.ps1")
. (Join-Path $PSScriptRoot "Ensure-WorkloadIdentity.ps1")
$OidcRepositorySegment = Resolve-GitHubOidcRepositorySegment -GitHubRepository $GitHubRepository -OidcOwnerId $OidcOwnerId -OidcRepositoryId $OidcRepositoryId

function Invoke-Native {
    param(
        [Parameter(Mandatory)]
        [string]$FilePath,
        [Parameter(Mandatory)]
        [string[]]$Arguments
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

function Ensure-CustomRoleDefinition {
    param(
        [Parameter(Mandatory)][string]$Name,
        [Parameter(Mandatory)][string[]]$Actions,
        [Parameter(Mandatory)][string]$AssignableScope,
        [Parameter(Mandatory)][string]$Description
    )

    $existing = @(Try-AzJson @("role", "definition", "list", "--name", $Name, "--scope", $AssignableScope))
    if ($existing.Count -gt 0) {
        return $Name
    }

    if (-not $PSCmdlet.ShouldProcess($Name, "Create custom role definition")) {
        return $Name
    }

    $definition = @{
        Name             = $Name
        IsCustom         = $true
        Description      = $Description
        Actions          = $Actions
        NotActions       = @()
        AssignableScopes = @($AssignableScope)
    }

    $definitionFile = Join-Path ([System.IO.Path]::GetTempPath()) "queenzone-$($Name -replace '[^a-zA-Z0-9]', '-').json"
    try {
        $definition | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $definitionFile -Encoding utf8NoBOM
        # `az role definition create`'s response does not flatten the same
        # way `list`/`show` do (roleName is not present at the top level),
        # so read it back only to confirm the call succeeded — the name we
        # need is the one we already set above.
        $null = Invoke-AzJson @("role", "definition", "create", "--role-definition", $definitionFile)
        return $Name
    }
    finally {
        Remove-Item -LiteralPath $definitionFile -Force -ErrorAction SilentlyContinue
    }
}

function Set-GitHubEnvironment {
    param(
        [Parameter(Mandatory)][string]$EnvironmentName,
        [Parameter(Mandatory)][string]$ClientId,
        [switch]$RequireReviewer
    )

    $reviewers = @()
    if ($RequireReviewer) {
        $reviewerId = (Invoke-Native -FilePath "gh" -Arguments @("api", "users/$ApplyReviewer", "--jq", ".id") | Select-Object -First 1)
        $reviewers = @(@{ type = "User"; id = [int64]$reviewerId })
    }

    $environmentBody = @{
        wait_timer               = 0
        prevent_self_review      = $false
        reviewers                = $reviewers
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
            ARM_CLIENT_ID            = $ClientId
            ARM_SUBSCRIPTION_ID      = $SubscriptionId
            ARM_TENANT_ID            = $script:TenantId
            TF_STATE_RESOURCE_GROUP  = $StateResourceGroup
            TF_STATE_STORAGE_ACCOUNT = $StateStorageAccount
            TF_STATE_CONTAINER       = $StateContainer
            TF_STATE_KEY             = "production.tfstate"
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

$script:TenantId = $account.tenantId
$operatorObjectId = (Invoke-Native -FilePath "az" -Arguments @("ad", "signed-in-user", "show", "--query", "id", "--output", "tsv") | Select-Object -First 1)

if ($WhatIfPreference) {
    Write-Output "Would create or verify state storage, data protection, OIDC identities, scoped role assignments, and GitHub environments."
    Write-Output "OIDC subject repository segment: $OidcRepositorySegment"
    Write-Output "Would not import or change any QueenZone application resource."
    return
}

if ($PSCmdlet.ShouldProcess($StateResourceGroup, "Create or update state resource group")) {
    $null = Invoke-AzJson @("group", "create", "--name", $StateResourceGroup, "--location", $Location)
}

$storageAccount = Try-AzJson @("storage", "account", "show", "--resource-group", $StateResourceGroup, "--name", $StateStorageAccount)
if ($null -eq $storageAccount) {
    if ($PSCmdlet.ShouldProcess($StateStorageAccount, "Create state storage account")) {
        $storageAccount = Invoke-AzJson @(
            "storage", "account", "create",
            "--resource-group", $StateResourceGroup,
            "--name", $StateStorageAccount,
            "--location", $Location,
            "--sku", "Standard_LRS",
            "--kind", "StorageV2",
            "--https-only", "true",
            "--min-tls-version", "TLS1_2",
            "--allow-blob-public-access", "false",
            "--allow-shared-key-access", "false",
            "--default-action", "Allow"
        )
    }
}

if ($null -eq $storageAccount) {
    throw "State storage account '$StateStorageAccount' is unavailable."
}

if ($PSCmdlet.ShouldProcess($StateStorageAccount, "Enable versioning and soft delete")) {
    $null = Invoke-AzJson @(
        "storage", "account", "blob-service-properties", "update",
        "--resource-group", $StateResourceGroup,
        "--account-name", $StateStorageAccount,
        "--enable-versioning", "true",
        "--enable-delete-retention", "true",
        "--delete-retention-days", $RetentionDays.ToString(),
        "--enable-container-delete-retention", "true",
        "--container-delete-retention-days", $RetentionDays.ToString()
    )
}

$storageScope = $storageAccount.id
$containerScope = "$storageScope/blobServices/default/containers/$StateContainer"
$container = Try-AzJson @("resource", "show", "--ids", $containerScope, "--api-version", "2023-05-01")
if ($null -eq $container -and $PSCmdlet.ShouldProcess($StateContainer, "Create private state container")) {
    $containerPropertiesFile = Join-Path ([System.IO.Path]::GetTempPath()) "queenzone-tofu-container.json"
    try {
        @{ publicAccess = "None" } | ConvertTo-Json | Set-Content -LiteralPath $containerPropertiesFile -Encoding utf8NoBOM
        $null = Invoke-AzJson @(
            "resource", "create",
            "--id", $containerScope,
            "--api-version", "2023-05-01",
            "--properties", "@$containerPropertiesFile"
        )
    }
    finally {
        Remove-Item -LiteralPath $containerPropertiesFile -Force -ErrorAction SilentlyContinue
    }
}

$planIdentity = Ensure-WorkloadIdentity -DisplayName "QueenZone OpenTofu Plan" -FederatedCredentialName $PlanFederatedCredentialName -EnvironmentName "opentofu-plan"
$applyIdentity = Ensure-WorkloadIdentity -DisplayName "QueenZone OpenTofu Apply" -FederatedCredentialName $ApplyFederatedCredentialName -EnvironmentName "opentofu-apply"

$workloadScope = "/subscriptions/$SubscriptionId/resourceGroups/$WorkloadResourceGroup"
Ensure-RoleAssignment -PrincipalObjectId $operatorObjectId -PrincipalType User -Role "Storage Blob Data Contributor" -Scope $containerScope
Ensure-RoleAssignment -PrincipalObjectId $planIdentity.PrincipalObjectId -PrincipalType ServicePrincipal -Role "Storage Blob Data Contributor" -Scope $containerScope
Ensure-RoleAssignment -PrincipalObjectId $planIdentity.PrincipalObjectId -PrincipalType ServicePrincipal -Role "Reader" -Scope $workloadScope

# Reader does not cover Microsoft.Web/sites/config/list/action — Azure gates
# that "list" action separately from */read even though it is read-only,
# because it can return app-service config content. Without this, tofu plan
# fails reading azurerm_linux_web_app auth settings with a 403
# AuthorizationFailed (confirmed on PR #1208's first real production plan
# run). Scoped to exactly this one action, nothing else.
$planConfigReaderRole = Ensure-CustomRoleDefinition `
    -Name "QueenZone OpenTofu Plan - App Service Config Reader" `
    -Description "Read-only list access to App Service site config (e.g. auth settings) that the built-in Reader role excludes. Used only by the QueenZone OpenTofu Plan identity." `
    -Actions @("Microsoft.Web/sites/config/list/action") `
    -AssignableScope $workloadScope
Ensure-RoleAssignment -PrincipalObjectId $planIdentity.PrincipalObjectId -PrincipalType ServicePrincipal -Role $planConfigReaderRole -Scope $workloadScope

Ensure-RoleAssignment -PrincipalObjectId $applyIdentity.PrincipalObjectId -PrincipalType ServicePrincipal -Role "Storage Blob Data Contributor" -Scope $containerScope
Ensure-RoleAssignment -PrincipalObjectId $applyIdentity.PrincipalObjectId -PrincipalType ServicePrincipal -Role "Contributor" -Scope $workloadScope

# #2211: blob public-access Azure Policy.
# Custom policy definitions can only live at subscription or management-group
# scope, so Apply needs policyDefinitions/write there (and Plan needs read to
# refresh them). This custom role grants only that, nothing else at subscription.
$subscriptionScope = "/subscriptions/$SubscriptionId"
$policyDefinitionWriterRole = Ensure-CustomRoleDefinition `
    -Name "QueenZone OpenTofu Apply - Policy Definition Writer" `
    -Description "Create, update and delete custom Azure Policy definitions. Used only by the QueenZone OpenTofu Apply identity (#2211)." `
    -Actions @("Microsoft.Authorization/policyDefinitions/read", "Microsoft.Authorization/policyDefinitions/write", "Microsoft.Authorization/policyDefinitions/delete") `
    -AssignableScope $subscriptionScope
Ensure-RoleAssignment -PrincipalObjectId $applyIdentity.PrincipalObjectId -PrincipalType ServicePrincipal -Role $policyDefinitionWriterRole -Scope $subscriptionScope

$policyDefinitionReaderRole = Ensure-CustomRoleDefinition `
    -Name "QueenZone OpenTofu Plan - Policy Definition Reader" `
    -Description "Read custom Azure Policy definitions so tofu plan can refresh them. Used only by the QueenZone OpenTofu Plan identity (#2211)." `
    -Actions @("Microsoft.Authorization/policyDefinitions/read") `
    -AssignableScope $subscriptionScope
Ensure-RoleAssignment -PrincipalObjectId $planIdentity.PrincipalObjectId -PrincipalType ServicePrincipal -Role $policyDefinitionReaderRole -Scope $subscriptionScope

# Contributor cannot write Microsoft.Authorization/*, so OpenTofu cannot create
# its own Resource Policy Contributor assignments. Grant RBAC Administrator on
# each workload RG with an ABAC condition that limits BOTH the role (Resource
# Policy Contributor only) AND the principal (the Apply identity only), on
# write and on delete.
# Template ("Constrain roles and principals"): Microsoft Learn, "Examples to delegate Azure role assignment
# management with conditions", "Example: Constrain roles and specific groups"
# https://learn.microsoft.com/azure/role-based-access-control/delegate-role-assignments-examples#example-constrain-roles-and-specific-groups
function New-ConstrainedRbacAdminCondition {
    param(
        [Parameter(Mandatory)][guid]$RoleDefinitionId,
        [Parameter(Mandatory)][guid]$PrincipalObjectId
    )

    $role = $RoleDefinitionId.ToString()
    $principal = $PrincipalObjectId.ToString()
    return @"
(
 (
  !(ActionMatches{'Microsoft.Authorization/roleAssignments/write'})
 )
 OR
 (
  @Request[Microsoft.Authorization/roleAssignments:RoleDefinitionId] ForAnyOfAnyValues:GuidEquals {$role}
  AND
  @Request[Microsoft.Authorization/roleAssignments:PrincipalId] ForAnyOfAnyValues:GuidEquals {$principal}
 )
)
AND
(
 (
  !(ActionMatches{'Microsoft.Authorization/roleAssignments/delete'})
 )
 OR
 (
  @Resource[Microsoft.Authorization/roleAssignments:RoleDefinitionId] ForAnyOfAnyValues:GuidEquals {$role}
  AND
  @Resource[Microsoft.Authorization/roleAssignments:PrincipalId] ForAnyOfAnyValues:GuidEquals {$principal}
 )
)
"@
}

function ConvertTo-NormalizedCondition {
    param([AllowNull()][string]$Condition)
    if ($null -eq $Condition) { return "" }
    return ($Condition -replace '\s+', '').ToLowerInvariant()
}

# Creates the constrained assignment, or reconciles an existing one. An
# existing assignment whose condition differs is deleted and recreated (shown
# by -WhatIf); it is never silently skipped.
function Ensure-ConstrainedRbacAdminAssignment {
    param(
        [Parameter(Mandatory)][string]$PrincipalObjectId,
        [Parameter(Mandatory)][string]$Scope,
        [Parameter(Mandatory)][string]$Condition
    )

    $role = "Role Based Access Control Administrator"
    $existing = @(Invoke-AzJson @("role", "assignment", "list", "--assignee", $PrincipalObjectId, "--role", $role, "--scope", $Scope) |
        Where-Object { $_.scope -eq $Scope })
    $wanted = ConvertTo-NormalizedCondition $Condition

    foreach ($assignment in $existing) {
        if ((ConvertTo-NormalizedCondition $assignment.condition) -eq $wanted) {
            Write-Output "Constrained RBAC Administrator for $PrincipalObjectId at $Scope is already correct."
            return
        }

        Write-Warning "RBAC Administrator assignment $($assignment.id) at $Scope has a different or missing condition. Current: '$($assignment.condition)'."
        if (-not $PSCmdlet.ShouldProcess($assignment.id, "Delete RBAC Administrator assignment whose condition differs (it will be recreated with the constrained condition)")) {
            continue
        }
        $null = Invoke-Native -FilePath "az" -Arguments @("role", "assignment", "delete", "--ids", $assignment.id)
    }

    if ($PSCmdlet.ShouldProcess("$PrincipalObjectId at $Scope", "Assign RBAC Administrator constrained to Resource Policy Contributor for this principal only")) {
        $null = Invoke-AzJson @(
            "role", "assignment", "create",
            "--assignee-object-id", $PrincipalObjectId,
            "--assignee-principal-type", "ServicePrincipal",
            "--role", $role,
            "--scope", $Scope,
            "--condition", $Condition,
            "--condition-version", "2.0"
        )

        $created = @(Invoke-AzJson @("role", "assignment", "list", "--assignee", $PrincipalObjectId, "--role", $role, "--scope", $Scope) |
            Where-Object { $_.scope -eq $Scope -and (ConvertTo-NormalizedCondition $_.condition) -eq $wanted })
        if ($created.Count -ne 1) {
            throw "Constrained RBAC Administrator assignment at $Scope did not read back with the expected condition."
        }
    }
}

$resourcePolicyContributorId = "36243c78-bf99-498c-9df9-86d9f8d28608"
$rbacCondition = New-ConstrainedRbacAdminCondition -RoleDefinitionId $resourcePolicyContributorId -PrincipalObjectId $applyIdentity.PrincipalObjectId
foreach ($rg in @($WorkloadResourceGroup, $DevWorkloadResourceGroup)) {
    Ensure-ConstrainedRbacAdminAssignment -PrincipalObjectId $applyIdentity.PrincipalObjectId -Scope "/subscriptions/$SubscriptionId/resourceGroups/$rg" -Condition $rbacCondition
}

if ($PSCmdlet.ShouldProcess($StateStorageAccount, "Apply CanNotDelete lock")) {
    $existingLock = Invoke-AzJson @(
        "lock", "list",
        "--resource-group", $StateResourceGroup,
        "--resource-name", $StateStorageAccount,
        "--resource-type", "Microsoft.Storage/storageAccounts"
    ) | Where-Object { $_.name -eq "protect-opentofu-state" } | Select-Object -First 1

    if ($null -eq $existingLock) {
        $null = Invoke-AzJson @(
            "lock", "create",
            "--name", "protect-opentofu-state",
            "--lock-type", "CanNotDelete",
            "--resource-group", $StateResourceGroup,
            "--resource-name", $StateStorageAccount,
            "--resource-type", "Microsoft.Storage/storageAccounts",
            "--notes", "Protect QueenZone OpenTofu remote state. Remove only under the documented recovery procedure."
        )
    }
}

Set-GitHubEnvironment -EnvironmentName "opentofu-plan" -ClientId $planIdentity.ClientId
Set-GitHubEnvironment -EnvironmentName "opentofu-apply" -ClientId $applyIdentity.ClientId -RequireReviewer

Write-Output "OpenTofu state bootstrap is configured. No application resource was imported or changed."
