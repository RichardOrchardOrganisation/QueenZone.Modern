variable "resource_group_id" {
  description = "Existing QueenZone production resource group ID."
  type        = string
}

variable "location" {
  description = "Azure region for low-cost mobile build storage."
  type        = string
  default     = "australiaeast"
}

variable "storage_account_name" {
  description = "Dedicated mobile test-build Storage account name."
  type        = string
  default     = "queenzonemobilebuilds"
}

variable "publisher_principal_id" {
  description = "Object ID of the GitHub deploy OIDC service principal."
  type        = string
  default     = "bb1dfbf7-851d-474b-8749-2f692e2f8f36"
}

variable "public_blob_containers" {
  description = "Blob containers that may have publicAccess other than None. Mobile builds only."
  type        = set(string)
  default     = ["builds"]
}

variable "containers" {
  description = "Blob container ACLs for this account. builds is the only intentionally public container."
  type        = map(string)
  default = {
    builds = "Blob"
  }

  validation {
    condition     = alltrue([for access in values(var.containers) : contains(["None", "Blob", "Container"], access)])
    error_message = "Container access must be None, Blob, or Container."
  }

  validation {
    condition = alltrue([
      for name, access in var.containers :
      access == "None" || contains(var.public_blob_containers, name)
    ])
    error_message = "Any container with publicAccess other than None must be named in public_blob_containers."
  }
}
