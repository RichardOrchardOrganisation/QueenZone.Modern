mock_provider "azurerm" {
  mock_resource "azurerm_app_service_custom_hostname_binding" {
    defaults = {
      id = "/subscriptions/00000000-0000-0000-0000-000000000000/resourceGroups/test/providers/Microsoft.Web/sites/test/hostNameBindings/dev.queenzone.org"
    }
  }
  mock_resource "azurerm_app_service_managed_certificate" {
    defaults = {
      id = "/subscriptions/00000000-0000-0000-0000-000000000000/resourceGroups/test/providers/Microsoft.Web/certificates/test"
    }
  }
  mock_resource "azurerm_log_analytics_workspace" {
    defaults = {
      id = "/subscriptions/00000000-0000-0000-0000-000000000000/resourceGroups/test/providers/Microsoft.OperationalInsights/workspaces/test"
    }
  }
  mock_resource "azurerm_service_plan" {
    defaults = {
      id = "/subscriptions/00000000-0000-0000-0000-000000000000/resourceGroups/test/providers/Microsoft.Web/serverFarms/test"
    }
  }
}

run "production_defaults" {
  command = plan
  assert {
    condition     = azurerm_linux_web_app.production.site_config[0].ip_restriction_default_action == "Deny" && azurerm_linux_web_app.production.site_config[0].scm_ip_restriction_default_action == "Allow" && !azurerm_linux_web_app.production.site_config[0].scm_use_main_ip_restriction && length(azurerm_app_service_custom_hostname_binding.production) == 2 && length(azurerm_app_service_managed_certificate.managed) == 0
    error_message = "Production must deny direct ingress, keep SCM Allow for GitHub-hosted deploy, and keep both uploaded TLS bindings."
  }
  assert {
    condition     = azurerm_service_plan.production.sku_name == "B1" && azurerm_service_plan.production.worker_count == 1 && azurerm_linux_web_app.production.site_config[0].always_on
    error_message = "Always-on single-worker B1 must be preserved."
  }
  assert {
    condition = (
      length(azurerm_monitor_action_group.alerts) == 1 &&
      azurerm_monitor_action_group.alerts[0].name == "queenzone-alerts" &&
      length(azurerm_monitor_action_group.alerts[0].webhook_receiver) == 0 &&
      length(azurerm_monitor_scheduled_query_rules_alert_v2.production) == 6 &&
      azurerm_monitor_scheduled_query_rules_alert_v2.production["qz-prod-server-5xx"].severity == 2 &&
      azurerm_monitor_scheduled_query_rules_alert_v2.production["qz-prod-server-5xx"].auto_mitigation_enabled &&
      azurerm_monitor_scheduled_query_rules_alert_v2.production["qz-prod-exception-new-problem"].query_time_range_override == "P2D" &&
      azurerm_monitor_scheduled_query_rules_alert_v2.production["qz-prod-exception-new-problem"].window_duration == "PT1H" &&
      azurerm_monitor_scheduled_query_rules_alert_v2.production["qz-prod-request-p95"].enabled == false &&
      azurerm_application_insights_standard_web_test.health[0].request[0].url == "https://www.queenzone.org/health" &&
      azurerm_application_insights_standard_web_test.health[0].frequency == 900 &&
      azurerm_application_insights_standard_web_test.health[0].validation_rules[0].expected_status_code == 200 &&
      azurerm_application_insights_standard_web_test.health[0].validation_rules[0].ssl_check_enabled &&
      azurerm_application_insights_standard_web_test.health[0].validation_rules[0].ssl_cert_remaining_lifetime == 14 &&
      length(azurerm_application_insights_standard_web_test.health[0].geo_locations) == 3 &&
      azurerm_monitor_metric_alert.availability[0].severity == 1 &&
      azurerm_monitor_metric_alert.availability[0].auto_mitigate &&
      azurerm_monitor_metric_alert.availability[0].application_insights_web_test_location_availability_criteria[0].failed_location_count == 2
    )
    error_message = "Production must import queenzone-alerts with no webhook and declare the six qz-prod-* log rules, the health web test, and the two-location availability alert."
  }
}

