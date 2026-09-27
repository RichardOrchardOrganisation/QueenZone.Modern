# Agent Handoff Cheat Sheet

Quick reference for starting fresh sessions on long-running QueenZone migration work.

## When To Start A Fresh Session

**Do it when:**

- Moving to a new major GitHub issue or content area
- After merging a pull request
- Starting route, canonical URL, or public rendering work
- After a long exploration or legacy SQL investigation phase
- The agent is losing precision on hard constraints

**You can usually continue when:**

- Still deep in one focused sub-task on the same issue
- Only a small, well-understood tweak is needed

**Pro tip:** Leave a `TODO` or `NOTE` comment in the code marking the exact resumption point.

## Agent Branch Prefixes

Agent branches use `{prefix}/{description}`. GitHub Actions adds an `agent:{prefix}` label to the pull request.

| Agent / tool    | Prefix      | Example branch                 |
| --------------- | ----------- | ------------------------------ |
| Grok            | `grok/`     | `grok/news-pagination`         |
| Claude Code     | `claude/`   | `claude/seo-foundation`        |
| Codex           | `codex/`    | `codex/legacy-news-dedup`      |
| Cursor Composer | `composer/` | `composer/health-smoke-tests`  |
| New tool        | `{name}/`   | `my-tool/forum-archive-review` |

Also fill in the **Agent** line when opening the pull request (see `.github/pull_request_template.md`).

## Category Quick Rules

