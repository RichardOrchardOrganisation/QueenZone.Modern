# #2211: constrained "Role Based Access Control Administrator" for the OpenTofu
# Apply identity on the workload resource groups. Dot-sourced by
# Bootstrap-OpenTofuState.ps1 and by tests/Test-ConstrainedRbacAdmin.ps1.
# Requires Invoke-AzJson and Invoke-Native from the caller.

$ResourcePolicyContributorRoleId = "36243c78-bf99-498c-9df9-86d9f8d28608"
$RbacAdministratorRoleName = "Role Based Access Control Administrator"

# Template ("Constrain roles and principals"): Microsoft Learn, "Examples to
# delegate Azure role assignment management with conditions", "Example:
# Constrain roles and specific groups"
# https://learn.microsoft.com/azure/role-based-access-control/delegate-role-assignments-examples#example-constrain-roles-and-specific-groups
# Limits BOTH the role and the principal, on write (@Request) and delete (@Resource).
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

# The roots grant Resource Policy Contributor to var.opentofu_apply_principal_object_id.
# Fail if that default differs from the principal the bootstrap derived.
function Assert-ApplyPrincipalMatchesOpenTofu {
    param(
        [Parameter(Mandatory)][string]$PrincipalObjectId,
        [Parameter(Mandatory)][string]$RepositoryRoot
    )

    foreach ($root in @("dev", "production")) {
        $path = Join-Path $RepositoryRoot "infra/environments/$root/variables.tf"
        $text = Get-Content -LiteralPath $path -Raw
        $match = [regex]::Match($text, 'variable\s+"opentofu_apply_principal_object_id"\s*\{[^}]*?default\s*=\s*"([0-9a-fA-F-]{36})"', 'Singleline')
        if (-not $match.Success) {
            throw "Could not find the opentofu_apply_principal_object_id default in $path."
        }
        if ($match.Groups[1].Value.ToLowerInvariant() -ne $PrincipalObjectId.ToLowerInvariant()) {
            throw "OpenTofu $root default opentofu_apply_principal_object_id '$($match.Groups[1].Value)' does not match the derived Apply principal '$PrincipalObjectId'. Fix the variable before running the bootstrap."
        }
    }
    Write-Output "OpenTofu dev/production opentofu_apply_principal_object_id matches $PrincipalObjectId."
}

function Get-RbacAdminAssignmentsAtScope {
    param([string]$PrincipalObjectId, [string]$Scope)
    return @(Invoke-AzJson @("role", "assignment", "list", "--assignee", $PrincipalObjectId, "--role", $RbacAdministratorRoleName, "--scope", $Scope) |
        Where-Object { $null -ne $_ -and $_.scope -eq $Scope })
}

# Creates the constrained assignment or reconciles an existing one. Reads are
# always performed (also under -WhatIf) so the preview lists current
# conditions; only the delete and create are gated by ShouldProcess.
function Ensure-ConstrainedRbacAdminAssignment {
    [CmdletBinding(SupportsShouldProcess)]
    param(
        [Parameter(Mandatory)][string]$PrincipalObjectId,
        [Parameter(Mandatory)][string]$Scope,
        [Parameter(Mandatory)][string]$Condition,
        [int[]]$ReadBackDelaysSeconds = @(2, 4, 8, 15)
    )

    $wanted = ConvertTo-NormalizedCondition $Condition
    $existing = @(Get-RbacAdminAssignmentsAtScope -PrincipalObjectId $PrincipalObjectId -Scope $Scope)
    Write-Output "Existing RBAC Administrator assignments for $PrincipalObjectId at ${Scope}: $($existing.Count)"
    foreach ($assignment in $existing) {
        Write-Output "  $($assignment.id) condition: $(if ($assignment.condition) { $assignment.condition -replace '\s+', ' ' } else { '<none>' })"
    }

    $correct = @($existing | Where-Object { (ConvertTo-NormalizedCondition $_.condition) -eq $wanted })
    $differing = @($existing | Where-Object { (ConvertTo-NormalizedCondition $_.condition) -ne $wanted })

    foreach ($assignment in $differing) {
        Write-Warning "RBAC Administrator assignment $($assignment.id) at $Scope has a different or missing condition; it will be deleted and recreated."
        if ($PSCmdlet.ShouldProcess($assignment.id, "Delete RBAC Administrator assignment whose condition differs")) {
            $null = Invoke-Native -FilePath "az" -Arguments @("role", "assignment", "delete", "--ids", $assignment.id)
        }
    }

    if ($correct.Count -gt 0) {
        Write-Output "Constrained RBAC Administrator for $PrincipalObjectId at $Scope is already correct."
        return
    }

    if (-not $PSCmdlet.ShouldProcess("$PrincipalObjectId at $Scope", "Create RBAC Administrator constrained to Resource Policy Contributor for this principal only")) {
        return
    }

    $null = Invoke-AzJson @(
        "role", "assignment", "create",
        "--assignee-object-id", $PrincipalObjectId,
        "--assignee-principal-type", "ServicePrincipal",
        "--role", $RbacAdministratorRoleName,
        "--scope", $Scope,
        "--condition", $Condition,
        "--condition-version", "2.0"
    )

    # RBAC reads are eventually consistent; retry the read-back with backoff.
    $attempt = 0
    while ($true) {
        $readBack = @(Get-RbacAdminAssignmentsAtScope -PrincipalObjectId $PrincipalObjectId -Scope $Scope |
            Where-Object { (ConvertTo-NormalizedCondition $_.condition) -eq $wanted })
        if ($readBack.Count -ge 1) {
            Write-Output "Created constrained RBAC Administrator for $PrincipalObjectId at $Scope."
            return
        }
        if ($attempt -ge $ReadBackDelaysSeconds.Count) {
            throw "Constrained RBAC Administrator assignment at $Scope did not read back with the expected condition after $($attempt + 1) attempts."
        }
        Start-Sleep -Seconds $ReadBackDelaysSeconds[$attempt]
        $attempt++
    }
}
