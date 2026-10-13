---
name: verify-queenzone
description: Drive the QueenZone public web archive the way a visitor does — launch an isolated Testing host, exercise Razor pages in a browser, and capture proof. Use when proving homepage, news, search, forum, or photography behavior after a UI or route change.
---

# Verify QueenZone (public web, all agents)

QueenZone.Web is the visitor-facing ASP.NET Core Razor site. This skill launches a disposable `Testing` host (in-memory sample data, no SQL) and drives real pages. Resolve the change through `docs/feature-map/` first, then read `features/README.md` and the matching feature file.

Other surfaces exist and are out of scope unless a feature file says otherwise: `/api/v1` and admin editorial screens. Drive the Expo client with `verify-queenzone-mobile`, not this skill.

This guide applies to every agent and editor. The `.cursor` path is historical and does not require Cursor or its orchestration loop. The local process helper currently requires Windows and PowerShell 7; agents on other hosts should use CI Playwright runs and explicitly report missing local/interactive proof.

## One-command automated verification

```powershell
pwsh -File scripts/Verify-Web.ps1 -Feature web.home.index -KeepRunning
```

This checks prerequisites, builds the Release E2E project, installs Chromium, publishes and launches a fresh isolated Testing host, runs doctor, captures the entry page, and executes every mapped deterministic fixture. It writes `proof.json`, `proof.md`, a screenshot, per-fixture TRX reports, and failure traces under `artifacts/proof/<id>/web/<timestamp>/`. The Markdown includes PR Verification fields; upload artifacts and replace the local path with a reviewable link.

Without `-KeepRunning`, the helper cleans up automatically. With it, a successful automated run leaves the host available for the Drive and Evidence steps below; clean up afterwards. Failed runs clean up automatically. An existing recorded host must be finished and cleaned up before a new run, so the command never silently reuses an older build.

An exit code of zero means automated checks passed only. Visual inspection remains NOT RUN until the agent actually inspects screenshots and exercises the changed journey. No mapped specs, zero discovered tests, skipped tests, missing reports, and failures cannot produce an automated PASS. Every fixture targets the recorded host via `E2E_BASE_URL`; existing environment values are restored after tests.

## Launch

Start only through the helper. Default bind is `http://127.0.0.1:5199` so this run never collides with local Development (`5146`), the mobile contract host (`5098`), the Playwright E2E host (`5099`), or HTTPS (`7162`).

From the repository root:

```powershell
pwsh -File .cursor/skills/verify-queenzone/scripts/control-queenzone.ps1 launch
```

Optional: `-Port 5201` when 5199 is occupied by something this skill did not start.

Ready when `GET /health` returns JSON `{"status":"ok"}` and the helper prints the base URL. The helper writes `.cursor/skills/verify-queenzone/.run/state.json` (pid, url, port). Print the URL later with:

```powershell
pwsh -File .cursor/skills/verify-queenzone/scripts/control-queenzone.ps1 url
```

The host uses `ASPNETCORE_ENVIRONMENT=Testing` and runs the Release publish output under `.run/app`. Publishing supplies the CSS bundles required outside Development; launch profiles are not used. Connection-string env vars are cleared for that process so the site stays on in-memory sample data even if the machine has `ConnectionStrings__QueenZoneLegacy` set. `Testing` is the only safe verification environment: it never reads a real database.

Do not drive `http://localhost:5146`, a live site, or an E2E process on `5099` unless that process was started by this helper. Two Testing hosts can run side by side on different ports; never attach to a shared instance you did not start.

Teardown is Cleanup below. Leave the host up for the whole drive, then clean up.

## Doctor

Run this first whenever anything looks off, and once after launch before driving:

```powershell
pwsh -File .cursor/skills/verify-queenzone/scripts/control-queenzone.ps1 doctor
```

Doctor is read-only. It must report:

- A state file from this skill, with a live PID.
- That PID still owns the recorded port.
- `GET {url}/health` is 200 and body contains `"status":"ok"`.
- `GET {url}/news/1003/queenzone-modernisation-begins` is 200 and the HTML contains `QueenZone modernisation begins` (sample article 1003). That proves the host is the in-memory Testing seed, not an empty or production-shaped database.

If doctor fails, stop. Do not fall back to another URL.

## Drive

Use an available real-browser tool against the helper URL. Prefer Chrome DevTools MCP for interactive inspection when available; Playwright MCP, Codex browser tools, and Cursor browser tools also qualify. Check availability first; do not assume an MCP is installed. The .NET Playwright suite is the automated regression runner. Exercise the visitor path in the feature file. Prefer:

- Routes (`/`, `/news`, `/search`, `/forum`, `/photography`)
- Accessible names (`Search`, `News`, `Forum`, `Photography`)
- Stable ids (`#qz-search`) and classes already used by `tests/QueenZone.Web.E2E/SmokeTests.cs` (`a.qz-card[href='/news']`, `.qz-news-row a`, `a.archive-pagination-next`, `article.article-body`)

Do not prove a page by calling `/api/v1` or by reading repositories. HTTP GET of the same HTML the browser shows is supporting evidence only.

Seeded Testing titles that must stay stable:

| Surface | Route | Visible title |
| --- | --- | --- |
| Homepage | `/` | `Twenty-five years of the Queen internet zone`; region text `Latest news` |
| News list | `/news` | heading `News` |
| News article | `/news/1003/queenzone-modernisation-begins` | `QueenZone modernisation begins` |
| Search | `/search` | heading `Search`; field `#qz-search` named `Search query` |
| Forum | `/forum` | heading `Forum`; card `/forum/1/the-music` |
| Topic | `/forum/topic/1002/ranking-every-studio-album` | `Ranking every studio album` |
| Photography | `/photography` | heading `Photography` |
| Photo | `/photography/brian-may/101` | `Brian in action with his guitar` |

Admin on this host uses test header `X-Test-User-Email: admin@test.local` (see `appsettings.Testing.json`). That is not a visitor path; do not use it for public-feature proof.

## Capture proof

UI PRs need a `## Verification` section. After `launch`, capture proof from the map id:

```powershell
pwsh -File .cursor/skills/verify-queenzone/scripts/control-queenzone.ps1 capture-proof -Feature web.home.index
```

This lower-level command requires a launched host, the Release E2E build, and installed Chromium. It runs doctor, captures the entry URL, and runs every mapped deterministic fixture against that host. Missing prerequisites or incomplete tests exit non-zero. Use `scripts/Verify-Web.ps1` to prepare prerequisites automatically. Screenshot capture, behavioural tests, and visual inspection are reported separately; a screenshot alone never earns an automated PASS.

Worked example: a homepage change uses `web.home.index` (`Pages/Index.cshtml`, `#qz-hero-archive`, `SmokeTests`). Attach the PNG and `proof.md` under `## Verification`.

## Evidence

Write proof under `artifacts/proof/<feature-id>/web/<timestamp>/` (and the skill `artifacts/` folder if you also keep an ARIA snapshot). Cleanup must not delete this directory.

Proof standards:

- Drive the visitor URL and controls, not internal setters or test-only endpoints.
- Inspect captured screenshots; saving a PNG does not establish that its layout is correct. Check relevant browser console/network failures and affected desktop/mobile web viewports. Record tool, viewport, actions and expected/observed outcomes in the PR proof.
- Capture the action and the resulting state (ARIA snapshot or HTML excerpt plus a screenshot), not only the final screen.
- For list → detail, show the list hit and the opened page.
- For search, show the query and a result card (or the empty-state sentence).
- Side effects on this host are in-memory only. A second GET of the same public URL is the persistence check.
- Do not use production, Azure SQL, or the SQL Express mirror for a default verify run.

Name artifacts with the feature id and entry point, for example `artifacts/news/detail.png` and `artifacts/news/detail.aria.txt`.

## Cleanup

```powershell
pwsh -File .cursor/skills/verify-queenzone/scripts/control-queenzone.ps1 cleanup
```

The helper kills the process tree of the PID it recorded, then deletes `.run/state.json`. It never kills by process name (`QueenZone.Web`, `dotnet`). It never deletes `artifacts/`.

If launch failed after starting a process, run cleanup before the next attempt.

## Helpers

All commands below are from the repository root. The script is `pwsh`-compatible.

```powershell
pwsh -File .cursor/skills/verify-queenzone/scripts/control-queenzone.ps1 launch
pwsh -File .cursor/skills/verify-queenzone/scripts/control-queenzone.ps1 doctor
pwsh -File .cursor/skills/verify-queenzone/scripts/control-queenzone.ps1 url
pwsh -File .cursor/skills/verify-queenzone/scripts/control-queenzone.ps1 capture-proof -Feature web.home.index
pwsh -File .cursor/skills/verify-queenzone/scripts/control-queenzone.ps1 cleanup
```

`launch` imports the Six Labors licence via `scripts/Import-SixLaborsLicense.ps1` when `SIXLABORS_LICENSE_KEY` is unset, because ImageSharp 4 restore/build needs it on this machine.
