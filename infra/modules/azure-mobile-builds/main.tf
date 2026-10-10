# Dedicated throwaway-build storage. AzAPI avoids exporting account keys or
# connection strings into OpenTofu state.
resource "azapi_resource" "storage_account" {
  type      = "Microsoft.Storage/storageAccounts@2026-04-01"
  name      = var.storage_account_name
  parent_id = var.resource_group_id
  location  = var.location

  body = {
    kind = "StorageV2"
    sku = {
      name = "Standard_LRS"
    }
    properties = {
      accessTier                   = "Hot"
      allowBlobPublicAccess        = true
      allowCrossTenantReplication  = false
      allowSharedKeyAccess         = true
      defaultToOAuthAuthentication = false
      minimumTlsVersion            = "TLS1_2"
      publicNetworkAccess          = "Enabled"
      supportsHttpsTrafficOnly     = true
      encryption = {
        keySource = "Microsoft.Storage"
        # requireInfrastructureEncryption is immutable after account
        # creation -- the live account has it unset (null), and Azure
        # rejects any attempt to set it (to true OR false) after the fact
        # with AccountPropertyCannotBeUpdated. Omit it entirely so this
        # resource's PUT never tries to touch it again.
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
    }
  }

  response_export_values = []

  lifecycle {
    prevent_destroy = true
  }
}

resource "azapi_resource" "blob_service" {
  type      = "Microsoft.Storage/storageAccounts/blobServices@2026-04-01"
  name      = "default"
  parent_id = azapi_resource.storage_account.id

  body = {
    properties = {
      cors = {
        corsRules = []
      }
      staticWebsite = {
        enabled = false
      }
    }
  }

  response_export_values = []

  lifecycle {
    prevent_destroy = true
  }
}

resource "azapi_resource" "builds_container" {
  type      = "Microsoft.Storage/storageAccounts/blobServices/containers@2026-04-01"
  name      = "builds"
  parent_id = azapi_resource.blob_service.id

  body = {
    properties = {
      defaultEncryptionScope      = "$account-encryption-key"
      denyEncryptionScopeOverride = false
      # Blob permits anonymous object reads but does not expose container listing.
      publicAccess = var.containers["builds"]
    }
  }

  response_export_values = []

  lifecycle {
    prevent_destroy = true
  }
}

resource "azurerm_role_assignment" "mobile_publisher" {
  scope                            = azapi_resource.storage_account.id
  role_definition_name             = "Storage Blob Data Contributor"
  principal_id                     = var.publisher_principal_id
  principal_type                   = "ServicePrincipal"
  skip_service_principal_aad_check = true

  lifecycle {
    # skip_service_principal_aad_check is a create-time-only client hint
    # with no live Azure representation to reconcile against post-import.
    # azurerm_role_assignment has no update API at all (role assignments
    # are immutable once created), so any drift here -- real or not --
    # hard-fails with "doesn't support update" instead of a normal diff.
    # Keep the value above for if this resource is ever recreated, but
    # never try to "update" it on the existing resource.
    ignore_changes = [skip_service_principal_aad_check]
  }
}
