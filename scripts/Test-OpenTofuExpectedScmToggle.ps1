<#
.SYNOPSIS
  Detects a plan whose only change is the temp-veronica SCM action toggle.

.DESCRIPTION
  Nightly drift should not open an issue when live temp-veronica is Allow
  and the plan only sets it back to Deny. Comparisons are case-sensitive.
  Any other SCM restriction change, extra resource, or attribute still
  counts as real drift.

  Writes expected_toggle=true|false to GITHUB_OUTPUT when that file is set.
  Never prints IP addresses.

.EXAMPLE
  ./scripts/Test-OpenTofuExpectedScmToggle.ps1 -PlanJsonPath plan.json

.EXAMPLE
  ./scripts/Test-OpenTofuExpectedScmToggle.ps1 -SelfTest
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

$script:TempVeronicaRuleName = "temp-veronica"
$script:ProductionWebAppAddress = "module.azure_web_target.azurerm_linux_web_app.production"

function Test-IsMaterialPlanChange {
    param($ResourceChange)

    if ($null -eq $ResourceChange -or $null -eq $ResourceChange.change) {
        return $false
    }

    $actions = @()
    if ($null -ne $ResourceChange.change.actions) {
        $actions = @($ResourceChange.change.actions)
    }

    $material = @($actions | Where-Object { $_ -cnotin @("no-op", "read") })
    return $material.Count -gt 0
}

function ConvertTo-NormalizedJson {
    param($Value)

    if ($null -eq $Value) {
        return "null"
    }

    return ($Value | ConvertTo-Json -Compress -Depth 100)
}

function Get-ObjectPropertyNames {
    param($Value)

    if ($null -eq $Value) {
        return @()
    }

    if ($Value -is [System.Collections.IDictionary]) {
        return @($Value.Keys | ForEach-Object { [string]$_ })
    }

    $properties = $Value.PSObject.Properties
    if ($null -eq $properties) {
        return @()
    }

    return @($properties.Name)
}

function Get-ObjectPropertyValue {
    param($Value, [string]$Name)

    if ($null -eq $Value) {
        return $null
    }

    if ($Value -is [System.Collections.IDictionary]) {
        if ($Value.Contains($Name)) {
            return $Value[$Name]
        }

        return $null
    }

    $property = $Value.PSObject.Properties[$Name]
    if ($null -eq $property) {
        return $null
    }

    return $property.Value
}

function Test-IsListLike {
    param($Value)

    if ($null -eq $Value -or $Value -is [string] -or $Value -is [System.Collections.IDictionary]) {
        return $false
    }

    return $Value -is [System.Collections.IEnumerable]
}

function Test-IsObjectLike {
    param($Value)

    if ($null -eq $Value -or $Value -is [string] -or $Value -is [ValueType]) {
        return $false
    }

    if ($Value -is [System.Collections.IDictionary]) {
        return $true
    }

    if (Test-IsListLike -Value $Value) {
        return $false
    }

    return $Value -is [PSCustomObject]
}

function Get-PlanValueDifferences {
    param($Before, $After, [string]$Path)

    $differences = [System.Collections.Generic.List[string]]::new()
    if ((ConvertTo-NormalizedJson -Value $Before) -ceq (ConvertTo-NormalizedJson -Value $After)) {
        return @($differences)
    }

    $beforeIsList = Test-IsListLike -Value $Before
    $afterIsList = Test-IsListLike -Value $After
    if ($beforeIsList -or $afterIsList) {
        $beforeItems = @($Before)
        $afterItems = @($After)
        $count = [Math]::Max($beforeItems.Count, $afterItems.Count)
        for ($index = 0; $index -lt $count; $index++) {
            $childPath = "${Path}[$index]"
            if ($index -ge $beforeItems.Count -or $index -ge $afterItems.Count) {
                $differences.Add($childPath)
                continue
            }

            foreach ($difference in @(Get-PlanValueDifferences -Before $beforeItems[$index] -After $afterItems[$index] -Path $childPath)) {
                $differences.Add([string]$difference)
            }
        }

        return @($differences)
    }

    if ((Test-IsObjectLike -Value $Before) -or (Test-IsObjectLike -Value $After)) {
        $names = @((Get-ObjectPropertyNames -Value $Before) + (Get-ObjectPropertyNames -Value $After) | Select-Object -Unique)
        foreach ($name in $names) {
            $childPath = if ([string]::IsNullOrWhiteSpace($Path)) { $name } else { "$Path.$name" }
            foreach ($difference in @(Get-PlanValueDifferences -Before (Get-ObjectPropertyValue -Value $Before -Name $name) -After (Get-ObjectPropertyValue -Value $After -Name $name) -Path $childPath)) {
                $differences.Add([string]$difference)
            }
        }

        return @($differences)
    }

    $differences.Add($Path)
    return @($differences)
}

