# Fresh resources, separate state. No imports or production resources here.
resource "azurerm_resource_group" "dev" {
  name     = "Queenzone-Dev-RG"
  location = "australiaeast"
  lifecycle {
    prevent_destroy = true
  }
}

module "azure_web" {
  source                       = "../../modules/azure-web"
  environment_name             = "dev"
  resource_group_name          = azurerm_resource_group.dev.name
  location                     = azurerm_resource_group.dev.location
  service_plan_name            = "ASP-Queenzone-Dev"
  web_app_name                 = "queenzone-devbox"
  log_analytics_workspace_name = "queenzone-devbox-law"
  application_insights_name    = "queenzone-devbox-ai"
  sku_name                     = "B1"
  worker_count                 = 1
  custom_hostnames             = {}
  managed_hostnames            = var.enable_custom_domain ? ["dev.queenzone.org"] : []
  # Azure-managed certificates require a direct CNAME for issuance and
  # renewal. dev.queenzone.org therefore stays DNS-only and this dev-only app
  # remains directly reachable; production retains Cloudflare-only ingress.
  allow_direct_access = true
}

# The logical server is an existing production-owned resource in Queenzone-RG.
# Only this empty Basic database is managed in dev state; no server firewall or
# role assignment is added here, so this root cannot grant access to prod data.
module "azure_data" {
  source = "../../modules/azure-data"

  resource_group_id                      = azurerm_resource_group.dev.id
  resource_group_name                    = azurerm_resource_group.dev.name
  location                               = azurerm_resource_group.dev.location
  existing_sql_server_id                 = "/subscriptions/${var.azure_subscription_id}/resourceGroups/Queenzone-RG/providers/Microsoft.Sql/servers/queenzone-sql-server"
  create_azure_services_firewall_rule    = false
  create_server_extended_auditing_policy = false
  sql_database_name                      = "queenzone-dev-db"
  sql_database_sku_name                  = "Basic"
  sql_database_max_size_gb               = 2
  storage_account_name                   = "queenzonedev"
  storage_custom_domain_name             = null
  manage_blob_service                    = false
  # Keep dev's existing 26-container inventory stable. The three additional
  # live source containers are production migration scope only.
  containers = {
    "album-or-single-covers"  = "Blob"
    "attachments"             = "Blob"
    "avatars"                 = "Blob"
    "brian-may"               = "Blob"
    "css"                     = "Container"
    "databasebackup"          = "None"
    "fan-art"                 = "Blob"
    "fan-pics"                = "Blob"
    "forum"                   = "Blob"
    "freddie-mercury"         = "Blob"
    "freddie-tribute-concert" = "Blob"
    "images"                  = "Blob"
    "john-deacon"             = "Blob"
    "miscellaneous"           = "Blob"
    "mp3"                     = "Blob"
    "pre-queen"               = "Blob"
    "queen"                   = "Blob"
    "queen-and-adam-lambert"  = "Blob"
    "queen-and-paul-rodgers"  = "Blob"
    "queen-memorabillia"      = "Blob"
    "roger-taylor"            = "Blob"
    "songfiles"               = "None"
    "special-events"          = "Blob"
    "ugc-avatars"             = "None"
    "ugc-forum"               = "None"
    "us-convention-2001"      = "Blob"
  }
}

moved {
  from = module.azure_data.azurerm_mssql_database.production
  to   = module.azure_data.azurerm_mssql_database.production[0]
}

moved {
  from = module.azure_data.azurerm_mssql_database_extended_auditing_policy.production
  to   = module.azure_data.azurerm_mssql_database_extended_auditing_policy.production[0]
}

moved {
  from = module.azure_data.azapi_resource.storage_account
  to   = module.azure_data.azapi_resource.storage_account[0]
}
