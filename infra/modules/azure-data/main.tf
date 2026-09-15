locals {
  sql_server_id = var.existing_sql_server_id != null ? var.existing_sql_server_id : (
    var.create_sql_server_with_write_only_password ? azurerm_mssql_server.created[0].id : azapi_resource.sql_server[0].id
  )
  blob_service_id = !var.manage_storage_account ? null : (
    var.manage_blob_service && var.blob_service_is_preexisting ? azapi_resource.blob_service[0].id : "${azapi_resource.storage_account[0].id}/blobServices/default"
  )
  blob_service_body = {
    properties = {
      containerDeleteRetentionPolicy = {
        days    = 7
        enabled = true
      }
      cors = {
        corsRules = []
      }
      deleteRetentionPolicy = {
        allowPermanentDelete = false
        days                 = 7
        enabled              = true
      }
      staticWebsite = {
        enabled = false
      }
    }
  }
}

resource "azapi_resource" "sql_server" {
  count = var.existing_sql_server_id == null && !var.create_sql_server_with_write_only_password ? 1 : 0

  type      = "Microsoft.Sql/servers@2025-02-01-preview"
  name      = var.sql_server_name
  parent_id = var.resource_group_id
  location  = var.location

  body = {
    properties = {
      # ARM requires the existing server administrator name in the resource
      # shape. Its password remains outside OpenTofu and is never exported.
      administratorLogin = "CloudSA6f234939"
      administrators = {
        administratorType         = "ActiveDirectory"
        azureADOnlyAuthentication = false
        login                     = "richard@thinkingwebsites.com.au"
        principalType             = "User"
        sid                       = "95e57126-c09d-4737-a76b-8281fac1ad6d"
        tenantId                  = "c9f094fd-23bf-4a35-a406-bcaacd7e1a8e"
      }
      minimalTlsVersion             = "1.2"
      publicNetworkAccess           = "Enabled"
      retentionDays                 = -1
      restrictOutboundNetworkAccess = "Disabled"
      version                       = "12.0"
    }
  }

  response_export_values = []

  lifecycle {
    prevent_destroy = true

    # The Entra administrator is recorded to preserve the existing ARM shape,
    # but operator identity rotation is governed outside this stack.
    ignore_changes = [
      body.properties.administrators,
      identity,
    ]
  }
}

# New SQL servers use AzureRM's write-only password field. The password is
# ephemeral in OpenTofu and is therefore sent to Azure without entering the
# plan or state. Existing production remains at its imported AzAPI address.
resource "azurerm_mssql_server" "created" {
  count = var.create_sql_server_with_write_only_password ? 1 : 0

  name                                    = var.sql_server_name
  resource_group_name                     = var.resource_group_name
  location                                = var.location
  version                                 = "12.0"
  administrator_login                     = "CloudSA6f234939"
  administrator_login_password_wo         = var.sql_server_administrator_password_wo
  administrator_login_password_wo_version = var.sql_server_administrator_password_wo_version
  minimum_tls_version                     = "1.2"
  public_network_access_enabled           = true
  outbound_network_restriction_enabled    = false

  azuread_administrator {
    login_username              = "richard@thinkingwebsites.com.au"
    object_id                   = "95e57126-c09d-4737-a76b-8281fac1ad6d"
    tenant_id                   = "c9f094fd-23bf-4a35-a406-bcaacd7e1a8e"
    azuread_authentication_only = false
  }

  lifecycle {
    prevent_destroy = true

    # Operator identity rotation remains outside this stack, matching the
    # imported production server's existing ownership boundary.
    ignore_changes = [azuread_administrator]
  }
}

resource "azurerm_mssql_firewall_rule" "azure_services" {
  count = var.create_azure_services_firewall_rule ? 1 : 0

  name             = "AllowAllWindowsAzureIps"
  server_id        = local.sql_server_id
  start_ip_address = "0.0.0.0"
  end_ip_address   = "0.0.0.0"

  lifecycle {
    prevent_destroy = true
  }
}

