variable "enable_custom_domain" {
  description = "Phase 3 enables dev.queenzone.org after the production Cloudflare CNAME apply."
  type        = bool
  default     = true
}

variable "azure_subscription_id" {
  description = "Azure subscription containing the existing shared SQL logical server."
  type        = string
  default     = "610e3b3a-028d-4f1b-ac1d-a5567a4f8b9d"

  validation {
    condition     = can(regex("^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$", var.azure_subscription_id))
    error_message = "azure_subscription_id must be a lowercase GUID."
  }
}

variable "storage_public_access_policy_effect" {
  description = "Effect of the #2211 blob-container public-access policy. Audit first; switch to Deny once compliance is clean."
  type        = string
  default     = "Audit"

  validation {
    condition     = contains(["Audit", "Deny", "Disabled"], var.storage_public_access_policy_effect)
    error_message = "storage_public_access_policy_effect must be Audit, Deny or Disabled."
  }
}

variable "opentofu_apply_principal_object_id" {
  description = "Object ID of the QueenZone OpenTofu Apply service principal (opentofu-apply environment, ARM_CLIENT_ID 7b466caa-...)."
  type        = string
  default     = "e5e5ea3b-2a6e-4b62-abb8-947e5e66378c"
}
