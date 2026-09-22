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

The main site is Cloudflare allow-only with deny-all. Each published prefix
is its own `ip_restriction` (one CIDR). The list was refreshed on
**2026-09-22** from <https://www.cloudflare.com/ips-v4> and
<https://www.cloudflare.com/ips-v6>. Azure publishes no Cloudflare service
tag; `AzureFrontDoor.Backend` is not this origin's network.

When `allow_direct_access` is false, SCM uses that same allow list
(`scm_use_main_ip_restriction = true`) and
`scm_ip_restriction_default_action = "Deny"`. Dev and migration apps that
allow direct access keep both defaults at Allow and do not install the
Cloudflare list. AzureRM 5.0.1 still needs the default actions set
explicitly (#626); leaving SCM at Allow on the production app would keep
Kudu on the public internet.

WebDeploy basic authentication stays enabled because `deploy.yml` and
`scripts/Invoke-AppServiceKudu.py` still use the publish profile. FTP basic
authentication stays enabled for the same reason. Applying the SCM deny
blocks GitHub-hosted runners from `*.scm.azurewebsites.net` until an
operator supplies an allow list or the deploy path moves. Do not guess
those addresses in configuration.

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
