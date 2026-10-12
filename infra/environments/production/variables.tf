variable "environment" {
  description = "Deployment environment. This root module is production-only."
  type        = string
  default     = "production"

  validation {
    condition     = var.environment == "production"
    error_message = "Only the production environment is valid in this root module."
  }
}

variable "azure_subscription_id" {
  description = "Azure subscription containing QueenZone production."
  type        = string
  default     = "610e3b3a-028d-4f1b-ac1d-a5567a4f8b9d"

  validation {
    condition     = can(regex("^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$", var.azure_subscription_id))
    error_message = "azure_subscription_id must be a lowercase GUID."
  }
}

variable "azure_resource_group_name" {
  description = "Existing Azure resource group to import in a later issue."
  type        = string
  default     = "Queenzone-RG"

  validation {
    condition     = var.azure_resource_group_name == "Queenzone-RG"
    error_message = "Production resources must remain in Queenzone-RG."
  }
}

variable "azure_location" {
  description = "Existing resource-group and legacy production region during the staged migration."
  type        = string
  default     = "australiaeast"

  validation {
    condition     = var.azure_location == "australiaeast"
    error_message = "The existing QueenZone production estate is in australiaeast."
  }
}

variable "production_target_location" {
  description = "Approved target region for the replacement production estate (ADR 0020)."
  type        = string
  default     = "canadaeast"

  validation {
    condition     = var.production_target_location == "canadaeast"
    error_message = "Production must migrate to canadaeast as approved by ADR 0020."
  }
}

variable "target_sql_admin_password" {
  description = "Ephemeral SQL administrator password for the replacement server. It must come from Bitwarden and is never stored in plan or state."
  type        = string
  default     = null
  nullable    = true
  sensitive   = true
  ephemeral   = true
}

variable "cloudflare_account_id" {
  description = "Existing Cloudflare account ID. Not a credential."
  type        = string
  default     = "f93121b2086286e79a7a9fdb8d03cb4c"

  validation {
    condition     = can(regex("^[0-9a-f]{32}$", var.cloudflare_account_id))
    error_message = "cloudflare_account_id must be a 32-character lowercase hexadecimal ID."
  }
}

variable "cloudflare_zone_id" {
  description = "Existing queenzone.org Cloudflare zone ID. Not a credential."
  type        = string
  default     = "079fc2f37095c82fb3a2b4da65718b2b"

  validation {
    condition     = can(regex("^[0-9a-f]{32}$", var.cloudflare_zone_id))
    error_message = "cloudflare_zone_id must be a 32-character lowercase hexadecimal ID."
  }
}

variable "cloudflare_zone_name" {
  description = "Existing Cloudflare zone name."
  type        = string
  default     = "queenzone.org"

  validation {
    condition     = var.cloudflare_zone_name == "queenzone.org"
    error_message = "This production root may manage only queenzone.org."
  }
}

variable "app_service_sku" {
  description = "Existing single-instance App Service SKU."
  type        = string
  default     = "B1"

  validation {
    condition     = var.app_service_sku == "B1"
    error_message = "QueenZone production must remain on B1 unless the hosting decision changes first."
  }
}

variable "app_service_worker_count" {
  description = "Existing App Service worker count."
  type        = number
  default     = 1

  validation {
    condition     = var.app_service_worker_count == 1
    error_message = "QueenZone production is intentionally single-instance."
  }
}
