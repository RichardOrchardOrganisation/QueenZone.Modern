output "import_contract" {
  description = "Non-sensitive live names for mobile build hosting."
  value = {
    storage_account = var.storage_account_name
    container       = azapi_resource.builds_container.name
  }
}

output "storage_account_id" {
  description = "Mobile build Storage account ID without keys."
  value       = azapi_resource.storage_account.id
}