function Get-NamedScmRestriction {
    param($WebApp, [string]$Name)

    if ($null -eq $WebApp) {
        return $null
    }

    $matches = [System.Collections.Generic.List[object]]::new()
    foreach ($siteConfig in @($WebApp.site_config)) {
        if ($null -eq $siteConfig) {
            continue
        }

        foreach ($rule in @($siteConfig.scm_ip_restriction)) {
            if ($null -ne $rule -and [string]$rule.name -ceq $Name) {
                $matches.Add($rule)
            }
        }
    }

    if ($matches.Count -ne 1) {
        return $null
    }

    return $matches[0]
}

function Test-IsExpectedTempVeronicaActionToggle {
    param($ResourceChanges)

    $material = @($ResourceChanges | Where-Object { Test-IsMaterialPlanChange -ResourceChange $_ })
    if ($material.Count -ne 1) {
        return $false
    }

    $resourceChange = $material[0]
    if ([string]$resourceChange.address -cne $script:ProductionWebAppAddress) {
        return $false
    }

    $actions = @($resourceChange.change.actions)
    if ($actions.Count -ne 1 -or $actions[0] -cne "update") {
        return $false
    }

    $beforeRule = Get-NamedScmRestriction -WebApp $resourceChange.change.before -Name $script:TempVeronicaRuleName
    $afterRule = Get-NamedScmRestriction -WebApp $resourceChange.change.after -Name $script:TempVeronicaRuleName
    if ($null -eq $beforeRule -or $null -eq $afterRule) {
        return $false
    }

    # Config always declares Deny. Only a live Allow flipped back is expected.
    if ([string]$beforeRule.action -cne "Allow" -or [string]$afterRule.action -cne "Deny") {
        return $false
    }

    $savedBefore = [string]$beforeRule.action
    $savedAfter = [string]$afterRule.action
    try {
        $beforeRule.action = "Deny"
        $afterRule.action = "Deny"
        $differences = @(Get-PlanValueDifferences -Before $resourceChange.change.before -After $resourceChange.change.after -Path "")
        return $differences.Count -eq 0
    }
    finally {
        $beforeRule.action = $savedBefore
        $afterRule.action = $savedAfter
    }
}

function Write-ExpectedToggleOutput {
    param([bool]$Expected)

    $label = if ($Expected) { "true" } else { "false" }
    if ($env:GITHUB_OUTPUT) {
        Add-Content -LiteralPath $env:GITHUB_OUTPUT -Value "expected_toggle=$label"
    }

    if ($Expected) {
        Write-Output "Plan only toggles the temp-veronica SCM action; treat as an expected operator window."
    }
    else {
        Write-Output "Plan is not an expected temp-veronica SCM action toggle."
    }
}

function New-WebAppChange {
    param(
        [string]$Address = $script:ProductionWebAppAddress,
        [string[]]$Actions = @("update"),
        [string]$BeforeAction = "Allow",
        [string]$AfterAction = "Deny",
        [string]$BeforeName = "temp-veronica",
        [string]$AfterName = "temp-veronica",
        [string]$BeforeIp = "192.0.2.10/32",
        [string]$AfterIp = "192.0.2.10/32",
        [hashtable]$BeforeExtra = @{},
        [hashtable]$AfterExtra = @{}
    )

    function New-App {
        param([string]$Action, [string]$Name, [string]$IpAddress, [hashtable]$Extra)

        $rule = [pscustomobject]@{
            name       = $Name
            action     = $Action
            priority   = 100
            ip_address = $IpAddress
            headers    = @()
        }
        $siteConfig = [pscustomobject]@{
            scm_use_main_ip_restriction       = $false
            scm_ip_restriction_default_action = "Deny"
            scm_ip_restriction                = @($rule)
        }
        $app = [ordered]@{
            name        = "queenzone-prod"
            site_config = @($siteConfig)
        }
        foreach ($key in $Extra.Keys) {
            $app[$key] = $Extra[$key]
        }

        return [pscustomobject]$app
    }

    return [pscustomobject]@{
        address = $Address
        change  = [pscustomobject]@{
            actions = $Actions
            before  = (New-App -Action $BeforeAction -Name $BeforeName -IpAddress $BeforeIp -Extra $BeforeExtra)
            after   = (New-App -Action $AfterAction -Name $AfterName -IpAddress $AfterIp -Extra $AfterExtra)
        }
    }
}

