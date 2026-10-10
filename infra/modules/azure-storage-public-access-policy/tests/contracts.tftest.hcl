mock_provider "azurerm" {
  mock_resource "azurerm_policy_definition" {
    defaults = {
      id = "/subscriptions/00000000-0000-0000-0000-000000000000/providers/Microsoft.Authorization/policyDefinitions/qz-blob-container-public-access-test"
    }
  }
}

variables {
  name_suffix = "test"
  resource_group_ids = {
    test = "/subscriptions/00000000-0000-0000-0000-000000000000/resourceGroups/test"
  }
  allowed_public_containers = {
    queenzoneprod         = ["images", "css"]
    queenzonemobilebuilds = ["builds"]
  }
}

run "defaults_to_audit_with_per_account_allow_list" {
  command = plan

  assert {
    condition     = jsondecode(azurerm_resource_group_policy_assignment.blob_container_public_access["test"].parameters).effect.value == "Audit"
    error_message = "The policy must start in Audit."
  }

  assert {
    condition     = jsondecode(azurerm_resource_group_policy_assignment.blob_container_public_access["test"].parameters).allowedContainers.value == ["queenzonemobilebuilds/default/builds", "queenzoneprod/default/css", "queenzoneprod/default/images"]
    error_message = "The allow-list must be per account as <account>/default/<container>, so a name allowed on one account is not allowed on another."
  }

  assert {
    condition     = azurerm_resource_group_policy_assignment.blob_container_public_access["test"].identity[0].type == "SystemAssigned"
    error_message = "The assignment must carry a managed identity rather than relying on shared credentials."
  }

  assert {
    condition     = azurerm_policy_definition.blob_container_public_access.mode == "All"
    error_message = "Containers have no location or tags, so the definition must use mode All."
  }

  assert {
    condition = alltrue([
      jsondecode(azurerm_policy_definition.blob_container_public_access.policy_rule).if.allOf[0].equals == "Microsoft.Storage/storageAccounts/blobServices/containers",
      jsondecode(azurerm_policy_definition.blob_container_public_access.policy_rule).if.allOf[2].notEquals == "None",
      jsondecode(azurerm_policy_definition.blob_container_public_access.policy_rule).if.allOf[3].field == "fullName",
    ])
    error_message = "The rule must target containers whose publicAccess is not None and whose fullName is not allow-listed."
  }
}

run "deny_is_the_later_switch" {
  command = plan

  variables {
    effect = "Deny"
  }

  assert {
    condition     = jsondecode(azurerm_resource_group_policy_assignment.blob_container_public_access["test"].parameters).effect.value == "Deny"
    error_message = "Setting effect = Deny must reach the assignment."
  }
}

run "rejects_unknown_effect" {
  command = plan

  variables {
    effect = "Modify"
  }

  expect_failures = [var.effect]
}
