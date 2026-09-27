# Entra admin authentication (App Service)

This documents the Microsoft Entra (Azure AD) app registration and App Service settings used for **admin** sign-in (`/admin/*`). Member social login (`Authentication__*`) is separate.

## Why this exists

PR Phase A production hardening (epic [#312](https://github.com/RichardOrchardOrganisation/QueenZone.Modern/issues/312), issues #313–#315) makes the web app **fail to start** outside Development/Testing when `AzureAd:ClientId` is missing or still a placeholder such as `YOUR_CLIENT_ID`.

App Service must therefore carry real Entra settings. Committed `appsettings.json` only has placeholders; secrets never belong in git.

## Admin authentication schemes

Admin pages use the policy scheme `AdminAccess` (`AdminAuthenticationSchemes.CompositeScheme`):

| Situation | Scheme used | Purpose |
| --- | --- | --- |
| Member cookie only | Ignored by `AdminAccess` | Member OAuth never grants admin access, even for an allowlisted email |
| Entra configured | Cookie scheme (Microsoft.Identity.Web) | Read the dedicated admin session |
| Entra configured, **challenge** (unauthenticated `/admin`) | **OpenID Connect** | Start Microsoft Entra sign-in — **not** member `/account/login` |
| Dev/Testing without Entra | `Test` (`X-Test-User-Email`) | Local and CI admin auth |

Unauthenticated access to `/admin/*` must challenge **Entra OIDC**, not the public member social login page. That selection lives in `QueenZoneAuthServiceCollectionExtensions.SelectAdminChallengeScheme`.

Dual-role users may hold both member and Entra cookies. `AdminAccess` always reads the Entra admin cookie; the member cookie cannot override it. Shared editor uploads use the separate `AuthoringAccess` composite scheme so both members and authenticated admins can continue to upload rich-text images.

## Production registration (configured 2026-07-23)

| Item | Value / note |
| --- | --- |
| App Service | `queenzone-prod` in resource group `Queenzone-RG` |
| Entra app display name | **QueenZone Admin** |
| Application (client) ID | `f6d32f3b-7a4e-4517-a4d1-0995caad8feb` |
| Sign-in audience | `AzureADandPersonalMicrosoftAccount` (work + personal Microsoft accounts) |
| Tenant setting for the app | `AzureAd__TenantId=common` (matches multi-account audience) |
| Client secret display name | `queenzone-prod-appservice` (historical secrets may retain the old display name) |
| Client secret created | **2026-07-23** (via `az ad app credential reset --years 2`) |
| **Renew secret by** | **2028-07-01** (allow ~3 weeks before the 2-year expiry; do not wait for outage) |
| ID token issuance | Enabled on the web platform |
| Access control | Entra signs the user in; **admin rights** still require the signed-in email to match `Admin:AllowedEmails` |

This hobby-site configuration supports personal Microsoft accounts through the `common` endpoint. MFA is controlled on each personal Microsoft account. QueenZone does not claim to enforce tenant Conditional Access or enterprise-app assignment.

## Dev registration (configured 2026-09-05)

Dev uses a separate, tenant-only registration. Do not copy the production client ID or `common` tenant setting into dev.

| Item | Value / note |
| --- | --- |
| App Service | `queenzone-devbox` in resource group `Queenzone-Dev-RG` |
| Entra app display name | **QueenZone Dev Admin** |
| Application (client) ID | `fe98ef8d-1b6d-47bd-83e1-190e483f121f` |
| Sign-in audience | `AzureADMyOrg` (Thinking Websites tenant only) |
| Tenant setting for the app | `AzureAd__TenantId=c9f094fd-23bf-4a35-a406-bcaacd7e1a8e` |
| Client secret display name | `QueenZone dev App Service` |
| Client secret created | **2026-09-05** |
| **Renew secret by** | **2028-08-15** (expires **2028-09-05**) |
| ID token issuance | **Enabled** on the web platform; required by the `id_token` OIDC response flow |
| Redirect URI | `https://dev.queenzone.org/signin-oidc` |

ID-token issuance was enabled on **2026-09-14** after Entra sign-in logs showed `AADSTS700054`. Without it, Entra rejects the callback flow before the application can create the admin session.

The admin and member OAuth registrations remain operator-managed. They are deliberately outside the OpenTofu estate; see [`opentofu-inventory.md`](opentofu-inventory.md#entra--identity-reference). Do not add an `azuread_application` resource without a separate identity-stack decision and a reviewed import plan for the existing registrations.

### Admin allowlist (not secrets, still not committed)

Committed `appsettings.json` ships **`Admin:AllowedEmails` as an empty array**. Production must supply the allowlist via App Service settings, not git. ([ADR 0008](../decisions/0008-app-service-settings-ownership.md) keeps App Service settings outside OpenTofu for now; Key Vault references were considered as a later option but are not in use.)

```text
Admin__AllowedEmails__0=you@example.com
Admin__AllowedEmails__1=other@example.com
```

- **Production / Staging / Preview:** startup fails (`ValidateOnStart`) if the allowlist is empty.
- **Development:** empty is allowed so the site boots; put real emails in git-ignored `appsettings.Local.json` for local admin work.
- **Testing:** `appsettings.Testing.json` includes `admin@test.local` for automated tests only.

Do not treat the allowlist as a secret, but also do not treat committed appsettings as the sole production source of admin access.

### Redirect URIs (web)

Production registration:

- `https://www.queenzone.org/signin-oidc`
- `https://queenzone.org/signin-oidc`
- `https://queenzone-prod.azurewebsites.net/signin-oidc`

Dev registration:

- `https://dev.queenzone.org/signin-oidc`

Add further hosts here (and in Entra) if you introduce staging slots or new custom domains.

### App Service application settings

Use double-underscore names (ASP.NET Core nested config):

| App setting | Purpose |
| --- | --- |
| `AzureAd__Instance` | `https://login.microsoftonline.com/` |
| `AzureAd__TenantId` | `common` in production; the Thinking Websites tenant ID in dev |
| `AzureAd__ClientId` | Environment-specific application (client) ID above |
| `AzureAd__ClientSecret` | Client secret value (never commit) |
| `AzureAd__CallbackPath` | `/signin-oidc` |

**Required in production** (not optional once the committed allowlist is empty):

- `Admin__AllowedEmails__0`, `Admin__AllowedEmails__1`, …

### Secrets and logging hygiene

Never put connection strings, client secrets, storage keys, or OpenRouter keys in committed config. When debugging:

- Prefer `az webapp config appsettings list` queries that return **name + length**, not values (see below).
- Do not paste secret values into GitHub issues, PR descriptions, App Insights custom events, or log messages.
- Health endpoints (`/health`, `/health/ready`) must not return exception text or connection strings (they already redact).
- Application Insights may capture request URLs and dependency names — avoid putting secrets in query strings or custom dimensions.
- Log scopes and structured properties should use identifiers (member id, article id), never passwords, tokens, or full connection strings.

### Related but different app

| Entra app | Used for |
| --- | --- |
| **QueenZone Admin** | Admin OIDC (`Microsoft.Identity.Web` / `/signin-oidc`) |
| **queenzone member login** (`3f4e4a95-7af3-48ce-be28-80d985e4014f`) | Member Microsoft OAuth (`Authentication__Microsoft__*`, `/signin-microsoft`) |

Do not point `AzureAd__*` at the member-login app without also aligning redirect URIs and OIDC vs OAuth schemes.

## Verify current configuration

Requires `az login` with access to subscription **Base Thinking** / the QueenZone resource group.

```powershell
az webapp config appsettings list `
  --name queenzone-prod `
  --resource-group Queenzone-RG `
  --query "[?starts_with(name, 'AzureAd')].{name:name, length:length(value)}" `
  -o table
```

Expect five `AzureAd__*` rows with non-zero lengths. Do not print secret values into logs, issues, or PRs.

Run the same name-and-length check against dev:

```powershell
az webapp config appsettings list `
  --name queenzone-devbox `
  --resource-group Queenzone-Dev-RG `
  --query "[?starts_with(name, 'AzureAd')].{name:name, length:length(value)}" `
  -o table
```

Validate both app registrations without reading secret values:

```powershell
$adminRegistrations = @(
  @{
    Environment = "production"
    ClientId = "f6d32f3b-7a4e-4517-a4d1-0995caad8feb"
    Audience = "AzureADandPersonalMicrosoftAccount"
    RedirectUri = "https://www.queenzone.org/signin-oidc"
  },
  @{
    Environment = "dev"
    ClientId = "fe98ef8d-1b6d-47bd-83e1-190e483f121f"
    Audience = "AzureADMyOrg"
    RedirectUri = "https://dev.queenzone.org/signin-oidc"
  }
)

foreach ($expected in $adminRegistrations) {
  $actual = az ad app show --id $expected.ClientId `
    --query "{audience:signInAudience, redirectUris:web.redirectUris, idToken:web.implicitGrantSettings.enableIdTokenIssuance}" `
    -o json | ConvertFrom-Json

  if (-not $actual.idToken) {
    throw "$($expected.Environment) admin registration does not issue ID tokens."
  }
  if ($actual.audience -ne $expected.Audience) {
    throw "$($expected.Environment) admin registration has the wrong sign-in audience."
  }
  if ($actual.redirectUris -notcontains $expected.RedirectUri) {
    throw "$($expected.Environment) admin callback URI is missing."
  }

  Write-Host "$($expected.Environment) admin registration is valid."
}
```

Smoke after restart:

```powershell
Invoke-WebRequest -Uri https://www.queenzone.org/health -UseBasicParsing | Select-Object StatusCode
# Then open /admin in a browser and complete Entra sign-in with an allowlisted email.
```

## Client secret renewal (do this before 2028-07-01)

Secrets expire. When admin login starts failing with token/credential errors, or when approaching the renewal date above:

1. Create a **new** secret on the same app (keep the old one until App Service is updated):

   ```powershell
   $appId = "f6d32f3b-7a4e-4517-a4d1-0995caad8feb"
   # Capture stdout only (CLI may write WARNING to stderr)
   $cred = az ad app credential reset --id $appId --append --display-name "queenzone-prod-appservice-$(Get-Date -Format yyyyMMdd)" --years 2 -o json 2>$null | ConvertFrom-Json
   # $cred.password is the new secret — set it next; do not commit it
   ```

2. Update App Service (replace with the new password from step 1):

   ```powershell
   az webapp config appsettings set `
     --name queenzone-prod `
     --resource-group Queenzone-RG `
     --settings "AzureAd__ClientSecret=<new-secret>"
   ```

3. Restart and smoke-test admin login:

   ```powershell
   az webapp restart --name queenzone-prod --resource-group Queenzone-RG
   Invoke-WebRequest -Uri https://www.queenzone.org/health -UseBasicParsing | Select-Object StatusCode
   ```

4. After admin login works, remove the **old** secret in Entra portal (App registration → Certificates & secrets) or via `az ad app credential delete`.

5. Update the **Renew secret by** date in this document (add two years from the new secret’s creation, minus a safety buffer).

### Optional calendar reminder

Set a personal or team calendar reminder for **2028-06-01**: “Rotate QueenZone Admin Entra client secret (see `docs/architecture/entra-admin-auth.md`).”

There is no automated Azure alert in this repo for app-secret expiry; treat this doc + calendar as the control.

## Local development

Local Development may leave `AzureAd:ClientId` empty and use `X-Test-User-Email` with allowlisted emails (see README). Do not copy the production client secret into git-tracked files.

`appsettings.Local.json` is loaded only in Development.

## Failure modes

| Symptom | Likely cause |
| --- | --- |
| App fails to start after deploy | Missing/placeholder `AzureAd__ClientId` on App Service (Phase A fail-closed) |
| Entra login error on redirect | Redirect URI not registered, or wrong ClientId |
| `AADSTS700054` after Entra sign-in | ID-token issuance is disabled on the environment's Entra app registration |
| Direct `GET /signin-oidc` returns 500 | Expected malformed callback: enter through `/admin`; the valid Entra callback is a state-bearing `POST` |
| Signed in but 403 on `/admin` | Email not in `Admin:AllowedEmails` (check claim `email` / `preferred_username`) |
| Signed in as a member but challenged again on `/admin` | Expected: member OAuth is separate; complete the QueenZone Admin Microsoft sign-in |
| Sudden admin login failure after long uptime | **Expired client secret** — follow renewal steps above |

## Related docs

- `docs/architecture/azure-hosting-plan.md` — hosting overview and hardening summary  
- `docs/agent-handoff-cheatsheet.md` — production debugging shortcuts  
- README — local admin test-header workflow  
