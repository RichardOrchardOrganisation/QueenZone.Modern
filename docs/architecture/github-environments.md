# GitHub Environments

Issue: [#1377](https://github.com/richardorchard/QueenZone.Modern/issues/1377) (Bob Architecture lock, Option B).

This is the durable map of GitHub Environments used by QueenZone Actions. Environments live only in GitHub Settings today; workflows reference them by exact name. Create and protect an environment **before** merging a workflow that first references it — GitHub auto-creates an unprotected environment on first use.

The four production environments exist (Gilfoyle / Delivery, #1377). Each uses **custom** deployment policies (`custom_branch_policies=true`, `protected_branches=false`). Environment **secret counts are 0 by design**: `BITWARDEN_SECRETS_MANAGER_ACCESS_TOKEN` stays a repository secret. Only environment **variables** (Bitwarden UUID→name maps, ARM IDs, Sentry) live on the environments.

## Why not rename `dev` / `deploy` in place

GitHub can rename an environment and keep its secrets and variables. That was the wrong move here:

- Legacy `dev` was **split**. It previously gated production migrate + zip deploy (`deploy.yml`), PR-time production Azure SQL writes (`ci.yml`), production reads that refresh the SQL Express mirror, and vestigial mirror-only probe jobs. One rename cannot express those four trust boundaries.
- Legacy `deploy` was **split**. It previously gated the production ARM/OIDC identity **and** Google Play signing/upload. Those credentials must not share an environment.

Those names are **deleted in Settings** (verified 2026-09-07). Workflows use `prod-release`, `prod-deploy`, `prod-google-play`, and `prod-data-read`. Do not recreate `dev` or `deploy`. See [Legacy environment retirement](#legacy-environment-retirement).

## Option B: PR-time Azure SQL writes stop

Pre-merge `ci.yml` `ef-migrations` applies pending EF migrations to the **SQL Express mirror** on the Windows self-hosted runner (`localhost\SQLEXPRESS` / `queenzone_legacy_sync`, Integrated Security). It has **no** production connection string and **no** production GitHub Environment.

Production `dotnet ef database update` stays on `deploy.yml` `migrate` (tag `v*` or manual dispatch from `main`), against real Azure SQL, before the zip deploy.

**Tradeoff.** SQL Express is not Azure SQL. Filegroup syntax, collation behaviour, and DTU-bound long DDL can pass on the mirror and fail later on the post-merge migrate. Those failures move later; they do not disappear. A non-production Azure SQL twin (Option C) is a deferred follow-up and must not block #1377.

## Production environments

| Environment | Purpose | Environment variables (confirmed) | Protection | Workflows / jobs |
| --- | --- | --- | --- | --- |
| `prod-release` | Production migrate + zip deploy | `BITWARDEN_APP_SERVICE_DEPLOY_SECRETS` maps only the Canada East publish profile, Canada East migration connection string, and `MOBILE_AUTH_SIGNING_KEY`. Repo-level Bitwarden token. | **Custom** branch `main` + tags `v*` | `deploy.yml` `migrate`; `deploy.yml` `deploy` |
| `prod-deploy` | ARM/OIDC App Service settings | `ARM_CLIENT_ID`, `ARM_TENANT_ID`, `ARM_SUBSCRIPTION_ID`, plus a narrow `BITWARDEN_APP_SERVICE_DEPLOY_SECRETS` mapping for `MOBILE_AUTH_SIGNING_KEY` only. No ARM vars on any other prod environment. | **Custom** branch `main` + tags `v*` | `deploy.yml` `configure-app-settings`; `app-service-setting-names-check.yml` |
| `prod-google-play` | Play signing / store upload | `BITWARDEN_MOBILE_BUILD_SECRETS` plus Sentry vars (`SENTRY_DSN`, `SENTRY_ORG`, `SENTRY_PROJECT`). Mapping yields Android keystore outputs, `SENTRY_AUTH_TOKEN`, and `GOOGLE_PLAY_SERVICE_ACCOUNT_JSON`. **No ARM.** | **Custom** branch `main` | `publish-android-google-play.yml` `publish-android` only |
| `prod-data-read` | Read production to refresh/resync the SQL Express mirror | Narrow Bitwarden map: Canada East publish profile + migration connection string **only** (not the probe-password / mobile-auth entries). The generic output aliases are retained for these workflows. Repo-level Bitwarden token. | **Custom** branch `main` (scheduled runs use the default branch) | `nightly-legacy-checks.yml` `sync-legacy-db`; `test-migrations-against-mirror.yml` `resync-mirror` only |

`SIXLABORS_LICENSE_KEY` and `BITWARDEN_SECRETS_MANAGER_ACCESS_TOKEN` stay **repository** secrets. Do not add environment secrets to these four environments unless a later split requires it.

Development App Service environments (`dev-migrate`, `dev-deploy`, `dev-data-refresh`) are unchanged. See [`dev-curated-snapshot.md`](dev-curated-snapshot.md) and [`opentofu-dev-environment.md`](opentofu-dev-environment.md).

## Jobs that must not use a production environment

These jobs touch only the SQL Express mirror, or use no environment secrets/vars:

| Workflow | Job | Why no prod environment |
| --- | --- | --- |
| `ci.yml` | `ef-migrations` | Hard-coded mirror connection string + `Assert-SqlExpressMirrorConnection.ps1`. No Bitwarden, no Azure SQL. |
| `nightly-legacy-checks.yml` | `legacy-read-probes` | Mirror over LAN. Bitwarden is used only for `QUEENZONE_SQL_EXPRESS_PROBE_PASSWORD` via **repository-level** token/mapping. |
| `nightly-legacy-checks.yml` | `apply-ef-migrations-mirror` | After Sync, before write probes (#1722). `localhost\SQLEXPRESS` Integrated Security + `Assert-SqlExpressMirrorConnection.ps1`. No environment secrets. |
| `nightly-legacy-checks.yml` | `legacy-write-probes` | `localhost\SQLEXPRESS` Integrated Security. No environment secrets. Waits on `apply-ef-migrations-mirror`. |
| `nightly-legacy-checks.yml` | `ui-e2e-realdata` | Windows: Integrated Security. macOS: repository-level Bitwarden probe password. |
| `nightly-legacy-checks.yml` | `residue-check` | Local mirror only. |
| `test-migrations-against-mirror.yml` | `test-migrations` | Local mirror Integrated Security. |

If the probe password later requires an environment-scoped Bitwarden token, create a **non-production** mirror environment. Never attach those jobs to `prod-data-read`.

## OpenTofu (deferred)

GitHub Environments are **not** managed in `infra/` today (`github_repository_environment` is unused). `opentofu-plan` / `opentofu-apply` already exist as environments, so a later import would have a bootstrap ordering problem: those workflows depend on the environments they would manage.

**Decision:** keep Settings/API as the source of truth for this split. Track OpenTofu ownership as a follow-up; do not block #1377 on it. Inventory rows for the new names live in [`opentofu-inventory.md`](opentofu-inventory.md).

## Legacy environment retirement

Issue [#1394](https://github.com/richardorchard/QueenZone.Modern/issues/1394). #1377 remapped workflows; Gilfoyle deleted the leftover Settings names. This repo slice records that — it does not gate Settings delete.

`gh api repos/richardorchard/QueenZone.Modern/environments` on 2026-09-07: **`dev` and `deploy` are absent**. Live names: `dev-data-refresh`, `dev-deploy`, `dev-migrate`, `opentofu-apply`, `opentofu-plan`, `prod-data-read`, `prod-deploy`, `prod-google-play`, `prod-release`.

1. Done. Exact workflow search on `main` is clean: no `environment: dev`, `environment: deploy`, `name: dev`, or `name: deploy` under `.github/workflows/` (kept `dev-migrate` / `dev-deploy` / `dev-data-refresh` and `deploy-dev.yml`).
2. Done. Tag `v2026.09.07.1` succeeded using `prod-release` + `prod-deploy`.
3. Done. `publish-android-google-play.yml` succeeded on `main` using `prod-google-play` (Play run `34095308960`).
4. Done. Nightly `sync-legacy-db` succeeded using `prod-data-read` (Nightly Sync `34096958201`).
5. Done. Gilfoyle deleted `dev` and `deploy` in Settings (API list above) and removed the Entra FIC subject `environment:deploy`. If either environment name reappears, a stale workflow auto-created it — delete it again in Settings; flip this sentence only.

## Required status check rename

`ci.yml` job display name changed from `EF migrations (Azure SQL)` to `EF migrations (SQL Express mirror)`. If that string is a required check on `main`, update branch protection to the new name when this workflow reaches `main`.
