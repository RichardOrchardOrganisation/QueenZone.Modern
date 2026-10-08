# Azure data module

This module owns the Azure SQL server/database, optional explicit firewall
rules, extended auditing, Storage account, Blob protection settings, and
approved containers. `create_azure_services_firewall_rule` still defaults to
the imported `AllowAllWindowsAzureIps` rule for `queenzone-sql-server`.
`queenzone-prod-sql` sets that flag false and passes App Service outbound
addresses instead. Turning the flag off drops the instance count to zero.
`lifecycle.destroy = false` forgets that state address instead of deleting
the Azure rule. `prevent_destroy` stays set.

SQL server-level extended auditing is off unless
`sql_extended_auditing_enabled` is set. That flag owns the server policy and
the `master` diagnostic setting only. Database-level auditing is a separate
opt-in (`sql_database_extended_auditing_enabled`, default false) because the
server-level policy already covers the user database. Production Canada East
keeps server audit on and the database copy off (#2204). Azure still reports
`log_monitoring_enabled` on that disabled database policy; the module keeps
that flag on whenever server audit is on so a production plan is a no-op.
When enabled, both paths point at `queenzone-prod-law`. Retention follows that workspace
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
`sql-security-audit` for `SQLSecurityAuditEvents` on `master` when server
auditing is enabled, and the matching database setting only when
`sql_database_extended_auditing_enabled` is true. Dropping that database
setting's count to zero sets `lifecycle.destroy = false` so OpenTofu forgets
the state address instead of failing `prevent_destroy`. No stack-owned RBAC
assignments exist.

Blob and container soft delete remain seven days. Versioning, change feed,
point-in-time restore, and lifecycle rules remain disabled or absent. This is a
cost-neutral first import, not a protection-policy expansion.

`databasebackup`, `ugc-articles`, `ugc-avatars`, `ugc-forum`, `ugc-photos`,
`songfiles`, and legacy `attachments` stay private in this module. Fan audio is
only readable through the member-authenticated app proxy
(`/fan-performances/{id}/audio`). Legacy forum files are only readable through
`/forum/attachment/legacy/{postId}` (#1656). Published gallery containers stay
public blob, and `css` stays public container access (legacy site CSS, not
member uploads). The live `test` container remains public blob access and is
preserved for the production region migration.

`songfiles` was set to `None` on 2026-08-16 via ARM after #702 deployed. The
`attachments` desired ACL is `None` in this module; the live container stays
public blob until a reviewed apply. Do not apply this stack from a local
operator session. A later reviewed apply should set `attachments` to `None`
and leave `songfiles` unchanged.
