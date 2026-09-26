locals {
  manage_production_alerts = var.environment_name == "production"

  # Bots generate up to 27 client-error requests per 5 minutes. Keep 4xx
  # out of every request and dependency query; 5xx and empty ResultCode stay.
  exclude_client_errors = "(toint(ResultCode) < 400 or toint(ResultCode) >= 500)"

  production_log_alerts = local.manage_production_alerts ? {
    "qz-prod-server-5xx" = {
      description               = "Weighted 5xx request count of 5 or more in 15 minutes, split by operation Name."
      severity                  = 2
      enabled                   = true
      evaluation_frequency      = "PT5M"
      window_duration           = "PT15M"
      query_time_range_override = null
      operator                  = "GreaterThanOrEqual"
      threshold                 = 5
      time_aggregation_method   = "Total"
      metric_measure_column     = "AggregatedValue"
      dimensions = [{
        name     = "Name"
        operator = "Include"
        values   = ["*"]
      }]
      query = <<-KQL
        AppRequests
        | where toint(ResultCode) >= 500
        | summarize AggregatedValue = sum(ItemCount) by Name
      KQL
    }
    "qz-prod-exception-new-problem" = {
      description               = "A ProblemId seen in the last hour that was not seen in the prior 47 hours."
      severity                  = 3
      enabled                   = true
      evaluation_frequency      = "PT15M"
      window_duration           = "PT1H"
      query_time_range_override = "P2D"
      operator                  = "GreaterThan"
      threshold                 = 0
      time_aggregation_method   = "Total"
      metric_measure_column     = "AggregatedValue"
      dimensions = [{
        name     = "ProblemId"
        operator = "Include"
        values   = ["*"]
      }]
      query = <<-KQL
        AppExceptions
        | where isnotempty(ProblemId)
        | summarize
            recentCount = sumif(coalesce(ItemCount, 1), TimeGenerated >= ago(1h)),
            priorCount = sumif(coalesce(ItemCount, 1), TimeGenerated < ago(1h))
            by ProblemId
        | where recentCount > 0 and priorCount == 0
        | project ProblemId, AggregatedValue = recentCount
      KQL
    }
    "qz-prod-exception-spike" = {
      description               = "20 or more exceptions in 15 minutes."
      severity                  = 2
      enabled                   = true
      evaluation_frequency      = "PT5M"
      window_duration           = "PT15M"
      query_time_range_override = null
      operator                  = "GreaterThanOrEqual"
      threshold                 = 20
      time_aggregation_method   = "Total"
      metric_measure_column     = "AggregatedValue"
      dimensions                = []
      query                     = <<-KQL
        AppExceptions
        | summarize AggregatedValue = sum(coalesce(ItemCount, 1))
      KQL
    }
    "qz-prod-dependency-failures" = {
      description               = "25 or more failed dependencies in 15 minutes, split by DependencyType, excluding HTTP 4xx."
      severity                  = 2
      enabled                   = true
      evaluation_frequency      = "PT5M"
      window_duration           = "PT15M"
      query_time_range_override = null
      operator                  = "GreaterThanOrEqual"
      threshold                 = 25
      time_aggregation_method   = "Total"
      metric_measure_column     = "AggregatedValue"
      dimensions = [{
        name     = "DependencyType"
        operator = "Include"
        values   = ["*"]
      }]
      query = <<-KQL
        AppDependencies
        | where Success == false
        | where ${local.exclude_client_errors} or isempty(ResultCode)
        | summarize AggregatedValue = sum(coalesce(ItemCount, 1)) by DependencyType
      KQL
    }
    "qz-prod-ingestion-cap" = {
      description               = "Log Analytics daily cap reached or data collection stopped."
      severity                  = 2
      enabled                   = true
      evaluation_frequency      = "PT15M"
      window_duration           = "PT1H"
      query_time_range_override = null
      operator                  = "GreaterThanOrEqual"
      threshold                 = 1
      time_aggregation_method   = "Total"
      metric_measure_column     = "AggregatedValue"
      dimensions                = []
      query                     = <<-KQL
        _LogOperation
        | where Category =~ "Ingestion"
        | where Detail has_any ("OverQuota", "Daily cap", "data collection is stopped", "Data collection is stopped")
          or Operation has_any ("OverQuota", "IngestionOverQuota", "DataCollectionStopped")
        | summarize AggregatedValue = count()
      KQL
    }
    "qz-prod-request-p95" = {
      description               = "Request duration p95 above 8000 ms, excluding /health and 4xx. Disabled until known-slow archive pages are fixed."
      severity                  = 3
      enabled                   = false
      evaluation_frequency      = "PT15M"
      window_duration           = "PT30M"
      query_time_range_override = null
      operator                  = "GreaterThan"
      threshold                 = 8000
      time_aggregation_method   = "Maximum"
      metric_measure_column     = "AggregatedValue"
      dimensions                = []
      query                     = <<-KQL
        AppRequests
        | where Name !has "/health" and Url !has "/health"
        | where ${local.exclude_client_errors}
        | summarize AggregatedValue = percentile(DurationMs, 95)
      KQL
    }
  } : {}
}

