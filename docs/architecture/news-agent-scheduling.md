# News Agent Scheduling

Operational guide for running the news discovery worker on a schedule. For pipeline behavior, flags, and local secrets, see `news-agent.md`.

## Worker entry point

Publish or run the worker project:

```powershell
dotnet publish src/QueenZone.NewsAgent.Worker/QueenZone.NewsAgent.Worker.csproj -c Release -o ./publish/news-agent
```

```powershell
# Editorial-safe scheduled pipeline: seed sources, fetch, triage (no drafts)
dotnet run --project src/QueenZone.NewsAgent.Worker -- discover-news --scheduled

# Deliberate full pipeline, including bulk drafts
dotnet run --project src/QueenZone.NewsAgent.Worker -- discover-news --seed-sources --triage --draft
```

| Flag | Purpose |
|------|---------|
| `--scheduled` | Preset for automation: `--seed-sources --triage` (no drafts) |
| `--scheduled-with-drafts` | Deliberate full preset including bulk draft generation |
| `--fetch-only` | Fetch feeds only (no AI) |
| `--force` | Bypass source poll-interval skip **and** the run lease (manual overlap override) |

Exit codes:

- `0` — success, or run skipped because another instance holds the lease
- `1` — one or more source/AI step failures (Task Scheduler / Azure can treat this as a failed run)

## Overlap protection

When `NewsAgentScheduler:UseRunLease` is `true` (default), each `discover-news` run acquires a database lease before processing. A second concurrent run exits `0` immediately with a log line such as:

```text
Skipping discover-news run because lease discover-news is held by another instance.
```

Configuration (`appsettings.json` or Azure App Settings):

```json
{
  "NewsAgentScheduler": {
    "UseRunLease": true,
    "LeaseName": "discover-news",
    "LeaseDurationMinutes": 120
  }
}
```

- Set `LeaseDurationMinutes` longer than your longest expected run.
- Use `--force` only for deliberate manual reruns while a scheduled job is still active.
- In-memory/sample mode (no `ConnectionStrings:QueenZoneLegacy`) uses an in-process lease store suitable for local dev, not cross-machine coordination.

Apply migrations before the first leased run against a real database:

```powershell
dotnet ef database update --project src/QueenZone.Data --startup-project src/QueenZone.Web
```

## Windows Task Scheduler (local pilot)

Use `scripts/Run-NewsAgentDiscovery.ps1` for automatic gathering and `scripts/Process-NewsAgentRunRequests.ps1` for requests queued from `/admin/news-discovery`. Both run from the repository root and forward worker exit codes.

### One-time setup

1. Configure `bws` and the scheduled-task account's user-scoped `BWS_ACCESS_TOKEN` per `docs/agent-bitwarden-secrets.md`. Both scheduling scripts load `ConnectionStrings__QueenZoneLegacyCanadaEast` from the `Queenzone Development` Bitwarden project on every run. They validate the production SQL target and override any stale local JSON or inherited connection setting. They fail before starting the worker if Bitwarden is unavailable or the secret is invalid.
2. Copy `src/QueenZone.NewsAgent.Worker/appsettings.Local.json.example` to `appsettings.Local.json` and set `OpenRouter:ApiKey`. The scheduled scripts do not use its database connection value.
3. Ensure the database has discovery migrations applied.
4. Smoke-test manually: `scripts/Process-NewsAgentRunRequests.ps1` and `scripts/Run-NewsAgentDiscovery.ps1 -Scheduled`.

### Create the task

1. Open **Task Scheduler** → **Create Task**.
2. **General**: run whether user is logged on or not; use an account that can reach SQL Server and the network.
3. **Triggers**: daily, repeating every 6 hours indefinitely (for example 00:00, 06:00, 12:00, and 18:00 local time).
4. **Actions** → **Start a program**:
   - **Program**: `powershell.exe`
   - **Arguments**: `-NoProfile -ExecutionPolicy Bypass -File "C:\path\to\QueenZone.Modern\scripts\Run-NewsAgentDiscovery.ps1" -Scheduled`
   - **Start in**: `C:\path\to\QueenZone.Modern`
5. **Settings**: stop task if it runs longer than 2 hours; if the task is already running, **Do not start a new instance** (belt-and-braces with the DB lease).

Optional log file (ignored path, not committed):

```powershell
scripts/Run-NewsAgentDiscovery.ps1 -Scheduled *>&1 | Tee-Object -FilePath "$env:LOCALAPPDATA\QueenZone\news-agent.log" -Append
```

### Web-queued runs on the local computer

