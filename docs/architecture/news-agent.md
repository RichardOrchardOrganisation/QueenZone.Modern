# News Agent

The news agent discovers public Queen-related stories from configured sources, triages them with OpenRouter, generates editor-reviewable drafts, and places results in an admin review queue. It does **not** publish to visitor-facing pages automatically.

See also:

- `docs/backlog/news-agent-mvp-handoff.md` — product rules, source strategy, and GitHub issue tracking
- `docs/architecture/news-agent-editorial-rules.md` — trust tiers, source registry, relevance scope, and safety rules
- `docs/architecture/automated-news-discovery-plan.md` — original architecture plan

## Pipeline

```text
Configured sources (RSS / sitemap / allowlisted pages)
  → fetch + dedupe → NewsCandidate records
  → AI triage (relevance, confidence, duplicates)
  → AI draft generation (for needs-review candidates)
  → admin review queue (/admin/news-discovery)
  → promote to admin news draft → editor publish (existing /admin/news workflow)
```

Public `/news` shows only published articles. Promoted discovery drafts remain hidden until an editor publishes them through admin news.

## Repository layout

```text
src/
  QueenZone.NewsAgent/           # discovery, triage, drafting, OpenRouter client
  QueenZone.NewsAgent.Worker/    # console worker (discover-news command)
  QueenZone.Data/                # discovery tables, repositories, workflow rules
  QueenZone.Web/Pages/Admin/
    News/                        # manual news editorial workflow
    NewsDiscovery/               # discovery candidate + draft review queue
```

### DI split (issue #336)

| Host | Registration | What it includes |
| --- | --- | --- |
| `QueenZone.Web` | `AddQueenZoneNewsAgentWeb` | OpenRouter client, budget guard, draft generation (admin “regenerate draft” only) |
| `QueenZone.NewsAgent.Worker` | `AddQueenZoneNewsAgentWorker` | Full pipeline: discovery HTTP + source fetchers, triage, draft, `DiscoverNewsWorker` |

The public web host does **not** register outbound feed fetchers, SSRF-gated discovery HTTP, triage, or the discover-news worker. Review queue pages use `QueenZone.Data` repositories; only draft regeneration needs the editorial AI surface.

`AddQueenZoneNewsAgent` remains as an alias of the worker registration for tests and tooling.

Source registry: `src/QueenZone.NewsAgent/news-discovery-sources.json` (seeded into the database with `--seed-sources`).

## Local setup

### Database

The worker persists candidates, AI runs, and drafts through `QueenZone.Data`. For real data, set `ConnectionStrings:QueenZoneLegacy` in the worker local settings file (see below). Without a connection string, the worker uses in-memory sample data (useful for quick runs, not for reviewing real discoveries).

The web app's admin review queue reads the same discovery tables when the site is configured with the same database connection.

### OpenRouter API key (one-time)

Copy the example secrets file:

```powershell
copy src\QueenZone.NewsAgent.Worker\appsettings.Local.json.example src\QueenZone.NewsAgent.Worker\appsettings.Local.json
```

Edit `src/QueenZone.NewsAgent.Worker/appsettings.Local.json`:

```json
{
  "ConnectionStrings": {
    "QueenZoneLegacy": ""
  },
  "OpenRouter": {
    "ApiKey": "sk-or-v1-..."
  }
}
```

That file is git-ignored. The worker copies it to the build output automatically. You can also set `OPENROUTER_API_KEY` in the environment.

Fetch-only discovery works without an API key. Triage and drafting require a key (or `--dry-run`).

### Budget and model defaults

Configured in `src/QueenZone.NewsAgent.Worker/appsettings.json` under `OpenRouter`, `NewsTriage`, and `NewsDraftGeneration`. Defaults include per-run and daily spend caps.

## Worker commands

```powershell
dotnet run --project src/QueenZone.NewsAgent.Worker -- discover-news [options]
```

| Flag | Purpose |
|------|---------|
| `--seed-sources` | Upsert sources from `news-discovery-sources.json` |
| `--fetch-only` | Fetch feeds only (no AI) |
| `--triage` | Run AI triage after fetch |
| `--triage-only` | Triage existing `Discovered` candidates only |
| `--draft` | Generate drafts after fetch/triage |
| `--draft-only` | Draft existing `NeedsReview` candidates only |
| `--scheduled` | Editorial-safe automation preset: `--seed-sources --triage` (no drafts) |
| `--scheduled-with-drafts` | Deliberate legacy/full preset: `--seed-sources --triage --draft` |
| `--dry-run` | Log AI steps without calling OpenRouter or persisting status changes |
| `--force` | Bypass source poll-interval skip; force draft regeneration where applicable |

Examples:

```powershell
# Seed sources and fetch new items
dotnet run --project src/QueenZone.NewsAgent.Worker -- discover-news --seed-sources

# Full local pipeline: fetch, triage, draft
dotnet run --project src/QueenZone.NewsAgent.Worker -- discover-news --seed-sources --triage --draft

# Re-run triage on candidates already in the database
dotnet run --project src/QueenZone.NewsAgent.Worker -- discover-news --triage-only
```

