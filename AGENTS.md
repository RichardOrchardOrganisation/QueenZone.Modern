# Agent Guide

This repository is the modern QueenZone rebuild. The project is archive-first: it should fully expose valuable legacy public content while keeping visitor-facing archive pages read-only. News is the first live editorial slice, so the architecture should also support newly approved news articles.

## Source Of Truth

- `README.md` gives the project overview and local development commands.
- `docs/architecture/testing-policy.md` defines the required testing layers (including CI Web.Tests mixed sharding).
- `docs/decisions/` contains accepted architectural decisions ([index](docs/decisions/README.md)).
- `docs/decisions/0006-hybrid-ef-core-admin-writes.md` is the Dapper vs EF access matrix and contributor rules for SQL in `QueenZone.Data`.
- `docs/architecture/blob-storage-ugc.md` is the UGC blob upload foundation (`QueenZone.Storage` / `IBlobUploadService`).
- `docs/architecture/opentofu-inventory.md` is the live Azure/Cloudflare ownership inventory for OpenTofu adoption (`infra/import/` holds sanitised IDs).
- `docs/architecture/opentofu-contributor-runbook.md` is the OpenTofu operating contract, including `prevent_destroy` on SQL, Storage, and other irreplaceable resources. OpenTofu does not manage blob objects or SQL rows, and it will not automatically refuse to destroy a data store unless that lifecycle flag is set.
- `docs/architecture/github-environments.md` is the GitHub Environment map (purpose, secrets/vars, protection, workflows). Production Actions use `prod-release`, `prod-deploy`, `prod-google-play`, and `prod-data-read` — not legacy `dev` / `deploy`.
- `docs/architecture/codeql.md` is the default CodeQL scan contract: Expo Android modules are path-ignored and `java-kotlin` is skipped because no first-party Java/Kotlin remains (#1418 Option A). Do not add an Android SDK CodeQL build.
- `docs/decisions/0007-rich-text-editor-quill.md` is the shared Quill rich-text editor decision (partial + `/api/uploads/editor-image`).
- `docs/architecture/json-api-v1.md` is the versioned `/api/v1` JSON API contract (pagination, Problem Details, OpenAPI).
- `docs/decisions/0019-api-versioning-convention.md` is the mobile/store-lag operational convention (breaking → `/api/v2` alongside v1; keep v1 while installed store builds depend on it). Complements [ADR 0010](docs/decisions/0010-versioned-json-api-conventions.md).
- `docs/decisions/0009-react-native-for-mobile-app.md` and `docs/decisions/0011-mobile-project-location-and-build-tooling.md` are the mobile client tech and project-location decisions. `docs/decisions/0012-react-navigation-app-shell.md` is the React Navigation shell and public vs member tab boundary. `docs/decisions/0018-mobile-server-state-strategy.md` is the mobile server-state decision (bespoke hooks, not React Query).
- `docs/mobile-development-environment.md` is the shared Windows/macOS native toolchain (Node 24, JDK 17, Android SDK 36).
- `docs/backlog/migration-backlog.md` tracks migration work.
- `docs/backlog/book-content-extraction-approach.md` is the standard approach for pulling new quote/event/trivia candidates out of book PDFs for `queen_quotes.csv` / `queen_history_events.csv` — use `pdftotext`, not vision-based PDF reading.
- `docs/sql/data-api-builder-mcp.md` explains the local SQL MCP setup for read-only legacy database investigation.
- `docs/agent-bitwarden-secrets.md` is the multi-machine Bitwarden Secrets Manager (`bws`) setup for local agents (Windows vs macOS).
- `.cursor/agents/` and `.cursor/skills/orchestrate-epic/` are the **Cursor-only** issue-queue overlay (planner / implementer / verifier / reviewer). Pin `/orchestrate-epic` as a Custom Mode in Cursor. Grok and other non-Cursor agents do not use that loop — they stay a single agent in the current chat (see [Grok and other non-Cursor agents](#grok-and-other-non-cursor-agents)). The portable protocol is the **issue-queue** Cursor plugin (`~/.cursor/plugins/local/issue-queue`, skill `/orchestrate-issues`). This repo keeps copies so a clone works without the plugin.

Keep durable workflow guidance in this file and keep user-facing setup guidance in `README.md`.

## UI Architecture

`QueenZone.Web` uses ASP.NET Core Razor Pages for server-rendered pages. Public archive pages, news pages, and admin editorial screens should live under `src/QueenZone.Web/Pages` as `.cshtml` files with page models.

Do not build visitor-facing or admin pages by streaming inline HTML from minimal route handlers. Minimal endpoints are appropriate for small non-page responses such as `/health` or the versioned JSON API under `/api/v1` (`src/QueenZone.Web/Api/`). Existing narrow endpoints in `src/QueenZone.Web/Endpoints/` (RSS, uploads, streaming) stay outside that contract. See `docs/architecture/json-api-v1.md`.

The React Native client lives at `src/QueenZone.Mobile/` as an Expo development-build project (TypeScript, `expo-dev-client`, Continuous Native Generation). Keep it out of `QueenZone.sln`. Expo Go is not a supported runtime. Native `ios/` and `android/` output is generated at build time and is not committed. Navigation is React Navigation (bottom tabs + native stacks per tab); signed-out vs signed-in surfaces follow ADR 0012. See `src/QueenZone.Mobile/README.md`.

Mobile server state uses the in-repo hooks, not a query library: `useHomeSection` for home sections, `useDetailQuery` for a single resource, `usePagedContent` for paginated and infinite lists, all fetching through `src/cache/fetchCached.ts` with a key from `src/cache/keys.ts` (same `cacheKey` shares one in-flight promise). Cross-screen invalidation uses `src/cache/externalStore.ts` (prefix subscribe/invalidate via `useSyncExternalStore`). Do not add `@tanstack/react-query` or another server-state library, and do not add a new bespoke pub/sub module for cache invalidation — see ADR 0018 for the decision and its revisit trigger.

`design/tokens/*.css` (colors, typography, spacing/shadows/motion) is the **canonical** source for design-token CSS custom properties, documented in `design/README.md`. `src/QueenZone.Web/wwwroot/design-system/tokens/` and every `design/design_handoff_*/tokens/` folder are generated copies — never hand-edit them. After changing anything under `design/tokens/`, run `pwsh ./scripts/Sync-DesignTokens.ps1` to propagate the change, or CI's "Design token sync check" (`pwsh ./scripts/Sync-DesignTokens.ps1 -Check`) will fail the PR. `site.css` itself (`src/QueenZone.Web/wwwroot/css/site.css`) consumes these tokens via `var(--...)` and is edited directly as normal.

## Branch And Pull Request Policy

Do not push feature work directly to `main`.

Use a branch named after the agent doing the work, not a single shared prefix. The agent slug must match whoever is performing the task so parallel work from different tools stays distinguishable.

Branch format:

```text
{agent}/{task}
```

- `{agent}`: lowercase slug for the active agent or assistant (for example `grok`, `claude`, `codex`, `composer`).
- `{task}`: short kebab-case description of the work (for example `news-pagination`, `seo-foundation`).

Examples:

| Agent / tool    | Prefix      | Example branch                | Auto label     |
| --------------- | ----------- | ----------------------------- | -------------- |
| Grok            | `grok/`     | `grok/news-pagination`        | `agent:grok`   |
| Claude Code     | `claude/`   | `claude/seo-foundation`       | `agent:claude` |
| Codex           | `codex/`    | `codex/legacy-news-dedup`       | `agent:codex`  |
| Cursor Composer | `composer/` | `composer/health-smoke-tests` | `agent:composer` |
| Cursor Agent    | `cursor/`   | `cursor/news-pagination`      | `agent:cursor` |
| New tool        | `{name}/`   | `my-tool/forum-archive-review`| `agent:{name}` |

GitHub Actions applies the matching `agent:*` label from the branch prefix via `.github/workflows/agent-pr-label.yml`.

Use the prefix for the agent you are, not a default from an earlier session or another tool. Different agents working on the same area should use different branch names, such as `grok/news-pagination` and `claude/news-pagination`, rather than reusing one shared branch.

### Open a pull request when a task is finished

When an agent finishes a task that changed tracked files, commit the work, push the branch, and open a pull request against `main` — don't leave finished work sitting uncommitted or unpublished for the user to package up. This applies once the change is actually complete and verified (default verification passed, or a clear note on what was skipped and why); it does not mean opening a PR mid-task or for exploratory/throwaway work the user didn't ask to keep.

Exceptions: skip auto-opening a PR when the user says they'll commit or push themselves, when work is explicitly a draft/spike not meant for review, or when repo/session instructions say not to. If a git identity, push access, or `gh` auth isn't available, say so instead of silently skipping.

### Update from `main` before opening a pull request

The default branch is **`main`** (not `master`). `main` is protected: required CI checks and merge gates evaluate the PR against the current tip of `main`. If a feature branch was cut hours or days earlier, `main` may have moved — an outdated PR base can block or confuse checks (coverage vs `origin/main`, migration jobs, mergeability) until the branch is updated.

**Before creating a PR** (and before asking for review on a long-lived branch), always:

```powershell
git fetch origin main
git merge origin/main
# or: git rebase origin/main
```

Prefer a merge of `origin/main` into the feature branch unless the user or stack workflow asks for a rebase. Resolve any conflicts, re-run the default verification (or the subset relevant to the conflicted files), then push and open or update the PR.

Do this even when the branch was originally created from `main` — time between branch creation and PR open is when `main` usually changes.

Before merging to `main`, open a pull request and fill in `.github/pull_request_template.md`. The pull request should include:

- Which agent authored the change.
- Summary of the change.
- Tests run.
- Whether real legacy database checks were run.
- Any skipped checks or known follow-up work.

For multi-session work, use `docs/agent-handoff-cheatsheet.md`.

### Auto-merge is enabled repo-wide

GitHub auto-merge is on for this repository: once you enable it on a PR (`gh pr merge --auto --squash`, or the "Enable auto-merge" button), GitHub merges it itself as soon as required checks pass and any branch-protection requirements (reviews, etc.) are satisfied — no need to poll CI and merge manually. Turn it on right after opening the PR rather than waiting for checks to go green first.

### Linking issues so merge auto-closes them

Fill in the template's `## Issues` section with a real GitHub closing keyword — `Closes #123`, `Fixes #123`, or `Resolves #123` — for every issue the PR fully resolves. GitHub only auto-closes an issue on merge when one of those keywords appears; a prose mention like "Implements #123" or a bare `[#123](...)` link anywhere else in the PR body (including `## Summary`) does not trigger it and leaves the issue open after merge. Use `Relates to #123` for issues the PR only touches without resolving. The `pr-issue-link-check` CI job fails the PR if it references an issue number without a recognized closing or relating keyword, so use the correct keyword up front rather than fixing it after the check fails.

## Grok and other non-Cursor agents

Grok, Claude, Codex, Composer, and any agent that is **not** a Cursor chat with `/orchestrate-epic` pinned work as a **single agent in this chat**.

- Do not load `/orchestrate-epic` or `/orchestrate-issues` because the user listed issue numbers (`761, 762 and 763`, `work on #15 #16 #17`).
- Do not spawn the planner / implementer / verifier / reviewer / orchestrator subagent loop. Do not act as a parent coordinator that refuses to implement.
- Implement, test, commit, and open PRs yourself. If given several issues, do them **one at a time** in this same chat (finish or PR one, then the next). Use that agent's branch prefix (`grok/`, `claude/`, `codex/`, …).

## Cursor issue-queue orchestration

This section applies **only** when `/orchestrate-epic` is pinned as a Custom Mode in Cursor (or `/orchestrator` is invoked there). It does not apply to Grok.

Cursor custom subagents are markdown files in `.cursor/agents/` (`planner`, `implementer`, `verifier`, `reviewer`, `orchestrator`). The QueenZone spawn protocol lives in `.cursor/skills/orchestrate-epic/SKILL.md` (overlay: surfaces, tests, `cursor/` branches, no worktrees). Pin that skill as a Custom Mode (`/orchestrate-epic`, then Alt+Enter on Windows or Option+Enter on Mac), or invoke `/orchestrator`. Do not also pin `/orchestrate-issues` in the same QueenZone chat.

The same loop is packaged as the **issue-queue** Cursor plugin for other repos: clone or copy `~/.cursor/plugins/local/issue-queue` onto the machine, reload Cursor, pin `/orchestrate-issues`. Product rules stay in that repo's `AGENTS.md`. Change the loop in the plugin first, then copy it into the QueenZone overlay.

In that Custom Mode, use `/orchestrate-epic` for **one issue** (`work on #757`), an epic's children, or an explicit list (`work on #15 #16 #17`). Skip planner when the queue has a single issue. The parent keeps a scoreboard and loops **one issue at a time** through implementer → verifier → reviewer → (one implementer response if the review requested changes) → PR so child context does not accumulate in the parent chat. Reviewer runs once per issue; do not send the same ticket back for a second review. Do not run sibling implementers in parallel. Share the parent checkout; do not isolate a git worktree per issue (that re-runs `dotnet restore` via `.cursor/worktrees.json` and is the usual cause of a slow queue). Website and `src/QueenZone.Mobile` are both in scope; do not mix those surfaces in one implementer unless the issue requires both. Child branches use `cursor/` unless the prompt names another agent.

Grok 4.6 effort when that Cursor mode is pinned: parent high (xhigh only if the split is messy), planner high, implementer medium, verifier high, reviewer high.

## Testing Expectations

Follow `docs/architecture/testing-policy.md`.

### Default verification before a pull request

```powershell
dotnet restore QueenZone.sln
dotnet build QueenZone.sln --configuration Release --no-restore
dotnet format QueenZone.sln --verify-no-changes
dotnet test QueenZone.sln --configuration Release --no-build
```

Use deterministic sample or fake data for normal unit and web integration tests. Real legacy database tests must be opt-in and clearly reported.

If a PR touches any file under `design/tokens/`, also run `pwsh ./scripts/Sync-DesignTokens.ps1` before committing (see UI Architecture above) — CI's "Design token sync check" gate fails otherwise.

When changing EF `SqlQueryRaw` projections over legacy tables, check the real SQL Server column types or cast projections to the C# row model types explicitly. Many legacy IDs and counts are `smallint`, which SQL Server materializes as `System.Int16`; in-memory route tests will not catch `Int16`-to-`Int32` mapping failures. Prefer a deterministic SQL-shape test plus an opt-in read-only legacy DB probe for new public legacy read surfaces.

Legacy read probes and self-cleaning write probes run automatically every night via `.github/workflows/nightly-legacy-checks.yml`. They use a same-day SQL Express mirror synced from the live Azure SQL DB, never the live database. The read probes run on macOS over the LAN; write probes run locally on the Windows SQL Express host. This is continuous signal, not a PR gate. See `docs/architecture/testing-policy.md` ("Data Integration Tests").

### The `E2E` hosting environment

`QueenZoneEnvironments.E2E` (`src/QueenZone.Web/Infrastructure/QueenZoneEnvironments.cs`) is a second automated-test environment, distinct from `Testing`: it composes the same test auth handlers (`UsesTestAuth`) and in-memory blob storage (`UsesInMemoryBlobStorage`) as `Testing`, but it does **not** use in-memory data (`UsesInMemoryData` matches only `Testing`) — it points at the real SQL Express mirror instead, so the nightly Playwright suite can exercise real legacy rows, `smallint`/`bit` projections, and encoding edge cases that sample data cannot reproduce.

**`Testing` must never be given a real connection string.** `WebApplicationFactory` tests (207+ files in `QueenZone.Web.Tests`) rely on `Testing` always resolving to in-memory sample data regardless of what `ConnectionStrings__QueenZoneLegacy` happens to be set to in the ambient environment — that is the guarantee `QueenZoneWebServiceCollectionExtensions.cs` provides by short-circuiting to `AddQueenZoneInMemoryData()` before it even reads the connection string for `Testing`. If `Testing` were changed to read a real connection string, every WAF test would start silently depending on whatever database happened to be configured on the machine or CI runner, and a bad row in that database could make unrelated PRs fail (or, worse, pass against stale data) for reasons that have nothing to do with the code under test. `E2E` exists as a separate environment specifically so this guarantee never has to be relaxed.

`E2E` has its own guard in the other direction: `E2EConnectionGuard.EnsureSafe` (`src/QueenZone.Web/Infrastructure/E2EConnectionGuard.cs`) runs at startup and refuses to boot unless `ConnectionStrings__QueenZoneLegacy` targets the `queenzone_legacy_sync` database on an allow-listed local SQL Express server (`localhost\SQLEXPRESS` and equivalents, or the Mac runner's LAN address to the Windows box, `glory11`). It mirrors `scripts/Assert-SqlExpressMirrorConnection.ps1` in-process, so a misconfigured nightly run can never point the write-heavy Playwright suite at Azure SQL or any other remote server — the guard fails closed on an empty connection string too, rather than silently falling back to in-memory data the way `Testing` does.

Run the E2E-suite locally or on demand with `scripts/Run-E2E.ps1`:

```powershell
# Same as the PR merge gate (Testing environment, in-memory, Deterministic category):
powershell -File ./scripts/Run-E2E.ps1 -Mode Deterministic

# Nightly real-data suite against the SQL Express mirror (E2E environment):
$env:ConnectionStrings__QueenZoneLegacy = "Server=localhost\SQLEXPRESS;Database=queenzone_legacy_sync;Integrated Security=True;TrustServerCertificate=True"
powershell -File ./scripts/Run-E2E.ps1 -Mode RealData

# Read-only sweep against a deployed site, no local app or database:
powershell -File ./scripts/Run-E2E.ps1 -Mode LiveSite -BaseUrl https://www.queenzone.org
```

See `docs/architecture/testing-policy.md` ("Nightly UI Regression (Real Data)") and `docs/architecture/self-hosted-e2e-runner.md` for suite content, runner setup, and troubleshooting.

When a change touches private messaging writes, SortKey assignment, or conversation locking, prefer the opt-in SQL Express mirror probe after the EF migration is applied:

```powershell
$env:ConnectionStrings__QueenZoneLegacy = "Server=localhost\SQLEXPRESS;Database=queenzone_legacy_sync;Integrated Security=True;TrustServerCertificate=True"
$env:RUN_PRIVATE_MESSAGE_PROBE = "true"
powershell -File .\scripts\Probe-PrivateMessaging.ps1
```

`ConnectionStrings__QueenZoneLegacy` must point to `queenzone_legacy_sync` on the local SQL Express instance. The script rejects Azure SQL and remote servers. The probe creates throwaway members, concurrent first-sends and replies, asserts IDENTITY `SortKey` + `LastMessageSortKey` tip consistency, then deletes the probe rows. Report whether it was run or skipped.

When a change touches admin news writes or discovery-to-news promotion, prefer running the opt-in admin write probe before release or after deployment verification:

```powershell
$env:RUN_LEGACY_WRITE_PROBE = "true"
powershell -File .\scripts\Probe-AdminNewsLegacyWrites.ps1
```

`ConnectionStrings__QueenZoneLegacy` must point to `queenzone_legacy_sync` on the local SQL Express instance. The script rejects Azure SQL and remote servers. It runs the admin news write lifecycle probe, the news-section `Admin_news_*` write Facts, and the self-seeding discovery promotion probe.

When a change touches modern forum thread/post writes, prefer:

```powershell
$env:RUN_FORUM_WRITE_PROBE = "true"
powershell -File .\scripts\Probe-ForumWrites.ps1
```

When a change touches photo or article submissions, or photo-submission promotion into the public gallery (`PIC_FILES_T` / `PIC_CAT_T`), prefer:

```powershell
$env:RUN_CONTENT_SUBMISSION_PROBE = "true"
powershell -File .\scripts\Probe-ContentSubmissions.ps1
```

`ConnectionStrings__QueenZoneLegacy` must point to `queenzone_legacy_sync` on the local SQL Express instance. The script refuses Azure SQL and remote servers. It runs `EfContentSubmissionLiveProbeTests`: photo/article submission status transitions plus the self-cleaning photo-promotion probe (inserts a visible `PIC_FILES_T` row joined to a real `PIC_CAT_T` category via the same repository path as `PhotoSubmissionPromotionService`, then deletes probe rows). Report whether it was run or skipped.

When a change touches member account create or external logins, prefer:

```powershell
$env:RUN_MEMBER_ACCOUNT_PROBE = "true"
powershell -File .\scripts\Probe-MemberAccounts.ps1
```

When a change touches the member public activity feed (`EfMemberPublicActivityRepository`, `/members/{id}`, `/following`) or the timestamp mapping of any source it reads, prefer the read-only activity probe. The SQLite unit tests cannot cover production ordering here — SQLite has no `ORDER BY` for `DateTimeOffset`, so it sorts client-side while SQL Server pages the `UNION ALL` server-side:

```powershell
$env:RUN_MEMBER_ACTIVITY_PROBE = "true"
powershell -File .\scripts\Probe-MemberActivity.ps1
```

When a change touches admin URL ingestion, the news-agent run-request queue, or the local `process-news-requests` runner, prefer the opt-in URL ingestion probe after the EF migration is applied:

```powershell
$env:ConnectionStrings__QueenZoneLegacy = "Server=localhost\SQLEXPRESS;Database=queenzone_legacy_sync;Integrated Security=True;TrustServerCertificate=True"
$env:RUN_NEWS_AGENT_URL_INGESTION_PROBE = "true"
powershell -File .\scripts\Probe-NewsAgentUrlIngestion.ps1          # schema + queue only
powershell -File .\scripts\Probe-NewsAgentUrlIngestion.ps1 -Full    # fetch + triage (optional OpenRouter key)
```

Both modes delete their requests, heartbeats, candidates, evidence, AI runs, and drafts before returning. The full probe never publishes. Report whether it was run or skipped.

When a change touches news-agent editorial guidance publish, rollback, or restore-default (`EfNewsAgentGuidanceRepository`), prefer the opt-in execution-strategy probe — Sqlite/in-memory providers never configure a retrying execution strategy, so only a real SQL Server connection with retry-on-failure enabled can catch a method that opens a transaction directly instead of routing it through `Database.CreateExecutionStrategy()`:

```powershell
$env:ConnectionStrings__QueenZoneLegacy = "Server=localhost\SQLEXPRESS;Database=queenzone_legacy_sync;Integrated Security=True;TrustServerCertificate=True"
$env:RUN_NEWS_AGENT_GUIDANCE_PROBE = "true"
powershell -File .\scripts\Probe-NewsAgentGuidance.ps1
```

`ConnectionStrings__QueenZoneLegacy` must point to `queenzone_legacy_sync` on the local SQL Express instance. The script rejects Azure SQL and remote servers. It runs `EfNewsAgentGuidanceLiveProbeTests`, which publishes, rolls back, and restores the compiled default for the Triage guidance type, then restores whatever draft/published rows it found there beforehand. Report whether it was run or skipped.

### Pull request CI gates (must pass before merge)

GitHub Actions workflow `.github/workflows/ci.yml` blocks merge when these fail:

| Check | Requirement | Blocks PR? |
| --- | --- | --- |
| **Build** | `dotnet restore`, `dotnet build` (Release), uploads binaries + Linux publish artifact | Yes |
| **Formatting** | `dotnet format QueenZone.sln --verify-no-changes` (matches root `.editorconfig`; CRLF via `.gitattributes`) — runs as its own job in parallel with Build/Test, not a Build step | Yes |
| **Test (sharded)** | Mixed `QueenZone.Web.Tests` shards (Release, Coverlet) | Yes |
| **Small test projects** | `Tools`/`Storage`/`NewsAgent` test projects, in parallel with the Web.Tests shards | Yes |
| **Global line coverage** | At least **51%** across the union of deterministic suite reports | Yes |
| **Changed-line coverage** | At least **70%** of changed, coverable `.cs` lines in the PR diff vs `main` | Yes |
| **Smoke test** | Published app responds on `/health`, `/`, `/news` (starts after `build`, overlaps coverage) | Yes |
| **EF migrations (SQL Express mirror)** | When migration-related paths change: `has-pending-model-changes` + `database update` against the SQL Express mirror (no production Azure SQL, no prod GitHub Environment) | Yes (job runs only for those PRs) |
| **Playwright e2e** | Self-hosted runner selected by the `e2e` label (Windows or macOS) | Yes (required PR merge gate; not rerun by deploy) |
| **Mobile JS** | `npm ci`, `scripts/check-npm-advisories.mjs` (high/critical fail-closed; see `src/QueenZone.Mobile/npm-advisory-allowlist.md`), typecheck, `npm run lint`, `npm run test:coverage`, `scripts/Test-MobileCoverageGate.mjs`, and Expo Doctor in `src/QueenZone.Mobile` when that tree (or the mobile coverage scripts) changes | Runs when mobile files change; skipped otherwise (non-matrix skip is treated as passing) |
| **Mobile Android build** | Unsigned debug APK via `expo prebuild` + `gradlew assembleDebug`, uploaded as a 1-day workflow artifact | Runs when mobile files change (or `workflow_dispatch`) |
| **Mobile iOS build** | Unsigned Simulator build via `expo prebuild` + `xcodebuild`; prefers an idle self-hosted `ios-build` Mac and falls back to `macos-26`; zipped and uploaded as a 1-day artifact | Runs when mobile files change (or `workflow_dispatch`) |

PRs that only change `src/QueenZone.Mobile/` (or docs/infra/design) skip the .NET build, tests, coverage, smoke, e2e, and the App Service deploy. Mixed web + mobile PRs run both. See `scripts/classify-pipeline-changes.sh` and `docs/architecture/testing-policy.md`.

There are two separate deploy workflows, not one: `deploy-dev.yml` auto-deploys every merge to `main` against the `dev` environment (Australia East App Service `queenzone-devbox`, `dev.queenzone.org`); `deploy.yml` deploys **production** to the Canada East App Service `queenzone-prod` and only triggers on a `v*` tag push (or manual dispatch from `main`) — see [epic #1264](https://github.com/richardorchard/QueenZone.Modern/issues/1264) Phase 4/5. Promote a change to production by tagging the already-merged, already-dev-verified commit: `git tag vX.Y.Z <sha> && git push --tags`. The previous Australia East production app `queenzone-dev` was retired on **14 September 2026**; do not target it for deployment or rollback. See `docs/architecture/azure-hosting-plan.md` ("Environments") for the full picture.

Coverage exclusions are configured in `coverlet.runsettings`. EF Core files under `**/Migrations/**/*.cs` are excluded from coverage metrics.

The changed-line gate compares `git diff origin/main...HEAD` for `*.cs` files. Large new modules (services, repositories, workers) usually need targeted unit or integration tests, often with fakes or SQLite/in-memory EF, or the gate will fail.

### CI Web.Tests sharding (agents)

CI parallelizes `QueenZone.Web.Tests` with **mixed** shards (light unit tests + `WebApplicationFactory` tests in every shard). Scripts: `scripts/Get-WebTestShardFilter.ps1`, `scripts/Invoke-WebTestsShard.ps1`. Full policy and anti-patterns: `docs/architecture/testing-policy.md` (section **CI test sharding**).

- Local default remains `dotnet test QueenZone.sln` (no filter).
- **Do not** split CI/jobs as unit-only vs WAF-only for speed — measured regression in [#442](https://github.com/richardorchard/QueenZone.Modern/issues/442).
- No shard manifest to maintain when adding tests; discovery is automatic from `*Tests` classes.

### EF migration PRs (required before merge)

If the PR touches any of:

- `src/QueenZone.Data/Migrations/`
- `src/QueenZone.Data/QueenZoneDbContext.cs`
- `src/QueenZone.Data/QueenZoneDbContextFactory.cs`
- `src/QueenZone.Data/Entities/`

then CI runs **EF migrations (SQL Express mirror)** (`has-pending-model-changes` + `database update` against `queenzone_legacy_sync` on the Windows self-hosted SQL Express). That job has no production connection string and no production GitHub Environment. Production Azure SQL `database update` runs on `deploy.yml` migrate after merge / tag / dispatch. Unit/SQLite tests do **not** catch SQL Server batch-binding errors; Express also misses Azure-only failures (filegroups, collation, DTU-bound DDL) — those move to the post-merge migrate.

`.github/workflows/test-migrations-against-mirror.yml` (manual `workflow_dispatch`) is the on-demand path against the same mirror, including an optional live resync. Check "resync first" to force a fresh copy from the live DB (currently about 25-30 minutes), or leave it against whatever's already there. A clean mirror run does not guarantee a clean run against Azure SQL.

Locally, before opening such a PR:

```powershell
dotnet tool restore
dotnet ef migrations has-pending-model-changes --project src/QueenZone.Data/QueenZone.Data.csproj --startup-project src/QueenZone.Web/QueenZone.Web.csproj

$env:ConnectionStrings__QueenZoneLegacy = "<migration connection string>"
dotnet ef database update --project src/QueenZone.Data/QueenZone.Data.csproj --startup-project src/QueenZone.Web/QueenZone.Web.csproj
```

Rules of thumb for hand-written SQL migrations:

- Separate dependent DDL into separate `migrationBuilder.Sql(...)` calls (SQL Server batch binding).
- Prefer idempotent SQL; large indexes may need `suppressTransaction: true` and a higher command timeout.
- Keep the EF model snapshot in sync (or add a no-op sync migration).

### Pre-PR verification (recommended before opening the PR)

Run the [default verification](#default-verification-before-a-pull-request) first, then the same coverage gate CI enforces so failures are caught early:

```powershell
git fetch origin main
# After default restore/build/format, collect coverage and gate:
dotnet test QueenZone.sln --configuration Release --no-build --collect:"XPlat Code Coverage" --settings coverlet.runsettings --results-directory ./TestResults
powershell -File ./scripts/Test-CoverageGate.ps1 -Reports ./TestResults -GlobalLineThreshold 51 -ChangedLineThreshold 70 -BaseRef origin/main
```

On Linux or GitHub Actions, use `pwsh` instead of `powershell` for the last command.

If the gate reports uncovered changed lines, it prints up to 20 `path:line` entries. Add or extend tests until changed-line coverage is at least 70%.

When the PR changes `src/QueenZone.Mobile`, run the same host-free gate CI uses before the coverage job — not app typecheck + Jest alone:

```powershell
cd src/QueenZone.Mobile
npm ci
npm run preflight
```

`npm run typecheck` is `tsc --noEmit && tsc --noEmit -p tsconfig.test.json` (app sources plus test files, `src/test/`, `jest.setup.ts`, and `contracts/**/*.ts`). `npm run preflight` is that combined typecheck + lint + unit tests + Expo Doctor. Doctor's package-version check consults Expo's current SDK list, so a lockfile that passed this morning can fail CI the same afternoon when Expo publishes a patch (`npx expo install <package>`). Do not skip Doctor on mobile PRs.

When the PR also changes production TypeScript/TSX, run the mobile coverage gate (floors in `scripts/mobile-coverage-floors.json`; do not copy the web C# 51%/70% numbers):

```powershell
cd src/QueenZone.Mobile
npm run coverage
```

Full detail, test-layer guidance, and coverage troubleshooting: `docs/architecture/testing-policy.md` (sections **Continuous Integration**, **Mobile coverage gates**, and **Pre-pull request checklist**).

## Local Secrets

Do not commit secrets.

Local secrets belong in ignored files such as:

- `src/QueenZone.Web/appsettings.Local.json`
- `src/QueenZone.NewsAgent.Worker/appsettings.Local.json`
- `.env`

Commit only examples such as `.env.example`.

Bitwarden **Secrets Manager** (`bws` CLI) is the shared local secret store for development agents on Richard's machines. This is **not** the password-manager CLI (`bw`). Full multi-machine setup (Windows vs macOS, install paths, troubleshooting): [`docs/agent-bitwarden-secrets.md`](docs/agent-bitwarden-secrets.md).

Rules for agents:

- Use **`bws`**, never `bw login`, for QueenZone App Service–style secrets.
- Authenticate with user-scoped **`BWS_ACCESS_TOKEN`**; do not ask the user to paste the token into chat; never print tokens or secret values (key names and value lengths only).
- Machine account: **`windows-codex`** on Windows, **`mac-codex`** on Macs (separate tokens per host).
- Project: **`Queenzone Development`** (`1c16fd2d-4bfb-4eb7-8357-b49400233490`).
- **This Windows workstation:** `bws` lives at `%USERPROFILE%\bin\bws.exe` with `%USERPROFILE%\bin` on the User `Path`; token is User env `BWS_ACCESS_TOKEN`. Restart agent shells after Path changes.

ImageSharp 4 builds require `SIXLABORS_LICENSE_KEY`. On an authorised Windows or
macOS machine, import it from Bitwarden without displaying the value:

```powershell
. ./scripts/Import-SixLaborsLicense.ps1
```

Use `-PersistForUser` only on Windows when new IDE/agent processes need the value;
restart them afterward. macOS agents import it into each new `pwsh` session.
Hosted agents receive the key through their platform secret manager, never through
a copied local Bitwarden machine token. External contributors must obtain their
own Six Labors licence. See `docs/agent-bitwarden-secrets.md` for renewal and
troubleshooting. Never commit `sixlabors.lic` or print the licence value.

```powershell
$env:BWS_ACCESS_TOKEN = [Environment]::GetEnvironmentVariable("BWS_ACCESS_TOKEN", "User")
bws secret list "1c16fd2d-4bfb-4eb7-8357-b49400233490"
```

App Service setting names are the canonical secret keys in Bitwarden, including `ConnectionStrings__QueenZoneLegacy`, `ConnectionStrings__BlobStorage`, `AzureAd__*`, `Authentication__*`, `Admin__AllowedEmails__*`, `APPLICATIONINSIGHTS_CONNECTION_STRING`, `MobileAuth__SigningKey`, and `OPENROUTER_API_KEY`. Azure App Service settings are a separate store: updating Bitwarden does not update Azure App Service, except for `MobileAuth__SigningKey`, which the deployment workflow deliberately reconciles before deploying. GitHub Actions fetches its deploy secrets (`AZURE_WEBAPP_PUBLISH_PROFILE`, `QUEENZONE_LEGACY_MIGRATION_CONNECTION_STRING`, and `MOBILE_AUTH_SIGNING_KEY`) from this same Bitwarden project at workflow runtime via `bitwarden/sm-action` — see `docs/bitwarden-secrets.md`. When credentials rotate, update every authoritative target deliberately and verify by name plus value length, not by printing values.

The deployed App Service runtime database setting is `ConnectionStrings__QueenZoneLegacy`. The current production route uses SQL authentication, stored in Azure App Service configuration, not in the repository.

`QUEENZONE_LEGACY_MIGRATION_CONNECTION_STRING` is fetched by repository and `prod-data-read` workflows from the Canada East Bitwarden secret `ConnectionStrings__QueenZoneLegacyCanadaEast`; `prod-release` exposes the same secret through the `_CANADA_EAST` output alias used by `deploy.yml`. Updating that Bitwarden secret does not update the live `ConnectionStrings__QueenZoneLegacy` App Service runtime setting, which is configured separately. When database credentials rotate, update both places as needed and restart `queenzone-prod` before verifying production.

For production debugging (log stream, Azure CLI, Azure MCP tenant setup, and forum smoke checks), see `docs/agent-handoff-cheatsheet.md`.

Admin Entra app registration, App Service `AzureAd__*` settings, and **client secret rotation** (renew by 2028-07-01): `docs/architecture/entra-admin-auth.md`.

Hosting scale/cost: production is **single-instance B1** with **no Redis**; see `docs/architecture/hosting-scale-and-cache.md` before proposing multi-instance or distributed cache work.

For local SQL MCP access through Azure Data API Builder, see `docs/sql/data-api-builder-mcp.md`. Keep the MCP surface narrow and read-oriented by default.

News agent worker and admin review queue: see `docs/architecture/news-agent.md`. OpenRouter key goes in `src/QueenZone.NewsAgent.Worker/appsettings.Local.json`. Manual OpenRouter smoke test: `scripts/Smoke-NewsAgent.bat`. Admin review UI: `/admin/news-discovery` (requires `Admin:AllowedEmails` from App Service or `appsettings.Local.json` — committed `appsettings.json` ships an empty allowlist; member OAuth at `/account/login` is unrelated).

Never log secrets (connection strings, client secrets, storage keys, OpenRouter keys, or Six Labors licence values) into issues, PR text, or application telemetry properties.

## Media Serving

Two Cloudflare hostnames serve Azure Blob Storage content. They are **not interchangeable** — pick the right one for the content type.

| Hostname | Type | Can set response headers? | Use for |
| --- | --- | --- | --- |
| `cdn.queenzone.org` | Straight CDN proxy | No | Photos and images (`PhotoImageUrl`) |
| `cdn2.queenzone.org` | Cloudflare Worker proxy (script `pictures-queenzone-org` on `cdn2.queenzone.org/*`) | Yes (cache/CORS/nosniff) | Legacy forum attachment redirect target. Returns 404 for `/songfiles/*`. |

Fan-performance audio is **not** a public CDN object. Signed-in members stream through `/fan-performances/{id}/audio`, which reads the private `songfiles` container and sets `Content-Disposition`. Do not emit `cdn2.queenzone.org/songfiles/…` or raw blob URLs in HTML.

`pictures-queenzone-org` is the Worker **script name**, not a DNS hostname. The public host is `cdn2.queenzone.org`. Retired `pictures.queenzone.org` is a compatibility hostname only (Worker `pictures-legacy-redirect` → `cdn`); do not use it for new media URLs. Legacy forum attachments use `cdn2` after a member-auth gate (`/forum/attachment/legacy/{postId}`). New forum uploads live in private `ugc-forum` and download via `/forum/attachment/{postId}/{attachmentId}` (member-only, app-streamed).

## Migration Principles

- Preserve public content first.
- Keep the public archive read-only for visitors.
- Allow deliberately designed editorial workflows for new approved news articles.
- Do not port Web Forms architecture.
- Keep all SQL Server access inside `QueenZone.Data` (no ad-hoc SQL in page models/tools). See ADR 0006 for the Dapper/EF matrix: new writes default to EF; complex legacy/projected reads may keep SQL/procs; target direction is EF Core as the single client library while retaining stored procedures for hot paths.
- The legacy database (`queenzone-db`) is the permanent production database, not an import source for a separate future one (ADR 0021) — legacy schema may be altered through the same reviewed-migration discipline as modern tables. Forum public reads use modern projected tables by default; other public content may keep reading legacy tables unless performance or safety problems appear.
- Prefer clean, stable, search-friendly canonical URLs over preserving legacy URL shapes.
- Never expose private, hidden, deleted, moderated, or credential-related data by default.

## Cursor Cloud specific instructions

Environment: .NET 10 SDK is preinstalled at `/usr/local/dotnet` and symlinked to `/usr/local/bin/dotnet` (already on `PATH`). The startup update script runs `dotnet restore QueenZone.sln` and `dotnet tool restore`; standard build/test/run commands live in `README.md` and above.

Because the startup restore runs before normal task work, Cursor Cloud must inject
`SIXLABORS_LICENSE_KEY` through its encrypted environment secrets before startup.
Do not place a local `BWS_ACCESS_TOKEN` in Cursor Cloud. If the licence secret is
absent, the ImageSharp 4 restore/build is expected to fail; ask the user to
configure the environment rather than exposing the key in chat or repository files.

No database is required for local development. When `ConnectionStrings:QueenZoneLegacy` is empty (the default), the app uses in-memory/sample data, so `dotnet run --project src/QueenZone.Web/QueenZone.Web.csproj` starts with zero external services (defaults to `Development` at `http://localhost:5146`). `dotnet run` builds `Debug` by default; do not pass `--no-build` unless you have already built the `Debug` configuration (the `--configuration Release` builds live under `bin/Release`).

Exercising admin editorial routes locally without real Entra: admin routes require Microsoft Entra sign-in unless `AzureAd:ClientId` is blank, in which case a test-header auth fallback is active. `appsettings.json` ships a placeholder `ClientId`, so create a git-ignored `src/QueenZone.Web/appsettings.Local.json` that sets `AzureAd:ClientId` to `""` and lists an allowed admin email under `Admin:AllowedEmails`. Then authenticate admin requests by sending the `X-Test-User-Email: <allowed-email>` header. Admin POSTs need the `__RequestVerificationToken` antiforgery field, so fetch the form first and reuse its token plus cookie. The news article body is validated as plain text (HtmlSanitizer), so a body containing HTML tags is rejected with "Article body must be plain text."
