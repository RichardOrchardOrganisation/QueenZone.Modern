# ADR 0008: App Service Application Settings Stay Outside OpenTofu (Option A)

## Status

Accepted.

## Context

[Epic #615](https://github.com/RichardOrchardOrganisation/QueenZone.Modern/issues/615) is bringing the existing QueenZone Azure/Cloudflare estate under OpenTofu without redesigning production or risking secret exposure. [Issue #618](https://github.com/RichardOrchardOrganisation/QueenZone.Modern/issues/618) (OpenTofu 7/8) required a decision, before the Web App configuration map is imported in [#622](https://github.com/RichardOrchardOrganisation/QueenZone.Modern/issues/622), on who owns each App Service application setting.

AzureRM's `azurerm_linux_web_app` (and its predecessor) exposes `app_settings` as a single map attribute — there is no provider-native way to manage a subset of keys without OpenTofu becoming authoritative for the whole map on every apply. QueenZone currently splits setting ownership across four places that OpenTofu does not control:

- Bitwarden Secrets Manager (automation source of truth; local recovery via `bws`)
- Live App Service runtime settings (set through the portal/CLI today)
- GitHub Actions deployment/migration credentials (Bitwarden-backed repo secrets/variables)
- Committed non-secret application defaults (`appsettings.json` in the repo)

The live setting names are already inventoried, values excluded, in [`opentofu-inventory.md`](../architecture/opentofu-inventory.md#app-service-application-setting-names-values-not-recorded).

Three options were considered:

- **Option A — Initial coexistence.** OpenTofu ignores the App Service settings map entirely; the existing Bitwarden/operator workflow stays authoritative. Lowest migration risk; non-secret setting drift stays uncontrolled by OpenTofu.
- **Option B — Key Vault references.** OpenTofu manages a Key Vault, its access policies/RBAC, and App Service `@Microsoft.KeyVault(...)` reference URIs; secret values are populated outside OpenTofu. Larger migration (new resource, identity wiring, per-setting reference conversion); removes App Service secret duplication once complete.
- **Option C — Split control via a supported Azure resource/API pattern.** Manage non-secret settings declaratively while preserving externally owned secret settings. Rejected: no AzureRM resource/API exists today that safely manages a *subset* of `app_settings` without risking deletion or exposure of the unmanaged remainder, which violates the epic's "never place secret values in state / never delete externally owned settings" guardrails.

## Decision

Adopt **Option A** for this pass of the OpenTofu migration.

- The `azure-web` module ([#622](https://github.com/RichardOrchardOrganisation/QueenZone.Modern/issues/622)) will import the App Service plan and site, but its site resource will either omit the `app_settings` / `connection_string` attributes or set `lifecycle { ignore_changes = [app_settings, connection_string] }`, so OpenTofu never reads, writes, or stores current values for either.
- Ownership of every setting stays exactly where it is today — nothing moves as part of this ADR:

  | Category | Owner | Examples |
  | --- | --- | --- |
  | Committed non-secret application defaults | Repo (`appsettings.json`), deployed by the existing GitHub release path | Logging levels, feature toggles, static config not tied to an environment |
  | App Service runtime settings (non-secret, environment-specific) | Azure portal/CLI, operator-managed | `WEBSITE_HEALTHCHECK_MAXPINGFAILURES`, `WEBSITE_HTTPLOGGING_RETENTION_DAYS`, `Admin__AllowedEmails__0/1`, `Analytics__TrafficCacheMinutes`, `Analytics__GoogleAnalyticsPropertyId`, `SCM_DO_BUILD_DURING_DEPLOYMENT`, `ENABLE_ORYX_BUILD` |
  | App Service runtime settings (non-secret, ARM-owned by the deploy workflow) | `deploy.yml`'s `configure-app-settings` job, via ARM (`azure/login` on GitHub environment `prod-deploy`) — not OpenTofu, and predates this ADR (#666) | `WEBSITE_WARMUP_PATH`; `WEBSITE_WARMUP_STATUSES` and `WEBSITE_RUN_FROM_PACKAGE` must stay absent (see `opentofu-inventory.md`'s "ARM-owned non-secret deploy keys" section) |
  | App Service runtime settings (auto-managed by an imported Azure resource) | Azure, read-only from OpenTofu's perspective | `APPLICATIONINSIGHTS_CONNECTION_STRING` (populated by the App Insights component imported in #622) |
  | Bitwarden Secrets Manager / runtime secrets | Bitwarden `Queenzone Development` project; synced to App Service manually or via operator tooling. `MobileAuth__SigningKey` is the narrow exception reconciled by `deploy.yml` before each web deploy. | `ConnectionStrings__QueenZoneLegacy`, `ConnectionStrings__BlobStorage`, `OPENROUTER_API_KEY`, `AzureAd__ClientSecret`, `Authentication__*__Secret`, `Analytics__GoogleAnalyticsServiceAccountJson`, `MobileAuth__SigningKey` |
  | Deploy-only secrets | GitHub Actions repo secret/variable, Bitwarden-backed | `AZURE_WEBAPP_PUBLISH_PROFILE`, `QUEENZONE_LEGACY_MIGRATION_CONNECTION_STRING`, `QUEENZONE_SQL_EXPRESS_PROBE_PASSWORD` (see [`bitwarden-secrets.md`](../bitwarden-secrets.md)) |
  | Deprecated / to reconcile | Legacy raw GitHub secrets predating the Bitwarden migration | Tracked as "defer" in `opentofu-inventory.md`; reconcile or delete outside this ADR's scope |

- `opentofu-inventory.md`'s "App settings (names only)" row is updated from `defer → #618` to `outside → ADR 0008`; the name-only inventory remains the source for "what exists," not "what OpenTofu manages."
- Rotation and break-glass procedures for these settings are unchanged by this ADR: Bitwarden remains the automation/local-recovery source, Azure portal/CLI remains the live-value source, and GitHub Actions secrets are rotated through the existing `bitwarden/sm-action` flow. `MobileAuth__SigningKey` is reconciled automatically from Bitwarden to Azure by `deploy.yml`; operators still set it directly in Azure during break-glass. The full procedure, including break-glass when Bitwarden itself is unreachable, is documented in [`bitwarden-secrets.md`](../bitwarden-secrets.md#rotation-and-break-glass-app-service-settings).
- A missing-required-setting-name check is implemented: `scripts/Test-AppServiceSettingNames.ps1`, run nightly by `.github/workflows/app-service-setting-names-check.yml`, diffs the live setting *names* on `queenzone-prod` against `infra/import/github-bitwarden.json`'s `appServiceSettingNames` list. It only ever requests/compares names, never values.
- A separate CI plan check (tracked under [#625](https://github.com/RichardOrchardOrganisation/QueenZone.Modern/issues/625)) that fails an OpenTofu plan if it would touch `app_settings` or `connection_string` on the imported site remains a future guardrail, not yet built — the name-check above covers "missing names," not "an OpenTofu plan trying to manage the map."

## Consequences

Benefits:

- Lowest-risk path to importing the Web App in #622: no new Azure resources, no identity/RBAC wiring, no risk of an OpenTofu apply deleting or exposing a Bitwarden- or operator-managed setting.
- Every production setting keeps a single, already-documented owner (satisfies the #618 acceptance criterion); nothing changes hands mid-migration.
- No secret values enter OpenTofu configuration, plan output, or state, by construction — OpenTofu simply never touches the attribute.
- Unblocks the remaining import sequence (#622, #628, #626, #625) without waiting on a Key Vault migration.

Tradeoffs:

- Non-secret setting drift (e.g. `WEBSITE_HTTPLOGGING_RETENTION_DAYS` changed by hand in the portal) is not detected or prevented by OpenTofu. This is an accepted gap, not an oversight.
- Secret duplication between Bitwarden and live App Service settings continues; Option B's deduplication benefit is deferred.
- This is a checkpoint, not a permanent architecture: revisit Option B (Key Vault references) once the SQL/Storage imports (#628) and CI/drift tooling (#625) have proven the OpenTofu workflow stable, or sooner if non-secret drift becomes an operational problem.

## Follow-up

Open a new issue for Option B (Key Vault references) if/when App Service secret duplication or setting drift becomes a real operational cost. Until then, this ADR is the recorded answer to #618, and the `azure-web` module must not add `app_settings` or `connection_string` management without superseding this ADR.
