# OpenTofu CI, drift detection, and operational runbooks

Issue: [#625](https://github.com/RichardOrchardOrganisation/QueenZone.Modern/issues/625), step 8 of epic [#615](https://github.com/RichardOrchardOrganisation/QueenZone.Modern/issues/615).

This is the CI-facing companion to
[`opentofu-contributor-runbook.md`](opentofu-contributor-runbook.md) (local
validation, provider upgrades, first-import, state moves) and
[`opentofu-state-and-identity.md`](opentofu-state-and-identity.md) (backend,
identities, lock recovery, backup/restore, credential rotation). It does not
repeat that content — it explains how the automated workflows fit together,
gives rollback guidance by resource class, and links disaster recovery.

## Cloudflare plan/apply tokens

Every plan or apply touches `module.cloudflare_edge` alongside the Azure
modules, since the production root plans them together. Both tokens
(`CLOUDFLARE_API_TOKEN_TOFU_PLAN` / `_APPLY`, scoped read-only for plan and
edit for apply — never the Global API Key) were created and wired on
2026-09-01, per `opentofu-state-and-identity.md`'s "Cloudflare token
strategy": stored in the `Queenzone Development` Bitwarden project, with the
`BITWARDEN_OPENTOFU_PLAN_SECRETS` / `BITWARDEN_OPENTOFU_APPLY_SECRETS`
repository variables mapping each to `CLOUDFLARE_API_TOKEN`
([`bitwarden-secrets.md`](../bitwarden-secrets.md) has the exact format).

During the staged #1272 production-region migration, both mappings also expose
the existing `QUEENZONE_LEGACY_MIGRATION_CONNECTION_STRING` secret. The
workflows extract its SQL password into an ephemeral OpenTofu variable through
[`Export-EphemeralSqlAdminPassword.ps1`](../../scripts/Export-EphemeralSqlAdminPassword.ps1).
The AzureRM write-only password field sends it to Azure without persisting it
in the plan or state.

If either token is ever missing or revoked, every workflow below fails fast
at an explicit "`CLOUDFLARE_API_TOKEN` is not configured" step rather than
hanging or failing unpredictably later.

## How the workflows fit together

| Workflow | Trigger | Identity | Can it change production? |
| --- | --- | --- | --- |
| [`opentofu-plan.yml`](../../.github/workflows/opentofu-plan.yml) | Pull request touching `infra/**` | `opentofu-plan` (Reader) | No — read-only plan, posts a PR comment |
| [`opentofu-apply.yml`](../../.github/workflows/opentofu-apply.yml) | Push to `main` touching `infra/**`, or manual dispatch | `opentofu-plan` for its own `plan` job, then `opentofu-apply` (Contributor) for `apply` | Yes, only after the required reviewer approves the `opentofu-apply` environment |
| [`opentofu-drift.yml`](../../.github/workflows/opentofu-drift.yml) | Daily schedule, or manual dispatch | `opentofu-plan` (Reader) only | No — structurally incapable of applying |
| [`deploy-dev.yml`](../../.github/workflows/deploy-dev.yml) migrate | Push to `main`/`master`, or manual dispatch | `dev-migrate`; Bitwarden mapping targets `queenzone-dev-db` | Skipped until App Service `DevSnapshot__Ready=true`; dev schema only and never production |
| [`refresh-dev-snapshot.yml`](../../.github/workflows/refresh-dev-snapshot.yml) | Manual confirmation plus `dev-data-refresh` approval | Separate production-read-only and dev-write SQL/Blob credentials | Reads production; resets and populates dev only |
| [`deploy-dev.yml`](../../.github/workflows/deploy-dev.yml) configure/deploy | Push to `main`/`master`, or manual dispatch | `dev-deploy`; OIDC Website Contributor scope is limited to `queenzone-devbox` | Dev App Service only; never production |
| [`deploy.yml`](../../.github/workflows/deploy.yml) | Push of a `v*` promotion tag, or manual dispatch from `main` | `prod-release` for migration/deployment; `prod-deploy` for ARM settings | Yes — Canada East production migration, `queenzone-prod` deployment, and smoke checks |

The dev application runs with deterministic sample data while
App Service `DevSnapshot__Ready` is absent or false. The curated refresh sets it only after
the schema, sampled data, search rebuild, privacy/size guards, public smoke, and
browser checks pass. See [`dev-curated-snapshot.md`](dev-curated-snapshot.md).

`opentofu-plan.yml`'s `fmt-validate` job runs for every PR, including forks,
with no cloud credentials at all (`scripts/Test-OpenTofu.ps1`), triggered by
the plain `pull_request` event. Its `plan` job only runs for same-repository
PRs, triggered by `pull_request_target` instead: the `opentofu-plan`
environment's deployment branch policy only trusts protected branches, and
for a plain `pull_request` event `github.ref` is the PR's own (unprotected)
feature branch — that combination blocks the job outright before any step
runs (confirmed the hard way on the PR that introduced this workflow).
`pull_request_target` runs with `github.ref` set to the protected base
branch, satisfying that policy, at the cost of an explicit checkout of the
PR head SHA in the job itself (its implicit default checkout is the base
branch, not the PR branch).

`opentofu-apply.yml`'s `apply` job applies the **exact binary plan artifact**
produced by its own `plan` job in the same run — never a freshly generated
plan — so "apply only a plan produced from the reviewed commit" holds by
construction, not by convention.

Concurrency is set **per job**, not per workflow: `plan` has its own group
(`opentofu-apply-plan`, `cancel-in-progress: true`) since it's read-only and
always safe to preempt with a newer run; `apply` has a separate group
(`opentofu-apply`, `cancel-in-progress: false`) since two applies must never
race, and the AzureRM backend's blob lease would block a second writer
regardless. This split exists because a shared group originally blocked a
*new run's read-only plan job* behind an old run's `apply` job sitting
unapproved at the reviewer gate — confirmed the hard way on 2026-09-03. The
split only fixes the plan-stage case: a genuinely abandoned, unapproved
`apply` run still correctly blocks a new `apply` from starting (that's
intentional), and GitHub Actions has no built-in timeout for pending
environment approvals, so an abandoned `apply` run still needs a human to
explicitly reject or cancel it (`gh run cancel <id>`, or Cancel workflow in
the Actions UI) before a later run's `apply` job can proceed.

This is entirely separate from the tag-triggered
[`deploy.yml`](../../.github/workflows/deploy.yml) production release
(EF migrations and App Service zip deploy). Neither workflow triggers the
other, and they use different GitHub environments and different OIDC
identities. The maintainer promotes only a commit already verified by
`deploy-dev.yml`; the exact command sequence is in the README deployment
section.

## Provider/OpenTofu upgrades in CI

The upgrade *procedure* (one provider per PR, refresh checksums, refresh-only
plan first) is unchanged and lives in `opentofu-contributor-runbook.md`. What
CI adds: a version-bump PR runs through the same `opentofu-plan.yml` as any
other infra change, so the resulting plan is reviewed and posted as a PR
comment before merge, and the resulting apply goes through the same
approval-gated `opentofu-apply.yml` as any other change — there is no
separate "upgrade" pipeline.

## Rollback guidance by resource class

OpenTofu has no built-in "rollback" — a bad apply is undone by applying a
corrected configuration, not by reverting state. What "corrected
configuration" means differs by resource class:

- **Azure web/telemetry (`azure-web` module)** — revert the PR on `main` and
  let the next `opentofu-apply` run plan the revert. `prevent_destroy` means
  a revert that would delete the web app, service plan, or hostname bindings
  fails closed instead of applying; if that happens, hand-edit the reverted
  configuration to match the resource's actual current state instead of
  deleting-and-recreating.
- **Azure SQL/Storage (`azure-data` module)** — same revert-and-replan
  approach for configuration (SKU, auditing, container ACLs). OpenTofu never
  owns schema or row/blob data, so a bad apply here cannot corrupt content —
  only settings. `prevent_destroy` blocks the database and storage account
  from ever being destroyed or replaced through this path.
- **Cloudflare edge (`cloudflare-edge` module)** — DNS, zone settings,
  Worker scripts/routes: revert-and-replan, same as above. A DNS or proxy
  misconfiguration is the highest-blast-radius category (can take the public
  site offline or expose the Azure origin directly) — if a revert-and-replan
  is not fast enough, the state-and-identity doc's break-glass dashboard
  process is the immediate mitigation, reconciled back into OpenTofu
  afterward as its own PR.
- **App Service application settings** — outside OpenTofu entirely per
  [ADR 0008](../decisions/0008-app-service-settings-ownership.md). Rotate/fix
  directly via `az webapp config appsettings set` or the Bitwarden/App
  Service flow in `bitwarden-secrets.md` — there is no Tofu apply/plan step
  to run or wait on for these.

## Cutover / post-apply verification

`opentofu-apply.yml`'s `post-apply-smoke` job runs
[`scripts/Test-OpenTofuPostApplySmoke.ps1`](../../scripts/Test-OpenTofuPostApplySmoke.ps1)
after every apply: the general route suite (`Smoke-LiveSite.ps1`), a direct
Azure origin check (`GET /health` on `queenzone-prod.azurewebsites.net` must
return 403), `/health/ready` reachability, the `cdn2.queenzone.org/songfiles/*`
and `cdn2.queenzone.org/attachments/*` → 404 contracts, and a Cloudflare-proxy
reachability check on `cdn.queenzone.org`. Application Insights freshness is checked best-effort
and never blocks the workflow.

A failure in this job does not undo the apply (see Rollback guidance above);
it fails the workflow loudly so it is investigated immediately rather than
discovered later.

## Drift detection

`opentofu-drift.yml` runs `tofu plan -detailed-exitcode` daily using the
Reader-only `opentofu-plan` identity — it can report drift but cannot
apply. On detected drift it opens (or comments on) a GitHub issue labeled
`opentofu-drift` with the redacted summary and a link to the run, so drift is
an actionable, visible item rather than a workflow log nobody reads. Not
every drift is a problem: a deliberate break-glass dashboard change (state-
and-identity doc) will show up here too — the response is to either
reconcile it into a PR or document why it's intentional, not to
auto-suppress the alert.

## Disaster recovery

Issue [#596](https://github.com/RichardOrchardOrganisation/QueenZone.Modern/issues/596)
(production disaster-recovery / restore runbook, RPO/RTO) is **open and
undecided** as of this writing — there is no finished DR document to link to
yet. What exists today: OpenTofu's own state backup/restore and lock
recovery procedures in `opentofu-state-and-identity.md`, which cover the
*infrastructure control plane* (recreating/reconciling Azure and Cloudflare
resource configuration) but explicitly not SQL data, blob contents, or
application-level recovery. When #596 is resolved, this section should link
directly to the resulting `docs/architecture/disaster-recovery.md` and note
which parts of a real incident OpenTofu's runbooks cover versus which parts
that document covers.

## What actually happened on the first real apply

The first real `opentofu-apply.yml` run against production happened
2026-09-03. What follows is what was actually found and fixed getting there,
kept as a reference for the next time a change ripples this widely.

- The `opentofu-plan` identity's `Reader` role didn't cover
  `Microsoft.Web/sites/config/list/action` (a "list" action Azure gates
  separately from `*/read`), so the very first plan failed reading the web
  app's auth settings. Fixed by adding a minimal custom role — see
  `opentofu-state-and-identity.md`'s identity table.
- `actions/upload-artifact` doesn't preserve a single file's full relative
  path the way the workflow assumed, so the plan artifact landed in the
  wrong directory and `apply` couldn't find it. Fixed in
  `opentofu-apply.yml`'s download step.
- The plan's redacted safety summary bucketed *any* resource with an
  `import {}` block as "import," even when it also carried real `update` (or
  worse) actions — silently hiding real changes behind a benign-looking
  label. In this case the hidden changes were themselves benign (import-time
  reconciliation, not anything destructive), including PR #892's
  `ip_restriction_default_action = "Deny"` finally taking effect, but the
  masking itself is a real gap worth knowing about if you're relying on the
  plan summary to judge risk before approving.
- `azurerm_linux_web_app.production`'s `app_settings` was never assigned in
  config (only referenced in `ignore_changes`, per ADR 0008), so OpenTofu's
  plan renderer printed the full live map — every secret in it, in
  plaintext — as unchanged context whenever any other attribute on the
  resource changed, which happens on every import. Real secrets leaked into
  several GitHub Actions run logs before this was caught; those runs were
  deleted and the exposed credentials rotated. Fixed by marking the value
  `sensitive({})`. See the "Known plan/apply quirks" section below —
  this class of bug can recur on any resource with a similar map/dict
  attribute.
- Config for the SQL database (`sku_name`/`max_size_gb`) and one storage
  account's `requireInfrastructureEncryption` had drifted from the live
  resources (config predates this stack having ever been applied for real).
  Fixed by correcting config to match live reality — see "Known plan/apply
  quirks" for the general principle.
- `azurerm_role_assignment.mobile_publisher` failed with "doesn't support
  update" — Azure RBAC role assignments are immutable, and the provider has
  no update implementation at all for this resource type. Fixed with
  `ignore_changes` on the one create-time-only attribute that had no live
  value to reconcile against.
- `Test-OpenTofuPostApplySmoke.ps1` had two independent bugs surface on the
  same run: header extraction failed on non-2xx responses under `pwsh`
  (`HttpResponseHeaders` doesn't support `["Name"]` indexer syntax the way
  the success-path headers or PS 5.1's `WebHeaderCollection` do), and a
  stale `$LASTEXITCODE` from the non-blocking Application Insights check
  leaked through to fail the whole step even after every check reported OK.
  Both fixed in the script.

## Known plan/apply quirks

- **`azapi_resource` always shows as "changing," even with zero real
  drift.** Its `output` and `sensitive_body` fields are write-only/computed
  and get re-diffed on every plan regardless of whether the actual `body`
  differs. Before treating an `azapi_resource` "N to change" as a real
  change, check whether `body` is in the diff's unchanged-attributes-hidden
  count — if so, it's this artifact, not a real change.
- **The `queenzoneprod` storage account's custom domain and its Cloudflare proxy
  fight each other.** Azure only verifies a storage account's `customDomain`
  CNAME at the moment of a PUT to that resource (which `azapi_resource`
  issues on *any* drift, not just custom-domain changes), but
  `cloudflare_dns_record.cdn` is configured `proxied = true` permanently.
  Any `opentofu-apply` that touches this storage account while the record is
  proxied will fail domain verification. To force a change through: un-proxy
  (grey-cloud) `cdn.queenzone.org` in the Cloudflare dashboard, wait for DNS
  to resolve directly to `queenzoneprod.blob.core.windows.net` (verify with
  `nslookup cdn.queenzone.org 1.1.1.1` — it should NOT return Cloudflare
  anycast IPs), apply, then re-proxy. Once bound, the verification doesn't
  need to happen again until the next PUT to this resource.
- **If apply fails with an "immutable"/"doesn't support update" error, check
  live reality before assuming config is right.** Several 2026-09-03
  failures (SQL `max_size_gb`, a storage account's
  `requireInfrastructureEncryption`, the mobile-builds role assignment) were
  all config that had silently drifted from, or never matched, the actual
  live resource — not bugs in the resources themselves. `az <resource> show`
  the live value first; the fix is usually correcting config to match
  reality, or `ignore_changes` for genuinely create-time-only attributes
  with no live representation to reconcile against.
