---
name: verify-queenzone
description: Drive the QueenZone public web archive the way a visitor does — launch an isolated Testing host, exercise Razor pages in a browser, and capture proof. Use when proving homepage, news, search, forum, or photography behavior after a UI or route change.
---

# Verify QueenZone (public web)

QueenZone.Web is the visitor-facing ASP.NET Core Razor site. This skill launches a disposable `Testing` host (in-memory sample data, no SQL) and drives real pages. Resolve the change through `docs/feature-map/` first, then read `features/README.md` and the matching feature file.

Other surfaces exist and are out of scope unless a feature file says otherwise: `/api/v1` and admin editorial screens. Drive the Expo client with `verify-queenzone-mobile`, not this skill.

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

The host uses `ASPNETCORE_ENVIRONMENT=Testing` and `--no-launch-profile`. Connection-string env vars are cleared for that process so the site stays on in-memory sample data even if the machine has `ConnectionStrings__QueenZoneLegacy` set. `Testing` is the only safe verification environment: it never reads a real database.

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

Use a real browser (Cursor browser tools or Playwright MCP) against the helper URL. Exercise the visitor path in the feature file. Prefer:

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

That screenshots the entry URL through the E2E project's `playwright.ps1 screenshot --full-page` and runs the mapped spec when one exists. Output is `artifacts/proof/<id>/web/<timestamp>/`. A missing host or Playwright build writes `NOT RUN: <reason>, needs <named check>` and exits non-zero.

Worked example: a homepage change uses `web.home.index` (`Pages/Index.cshtml`, `#qz-hero-archive`, `SmokeTests`). Attach the PNG and `proof.md` under `## Verification`.

## Evidence

Write proof under `artifacts/proof/<feature-id>/web/<timestamp>/` (and the skill `artifacts/` folder if you also keep an ARIA snapshot). Cleanup must not delete this directory.

Proof standards:

- Drive the visitor URL and controls, not internal setters or test-only endpoints.
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
