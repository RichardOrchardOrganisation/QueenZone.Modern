# QueenZone public-web verification map

The machine-readable map of every public and member page lives in [`docs/feature-map/`](../../../../docs/feature-map/README.md). Read that index to resolve a vague request to an `id`, sources, selectors, and E2E specs. This folder keeps the prose recipes those entries point at via `recipe`.

This directory is the maintained source for verifying visitor-facing QueenZone.Web behavior. Read this index before driving the app, then use the matching feature file as the recipe.

## Baseline preconditions

- Launch an isolated Testing host with `pwsh -File .cursor/skills/verify-queenzone/scripts/control-queenzone.ps1 launch`.
- Default URL is `http://127.0.0.1:5199`. Never attach to `http://localhost:5146`, `http://127.0.0.1:5099`, or https://www.queenzone.org unless a feature file says so.
- Run `control-queenzone.ps1 doctor` and require `Testing`, the recorded pid, `/health` `{ status: ok }`, and sample article `/news/1003/queenzone-modernisation-begins`.
- Never drive an instance that was not started by this verification run.
- Seeded in-memory titles are fixed: `QueenZone modernisation begins`, `Ranking every studio album`, `Brian in action with his guitar`. Hidden draft `Hidden moderation draft` (id 9001) must not appear on public pages.

## Driving conventions

- Start every recipe from the baseline state unless its preconditions say otherwise.
- Prefer ARIA roles, accessible names, `#qz-search`, and the Playwright handles already used in `tests/QueenZone.Web.E2E/SmokeTests.cs`.
- Treat every command as literal. Keep quoted names and routes unchanged.
- Browser actions go through Cursor browser tools or Playwright MCP against the helper URL.
- Supporting HTTP checks may use `Invoke-WebRequest` against that same URL only.
- Do not remove proof artifacts during cleanup.
- Capture PR proof with `control-queenzone.ps1 capture-proof -Feature <map-id>` after launch.

## Proof and skip reporting

- Capture the user action and the resulting state, not only the final screen.
- UI proof includes an ARIA snapshot and a screenshot with Queenzone.org visible in the masthead.
- Mutation is out of scope for these public archive features. A second GET of the same public URL is the persistence check.
- Record the feature ID and entry point used with every artifact.
- Report an unreachable path with the attempted command and the unmet precondition.
- Do not report a skipped entry point as verified through a different path.

## Feature entry contract

Each feature file starts with an H1 title and one paragraph describing the user-visible behavior. It then uses exactly four H2 sections in this order.

1. `Sub-features` lists short IDs with one line for each behavior.
2. `How to get to it (user POV)` lists every user entry point.
3. `Driving it with the browser` starts with `Preconditions:` and uses labeled bullets that pair each user action with an exact command and observable result.
4. `Gotchas` lists traps that can waste or invalidate a verification run.

Keep implementation details out of the map. Name only user paths, stable handles, required state, commands, and observable proof.

## Features

- [Homepage](./homepage.md) covers the archive hero, latest-news region, and the news card.
- [News archive](./news.md) covers the list, pagination, and sample article 1003.
- [Search the archive](./search.md) covers header search, the news-archive search box, matches, and empty state.
- [Forum](./forum.md) covers the board index, The Music category, and the seeded ranking topic.
- [Photography](./photography.md) covers the gallery index, Brian May collection, and photo 101.
