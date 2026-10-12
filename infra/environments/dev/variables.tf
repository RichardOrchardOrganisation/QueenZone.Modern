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
