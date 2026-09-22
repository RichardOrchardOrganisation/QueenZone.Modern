# OpenTofu live-estate inventory and ownership boundaries

Issue: [#624](https://github.com/richardorchard/QueenZone.Modern/issues/624) (OpenTofu 1/8 under epic [#615](https://github.com/richardorchard/QueenZone.Modern/issues/615)).

**Audit date:** 2026-08-12  
**Settings / GitHub refresh:** 2026-08-24 — added the four live APNs App Service setting names created for [#846](https://github.com/richardorchard/QueenZone.Modern/issues/846) and the Android FCM setting names for [#847](https://github.com/richardorchard/QueenZone.Modern/issues/847). GitHub environment names, Azure/Cloudflare resource IDs, storage ACLs, and `cdn`/`cdn2` probes were not re-run.

**#1394 GitHub Environment refresh:** 2026-09-07 — legacy `dev` / `deploy` **deleted in Settings** (`gh api …/environments`: neither name present). Workflows already use `prod-*`. Azure/Cloudflare IDs were not re-probed.

**#1272 storage refresh:** 2026-09-09 — the live source has 29 containers. `test`, `ugc-articles`, and `ugc-photos` were added to the managed inventory so every source container is preserved during the Canada East migration.

**#177 live refresh:** 2026-08-16 — `songfiles` ACL set to `None` via ARM; Worker `pictures-queenzone-org` published on `cdn2.queenzone.org/*` (404 `/songfiles/*`).  
**Method:** read-only Azure CLI/`az` against subscription `Base Subscription`, live HTTP/DNS probes of public hostnames, Cloudflare API (tokens `CLOUDFLARE_API_TOKEN_READONLY` and `CLOUDFLARE_WORKER_READWRITE` from Bitwarden — values not recorded), GitHub API for environments/secret *names*, Bitwarden Secrets Manager key *names* only.  
**Mutations performed:** none during the 2026-08-12 audit. 2026-08-16 applied the #177 ACL and Worker publish outside OpenTofu.

Sanitised machine-readable IDs and import hints live under [`infra/import/`](../../infra/import/).

## Dev environment (infrastructure and application deployment verified 2026-09-05)

Issue #1265 adds `infra/environments/dev` with a separate `dev.tfstate`.
These resources are **provisioned and verified** in `australiaeast`:

| Resource | Name |
| --- | --- |
| Resource group | `Queenzone-Dev-RG` |
| Linux B1 plan, one worker | `ASP-Queenzone-Dev` |
| Always-on web app | `queenzone-devbox` |
| Log Analytics | `queenzone-devbox-law` |
| Application Insights | `queenzone-devbox-ai` |

The [approved dev-only apply](https://github.com/richardorchard/QueenZone.Modern/actions/runs/33847788695)
succeeded after the resource-group bootstrap. Azure confirmed B1, one worker,
Always On, .NET 10 and HTTPS-only. The default Azure hostname returned HTTP 200
with its welcome page; a fresh remote-state plan returned no changes.

Production's Canada East `queenzone-prod` App Service remains separate. Phase 2 provisioned
an empty `queenzone-dev-db` database on the Australia East
`queenzone-sql-server` and the separate `queenzonedev` storage account in
the dev state. They are not imports and contain no production data. While
separate work completes the legacy database baseline, dev runs the deployed
application with deterministic sample data and skips EF migrations. Phase 3
decommissioned the unused `queenzone-dev` Static Web App and its
APK-distribution workflow. The production Cloudflare root now declares the
`dev.queenzone.org` CNAME at
`queenzone-devbox.azurewebsites.net`; it retains the same resource address and
`prevent_destroy` protection. The dev hostname binding and Azure-managed
certificate are applied; `https://dev.queenzone.org` serves the application
and has passed repeated deployment warmup and public/API smoke checks. See
[#1267](https://github.com/richardorchard/QueenZone.Modern/issues/1267) and the
[dev provisioning runbook](opentofu-dev-environment.md) for the required apply
order and verification steps. The CNAME remains DNS-only so Azure can renew its
managed certificate; dev permits direct ingress while production remains
Cloudflare-only.

## Scope boundaries

| In scope for OpenTofu adoption | Out of scope / never OpenTofu |
| --- | --- |
| Azure resources in `Queenzone-RG` | Application schema/data (EF migrations, SQL content) |
| Cloudflare zone DNS, TLS mode, Workers/routes for QueenZone hostnames | DNS registrar ownership / domain purchase |
| App Service hostname bindings, access restrictions, plan SKU | Secret *values* (connection strings, publish profiles, OAuth secrets, OpenRouter keys) |
| Storage account settings, container public-access flags, soft-delete | Blob object contents |
| Application Insights + Log Analytics caps/retention, alerts | GitHub Actions workflow *logic* (stays in `.github/workflows`) |
| Declaring *which* App Service setting *names* exist | Putting secret values into OpenTofu state or `.tfvars` |

Other Azure resource groups on the same subscription (`RichardOrchardRG`, `RO-Backup`, Cloud Shell, default Insights groups, etc.) are **personal/shared operator estate** — keep outside the QueenZone OpenTofu stack unless deliberately expanded later.

## Verified SKU / scale constraints

Live state matches the product decision in [`hosting-scale-and-cache.md`](hosting-scale-and-cache.md):

| Item | Live value |
| --- | --- |
| App Service plan | `ASP-Queenzone-Prod`, **B1 / Basic**, capacity **1**, Linux |
| App Service | `queenzone-prod`, Always On **on**, workers **1**, no deployment slots |
| Redis / Front Door / Azure CDN | **None** in `Queenzone-RG` or subscription QueenZone resources |
| Azure SQL | `queenzone-db` on Basic (5 DTU), max size 2 GB, LRS short-term backup |
| Storage | `queenzoneprod`, Standard_LRS, Hot |

Do not encode scale-out, Redis, Front Door, or multi-instance assumptions in OpenTofu modules.

## `cdn` vs `cdn2` — live resolution

Repository docs disagreed. Live behaviour (2026-08-12):

| Hostname | Live routing | Evidence | Correct doc stance |
| --- | --- | --- | --- |
| `cdn.queenzone.org` | **Straight Cloudflare proxy** to Azure Blob. **No Worker header rewriting.** | Successful photo/CSS responses pass through Azure `x-ms-*` headers; Cloudflare `Cache-Control: max-age=14400`; **no** Worker-added `Access-Control-Allow-Origin` / `X-Content-Type-Options`. Azure Storage **custom domain** is registered as `cdn.queenzone.org`. | Matches `AGENTS.md`, `blob-storage-ugc.md`, `picture-library-plan.md`, `PhotoImageUrl.cs`. |
| `cdn2.queenzone.org` | **Cloudflare Worker** script `pictures-queenzone-org` on route `cdn2.queenzone.org/*`, fetching `https://queenzoneprod.blob.core.windows.net`. | DNS name is **cdn2**, not `pictures`. Script returns 404 for `/songfiles/*` (#177). Live responses add `Access-Control-Allow-Origin: *`, `X-Content-Type-Options: nosniff`, `Cache-Control` on 200. No Azure custom domain for `cdn2`. | Legacy forum attachment redirect target. Fan audio is app-proxied. Do not treat the script name as a hostname. |

`docs/architecture/azure-hosting-plan.md` previously attributed Worker `pictures-queenzone-org` and route `cdn.queenzone.org/*` to **cdn**, and told operators not to add an Azure Storage custom domain. Both statements are **false against live state** and are corrected in that file as part of this issue.

Both `cdn` and `cdn2` are proxied CNAMEs to `queenzoneprod.blob.core.windows.net`. Direct `https://queenzone-prod.azurewebsites.net/health` returns **403 Ip Forbidden** from non-Cloudflare clients — App Service origin lock is effective for the main site.

The retired `pictures.queenzone.org` hostname remains available for crawler and link compatibility. Worker `pictures-legacy-redirect` serves `/robots.txt` with `200` and `Disallow: /`; every other path redirects permanently to the equivalent `cdn.queenzone.org` URL. Its source snapshot is `infra/import/workers/pictures-legacy-redirect.js`.

## Ownership matrix

Treatments:

- **import** — OpenTofu will import and manage.
- **data** — reference via data source / read-only; do not recreate.
- **outside** — keep outside OpenTofu permanently (or until a later explicit decision).
- **defer** — needs a decision or missing credentials before classifying.

### Azure (`Queenzone-RG`)

| Resource | ID (sanitised path) | Treatment | Notes / outage risk |
| --- | --- | --- | --- |
| Resource group `Queenzone-RG` | `/subscriptions/…/resourceGroups/Queenzone-RG` | import | australiaeast; container for the stack |
| Plan `ASP-Queenzone-Prod` | `…/Microsoft.Web/serverFarms/ASP-Queenzone-Prod` | manage | Canada East live plan; SKU must stay B1×1 |
| Site `queenzone-prod` | `…/Microsoft.Web/sites/queenzone-prod` | manage | Canada East live application; Cloudflare-only ingress |
| Hostname bindings `queenzone.org`, `www.queenzone.org` | `…/sites/queenzone-prod/hostNameBindings/…` | manage | SNI certs bound; breaking bindings = public TLS outage |
| Plan `ASP-Queenzone` / site `queenzone-dev` | Australia East resource paths | retired | Removed from state and deleted on **2026-09-14** after the accepted observation period |
| Rollback certificates `queenzone.org`, `www.queenzone.org` | `…/Microsoft.Web/certificates/…` | retired | Old Australia East certificate resources deleted with the rollback app on **2026-09-14** |
| Active Canada East SNI certificate | uploaded certificate in the `queenzone-prod` webspace | outside | Cloudflare Origin CA certificate; secret material stays in Bitwarden. OpenTofu manages the target hostname bindings by the non-secret thumbprint recorded in `production-region-migration.md` |
| Access restrictions (Cloudflare IPv4/IPv6 allow + deny all) | site `ipSecurityRestrictions` | import | One CIDR per rule (refreshed 2026-09-22). A comma-separated `ip_address` does not match Cloudflare. Mis-order or drop = either open origin or lock out Cloudflare |
| SCM access restrictions | site `scmIpSecurityRestrictions` | import | Configuration reuses the main Cloudflare allow list and default Deny when direct access is off (#1653). Live SCM stays **Allow all** until that configuration is applied. WebDeploy basic auth stays on because deploy still uses the publish profile |
| App settings (names only) | site config | outside → [ADR 0008](../decisions/0008-app-service-settings-ownership.md) | Names re-listed 2026-08-24. Secret **values** stay in Azure/Bitwarden, never state. `deploy.yml` ARM-owns three non-secret deploy keys outside OpenTofu (see [App Service settings](#app-service-application-setting-names-values-not-recorded)). #622's site resource must omit/`ignore_changes` on `app_settings`/`connection_string` |
| SQL server `queenzone-prod-sql` | `…/Microsoft.Sql/servers/queenzone-prod-sql` | manage | Canada East live server; SQL auth still used by app |
| SQL server `queenzone-sql-server` | `…/Microsoft.Sql/servers/queenzone-sql-server` | import | Retained because the active dev environment still uses `queenzone-dev-db` |
| Firewall `AllowAllWindowsAzureIps` | `…/firewallRules/AllowAllWindowsAzureIps` | import | Required for App Service → SQL |
| Firewall `ClientIPAddress_2026-6-11_20-28-58` | `…/firewallRules/ClientIPAddress_…` | defer | Operator workstation IP; likely keep outside or replace with named break-glass rule |
| Database `queenzone-db` on `queenzone-prod-sql` | `…/servers/queenzone-prod-sql/databases/queenzone-db` | manage | Canada East live database; **never recreate** (data loss). Schema via EF only |
| Database `queenzone-db` on `queenzone-sql-server` | `…/servers/queenzone-sql-server/databases/queenzone-db` | retired | Old Australia East production database removed from state and deleted on **2026-09-14** |
| Pre-cutover copy `queenzone-db-precutover-20260910-083153` | `…/servers/queenzone-sql-server/databases/queenzone-db-precutover-20260910-083153` | retired | Recorded zero connections during observation; deleted by separate maintainer approval on **2026-09-14** |
| SQL auditing (server + db) | `…/auditingSettings/Default` | data / defer | Currently **Disabled** — do not “enable by default” in first import |
| Short-term backup (7 days, LRS) | backup policy | import | Provider default-ish for Basic; LTR all zero |
| Storage account `queenzoneprod` | `…/storageAccounts/queenzoneprod` | manage | Canada East live account; public blob access allowed; **custom domain `cdn.queenzone.org`** |
| Storage account `queenzone` | `…/storageAccounts/queenzone` | retired | Removed from state and deleted on **2026-09-15** after content, dependency, no-write, deployment, and post-deletion health checks passed |
| Blob soft-delete / container soft-delete (7 days) | blob service properties | import | Versioning **not** enabled; no lifecycle management policy |
| Blob containers + public access flags | per-container | import | See [Storage containers](#storage-containers-live); changing ACLs can break media or expose private UGC |
| Storage RBAC assignments | scope storage account | data | Empty list at audit time (access via keys / portal roles at higher scope) |
| Log Analytics `queenzone-dev-law` | `…/workspaces/queenzone-dev-law` | retired | Deleted on **2026-09-14** with the old web estate |
| App Insights `queenzone-dev-ai` | `…/components/queenzone-dev-ai` | retired | Deleted on **2026-09-14** with the old web estate |
| Action group `queenzone-alerts` | `…/actionGroups/queenzone-alerts` | import | Email receiver present (address not recorded here) |
| Legacy webtest `queenzone-dev-health` | `…/webtests/queenzone-dev-health` | retired | Deleted on **2026-09-14** because it targeted the retired app |
| Legacy metric / query alerts | listed in import JSON | retired | Five rules scoped only to the old Application Insights or Log Analytics resources were deleted on **2026-09-14** |
| Smart detector `Failure Anomalies - queenzone-dev-ai` | alertsmanagement | retired | Deleted with the old Application Insights component on **2026-09-14** |
| Diagnostic settings (web/sql/storage) | n/a | outside | None configured — do not invent |

### Cloudflare (API inventory complete)

Account id `f93121b2086286e79a7a9fdb8d03cb4c`. Zone id `079fc2f37095c82fb3a2b4da65718b2b` (`queenzone.org`, Free, full setup). NS: `daisy.ns.cloudflare.com`, `skip.ns.cloudflare.com`. DNSSEC disabled. Detail JSON: [`infra/import/cloudflare-hostnames.json`](../../infra/import/cloudflare-hostnames.json). Worker source snapshot: [`infra/import/workers/pictures-queenzone-org.js`](../../infra/import/workers/pictures-queenzone-org.js).

| Item | Treatment | Notes |
| --- | --- | --- |
| Zone `queenzone.org` | import | Free plan; never recreate casually |
| DNS `queenzone.org` A → `40.69.106.99` (proxied) | import | Canada East `queenzone-prod` App Service inbound IP |
| DNS `www` CNAME → `queenzone-prod.azurewebsites.net` (proxied) | import | |
| DNS `cdn` / `cdn2` CNAME → `queenzoneprod.blob.core.windows.net` (proxied) | import | Only **cdn2** has a Worker route |
| DNS `asverify.cdn` CNAME (DNS-only) | import | Azure Storage custom-domain verification |
| DNS `asuid` / Bing / Google TXT|CNAME verify records | import | Keep; not secrets |
| SSL/TLS mode **strict** | import | Confirmed Full (strict). Edge Universal SSL active (+ backup pack) |
| `min_tls_version` = `1.0` | defer | Dashboard default; decide before encoding as desired |
| Always HTTPS / TLS 1.3 / automatic HTTPS rewrites | import | on |
| Worker script `pictures-queenzone-org` | import | **Never recreate blindly**; route is cdn2 despite name |
| Worker route `cdn2.queenzone.org/*` → that script | import | Do **not** attach to `cdn` |
| Page Rules | outside | None |
| Custom WAF / Transform / Cache / Origin rulesets | outside | No zone entrypoint custom rulesets |
| Managed Free WAF / Normalization / DDoS L7 rulesets | outside | Provider defaults — do not copy rule bodies |
| Account billing / members | outside | |
| Domain registrar | outside | |
| `CLOUDFLARE_API_TOKEN_READONLY` (Bitwarden) | outside | Inventory/ops read-only; never OpenTofu state |
| `CLOUDFLARE_WORKER_READWRITE` (Bitwarden) | outside | Worker script publish (#177); not the planned OpenTofu apply token |

### GitHub Actions / Bitwarden

| Item | Treatment | Notes |
| --- | --- | --- |
| Workflows under `.github/workflows/` | outside | App deploy path stays GitHub; OpenTofu CI is a later issue (#625). Inventory now includes `opentofu-backend-smoke.yml`. |
| Environment `prod-release` | outside | Custom policy: branch `main` + tags `v*`. Production migrate + zip deploy (`deploy.yml`). See [`github-environments.md`](github-environments.md). |
| Environment `prod-deploy` | outside | Custom policy: branch `main` + tags `v*`. Dedicated OIDC identity with **Website Contributor** on site `queenzone-prod` only. `deploy.yml` `configure-app-settings` and `app-service-setting-names-check.yml`. Not an OpenTofu principal — sibling bootstrap at `infra/bootstrap/Bootstrap-DeployIdentity.ps1`. |
| Environment `prod-google-play` | outside | Custom policy: branch `main`. Play signing / store upload only (`publish-android-google-play.yml`). |
| Environment `prod-data-read` | outside | Custom policy: branch `main`. Production read used only to refresh/resync the SQL Express mirror. |
| Environment `dev` (legacy) | outside | **Deleted in Settings** (2026-09-07, #1394). Retired from workflows in #1377. Do not recreate. |
| Environment `deploy` (legacy) | outside | **Deleted in Settings** (2026-09-07, #1394). Entra FIC subject `environment:deploy` removed. Retired from workflows in #1377. ARM/OIDC now lives on `prod-deploy`. Do not recreate. |
| Environment `opentofu-plan` | outside | Protected branches only. Reader on `Queenzone-RG` plus state-container data access. See [`opentofu-state-and-identity.md`](opentofu-state-and-identity.md). |
| Environment `opentofu-apply` | outside | Protected branches + `richardorchard` approval. Contributor on `Queenzone-RG`. Do not reuse for routine zip deploys. |
| Repo secret `BITWARDEN_SECRETS_MANAGER_ACCESS_TOKEN` | outside | Token only; never OpenTofu state |
| Repo variable `BITWARDEN_APP_SERVICE_DEPLOY_SECRETS` | outside | UUID→name mapping only |
| Repo secret `SIXLABORS_LICENSE_KEY` | outside | CI/deploy ImageSharp licence; not an App Service setting |
| Legacy raw GH secrets (`AZURE_WEBAPP_PUBLISH_PROFILE`, migration connection strings, etc.) | defer | Names still present; Bitwarden is intended SoT — reconcile/delete later (#618) |
| Bitwarden project `Queenzone Development` | outside | Secret values; OpenTofu may later wire Key Vault / references but not store values |
| App Service publish profile | outside | Rotatable credential; consumed by environment `prod-release`, not `prod-deploy` |

### Entra / identity (reference)

| Item | Treatment | Notes |
| --- | --- | --- |
| App Service system-assigned MI | import with site | Principal id recorded in import JSON |
| `QueenZone Deploy` OIDC app | outside | Used by GitHub environment `prod-deploy` to write ARM settings. Not part of the OpenTofu plan/apply pair. |
| Entra app registrations (Admin / member OAuth) | outside / defer | Documented in `entra-admin-auth.md`; not Azure RG resources |
| SQL AAD admin | data | Login name known; do not put credentials in state |

The 2026-08-15 #622 refresh found no direct role assignments for the App
Service system-assigned identity. The identity remains managed as part of the
site; no empty or speculative RBAC resources are declared.

## Storage containers (live)

| Container | publicAccess | Product role | OpenTofu note |
| --- | --- | --- | --- |
| Photo/archive galleries (`queen`, `freddie-mercury`, …) | `blob` | Public photos via `cdn` | Keep public blob read |
| `images`, `css`, `mp3`, `forum`, `avatars`, `album-or-single-covers`, … | `blob` or `container` (`css`) | Legacy public assets | Keep; `css` is listable |
| `songfiles` | **`None` (private)** | Fan audio streamed by `/fan-performances/{id}/audio` | Live since 2026-08-16 (ARM). Module desired state already `None`. |
| `attachments` | **`blob` (public)** | Legacy forum files; app redirects after auth | URL guessing bypasses app gate. Relates to #177 / media lockdown |
| `databasebackup` | private | Backups | Keep private; **never** public |
| `ugc-articles`, `ugc-avatars`, `ugc-forum`, `ugc-photos` | private | Modern UGC | Keep private; app proxy. All four were live at the 2026-09-09 refresh. Relates to [#583](https://github.com/richardorchard/QueenZone.Modern/issues/583), [#584](https://github.com/richardorchard/QueenZone.Modern/issues/584) |
| `test` | `blob` | Legacy/scratch content | **2,320 blobs / 4,110,472,406 bytes** at the 2026-09-09 refresh. Preserve during migration; review any later deletion separately. |

No storage lifecycle policy exists. Soft delete is 7 days for blobs and containers; versioning is off.

## App Service application setting names (values not recorded)

Required on `queenzone-prod` after the 2026-09-10 cutover (`az webapp config appsettings list`, names only except the one non-secret deploy key below):

`APPLICATIONINSIGHTS_CONNECTION_STRING`, `Authentication__Apple__ClientId/TeamId/KeyId/PrivateKey`, `Authentication__Discord__ClientId/Secret`, `Authentication__Facebook__ClientId/Secret`, `Authentication__GitHub__ClientId/Secret`, `Authentication__Google__ClientId/Secret`, `Authentication__Microsoft__ClientId/Secret`, `MobileAuth__SigningKey`, `PushNotifications__Apns__TeamId`, `PushNotifications__Apns__KeyId`, `PushNotifications__Apns__PrivateKeyPem`, `PushNotifications__Apns__Environment`, `PushNotifications__Fcm__ProjectId`, `PushNotifications__Fcm__ServiceAccountJson`, `BlobUpload__PublicBaseUrl`, `ConnectionStrings__BlobStorage`, `ConnectionStrings__QueenZoneLegacy`, `OPENROUTER_API_KEY`, `WEBSITE_HEALTHCHECK_MAXPINGFAILURES`, `WEBSITE_HTTPLOGGING_RETENTION_DAYS`, `Analytics__GoogleAnalyticsServiceAccountJson`, `Analytics__TrafficCacheMinutes`, `Analytics__GoogleAnalyticsPropertyId`, `AzureAd__Instance`, `AzureAd__TenantId`, `AzureAd__ClientId`, `AzureAd__ClientSecret`, `AzureAd__CallbackPath`, `Admin__AllowedEmails__0`, `Admin__AllowedEmails__1`, `SCM_DO_BUILD_DURING_DEPLOYMENT`, `ENABLE_ORYX_BUILD`, `WEBSITE_WARMUP_PATH`. `WEBSITE_RUN_FROM_PACKAGE` was removed (see below) and must stay absent. HTTP log retention is already configured through the App Service logging resource; the redundant legacy `DIAGNOSTICS_AZUREBLOBRETENTIONINDAYS` app-setting name is not required.


Ownership of App Service settings is decided in [ADR 0008](../decisions/0008-app-service-settings-ownership.md)
([#618](https://github.com/richardorchard/QueenZone.Modern/issues/618)): OpenTofu stays out of `app_settings`/
`connection_string` entirely (Option A). This same name list (`infra/import/github-bitwarden.json`'s
`appServiceSettingNames`) is checked nightly for missing names by `scripts/Test-AppServiceSettingNames.ps1` — see
`.github/workflows/app-service-setting-names-check.yml`.

### ARM-owned non-secret deploy keys (#666)

These are **not** Bitwarden secrets, and they are **not** OpenTofu-managed either — `deploy.yml`'s
`configure-app-settings` job writes them directly through ARM (`azure/login` on GitHub environment `prod-deploy`), a
mechanism that predates and is unaffected by ADR 0008. Do **not** write them through Kudu `POST /api/settings`
(that was the #664 no-op). ADR 0008 rejected importing any subset of the App Service `app_settings` map into
OpenTofu (its "Option C") — AzureRM has no resource/API that safely manages a subset without risking the
unmanaged remainder, so this key stays owned by `deploy.yml`'s targeted ARM writes, not by OpenTofu.

The same targeted ARM job also reconciles the secret `MobileAuth__SigningKey` from Bitwarden. Its value is never
recorded in this inventory or OpenTofu state.

| Name | Live value | Owner | Notes |
| --- | --- | --- | --- |
| `WEBSITE_WARMUP_PATH` | `/health` | ARM via `deploy.yml` | Platform container-start probe. Must stay on `/health`, **not** `/warmup` (#673). `/warmup` remains the deploy-time readiness gate. |
| `WEBSITE_WARMUP_STATUSES` | **absent** | ARM via `deploy.yml` (deleted if present) | Must stay unset. Requiring `200` crash-looped the B1 worker when App Service used an internal Host header (#684). |
| `WEBSITE_RUN_FROM_PACKAGE` | **absent** | ARM via `deploy.yml` (deleted if present) | Added after the 2026-08-12 audit to mount the OneDeploy zip read-only; removed after it repeatedly served stale builds post-deploy (a worker could pass the build-stamp smoke check and then revert to the previous build minutes later with no further restart logged). Plain extract-mode deploys do not have this failure mode. |

Site health-check path remains `/health`. Remaining secret settings (SQL, Entra, member OAuth, APNs, FCM, blob, OpenRouter, Insights, admin emails) stay Bitwarden/operator-owned.


## Resources that must never be recreated

Blind destroy/recreate of any of these is an outage or data-loss event:

1. Live Azure SQL database `queenzone-db` on `queenzone-prod-sql`.
2. Live Storage account `queenzoneprod` and its blob data.
3. App Service plan `ASP-Queenzone-Prod` / site `queenzone-prod` while DNS points at them.
4. Custom hostname bindings + bound certificates for `queenzone.org` / `www.queenzone.org`.
5. Cloudflare proxied DNS for apex/www/cdn/cdn2 and the **cdn2 Worker**.
6. Azure Storage custom domain association for `cdn.queenzone.org`.
7. App Service access restrictions that deny non-Cloudflare ingress (unless carefully replaced).

The rollback server `queenzone-sql-server` also hosts `queenzone-dev-db` from
the separate dev state. Production retirement must not delete that logical
server until the dev database has moved or the dependency has been deliberately
retained.

## Suggested import order (later issues)

Documented for [#622](https://github.com/richardorchard/QueenZone.Modern/issues/622) / [#628](https://github.com/richardorchard/QueenZone.Modern/issues/628) / [#626](https://github.com/richardorchard/QueenZone.Modern/issues/626) — do not execute until remote state (#616) and safety controls (#619) exist.

1. Resource group (or data-source it).
2. Log Analytics workspace → Application Insights.
3. App Service plan → web app (no hostname swap).
4. Certificates → hostname bindings.
5. Access restriction rules (validate Cloudflare still serves `/health` after plan).
6. Action group → alerts / webtest (fix webtest URL when importing).
7. SQL server → firewall rules → database (import existing; never `create`).
8. Storage account → blob service properties → containers → custom domain.
9. Cloudflare zone/DNS → TLS → Worker/routes for **cdn2 only**.
10. App settings (#618 / ADR 0008): no import step — the site resource must omit/`ignore_changes` `app_settings` and `connection_string` so OpenTofu never manages them.

**Outage risks during import:** hostname/TLS drift, IP restriction mistakes, storage public-access flips, Worker route removal on cdn2, SQL firewall removing `AllowAllWindowsAzureIps`.

## Dashboard defaults — do not copy blindly

| Observation | Guidance |
| --- | --- |
| Smart detection failure anomalies rule | Leave as Azure default unless product wants it managed |
| App Insights billing cap 100 GB | Far above LAW 0.1 GB daily cap; LAW cap is the real budget control — do not “harmonise” upward |
| `use32BitWorkerProcess: true` on a Linux .NET site | Likely portal noise; verify before encoding |
| Retired metric alert `queenzone-dev-failed-requests` | Deleted with the old telemetry stack on **2026-09-14**; do not recreate |
| SQL auditing disabled | Import as disabled; enabling is a product decision |
| Storage versioning off | Do not enable in first apply |
| Operator SQL firewall ClientIP rule | Do not encode personal IPs as production IaC without renaming |
| Legacy GH secrets alongside Bitwarden | Clean up separately; do not duplicate into OpenTofu |
| Cloudflare `min_tls_version` = `1.0` | Free-plan dashboard default; decide before treating as desired state |
| Cloudflare managed Free WAF / Normalization / DDoS rulesets | Leave as provider defaults; do not import rule bodies |
| Worker script name `pictures-queenzone-org` | Historical; live route is cdn2 — do not “fix” by attaching it to cdn |

## Dependencies on open product issues

| Issue | Relevance |
| --- | --- |
| [#177](https://github.com/richardorchard/QueenZone.Modern/issues/177) | `songfiles` is private and app-proxied. Legacy `attachments` remain public blob access. |
| [#583](https://github.com/richardorchard/QueenZone.Modern/issues/583) | Anonymous `/ugc` proxy sensitivity — private containers must stay private |
| [#584](https://github.com/richardorchard/QueenZone.Modern/issues/584) | Upload API container narrowing — affects which containers exist and who may write |
| [#428](https://github.com/richardorchard/QueenZone.Modern/issues/428) | Cloudflare proxy / origin restriction history — current live state already restricts App Service to Cloudflare IPs |
| [#618](https://github.com/richardorchard/QueenZone.Modern/issues/618) | Secret-safe App Service configuration ownership. Live state is already a split: `deploy.yml` ARM-owns `WEBSITE_WARMUP_PATH` and keeps `WEBSITE_WARMUP_STATUSES` / `WEBSITE_RUN_FROM_PACKAGE` absent; Bitwarden still owns secrets. |
| [#666](https://github.com/richardorchard/QueenZone.Modern/issues/666) | ARM Application Settings for run-from-package and warmup; dedicated `deploy` OIDC identity. Explicitly left OpenTofu out of the settings map. |

## Follow-ups (non-blocking for #624)

1. Optional: **Storage Blob Data Reader** on `queenzone` for private-container object audits without account keys.
2. Confirm Azure App Service certificate renewal path (GeoTrust uploads expire **2026-12-29**). #622 preserves the SNI bindings and thumbprints but leaves the uploaded certificate resources outside state because AzureRM would require private PFX material.
3. Product decision whether Worker should set `Content-Disposition` for audio downloads (capability exists; live script does not).
4. [#618](https://github.com/richardorchard/QueenZone.Modern/issues/618) should classify the ARM-owned deploy keys separately from Bitwarden secrets before `infra/modules/azure-web` imports any settings.

No infrastructure mutation was performed for this audit.