run "dev_bootstrap" {
  command = plan
  variables {
    environment_name    = "dev"
    custom_hostnames    = {}
    allow_direct_access = true
  }
  assert {
    condition     = azurerm_linux_web_app.production.site_config[0].ip_restriction_default_action == "Allow" && azurerm_linux_web_app.production.site_config[0].scm_ip_restriction_default_action == "Allow" && !azurerm_linux_web_app.production.site_config[0].scm_use_main_ip_restriction && length(azurerm_linux_web_app.production.site_config[0].ip_restriction) == 0 && length(azurerm_app_service_custom_hostname_binding.production) == 0 && length(azurerm_app_service_custom_hostname_binding.managed) == 0
    error_message = "Dev must be reachable without Cloudflare and create no premature bindings."
  }
  assert {
    condition     = azurerm_linux_web_app.production.app_settings["WEBSITE_WARMUP_PATH"] == "/health"
    error_message = "Fresh dev apps need a warmup setting."
  }
  assert {
    condition     = length(azurerm_monitor_action_group.alerts) == 0 && length(azurerm_monitor_scheduled_query_rules_alert_v2.production) == 0 && length(azurerm_application_insights_standard_web_test.health) == 0 && length(azurerm_monitor_metric_alert.availability) == 0
    error_message = "Dev must not create production App Insights alert rules or import queenzone-alerts."
  }
}

run "production_rejects_direct_access" {
  command = plan
  variables {
    allow_direct_access = true
  }
  expect_failures = [var.allow_direct_access]
}

run "production_retains_hostnames" {
  command = plan
  variables {
    custom_hostnames = {}
  }
  expect_failures = [var.custom_hostnames]
}

run "production_rejects_managed_dev_hostname" {
  command = plan
  variables {
    managed_hostnames = ["dev.queenzone.org"]
  }
  expect_failures = [var.managed_hostnames]
}

run "dev_rejects_production_hostname" {
  command = plan
  variables {
    environment_name = "dev"
    custom_hostnames = { "www.queenzone.org" = "thumbprint" }
  }
  expect_failures = [var.custom_hostnames]
}

run "migration_candidate_allows_direct_ingress_without_custom_hostnames" {
  command = plan

  variables {
    environment_name    = "migration"
    allow_direct_access = true
    custom_hostnames    = {}
  }

  assert {
    condition     = azurerm_linux_web_app.production.site_config[0].ip_restriction_default_action == "Allow" && azurerm_linux_web_app.production.site_config[0].scm_ip_restriction_default_action == "Allow" && length(azurerm_linux_web_app.production.site_config[0].ip_restriction) == 0 && length(azurerm_app_service_custom_hostname_binding.production) == 0
    error_message = "The migration candidate must be directly testable before DNS cutover and must not claim production hostnames."
  }
}

run "production_cloudflare_rules_are_single_cidrs" {
  command = plan

  assert {
    condition = (
      length(azurerm_linux_web_app.production.site_config[0].ip_restriction) == 22 &&
      length(distinct([for rule in azurerm_linux_web_app.production.site_config[0].ip_restriction : rule.priority])) == 22 &&
      alltrue([
        for rule in azurerm_linux_web_app.production.site_config[0].ip_restriction :
        strcontains(rule.ip_address, "/") && !strcontains(rule.ip_address, ",")
      ]) &&
      contains([for rule in azurerm_linux_web_app.production.site_config[0].ip_restriction : rule.ip_address], "173.245.48.0/20") &&
      contains([for rule in azurerm_linux_web_app.production.site_config[0].ip_restriction : rule.ip_address], "2c0f:f248::/32") &&
      azurerm_linux_web_app.production.site_config[0].scm_ip_restriction_default_action == "Allow" &&
      !azurerm_linux_web_app.production.site_config[0].scm_use_main_ip_restriction &&
      length(azurerm_linux_web_app.production.site_config[0].scm_ip_restriction) == 0 &&
      azurerm_linux_web_app.production.ftp_publish_basic_authentication_enabled &&
      azurerm_linux_web_app.production.webdeploy_publish_basic_authentication_enabled
    )
    error_message = "Production must allow one Cloudflare CIDR per main-site rule, keep SCM Allow without that list, and keep publish-profile basic auth enabled."
  }
}

run "dev_managed_tls_after_dns" {
  command = plan
  variables {
    environment_name    = "dev"
    custom_hostnames    = {}
    managed_hostnames   = ["dev.queenzone.org"]
    allow_direct_access = true
  }
  assert {
    condition     = length(azurerm_app_service_managed_certificate.managed) == 1 && azurerm_app_service_certificate_binding.managed["dev.queenzone.org"].ssl_state == "SniEnabled" && azurerm_linux_web_app.production.site_config[0].ip_restriction_default_action == "Allow"
    error_message = "The later DNS phase must enable managed SNI TLS while preserving direct dev ingress for certificate renewal."
  }
}
