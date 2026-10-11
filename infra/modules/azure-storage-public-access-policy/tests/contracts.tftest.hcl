mock_provider "azurerm" {
  mock_resource "azurerm_policy_definition" {
    defaults = {
      id = "/subscriptions/00000000-0000-0000-0000-000000000000/providers/Microsoft.Authorization/policyDefinitions/qz-blob-container-public-access-test"
    }
  }
}

variables {
  location    = "australiaeast"
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

run "accepts_valid_container_names" {
  command = plan

  variables {
    allowed_public_containers = {
      queenzoneprod = ["a-b", "abc", "a1b2-c3", "${join("", [for i in range(63) : "a"])}"]
    }
  }

  assert {
    condition     = contains(output.allowed_full_names, "queenzoneprod/default/a-b") && length(output.allowed_full_names) == 4
    error_message = "Valid Azure container names (3-63 chars, single hyphens) must be accepted."
  }
}

run "rejects_container_name_too_short" {
  command = plan
  variables {
    allowed_public_containers = { queenzoneprod = ["ab"] }
  }
  expect_failures = [var.allowed_public_containers]
}

run "rejects_container_name_too_long" {
  command = plan
  variables {
    allowed_public_containers = { queenzoneprod = ["${join("", [for i in range(64) : "a"])}"] }
  }
  expect_failures = [var.allowed_public_containers]
}

run "rejects_consecutive_hyphens" {
  command = plan
  variables {
    allowed_public_containers = { queenzoneprod = ["a--b"] }
  }
  expect_failures = [var.allowed_public_containers]
}

run "rejects_leading_or_trailing_hyphen_and_uppercase" {
  command = plan
  variables {
    allowed_public_containers = { queenzoneprod = ["-ab"], queenzonedev = ["ab-"], queenzonemobilebuilds = ["Abc"] }
  }
  expect_failures = [var.allowed_public_containers]
}

run "rejects_blank_location" {
  command = plan
  variables {
    location = ""
  }
  expect_failures = [var.location]
}
