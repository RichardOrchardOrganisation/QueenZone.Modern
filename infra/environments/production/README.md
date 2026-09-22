# QueenZone production OpenTofu root

This root module is the only production entry point. The resource group,
Azure web/telemetry estate, Azure data estate, and Cloudflare edge estate all
use declarative import blocks from #622, #628, and #626.

The first remote plan must show imports and no unexplained change, replacement,
or deletion. Do not apply from a local operator session. The protected
`opentofu-apply` environment remains the only apply path.

Use [`scripts/Test-OpenTofu.ps1`](../../../scripts/Test-OpenTofu.ps1) for local validation. See [`docs/architecture/opentofu-contributor-runbook.md`](../../../docs/architecture/opentofu-contributor-runbook.md) before planning, importing, moving state, or applying.

## Phase 7 staged migration

Issue #1272 moved production to `canadaeast`. After four days of verified
operation, the maintainer accepted the observation window on 14 September
2026 and approved staged retirement of the old estate. The production root no
longer manages the old web resources or old production database. It retains
the Australia East SQL server because the dev environment still uses
`queenzone-dev-db`. The old Storage account was removed from state and deleted
on 15 September 2026 after its final manual retirement checks passed.

The SQL administrator password comes from the existing Bitwarden migration
connection-string secret. OpenTofu passes it through the AzureRM provider's
write-only field; it is ephemeral and cannot enter the plan or state.

Follow
[`production-region-migration.md`](../../../docs/architecture/production-region-migration.md)
for the phased apply, copy, verification, cutover, and retirement gates.

## SQL firewall and auditing

`queenzone-prod-sql` allows the possible outbound IPs of `queenzone-prod` and
does not manage `AllowAllWindowsAzureIps`. `lifecycle.destroy = false` forgets
that rule from state and leaves the Azure object in place. After apply, confirm
`/health/ready`, then delete the live `0.0.0.0` rule only once GitHub-hosted
migration runners have a firewall path of their own. `queenzone-sql-server`
keeps its Azure-services rule for `queenzone-dev-db`.

Auditing writes `SQLSecurityAuditEvents` to `queenzone-prod-law` (30-day
workspace retention). Entra-only SQL authentication is not part of this
change.
