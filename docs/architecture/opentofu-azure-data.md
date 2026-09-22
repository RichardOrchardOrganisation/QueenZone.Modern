# OpenTofu Azure data import

Issue: [#628](https://github.com/richardorchard/QueenZone.Modern/issues/628),
step 5 of epic [#615](https://github.com/richardorchard/QueenZone.Modern/issues/615).

## Managed boundary

Production now uses `queenzone-db` on the Canada East logical server
`queenzone-prod-sql` and Blob Storage account `queenzoneprod`. The original
Australia East production database was removed from state and deleted on
**14 September 2026**. Its logical server remains protected because it hosts
`queenzone-dev-db`. The original Storage account was removed from state and
deleted on **15 September 2026** after content, dependency, no-write, deployment,
and post-deletion health checks passed.

The production root retains imports for the existing Australia East SQL logical
server, its Azure-services firewall rule, and its server auditing setting. It
no longer declares the old production database or the old Storage account.

`queenzone-prod-sql` does not manage `AllowAllWindowsAzureIps`. The rule
resource sets `lifecycle.destroy = false`, so dropping its count to zero
forgets the state address and leaves the Azure rule in place. Explicit
firewall rules allow the possible outbound IPs of `queenzone-prod`. Delete
the live `0.0.0.0` rule only after those rules are applied, `/health/ready`
still passes, and GitHub-hosted migration runners have their own path. Public
network access stays enabled for those clients. `queenzone-sql-server` keeps
`AllowAllWindowsAzureIps` because `queenzone-devbox` still reaches
`queenzone-dev-db` through it, and this root does not know that app's
outbound addresses.

Server and database extended auditing is enabled to `queenzone-prod-law`.
Diagnostic settings send `SQLSecurityAuditEvents` from `master` and from
`queenzone-db`. Retention is the workspace's 30 days. The auditing policy's
`retention_in_days` stays 0 because that field is storage-account retention,
and this stack does not put a storage key in state. Entra-only authentication
stays off: `ignore_changes = [azuread_administrator]` would drop that flip,
and the app connection string is still SQL authentication.

OpenTofu records the existing SQL server administrator name because ARM requires
it, but does not manage its password. The write-only administrator password
stays ephemeral. Database principals, schema, EF migrations, tables, procedures,
rows, blob objects, and the operator workstation firewall rule remain outside
the stack. No stack-owned RBAC assignments exist.

## Secret-free Storage state

The AzureRM Storage account resource exports account keys and connection
strings even when configuration does not reference them. This violates the
stack's no-secrets-in-state boundary. Storage is therefore managed through
AzAPI ARM resources with empty response exports. No `listKeys` action exists in
the configuration; module outputs expose the Storage resource ID only.

## Recovery and retention

Azure SQL remains Basic 5 DTU, 2 GB, with locally redundant backup storage and
seven days of point-in-time restore retention. Differential backups run every
24 hours. Long-term retention is disabled. These are the current low-cost
recovery controls; #596 can assess stronger recovery separately.

Blob and container soft delete remain enabled for seven days. Blob versioning,
change feed, point-in-time restore, and lifecycle management remain disabled or
absent. The first import deliberately avoids new storage cost or retention.

## Container ACL decision

The imported ACLs match live product behaviour:

- `databasebackup`, `ugc-articles`, `ugc-avatars`, `ugc-forum`, `ugc-photos`, `songfiles`, and legacy `attachments` are private;
- archive/media containers retain public blob access;
- `css` retains public container access (published site CSS, not member uploads);
- `test` retains public blob access and is preserved through the production
  region migration.

Live `songfiles` is already private (ARM apply 2026-08-16 after the #702
app proxy shipped). The module desired state is `None`. The next reviewed
OpenTofu apply should not change that ACL. `prevent_destroy` does not block
container ACL changes; do not flip it back to public.

Legacy `attachments` desired state is `None` (#1656). The live container is
still public blob until that reviewed apply. The app streams the file after
member auth; it does not redirect to `cdn2`. Gallery containers
(`freddie-mercury`, album covers, and the rest) stay public blob.

## Verification contract

Keep plan files outside the repository and report resource actions only. A
valid first plan must show imports with no Azure property create, update,
replacement, deletion, or ACL change. AzAPI may report state-only updates to its
computed `output` after imports because response exports are deliberately empty.
Live checks must cover public CDN media, raw public ACL behaviour,
private-container denial, and read-only SQL connectivity.
