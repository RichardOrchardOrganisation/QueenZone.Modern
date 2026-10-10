mock_provider "azapi" {}
mock_provider "azurerm" {
  mock_resource "azurerm_mssql_server" {
    defaults = {
      id = "/subscriptions/00000000-0000-0000-0000-000000000000/resourceGroups/test/providers/Microsoft.Sql/servers/queenzone-prod-sql"
    }
  }
}

variables {
  resource_group_id                    = "/subscriptions/00000000-0000-0000-0000-000000000000/resourceGroups/test"
  resource_group_name                  = "test"
  sql_server_administrator_password_wo = "test-only-password"
}

run "existing_production_shape_remains_managed" {
  command = plan

  override_resource {
    target = azapi_resource.sql_server
    values = {
      id = "/subscriptions/00000000-0000-0000-0000-000000000000/resourceGroups/test/providers/Microsoft.Sql/servers/test"
    }
  }

  override_resource {
    target = azapi_resource.storage_account
    values = {
      id = "/subscriptions/00000000-0000-0000-0000-000000000000/resourceGroups/test/providers/Microsoft.Storage/storageAccounts/test"
    }
  }

  override_resource {
    target = azapi_resource.blob_service
    values = {
      id = "/subscriptions/00000000-0000-0000-0000-000000000000/resourceGroups/test/providers/Microsoft.Storage/storageAccounts/test/blobServices/default"
    }
  }

  override_resource {
    target = azurerm_mssql_database.production
    values = {
      id = "/subscriptions/00000000-0000-0000-0000-000000000000/resourceGroups/test/providers/Microsoft.Sql/servers/test/databases/queenzone-db"
    }
  }

  assert {
    condition     = length(azapi_resource.sql_server) == 1 && length(azurerm_mssql_server.created) == 0 && length(azurerm_mssql_database.production) == 1
    error_message = "The default imported production shape must retain its AzAPI server and managed database."
  }

  assert {
    condition     = length(azurerm_mssql_firewall_rule.azure_services) == 1 && azurerm_mssql_server_extended_auditing_policy.production[0].enabled == false
    error_message = "The imported server shape must keep its Azure-services firewall rule and leave auditing off until a caller opts in."
  }

  assert {
    condition     = length(var.containers) == 29 && var.containers["test"] == "None"
    error_message = "The complete 29-container source inventory must remain managed, and the leftover test container must stay private."
  }

  assert {
    condition     = var.containers["ugc-articles"] == "None" && var.containers["ugc-photos"] == "None"
    error_message = "Article and photo UGC containers must remain private."
  }

  assert {
    condition     = var.containers["attachments"] == "None" && var.containers["songfiles"] == "None" && var.containers["freddie-mercury"] == "Blob" && var.containers["album-or-single-covers"] == "Blob" && var.containers["css"] == "Blob"
    error_message = "Legacy attachments and songfiles must stay private. Published gallery containers stay public blob, and css must not allow anonymous listing."
  }

  assert {
    condition     = var.containers["forum"] == "Blob" && var.containers["mp3"] == "Blob" && contains(var.public_blob_containers, "forum") && contains(var.public_blob_containers, "mp3")
    error_message = "forum and mp3 stay public blob on the allow-list until Gilfoyle's anonymous-read metrics land."
  }

  assert {
    condition = alltrue([
      for name, access in var.containers :
      access == "None" || contains(var.public_blob_containers, name)
    ])
    error_message = "Every public container in the default inventory must be named in public_blob_containers."
  }
}

run "migration_target_uses_write_only_password_and_defers_database" {
  command = plan

  override_resource {
    target = azapi_resource.storage_account
    values = {
      id = "/subscriptions/00000000-0000-0000-0000-000000000000/resourceGroups/test/providers/Microsoft.Storage/storageAccounts/queenzoneprod"
    }
  }


  override_resource {
    target = azapi_resource.blob_service
    values = {
      id = "/subscriptions/00000000-0000-0000-0000-000000000000/resourceGroups/test/providers/Microsoft.Storage/storageAccounts/queenzoneprod/blobServices/default"
    }
  }

  variables {
    create_sql_server_with_write_only_password = true
    blob_service_is_preexisting                = false
    manage_sql_database                        = false
    sql_server_name                            = "queenzone-prod-sql"
    storage_account_name                       = "queenzoneprod"
  }

  assert {
    condition     = length(azapi_resource.sql_server) == 0 && length(azurerm_mssql_server.created) == 1 && length(azurerm_mssql_database.production) == 0
    error_message = "The migration target must create its server with the write-only password and defer the database until Azure copy completes."
  }

  assert {
    condition     = length(azapi_resource.blob_service) == 0 && length(azapi_update_resource.blob_service_settings) == 1
    error_message = "A new StorageV2 account must patch its automatically created blob service instead of creating the child again."
  }
}