if ($PSCmdlet.ParameterSetName -eq "SelfTest") {
    $failures = [System.Collections.Generic.List[string]]::new()

    if (-not (Test-IsExpectedTempVeronicaActionToggle -ResourceChanges @((New-WebAppChange)))) {
        $failures.Add("Expected an Allow-to-Deny temp-veronica action toggle to be recognised.")
    }

    if (Test-IsExpectedTempVeronicaActionToggle -ResourceChanges @((New-WebAppChange -BeforeAction "Deny" -AfterAction "Allow"))) {
        $failures.Add("Expected a Deny-to-Allow temp-veronica action toggle not to be treated as expected.")
    }

    $noOp = New-WebAppChange
    $noOp.change.actions = @("no-op")
    if (Test-IsExpectedTempVeronicaActionToggle -ResourceChanges @($noOp)) {
        $failures.Add("Expected a no-op not to be treated as a toggle.")
    }

    if (Test-IsExpectedTempVeronicaActionToggle -ResourceChanges @((New-WebAppChange -Address "module.azure_web.azurerm_linux_web_app.production"))) {
        $failures.Add("Expected a non-production web app address not to be treated as a toggle.")
    }

    if (Test-IsExpectedTempVeronicaActionToggle -ResourceChanges @((New-WebAppChange -AfterName "github-deploy-1-1"))) {
        $failures.Add("Expected a renamed SCM rule not to be treated as a toggle.")
    }

    if (Test-IsExpectedTempVeronicaActionToggle -ResourceChanges @((New-WebAppChange -AfterName "TEMP-VERONICA"))) {
        $failures.Add("Expected a case-only SCM rule name change not to be treated as a toggle.")
    }

    if (Test-IsExpectedTempVeronicaActionToggle -ResourceChanges @((New-WebAppChange -AfterIp "192.0.2.99/32"))) {
        $failures.Add("Expected an IP change not to be treated as a toggle.")
    }

    if (Test-IsExpectedTempVeronicaActionToggle -ResourceChanges @((New-WebAppChange -AfterExtra @{ https_only = $false }))) {
        $failures.Add("Expected an extra web-app attribute change not to be treated as a toggle.")
    }

    if (Test-IsExpectedTempVeronicaActionToggle -ResourceChanges @((New-WebAppChange -BeforeExtra @{ some_setting = "ABC" } -AfterExtra @{ some_setting = "abc" }))) {
        $failures.Add("Expected a case-only extra attribute change not to be treated as a toggle.")
    }

    $second = New-WebAppChange -Address "azurerm_resource_group.production" -BeforeExtra @{ location = "canadaeast" } -AfterExtra @{ location = "australiaeast" }
    if (Test-IsExpectedTempVeronicaActionToggle -ResourceChanges @((New-WebAppChange), $second)) {
        $failures.Add("Expected a second material change not to be treated as a toggle.")
    }

    if ($failures.Count -gt 0) {
        $failures | ForEach-Object { Write-Error $_ }
        exit 1
    }

    Write-Output "Test-OpenTofuExpectedScmToggle self-test passed."
    exit 0
}

if (-not (Test-Path -LiteralPath $PlanJsonPath)) {
    throw "Plan JSON not found at $PlanJsonPath."
}

$plan = Get-Content -LiteralPath $PlanJsonPath -Raw | ConvertFrom-Json
$expected = Test-IsExpectedTempVeronicaActionToggle -ResourceChanges @($plan.resource_changes)
Write-ExpectedToggleOutput -Expected $expected
exit 0
