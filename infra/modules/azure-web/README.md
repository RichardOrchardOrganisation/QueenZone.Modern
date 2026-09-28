# Azure web module

This module owns the imported production App Service plan, Linux web app,
workspace-linked telemetry, custom hostname bindings, TLS configuration, and
the existing Cloudflare-only main-site ingress policy.

When `allow_direct_access` is false, the main site allows one Cloudflare
prefix per rule and denies everything else. The prefix list was verified on
2026-09-28 against the published Cloudflare IPv4 and IPv6 pages. Azure has no
Cloudflare service tag, so the module does not use `AzureFrontDoor.Backend`.
Dev and migration apps that allow direct access omit that list and keep the
main-site default at Allow.

Production SCM has a separate restriction list with default Deny. The release
workflow signs in through the existing `prod-release` OIDC federation, grants
its current runner IPv4 `/32` during zip deploy and Kudu restart, and removes
the rule even when deployment fails. A cancelled runner can leave its rule
behind; check for and remove stale `github-deploy-*` SCM rules before applying
the next release. The publish profile still authenticates the deploy and Kudu
restart, so WebDeploy basic publishing credentials remain enabled. FTP basic
publishing credentials remain enabled until that path is separately audited.
Dev and migration apps keep SCM default Allow for direct development access.

Uploaded App Service certificate resources remain outside OpenTofu. AzureRM
cannot describe them without the private PFX material, and their renewal path
has not been confirmed. The hostname resources retain the current SNI state and
certificate thumbprints without putting certificate secrets in state.

Every irreplaceable resource must include `lifecycle { prevent_destroy = true }`.
Do not use broad `ignore_changes`; record each externally owned attribute and
its reason. OpenTofu never manages `app_settings` or `connection_string` under
[ADR 0008](../../../docs/decisions/0008-app-service-settings-ownership.md).
The site therefore ignores `app_settings` and omits the unused
`connection_string` collection.

## Production alerts (#1805)

When `environment_name` is `production`, this module also owns:

- the imported action group `queenzone-alerts` (existing email receiver kept;
  no webhook; receivers, location, and short name are ignored so import cannot
  replace the live group);
- six Log Analytics scheduled-query rules scoped to `queenzone-prod-law`
  (`qz-prod-server-5xx`, `qz-prod-exception-new-problem`,
  `qz-prod-exception-spike`, `qz-prod-dependency-failures`,
  `qz-prod-ingestion-cap`, and disabled `qz-prod-request-p95`);
- standard web test `qz-prod-health` against `https://www.queenzone.org/health`;
- metric alert `qz-prod-availability` that fires when two of three locations fail.

All rules auto-resolve. AzureRM 5.0.1 cannot combine auto-resolve with a
60-minute mute, so log rules do not set `mute_actions_after_alert_duration`.
The new-problem rule uses `query_time_range_override = P2D` with a one-hour
window so the KQL can see the prior 47 hours. Dev and migration instantiations
create none of these resources.
