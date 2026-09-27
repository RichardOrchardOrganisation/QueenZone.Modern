[CmdletBinding()]
param(
    [string]$InfraPath = (Join-Path $PSScriptRoot "../infra")
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$criticalResourceTypes = @(
    "azurerm_resource_group",
    "azurerm_service_plan",
    "azurerm_linux_web_app",
    "azurerm_app_service_certificate",
    "azurerm_app_service_managed_certificate",
    "azurerm_app_service_custom_hostname_binding",
    "azurerm_app_service_certificate_binding",
    "azurerm_mssql_server",
    "azurerm_mssql_database",
    "azurerm_mssql_firewall_rule",
    "azurerm_mssql_server_extended_auditing_policy",
    "azurerm_mssql_database_extended_auditing_policy",
    "azurerm_monitor_diagnostic_setting",
    "azurerm_storage_account",
    "azurerm_storage_container",
    "azurerm_log_analytics_workspace",
    "azurerm_application_insights",
    "azapi_resource",
    "cloudflare_zone",
    "cloudflare_dns_record",
    "cloudflare_workers_script",
    "cloudflare_worker",
    "cloudflare_worker_version",
    "cloudflare_workers_deployment",
    "cloudflare_workers_route"
)

$topLevelBlock = [regex]'(?m)^(terraform|provider|resource|data|module|variable|locals|output|check|import|moved|removed)\b'
$resourceHeader = [regex]'(?m)^resource\s+"(?<type>[^"]+)"\s+"(?<name>[^"]+)"\s*\{'
$failures = [System.Collections.Generic.List[string]]::new()

foreach ($file in Get-ChildItem -LiteralPath $InfraPath -Recurse -Filter "*.tf") {
    $content = Get-Content -LiteralPath $file.FullName -Raw
    if ($content -match '(?im)^\s*(api_token|client_secret|access_key|sas_token)\s*=') {
        $failures.Add("$($file.FullName): credentials must come from the process environment.")
    }

    $blocks = @($topLevelBlock.Matches($content))
    foreach ($resource in $resourceHeader.Matches($content)) {
        $type = $resource.Groups["type"].Value
        if ($type -notin $criticalResourceTypes) {
            continue
        }

        $nextBlock = $blocks | Where-Object { $_.Index -gt $resource.Index } | Select-Object -First 1
        $length = if ($null -eq $nextBlock) { $content.Length - $resource.Index } else { $nextBlock.Index - $resource.Index }
        $resourceBody = $content.Substring($resource.Index, $length)

        if ($resourceBody -notmatch '(?s)lifecycle\s*\{.*?prevent_destroy\s*=\s*true') {
            $failures.Add("$($file.FullName): $type.$($resource.Groups['name'].Value) must set lifecycle.prevent_destroy = true.")
        }

        if ($resourceBody -match '(?s)ignore_changes\s*=\s*all') {
            $failures.Add("$($file.FullName): $type.$($resource.Groups['name'].Value) must not use ignore_changes = all.")
        }
    }
}

if ($failures.Count -gt 0) {
    $failures | ForEach-Object { Write-Error $_ }
    exit 1
}

Write-Output "OpenTofu credential and critical-resource lifecycle checks passed."