## Scheduled runs

For Task Scheduler, Azure Container Apps Jobs, Functions, or WebJobs, see **`docs/architecture/news-agent-scheduling.md`**.

Quick local scheduled preset:

```powershell
scripts/Run-NewsAgentDiscovery.ps1 -Scheduled
```

This preset fetches and triages only. Generate a draft from the selected candidate's admin review page. To process a run requested from the web admin, configure the local Windows polling task described in `news-agent-scheduling.md`.

Overlapping runs are skipped via a database lease (`NewsAgentScheduler` in worker `appsettings.json`). Use `--force` to bypass the lease for manual reruns.

## OpenRouter smoke test (Windows)

Double-click `scripts/Smoke-NewsAgent.bat` or run it from a terminal. The script:

1. Checks that `appsettings.Local.json` exists with a real API key
2. Runs fetch (`--seed-sources`)
3. Runs triage (`--triage-only`)

Pass criteria: triage completes with `failures=0` and log lines such as `OpenRouter completed`. Some feed fetch errors are common and do not fail the smoke test if triage succeeds.

## Admin review queue

Authenticated admins use Razor Pages under `/admin/news-discovery`:

| Route | Purpose |
|-------|---------|
| `/admin/news-discovery` | Filterable list (status, source, trust tier, confidence, entity, dates, has-draft) |
| `/admin/news-discovery/prompt-settings` | Publish triage/draft editorial guidance overlays (future runs only) |
| `/admin/news-discovery/{id}` | Candidate detail: source URL, evidence, AI rationale, draft fields, AI provenance |
| `/admin/news-discovery/{id}/edit-draft` | Edit generated draft fields |

Editor actions (POST, anti-forgery protected):

- **Queue news gathering** → records a triage-only scheduled gathering request for the outbound-polling local Windows runner
- **Submit article URL** → queues forced triage for one public HTTP(S) URL (default triage-only; optional explicit “generate AI draft”). Fetch runs on the local runner with SSRF guards; duplicates reuse existing candidates; never auto-publishes
- **Mark not relevant** → `Rejected`
- **Ignore duplicate** → `IgnoredDuplicate`
- **Edit draft** → save fields; moves `NeedsReview` → `Drafted` when appropriate
- **Promote to admin news** → creates an unpublished article in `/admin/news` and marks the candidate `PromotedToArticle`

Draft generation uses prompt version `draft-v4`, which requires exact band-member quotations to appear in both the body and a `preserved_quotes` collection backed by evidence. Unverifiable quotation marks are stripped before the draft is stored. Review pages surface preserved quotes for editor comparison before promotion.

### Editorial guidance overlay (admin-editable)

Admins can publish a plain-text editorial guidance overlay for triage and draft runs from `/admin/news-discovery/prompt-settings`. This is **not** the full compiled system prompt.

- Each prompt is split into a fixed contract (`NewsTriagePrompt.BuildFixedContract()` / `NewsDraftPrompt.BuildFixedContract()`) and a default editorial guidance (`...BuildDefaultEditorialGuidance()`). The fixed contract — JSON schema, evidence, quotation, media-link, and safety rules — always stays in code and is never overridable from the admin UI.
- The prompt-settings textarea is pre-filled with the built-in default editorial guidance (voice/tone for drafts, preservation policy for triage) so there is real text to tune, not a blank box. Saving replaces that default outright: whatever is published is used verbatim as the editorial guidance section, appended after the fixed contract in a delimited block. Empty, missing, or invalid published guidance falls back to the built-in default (current behaviour).
- Revisions live in the EF table `NewsAgentGuidanceRevisions`. There is no seed. After migration, both types have no published row, so workers use the built-in default editorial guidance alongside the fixed contract.
- **Restore compiled default** publishes a new empty revision (`Content` `""`). History stays. The worker treats empty content as no overlay.
- Web and worker are separate processes. Published guidance is cached in-process with an **absolute 60 second** TTL. The web host may evict its cache on publish; the worker still waits up to 60 seconds. That TTL is the propagation contract.
- If the database is down or a published row is invalid, the provider returns an empty snapshot (compiled default) and logs the guidance type plus fallback reason, never the guidance text.
- Guidance affects **future runs only**. Existing candidates, AI results, and drafts are not rewritten. Each `NewsAiRun` stores compiled `PromptVersion` plus optional guidance revision id/number/hash, not the full overlay text.

### SQL Express mirror URL ingestion probe (opt-in)

After the nightly mirror refresh, verify the SQL-backed queue and optionally the full fetch and triage path:

```powershell
$env:ConnectionStrings__QueenZoneLegacy = "Server=localhost\SQLEXPRESS;Database=queenzone_legacy_sync;Integrated Security=True;TrustServerCertificate=True"
$env:RUN_NEWS_AGENT_URL_INGESTION_PROBE = "true"

# 1) Schema + queue + claim only (no outbound HTTP)
powershell -File .\scripts\Probe-NewsAgentUrlIngestion.ps1

# 2) Full local runner path: fetch public URL, triage, never publish
# Optional: $env:OPENROUTER_API_KEY = "..."
powershell -File .\scripts\Probe-NewsAgentUrlIngestion.ps1 -Full
```

