output "import_contract" {
  description = "Non-sensitive names that later Azure web imports must match."
  value = {
    resource_group = var.resource_group_name
    service_plan   = var.service_plan_name
    web_app        = var.web_app_name
    log_analytics  = var.log_analytics_workspace_name
    app_insights   = var.application_insights_name
    hostnames      = sort(keys(var.custom_hostnames))
  }
}

output "log_analytics_workspace_id" {
  description = "Log Analytics workspace that receives site telemetry and SQL audit events."
  value       = azurerm_log_analytics_workspace.production.id
}

output "action_group_id" {
  description = "Imported production action group that receives qz-prod-* alerts. Empty in non-production environments."
  value       = try(azurerm_monitor_action_group.alerts[0].id, null)
}

output "possible_outbound_ip_addresses" {
  description = "Every outbound IP address the App Service stamp may assign to this site."
  value       = azurerm_linux_web_app.production.possible_outbound_ip_address_list
}

output "web_app_id" {
  description = "Managed production web app resource ID."
  value       = azurerm_linux_web_app.production.id
}

output "managed_identity_principal_id" {
  description = "System-assigned identity principal ID for separately scoped RBAC."
  value       = azurerm_linux_web_app.production.identity[0].principal_id
}

output "default_hostname" {
  description = "Azure-provided HTTPS hostname."
  value       = azurerm_linux_web_app.production.default_hostname
}

output "custom_domain_verification_id" {
  sensitive   = true
  description = "Public DNS ownership verification value for the later domain cutover."
  value       = azurerm_linux_web_app.production.custom_domain_verification_id
}