| Category | Must remember |
| -------- | ------------- |
| **CI / Web.Tests speed** | CI uses **mixed** Web.Tests shards (`scripts/Get-WebTestShardFilter.ps1`). Never split unit-vs-WAF jobs for wall-clock — that regresses (#442). Local default stays `dotnet test QueenZone.sln`. |
| **Routes / canonical URLs** | `/news` is canonical page 1; use `/news/page/{n}` for later pages; wrong slugs redirect to canonical detail URLs; do not preserve legacy Web Forms URL shapes by default |
| **Legacy SQL / data** | Keep SQL in `QueenZone.Data`; treat `DISPLAY = 1` as the public visibility gate; deduplicate by stable article ID before paging; legacy DB tests are opt-in |
| **SQL MCP** | Local agents can expose a narrow read-only DAB MCP server for legacy DB inspection; see `docs/sql/data-api-builder-mcp.md`; do not expose private/user/mail/IP/moderation data |
| **Public rendering** | Never expose hidden, deleted, moderated, draft, or private records in public output |
| **SEO / crawlability** | Unique page titles, canonical links, and crawlable HTML matter; avoid duplicate canonical pages |
| **Testing** | Pure logic in unit tests; route behavior in web integration tests with sample/fake data by default; report whether legacy DB checks ran |
| **Secrets / deploy** | No secrets in git; Bitwarden Secrets Manager project `Queenzone Development` mirrors local/App Service development secrets for agents; App Service runtime uses `ConnectionStrings__QueenZoneLegacy`; GitHub migrations use the separate `QUEENZONE_LEGACY_MIGRATION_CONNECTION_STRING` secret; say what was tested before merge |
| **News agent** | Worker: `QueenZone.NewsAgent.Worker` + `discover-news` / `process-news-requests`; secrets in `appsettings.Local.json`; smoke: `scripts/Smoke-NewsAgent.bat`; review: `/admin/news-discovery` (queue gathering + submit URL); live URL probe: `scripts/Probe-NewsAgentUrlIngestion.ps1`; never auto-publish to public `/news` |

## Most-Used Minimal Template

```markdown
Resume #[ISSUE_NUMBER] on `[agent]/[short-desc]`.

Status: [concise state]. Next: [exact next step or TODO location].

Key rules:

- Use the correct agent branch prefix (`grok/`, `claude/`, `codex/`, etc.).
- Reference #[ISSUE_NUMBER] in commits when useful.
- Fill in the PR **Agent** line when opening the pull request.
- Run the default dotnet build/test gate before handoff.
- Report whether legacy database checks were run or skipped.

Read the TODO in [specific file] and the GitHub issue first.
```

## Production Debugging (Azure)

App Service: `queenzone-prod` in resource group `Queenzone-RG` (Canada East).

Public hostnames: `queenzone.org`, `www.queenzone.org`, `queenzone-prod.azurewebsites.net`.

### Bitwarden Secrets Manager

Use **`bws`** (Secrets Manager), **not** password-manager **`bw`**. Full guide: [`agent-bitwarden-secrets.md`](agent-bitwarden-secrets.md).

| | |
| --- | --- |
| Project | `Queenzone Development` (`1c16fd2d-4bfb-4eb7-8357-b49400233490`) |
| Windows machine account | `windows-codex` — this PC: `bws` at `%USERPROFILE%\bin\bws.exe`, User `Path` includes `%USERPROFILE%\bin`, token in User env `BWS_ACCESS_TOKEN` |
| macOS machine account | `mac-codex` — separate token; install `bws` via official script or release zip; ensure `PATH` + `BWS_ACCESS_TOKEN` in the agent environment |

```powershell
$env:BWS_ACCESS_TOKEN = [Environment]::GetEnvironmentVariable("BWS_ACCESS_TOKEN", "User")
bws secret list "1c16fd2d-4bfb-4eb7-8357-b49400233490"
```

Keep App Service setting names canonical in Bitwarden (`ConnectionStrings__QueenZoneLegacy`, `AzureAd__ClientSecret`, `OPENROUTER_API_KEY`, etc.). Do not print secret values. Bitwarden is a local/recovery mirror; Azure App Service settings and GitHub environment secrets still need explicit updates when credentials rotate. OpenTofu stays out of `app_settings`/`connection_string` entirely — see [ADR 0008](decisions/0008-app-service-settings-ownership.md) — so nothing here changes as OpenTofu import work lands.

### Scale / cache (single instance — no Redis)

- Plan **ASP-Queenzone-Prod**: **B1 Basic**, **1 worker**. Do not assume scale-out.
- Process-local memory/output cache and invalidation are intentional.
- **No Azure Cache for Redis** while this cost model holds.
- Decision + archived issues: [`docs/architecture/hosting-scale-and-cache.md`](architecture/hosting-scale-and-cache.md).

### Admin Entra auth (required for Production)

Full runbook: [`docs/architecture/entra-admin-auth.md`](architecture/entra-admin-auth.md).

- Entra app: **QueenZone Admin** (`f6d32f3b-7a4e-4517-a4d1-0995caad8feb`).
- App Service must define `AzureAd__ClientId` / `AzureAd__ClientSecret` / related keys (not the committed placeholders).
- **Client secret:** created 2026-07-23 (2 years) — **renew by 2028-07-01** (see runbook).
- Admin access is still gated by `Admin:AllowedEmails` after Entra sign-in.
- Member Microsoft login uses a **different** app (`Authentication__Microsoft__*`); do not confuse the two.

```powershell
# Confirm AzureAd settings exist (lengths only — do not dump secrets)
az webapp config appsettings list `
  --name queenzone-prod `
  --resource-group Queenzone-RG `
  --query "[?starts_with(name, 'AzureAd')].{name:name, length:length(value)}" `
  -o table
```

### Azure CLI (preferred for agents)

Requires `az login` as a user with access to subscription **Base Thinking**.

Live log stream:

```powershell
az webapp log tail --name queenzone-prod --resource-group Queenzone-RG
```

Download recent logs:

```powershell
az webapp log download --name queenzone-prod --resource-group Queenzone-RG --log-file appservice-logs.zip
```

Quick health checks:

```powershell
az webapp show --name queenzone-prod --resource-group Queenzone-RG --query "{hostNames:hostNames, state:state}"
Invoke-WebRequest -Uri https://www.queenzone.org/health -UseBasicParsing | Select-Object StatusCode
Invoke-WebRequest -Uri https://www.queenzone.org/warmup -UseBasicParsing | Select-Object StatusCode
```

Post-deploy smoke (custom domain) is a separate job at the end of `.github/workflows/deploy.yml` (`resolve-ci-run` in parallel with `migrate` → `deploy` → `post-deploy-smoke`). Production deploy runs only for a `v*` tag or a manual Actions dispatch from `main`; normal qualifying web merges deploy to dev through `deploy-dev.yml`. Production promotion classifies the tag span or dispatch tip (skip when that range is non-web). `resolve-ci-run` finds the `ci.yml` run that already built and tested the selected commit's PR head and `deploy` reuses its `web-publish` artifact when present; otherwise it publishes from the checked-out SHA. It does not walk back to an older web tip. The deploy job zip-pushes (plain extract mode — `WEBSITE_RUN_FROM_PACKAGE` was removed after it repeatedly served stale builds post-deploy), recycles the App Service via Kudu (`POST /api/app/restart` — required; #688's skip left `/warmup` 500), polls `/warmup` and the PR-head build stamp on `/`, then post-deploy smoke repeats the stamp check on the content routes. Pull-request tests are not rerun by deployment. Checks hit `https://www.queenzone.org` (`/warmup`, then `/health` plus key public routes); a failure fails the smoke job so GitHub Actions can notify watchers. Re-run the same checks locally:

```powershell
powershell -File .\scripts\Smoke-LiveSite.ps1
```

B1 App Service warmup settings (portal / ARM Application Settings, not Kudu):

```text
WEBSITE_WARMUP_PATH=/health
WEBSITE_WARMUP_STATUSES=200
```

`WEBSITE_WARMUP_PATH` is `/health`, not `/warmup` (#673). `/health` is the cheap platform liveness ping. `deploy.yml` still polls `/warmup` (and the new build stamp on `/`) to run readiness checks and prime public query caches (SQL connect bounded at 15s; nine cache steps run concurrently at 8s each — see [`azure-hosting-plan.md`](architecture/azure-hosting-plan.md)). Enable Always On on the App Service when the active SKU supports it; it reduces idle cold starts.

Application logging should be enabled at Information level on the filesystem. If the stream is quiet, hit the failing route to generate entries.

### Reproducing live database issues locally

Use live legacy data locally only for production-only SQL, mapping, or data-shape debugging. Prefer an imported local SQL Server copy when you will run repeated checks.

#### Local SQL Server copy

Export Azure SQL to a BACPAC, then import it to local SQL Server Express:

```powershell
$settings = Get-Content -Raw .\src\QueenZone.Web\appsettings.Local.json | ConvertFrom-Json
$sourceConnectionString = [string]$settings.ConnectionStrings.QueenZoneLegacy

New-Item -ItemType Directory -Force C:\Backups | Out-Null
SqlPackage /Action:Export `
  /SourceConnectionString:$sourceConnectionString `
  /TargetFile:C:\Backups\queenzone-live.bacpac `
  /p:CommandTimeout=1200

SqlPackage /Action:Import `
  /SourceFile:C:\Backups\queenzone-live.bacpac `
  /TargetConnectionString:"Server=glory11\sqlexpress;Initial Catalog=QueenZoneLocal;Integrated Security=True;Encrypt=False;TrustServerCertificate=True;" `
  /p:CommandTimeout=1200
```

Use this local web connection string:

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

#### Direct Azure SQL

The web app also reads `src/QueenZone.Web/appsettings.Local.json`, which is ignored by git. To connect directly to Azure SQL, put the live value in:

```json
{
  "ConnectionStrings": {
    "QueenZoneLegacy": "..."
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

Then run:

```powershell
dotnet run --project src/QueenZone.Web/QueenZone.Web.csproj
```

Admin routes can be exercised locally with `X-Test-User-Email` when `AzureAd:ClientId` is empty. Never print, commit, or paste the connection string into logs or pull requests. If `src/QueenZone.NewsAgent.Worker/appsettings.Local.json` already has `ConnectionStrings:QueenZoneLegacy`, copy that value into the web app local settings.

Local BACPAC and SQL Server data files contain production data. Keep them outside the repository, do not attach them to issues or pull requests, and treat them as sensitive.

### Azure MCP

The Azure MCP server must authenticate to tenant `c9f094fd-23bf-4a35-a406-bcaacd7e1a8e`. If tools return `InvalidAuthenticationTokenTenant`, run:

```powershell
az login --tenant c9f094fd-23bf-4a35-a406-bcaacd7e1a8e
```

Then restart the Azure MCP server in Cursor. `az account show` succeeding does not guarantee MCP is on the same tenant.

Useful MCP tools after auth is fixed:

- `subscription_list`
- `appservice` → `appservice_webapp_get` for site details
- `appservice` → `appservice_webapp_diagnostic_list` / `appservice_webapp_diagnostic_diagnose` for health probes

When MCP auth fails, fall back to Azure CLI commands above.

### Forum production smoke checks

Post-deploy smoke (`.github/workflows/deploy.yml`) already hits these public URLs. After a forum deploy, you can also verify manually:

- `GET https://www.queenzone.org/forum` → 200
- `GET https://www.queenzone.org/forum/1/queen-serious-discussion` → 200
- `GET https://www.queenzone.org/forum/topic/455095/forum-guidelines` → 200

In the log stream, `SqlException: Execution Timeout` in `LegacyForumRepository` usually means a full-table `COUNT(*)` on `Q_FORUM_TOPIC_T` or other heavy ad-hoc SQL. The modern read path should use denormalised `Q_FORUM_T.Q_FORUM_POST_COUNT`, `dbUser.Q_FORUM_TOPIC_THREAD_COUNT_V`, and `Q_FORUM_VIEW_PAGE_SP` instead. See `docs/legacy/table-map.md`.

### Modern forum read path configuration

`ForumData:UseModernForumReads` defaults to `true` in code and in published `appsettings.json`. **Absent App Service setting = modern default** — production does not need `ForumData__UseModernForumReads=true` unless you want an explicit override for ops visibility. Set `ForumData__UseModernForumReads=false` in App Service configuration only for an emergency rollback to `LegacyForumRepository`.

Registration coverage: `tests/QueenZone.Web.Tests/ForumDataRegistrationTests.cs` asserts the modern repository is registered by default and that the legacy repository can be selected for rollback.

### Modern forum import status

`docs/sql/004-modern-forum-batched-import.sql` creates the first modern forum archive read-model tables and resumable import procedures:

- `ModernForumCategory`
- `ModernForumThread`
- `ModernForumPost`
- `ImportCheckpoint`

It was applied to `queenzone-db` on 2026-06-29. The initial import used a too-narrow `Q_FORUM_TOPIC_PARENT_ID = 0` thread rule and was reset. The corrected import treats `TOPIC_STARTER = 1 OR Q_FORUM_TOPIC_PARENT_ID = 0` as legacy threads, recovers orphaned replies into synthetic thread containers, and carries attachment fields.

The corrected live run first hit Azure SQL error `40544` at 570,000 imported posts because the database reached its 2 GB size quota before the full 1,164,816-row `Q_FORUM_TOPIC_T` corpus could fit alongside the legacy schema. The partial `ModernForum*` tables were reset, and `DBCC SHRINKFILE (data_0, 1800)` reduced the data file back to 1.8 GB allocated.

After the database max size was increased to 5 GB, the corrected import completed successfully: 18 categories, 89,070 threads, and 1,164,816 posts. Reconciliation reported 0 legacy rows unmapped to a source thread. Attachment fields are present in the modern tables: 4,754 thread rows with starter attachments and 11,690 post rows with attachments. After import, the data file was about 2.5 GB allocated of 5 GB max.

The public site reads forum pages through `ModernForumRepository` by default (see **Modern forum read path configuration** above).

## Infrastructure (OpenTofu)

The Azure/Cloudflare infrastructure control plane (epic
[#615](https://github.com/RichardOrchardOrganisation/QueenZone.Modern/issues/615)) is
managed by OpenTofu, entirely separate from application deploys above.

- Contributor workflow (local validation, provider upgrades, first import,
  state moves): [`architecture/opentofu-contributor-runbook.md`](architecture/opentofu-contributor-runbook.md)
- Backend, workload identities, lock recovery, backup/restore, credential
  rotation: [`architecture/opentofu-state-and-identity.md`](architecture/opentofu-state-and-identity.md)
- CI (plan-on-PR, approval-gated apply, drift detection), rollback guidance
  by resource class, cutover verification, and disaster-recovery linkage:
  [`architecture/opentofu-ci-and-runbooks.md`](architecture/opentofu-ci-and-runbooks.md)
- Live-estate inventory and ownership boundaries: [`architecture/opentofu-inventory.md`](architecture/opentofu-inventory.md)

GitHub environments `opentofu-plan` (Reader) and `opentofu-apply`
(Contributor, requires approval) are separate from the `deploy` environment
used by `deploy.yml` — an OpenTofu apply never runs application code, and an
application deploy never touches Azure/Cloudflare resource configuration.

## Quick Links

- Primary agent instructions: [AGENTS.md](../AGENTS.md)
- Claude entry point: [CLAUDE.md](../CLAUDE.md)
- Testing policy: [docs/architecture/testing-policy.md](architecture/testing-policy.md)
- SQL MCP setup: [docs/sql/data-api-builder-mcp.md](sql/data-api-builder-mcp.md)
- OpenTofu CI and runbooks: [docs/architecture/opentofu-ci-and-runbooks.md](architecture/opentofu-ci-and-runbooks.md)
- Migration backlog: [docs/backlog/migration-backlog.md](backlog/migration-backlog.md)
