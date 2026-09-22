# Azure data module

This module owns the Azure SQL server/database, optional explicit firewall
rules, extended auditing, Storage account, Blob protection settings, and
approved containers. `create_azure_services_firewall_rule` still defaults to
the imported `AllowAllWindowsAzureIps` rule for `queenzone-sql-server`.
`queenzone-prod-sql` sets that flag false and passes App Service outbound
addresses instead. Turning the flag off drops the instance count to zero.
`lifecycle.destroy = false` forgets that state address instead of deleting
the Azure rule. `prevent_destroy` stays set.

SQL extended auditing is off unless `sql_extended_auditing_enabled` is set.
Production points it at `queenzone-prod-law`. Retention follows that workspace
(30 days). Do not add a storage-account audit destination. `public_network_access_enabled`
stays true, and `azuread_authentication_only` stays false while
`ignore_changes = [azuread_administrator]` is set and the app still uses SQL
authentication.

ARM requires the existing SQL administrator name in the server resource, but
its password remains external. Database schema, SQL principals, connection
strings, and blob objects remain outside OpenTofu.

Storage uses AzAPI so the provider never calls `listKeys` or exports generated
account keys and connection strings into state. The resources export IDs only.

The SQL database remains Basic 5 DTU with a 2 GB limit, 7-day LRS short-term
retention, and no long-term retention. The personal workstation firewall rule
is outside this stack. The only diagnostic settings this module creates are
`sql-security-audit` for `SQLSecurityAuditEvents` when auditing is enabled.
No stack-owned RBAC assignments exist.

Blob and container soft delete remain seven days. Versioning, change feed,
point-in-time restore, and lifecycle rules remain disabled or absent. This is a
cost-neutral first import, not a protection-policy expansion.

Container ACLs match the live estate. `databasebackup`, `ugc-articles`,
`ugc-avatars`, `ugc-forum`, `ugc-photos`, and `songfiles` stay private. Fan audio is only readable through
the member-authenticated app proxy (`/fan-performances/{id}/audio`). Legacy
`attachments` remain public blob access (out of scope for #177). The live
`test` container remains public blob access and is preserved for the production
region migration. `ugc-photos` and `ugc-articles` are private.

`songfiles` was set to `None` on 2026-08-16 via ARM after #702 deployed. The
OpenTofu apply workflow (#625) is not built yet; do not apply this stack from a
local operator session. A later reviewed apply should leave the ACL unchanged.
