mock_provider "azapi" {}
mock_provider "azurerm" {}

variables {
  resource_group_id = "/subscriptions/00000000-0000-0000-0000-000000000000/resourceGroups/test"
}

run "builds_is_the_only_public_container" {
  command = plan

  override_resource {
    target = azapi_resource.storage_account
    values = {
      id = "/subscriptions/00000000-0000-0000-0000-000000000000/resourceGroups/test/providers/Microsoft.Storage/storageAccounts/queenzonemobilebuilds"
    }
  }

  override_resource {
    target = azapi_resource.blob_service
    values = {
      id = "/subscriptions/00000000-0000-0000-0000-000000000000/resourceGroups/test/providers/Microsoft.Storage/storageAccounts/queenzonemobilebuilds/blobServices/default"
    }
  }

  assert {
    condition     = var.containers["builds"] == "Blob" && length(var.containers) == 1 && contains(var.public_blob_containers, "builds") && length(var.public_blob_containers) == 1
    error_message = "Mobile build storage may only publish the builds container."
  }

  assert {
    condition     = azapi_resource.builds_container.name == "builds"
    error_message = "The managed mobile-build container resource must remain named builds."
  }
}

run "rejects_public_container_off_allow_list" {
  command = plan

  override_resource {
    target = azapi_resource.storage_account
    values = {
      id = "/subscriptions/00000000-0000-0000-0000-000000000000/resourceGroups/test/providers/Microsoft.Storage/storageAccounts/queenzonemobilebuilds"
    }
  }

  override_resource {
    target = azapi_resource.blob_service
    values = {
      id = "/subscriptions/00000000-0000-0000-0000-000000000000/resourceGroups/test/providers/Microsoft.Storage/storageAccounts/queenzonemobilebuilds/blobServices/default"
    }
  }

  variables {
    containers = {
      builds = "Blob"
      extra  = "Blob"
    }
  }

  expect_failures = [
    var.containers,
  ]
}
