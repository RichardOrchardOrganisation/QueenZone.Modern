# Azure web module

This module owns the imported production App Service plan, Linux web app,
workspace-linked telemetry, custom hostname bindings, TLS configuration, and
the existing Cloudflare-only main-site ingress policy.

When `allow_direct_access` is false, the main site and SCM allow one
Cloudflare prefix per rule and deny everything else. The prefix list was
refreshed on 2026-09-22 from the published Cloudflare IPv4 and IPv6 pages.
Azure has no Cloudflare service tag, so the module does not use
`AzureFrontDoor.Backend`. Dev and migration apps that allow direct access
omit that list and keep both defaults at Allow.

WebDeploy and FTP basic publishing credentials stay enabled. Production
deploy still signs in to SCM with the publish profile (`deploy.yml` and
`scripts/Invoke-AppServiceKudu.py`), not GitHub OIDC. Applying the SCM deny
blocks GitHub-hosted runners until an operator allow list is supplied or the
deploy path moves. Do not guess those addresses here.

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
