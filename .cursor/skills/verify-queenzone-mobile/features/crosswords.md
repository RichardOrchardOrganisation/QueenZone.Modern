# Crosswords

Members and guests can open Queen crosswords, type on the in-app keyboard, check or reveal selections, pause and restore device progress.

## Sub-features

- `mobile.crosswords.list`: difficulty/size filters and personal progress status.
- `mobile.crosswords.play`: shared navigation, native keyboard, zoom/pan, checks/reveals, local persistence and member sync.
- Home's crossword card opens the newest published puzzle and offers Continue for an in-progress solve.

## How to get to it (user POV)

Open Archive, then Crosswords and Meet the band. Type BRIAN, check word and return to the list. Canonical `queenzone://crosswords/{slug}` links open the same screen.

## Driving it with Maestro

Use a Release-embedded build with the Testing host. Run `31-crossword.yaml` for list → play → type → check → back. The public Testing-only crossword publication fixture exposes the 15×15 sample for zoom proof; it must never be enabled outside Testing. Use `32-crossword-large.yaml` to open that seed, zoom and select its final playable cell. Proof is recorded under `mobile.crosswords.play`.

On Veronica, explicitly select a dedicated simulator using `IOS_SIM_UDID` and `MAESTRO_TARGET_DEVICE`. Richard authorised a separate simulator to preserve the Store app. The smoke runner targets that UDID for install, data and screenshots; it never installs onto the Store simulator. The PowerShell helper still has Windows-only port/process operations; use the repository's supported device-smoke runner for rebuilding the Release-embedded app.

## Gotchas

Answers are never in the cached detail. Checks, reveals and completion need a connection. Every mutation sends a current nonempty playVersion; old versions need a reload. A cached puzzle remains playable offline, with saves partitioned by guest/member. Signed-out writes cannot be queued for a previous account. Reveal word/grid requires confirmation and permanently marks assistance. Crosses, strike-through and triangles distinguish statuses without colour alone. Universal web links additionally require the website/app signing association, which must be verified independently from the custom scheme.
