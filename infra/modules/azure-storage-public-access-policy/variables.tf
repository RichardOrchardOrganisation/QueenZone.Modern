variable "name_suffix" {
  description = "Environment suffix that keeps the subscription-scoped definition name unique per root (for example dev or production)."
  type        = string

  validation {
    condition     = can(regex("^[a-z0-9-]{1,24}$", var.name_suffix))
    error_message = "name_suffix must be 1-24 lowercase letters, digits or hyphens."
  }
}

variable "resource_group_ids" {
  description = "Resource groups the assignment covers, keyed by a stable label. Each RG gets one assignment."
  type        = map(string)

  validation {
    condition     = length(var.resource_group_ids) > 0
    error_message = "At least one resource group must be in scope."
  }
}

variable "allowed_public_containers" {
  description = "Per-account allow-list: storage account name => container names that may have publicAccess other than None. Fed from the azure-data / azure-mobile-builds public_blob_containers outputs (#2211)."
  type        = map(list(string))

  validation {
    condition = alltrue([
      for account, names in var.allowed_public_containers :
      can(regex("^[a-z0-9]{3,24}$", account)) && alltrue([for n in names : can(regex("^[a-z0-9](?:[a-z0-9]|-[a-z0-9]){2,62}$", n))])
    ])
    error_message = "Account names must be valid storage account names and container names valid blob container names."
  }
}

variable "effect" {
  description = "Policy effect. Starts at Audit; switch to Deny once compliance is clean (#2211)."
  type        = string
  default     = "Audit"

  validation {
    condition     = contains(["Audit", "Deny", "Disabled"], var.effect)
    error_message = "effect must be Audit, Deny or Disabled."
  }
}
