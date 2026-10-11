output "policy_definition_id" {
  description = "Custom policy definition ID."
  value       = azurerm_policy_definition.blob_container_public_access.id
}

output "assignment_ids" {
  description = "Policy assignment IDs keyed by resource group label."
  value       = { for k, a in azurerm_resource_group_policy_assignment.blob_container_public_access : k => a.id }
}

output "allowed_full_names" {
  description = "Allow-list as passed to the policy (<account>/default/<container>)."
  value       = local.allowed_full_names
}
