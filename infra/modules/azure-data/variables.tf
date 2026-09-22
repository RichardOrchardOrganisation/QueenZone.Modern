variable "resource_group_name" {
  description = "Resource group that owns the storage account."
  type        = string
  default     = "Queenzone-RG"
}

variable "resource_group_id" {
  description = "Resource group ID that owns the storage account."
  type        = string
}

variable "location" {
  description = "Azure region for the storage account."
  type        = string
  default     = "australiaeast"
}

variable "sql_server_name" {
  description = "Azure SQL logical server name when this module manages the server."
  type        = string
  default     = "queenzone-sql-server"
}

variable "sql_database_name" {
  description = "Azure SQL database name."
  type        = string
  default     = "queenzone-db"
}

variable "storage_account_name" {
  description = "Application media storage account name."
  type        = string
  default     = "queenzone"
}

variable "manage_storage_account" {
  description = "Whether this module manages application media Storage resources. Set false when retaining SQL without its former Storage sibling."
  type        = bool
  default     = true
}

variable "sql_database_sku_name" {
  description = "Azure SQL database SKU name."
  type        = string
  default     = "S0"
}

variable "sql_database_max_size_gb" {
  description = "Maximum Azure SQL database size in GB."
  type        = number
  default     = 10

  validation {
    condition     = var.sql_database_max_size_gb >= 2
    error_message = "sql_database_max_size_gb must be at least 2 GB."
  }
}

variable "existing_sql_server_id" {
  description = "Existing Azure SQL logical server ARM ID. When set, this module creates only the database on that server."
  type        = string
  default     = null
  nullable    = true

  validation {
    condition     = var.existing_sql_server_id == null || can(regex("^/subscriptions/[0-9a-f-]+/resourceGroups/[^/]+/providers/Microsoft\\.Sql/servers/[^/]+$", var.existing_sql_server_id))
    error_message = "existing_sql_server_id must be an Azure SQL logical server ARM ID."
  }
}

variable "create_sql_server_with_write_only_password" {
  description = "Create a new SQL logical server with AzureRM's write-only password field. Existing imported production callers leave this false."
  type        = bool
  default     = false

  validation {
    condition     = !var.create_sql_server_with_write_only_password || var.existing_sql_server_id == null
    error_message = "A caller cannot create a SQL server and supply an existing SQL server ID."
  }
}

variable "sql_server_administrator_password_wo" {
  description = "Ephemeral SQL administrator password used only when creating a new server; it is never persisted in plan or state."
  type        = string
  default     = null
  nullable    = true
  sensitive   = true
  ephemeral   = true
}

variable "sql_server_administrator_password_wo_version" {
  description = "Version marker for the write-only SQL administrator password. Increment only when intentionally rotating it."
  type        = number
  default     = 1
}

variable "manage_sql_database" {
  description = "Manage the SQL database. Migration callers leave this false until Azure's database-copy operation has created the destination database."
  type        = bool
  default     = true
}

variable "create_azure_services_firewall_rule" {
  description = "Whether this module owns the server-wide AllowAllWindowsAzureIps firewall rule. Production queenzone-prod-sql leaves this false."
  type        = bool
  default     = true

  validation {
    condition     = var.existing_sql_server_id == null || !var.create_azure_services_firewall_rule
    error_message = "A caller using an existing SQL server must not create a server-wide firewall rule."
  }
}

variable "sql_firewall_rules" {
  description = "Explicit public SQL firewall rules. Names are the Azure rule names. Use this for known clients such as App Service outbound addresses."
  type = map(object({
    start_ip_address = string
    end_ip_address   = string
  }))
  default = {}

  validation {
    condition = alltrue([
      for name, rule in var.sql_firewall_rules : (
        can(regex("^[0-9A-Za-z][0-9A-Za-z_-]{0,127}$", name))
        && name != "AllowAllWindowsAzureIps"
        && !(rule.start_ip_address == "0.0.0.0" && rule.end_ip_address == "0.0.0.0")
      )
    ])
    error_message = "SQL firewall rules must use an explicit range. AllowAllWindowsAzureIps and 0.0.0.0-0.0.0.0 are not accepted."
  }
}

variable "sql_extended_auditing_enabled" {
  description = "Enable server and database extended auditing to Azure Monitor. Retention follows the Log Analytics workspace."
  type        = bool
  default     = false
}

variable "log_analytics_workspace_id" {
  description = "Log Analytics workspace that receives SQLSecurityAuditEvents when SQL extended auditing is enabled."
  type        = string
  default     = null
  nullable    = true

  validation {
    condition = (
      var.log_analytics_workspace_id == null ||
      can(regex("^/subscriptions/[0-9a-f-]+/resourceGroups/[^/]+/providers/Microsoft\\.OperationalInsights/workspaces/[^/]+$", var.log_analytics_workspace_id))
    )
    error_message = "log_analytics_workspace_id must be a Log Analytics workspace ARM ID."
  }
}

variable "create_server_extended_auditing_policy" {
  description = "Whether this module owns the SQL server-wide auditing policy."
  type        = bool
  default     = true

  validation {
    condition     = var.existing_sql_server_id == null || !var.create_server_extended_auditing_policy
    error_message = "A caller using an existing SQL server must not create its server-wide auditing policy."
  }
}

variable "storage_custom_domain_name" {
  description = "Optional custom domain for the storage account."
  type        = string
  default     = "cdn.queenzone.org"
  nullable    = true
}

variable "manage_blob_service" {
  description = "Whether this module manages blob-service settings instead of using Azure's automatic default service."
  type        = bool
  default     = true
}

variable "blob_service_is_preexisting" {
  description = "Whether the default blob service predates this apply and must be managed as an imported resource. New StorageV2 accounts create this child automatically and must patch it instead."
  type        = bool
  default     = true
}

variable "containers" {
  description = "Live Blob container ACLs approved for import and migration. None means private."
  type        = map(string)
  default = {
    "album-or-single-covers"  = "Blob"
    "attachments"             = "Blob"
    "avatars"                 = "Blob"
    "brian-may"               = "Blob"
    "css"                     = "Container"
    "databasebackup"          = "None"
    "fan-art"                 = "Blob"
    "fan-pics"                = "Blob"
    "forum"                   = "Blob"
    "freddie-mercury"         = "Blob"
    "freddie-tribute-concert" = "Blob"
    "images"                  = "Blob"
    "john-deacon"             = "Blob"
    "miscellaneous"           = "Blob"
    "mp3"                     = "Blob"
    "pre-queen"               = "Blob"
    "queen"                   = "Blob"
    "queen-and-adam-lambert"  = "Blob"
    "queen-and-paul-rodgers"  = "Blob"
    "queen-memorabillia"      = "Blob"
    "roger-taylor"            = "Blob"
    "songfiles"               = "None"
    "special-events"          = "Blob"
    "test"                    = "Blob"
    "ugc-articles"            = "None"
    "ugc-avatars"             = "None"
    "ugc-forum"               = "None"
    "ugc-photos"              = "None"
    "us-convention-2001"      = "Blob"
  }

  validation {
    condition     = alltrue([for access in values(var.containers) : contains(["None", "Blob", "Container"], access)])
    error_message = "Container access must be None, Blob, or Container."
  }

  validation {
    condition     = var.containers["databasebackup"] == "None" && lookup(var.containers, "ugc-articles", "None") == "None" && var.containers["ugc-avatars"] == "None" && var.containers["ugc-forum"] == "None" && lookup(var.containers, "ugc-photos", "None") == "None" && var.containers["songfiles"] == "None"
    error_message = "Backup, modern UGC, and songfiles containers must remain private."
  }
}
