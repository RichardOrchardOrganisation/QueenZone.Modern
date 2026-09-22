<#
.SYNOPSIS
  Fails a plan that proposes to delete or replace any resource, and writes a
  redacted (resource address + action only, no attribute values) summary.

.DESCRIPTION
  Takes the JSON produced by `tofu show -json <planfile>` and inspects
  `resource_changes[].change.actions`. This stack's own rule is "never
  destroy production" (see docs/architecture/opentofu-contributor-runbook.md)
  and every critical resource already sets `lifecycle.prevent_destroy = true`
  (enforced by Test-OpenTofuSafety.ps1) — so a real delete or replace
  proposal here means stop and investigate, not something to allow through
  CI with an override list.

  Declarative `import {}` blocks (see infra/environments/production/imports.tf)
  surface as an entry with `change.importing` set; these are reported
  separately from create/update/delete/replace and never fail the check.

  A `forget` action (`lifecycle.destroy = false`, or a removed block) drops
  state only.
  It is reported and does not fail the destructive check.

  A create or update that adds AllowAllWindowsAzureIps, or the 0.0.0.0
  range, on module.azure_data_target fails. The summary still prints
  addresses only.

.EXAMPLE
  ./scripts/Test-OpenTofuPlanSafety.ps1 -PlanJsonPath plan.json -SummaryPath summary.md

.EXAMPLE
  ./scripts/Test-OpenTofuPlanSafety.ps1 -SelfTest
