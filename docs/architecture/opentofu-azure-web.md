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
is its own `ip_restriction` (one CIDR). That split is the change to ship.
The list was refreshed on **2026-09-22** from
<https://www.cloudflare.com/ips-v4> and <https://www.cloudflare.com/ips-v6>.
Azure publishes no Cloudflare service tag; `AzureFrontDoor.Backend` is not
this origin's network. Dev and migration apps that allow direct access omit
the Cloudflare list and keep the main-site default at Allow.

SCM stays `scm_use_main_ip_restriction = false` and
`scm_ip_restriction_default_action = "Allow"`. AzureRM 5.0.1 still needs
that default set explicitly (#626). #1653's SCM Deny is not done.
`deploy.yml` (`azure/webapps-deploy`) and `scripts/Invoke-AppServiceKudu.py`
reach `*.scm.azurewebsites.net` with the publish profile from GitHub-hosted
runners. Those addresses are not Cloudflare ranges, so copying only the
main-site allow list onto SCM would 403 production deploys. SCM Deny waits
for an explicit deploy or operator allow list, or for deploy to leave public
SCM. Do not guess those addresses. WebDeploy and FTP basic authentication
stay enabled because that publish-profile path still uses them.

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
