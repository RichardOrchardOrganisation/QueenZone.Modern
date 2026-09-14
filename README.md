# QueenZone Modern

This repository contains the new QueenZone modernization project.

The goal is to restart QueenZone as a clean, modern public site that fully exposes the valuable legacy archive while also making news the first live editorial slice. The public archive should be read-only for visitors, but approved editors will be able to add new news articles once the news workflow is in place.

## Repository Shape

`QueenZone.Web` uses ASP.NET Core Razor Pages for server-rendered public and admin UI. Keep page rendering in `Pages/` with `.cshtml` and page-model classes; use endpoint routes only for small non-page endpoints such as health checks or future APIs.

```text
/
  QueenZone.sln
  src/
    QueenZone.Data/
      Entities/ Repositories/ Sql/ SampleData/ SharedStores/ Migrations/
    QueenZone.NewsAgent/
    QueenZone.NewsAgent.Worker/
    QueenZone.Storage/
    QueenZone.Tools/
    QueenZone.Web/
      Auth/ Caching/ Content/ Endpoints/ Admin/ Member/ Forum/ Infrastructure/
      Pages/ Health/ Sitemap/ wwwroot/
    QueenZone.Mobile/            # Expo React Native client (not in QueenZone.sln)
  tests/
    QueenZone.Storage.Tests/
    QueenZone.Tools.Tests/
    QueenZone.NewsAgent.Tests/   # agent unit tests
    QueenZone.Web.Tests/         # HTTP + composition
    QueenZone.Web.E2E/
  docs/
    architecture/
    sql/                         # reviewable proc/FTS SQL
    legacy/
    decisions/
    backlog/
  scripts/
    Smoke-NewsAgent.bat
    Smoke-LiveSite.ps1
```

C# namespaces remain `QueenZone.Data` / `QueenZone.Web` (folders are for navigation only).

The native mobile client lives at `src/QueenZone.Mobile/` and uses its own Node
toolchain (Expo SDK 57, development builds). It is not part of `QueenZone.sln`.
Local setup for Android and iOS: [`src/QueenZone.Mobile/README.md`](src/QueenZone.Mobile/README.md).
Decisions: [`docs/decisions/0011-mobile-project-location-and-build-tooling.md`](docs/decisions/0011-mobile-project-location-and-build-tooling.md).

## Local Development

The app targets .NET 10.

The planned React Native client uses Node 24 LTS, JDK 17, and Android API 36.
See the [mobile development environment guide](docs/mobile-development-environment.md)
for the Windows and macOS toolchain.

```powershell
dotnet restore QueenZone.sln
dotnet build QueenZone.sln
dotnet test QueenZone.sln
dotnet run --project src/QueenZone.Web/QueenZone.Web.csproj
```

ImageSharp 4 requires a Six Labors licence at build time. Authorised development
agents on Richard's Windows or macOS machines should import the shared licence
from the `Queenzone Development` Bitwarden project without printing it:

```powershell
# Windows PowerShell or cross-platform pwsh, from the repository root
. ./scripts/Import-SixLaborsLicense.ps1
dotnet restore QueenZone.sln

# Optional on Windows: also persist it for new IDE and agent processes
. ./scripts/Import-SixLaborsLicense.ps1 -PersistForUser
```

The import lasts for the current PowerShell process. Restart Windows IDEs and
agents after using `-PersistForUser`. On macOS, run the import in each new `pwsh`
session; do not write the licence value to a shell profile. Hosted agents must
receive `SIXLABORS_LICENSE_KEY` through their platform secret manager before the
automatic restore runs. Do not give cloud environments a local Bitwarden machine
token.

