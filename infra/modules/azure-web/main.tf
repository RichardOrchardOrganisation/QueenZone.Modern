locals {
  # Refreshed 2026-09-22 from https://www.cloudflare.com/ips-v4 and
  # https://www.cloudflare.com/ips-v6. App Service ip_restriction.ip_address
  # is one CIDR (or one service tag) per rule. Azure publishes no Cloudflare
  # service tag; AzureFrontDoor.Backend is a different network and is not used.
  cloudflare_ipv4_prefixes = [
    "173.245.48.0/20",
    "103.21.244.0/22",
    "103.22.200.0/22",
    "103.31.4.0/22",
    "141.101.64.0/18",
    "108.162.192.0/18",
    "190.93.240.0/20",
    "188.114.96.0/20",
    "197.234.240.0/22",
    "198.41.128.0/17",
    "162.158.0.0/15",
    "104.16.0.0/13",
    "104.24.0.0/14",
    "172.64.0.0/13",
    "131.0.72.0/22",
  ]

  cloudflare_ipv6_prefixes = [
    "2400:cb00::/32",
    "2606:4700::/32",
    "2803:f800::/32",
    "2405:b500::/32",
    "2405:8100::/32",
    "2a06:98c0::/29",
    "2c0f:f248::/32",
  ]

  cloudflare_ipv4_rules = [for index, prefix in local.cloudflare_ipv4_prefixes : {
    name        = "Cloudflare-IPv4-${index + 1}"
    priority    = 100 + index
    ip_address  = prefix
    description = "Allow Cloudflare published IPv4 prefix"
  }]

  cloudflare_ipv6_rules = [for index, prefix in local.cloudflare_ipv6_prefixes : {
    name        = "Cloudflare-IPv6-${index + 1}"
    priority    = 200 + index
    ip_address  = prefix
    description = "Allow Cloudflare published IPv6 prefix"
  }]

  # Dev and migration set allow_direct_access and must stay reachable without
  # Cloudflare, so they get no allow-list rules and a default of Allow.
  cloudflare_ip_restrictions = [
    for rule in concat(local.cloudflare_ipv4_rules, local.cloudflare_ipv6_rules) : rule
    if !var.allow_direct_access
  ]
}

resource "azurerm_log_analytics_workspace" "production" {
  name                = var.log_analytics_workspace_name
  location            = var.location
  resource_group_name = var.resource_group_name
  sku                 = "PerGB2018"
  retention_in_days   = 30
  daily_quota_gb      = 0.1

  allow_resource_only_permissions = true
  internet_ingestion_access_type  = "Enabled"
  internet_query_access_type      = "Enabled"

  lifecycle {
    prevent_destroy = true

    # Azure reports this legacy workspace flag as null while the provider
    # normalises an omitted value to true. Preserve the imported ARM shape.
    ignore_changes = [local_authentication_enabled]
  }
}

resource "azurerm_application_insights" "production" {
  name                = var.application_insights_name
  location            = var.location
  resource_group_name = var.resource_group_name
  workspace_id        = azurerm_log_analytics_workspace.production.id
  application_type    = "web"
  retention_in_days   = 90

  internet_ingestion_enabled = true
  internet_query_enabled     = true
  sampling_percentage        = 0

  lifecycle {
    prevent_destroy = true
  }
}

resource "azurerm_service_plan" "production" {
  name                = var.service_plan_name
  location            = var.location
  resource_group_name = var.resource_group_name
  os_type             = "Linux"
  sku_name            = var.sku_name
  worker_count        = var.worker_count

  per_site_scaling_enabled = false
  zone_balancing_enabled   = false

  lifecycle {
    prevent_destroy = true
  }
}