Admin actions on `/admin/news-discovery` do not contact the Windows computer directly. They insert a pending request into the shared Azure SQL database. The computer polls outbound, records a heartbeat, and atomically claims at most one request.

Request kinds:

| Kind | Source in admin UI | What the runner does |
|------|--------------------|----------------------|
| `ScheduledGathering` | **Queue news gathering** | Same `--scheduled` preset: seed sources, fetch feeds, triage (no bulk drafts) |
| `UrlIngestion` | **Submit article URL** | Fetch that public URL, create/reuse a candidate, triage; optional explicit draft only when the form requested it |

If the computer is off, the request remains pending. URL submissions stay in the queue independently of the single active gathering slot.

Create a second Task Scheduler task:

1. **General**: run whether the user is logged on or not, using the same account and local worker settings as the scheduled task.
2. **Trigger**: daily, repeat every 1 hour indefinitely.
3. **Action**:
   - **Program**: `powershell.exe`
   - **Arguments**: `-NoProfile -ExecutionPolicy Bypass -File "C:\path\to\QueenZone.Modern\scripts\Process-NewsAgentRunRequests.ps1"`
   - **Start in**: `C:\path\to\QueenZone.Modern`
4. **Settings**: if already running, **Do not start a new instance**.

Smoke-test the poller before saving the task:

```powershell
scripts/Process-NewsAgentRunRequests.ps1 -RunnerId "news-pc"
```

With no pending request it exits `0` after updating the heartbeat. Queue a gathering or URL request from `/admin/news-discovery`, run the script again, and confirm that the page shows the request moving from `Pending` to `Completed` or `Failed`. The hourly interval means a web-queued run can take up to an hour to start. The runner never needs an inbound port, tunnel, or webhook.

Opt-in live probe (schema or full fetch+triage against the real DB): `scripts/Probe-NewsAgentUrlIngestion.ps1` — see `docs/architecture/news-agent.md`.

## Azure hosting options

The web app deploy workflow (`.github/workflows/deploy.yml`) publishes `QueenZone.Web` only. The news agent worker is a **separate** console app. Pick one of the paths below when you need cloud automation.

### Option A — Azure Container Apps Job (recommended for isolated scheduling)

1. `dotnet publish src/QueenZone.NewsAgent.Worker -c Release -o publish/news-agent`
2. Containerize the publish output (Dockerfile calling `dotnet QueenZone.NewsAgent.Worker.dll discover-news --scheduled`).
3. Deploy a Container Apps **Job** with a cron trigger (e.g. `0 6 * * *`).
4. Set App Settings / secrets:
   - `ConnectionStrings__QueenZoneLegacy`
   - `OPENROUTER_API_KEY` or `OpenRouter__ApiKey`
5. Use the same SQL database as the web app so `/admin/news-discovery` sees new candidates.

### Option B — Azure Functions timer trigger

1. Add a timer-triggered function project that references `QueenZone.NewsAgent` and `QueenZone.Data`.
2. On timer fire, build a DI container mirroring `QueenZone.NewsAgent.Worker/Program.cs` and call `DiscoverNewsWorker.RunAsync` with `--scheduled` options.
3. Configure connection string and OpenRouter key in Function app settings.
4. Keep function timeout above worst-case fetch + triage + draft duration.

### Option C — App Service WebJob

1. Publish the worker to a folder and zip as a **triggered WebJob** under `App_Data/jobs/triggered/news-discovery`.
2. Add a `settings.job` CRON schedule, e.g. `{ "schedule": "0 0 6 * * *" }`.
3. Set the same connection string and OpenRouter secrets on the App Service.
4. Do **not** host the worker inside the scaled-out web process without the DB lease; overlapping instances will otherwise duplicate work.

### Option D — Operator machine (current default)

Run `scripts/Run-NewsAgentDiscovery.ps1 -Scheduled` for automatic triage and `scripts/Process-NewsAgentRunRequests.ps1` for web-queued runs via Task Scheduler on the Windows PC. Discovery pauses when the machine is off; queued requests wait and the public site is unaffected.

## Secrets

Never commit API keys or connection strings. Use:

- Local: `src/QueenZone.NewsAgent.Worker/appsettings.Local.json` (git-ignored)
- Azure: App Service / Function / Container App configuration
- CI: not required for default tests (fakes only)

## Related

- `docs/architecture/news-agent.md` — pipeline, admin review, smoke test
- `docs/backlog/news-agent-mvp-handoff.md` — MVP scope and GitHub issues (#107)
- `scripts/Smoke-NewsAgent.bat` — manual OpenRouter smoke (fetch + triage)
