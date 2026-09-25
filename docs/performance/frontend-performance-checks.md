# Frontend performance checks

Issue: <https://github.com/RichardOrchardOrganisation/QueenZone.Modern/issues/170>

Repeatable, documented workflow for measuring end-user performance on the public pages people actually hit. Use it to capture a **before** baseline, make a change, then capture an **after** result for PR comparison.

Checks are **advisory by default**. They are not a pull-request CI gate yet, because Lighthouse variance (CPU load, network, throttling) can make hard failures noisy.

## When to run

**Not on every PR, and not on a fixed daily schedule.** Treat this like the opt-in legacy DB probes: run it when the risk is frontend load cost, not as part of the default restore/build/test loop.

| Cadence | When | What to do |
| --- | --- | --- |
| **Change-driven (primary)** | Before and after work that can change page weight or render path: images, CSS/JS bundling, fonts, third-party tags (GTM/analytics), layout shell, compression, cache headers, homepage hero, shared `_Layout` | Same `-BaseUrl`, form factor, and paths; attach both `summary.md` tables (or median of `-Runs 3`) in the PR |
| **Release / deploy spot-check** | After a production deploy that includes frontend or hosting changes | Point `-BaseUrl` at the public production host; sample a few real article/topic URLs if not on sample data |
| **Quarterly drift check** | About every 3 months, or after a large UI kit / design-system pass, even if no single “perf” issue is open | Full default path list, mobile form factor; optionally commit a dated baseline note under `docs/performance/` if numbers moved a lot |
| **Skip** | Pure backend/data/docs/tests with no public HTML/CSS/JS/static-asset impact | Default `dotnet test` is enough |

**Do not** add this to required PR CI yet. Keep it local (or an optional workflow later) so Lighthouse noise does not block merges.

**Practical default for most contributors:** run it only on performance-tagged work and on PRs that touch `wwwroot`, shared layout/partials, static caching, compression, or third-party script includes.

## What it measures

| Metric | Why it matters |
| --- | --- |
| **LCP** (Largest Contentful Paint) | When the main content becomes visible |
| **CLS** (Cumulative Layout Shift) | Visual stability during load |
| **Total transfer size** | Page weight (compressed when the server supports it) |
| **Request count** | Number of network requests on first load |
| **TTFB / server response time** | Backend and hosting latency signal |
| **Performance score** | Lighthouse aggregate (directional only) |
| **Repeat load** (optional) | Same page with storage not reset — cache/header effect |

Default public paths (sample-data friendly when the app runs with an empty legacy connection string):

| Path | Role |
| --- | --- |
| `/` | Homepage |
| `/news` | News archive |
| `/news/1003/queenzone-modernisation-begins` | Article detail |
| `/forum` | Forum index |
| `/forum/1/the-music` | Forum category |
| `/forum/topic/1002/ranking-every-studio-album` | Forum topic |

Override paths when measuring production or a DB-backed environment (use real article/topic URLs).

## Prerequisites

- Node.js with `npx` on `PATH` (Lighthouse is pulled via `npx lighthouse@13.4.0`).
- A Chrome/Chromium install that `chrome-launcher` can find.
- PowerShell 7+ recommended (`pwsh`); Windows PowerShell 5.1 also works for the script.
- A reachable site at `-BaseUrl`, **or** pass `-StartLocalApp` to publish and start a local Testing build.

### Windows note

On some Windows machines `chrome-launcher` prints `EPERM` while deleting its temp profile after a successful run and exits non-zero. The script still accepts the report when metrics are present and continues. You may see a warning; that alone is not a failed measurement.

## Quick start (local sample data)

From the repository root:

```powershell
# Publish sample-data app, run mobile Lighthouse on the default paths, write results.
powershell -File .\scripts\Measure-FrontendPerformance.ps1 -StartLocalApp -FormFactor mobile
```

Or against an already-running site:

```powershell
dotnet run --project src/QueenZone.Web/QueenZone.Web.csproj --urls http://127.0.0.1:5146
# other terminal:
powershell -File .\scripts\Measure-FrontendPerformance.ps1 -BaseUrl http://127.0.0.1:5146
```

Useful options:

```powershell
# From an interactive PowerShell session (array syntax works):
.\scripts\Measure-FrontendPerformance.ps1 -StartLocalApp -FormFactor both

# Three runs per path (use median when comparing noisy results)
.\scripts\Measure-FrontendPerformance.ps1 -StartLocalApp -Runs 3

# First load + warm/repeat load (storage not reset on the second pass)
.\scripts\Measure-FrontendPerformance.ps1 -StartLocalApp -IncludeRepeatLoad

# Custom paths — prefer array syntax in-session, or a comma-separated string with -File:
.\scripts\Measure-FrontendPerformance.ps1 -BaseUrl https://www.queenzone.org -Paths @("/", "/news", "/forum")
powershell -File .\scripts\Measure-FrontendPerformance.ps1 -BaseUrl https://www.queenzone.org -Paths "/,/news,/forum"

# Fail the process if any budget is exceeded (optional; not used in CI yet)
.\scripts\Measure-FrontendPerformance.ps1 -StartLocalApp -FailOnBudget
```

