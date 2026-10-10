<#
Fake-az self-test for Ensure-ConstrainedRbacAdminAssignment.ps1 (#2211).
Run: pwsh -File infra/bootstrap/tests/Test-ConstrainedRbacAdmin.ps1
No Azure access; Invoke-AzJson / Invoke-Native are replaced by an in-memory fake.
#>
$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

. (Join-Path $PSScriptRoot "../Ensure-ConstrainedRbacAdminAssignment.ps1")

$principal = "e5e5ea3b-2a6e-4b62-abb8-947e5e66378c"
$scope = "/subscriptions/00000000-0000-0000-0000-000000000000/resourceGroups/rg"
$condition = New-ConstrainedRbacAdminCondition -RoleDefinitionId $ResourcePolicyContributorRoleId -PrincipalObjectId $principal
$failures = [System.Collections.Generic.List[string]]::new()

function Reset-Fake {
    param([object[]]$Assignments, [int]$HideAfterCreateReads = 0)
    $script:fakeAssignments = [System.Collections.Generic.List[object]]::new()
    foreach ($a in $Assignments) { $script:fakeAssignments.Add($a) }
    $script:writes = [System.Collections.Generic.List[string]]::new()
    $script:hideReads = $HideAfterCreateReads
    $script:created = $false
}

function Invoke-AzJson {
    param([string[]]$Arguments)
    $verb = $Arguments[2]
    if ($verb -eq "list") {
        if ($script:created -and $script:hideReads -gt 0) {
            $script:hideReads--
            return @($script:fakeAssignments | Where-Object { $_.id -ne "created" })
        }
        return @($script:fakeAssignments)
    }
    if ($verb -eq "create") {
        $script:writes.Add("create")
        $cond = $Arguments[[array]::IndexOf($Arguments, "--condition") + 1]
        $script:fakeAssignments.Add([pscustomobject]@{ id = "created"; scope = $scope; condition = $cond })
        $script:created = $true
        return $null
    }
    throw "unexpected az call: $($Arguments -join ' ')"
}

function Invoke-Native {
    param([string]$FilePath, [string[]]$Arguments)
    if ($Arguments[2] -ne "delete") { throw "unexpected az call: $($Arguments -join ' ')" }
    $id = $Arguments[[array]::IndexOf($Arguments, "--ids") + 1]
    $script:writes.Add("delete:$id")
    $keep = @($script:fakeAssignments | Where-Object { $_.id -ne $id })
    $script:fakeAssignments.Clear(); foreach ($a in $keep) { $script:fakeAssignments.Add($a) }
}

function Assert-Case {
    param([string]$Name, [scriptblock]$Check)
    try { if (-not (& $Check)) { $failures.Add($Name) } } catch { $failures.Add("$Name threw: $_") }
}

function Invoke-Case {
    param([switch]$WhatIf, [int[]]$Delays = @(0, 0, 0))
    return @(Ensure-ConstrainedRbacAdminAssignment -PrincipalObjectId $principal -Scope $scope -Condition $condition -ReadBackDelaysSeconds $Delays -WhatIf:$WhatIf 3>&1 6>&1)
}

$loose = [pscustomobject]@{ id = "loose"; scope = $scope; condition = "((!(ActionMatches{'Microsoft.Authorization/roleAssignments/write'})) OR (@Request[Microsoft.Authorization/roleAssignments:RoleDefinitionId] ForAnyOfAnyValues:GuidEquals {$ResourcePolicyContributorRoleId}))" }
$same = [pscustomobject]@{ id = "same"; scope = $scope; condition = ($condition -replace '\s+', ' ').ToUpperInvariant() }

# Condition shape
Assert-Case "condition constrains principal on write and delete" { ([regex]::Matches($condition, "roleAssignments:PrincipalId\] ForAnyOfAnyValues:GuidEquals \{$principal\}")).Count -eq 2 -and $condition -match '@Request\[Microsoft.Authorization/roleAssignments:PrincipalId\]' -and $condition -match '@Resource\[Microsoft.Authorization/roleAssignments:PrincipalId\]' }

# 1. No assignment -> create, read back
Reset-Fake @()
$null = Invoke-Case
Assert-Case "no assignment creates once" { ($script:writes -join ",") -eq "create" }

# 2. Already correct (whitespace/case differ) -> no writes
Reset-Fake @($same)
$out = Invoke-Case
Assert-Case "already correct is a no-op" { $script:writes.Count -eq 0 -and ($out -join "`n") -match "already correct" }

# 3. Loose condition -> delete then create
Reset-Fake @($loose)
$null = Invoke-Case
Assert-Case "loose condition is deleted and recreated" { ($script:writes -join ",") -eq "delete:loose,create" }

# 4. WhatIf with loose -> lists condition, no writes
Reset-Fake @($loose)
$out = Invoke-Case -WhatIf
Assert-Case "WhatIf lists existing condition and writes nothing" { $script:writes.Count -eq 0 -and ($out -join "`n") -match "loose condition: .*RoleDefinitionId" -and ($out -join "`n") -match "deleted and recreated" }

# 5. WhatIf with none -> no writes
Reset-Fake @()
$null = Invoke-Case -WhatIf
Assert-Case "WhatIf with no assignment writes nothing" { $script:writes.Count -eq 0 }

# 6. Read-back eventually consistent -> retries then succeeds
Reset-Fake @() -HideAfterCreateReads 2
$null = Invoke-Case -Delays @(0, 0, 0)
Assert-Case "read-back retries until visible" { ($script:writes -join ",") -eq "create" -and $script:hideReads -eq 0 }

# 7. Read-back never visible -> throws
Reset-Fake @() -HideAfterCreateReads 99
$threw = $false
try { $null = Invoke-Case -Delays @(0, 0) } catch { $threw = $_.Exception.Message -match "did not read back" }
Assert-Case "read-back throws after bounded retries" { $threw }

# 8. Principal check against the OpenTofu roots
$repo = Join-Path $PSScriptRoot "../../.."
Assert-Case "principal matches OpenTofu default" { $null = Assert-ApplyPrincipalMatchesOpenTofu -PrincipalObjectId $principal -RepositoryRoot $repo; $true }
$mismatch = $false
try { $null = Assert-ApplyPrincipalMatchesOpenTofu -PrincipalObjectId "11111111-1111-1111-1111-111111111111" -RepositoryRoot $repo } catch { $mismatch = $_.Exception.Message -match "does not match" }
Assert-Case "principal mismatch fails" { $mismatch }

if ($failures.Count -gt 0) {
    $failures | ForEach-Object { Write-Error "FAIL: $_" -ErrorAction Continue }
    exit 1
}
Write-Output "Test-ConstrainedRbacAdmin: all cases passed."