#>
[CmdletBinding(DefaultParameterSetName = "Check")]
param(
    [Parameter(Mandatory = $true, ParameterSetName = "Check")]
    [string]$PlanJsonPath,

    [Parameter(ParameterSetName = "Check")]
    [string]$SummaryPath,

    [Parameter(Mandatory = $true, ParameterSetName = "SelfTest")]
    [switch]$SelfTest
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

function Get-OpenTofuPlanBuckets {
    param([Parameter(Mandatory = $true)]$ResourceChanges)

    $buckets = [ordered]@{
        import  = [System.Collections.Generic.List[string]]::new()
        create  = [System.Collections.Generic.List[string]]::new()
        update  = [System.Collections.Generic.List[string]]::new()
        replace = [System.Collections.Generic.List[string]]::new()
        delete  = [System.Collections.Generic.List[string]]::new()
        forget  = [System.Collections.Generic.List[string]]::new()
    }

    foreach ($resourceChange in $ResourceChanges) {
        $actions = @($resourceChange.change.actions)
        $address = $resourceChange.address

        if ($resourceChange.change.PSObject.Properties.Name -contains "importing" -and $null -ne $resourceChange.change.importing) {
            $buckets.import.Add($address)
            continue
        }

        $hasCreate = $actions -contains "create"
        $hasDelete = $actions -contains "delete"

        if ($hasCreate -and $hasDelete) {
            $buckets.replace.Add($address)
        }
        elseif ($hasDelete) {
            $buckets.delete.Add($address)
        }
        elseif ($hasCreate) {
            $buckets.create.Add($address)
        }
        elseif ($actions -contains "forget") {
            $buckets.forget.Add($address)
        }
        elseif ($actions -contains "update") {
            $buckets.update.Add($address)
        }
        # "no-op" and "read" (data sources) are intentionally not reported.
    }

    return $buckets
}

function Get-OpenTofuPlanSummaryText {
    param([Parameter(Mandatory = $true)]$Buckets)

    $summaryLines = [System.Collections.Generic.List[string]]::new()
    $summaryLines.Add("## OpenTofu plan summary")
    $summaryLines.Add("")
    $summaryLines.Add("| Action | Count |")
    $summaryLines.Add("| --- | --- |")
    foreach ($key in $Buckets.Keys) {
        $summaryLines.Add("| $key | $($Buckets[$key].Count) |")
    }

    foreach ($key in $Buckets.Keys) {
        if ($Buckets[$key].Count -eq 0) {
            continue
        }
        $summaryLines.Add("")
        $summaryLines.Add("### $key")
        foreach ($address in $Buckets[$key]) {
            $summaryLines.Add("- ``$address``")
        }
    }

    return $summaryLines -join "`n"
}

function Test-OpenTofuPlanDestructive {
    param([Parameter(Mandatory = $true)]$Buckets)

    return ($Buckets.replace.Count + $Buckets.delete.Count) -gt 0
}

function Get-PlanAfterValue {
    param($Change, [string]$Name)

    if ($null -eq $Change) {
        return $null
    }

    $changeProperties = $Change.PSObject.Properties.Name
    if ($changeProperties -notcontains "after" -or $null -eq $Change.after) {
        return $null
    }

    $afterProperties = $Change.after.PSObject.Properties.Name
    if ($afterProperties -notcontains $Name) {
        return $null
    }

    return $Change.after.$Name
}

function Test-OpenTofuPlanAddsAzureServicesFirewallRule {
    param([Parameter(Mandatory = $true)]$ResourceChanges)

    foreach ($resourceChange in @($ResourceChanges)) {
        if ($null -eq $resourceChange) {
            continue
        }

        $actions = @($resourceChange.change.actions)
        $addsRule = ($actions -contains "create") -or ($actions -contains "update")
        if (-not $addsRule) {
            continue
        }

        $address = [string]$resourceChange.address
        if ($address -notlike "module.azure_data_target.*azurerm_mssql_firewall_rule.*") {
            continue
        }

        $name = Get-PlanAfterValue -Change $resourceChange.change -Name "name"
        $startIp = Get-PlanAfterValue -Change $resourceChange.change -Name "start_ip_address"
        $endIp = Get-PlanAfterValue -Change $resourceChange.change -Name "end_ip_address"
        $isWideName = $name -eq "AllowAllWindowsAzureIps" -or $address -like "*azurerm_mssql_firewall_rule.azure_services*"
        $isWideRange = $startIp -eq "0.0.0.0" -and $endIp -eq "0.0.0.0"
        if ($isWideName -or $isWideRange) {
            return $true
        }
    }

    return $false
}

function New-FixtureResourceChange {
    param(
        [string]$Address,
        [string[]]$Actions,
        [switch]$Importing,
        [hashtable]$After
    )

    $change = [ordered]@{ actions = $Actions }
    if ($Importing) {
        $change.importing = @{ id = "fixture-id" }
    }
    if ($null -ne $After) {
        $change.after = [pscustomobject]$After
    }
    return [pscustomobject]@{ address = $Address; change = [pscustomobject]$change }
}

if ($PSCmdlet.ParameterSetName -eq "SelfTest") {
    $failures = [System.Collections.Generic.List[string]]::new()

    # An import-only, create-only, and update-only plan must pass.
    $safeChanges = @(
        (New-FixtureResourceChange -Address "azurerm_resource_group.production" -Actions @("no-op") -Importing),
        (New-FixtureResourceChange -Address "azurerm_storage_container.example" -Actions @("create")),
        (New-FixtureResourceChange -Address "cloudflare_dns_record.www" -Actions @("update"))
    )
    $safeBuckets = Get-OpenTofuPlanBuckets -ResourceChanges $safeChanges
    if (Test-OpenTofuPlanDestructive -Buckets $safeBuckets) {
        $failures.Add("Expected a safe (import/create/update) plan to pass, but it was flagged as destructive.")
    }
    if ($safeBuckets.import.Count -ne 1 -or $safeBuckets.create.Count -ne 1 -or $safeBuckets.update.Count -ne 1) {
        $failures.Add("Expected exactly one import, one create, and one update in the safe fixture.")
    }

    # A pure delete must fail.
    $deleteBuckets = Get-OpenTofuPlanBuckets -ResourceChanges @(
        (New-FixtureResourceChange -Address "azurerm_storage_container.doomed" -Actions @("delete"))
    )
    if (-not (Test-OpenTofuPlanDestructive -Buckets $deleteBuckets)) {
        $failures.Add("Expected a delete-only plan to be flagged as destructive.")
    }

    # A replace (delete+create) must fail.
    $replaceBuckets = Get-OpenTofuPlanBuckets -ResourceChanges @(
        (New-FixtureResourceChange -Address "azurerm_mssql_database.production" -Actions @("delete", "create"))
    )
    if (-not (Test-OpenTofuPlanDestructive -Buckets $replaceBuckets)) {
        $failures.Add("Expected a replace plan to be flagged as destructive.")
    }

    $forgetBuckets = Get-OpenTofuPlanBuckets -ResourceChanges @(
        (New-FixtureResourceChange -Address "module.azure_data_target.azurerm_mssql_firewall_rule.azure_services[0]" -Actions @("forget"))
    )
    if (Test-OpenTofuPlanDestructive -Buckets $forgetBuckets) {
        $failures.Add("Expected a forget (state removal without an Azure delete) to pass the destructive check.")
    }
    if ($forgetBuckets.forget.Count -ne 1) {
        $failures.Add("Expected the forget action to be reported separately.")
    }

    $wideRule = @(
        (New-FixtureResourceChange -Address "module.azure_data_target.azurerm_mssql_firewall_rule.azure_services[0]" -Actions @("create") -After @{
                name             = "AllowAllWindowsAzureIps"
                start_ip_address = "0.0.0.0"
                end_ip_address   = "0.0.0.0"
            })
    )
    if (-not (Test-OpenTofuPlanAddsAzureServicesFirewallRule -ResourceChanges $wideRule)) {
        $failures.Add("Expected a plan that creates AllowAllWindowsAzureIps on the production target to fail.")
    }

    $wideRange = @(
        (New-FixtureResourceChange -Address "module.azure_data_target.azurerm_mssql_firewall_rule.explicit[`"wide`"]" -Actions @("update") -After @{
                name             = "wide"
                start_ip_address = "0.0.0.0"
                end_ip_address   = "0.0.0.0"
            })
    )
    if (-not (Test-OpenTofuPlanAddsAzureServicesFirewallRule -ResourceChanges $wideRange)) {
        $failures.Add("Expected an update to 0.0.0.0-0.0.0.0 on the production target to fail.")
    }

    $explicitRule = @(
        (New-FixtureResourceChange -Address "module.azure_data_target.azurerm_mssql_firewall_rule.explicit[`"AppService-203-0-113-10`"]" -Actions @("create") -After @{
                name             = "AppService-203-0-113-10"
                start_ip_address = "203.0.113.10"
                end_ip_address   = "203.0.113.10"
            })
    )
    if (Test-OpenTofuPlanAddsAzureServicesFirewallRule -ResourceChanges $explicitRule) {
        $failures.Add("Expected an explicit App Service firewall rule on the production target to pass.")
    }

    $retainedNoOp = @(
        (New-FixtureResourceChange -Address "module.azure_data.azurerm_mssql_firewall_rule.azure_services[0]" -Actions @("no-op") -After @{
                name             = "AllowAllWindowsAzureIps"
                start_ip_address = "0.0.0.0"
                end_ip_address   = "0.0.0.0"
            })
    )
    if (Test-OpenTofuPlanAddsAzureServicesFirewallRule -ResourceChanges $retainedNoOp) {
        $failures.Add("Expected the retained server's existing AllowAllWindowsAzureIps rule to stay a no-op.")
    }

    if ($failures.Count -gt 0) {
        $failures | ForEach-Object { Write-Error $_ }
        exit 1
    }

    Write-Output "Test-OpenTofuPlanSafety self-test passed."
    exit 0
}

if (-not (Test-Path -LiteralPath $PlanJsonPath)) {
    throw "Plan JSON not found at $PlanJsonPath."
}

$plan = Get-Content -LiteralPath $PlanJsonPath -Raw | ConvertFrom-Json
$buckets = Get-OpenTofuPlanBuckets -ResourceChanges @($plan.resource_changes)
$summary = Get-OpenTofuPlanSummaryText -Buckets $buckets
Write-Output $summary

if ($SummaryPath) {
    Set-Content -LiteralPath $SummaryPath -Value $summary -NoNewline
}

if (Test-OpenTofuPlanDestructive -Buckets $buckets) {
    $destructiveCount = $buckets.replace.Count + $buckets.delete.Count
    throw "Plan proposes $destructiveCount destructive change(s) (delete/replace). This stack never destroys production resources through CI — stop and investigate before touching this configuration further."
}

if (Test-OpenTofuPlanAddsAzureServicesFirewallRule -ResourceChanges @($plan.resource_changes)) {
    throw "Plan adds AllowAllWindowsAzureIps on the production SQL target. That rule allows any Azure tenant to attempt the SQL login."
}

Write-Output "No destructive (delete/replace) changes proposed."