GitHub Actions uses a repository secret with the same name. External contributors
are not authorised to use the shared QueenZone licence and must obtain their own
licence from [Six Labors](https://licensing.sixlabors.com/). Full setup, renewal,
and troubleshooting guidance: [`docs/agent-bitwarden-secrets.md`](docs/agent-bitwarden-secrets.md#six-labors-build-licence).

To generate a local code coverage report:

```powershell
dotnet tool restore
dotnet test QueenZone.sln --configuration Release --collect:"XPlat Code Coverage" --settings coverlet.runsettings --results-directory ./TestResults
dotnet tool run reportgenerator -reports:".\TestResults\**\coverage.cobertura.xml" -targetdir:".\coverage-report" -reporttypes:"HtmlInline;Cobertura;MarkdownSummary"
```

Open `coverage-report/index.html` to inspect the report. Coverage reports and raw test result folders are local artifacts and should not be committed.

Local secrets belong in `src/QueenZone.Web/appsettings.Local.json`, which is ignored by git. You can also set `ConnectionStrings__QueenZoneLegacy` in your shell or a local `.env` file for tooling that loads dotenv values. If no `ConnectionStrings:QueenZoneLegacy` value is present, the site uses sample news data so the first slice can still run locally.

Bitwarden **Secrets Manager** (`bws` CLI — not the password vault `bw`) is the shared local secret store for development agents. Use machine account **`windows-codex`** on Windows or **`mac-codex`** on Macs; project **`Queenzone Development`**. Auth is user-scoped **`BWS_ACCESS_TOKEN`**. Do not commit or print that token.

On this Windows workstation, `bws` is installed at `%USERPROFILE%\bin\bws.exe` (User `Path` includes `%USERPROFILE%\bin`). Full install, Mac setup, and troubleshooting: [`docs/agent-bitwarden-secrets.md`](docs/agent-bitwarden-secrets.md).

```powershell
$env:BWS_ACCESS_TOKEN = [Environment]::GetEnvironmentVariable("BWS_ACCESS_TOKEN", "User")
bws project list
bws secret list "1c16fd2d-4bfb-4eb7-8357-b49400233490"
```

When copying settings from Bitwarden to local app settings, keep App Service setting names as canonical (`ConnectionStrings__QueenZoneLegacy`, `AzureAd__ClientSecret`, `OPENROUTER_API_KEY`, etc.) and translate to JSON sections only in ignored local files. Azure App Service settings and GitHub environment secrets remain separate runtime/deploy stores; Bitwarden mirrors them for local development and recovery, but changing Bitwarden alone does not update Azure or GitHub.

### Google Analytics dashboard traffic

The admin dashboard can show GA4 traffic data when server-side credentials are configured. Local development works without these values; the dashboard shows an unavailable state instead of calling Google Analytics.

Configure these as local secrets or app settings, never in committed files:

```text
Analytics__GoogleAnalyticsPropertyId=<GA4 property id>
Analytics__GoogleAnalyticsServiceAccountJson=<service account JSON key>
```

Grant the service account Viewer access to the GA4 property. The dashboard caches GA4 responses for at least 60 minutes.

### Using live legacy data locally

Live-data development is opt-in. Use it when you need to reproduce a production-only data mapping, SQL, or admin editorial issue. Prefer a local SQL Server copy for repeat debugging so normal local work does not depend on Azure SQL latency, firewall rules, or live database load.

#### Option A: point at a local SQL Server copy

Install or use a local SQL Server instance such as SQL Server Express. On Richard's workstation the instance is:

```text
glory11\sqlexpress
```

Export the Azure SQL database to a BACPAC. Read the source connection string from an ignored local settings file, or paste it into a temporary shell variable; do not print it to the console or commit it.

```powershell
$settings = Get-Content -Raw .\src\QueenZone.Web\appsettings.Local.json | ConvertFrom-Json
$sourceConnectionString = [string]$settings.ConnectionStrings.QueenZoneLegacy

New-Item -ItemType Directory -Force C:\Backups | Out-Null
SqlPackage /Action:Export `
  /SourceConnectionString:$sourceConnectionString `
  /TargetFile:C:\Backups\queenzone-live.bacpac `
  /p:CommandTimeout=1200
```

Import the BACPAC into SQL Express:

```powershell
SqlPackage /Action:Import `
  /SourceFile:C:\Backups\queenzone-live.bacpac `
  /TargetConnectionString:"Server=glory11\sqlexpress;Initial Catalog=QueenZoneLocal;Integrated Security=True;Encrypt=False;TrustServerCertificate=True;" `
  /p:CommandTimeout=1200
```

Verify the import:

```powershell
sqlcmd -S "glory11\sqlexpress" -E -d QueenZoneLocal -Q "SELECT DB_NAME() AS DatabaseName; SELECT COUNT_BIG(*) AS NewsRows FROM dbo.NEWS_T;"
```

Point the web app at the local copy in `src/QueenZone.Web/appsettings.Local.json`:

```json
{
  "ConnectionStrings": {
    "QueenZoneLegacy": "Server=glory11\\sqlexpress;Database=QueenZoneLocal;Integrated Security=True;Encrypt=False;TrustServerCertificate=True;"
  },
  "AzureAd": {
    "ClientId": ""
  },
  "Admin": {
    "AllowedEmails": [
      "richard@thinkingwebsites.com.au",
      "me@richardorchard.com"
    ]
  }
}
```

SQL Server Express has a 10 GB database-size limit. The imported QueenZone copy was about 3 GB on 2026-07-01, so Express was sufficient then. Use SQL Server Developer edition if the local copy grows beyond the Express limit.

#### Option B: point directly at Azure SQL

Create `src/QueenZone.Web/appsettings.Local.json` and add the live Azure SQL connection string under `ConnectionStrings:QueenZoneLegacy`. This file is ignored by git; do not commit it, paste it into a pull request, or include it in logs.

For local admin testing without real Entra sign-in, also set `AzureAd:ClientId` to an empty string and include your admin email in `Admin:AllowedEmails`:

```json
{
  "ConnectionStrings": {
    "QueenZoneLegacy": "Server=tcp:...;Database=...;User ID=...;Password=...;Encrypt=True;TrustServerCertificate=False;"
  },
  "AzureAd": {
    "ClientId": ""
  },
  "Admin": {
    "AllowedEmails": [
      "richard@thinkingwebsites.com.au",
      "me@richardorchard.com"
    ]
  }
}
```

Run the web app normally:

```powershell
dotnet run --project src/QueenZone.Web/QueenZone.Web.csproj
```

To exercise admin pages locally with the test-header fallback, send the allowlisted email header:

```powershell
Invoke-WebRequest `
  -Uri http://localhost:5146/admin/news `
  -Headers @{ "X-Test-User-Email" = "richard@thinkingwebsites.com.au" } `
  -UseBasicParsing
```

**Admin auth environments**

| Environment | `AzureAd:ClientId` | Admin sign-in |
| --- | --- | --- |
| Development | Empty / placeholder | `X-Test-User-Email` test header (allowlisted emails only) |
| Testing | Any | Test auth handlers for automated tests |
| Staging / Production | **Required real Entra client ID** | Microsoft Entra (OIDC). App **fails to start** if ClientId is missing or still `YOUR_CLIENT_ID`. |

`TestAuthHandler` is never registered outside Development/Testing. Do not set `ASPNETCORE_ENVIRONMENT=Production` locally without a real Entra app registration.

Production App Service Entra app, settings, and **client secret renewal** (renew by **2028-07-01**): [`docs/architecture/entra-admin-auth.md`](docs/architecture/entra-admin-auth.md).

**Allowed hosts**

Committed `appsettings.json` sets `QueenZoneHostFiltering:AllowedHosts` to the production hosts (`www.queenzone.org;queenzone.org;*.azurewebsites.net`). Development and Testing override it with `"*"`. ASP.NET Core's framework-level `AllowedHosts` stays `"*"` deliberately: QueenZone applies its own equivalent filter after the App Service probe short-circuit, because Azure startup pings use an internal Host header. Override `QueenZoneHostFiltering:AllowedHosts` in App Service when adding domains.

If you already have `src/QueenZone.NewsAgent.Worker/appsettings.Local.json` configured with `ConnectionStrings:QueenZoneLegacy`, you can copy that value into the web app's local settings. Keep both files local-only.

The local SQL copy may contain production user, mail, IP, moderation, and private-message data. Treat local BACPAC and MDF/LDF files as sensitive data: store them outside the repository, do not attach them to issues or pull requests, and delete or refresh them deliberately when they are no longer needed.

### News agent (discovery worker)

The news agent fetches configured public sources, triages items with OpenRouter, and stores candidates for the admin review queue. Admins can also submit a single public article URL for forced triage. Scheduled gathering and default URL actions stop after triage; AI drafts require an explicit per-candidate action or an explicit “generate AI draft” choice on URL submit. Nothing publishes to public pages automatically.

Full setup, worker flags, and admin review UI are documented in `docs/architecture/news-agent.md`.

Quick start:

```powershell
copy src\QueenZone.NewsAgent.Worker\appsettings.Local.json.example src\QueenZone.NewsAgent.Worker\appsettings.Local.json
# Edit appsettings.Local.json: set OpenRouter:ApiKey and optionally ConnectionStrings:QueenZoneLegacy

dotnet run --project src/QueenZone.NewsAgent.Worker -- discover-news --scheduled
```

OpenRouter smoke test (Windows): double-click `scripts/Smoke-NewsAgent.bat`.

Post-deploy live site smoke (custom domain): separate job at the end of the App Service deploy workflow (`build` → `migrate` → `deploy` → `smoke`) against `https://www.queenzone.org`. Re-run locally with `powershell -File .\scripts\Smoke-LiveSite.ps1`.

Admin review queue (after signing in as an allowed admin): `/admin/news-discovery`. **Queue news gathering** and **Submit article URL** write requests to the shared database for the local Windows runner (`process-news-requests`); see `docs/architecture/news-agent-scheduling.md`. Selected candidates can then have a draft generated, edited, and published through the existing `/admin/news` workflow.

The hosted App Service currently connects to Azure SQL with SQL authentication. For local development, use a local-only SQL auth connection string or another explicitly granted development principal:

```json
{
  "ConnectionStrings": {
    "QueenZoneLegacy": "Server=tcp:queenzone-prod-sql.database.windows.net,1433;Database=queenzone-db;User ID=...;Password=...;Encrypt=True;TrustServerCertificate=False;",
    "BlobStorage": "DefaultEndpointsProtocol=https;AccountName=...;AccountKey=...;EndpointSuffix=core.windows.net"
  }
}
```

User-generated content uploads use `QueenZone.Storage` (`IBlobUploadService`). Leave `ConnectionStrings:BlobStorage` empty for local runs without Azure; see `docs/architecture/blob-storage-ugc.md`.

Do not commit real usernames, passwords, publish profiles, or copied Azure setting values. If a SQL password contains semicolons or other connection-string delimiters, wrap it using the standard connection-string escaping rules before saving it in Azure or local settings.

Agents can use Azure Data API Builder as a local SQL MCP server for controlled read-only legacy database investigation. Setup lives in `docs/sql/data-api-builder-mcp.md`; the DAB tool, config, and smoke-test artifacts should stay under ignored local paths such as `.tools`.

## Testing And Workflow

Follow the layered testing policy in `docs/architecture/testing-policy.md`.

Default verification:

```powershell
dotnet restore QueenZone.sln
dotnet build QueenZone.sln --configuration Release --no-restore
dotnet test QueenZone.sln --configuration Release --no-build
```

CI also publishes a code coverage artifact for pull requests and manual CI runs. Use it to guide test review around risky changes, especially canonical routing, publication rules, legacy data mapping, and HTML sanitisation.

A Playwright browser smoke suite (`tests/QueenZone.Web.E2E`) runs in CI on whichever self-hosted runner carrying the `e2e` label is available (currently Windows or macOS), so it does not consume GitHub Actions minutes. It is a required pull-request merge gate and is not rerun during deployment. The suite covers public homepage/news/forum journeys, mobile nav, axe-core critical a11y checks, and admin/editorial smoke. See `docs/architecture/self-hosted-e2e-runner.md` for runner setup and local commands. On failure, screenshots and traces land in `test-results/e2e/` (uploaded as CI artifacts). Run it locally with `powershell -File .\scripts\Run-E2E.ps1 -Mode Deterministic` (see `docs/architecture/testing-policy.md` for the separate nightly `-Mode RealData` tier against real legacy data).

For advisory frontend performance baselines (LCP, CLS, transfer size, request count on key public pages), use the Lighthouse workflow:

```powershell
powershell -File .\scripts\Measure-FrontendPerformance.ps1 -StartLocalApp -FormFactor mobile
```

Results land under `docs/performance/results/` (gitignored). Budgets and comparison guidance: `docs/performance/frontend-performance-checks.md`.

Normal CI and pull request checks should not require the restored legacy database. Real legacy database checks are opt-in until a controlled test database exists.

If you change admin news write behavior or discovery promotion behavior, run the opt-in legacy write probe against the nightly-refreshed SQL Express mirror:

```powershell
$env:RUN_LEGACY_WRITE_PROBE = "true"
.\scripts\Probe-AdminNewsLegacyWrites.ps1
```

The script refuses Azure SQL, remote servers, and databases other than `queenzone_legacy_sync`. It runs the admin news write lifecycle probe and the news-section `Admin_news_*` write Facts. The nightly Windows write job runs the same script after the mirror refresh and read probes, then checks for residue.

Feature work should happen on an agent-prefixed branch such as `grok/news-pagination` and be reviewed through a pull request before it reaches `main`. See `AGENTS.md` for the branch and PR policy.

## Deployment

Every qualifying web merge to `main` deploys automatically to the isolated dev environment through `.github/workflows/deploy-dev.yml`; workflow/docs-only and mobile-only merges skip the web deploy. The production `Deploy` workflow (`.github/workflows/deploy.yml`) runs only for a `v*` tag or a manual dispatch from `main`. Production is the Canada East `queenzone-prod` App Service at `https://www.queenzone.org`.

`.github/workflows/ci.yml` runs the required build, test, coverage, migration consistency, smoke, and Playwright checks on pull requests. The dev and production deploy workflows reuse the tested `web-publish` artifact when it is still present. If resolve cannot find that zip and a website Deploy is still required, they publish from the checked-out `main`/tag SHA. Manual dispatch and `v*` tags skip when the classified range is mobile-only or otherwise non-web — they do not walk back to an older web tip or rerun CI.

### Promote a verified dev release to production

1. Merge the PR and wait for `deploy-dev.yml` to complete.
2. Verify the change at `https://dev.queenzone.org`.
3. Tag that exact commit on `main`, then push the tag:

```powershell
git checkout main
git pull --ff-only origin main
git tag vX.Y.Z <verified-commit-sha>
git push origin vX.Y.Z
```

The `v*` tag triggers the production workflow. That workflow classifies the previous-tag…this-tag span (skip when the entire span is non-web), resolves the tagged commit back to the PR head SHA, and deploys the matching CI `web-publish` artifact when it is still present. If that zip is gone and the span includes website changes, it publishes from the tagged SHA instead of walking back to an older web tip. You can also open **Actions → Deploy → Run workflow**, select `main`, and run the same production deployment without creating a tag (dispatch classifies tip vs previous `main` commit and skips a mobile-only tip).

Dev runs deterministic sample data until the approval-gated [curated snapshot workflow](docs/architecture/dev-curated-snapshot.md) completes. That workflow builds a production-compatible legacy schema, loads a capped sanitised public sample, copies only manifest assets into `queenzonedev`, rebuilds search, and enables the dev-only connection after size, privacy, smoke, and browser checks pass. The dev App Service setting `DevSnapshot__Ready` keeps later `deploy-dev.yml` runs in sample mode until that proof exists.

The planned public canonical domain for the site is `https://www.queenzone.org`. SEO features that emit absolute public URLs, such as sitemaps and robots.txt, should use that host in production configuration.

Set `Site:PublicBaseUrl` in App Service configuration (or `appsettings.Local.json` for local overrides) when the public host differs from the default in `appsettings.json`.

Production GitHub Environment configuration (`prod-release` for migrate + zip deploy) required:

- Secret `BITWARDEN_SECRETS_MANAGER_ACCESS_TOKEN`: authorizes the workflow to read the QueenZone Bitwarden Secrets Manager project.
- Variable `BITWARDEN_APP_SERVICE_DEPLOY_SECRETS`: maps the Bitwarden secret IDs to `AZURE_WEBAPP_PUBLISH_PROFILE` and `QUEENZONE_LEGACY_MIGRATION_CONNECTION_STRING`. See `docs/bitwarden-secrets.md` for the mapping and rotation procedure. The migration connection is separate from the App Service runtime connection and should use a principal with permission to create or alter tables.

See [`docs/architecture/github-environments.md`](docs/architecture/github-environments.md) for the full environment map (`prod-release`, `prod-deploy`, `prod-google-play`, `prod-data-read`). Do not put production secrets on legacy `dev` / `deploy`.

App Service configuration required:

- `ConnectionStrings__QueenZoneLegacy`: the Azure SQL connection string for the copied legacy tables. The current runtime path uses SQL authentication against `queenzone-db` on `queenzone-prod-sql.database.windows.net`.
- `APPLICATIONINSIGHTS_CONNECTION_STRING`: the Application Insights connection string for production/preview telemetry. Leave unset in local development unless local telemetry is intentional.

Application Insights is wired through Azure Monitor OpenTelemetry and is opt-in at runtime: no telemetry is exported unless `APPLICATIONINSIGHTS_CONNECTION_STRING` is present. The default `ApplicationInsights` settings in `src/QueenZone.Web/appsettings.json` are deliberately low-volume for a hobby project: at most 0.2 traces per second, warning-or-higher exported logs, trace-based log sampling enabled, and Live Metrics disabled. In Azure, also set a small daily cap on both the Application Insights resource and its Log Analytics workspace, for example 0.05-0.10 GB/day, so unexpected traffic or noisy logging cannot run up a large bill.

The runtime App Service setting and the GitHub Actions migration secret are intentionally separate. Updating `QUEENZONE_LEGACY_MIGRATION_CONNECTION_STRING` in GitHub does not update the live App Service setting. If the database name, login, or password changes, update both places as needed and restart the App Service so the running container reloads the connection string.

The runtime SQL login should be created in the target database and granted only the permissions the app needs. Public archive reads need read access to the legacy news tables. Admin news publishing writes to `NEWS_T` and `NewsAuditLog`, so the runtime login also needs scoped write permissions once the admin workflow is enabled.

Example permission shape:

```sql
CREATE USER [app_login_name] FOR LOGIN [app_login_name];
ALTER ROLE db_datareader ADD MEMBER [app_login_name];
ALTER ROLE db_datawriter ADD MEMBER [app_login_name];
```

Keep read-only environments on `db_datareader` only.

The production `Deploy` workflow applies pending EF Core migrations when required, then deploys the existing CI artifact; it does not rebuild. Pull-request tests have already passed in the separate `CI` workflow and are not rerun here. Pre-merge CI applies migrations to the SQL Express mirror only; production Azure SQL `database update` is this workflow's `migrate` job. Configure the Bitwarden access secret and deploy-secret mapping in the GitHub `prod-release` environment as described above.

For manual bootstrap or recovery, you can still run:

```text
docs/sql/001-news-admin-columns.sql
```

or:

```powershell
dotnet tool restore
dotnet ef database update --project src/QueenZone.Data/QueenZone.Data.csproj --startup-project src/QueenZone.Web/QueenZone.Web.csproj
```

Public and admin SQL access uses EF Core (`QueenZoneDbContext`). Hot paths keep stored procedures, invoked via EF (`SqlQuery` / `SqlQueryRaw` / EF-managed proc calls). See ADR 0006.

### Member authentication

Member sign-in at `/account/login` supports Google, Microsoft, Discord, GitHub and Apple. A provider appears only when its complete configuration is present. Member OAuth is separate from the Microsoft Entra sign-in used for admin access.

The standard providers require `Authentication__{Provider}__ClientId` and `Authentication__{Provider}__ClientSecret`. Their callback paths are `/signin-google`, `/signin-microsoft`, `/signin-discord` and `/signin-github`.

Apple uses a generated client-secret JWT instead of a stored, long-lived client secret. Configure these App Service settings and matching Bitwarden secret names:

```text
Authentication__Apple__ClientId
Authentication__Apple__TeamId
Authentication__Apple__KeyId
Authentication__Apple__PrivateKey
```

`ClientId` is the Apple Services ID. `PrivateKey` is the full `.p8` PEM value; either real newlines or escaped `\n` separators are accepted. Never commit or log the private key. The app signs and caches Apple client-secret JWTs from this key, bounded by Apple's six-month limit.

Apple Developer setup:

1. Enable Sign in with Apple on a primary App ID.
2. Create and link a Services ID for the website.
3. Register the production domain and exact HTTPS return URL `https://www.queenzone.org/signin-apple`. Register any Azure hostname used for pre-production testing separately. Apple does not accept `localhost` return URLs.
4. Create a Sign in with Apple private key. Record its Key ID and the developer Team ID, then store the `.p8` value in Bitwarden and App Service.
5. If QueenZone sends mail to Apple relay addresses, register and authenticate its outbound email domain with Apple.

Apple may provide the member's name only on their first authorization. The callback saves that first name payload when creating an account, then relies on Apple's stable subject for later sign-ins. Hide My Email relay addresses are supported; account lookup uses the provider subject before email linking.

### Admin authentication

Admin routes require a signed-in user whose email address is listed in `Admin:AllowedEmails`.
In production, the identity must come from the dedicated Microsoft Entra ID admin sign-in. A member login does not grant admin access, even when both identities use the same email address.

Committed `appsettings.json` ships an **empty** allowlist on purpose. Production must set the list on App Service (or Key Vault); Development uses git-ignored `appsettings.Local.json`. See `docs/architecture/entra-admin-auth.md`.

1. Create an Entra app registration for `QueenZone.Web`.
2. Add a web redirect URI such as `https://queenzone-prod.azurewebsites.net/signin-oidc`.
3. Create a client secret for the app registration.
4. Configure these App Service settings:

```text
AzureAd__TenantId
AzureAd__ClientId
AzureAd__ClientSecret
Admin__AllowedEmails__0
Admin__AllowedEmails__1
```

Use `src/QueenZone.Web/appsettings.Local.json` for local Entra values and local admin emails. If `AzureAd:ClientId` is empty locally, the app falls back to test-header authentication for development only.

Do not log secrets (connection strings, client secrets, storage keys, API keys) to App Insights, issues, or PR text. When auditing App Service settings, prefer name + value length over printing values.

For automated admin tests, send `X-Test-User-Email` with an allowed admin email address. The member OAuth callback test double only creates a member session and must not grant admin access, even when its email is allowlisted.

Member sign-in is separate from admin access. Removing a member provider does not remove the admin requirement: admins still need Entra sign-in in production, or the test-header fallback locally as described above.

Admin editorial surfaces:

- `/admin/news` — create, edit, preview, publish news articles
- `/admin/news-discovery` — review discovered candidates and AI-generated drafts (see `docs/architecture/news-agent.md`)
- `/admin/forum-reports` — review reported forum posts (see `docs/forum-report-moderation.md`)

Do not commit publish profiles, `.pubxml` files, local app settings, or connection strings. Rotate the App Service publish profile if it has ever been saved outside GitHub Secrets.

## First Milestone

Build the first vertical slice around news. This slice should prove both archive rendering and the path for new approved news articles:

1. Connect to the restored legacy SQL Server database.
2. Read archived published rows from `NEWS_T`, either directly or through `Q_NEWS_*` stored procedures.
3. Add separate modern tables for newly approved live news articles and draft/review workflow.
4. Render latest news on the homepage as one seamless list across archived and new articles.
5. Render `/news` archive across both archived and new articles.
6. Render `/news/{id}/{slug}` detail pages without exposing the storage split to visitors.
7. Use stable, search-friendly canonical URLs.
8. Keep automated discovery and AI-assisted drafts behind explicit editorial approval.
9. Deploy a preview to Azure App Service.

## Legacy Reference Policy

Keep the old repository available locally as an archaeological reference, but do not copy it wholesale into the new repo.

Useful reference files belong in `docs/legacy`:

- `db-schema.sql`
- table map
- stored procedure index
- legacy URL notes, only when they help understand content relationships
- content inventory

The old source is only copied or ported when a specific page or behavior needs to be understood.

## License

Source code authored for QueenZone Modern is licensed under the [MIT License](LICENSE).

The MIT License does not cover archive content, database records, user submissions,
photographs, audio, video, lyrics, editorial material, logos, trademarks, or
third-party components. Those materials remain subject to their respective rights
and licences. See [Third-Party Notices](THIRD-PARTY-NOTICES.md) for key bundled and
direct dependencies.

### Dev Azure environment

The isolated dev App Service, database, storage, and managed TLS are declared
in `infra/environments/dev`, with a separate state key; the shared Cloudflare
DNS record remains in the production root. Run
`./scripts/Test-OpenTofu.ps1` to validate both infrastructure roots. See the
[dev environment runbook](docs/architecture/opentofu-dev-environment.md) for
the current runtime boundary and historical provisioning evidence. Application
deployment is automatic through `deploy-dev.yml`; production deployment is a
separate tag or manual action.