## Output layout

Each run writes a timestamped folder under `docs/performance/results/` (gitignored):

```text
docs/performance/results/2026-07-09-120000/
  summary.md          # human-readable table for PR notes
  summary.json        # machine-readable metrics
  raw/
    home-mobile-first-run1.report.json
    home-mobile-first-run1.report.html
    ...
  local-app.log       # only when -StartLocalApp is used
```

Commit **summaries you want as durable baselines** into `docs/performance/` with a dated name (see below). Leave bulk HTML/JSON under `results/` uncommitted.

## Budgets

Budgets live in [`frontend-performance-budgets.json`](./frontend-performance-budgets.json).

| Form factor | LCP | CLS | Total transfer | Requests | TTFB |
| --- | ---: | ---: | ---: | ---: | ---: |
| Mobile | ≤ 2500 ms | ≤ 0.1 | ≤ 1.5 MB | ≤ 40 | ≤ 600 ms |
| Desktop | ≤ 2000 ms | ≤ 0.1 | ≤ 2.0 MB | ≤ 45 | ≤ 400 ms |

These align with Core Web Vitals “good” LCP/CLS thresholds and a lean archive shell after compression and static caching work (#162–#165). Local sample-data runs usually land well under the budgets; throttled mobile and real hosting are the meaningful comparisons.

Budgets are **advisory**. The script prints `ok` / `OVER` per metric. Only `-FailOnBudget` turns an over-budget result into a non-zero exit code.

## Before / after comparison workflow

1. On `main` (or the pre-change commit), run the script and keep `summary.md` / `summary.json`.
2. Apply the performance change on a feature branch.
3. Re-run with the **same** `-BaseUrl`, form factor, paths, and preferably the same machine load.
4. Diff LCP, CLS, transfer size, and request count. Prefer the median of `-Runs 3` when numbers move only slightly.
5. Paste both summary tables (or link committed baseline docs) into the pull request.

Example PR note:

```markdown
### Performance check (advisory)
- Command: `Measure-FrontendPerformance.ps1 -StartLocalApp -FormFactor mobile -Runs 3`
- Homepage LCP: 980 ms → 720 ms
- Homepage transfer: 1.2 MB → 0.4 MB
- CLS remained 0.00 on all default paths
```

## Optional: Playwright trace (manual)

When you need a request waterfall or CPU profile rather than Lighthouse scores, use Playwright against the same URLs (the e2e project already depends on Playwright):

```powershell
dotnet build tests/QueenZone.Web.E2E/QueenZone.Web.E2E.csproj --configuration Release
.\tests\QueenZone.Web.E2E\bin\Release\net10.0\playwright.ps1 install chromium
# Start the app, then in a Node/Playwright script or inspector:
#   await page.goto(url); await page.context().tracing.stop({ path: 'trace.zip' })
# Open with: npx playwright show-trace trace.zip
```

Prefer the Lighthouse script for routine before/after numbers; use traces for deep diagnosis.

## Optional: WebPageTest / external labs

For multi-location or real-device lab data, point WebPageTest (or similar) at the same path list and record LCP, CLS, requests, and bytes. Keep the path list and budgets above so external runs stay comparable to local Lighthouse output.

## Historical baseline (2026-07-06)

Before several frontend optimizations landed, a local Chrome trace baseline recorded approximately:

| Page | Profile | LCP | CLS | Requests | Notes |
| --- | --- | ---: | ---: | ---: | --- |
| `/` | Desktop | 201 ms | 0.00 | 22 | Eager below-the-fold JPGs ~5.1 MB; no response compression locally |
| `/` | Mobile Fast 4G + 4× CPU | 834 ms | 0.00 | 23 | Same weight issues |
| `/news` | Mobile Fast 4G + 4× CPU | 549 ms | 0.00 | 14 | Shared shell: GTM, stylesheets, fonts, crest, `site.js` |
| `/forum` | Mobile Fast 4G + 4× CPU | 646 ms | 0.00 | 14 | Same shared shell |

That capture fed issues #162–#165 and #168. Prefer regenerating numbers with `Measure-FrontendPerformance.ps1` for new work rather than treating the table as a live budget.

## Related docs

- Testing policy: `docs/architecture/testing-policy.md` (performance check layer)
- Budgets file: `docs/performance/frontend-performance-budgets.json`
- Script: `scripts/Measure-FrontendPerformance.ps1`
- Server-side forum read benchmarks (different concern): `docs/performance/forum-read-benchmark-2026-06-29.md`
