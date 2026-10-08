variable "resource_group_name" {
  description = "Existing QueenZone production resource group."
  type        = string
  default     = "Queenzone-RG"
}

variable "location" {
  description = "Existing Azure region."
  type        = string
  default     = "australiaeast"
}

variable "service_plan_name" {
  description = "Existing App Service plan name."
  type        = string
  default     = "ASP-Queenzone"
}

variable "web_app_name" {
  description = "Existing Linux web app name."
  type        = string
  default     = "queenzone-dev"
}

variable "log_analytics_workspace_name" {
  description = "Existing Log Analytics workspace name."
  type        = string
  default     = "queenzone-dev-law"
}

variable "application_insights_name" {
  description = "Existing workspace-linked Application Insights component name."
  type        = string
  default     = "queenzone-dev-ai"
}

variable "log_analytics_daily_quota_gb" {
  description = "Log Analytics workspace daily ingestion cap in GB. Production sets 0.25 after the #2204 cap-hit fix; the default keeps non-prod workspaces at 0.1."
  type        = number
  default     = 0.1

  validation {
    condition     = var.log_analytics_daily_quota_gb > 0
    error_message = "log_analytics_daily_quota_gb must be greater than zero."
  }
}

variable "custom_hostnames" {
  description = "Existing custom hostname bindings and their uploaded certificate thumbprints. Thumbprints are identifiers, not secrets."
  type        = map(string)
  default = {
    "queenzone.org"     = "4B8832D7F69444E881F5BFF26AEC0578A415E275"
    "www.queenzone.org" = "A45C7DDFDA8719399B772B1C6336F8B28BD80B24"
  }

  validation {
    condition = var.environment_name == "production" ? (
      toset(keys(var.custom_hostnames)) == toset(["queenzone.org", "www.queenzone.org"])
      ) : var.environment_name == "migration" ? (
      length(var.custom_hostnames) == 0
    ) : alltrue([for hostname in keys(var.custom_hostnames) : hostname == "dev.queenzone.org"])
    error_message = "Production retains apex/www; migration binds no custom hostname; dev may bind only dev.queenzone.org."
  }
}

variable "sku_name" {
  description = "Existing App Service SKU."
  type        = string
  default     = "B1"

  validation {
    condition     = var.sku_name == "B1"
    error_message = "The accepted production hosting SKU is B1."
  }
}

variable "worker_count" {
  description = "Existing App Service worker count."
  type        = number
  default     = 1

  validation {
    condition     = var.worker_count == 1
    error_message = "The accepted production topology is single-instance."
  }
}

variable "environment_name" {
  description = "Selects production, migration-candidate, or dev safeguards; existing callers remain production."
  type        = string
  default     = "production"
  validation {
    condition     = contains(["production", "migration", "dev"], var.environment_name)
    error_message = "Use production, migration, or dev."
  }
}

variable "allow_direct_access" {
  description = "Dev bootstrap only: allow HTTPS verification before DNS cutover."
  type        = bool
  default     = false
  validation {
    condition     = !var.allow_direct_access || contains(["migration", "dev"], var.environment_name)
    error_message = "Only migration and dev apps may allow direct ingress."
  }
}

variable "managed_hostnames" {
  description = "Dev hostnames with Azure-managed TLS; enable only after DNS validation is ready."
  type        = set(string)
  default     = []
  validation {
    condition = alltrue([
      for hostname in var.managed_hostnames :
      var.environment_name == "dev" && hostname == "dev.queenzone.org" && !contains(keys(var.custom_hostnames), hostname)
    ])
    error_message = "Only dev.queenzone.org may use managed TLS, without an uploaded-certificate binding."
  }
}
