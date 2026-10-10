locals {
  # Policy matches the container's fullName, which is "<account>/default/<container>".
  allowed_full_names = sort(flatten([
    for account, names in var.allowed_public_containers : [for n in names : "${account}/default/${n}"]
  ]))
}

resource "azurerm_policy_definition" "blob_container_public_access" {
  name         = "qz-blob-container-public-access-${var.name_suffix}"
  policy_type  = "Custom"
  mode         = "All"
  display_name = "QueenZone: blob containers must be private unless allow-listed (${var.name_suffix})"
  description  = "Flags or denies blob containers whose publicAccess is not None unless <account>/default/<container> is on the OpenTofu allow-list. See #2211."

  metadata = jsonencode({
    category = "Storage"
    source   = "infra/modules/azure-storage-public-access-policy"
  })

  parameters = jsonencode({
    effect = {
      type          = "String"
      allowedValues = ["Audit", "Deny", "Disabled"]
      defaultValue  = "Audit"
      metadata      = { displayName = "Effect" }
    }
    allowedContainers = {
      type         = "Array"
      defaultValue = []
      metadata     = { displayName = "Allowed public containers (<account>/default/<container>)" }
    }
  })

  policy_rule = jsonencode({
    if = {
      allOf = [
        { field = "type", equals = "Microsoft.Storage/storageAccounts/blobServices/containers" },
        { field = "Microsoft.Storage/storageAccounts/blobServices/containers/publicAccess", exists = "true" },
        { field = "Microsoft.Storage/storageAccounts/blobServices/containers/publicAccess", notEquals = "None" },
        { field = "fullName", notIn = "[parameters('allowedContainers')]" },
      ]
    }
    then = { effect = "[parameters('effect')]" }
  })
}

resource "azurerm_resource_group_policy_assignment" "blob_container_public_access" {
  for_each = var.resource_group_ids

  name                 = "qz-blob-public-${each.key}"
  display_name         = "QueenZone blob containers private unless allow-listed (${each.key})"
  resource_group_id    = each.value
  policy_definition_id = azurerm_policy_definition.blob_container_public_access.id
  enforce              = true

  parameters = jsonencode({
    effect            = { value = var.effect }
    allowedContainers = { value = local.allowed_full_names }
  })
}
