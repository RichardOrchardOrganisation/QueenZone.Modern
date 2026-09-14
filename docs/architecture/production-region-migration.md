# Production region migration

Issue: [#1272](https://github.com/richardorchard/QueenZone.Modern/issues/1272),
Phase 7 of [epic #1264](https://github.com/richardorchard/QueenZone.Modern/issues/1264).

## Current status: 14 September 2026

Stages 1–4 are complete. Production traffic, deployment, SQL, Blob Storage,
telemetry, authentication callbacks, and mobile configuration now target the
Canada East estate listed below. After four days of verified operation, the
maintainer accepted that observation period and approved Stage 5 retirement
on **14 September 2026** to stop the old estate's ongoing cost.

Retirement is staged. The old App Service estate and production database are
the first tranche. `queenzone-sql-server` remains because it hosts the active
dev database `queenzone-dev-db`. The pre-cutover database copy and old Storage
account also remain until their separate retention and retirement decisions.

## Target and safety boundary

ADR 0020 approves `canadaeast` for production. The App Service, SQL server and
database, and Blob Storage move together. The existing `Queenzone-RG`
resource group remains; its own `australiaeast` location is metadata and does
not constrain child-resource locations.

The replacement names are:

| Resource | Target |
| --- | --- |
| App Service plan | `ASP-Queenzone-Prod` |
| App Service | `queenzone-prod` |
| Log Analytics | `queenzone-prod-law` |
| Application Insights | `queenzone-prod-ai` |
| SQL server | `queenzone-prod-sql` |
| SQL database | `queenzone-db` |
| Storage account | `queenzoneprod` |

Never combine the build, data-copy, cutover, and retirement stages into one
apply. Each gate must leave the old production path intact until the new path
has passed its checks. Never run `tofu destroy` against either estate.

## Completed one-time cleanup: failed East US candidate

The first Stage 1 apply on **7 September 2026** created only these East US
resources before Azure rejected the App Service and SQL server:

- `module.azure_data_target.azapi_resource.storage_account` —
  `queenzoneprod`;
- `module.azure_web_target.azurerm_application_insights.production` —
  `queenzone-prod-ai`;
- `module.azure_web_target.azurerm_log_analytics_workspace.production` —
  `queenzone-prod-law`.

On **9 September 2026**, verification found no containers in the Storage
account and no telemetry records in Application Insights. No target App
Service plan, web app, SQL server, or database exists. Microsoft then
confirmed that the East US regions were unavailable for the subscription,
and ADR 0020 changed the target to Canada East.

These three resource names had to be released before the Canada East
create-only plan could succeed. Their `prevent_destroy` lifecycle rules
correctly blocked a location replacement. Do not weaken or remove those rules.

The explicitly approved cleanup completed on **9 September 2026**. It was
cleanup of the empty failed candidate, not retirement of the live Australia
East estate:

1. Recheck the exact locations, Storage container count, and telemetry count.
2. Snapshot the remote production state and record the snapshot identifier.
3. Remove only the three state addresses listed above with reviewed
   `tofu state rm` commands.
4. Delete `queenzone-prod-ai`, force-delete `queenzone-prod-law` so its name
   is released immediately, delete `queenzoneprod`, then remove the orphaned
   `Failure Anomalies` smart detector rule.
5. Confirm all four Azure resources and the three state addresses are absent.
6. Run the Canada East capacity preflight and a fresh production plan.

The state backup snapshot is
`2026-09-09T04:24:43.6156940Z`. Post-cleanup checks found zero matching target
resources, zero target state addresses, and confirmed that `queenzoneprod`
was available for reuse. The next plan must contain Canada East creates only.
Any update, replacement, or delete blocks the apply.

The following records the exact state operation that was executed once from
`infra/environments/production`. Do not rerun it:

```powershell
tofu state rm `
  'module.azure_data_target.azapi_resource.storage_account' `
  'module.azure_web_target.azurerm_application_insights.production' `
  'module.azure_web_target.azurerm_log_analytics_workspace.production'
```

The following records the exact Azure cleanup. Do not rerun it:

```powershell
az monitor app-insights component delete `
  --resource-group Queenzone-RG `
  --app queenzone-prod-ai

az monitor log-analytics workspace delete `
  --resource-group Queenzone-RG `
  --workspace-name queenzone-prod-law `
  --force true `
  --yes

az storage account delete `
  --resource-group Queenzone-RG `
  --name queenzoneprod `
  --yes

az resource delete `
  --resource-group Queenzone-RG `
  --name 'Failure Anomalies - queenzone-prod-ai' `
  --resource-type Microsoft.AlertsManagement/smartDetectorAlertRules
```

## Stage 1: build the parallel target — completed

The production apply plan runs `Test-AzureMigrationTargetCapacity.ps1` before
the approval gate. If the target App Service plan or SQL logical server does
not yet exist, the check fails closed unless the requested App Service SKU has
enough regional capacity and Azure SQL reports logical-server provisioning as
available for the subscription.

On **7 September 2026**, the first `eastus` apply stopped with B1 capacity at
`0/0` and Azure SQL status `Visible` with `ProvisioningDisabled`. It created
only the three resources covered by the one-time cleanup above. Microsoft
later declined access to both East US regions. Do not retry that target.

The initial configuration keeps the imported `module.azure_web` and
`module.azure_data` unchanged. `module.azure_web_target` and
`module.azure_data_target` create the parallel resources. The candidate app
allows direct access at `https://queenzone-prod.azurewebsites.net` and has no
production hostname binding. The target Storage account has no custom domain.

The target SQL database is deliberately absent. Azure's cross-server copy
operation must create the destination database, so creating an empty database
first would block the supported copy path.

Required evidence before approval:

- local `scripts/Test-OpenTofu.ps1` passes;
- the production remote plan reports creates only;
- the plan contains no update, replacement, or delete;
- the SQL password is shown only as `(write-only attribute)`;
- target resources use `canadaeast` and the names above.

Merge does not apply immediately. Review the `opentofu-apply.yml` plan summary,
then approve the protected `opentofu-apply` environment only when it still
contains creates and state-address moves only. Reject any run that changes or
deletes an existing resource.

## Stage 2: copy and verify data — completed

Take a fresh, retained database export or backup before the final cutover. Do
not delete or overwrite the source database or source blobs.

Create the database copy through Azure's native cross-server operation:

```powershell
az sql db copy `
  --resource-group Queenzone-RG `
  --server queenzone-sql-server `
  --name queenzone-db `
  --dest-resource-group Queenzone-RG `
  --dest-server queenzone-prod-sql `
  --dest-name queenzone-db `
  --service-objective S0 `
  --backup-storage-redundancy Local
```

Copy Blob Storage container-by-container using a short-lived, read/list-only
source user-delegation SAS and authenticated destination writes. Never print a
SAS, account key, connection string, or blob content. Poll every server-side
copy to success before comparison.

Compare exact row counts for every user table. Compare blob count and total
content length for every container. Any mismatch blocks deployment and
cutover. Record only table/container names, counts, byte totals, and mismatch
details; do not record row values, blob names, or credentials.

After the database copy is verified, add it to the target module with
`manage_sql_database = true` and a declarative import at its target address.
The follow-up plan may show that import but must still show no replacement or
delete.

## Stage 3: configure and test the candidate — completed

Create candidate connection strings by changing only the SQL server and
Storage account endpoints. Store them in Bitwarden; never commit or print
them. Copy the remaining production application settings by name and value
through a secret-safe operator script, then update only the candidate
connection strings and Application Insights connection.

The candidate stage granted the existing production deploy identity Website
Contributor on `queenzone-prod`, added a candidate publish profile in Bitwarden,
and deployed the same verified build that Australia East production ran. The
routine `deploy.yml` target was deliberately left unchanged until cutover.

Stage 3 used a temporary candidate-preparation script and manual deployment
workflow to clone the secret settings and deploy the exact Australia East
package. Both were retired after cutover so they cannot redeploy from the old
application or reuse candidate-only credential aliases. Routine production
deployment now targets Canada East through `deploy.yml`.

Before DNS changes, test the candidate hostname directly:

- `/health`, `/health/ready`, `/`, `/news`, and representative archive pages;
- forum reads and an authenticated forum journey;
- admin and member authentication callbacks;
- representative public and private blobs;
- mobile API health, public lists/details, authentication, and member routes.

Source and candidate may both receive writes during this stage. Treat the
candidate as disposable until the final copy.

## Stage 4: final copy and cutover — completed

Schedule a write freeze. Stop the old app or enable a maintenance response so
no writes occur after the final copy starts. Recreate the candidate database
from a fresh Azure copy and run a final incremental blob copy. Repeat every
row and blob inventory check.

Bind `queenzone.org` and `www.queenzone.org` to `queenzone-prod` with valid SNI
certificates before changing traffic. Repoint the Cloudflare apex and `www`
records to `queenzone-prod.azurewebsites.net`, then disable direct candidate
ingress by restoring the Cloudflare-only default deny rule. Update
`deploy.yml` and its deploy identity/publish-profile mappings to the new app.

Move public media with the application data. Pre-stage
`asverify.cdn.queenzone.org` against `queenzoneprod`, register
`cdn.queenzone.org` as that account's custom domain, then repoint the `cdn` and
`cdn2` CNAMEs and the `pictures-queenzone-org` Worker origin to
`queenzoneprod.blob.core.windows.net`. Leaving those origins on `queenzone`
would make newly uploaded Canada East blobs return 404 through the public CDN.

For the Canada East cutover, use the Cloudflare Origin CA certificate covering
the apex and `www` that is stored in Bitwarden and uploaded to the target App
Service webspace. Its non-secret Azure thumbprint is
`16D5EE38A8ECAB979AE6960C2CEAABCAF5B7FC14`. This keeps Cloudflare Full (strict)
enabled throughout the handover; never put the PFX or password in OpenTofu
configuration, state, plans, workflow output, or the repository.

Verify the live hostname over HTTPS, including build stamp, full route smoke,
auth, forum, uploads, Blob media, and mobile API journeys. If a blocking check
fails, point Cloudflare back to `queenzone-dev`, restore the old app to service,
and end the write freeze. Do not attempt an OpenTofu state rollback during the
traffic incident.

## Stage 5: observation and retirement — in progress

The maintainer accepted the four-day period from cutover and waived the
previously proposed seven-day window on **14 September 2026**. Release
`v2026.09.14.2` deployed the search-plan fix from #1509 before retirement. The
production migration, exact CI artifact deployment, build-stamp warmup, and
post-deploy smoke passed in workflow run `34813716156`. Independent
verification passed all 11 live smoke routes. Repeated `queen` and `freddie`
searches returned results in under 0.5 seconds after the first post-recycle
execution, without the unavailable alert.

The first retirement tranche removes the old App Service, plan, web telemetry,
obsolete web monitors, and old production `queenzone-db`. It deliberately
retains `queenzone-sql-server`, `queenzone-dev-db`, the pre-cutover database
copy, and old Blob Storage. Record an explicit retention or deletion date for
`queenzone-db-precutover-20260910-083153`; the database copy has no scheduled
deletion, seven-day point-in-time restore, and no long-term-retention policy.

Keep the old App Service, plan, SQL database/server, Storage account, and the
immediate pre-cutover backup throughout the agreed observation window. Do not
remove their state or delete them in the cutover change.

The old logical SQL server also hosts the independently managed
`queenzone-dev-db` database used by the dev environment. Do **not** delete that
server during production retirement. Either move the dev database to a
dev-owned server first, or retain the logical server and remove only the old
production `queenzone-db` after confirming the dev state still references it.

After the approved observation period passes with no unresolved mismatch:

1. Back up remote OpenTofu state and stop all applies.
2. Use reviewed `tofu state rm` commands for the old resource addresses.
3. Remove the old import blocks and configuration in a follow-up pull request.
4. Confirm the normal plan has no delete or replacement.
5. Manually delete only the exact old Azure resources after one final target
   inventory and backup check. Exclude `queenzone-sql-server` while it hosts
   `queenzone-dev-db`.
6. Record the retained backup location and expiry without recording secrets.

Update current-state documentation only after live cutover is proven. The
final repository search must find no operational reference that still targets
the old `queenzone-dev` App Service; historical incident notes may retain the
old name when clearly dated.
