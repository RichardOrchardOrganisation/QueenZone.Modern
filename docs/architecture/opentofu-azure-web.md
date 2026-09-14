# OpenTofu Azure web import

Issue: [#622](https://github.com/richardorchard/QueenZone.Modern/issues/622),
step 4 of epic [#615](https://github.com/richardorchard/QueenZone.Modern/issues/615).

## Managed boundary

Production now runs on the Canada East `queenzone-prod` app, plan
`ASP-Queenzone-Prod`, Log Analytics workspace `queenzone-prod-law`, and
Application Insights component `queenzone-prod-ai`. The imported Australia East
web estate was removed from state and deleted on **14 September 2026** after
the accepted four-day observation period.

The retired estate comprised:

- Linux App Service plan `ASP-Queenzone`, fixed at B1 and one worker;
- Linux web app `queenzone-dev` and its system-assigned identity;
- `queenzone.org` and `www.queenzone.org` SNI hostname bindings;
- Log Analytics workspace `queenzone-dev-law`;
- Application Insights component `queenzone-dev-ai`.

No direct role assignment existed for the old web app identity, so the module did
not invent one. ADR 0008 keeps App Service settings outside OpenTofu. The narrow
`ignore_changes` entry prevents an incomplete settings map from deleting live
secrets or the ARM-owned deployment settings.

The live main-site restriction remains Cloudflare allow-only with deny-all.
SCM retains a separate allow-all policy for the current deployment workflow.
AzureRM 5.0.1 normalises both explicit terminal rules to empty default-action
fields during import; #626 now sets `ip_restriction_default_action = "Deny"`
and `scm_ip_restriction_default_action = "Allow"` explicitly so a plan cannot
silently open the origin or lock out SCM. The Cloudflare allow ranges remain
managed alongside them.

## Certificate boundary

The two old GeoTrust PFX resources were deleted with the Australia East app.
The live Canada East app uses a separate Cloudflare Origin CA certificate.
OpenTofu manages the SNI hostname bindings by non-secret thumbprint but keeps
the certificate resource and all private material outside configuration and
state.

## Verification

On **2026-08-15**, the protected remote state was read and a production plan
was generated without applying it. All seven declared import addresses were
`no-op`; no create, update, replace, or delete was proposed.

The checks below record the original import verification. Current live smoke
checks target `https://www.queenzone.org`; direct access to
`https://queenzone-prod.azurewebsites.net` is denied outside Cloudflare.

Original read-only live checks also passed:

- `scripts/Smoke-LiveSite.ps1` passed `/warmup`, GET `/health`, and all public routes;
- direct GET `https://queenzone-dev.azurewebsites.net/health` returned 403;
- the SCM API endpoint remained reachable;
- Application Insights contained 499 requests in the preceding hour, with the latest at `2026-08-15T04:44:35Z`.

The plan file and provider state can contain sensitive values. Keep them outside
the repository and report only resource actions during review.
