locals {
  ownership_boundary = {
    azure_resource_group = var.azure_resource_group_name
    cloudflare_zone      = var.cloudflare_zone_name
    environment          = var.environment
  }
}

check "production_scale_contract" {
  assert {
    condition     = var.app_service_sku == "B1" && var.app_service_worker_count == 1
    error_message = "Production must remain B1 with one worker unless the accepted hosting decision changes."
  }
}

resource "azurerm_resource_group" "production" {
  name     = var.azure_resource_group_name
  location = var.azure_location

  lifecycle {
    prevent_destroy = true
  }
}

module "azure_data" {
  source = "../../modules/azure-data"

  resource_group_id   = azurerm_resource_group.production.id
  resource_group_name = var.azure_resource_group_name
  location            = var.azure_location

  # The logical server remains because the dev environment uses
  # queenzone-dev-db. Its old production database and Storage sibling are
  # retired separately.
  manage_sql_database    = false
  manage_storage_account = false
}

# Phase 7 (#1272), Canada East production target.
module "azure_web_target" {
  source = "../../modules/azure-web"

  resource_group_name          = azurerm_resource_group.production.name
  location                     = var.production_target_location
  service_plan_name            = "ASP-Queenzone-Prod"
  web_app_name                 = "queenzone-prod"
  log_analytics_workspace_name = "queenzone-prod-law"
  application_insights_name    = "queenzone-prod-ai"
  sku_name                     = var.app_service_sku
  worker_count                 = var.app_service_worker_count
  environment_name             = "production"
  allow_direct_access          = false
  custom_hostnames = {
    "queenzone.org"     = "16D5EE38A8ECAB979AE6960C2CEAABCAF5B7FC14"
    "www.queenzone.org" = "16D5EE38A8ECAB979AE6960C2CEAABCAF5B7FC14"
  }
}

module "azure_data_target" {
  source = "../../modules/azure-data"

  resource_group_id                          = azurerm_resource_group.production.id
  resource_group_name                        = azurerm_resource_group.production.name
  location                                   = var.production_target_location
  sql_server_name                            = "queenzone-prod-sql"
  sql_database_name                          = "queenzone-db"
  storage_account_name                       = "queenzoneprod"
  storage_custom_domain_name                 = "cdn.queenzone.org"
  blob_service_is_preexisting                = false
  create_sql_server_with_write_only_password = true
  sql_server_administrator_password_wo       = var.target_sql_admin_password
  # The copied database was admitted only after exact source/target row-count
  # verification. Its declarative imports are in imports.tf.
  manage_sql_database = true
}

# azure-data can also attach a database to an existing logical server for the
# dev root. Preserve these imported production resource addresses as indexed
# optional resources without proposing a replacement.
moved {
  from = module.azure_data.azapi_resource.sql_server
  to   = module.azure_data.azapi_resource.sql_server[0]
}

moved {
  from = module.azure_data.azurerm_mssql_firewall_rule.azure_services
  to   = module.azure_data.azurerm_mssql_firewall_rule.azure_services[0]
}

moved {
  from = module.azure_data.azurerm_mssql_server_extended_auditing_policy.production
  to   = module.azure_data.azurerm_mssql_server_extended_auditing_policy.production[0]
}

moved {
  from = module.azure_data_target.azapi_resource.storage_account
  to   = module.azure_data_target.azapi_resource.storage_account[0]
}

module "azure_mobile_builds" {
  source = "../../modules/azure-mobile-builds"

  resource_group_id = azurerm_resource_group.production.id
  location          = azurerm_resource_group.production.location
}

module "cloudflare_edge" {
  source = "../../modules/cloudflare-edge"

  account_id = var.cloudflare_account_id
  zone_id    = var.cloudflare_zone_id
  zone_name  = var.cloudflare_zone_name
}

# All modules use declarative imports in imports.tf; an apply must never
# precede plan review.
