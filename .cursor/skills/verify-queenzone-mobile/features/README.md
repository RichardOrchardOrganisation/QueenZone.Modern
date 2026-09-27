# QueenZone.Mobile verification map

The machine-readable map of every mobile screen lives in [`docs/feature-map/`](../../../../docs/feature-map/README.md). Read that index to resolve a vague request to an `id`, sources, `testIds`, and Maestro flows. This folder keeps the prose recipes those entries point at via `recipe`.

This directory is the maintained source for verifying visitor-facing QueenZone.Mobile behavior. Read this index before driving the app, then use the matching feature file as the recipe.

## Baseline preconditions

- Launch the Testing contract host with `pwsh -File .cursor/skills/verify-queenzone-mobile/scripts/control-queenzone-mobile.ps1 launch`.
- Default URL is `http://127.0.0.1:5098`. The Android APK must be baked at `http://10.0.2.2:5098`.
- Run `control-queenzone-mobile.ps1 doctor` and require `Testing`, the recorded pid, `/health`, fixture `environment=Testing`, and `GET /api/v1/content/news/1003` titled `QueenZone modernisation begins`.
- A Debug build of `org.queenzone.mobile` must be installed on a booted API 36 emulator (Android) or Simulator (iOS, macOS only). Expo Go is not valid.
- Never drive a Metro session or production API the user already has open.
- Seeded titles are fixed: `QueenZone modernisation begins`, `Ranking every studio album`, `Contract Member`. Hidden web draft `Hidden moderation draft` must not appear.

## Driving conventions

- Start every recipe from the launched app state (`01-launch.yaml` visible `home-screen` / `home-hero`) unless a feature says otherwise.
- Prefer `testID` values from `src/QueenZone.Mobile/src/test/testIds.ts` and the ids already used in `src/QueenZone.Mobile/maestro/flows/`.
- Treat every Maestro command as literal.
- Run one flow through `control-queenzone-mobile.ps1 drive -Flow <id>` (aliases resolve from `docs/feature-map/`).
- Capture PR proof with `control-queenzone-mobile.ps1 capture-proof -Feature <map-id>`.
- Do not remove proof artifacts during cleanup. Do not kill the emulator during cleanup.

## Proof and skip reporting

- Capture the tap and the resulting screen, not only the final screenshot.
- UI proof includes Maestro debug screenshots and the JUnit row for that flow.
- `curl` of `/api/v1` is a host-health check, not visitor proof.
- `queenzone://smoke-auth` is Debug-only. Never print the token.
- Record the feature ID and flow file with every artifact.
- Report an unreachable path with the attempted command and the unmet precondition (missing Maestro, no device, APK baked at the wrong origin).

## Feature entry contract

Each feature file starts with an H1 title and one paragraph describing the user-visible behavior. It then uses exactly four H2 sections in this order.

1. `Sub-features`
2. `How to get to it (user POV)`
3. `Driving it with Maestro`
4. `Gotchas`

## Features

- [Home](./home.md) covers launch, the hero, and opening sample article 1003.
- [News](./news.md) covers the News tab and story 1003.
- [Photography](./photography.md) covers Photos, Brian May, and photo 101.
- [Search](./search.md) covers home search and the modernisation result.
- [Forum](./forum.md) covers The Music and the ranking thread.
