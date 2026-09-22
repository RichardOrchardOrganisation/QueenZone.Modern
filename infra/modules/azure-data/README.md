# Azure data module

This module owns the imported Azure SQL server/database, the Azure-services
firewall rule, disabled auditing settings, Storage account, Blob protection
settings, and approved containers. ARM requires the existing SQL administrator
name in the server resource, but its password remains external. Database schema,
SQL principals, connection strings, and blob objects remain outside OpenTofu.

Storage uses AzAPI so the provider never calls `listKeys` or exports generated
account keys and connection strings into state. The resources export IDs only.

The SQL database remains Basic 5 DTU with a 2 GB limit, 7-day LRS short-term
retention, and no long-term retention. The personal workstation firewall rule
is outside this stack. No diagnostic settings or stack-owned RBAC assignments
exist, so none are invented.

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