resource "azurerm_mssql_database" "production" {
  count = var.manage_sql_database ? 1 : 0

  name      = var.sql_database_name
  server_id = local.sql_server_id

  sku_name                            = var.sql_database_sku_name
  max_size_gb                         = var.sql_database_max_size_gb
  collation                           = "SQL_Latin1_General_CP1_CI_AS"
  storage_account_type                = "Local"
  zone_redundant                      = false
  read_scale                          = false
  ledger_enabled                      = false
  transparent_data_encryption_enabled = true

  short_term_retention_policy {
    retention_days           = 7
    backup_interval_in_hours = 24
  }

  long_term_retention_policy {
    weekly_retention  = "PT0S"
    monthly_retention = "PT0S"
    yearly_retention  = "PT0S"
    week_of_year      = 1
  }

  lifecycle {
    prevent_destroy = true
  }
}

resource "azurerm_mssql_server_extended_auditing_policy" "production" {
  count = var.create_server_extended_auditing_policy ? 1 : 0

  server_id              = local.sql_server_id
  enabled                = false
  log_monitoring_enabled = false
  retention_in_days      = 0

  lifecycle {
    prevent_destroy = true
  }
}

resource "azurerm_mssql_database_extended_auditing_policy" "production" {
  count = var.manage_sql_database ? 1 : 0

  database_id            = azurerm_mssql_database.production[0].id
  enabled                = false
  log_monitoring_enabled = false
  retention_in_days      = 0

  lifecycle {
    prevent_destroy = true
  }
}

# AzAPI manages Storage through ARM without calling listKeys. The AzureRM
# storage-account resource exports keys and connection strings into state,
# which violates this stack's no-secrets-in-state boundary.
resource "azapi_resource" "storage_account" {
  count = var.manage_storage_account ? 1 : 0

  type      = "Microsoft.Storage/storageAccounts@2026-04-01"
  name      = var.storage_account_name
  parent_id = var.resource_group_id
  location  = var.location

  body = {
    kind = "StorageV2"
    sku = {
      name = "Standard_LRS"
    }
    properties = merge({
      accessTier                   = "Hot"
      allowBlobPublicAccess        = true
      allowCrossTenantReplication  = false
      allowSharedKeyAccess         = true
      defaultToOAuthAuthentication = false
      dnsEndpointType              = "Standard"
      dualStackEndpointPreference = {
        publishIpv6Endpoint = false
      }
      minimumTlsVersion        = "TLS1_2"
      publicNetworkAccess      = "Enabled"
      supportsHttpsTrafficOnly = true
      encryption = {
        keySource                       = "Microsoft.Storage"
        requireInfrastructureEncryption = false
        services = {
          blob = {
            enabled = true
            keyType = "Account"
          }
          file = {
            enabled = true
            keyType = "Account"
          }
        }
      }
      networkAcls = {
        bypass              = "AzureServices"
        defaultAction       = "Allow"
        ipRules             = []
        ipv6Rules           = []
        virtualNetworkRules = []
      }
      }, var.storage_custom_domain_name == null ? {} : {
      customDomain = {
        name = var.storage_custom_domain_name
      }
    })
  }

  response_export_values = []

  lifecycle {
    prevent_destroy = true
  }
}

resource "azapi_resource" "blob_service" {
  count = var.manage_storage_account && var.manage_blob_service && var.blob_service_is_preexisting ? 1 : 0

  type      = "Microsoft.Storage/storageAccounts/blobServices@2026-04-01"
  name      = "default"
  parent_id = azapi_resource.storage_account[0].id

  body = local.blob_service_body

  response_export_values = []

  lifecycle {
    prevent_destroy = true
  }
}

# A new StorageV2 account creates blobServices/default automatically. Patch
# that child after account creation instead of attempting a second create.
# Imported estates retain azapi_resource.blob_service above so their existing
# state address and prevent_destroy protection remain unchanged.
resource "azapi_update_resource" "blob_service_settings" {
  count = var.manage_storage_account && var.manage_blob_service && !var.blob_service_is_preexisting ? 1 : 0

  type        = "Microsoft.Storage/storageAccounts/blobServices@2026-04-01"
  resource_id = "${azapi_resource.storage_account[0].id}/blobServices/default"
  body        = local.blob_service_body

  response_export_values = []
}

resource "azapi_resource" "container" {
  for_each = var.manage_storage_account ? var.containers : {}

  type      = "Microsoft.Storage/storageAccounts/blobServices/containers@2026-04-01"
  name      = each.key
  parent_id = local.blob_service_id

  body = {
    properties = {
      defaultEncryptionScope      = "$account-encryption-key"
      denyEncryptionScopeOverride = false
      publicAccess                = each.value
    }
  }

  response_export_values = []

  lifecycle {
    prevent_destroy = true
  }
}