run "retained_sql_can_exclude_retired_storage" {
  command = plan

  variables {
    existing_sql_server_id                 = "/subscriptions/00000000-0000-0000-0000-000000000000/resourceGroups/test/providers/Microsoft.Sql/servers/test"
    create_azure_services_firewall_rule    = false
    create_server_extended_auditing_policy = false
    manage_sql_database                    = false
    manage_storage_account                 = false
  }

  assert {
    condition = (
      length(azapi_resource.storage_account) == 0 &&
      length(azapi_resource.blob_service) == 0 &&
      length(azapi_update_resource.blob_service_settings) == 0 &&
      length(azapi_resource.container) == 0
    )
    error_message = "A SQL-only module call must not recreate a retired Storage account, blob service, or containers."
  }

  assert {
    condition     = output.storage_account_id == null
    error_message = "A SQL-only module call must expose no managed Storage account ID."
  }
}

run "production_target_locks_down_firewall_and_audits" {
  command = plan

  override_resource {
    target = azurerm_mssql_database.production
    values = {
      id = "/subscriptions/00000000-0000-0000-0000-000000000000/resourceGroups/test/providers/Microsoft.Sql/servers/queenzone-prod-sql/databases/queenzone-db"
    }
  }

  override_resource {
    target = azapi_resource.storage_account
    values = {
      id = "/subscriptions/00000000-0000-0000-0000-000000000000/resourceGroups/test/providers/Microsoft.Storage/storageAccounts/queenzoneprod"
    }
  }

  override_resource {
    target = azapi_resource.blob_service
    values = {
      id = "/subscriptions/00000000-0000-0000-0000-000000000000/resourceGroups/test/providers/Microsoft.Storage/storageAccounts/queenzoneprod/blobServices/default"
    }
  }

  variables {
    create_sql_server_with_write_only_password = true
    create_azure_services_firewall_rule        = false
    blob_service_is_preexisting                = false
    sql_extended_auditing_enabled              = true
    log_analytics_workspace_id                 = "/subscriptions/00000000-0000-0000-0000-000000000000/resourceGroups/test/providers/Microsoft.OperationalInsights/workspaces/queenzone-prod-law"
    sql_server_name                            = "queenzone-prod-sql"
    storage_account_name                       = "queenzoneprod"
    sql_firewall_rules = {
      "AppService-203-0-113-10" = {
        start_ip_address = "203.0.113.10"
        end_ip_address   = "203.0.113.10"
      }
    }
  }

  assert {
    condition     = length(azurerm_mssql_firewall_rule.azure_services) == 0 && length(azurerm_mssql_firewall_rule.explicit) == 1
    error_message = "The production target must not manage AllowAllWindowsAzureIps and must keep the explicit App Service rule."
  }

  assert {
    condition = (
      azurerm_mssql_server_extended_auditing_policy.production[0].enabled &&
      azurerm_mssql_server_extended_auditing_policy.production[0].log_monitoring_enabled &&
      azurerm_mssql_database_extended_auditing_policy.production[0].enabled == false &&
      azurerm_mssql_database_extended_auditing_policy.production[0].log_monitoring_enabled &&
      azurerm_mssql_server_extended_auditing_policy.production[0].retention_in_days == 0 &&
      length(azurerm_monitor_diagnostic_setting.sql_server_audit) == 1 &&
      length(azurerm_monitor_diagnostic_setting.sql_database_audit) == 0
    )
    error_message = "Production SQL auditing must keep the server/master Azure Monitor path and leave the duplicate database-level audit and diagnostic setting off."
  }

  assert {
    condition     = azurerm_mssql_server.created[0].public_network_access_enabled && azurerm_mssql_server.created[0].azuread_administrator[0].azuread_authentication_only == false
    error_message = "Public SQL access and SQL authentication stay until Entra auth and non-app clients have a path."
  }
}

run "database_level_audit_is_opt_in" {
  command = plan

  override_resource {
    target = azurerm_mssql_database.production
    values = {
      id = "/subscriptions/00000000-0000-0000-0000-000000000000/resourceGroups/test/providers/Microsoft.Sql/servers/queenzone-prod-sql/databases/queenzone-db"
    }
  }

  override_resource {
    target = azapi_resource.storage_account
    values = {
      id = "/subscriptions/00000000-0000-0000-0000-000000000000/resourceGroups/test/providers/Microsoft.Storage/storageAccounts/queenzoneprod"
    }
  }

  override_resource {
    target = azapi_resource.blob_service
    values = {
      id = "/subscriptions/00000000-0000-0000-0000-000000000000/resourceGroups/test/providers/Microsoft.Storage/storageAccounts/queenzoneprod/blobServices/default"
    }
  }

  variables {
    create_sql_server_with_write_only_password = true
    create_azure_services_firewall_rule        = false
    blob_service_is_preexisting                = false
    sql_extended_auditing_enabled              = true
    sql_database_extended_auditing_enabled     = true
    log_analytics_workspace_id                 = "/subscriptions/00000000-0000-0000-0000-000000000000/resourceGroups/test/providers/Microsoft.OperationalInsights/workspaces/queenzone-prod-law"
    sql_server_name                            = "queenzone-prod-sql"
    storage_account_name                       = "queenzoneprod"
  }

  assert {
    condition = (
      azurerm_mssql_database_extended_auditing_policy.production[0].enabled &&
      azurerm_mssql_database_extended_auditing_policy.production[0].log_monitoring_enabled &&
      length(azurerm_monitor_diagnostic_setting.sql_server_audit) == 1 &&
      length(azurerm_monitor_diagnostic_setting.sql_database_audit) == 1
    )
    error_message = "Opting into database-level auditing must enable the user-database policy and both diagnostic settings."
  }
}