# Live group already has an email receiver. The address is not recorded in
# this repository. Import the existing object and ignore receiver/location
# drift so the plan cannot replace the group or add a webhook.
resource "azurerm_monitor_action_group" "alerts" {
  count = local.manage_production_alerts ? 1 : 0

  name                = "queenzone-alerts"
  resource_group_name = var.resource_group_name
  short_name          = "qz-alerts"
  location            = "global"
  enabled             = true

  lifecycle {
    prevent_destroy = true
    ignore_changes = [
      arm_role_receiver,
      automation_runbook_receiver,
      azure_app_push_receiver,
      azure_function_receiver,
      email_receiver,
      event_hub_receiver,
      itsm_receiver,
      location,
      logic_app_receiver,
      short_name,
      sms_receiver,
      tags,
      voice_receiver,
      webhook_receiver,
    ]
  }
}

resource "azurerm_monitor_scheduled_query_rules_alert_v2" "production" {
  for_each = local.production_log_alerts

  name                 = each.key
  display_name         = each.key
  resource_group_name  = var.resource_group_name
  location             = var.location
  scopes               = [azurerm_log_analytics_workspace.production.id]
  severity             = each.value.severity
  enabled              = each.value.enabled
  description          = each.value.description
  evaluation_frequency = each.value.evaluation_frequency
  window_duration      = each.value.window_duration
  # AzureRM 5.0.1 (Microsoft.Insights 2023-03-15-preview) treats
  # auto_mitigation_enabled and mute_actions_after_alert_duration as
  # mutually exclusive. Keep auto-resolve; do not set a 60-minute mute.
  auto_mitigation_enabled   = true
  skip_query_validation     = false
  query_time_range_override = each.value.query_time_range_override

  criteria {
    query                   = each.value.query
    operator                = each.value.operator
    threshold               = each.value.threshold
    time_aggregation_method = each.value.time_aggregation_method
    metric_measure_column   = each.value.metric_measure_column

    dynamic "dimension" {
      for_each = each.value.dimensions
      content {
        name     = dimension.value.name
        operator = dimension.value.operator
        values   = dimension.value.values
      }
    }

    failing_periods {
      minimum_failing_periods_to_trigger_alert = 1
      number_of_evaluation_periods             = 1
    }
  }

  action {
    action_groups = [azurerm_monitor_action_group.alerts[0].id]
  }
}

resource "azurerm_application_insights_standard_web_test" "health" {
  count = local.manage_production_alerts ? 1 : 0

  name                    = "qz-prod-health"
  resource_group_name     = var.resource_group_name
  location                = var.location
  application_insights_id = azurerm_application_insights.production.id
  description             = "GET https://www.queenzone.org/health from three locations every 900 seconds."
  enabled                 = true
  frequency               = 900
  timeout                 = 30
  retry_enabled           = true
  geo_locations = [
    "us-va-ash-azr",
    "emea-nl-ams-azr",
    "apac-sg-sin-azr",
  ]

  request {
    url                              = "https://www.queenzone.org/health"
    http_verb                        = "GET"
    follow_redirects_enabled         = false
    parse_dependent_requests_enabled = false
  }

  validation_rules {
    expected_status_code        = 200
    ssl_check_enabled           = true
    ssl_cert_remaining_lifetime = 14
  }
}

resource "azurerm_monitor_metric_alert" "availability" {
  count = local.manage_production_alerts ? 1 : 0

  name                = "qz-prod-availability"
  resource_group_name = var.resource_group_name
  scopes              = [azurerm_application_insights.production.id]
  description         = "Standard web test qz-prod-health failed from 2 or more locations."
  severity            = 1
  enabled             = true
  auto_mitigate       = true
  frequency           = "PT5M"
  window_size         = "PT15M"

  application_insights_web_test_location_availability_criteria {
    web_test_id           = azurerm_application_insights_standard_web_test.health[0].id
    component_id          = azurerm_application_insights.production.id
    failed_location_count = 2
  }

  action {
    action_group_id = azurerm_monitor_action_group.alerts[0].id
  }
}