resource "azurerm_linux_web_app" "production" {
  name                = var.web_app_name
  location            = var.location
  resource_group_name = var.resource_group_name
  service_plan_id     = azurerm_service_plan.production.id

  enabled                    = true
  https_only                 = true
  client_affinity_enabled    = false
  client_certificate_enabled = false

  # deploy.yml and scripts/Invoke-AppServiceKudu.py still authenticate to SCM
  # with the publish profile (Basic auth), not GitHub OIDC or a managed
  # identity. Leave both publishing credentials enabled until that path moves.
  ftp_publish_basic_authentication_enabled       = true
  webdeploy_publish_basic_authentication_enabled = true

  # sensitive({}) rather than omitting the argument: ignore_changes alone
  # does not stop OpenTofu's plan renderer from printing the full live
  # app_settings map (every key/value in plaintext) as unchanged context
  # whenever any OTHER attribute on this resource changes — which happens
  # on every import. Confirmed the hard way: real secrets (OAuth client
  # secrets, signing keys, private keys) were printed in full in GitHub
  # Actions logs on 2026-09-03 across every real plan/apply run before this
  # fix. Marking the configured value sensitive redacts it as
  # "(sensitive value)" in all cases; ignore_changes still keeps OpenTofu
  # from ever touching the live map (ADR 0008).
  app_settings = sensitive(var.environment_name == "dev" ? {
    WEBSITE_WARMUP_PATH = "/health"
  } : {})

  tags = {
    "hidden-link: /app-insights-resource-id" = replace(
      azurerm_application_insights.production.id,
      "Microsoft.Insights",
      "microsoft.insights",
    )
  }

  identity {
    type = "SystemAssigned"
  }

  site_config {
    always_on                         = true
    ftps_state                        = "FtpsOnly"
    health_check_path                 = "/health"
    health_check_eviction_time_in_min = 10
    http2_enabled                     = true
    minimum_tls_version               = "1.2"
    remote_debugging_enabled          = false
    scm_minimum_tls_version           = "1.2"
    # SCM stays Allow. deploy.yml (azure/webapps-deploy) and
    # scripts/Invoke-AppServiceKudu.py reach *.scm.azurewebsites.net from
    # GitHub-hosted runners, which are not Cloudflare addresses. Copying the
    # main-site Cloudflare allow list onto SCM would 403 production deploys.
    # SCM Deny waits for an explicit deploy/operator allow list, or for deploy
    # to leave public SCM. Do not flip this with the main-site rules.
    scm_use_main_ip_restriction       = false
    scm_ip_restriction_default_action = "Allow"
    ip_restriction_default_action     = var.allow_direct_access ? "Allow" : "Deny"
    use_32_bit_worker                 = true
    websockets_enabled                = false
    worker_count                      = var.worker_count

    application_stack {
      dotnet_version = "10.0"
    }

    dynamic "ip_restriction" {
      for_each = local.cloudflare_ip_restrictions

      content {
        name        = ip_restriction.value.name
        priority    = ip_restriction.value.priority
        action      = "Allow"
        ip_address  = ip_restriction.value.ip_address
        description = ip_restriction.value.description
      }
    }
  }

  logs {
    detailed_error_messages = false
    failed_request_tracing  = false

    application_logs {
      file_system_level = "Information"
    }

    http_logs {
      file_system {
        retention_in_days = 3
        retention_in_mb   = 100
      }
    }
  }

  lifecycle {
    prevent_destroy = true

    # ADR 0008 keeps the split ARM/operator settings map outside OpenTofu.
    # Reading this map into state would expose values; managing an incomplete
    # map would delete live settings.
    ignore_changes = [
      app_settings,
    ]
  }
}

resource "azurerm_app_service_custom_hostname_binding" "production" {
  for_each = var.custom_hostnames

  hostname            = each.key
  app_service_name    = azurerm_linux_web_app.production.name
  resource_group_name = var.resource_group_name
  ssl_state           = "SniEnabled"
  thumbprint          = each.value

  lifecycle {
    prevent_destroy = true
  }
}

# Separate addresses preserve production's imported uploaded-certificate bindings.
resource "azurerm_app_service_custom_hostname_binding" "managed" {
  for_each            = var.managed_hostnames
  hostname            = each.value
  app_service_name    = azurerm_linux_web_app.production.name
  resource_group_name = var.resource_group_name
  lifecycle {
    prevent_destroy = true
    ignore_changes  = [ssl_state, thumbprint]
  }
}

resource "azurerm_app_service_managed_certificate" "managed" {
  for_each                   = var.managed_hostnames
  custom_hostname_binding_id = azurerm_app_service_custom_hostname_binding.managed[each.key].id
  lifecycle {
    prevent_destroy = true
  }
}

resource "azurerm_app_service_certificate_binding" "managed" {
  for_each            = var.managed_hostnames
  hostname_binding_id = azurerm_app_service_custom_hostname_binding.managed[each.key].id
  certificate_id      = azurerm_app_service_managed_certificate.managed[each.key].id
  ssl_state           = "SniEnabled"
  lifecycle {
    prevent_destroy = true
  }
}
