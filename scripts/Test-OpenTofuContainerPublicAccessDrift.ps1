<#
.SYNOPSIS
  Fails when an OpenTofu plan changes any blob container publicAccess.

.DESCRIPTION
  Scheduled drift currently treats any plan diff as a comment-only
  opentofu-drift issue. Container ACL drift (#2209, #2211) must fail the
  job instead so the existing opentofu-drift-failure issue path fires.

  Inspects `tofu show -json` resource_changes for AzAPI blob containers
  and compares before/after publicAccess. A plan that only updates other
  attributes is not enough to fail this check.

.EXAMPLE
  ./scripts/Test-OpenTofuContainerPublicAccessDrift.ps1 -PlanJsonPath plan.json

.EXAMPLE
  ./scripts/Test-OpenTofuContainerPublicAccessDrift.ps1 -SelfTest
#>
[CmdletBinding(DefaultParameterSetName = "Check")]
param(
    [Parameter(Mandatory = $true, ParameterSetName = "Check")]
    [string]$PlanJsonPath,

    [Parameter(Mandatory = $true, ParameterSetName = "SelfTest")]
    [switch]$SelfTest
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

function ConvertTo-AzApiBodyObject {
    param($Body)

    if ($null -eq $Body) {
        return $null
    }

    if ($Body -is [string]) {
        if ([string]::IsNullOrWhiteSpace($Body)) {
            return $null
        }

        return $Body | ConvertFrom-Json
    }

    return $Body
}

function Get-AzApiPublicAccess {
    param($ChangeSide)

    if ($null -eq $ChangeSide) {
        return $null
    }

    $properties = $ChangeSide.PSObject.Properties.Name
    if ($properties -contains "body") {
        $body = ConvertTo-AzApiBodyObject -Body $ChangeSide.body
        if ($null -ne $body) {
            $bodyProperties = $body.PSObject.Properties.Name
            if ($bodyProperties -contains "properties" -and $null -ne $body.properties) {
                $accessProperties = $body.properties.PSObject.Properties.Name
                if ($accessProperties -contains "publicAccess") {
                    return [string]$body.properties.publicAccess
                }
            }
        }
    }

    if ($properties -contains "output" -and $null -ne $ChangeSide.output) {
        $output = ConvertTo-AzApiBodyObject -Body $ChangeSide.output
        if ($null -ne $output) {
            $outputProperties = $output.PSObject.Properties.Name
            if ($outputProperties -contains "properties" -and $null -ne $output.properties) {
                $accessProperties = $output.properties.PSObject.Properties.Name
                if ($accessProperties -contains "publicAccess") {
                    return [string]$output.properties.publicAccess
                }
            }
        }
    }

    return $null
}

function Test-IsBlobContainerChange {
    param($ResourceChange)

    if ($null -eq $ResourceChange) {
        return $false
    }

    $address = [string]$ResourceChange.address
    if ($address -match 'azapi_resource\.container\[' -or $address -match 'azapi_resource\.builds_container(\b|$)') {
        return $true
    }

    $type = [string]$ResourceChange.type
    if ($type -like '*blobServices/containers*') {
        return $true
    }

    return $false
}

function Get-ContainerPublicAccessDrift {
    param([Parameter(Mandatory = $true)]$ResourceChanges)

    $failures = [System.Collections.Generic.List[string]]::new()
    foreach ($resourceChange in @($ResourceChanges)) {
        if (-not (Test-IsBlobContainerChange -ResourceChange $resourceChange)) {
            continue
        }

        $actions = @()
        if ($null -ne $resourceChange.change -and $null -ne $resourceChange.change.actions) {
            $actions = @($resourceChange.change.actions)
        }

        if ($actions -contains "no-op" -or $actions -contains "read") {
            continue
        }

        $beforeAccess = Get-AzApiPublicAccess -ChangeSide $resourceChange.change.before
        $afterAccess = Get-AzApiPublicAccess -ChangeSide $resourceChange.change.after
        if ($beforeAccess -eq $afterAccess) {
            continue
        }

        $failures.Add("$($resourceChange.address): live publicAccess '$beforeAccess' differs from code '$afterAccess'")
    }

    return @($failures)
}

function New-ContainerChange {
    param(
        [string]$Address,
        [string[]]$Actions,
        $BeforeAccess,
        $AfterAccess,
        [switch]$BodyAsJson,
        [string]$Type = "azapi_resource"
    )

    function New-Side {
        param($Access)
        if ($null -eq $Access) {
            return $null
        }

        $body = [pscustomobject]@{
            properties = [pscustomobject]@{ publicAccess = $Access }
        }
        if ($BodyAsJson) {
            $body = ($body | ConvertTo-Json -Compress -Depth 5)
        }

        return [pscustomobject]@{ body = $body }
    }

    return [pscustomobject]@{
        address = $Address
        type    = $Type
        change  = [pscustomobject]@{
            actions = $Actions
            before  = (New-Side -Access $BeforeAccess)
            after   = (New-Side -Access $AfterAccess)
        }
    }
}

if ($PSCmdlet.ParameterSetName -eq "SelfTest") {
    $selfTestFailures = [System.Collections.Generic.List[string]]::new()

    $matchingUpdate = @(Get-ContainerPublicAccessDrift -ResourceChanges @(
            (New-ContainerChange -Address 'module.azure_data.azapi_resource.container["css"]' -Actions @("update") -BeforeAccess "Container" -AfterAccess "Blob")
        ))
    if ($matchingUpdate.Count -ne 1 -or $matchingUpdate[0] -notlike '*css*') {
        $selfTestFailures.Add("Expected a css publicAccess update to be reported.")
    }

    $jsonBody = @(Get-ContainerPublicAccessDrift -ResourceChanges @(
            (New-ContainerChange -Address 'module.azure_data_target.azapi_resource.container["test"]' -Actions @("update") -BeforeAccess "Blob" -AfterAccess "None" -BodyAsJson)
        ))
    if ($jsonBody.Count -ne 1) {
        $selfTestFailures.Add("Expected a JSON-string AzAPI body publicAccess update to be reported.")
    }

    $builds = @(Get-ContainerPublicAccessDrift -ResourceChanges @(
            (New-ContainerChange -Address "module.azure_mobile_builds.azapi_resource.builds_container" -Actions @("update") -BeforeAccess "Blob" -AfterAccess "Container")
        ))
    if ($builds.Count -ne 1) {
        $selfTestFailures.Add("Expected the mobile builds container to be inspected.")
    }

    $otherUpdate = @(Get-ContainerPublicAccessDrift -ResourceChanges @(
            (New-ContainerChange -Address 'module.azure_data.azapi_resource.container["css"]' -Actions @("update") -BeforeAccess "Blob" -AfterAccess "Blob")
        ))
    if ($otherUpdate.Count -ne 0) {
        $selfTestFailures.Add("Expected a container update that does not change publicAccess to pass.")
    }

    $unrelated = @(Get-ContainerPublicAccessDrift -ResourceChanges @(
            (New-ContainerChange -Address "module.azure_web.azurerm_linux_web_app.production" -Actions @("update") -BeforeAccess "Blob" -AfterAccess "None" -Type "azurerm_linux_web_app")
        ))
    if ($unrelated.Count -ne 0) {
        $selfTestFailures.Add("Expected non-container updates to be ignored even when a publicAccess field is present.")
    }

    $noOp = @(Get-ContainerPublicAccessDrift -ResourceChanges @(
            (New-ContainerChange -Address 'module.azure_data.azapi_resource.container["css"]' -Actions @("no-op") -BeforeAccess "Container" -AfterAccess "Blob")
        ))
    if ($noOp.Count -ne 0) {
        $selfTestFailures.Add("Expected no-op container reads to be ignored.")
    }

    if ($selfTestFailures.Count -gt 0) {
        $selfTestFailures | ForEach-Object { Write-Error $_ }
        exit 1
    }

    Write-Output "Test-OpenTofuContainerPublicAccessDrift self-test passed."
    exit 0
}

if (-not (Test-Path -LiteralPath $PlanJsonPath)) {
    throw "Plan JSON not found at $PlanJsonPath."
}

$plan = Get-Content -LiteralPath $PlanJsonPath -Raw | ConvertFrom-Json
$drift = @(Get-ContainerPublicAccessDrift -ResourceChanges @($plan.resource_changes))
if ($drift.Count -gt 0) {
    $message = @(
        "Container publicAccess drift must fail the scheduled OpenTofu drift job, not only open a plan-diff issue."
        $drift
    ) -join [Environment]::NewLine
    throw $message
}

Write-Output "No blob container publicAccess drift in the plan."