run "rejects_allow_all_azure_firewall_rule" {
  command = plan

  override_resource {
    target = azapi_resource.sql_server
    values = {
      id = "/subscriptions/00000000-0000-0000-0000-000000000000/resourceGroups/test/providers/Microsoft.Sql/servers/test"
    }
  }

  override_resource {
    target = azapi_resource.storage_account
    values = {
      id = "/subscriptions/00000000-0000-0000-0000-000000000000/resourceGroups/test/providers/Microsoft.Storage/storageAccounts/test"
    }
  }

  override_resource {
    target = azapi_resource.blob_service
    values = {
      id = "/subscriptions/00000000-0000-0000-0000-000000000000/resourceGroups/test/providers/Microsoft.Storage/storageAccounts/test/blobServices/default"
    }
  }

  override_resource {
    target = azurerm_mssql_database.production
    values = {
      id = "/subscriptions/00000000-0000-0000-0000-000000000000/resourceGroups/test/providers/Microsoft.Sql/servers/test/databases/queenzone-db"
    }
  }

  variables {
    sql_firewall_rules = {
      "AllowAllWindowsAzureIps" = {
        start_ip_address = "0.0.0.0"
        end_ip_address   = "0.0.0.0"
      }
    }
  }

  expect_failures = [
    var.sql_firewall_rules,
  ]
}

run "rejects_public_container_off_allow_list" {
  command = plan

  override_resource {
    target = azapi_resource.sql_server
    values = {
      id = "/subscriptions/00000000-0000-0000-0000-000000000000/resourceGroups/test/providers/Microsoft.Sql/servers/test"
    }
  }

  override_resource {
    target = azapi_resource.storage_account
    values = {
      id = "/subscriptions/00000000-0000-0000-0000-000000000000/resourceGroups/test/providers/Microsoft.Storage/storageAccounts/test"
    }
  }

  override_resource {
    target = azapi_resource.blob_service
    values = {
      id = "/subscriptions/00000000-0000-0000-0000-000000000000/resourceGroups/test/providers/Microsoft.Storage/storageAccounts/test/blobServices/default"
    }
  }

  override_resource {
    target = azurerm_mssql_database.production
    values = {
      id = "/subscriptions/00000000-0000-0000-0000-000000000000/resourceGroups/test/providers/Microsoft.Sql/servers/test/databases/queenzone-db"
    }
  }

  variables {
    containers = {
      "album-or-single-covers"  = "Blob"
      "attachments"             = "None"
      "avatars"                 = "Blob"
      "brian-may"               = "Blob"
      "css"                     = "Blob"
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
      "test"                    = "None"
      "ugc-articles"            = "None"
      "ugc-avatars"             = "None"
      "ugc-forum"               = "None"
      "ugc-photos"              = "None"
      "us-convention-2001"      = "Blob"
      "not-allow-listed"        = "Blob"
    }
  }

  expect_failures = [
    var.containers,
  ]
}

run "rejects_required_private_container_made_public" {
  command = plan

  override_resource {
    target = azapi_resource.sql_server
    values = {
      id = "/subscriptions/00000000-0000-0000-0000-000000000000/resourceGroups/test/providers/Microsoft.Sql/servers/test"
    }
  }

  override_resource {
    target = azapi_resource.storage_account
    values = {
      id = "/subscriptions/00000000-0000-0000-0000-000000000000/resourceGroups/test/providers/Microsoft.Storage/storageAccounts/test"
    }
  }

  override_resource {
    target = azapi_resource.blob_service
    values = {
      id = "/subscriptions/00000000-0000-0000-0000-000000000000/resourceGroups/test/providers/Microsoft.Storage/storageAccounts/test/blobServices/default"
    }
  }

  override_resource {
    target = azurerm_mssql_database.production
    values = {
      id = "/subscriptions/00000000-0000-0000-0000-000000000000/resourceGroups/test/providers/Microsoft.Sql/servers/test/databases/queenzone-db"
    }
  }

  variables {
    public_blob_containers = ["attachments"]
    containers = {
      "attachments"    = "Blob"
      "databasebackup" = "None"
      "songfiles"      = "None"
      "ugc-avatars"    = "None"
      "ugc-forum"      = "None"
    }
  }

  expect_failures = [
    var.containers,
  ]
}
