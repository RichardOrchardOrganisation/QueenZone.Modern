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

  Also rejects a main-site App Service `ip_address` that contains a comma
  (one CIDR per rule). SCM may stay Allow while the main site is Deny:
  production deploy still reaches SCM from GitHub-hosted runners.

  Declarative `import {}` blocks (see infra/environments/production/imports.tf)
  surface as an entry with `change.importing` set; these are reported
  separately from create/update/delete/replace and never fail the check.

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

function Get-OpenTofuPlanIngressFailures {
    param([Parameter(Mandatory = $true)]$ResourceChanges)

    $failures = [System.Collections.Generic.List[string]]::new()
    foreach ($resourceChange in $ResourceChanges) {
        $change = $resourceChange.change
        $afterProperty = $change.PSObject.Properties["after"]
        if ($null -eq $afterProperty -or $null -eq $afterProperty.Value) {
            continue
        }

        $siteConfigProperty = $afterProperty.Value.PSObject.Properties["site_config"]
        if ($null -eq $siteConfigProperty -or $null -eq $siteConfigProperty.Value) {
            continue
        }

        foreach ($siteConfig in @($siteConfigProperty.Value)) {
            $restrictionProperty = $siteConfig.PSObject.Properties["ip_restriction"]
            if ($null -ne $restrictionProperty -and $null -ne $restrictionProperty.Value) {
                foreach ($rule in @($restrictionProperty.Value)) {
                    $addressProperty = $rule.PSObject.Properties["ip_address"]
                    if ($null -ne $addressProperty -and $null -ne $addressProperty.Value -and [string]$addressProperty.Value -match ",") {
                        $failures.Add("$($resourceChange.address): ip_address must be one CIDR, found '$($addressProperty.Value)'.")
                    }
                }
            }
        }
    }

    return $failures
}

function New-FixtureResourceChange {
    param([string]$Address, [string[]]$Actions, [switch]$Importing)

    $change = [ordered]@{ actions = $Actions }
    if ($Importing) {
        $change.importing = @{ id = "fixture-id" }
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

    function New-FixtureWebAppChange {
        param(
            [string]$MainDefaultAction,
            [string]$ScmDefaultAction,
            [string[]]$IpAddresses
        )

        $rules = [System.Collections.Generic.List[object]]::new()
        foreach ($ip in $IpAddresses) {
            $rules.Add([pscustomobject]@{ ip_address = $ip })
        }

        $siteConfig = [pscustomobject]@{
            ip_restriction                    = $rules
            ip_restriction_default_action     = $MainDefaultAction
            scm_ip_restriction_default_action = $ScmDefaultAction
        }
        return [pscustomobject]@{
            address = "module.azure_web.azurerm_linux_web_app.production"
            change  = [pscustomobject]@{
                actions = @("update")
                after   = [pscustomobject]@{ site_config = @($siteConfig) }
            }
        }
    }

    $splitRanges = @(Get-OpenTofuPlanIngressFailures -ResourceChanges @(
        (New-FixtureWebAppChange -MainDefaultAction "Deny" -ScmDefaultAction "Allow" -IpAddresses @("173.245.48.0/20"))
    ))
    if ($splitRanges.Count -ne 0) {
        $failures.Add("Expected one Cloudflare CIDR with main Deny and SCM Allow to pass the ingress check.")
    }

    $directDev = @(Get-OpenTofuPlanIngressFailures -ResourceChanges @(
        (New-FixtureWebAppChange -MainDefaultAction "Allow" -ScmDefaultAction "Allow" -IpAddresses @())
    ))
    if ($directDev.Count -ne 0) {
        $failures.Add("Expected direct-access Allow/Allow with no rules to pass the ingress check.")
    }

    $commaRanges = @(Get-OpenTofuPlanIngressFailures -ResourceChanges @(
        (New-FixtureWebAppChange -MainDefaultAction "Deny" -ScmDefaultAction "Allow" -IpAddresses @("173.245.48.0/20,103.21.244.0/22"))
    ))
    if ($commaRanges.Count -eq 0) {
        $failures.Add("Expected a comma-separated ip_address to be rejected.")
    }

    $unchangedFixtures = @(Get-OpenTofuPlanIngressFailures -ResourceChanges $safeChanges)
    if ($unchangedFixtures.Count -ne 0) {
        $failures.Add("Expected plans without site_config to skip the ingress check.")
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

$ingressFailures = @(Get-OpenTofuPlanIngressFailures -ResourceChanges @($plan.resource_changes))
if ($ingressFailures.Count -gt 0) {
    throw ($ingressFailures -join [Environment]::NewLine)
}

Write-Output "No destructive (delete/replace) changes proposed."