The script rejects Azure SQL and remote targets. `-Full` accepts `-ArticleUrl` and `-GenerateDraft`. The default URL is unique. Both modes delete their requests and related records before returning; the full probe does **not** promote or publish.

Link from **Admin news** (`/admin/news`) → “Review discovered candidates”.

Regenerating a draft: use **Regenerate draft with AI** on the candidate review page (requires `OpenRouter:ApiKey` in `src/QueenZone.Web/appsettings.Local.json` or `OPENROUTER_API_KEY`), or run the worker with `--draft-only` / `--force`.

### Admin authentication

Admin routes require sign-in plus an email listed in `Admin:AllowedEmails`.

- **Production:** Microsoft Entra ID (`AzureAd:*` settings). See `README.md` → Admin authentication.
- **Local dev without Entra:** set `AzureAd:ClientId` to `""` in `src/QueenZone.Web/appsettings.Local.json` and send the `X-Test-User-Email` header with an allowed admin email. Automated tests use this path. There is no separate admin password login; member OAuth at `/account/login` does not grant admin access.

## Implementation status (News Agent MVP)

| Issue | Status | Notes |
|-------|--------|-------|
| #99 Source registry | Done | `news-discovery-sources.json` + `news-agent-editorial-rules.md` |
| #100 Data model | Done | Discovery tables + repositories in `QueenZone.Data` |
| #101 Fetchers + worker | Done | RSS, sitemap, allowlisted pages; `discover-news` command |
| #102 OpenRouter client | Done | Budget guard, model defaults, AI run logging |
| #103 AI triage | Done | Structured triage + deterministic duplicate checks |
| #104 Draft generation | Done | Citations/attribution; `--draft` flags |
| #105 Admin review queue | Done | `/admin/news-discovery` + in-admin regenerate draft |
| #106 Promote workflow | Done | Provenance panel on admin news, richer promote audit, bidirectional links |
| #107 Scheduled hosting | Done | DB run lease, `--scheduled`, `Run-NewsAgentDiscovery.ps1`, scheduling doc |
| #108 Tests/observability | Done | Run summary telemetry, failure-mode tests, test matrix in docs |

## Observability

Each `discover-news` run emits a structured **`NewsAgentRunCompleted`** log event (event id `4100`) with fetch, triage, draft, failure, and estimated AI spend totals. Step-level logs (`Discovery finished`, `Triage finished`, `Draft generation finished`) remain for troubleshooting.

Search application logs for `NewsAgentRunCompleted` or `EventId=4100` when monitoring scheduled runs.

## Test matrix

| Layer | Default CI | Opt-in manual |
|-------|------------|---------------|
| Source fetchers | Fake HTTP fixtures | `scripts/Smoke-NewsAgent.bat` (live feeds) |
| OpenRouter triage/draft | Fake `INewsAiClient` | `Smoke-NewsAgent.bat` + real API key |
| Admin review / no auto-publish | Web integration tests | — |
| Failure modes (malformed AI, disabled key) | `NewsAgentFailureModeTests` | — |
| Legacy SQL discovery tables | In-memory / SQLite tests | Real DB migration apply |

Report in pull requests whether opt-in smoke and legacy database checks were run or skipped.

## Testing

Default CI tests use fake fetchers and fake AI clients. They do not call OpenRouter or live RSS feeds.

Before a pull request touching the news agent:

```powershell
dotnet restore QueenZone.sln
dotnet build QueenZone.sln --configuration Release --no-restore
dotnet test QueenZone.sln --configuration Release --no-build
```

Report in the PR whether manual smoke tests (`scripts/Smoke-NewsAgent.bat`) and real legacy database checks were run or skipped.

### Full source articles and editorial guidance

Draft generation fetches the full source page and extracts article text, preferring
publisher article-body containers (including Queen Online's `.article-detail`),
then semantic article/main content, with a paragraph fallback for older pages.
Navigation, scripts, sidebars, forms, and footers are excluded. The full extracted
text, including late paragraphs and quotations, is stored in
`NewsCandidateEvidence.Excerpt` and supplied to the drafting model. The existing
column name is retained for compatibility; it now uses `nvarchar(max)`.

A draft cannot be generated from metadata alone. When fetching fails, stored full
article evidence may be reused; candidates with only a feed excerpt or page
description fail with a clear error. Review any extraction from unfamiliar
publishers; inaccessible or JavaScript-only article bodies need another source.

Editorial guidance has no application character cap. Both the editor and server
retain the complete sanitized text; `NewsAgentGuidanceRevisions.Content` also uses
`nvarchar(max)`. Draft requests that exceed the configured `OpenRouter:MaxInputTokens`
approximate budget fail before calling the model rather than silently truncating
the article or guidance. Raise that budget deliberately for longer sources, within
the selected model's context capacity and existing spend limits. Triage retains
its existing input-budget trimming. Apply the `PreserveFullNewsSourceEvidence`
migration before running the updated web app and local news runner.
