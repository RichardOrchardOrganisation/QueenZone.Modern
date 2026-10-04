# Queen crossword — epics, user stories, and acceptance criteria

Planning scope for a Queen-themed crossword that plays on the **mobile app**, the **mobile website**, and **desktop**, with an **admin section** that ships with **10 pre-populated crosswords**. Tracked as [#2050](https://github.com/RichardOrchardOrganisation/QueenZone.Modern/issues/2050) (epic) with children [#2051](https://github.com/RichardOrchardOrganisation/QueenZone.Modern/issues/2051)–[#2056](https://github.com/RichardOrchardOrganisation/QueenZone.Modern/issues/2056). This document is the narrative, acceptance criteria, verification plan, and seed content; each child issue carries its epic's stories.

Ordering is dependency order, not priority. Epic 1 (data + API) gates everything else.

## Where this starts from

The crossword should copy the **Quiz** feature's shape rather than invent a new one:

| Concern | Quiz precedent to copy |
|---|---|
| Public web pages | `Pages/Quizzes/` (`Index`, `Play`, `Leaderboard`) |
| Admin pages | `Pages/Admin/Quizzes/` (`Index`, `New`, `Edit`, `AdminQuizPageModel`, `_QuizForm`) |
| Modern EF tables | `QuizEntity` / `QuizQuestionEntity` + `Configurations/Quizzes/` ([ADR 0006](../decisions/0006-hybrid-ef-core-admin-writes.md)) |
| JSON API | `Api/Content/ContentQuizApiEndpoints.cs` under `/api/v1` ([json-api-v1.md](../architecture/json-api-v1.md)) |
| Mobile screens | `src/QueenZone.Mobile/src/screens/archive/Quiz*Screen.tsx` in `ArchiveStack` |
| Bulk content load | `import-quiz-questions` in `QueenZone.Tools` ([quiz-bulk-import.md](../quiz-bulk-import.md)) |
| Leaderboard | `/api/v1/quizzes/leaderboard`, `QuizLeaderboardScreen` |

### What "desktop app" means here

There is no native desktop client in this repo. The website already ships a PWA (`wwwroot/manifest.webmanifest`, `display: standalone`, `sw.js`). This plan treats **desktop app = the installed PWA on Windows/macOS (Edge/Chrome) plus normal desktop browsers**. If a native desktop wrapper (Electron/Tauri/MAUI) is actually wanted, that is a separate decision and a separate epic — see [Open questions](#confirmed-choices-and-open-questions).

### Target surfaces

| Surface | Runtime | Input model | Reference viewport |
|---|---|---|---|
| Mobile app | React Native (Expo dev build), Android + iOS | Touch + **custom in-app letter keyboard** | 360×780 (Android), 390×844 (iPhone) |
| Mobile website | Razor Pages in mobile Safari / Chrome Android | Touch + native soft keyboard via hidden input | 375×812 |
| Desktop | Razor Pages in Edge/Chrome/Firefox/Safari, and the installed PWA | Physical keyboard + mouse | 1280×800 and 1920×1080 |

### Glossary

- **Entry** — one answer (e.g. *14 Across, GALILEO*).
- **Cell** — one square. **Block** — a black square.
- **Checked cell** — a cell that belongs to both an Across and a Down entry.
- **Reveal** — the solver asks for a letter/word/grid to be filled in for them.
- **Clean solve** — completed with no reveals. Only clean solves are leaderboard-eligible.

---

## Epic 1 — Crossword data model, seed content, and API ([#2051](https://github.com/RichardOrchardOrganisation/QueenZone.Modern/issues/2051))

### XW-1.1 Store crosswords in modern tables

> As a backend maintainer, I want crosswords stored in modern EF Core tables, so admin writes follow ADR 0006 and nothing touches the legacy schema.

**Acceptance criteria**

- New entities: `CrosswordEntity` (Id, Slug, Title, Description, Difficulty `easy|medium|hard`, Width, Height, Status `Draft|Scheduled|Published|Archived`, PublishAt, PublishedAt, CreatedAt, CreatedByMemberId, concurrency token), `CrosswordEntryEntity` (Id, CrosswordId, Number, Direction `Across|Down`, Row, Column, Answer, Clue, Enumeration e.g. `(3,5)`, optional Explanation), and `CrosswordCellBlock` (or a serialised block mask on the crossword).
- Width and height are 5–15 inclusive. Answers are stored upper-case A–Z only; the enumeration preserves word breaks and hyphens for display.
- Concurrency token follows `20260831065121_AddAdminOptimisticConcurrencyTokens`.
- An EF migration is added; there is no change to any legacy table.
- In-memory twin repository exists alongside the EF one (same convention as `InMemoryAdminPhotoRepository`), so `Testing` uses sample data and never a real connection string.

**Verification**

- Unit: entity configuration tests (lengths, required fields, unique `(CrosswordId, Number, Direction)`, unique `Slug`).
- `QueenZone.SqlServerTests`: migration applies cleanly on SQL Server; round-trip a crossword.
- `dotnet build` + `dotnet test` default verification.

### XW-1.2 Grid validator

> As an admin and as a maintainer, I want one validator that decides whether a grid is playable, so the builder, importer, and publish step all apply the same rules.

**Acceptance criteria**

- `CrosswordGridValidator` (pure, no I/O) returns errors and warnings:
  - **Errors (block publish):** an entry's letters disagree with a crossing entry; an entry runs off the grid or through a block; an entry has fewer than 3 letters; a white run of 2+ cells has no entry; missing clue; numbering does not match standard left-to-right, top-to-bottom numbering; white cells are not all connected; answer contains non A–Z.
  - **Warnings (allowed, shown to admin):** grid not rotationally symmetric; unchecked cells exist (British style); duplicate answer within the puzzle; clue contains the answer word.
- Numbering is derived from the grid, never trusted from input.
- Validator is shared by admin builder (XW-5.x), JSON importer (XW-1.3), and publish (XW-5.6).

**Verification**

- Unit: one test per rule, plus the 10 seed puzzles must pass with zero errors (fixture test that loads `data/crosswords/*.json`).
- Property-style test: random valid grids re-numbered produce identical numbering.

### XW-1.3 Ten pre-populated crosswords

> As the site owner, I want 10 Queen crosswords available in admin from day one, so the feature launches with content and admins have worked examples to copy.

**Acceptance criteria**

- Ten JSON files live in `data/crosswords/` (one per puzzle, schema in [Appendix B](#appendix-b--seed-json-shape)). Content brief per puzzle is in [Appendix A](#appendix-a--the-10-seed-crosswords).
- A `QueenZone.Tools` command `import-crosswords --dir data/crosswords [--dry-run] [--connection-string …]` validates **every** file first and writes nothing if any file fails (same all-or-nothing contract as `import-quiz-questions`).
- Import is **idempotent by `Slug`**: re-running skips existing slugs and reports them; it never overwrites an admin's edits.
- Imported puzzles land as **Draft** (admin reviews and publishes) unless `--publish` is passed.
- The in-memory sample data used by `Testing` and the dev sample mode loads the same 10 files, so local dev, WAF tests, and Playwright see the same puzzles as production.
- Content rules: every answer is factually verifiable from the existing biography/discography archive or a cited source; clues quote **no song lyrics** beyond a song title or a single word; no member names or personal data.

**Verification**

- Unit (`QueenZone.Tools.Tests`): dry-run reports 10 puzzles / 0 errors; a corrupted fixture aborts with nothing written; second run reports 10 skipped.
- WAF: `/admin/crosswords` lists exactly 10 seeded puzzles in `Testing`.
- Manual: editorial fact-check sign-off recorded on the epic issue (one tick per puzzle).

### XW-1.4 Public crossword API

> As the mobile app, I want versioned JSON endpoints for crosswords, so the app and website share one contract.

**Acceptance criteria**

- `GET /api/v1/crosswords` — paged list of **published** puzzles (id, slug, title, difficulty, size, publishedAt, and for a signed-in member: `progress` = `notStarted|inProgress|completed`). Follows `/api/v1` pagination and Problem Details conventions.
- `GET /api/v1/crosswords/{id}` — grid shape, blocks, numbering, clues, enumerations. **Answers are never included.**
- `POST /api/v1/crosswords/{id}/check` — body is the solver's letters (whole grid or one entry); returns per-cell `correct|incorrect|empty`. Does not reveal correct letters.
- `POST /api/v1/crosswords/{id}/reveal` — body names a cell, entry, or `grid`; returns the revealed letters and marks the attempt as not clean.
- `GET/PUT /api/v1/crosswords/{id}/progress` (member only) — save/load in-progress letters, elapsed seconds, reveal flags, `updatedAt`.
- `POST /api/v1/crosswords/{id}/complete` (member only) — server re-checks the grid; records time and clean/not-clean.
- Draft and Scheduled (before `PublishAt`) puzzles return 404 to non-admins. Archived puzzles are omitted from public lists but remain playable by direct link with an **Archived** banner (Richard approved on 2026-10-04; this supersedes the conflicting archived-404 wording in #2051).
- Every new `POST`/`PUT` is classified in [`mutation-rate-limiting.md`](../architecture/mutation-rate-limiting.md) with a named rate-limit policy; `MutationEndpointInventoryTests` passes.
- Endpoints appear in OpenAPI. Changes are additive to v1 ([ADR 0019](../decisions/0019-api-versioning-convention.md)).

**Verification**

- WAF integration tests per endpoint: published vs draft visibility; answers absent from `GET {id}` payload (assert no answer string appears in the JSON); check/reveal correctness; anonymous `PUT progress` → 401; rate-limit metadata present.
- Contract: mobile `contracts/` types regenerated/updated and type-checked.

---

## Epic 2 — Core solving experience (all surfaces) ([#2052](https://github.com/RichardOrchardOrganisation/QueenZone.Modern/issues/2052))

These stories apply to **every** surface. Surface-specific behaviour is in Epics 3 and 4.

### XW-2.1 Browse crosswords

> As a visitor, I want a list of Queen crosswords with difficulty and size, so I can pick one that suits my time and skill.

**Acceptance criteria**

- Web route `/crosswords`; mobile entry Archive tab → **Crosswords** (alongside Quiz), screen `ArchiveStack/CrosswordList`.
- Each card shows title, difficulty badge, grid size, and (signed-in) a status chip: *Not started*, *In progress (n%)*, *Completed* with time.
- Newest published first; filter by difficulty.
- Empty state when nothing is published: "No crosswords yet — check back soon."
- Visitors can play without signing in; signing in is offered to save progress across devices and appear on the leaderboard.

**Verification**

- WAF: page renders published only; filter works.
- Jest: `CrosswordListScreen` renders cards, status chips, empty state, error/retry state.
- Playwright: list → open puzzle.
- Feature map: add `web/archive.json` and `mobile/archive.json` entries; `node scripts/check-feature-map.mjs` passes.

### XW-2.2 Fill in the grid

> As a solver, I want to select a cell and type letters, so I can solve the puzzle.

**Acceptance criteria**

- Selecting a cell highlights the cell and its current entry; the **active clue** (number, direction, text, enumeration) is always visible without scrolling.
- Typing a letter fills the cell and advances to the next empty cell in the entry; at the end of an entry it moves to the next entry's first empty cell.
- Delete/backspace clears the current cell; on an empty cell it moves back and clears.
- Selecting an already-selected cell toggles Across/Down when both exist.
- Selecting a clue in the clue list jumps to the first empty cell of that entry.
- Completed entries' clues are visually marked (not colour alone — e.g. strike-through or tick).
- Only A–Z accepted; lower case is upper-cased; other characters ignored.

**Verification**

- Shared grid-navigation logic is a pure module with unit tests in both stacks (C#/JS as applicable): next-cell, previous-cell, toggle, jump-to-clue, end-of-grid wrap.
- Jest + Playwright interaction tests for the same scenarios.

### XW-2.3 Check and reveal

> As a solver, I want to check or reveal a letter, word, or the whole grid, so I can get unstuck.

**Acceptance criteria**

- Menu offers **Check** (cell / word / grid) and **Reveal** (cell / word / grid).
- Incorrect letters are marked (e.g. red slash) and stay marked until changed. Revealed cells carry a persistent marker (e.g. corner triangle).
- Reveal word/grid asks for confirmation ("This solve won't count for the leaderboard").
- Any reveal sets the attempt to *not clean*.
- Optional **auto-check** toggle (off by default) — enabling it also marks the attempt not clean.

**Verification**

- WAF: check/reveal endpoints (XW-1.4).
- Jest/Playwright: confirm dialog appears; markers render; clean flag changes.

### XW-2.4 Timer, pause, and completion

> As a solver, I want a timer and a completion moment, so finishing feels like an achievement.

**Acceptance criteria**

- Timer starts on first keystroke, pauses when the puzzle is backgrounded/hidden (Page Visibility API on web; `AppState` on mobile) or the solver taps Pause; paused state hides the grid.
- When every cell is filled, the server confirms correctness. Correct → completion screen with time, clean/not-clean, and (members) leaderboard position. Incorrect → "Not quite — some letters are wrong" with a *Check grid* shortcut; the timer keeps running.
- Completion screen offers **Share** (spoiler-free text: title, time, clean ✓) and **Next crossword**.

**Verification**

- Unit: timer pause/resume accounting.
- Jest: `AppState` background pauses timer.
- Playwright: complete a seeded 7×7 puzzle end to end; assert completion screen.

### XW-2.5 Save and resume progress

> As a solver, I want my progress saved, so I can stop and come back — including on a different device.

**Acceptance criteria**

- Signed-out: progress saved locally (web `localStorage` wrapped in try/catch; mobile local storage) per puzzle id; never blocks play if storage is unavailable.
- Signed-in: progress syncs to `PUT /progress` (debounced, ~2 s after last keystroke and on pause/background). Opening the puzzle on another surface restores letters, reveal markers, and elapsed time.
- Conflict rule: **last write wins by `updatedAt`**, whole-grid. If the server copy is newer than local on open, the server copy is used and the solver sees "Progress restored from another device".
- On first sign-in, an in-progress local attempt is offered for upload ("Keep progress from this device?").
- Mobile app offline: letters keep saving locally and queue through `src/offlineQueue/`; check/reveal show "Needs a connection" while offline.

**Verification**

- WAF: progress PUT/GET round-trip; stale `updatedAt` rejected/ignored per rule.
- Jest: offline queue receives progress writes; flush on reconnect.
- Manual cross-device script: start on Android app → continue on desktop web → finish on mobile web; letters and time carry over.

### XW-2.6 Accessibility

> As a solver using assistive technology, I want the crossword to be operable and understandable, so I can play too.

**Acceptance criteria**

- WCAG 2.1 AA. Colour is never the only signal (selection, incorrect, revealed, completed all have a shape/pattern too). Contrast ≥ 4.5:1 for letters and clue text, ≥ 3:1 for grid lines and highlights, in light and dark themes.
- Web grid uses `role="grid"` / `gridcell`; each cell's accessible name reads e.g. "14 Across, 7 letters, letter 3, blank. Also 3 Down." Live region announces clue changes and check results.
- Mobile cells expose `accessibilityLabel` with the same information; VoiceOver/TalkBack can move cell to cell and type.
- Touch targets ≥ 44×44 pt on mobile (achieved via zoom for large grids, see XW-3.3).
- Respects reduced motion (no celebratory animation) and OS text size for clues.

**Verification**

- Playwright + axe (`AccessibilitySmokeTests` / `AxeAssertions`): no serious/critical violations on list and play pages.
- Skill: `design:accessibility-review` audit on mockups before build.
- Manual: VoiceOver (iOS + macOS Safari) and TalkBack (Android) script — select clue, enter answer, check word.

---

## Epic 3 — Mobile app (React Native) ([#2053](https://github.com/RichardOrchardOrganisation/QueenZone.Modern/issues/2053))

### XW-3.1 Crossword screens in the app

> As a mobile member, I want crosswords in the app, so I can solve on my phone without a browser.

**Acceptance criteria**

- Screens `ArchiveStack/CrosswordList`, `ArchiveStack/CrosswordPlay`, `ArchiveStack/CrosswordLeaderboard`, using `usePagedContent` for the list and `useDetailQuery` for a puzzle via `fetchCached` (no new server-state library — ADR 0018).
- Brand theme tokens from the existing RN theme; dark mode supported.
- Deep link `queenzone://crosswords/{slug}` and the web URL `/crosswords/{slug}` open the play screen.

**Verification**

- Jest tests per screen (render, loading, error, empty).
- Feature-map entries for each screen with test IDs.
- Maestro flow `flows/NN-crossword.yaml`: Archive → Crosswords → open seed puzzle → type a word → check word → back.

### XW-3.2 Custom in-app keyboard

> As a mobile solver, I want a crossword keyboard that doesn't cover the grid, so I can see the clue and the cells while typing.

**Acceptance criteria**

- A fixed QWERTY letter keyboard with backspace, rendered in-app; the system keyboard never opens on the play screen.
- Layout top-to-bottom: grid → active-clue bar (with ‹ › to previous/next clue, tap to toggle direction) → keyboard. All visible without scrolling at 360×640 for grids up to 9×9.
- Haptic tick on key press (respects system setting).
- Works on iOS and Android with identical behaviour.

**Verification**

- Jest: key press dispatches letter; backspace behaviour.
- Maestro on Android emulator and iOS simulator; screenshots at 360×640 and 390×844 attached as proof.

### XW-3.3 Large grids on small screens

> As a mobile solver, I want to zoom and pan large grids, so 13×13 and 15×15 puzzles are playable on a phone.

**Acceptance criteria**

- Grids larger than 9×9 open fitted-to-width; pinch-zoom and pan supported; selecting a cell auto-scrolls it into view above the clue bar.
- At default zoom the active entry is fully visible.
- No conflict with the existing `react-native-reanimated` pins (#1782).

**Verification**

- Manual on a small Android device (≤ 5.5") and iPhone SE-class simulator with the 15×15 seed puzzle.
- Maestro: open 15×15 seed puzzle, select a cell in the bottom-right, assert the clue bar shows the right clue.

### XW-3.4 New-crossword notification (optional)

> As a mobile member, I want an optional push notification when a new crossword is published, so I don't miss one.

**Acceptance criteria**

- Off by default; toggle in notification settings using the existing transport ([ADR 0014](../decisions/0014-push-notification-transport-and-dispatch.md)).
- Fires once per puzzle at publish time (including scheduled publishes); tapping it deep-links to the puzzle.

**Verification**

- Unit: dispatch fires once per publish, not on re-publish.
- Manual per [`mobile-push-testing.md`](../mobile-push-testing.md).

---

## Epic 4 — Mobile website and desktop ([#2054](https://github.com/RichardOrchardOrganisation/QueenZone.Modern/issues/2054))

### XW-4.1 Mobile web play page

> As a mobile web visitor, I want the crossword to work in my phone's browser, so I don't have to install the app.

**Acceptance criteria**

- Route `/crosswords/{slug}`; Razor Page + progressive JS module (no SPA framework).
- Tapping a cell focuses a visually-hidden input so the native soft keyboard opens; `inputmode="text"`, `autocapitalize="characters"`, `autocomplete="off"`, `autocorrect="off"`, `spellcheck="false"`.
- The active-clue bar sticks directly above the soft keyboard using the `visualViewport` API; the selected cell scrolls into view when the keyboard opens.
- Page does not zoom on input focus (input font-size ≥ 16 px); double-tap zoom on the grid is disabled but pinch-zoom of the page still works.
- Works in iOS Safari (current and previous major) and Chrome Android.
- Without JavaScript the page shows the clues and an empty printable grid (graceful degradation).

**Verification**

- Playwright mobile projects (iPhone and Pixel emulation): type an answer, check, complete.
- Manual on a real iPhone (Safari) and Android (Chrome): keyboard does not cover the clue bar; no input zoom.
- Note: Expo web is not mobile proof — this is the Razor site, tested separately.

### XW-4.2 Desktop play page

> As a desktop solver, I want full keyboard control and both clue lists on screen, so I can solve quickly.

**Acceptance criteria**

- At ≥ 1024 px: grid left, Across and Down clue lists right (independently scrollable), active clue above the grid.
- Keyboard: letters type; arrows move (arrow perpendicular to current direction first switches direction); **Tab / Shift+Tab** next/previous entry; **Space** toggles direction; **Backspace/Delete** clear; **Esc** closes menus; **Ctrl/⌘+Enter** check word.
- Mouse: click cell; click clue jumps.
- Clue list auto-scrolls to keep the active clue visible.
- **Print** view: blank grid + clues on one A4/Letter page, no site chrome.

**Verification**

- Playwright desktop (Chromium, Firefox, WebKit): keyboard navigation test covering every shortcut.
- Playwright print-media snapshot of the 13×13 seed puzzle.

### XW-4.3 Installed desktop app (PWA)

> As a desktop fan, I want the crossword to work in the installed QueenZone app window, so it feels like an app.

**Acceptance criteria**

- Crosswords are reachable from the installed PWA's navigation; layout is correct in a standalone window (no browser chrome) at 1280×800 and when resized narrow (falls back to the mobile layout below 768 px).
- `sw.js` caches the play page shell and JS so a previously opened puzzle loads offline and keeps local progress; check/reveal show "Needs a connection" offline.
- Optional manifest shortcut "Crossword" (deep link to `/crosswords`).

**Verification**

- Manual: install in Edge on Windows and Chrome on macOS; play, go offline, reload, continue.
- Lighthouse PWA/installability check still passes after the change.

### XW-4.4 Home page teaser

> As a visitor, I want the latest crossword on the home page, so I discover it.

**Acceptance criteria**

- A home partial (like `_HomeSprintQuiz.cshtml`) shows the newest published crossword with *Play* / *Continue* CTA; mobile Home gets an equivalent card through `useHomeSection`.
- Hidden when nothing is published.

**Verification**

- WAF: partial renders/hides correctly. Jest: home card states.

---

## Epic 5 — Admin section ([#2055](https://github.com/RichardOrchardOrganisation/QueenZone.Modern/issues/2055))

Admin access uses the existing admin scheme and `Admin:AllowedEmails`. Admin pages are desktop-first but must be usable on a tablet; phone admin is out of scope for the builder (list/publish must still work on a phone).

### XW-5.1 Admin crossword list

> As an admin, I want to see every crossword and its status, so I can manage the catalogue.

**Acceptance criteria**

- `/admin/crosswords` lists all puzzles, including the 10 seeded drafts, with title, slug, difficulty, size, status, publish date, last edited by/at, starts, completions.
- Filter by status; search by title; sort by publish date.
- Row actions: Edit, Preview, Publish/Unpublish, Duplicate, Archive.
- Linked from the admin dashboard (`Pages/Admin/Index`).
- Non-admins get the standard admin 403/redirect.

**Verification**

- WAF: lists 10 seeded drafts; non-admin denied; filters.
- Playwright admin smoke (`AdminSmokeTests`): page loads, axe clean.

### XW-5.2 Create a crossword in the grid builder

> As an admin, I want to build a crossword visually, so I don't need to hand-write JSON.

**Acceptance criteria**

- *New crossword*: title, description, difficulty, width × height (5–15).
- Builder modes: **Blocks** (click to toggle block; *symmetry* on by default mirrors the 180° partner) and **Letters** (type answers straight into the grid).
- Numbering recalculates live; the clue panel auto-lists every entry with its number, direction, current letters, and a clue + enumeration field.
- Live validator panel (XW-1.2) shows errors and warnings; clicking one selects the offending cell/entry.
- Saving a draft is allowed with errors; publishing is not.
- Optional helper: pattern lookup (`G?L?L?O`) against a Queen word list built from the archive (song titles, albums, people, places) — nice-to-have.

**Verification**

- WAF: create → save draft → reload shows identical grid and clues.
- Playwright: build a 5×5 from scratch, fix a validator error, save.

### XW-5.3 Edit an existing crossword

> As an admin, I want to edit any crossword, including the seeded ones, so I can fix clues or improve grids.

**Acceptance criteria**

- Same builder pre-loaded. Optimistic concurrency: if another admin saved first, show the standard conflict message and do not overwrite.
- Editing a **published** puzzle's clue text/explanation saves immediately. Changing **grid shape or answers** on a published puzzle requires confirmation ("Members with progress on this puzzle will have their progress reset") and resets stored progress for it; completed attempts and leaderboard entries are kept.

**Verification**

- WAF: concurrency conflict test (two saves with same token); published answer change resets progress rows but not completions.

### XW-5.4 Preview as a solver

> As an admin, I want to play-test a puzzle before publishing, so I catch bad clues.

**Acceptance criteria**

- *Preview* opens the real play page for a draft (admin-only), with a banner "Preview — not published", a *Show answers* toggle, and mobile/desktop width presets.
- Preview attempts are never saved or ranked.

**Verification**

- WAF: non-admin gets 404 for the draft; admin preview writes no attempt rows.

### XW-5.5 Clue explanations

> As an admin, I want to attach a short explanation to each clue, so solvers learn something after revealing or completing.

**Acceptance criteria**

- Optional per-entry *Explanation* (max 300 chars), optionally linking to an archive page (e.g. the discography album page).
- Shown to solvers only after the entry is completed or revealed, and on the completion screen's *Review answers* view.

**Verification**

- WAF: explanation absent from `GET {id}` payload; present after reveal/complete.

### XW-5.6 Publish, schedule, unpublish

> As an admin, I want to publish now or schedule a date, so I can release puzzles on a cadence (e.g. weekly).

**Acceptance criteria**

- *Publish now* and *Schedule for…* (date/time, site time zone shown). Both blocked while the validator has errors.
- Scheduled puzzles become visible at `PublishAt` without a deploy or restart (query filters on `PublishAt <= now`, and public cache entries for the list and home teaser are invalidated or short-lived enough).
- *Unpublish* returns a puzzle to Draft; members' progress is kept. *Archive* hides it from public lists but keeps direct links working with an "archived" banner (confirmed by Richard on 2026-10-04).
- Bulk action on the list: *Publish selected* (to release seeded puzzles in one go).

**Verification**

- WAF with a fake clock: scheduled puzzle hidden before and visible after `PublishAt`.
- WAF: publish blocked when invalid.

### XW-5.7 Import and export JSON

> As an admin, I want to import and export a crossword as JSON, so puzzles can be authored elsewhere, backed up, and moved between environments.

**Acceptance criteria**

- *Export* downloads the [Appendix B](#appendix-b--seed-json-shape) format, including answers.
- *Import* uploads one JSON file, runs the validator, shows a preview, and creates a new Draft (never overwrites; slug collision prompts for a new slug).
- Shares parsing/validation code with `import-crosswords` (XW-1.3).
- Upload limited to 256 KB, `application/json` only.

**Verification**

- WAF: export → import round-trip produces an identical puzzle; malformed JSON rejected with field-level errors.

### XW-5.8 Duplicate and archive

> As an admin, I want to duplicate a puzzle as a starting point and archive old ones, so I can work faster and keep the list tidy.

**Acceptance criteria**

- *Duplicate* creates a Draft "Copy of …" with a new slug and no attempts.
- *Archive* is a soft state; there is no hard delete in the UI. Hard delete of a puzzle with zero attempts may be added later.

**Verification**

- WAF: duplicate copies grid/clues, not attempts; archived hidden from public list.

### XW-5.9 Audit log

> As the site owner, I want every admin change recorded, so moderation is accountable.

**Acceptance criteria**

- `CrosswordAuditLogEntity` records actor email, action (`Created`, `Edited`, `Published`, `Scheduled`, `Unpublished`, `Archived`, `Imported`, `Duplicated`), timestamp, and a short diff summary.
- Visible on the puzzle's edit page, newest first.
- Seed import writes `Imported` entries attributed to the tool.

**Verification**

- WAF: each action writes exactly one audit row.

### XW-5.10 Puzzle statistics

> As an admin, I want to see how each puzzle performs, so I can tune difficulty.

**Acceptance criteria**

- Per puzzle: starts, completions, completion rate, clean-solve rate, median solve time, and the five most-revealed entries.
- Counts include members only (anonymous play isn't tracked server-side beyond check/reveal calls).

**Verification**

- WAF with seeded attempts: numbers match a hand-computed fixture.

---

## Epic 6 — Members, leaderboard, and sharing ([#2056](https://github.com/RichardOrchardOrganisation/QueenZone.Modern/issues/2056))

### XW-6.1 Per-puzzle leaderboard

> As a member, I want a leaderboard of fastest clean solves, so I can compete with other fans.

**Acceptance criteria**

- Per-puzzle top 50 by time, clean solves only, first completion per member only. Shows display name (respecting existing member privacy settings), time, date.
- My own rank shown even if outside the top 50.
- Web `/crosswords/{slug}/leaderboard`; mobile `ArchiveStack/CrosswordLeaderboard`; API `GET /api/v1/crosswords/{id}/leaderboard`.
- Server rejects implausible times (< 10 s per 5×5-equivalent, configurable) from ranking.

**Verification**

- WAF: ordering, clean-only, first-attempt-only, implausible time excluded.
- Jest: leaderboard screen states.

### XW-6.2 My crosswords

> As a member, I want to see which crosswords I've finished, so I can track my streak.

**Acceptance criteria**

- Account area section listing completed puzzles with times and clean badges, plus total completed and current weekly streak (completed at least one puzzle in each consecutive week).

**Verification**

- WAF: streak calculation across week boundaries (fake clock).

---

## Non-functional requirements

| Area | Requirement | How verified |
|---|---|---|
| Performance | Play page interactive < 2.5 s on mid-range Android over 4G; puzzle JSON < 20 KB for 15×15 | Lighthouse mobile run; payload size assertion in WAF test |
| Hosting | No new infrastructure; fits single-instance B1 with no Redis | Design review against `hosting-scale-and-cache.md` |
| Security | No answers in public payloads; admin endpoints behind admin scheme; mutations rate-limited and antiforgery-protected (web) | WAF tests; `MutationEndpointInventoryTests` |
| Privacy | Leaderboard respects member privacy; local storage holds only puzzle progress | Code review; WAF |
| Store lag | App tolerates the feature being absent on an older API and unknown fields on a newer one | Jest with mocked 404 / extra fields |
| Browsers | Last two majors of Chrome, Edge, Firefox, Safari (macOS + iOS), Chrome Android | Playwright projects + manual device pass |

## Verification matrix

| Layer | Tooling in this repo | Covers |
|---|---|---|
| Unit (.NET) | xUnit in `QueenZone.Web.Tests`, `QueenZone.Tools.Tests` | Validator, numbering, importer, timer maths, streaks |
| Web integration | `WebApplicationFactory` in `QueenZone.Web.Tests` (`Testing` env, in-memory data) | API contract, visibility, admin flows, concurrency, rate-limit inventory |
| SQL Server | `QueenZone.SqlServerTests` | Migration, real column types |
| Browser E2E | Playwright in `QueenZone.Web.E2E` (desktop + mobile emulation) + axe | Solve flows, keyboard shortcuts, print, admin smoke, accessibility |
| Mobile unit | Jest in `src/QueenZone.Mobile` | Screens, keyboard, offline queue, AppState pause |
| Mobile device | Maestro flow + `capture-proof`; `mobile-device-smoke.yml` (`suite: proof`) for cloud agents | Real Android/iOS play-through, screenshots |
| Feature map | `node scripts/check-feature-map.mjs` / `npm run preflight` | Every new screen/page mapped |
| Manual | Device pass (real iPhone, real Android, Windows Edge PWA, macOS Chrome PWA), VoiceOver/TalkBack script, editorial fact-check | Things automation can't prove |

**Definition of done for the epic:** all seed puzzles playable to completion on all three surfaces; default verification (`dotnet restore/build/format/test`) green; Playwright + Maestro flows green; PR `## Verification` section filled with proof links for web and mobile.

---

## Confirmed choices and open questions

1. **Desktop app — confirmed.** Use the installed PWA and normal desktop browsers.
2. **Answer secrecy — confirmed.** Answers stay server-side; check/reveal require a connection.
3. **Seeded puzzle status — confirmed.** Import as Draft for editorial review before release.
4. **Style — confirmed.** Richard approved British-style grids with unchecked cells for the first ten seeds on 2026-10-04.
5. **Cryptic clues.** Out of scope for v1 (straight clues only); a future "cryptic" difficulty could reuse everything here.
6. **Archived puzzle links — confirmed.** Richard approved playable direct links with an Archived banner on 2026-10-04. Archives remain hidden from public lists.

## Non-goals (v1)

- Rebus cells (multiple letters in one square), circled/shaded themed cells, and barred grids.
- Real-time co-op solving.
- Member-submitted crosswords (could later follow the quiz-question submission pattern).
- Native desktop wrapper (see open question 1).

---

## Appendix A — The 10 seed crosswords

Each puzzle lists its theme, target size and difficulty, and a **candidate answer bank with clues**. The constructor builds the final grid from the bank (using the builder or an offline filler) and may drop or add entries to make it interlock; every final answer must pass the content rules in XW-1.3. Clues are written to stand alone (no "see 5-Across" cross-references), so they survive re-gridding. Facts below should still be checked against the site's own biography/discography during editorial sign-off.

Sizes are chosen so phones get small grids first and every bank answer fits: one 7×7 starter, one 9×9, four 11×11, two 13×13, two 15×15.

### 1. `meet-the-band` — Meet the Band (7×7, easy)

| Answer | Clue |
|---|---|
| FREDDIE | Lead singer born in Zanzibar |
| BRIAN | Guitarist with a home-made guitar |
| ROGER | Drummer who also sang the high parts |
| JOHN | Bass player, the last to join |
| MAY | Guitarist's surname, also a month |
| SMILE | Pre-Queen band of May and Taylor |
| BASS | Deacon's instrument |
| DRUMS | Taylor's kit |

### 2. `a-night-at-the-opera` — Bohemian Rhapsody (11×11, easy)

| Answer | Clue |
|---|---|
| GALILEO | Astronomer whose name echoes through the operatic section |
| FIGARO | Barber of Seville, name-checked in the song |
| SCARAMOUCHE | Stock clown asked to do the fandango |
| FANDANGO | Spanish dance Scaramouche is asked to do |
| BISMILLAH | Arabic phrase sung in the opera section |
| BEELZEBUB | Devil said to have one put aside |
| MAMA | Who the narrator addresses in the ballad |
| OPERA | Middle section style; also part of the album title |
| BALLAD | Style of the song's opening section |
| ROCKFIELD | Welsh studio where recording began |

### 3. `studio-albums` — The Studio Albums (15×15, medium)

| Answer | Clue |
|---|---|
| JAZZ | 1978 album featuring "Bicycle Race" |
| INNUENDO | 1991 album, the last released in Freddie's lifetime |
| THEMIRACLE | 1989 album with a four-headed cover (3,7) |
| HOTSPACE | 1982 album with a funk and disco turn (3,5) |
| THEGAME | 1980 album with "Another One Bites the Dust" (3,4) |
| THEWORKS | 1984 album with "Radio Ga Ga" (3,5) |
| NEWSOFTHEWORLD | 1977 album with a giant robot on the cover (4,2,3,5) |
| AKINDOFMAGIC | 1986 album tied to *Highlander* (1,4,2,5) |
| MADEINHEAVEN | 1995 album completed after Freddie's death (4,2,6) |
| FLASHGORDON | 1980 soundtrack album (5,6) |
| QUEENII | 1974 album with "white" and "black" sides (5,2) |

### 4. `freddie` — Freddie Mercury (11×11, medium)

| Answer | Clue |
|---|---|
| ZANZIBAR | Island of Freddie's birth |
| FARROKH | Freddie's given first name |
| BULSARA | Freddie's family surname |
| PANCHGANI | Indian hill town of his boarding school |
| DELILAH | Cat who got her own song on *Innuendo* |
| BARCELONA | 1988 duet album and Olympic anthem |
| CABALLE | Montserrat, Spanish soprano and duet partner |
| MRBADGUY | 1985 solo album (2,3,3) |
| GARDENLODGE | Kensington home (6,5) |
| EALING | Art college where he studied graphic design |

### 5. `brian-may` — Brian May (11×11, medium)

| Answer | Clue |
|---|---|
| REDSPECIAL | Guitar Brian built with his father (3,7) |
| SIXPENCE | Coin used as a plectrum |
| PHD | Doctorate he completed in 2007 |
| IMPERIAL | London college that awarded his doctorate |
| VOX | Amp brand of the AC30 |
| ANITA | Wife, actress Ms Dobson |
| STEREO | Kind of photography (and viewers) he champions |
| NASA | Agency behind New Horizons, which he worked with |
| BADGERS | Animals he has campaigned to protect |
| HAROLD | Father who helped build the guitar |

### 6. `roger-and-john` — Roger and John (11×11, medium)

| Answer | Clue |
|---|---|
| TRURO | Cornish city where Roger grew up |
| DENTISTRY | Roger's first course of study |
| BIOLOGY | Degree Roger switched to |
| THECROSS | Roger's side band of the late 1980s (3,5) |
| RADIOGAGA | Roger's 1984 hit (5,3,3) |
| LEICESTER | John's home city |
| ELECTRONICS | John's degree subject |
| DEACY | John's nickname |
| BREAKFREE | "I Want to ___ ___", a John song (5,4) |
| CHELSEA | College where John studied |

### 7. `live-aid` — Live Aid, 1985 (9×9, easy)

| Answer | Clue |
|---|---|
| WEMBLEY | London stadium of the 1985 set |
| GELDOF | Bob who organised the concert |
| URE | Midge, co-organiser |
| JULY | Month of the concert |
| ETHIOPIA | Country the concert raised famine relief for |
| PIANO | Instrument the set opened at |
| GAGA | "Radio ___", second song of the set |
| CHAMPIONS | "We Are the ___", the closer |
| ROCKYOU | "We Will ___ ___" (4,3) |
| SETLIST | Running order Queen famously tightened for TV (3,4) |

### 8. `on-tour` — On Tour (13×13, hard)

| Answer | Clue |
|---|---|
| KNEBWORTH | 1986 venue of Freddie's last show with Queen |
| BUDAPEST | Népstadion city, 1986 |
| RIO | Rock in ___, January 1985 |
| HYDEPARK | Free London concert, September 1976 (4,4) |
| MONTREAL | City of the 1981 Forum concert film |
| MILTONKEYNES | Bowl venue filmed in 1982 (6,6) |
| MAGICTOUR | 1986 tour (5,4) |
| MOTTTHEHOOPLE | Band Queen supported in 1973 (4,3,6) |
| ADAMLAMBERT | Singer with Queen since 2011 (4,7) |
| PAULRODGERS | Singer for Queen + in the 2000s (4,7) |
| ARGENTINA | South American country of the 1981 stadium shows |

### 9. `silver-screen` — Queen on Screen (13×13, medium)

| Answer | Clue |
|---|---|
| FLASHGORDON | 1980 film with a Queen score (5,6) |
| MING | Flash Gordon's merciless foe |
| HIGHLANDER | 1986 film of immortals |
| KURGAN | Highlander's villain |
| MALEK | Rami who played Freddie |
| OSCAR | Award Rami Malek won for playing Freddie |
| WAYNESWORLD | 1992 film that revived "Bohemian Rhapsody" (6,5) |
| HEADBANG | What the car crew famously did in *Wayne's World* |
| BIOPIC | 2018 film genre |
| MAZZELLO | Joe who played John Deacon |

### 10. `studios-and-collaborators` — Studios and Collaborators (15×15, hard)

| Answer | Clue |
|---|---|
| BOWIE | Co-writer of "Under Pressure" |
| UNDERPRESSURE | 1981 duet single (5,8) |
| MACK | Reinhold, Munich-based producer |
| MUSICLAND | Mack's Munich studio |
| MONTREUX | Swiss town of Mountain Studios |
| TRIDENT | London studio of the first album |
| BAKER | Roy Thomas, producer of the early albums |
| ELEKTRA | US label until 1983 |
| EMI | UK record label |
| ROCKFIELD | Welsh studio of "Bohemian Rhapsody" sessions |
| CAPITOL | US label from 1983 |
| STAFFELL | Tim of Smile |

## Appendix B — Seed JSON shape

```json
{
  "slug": "a-night-at-the-opera",
  "title": "Bohemian Rhapsody",
  "description": "Six minutes, one crossword.",
  "difficulty": "easy",
  "width": 11,
  "height": 11,
  "style": "american",
  "grid": [
    "GALILEO####",
    "A##########"
  ],
  "entries": [
    {
      "number": 1,
      "direction": "across",
      "answer": "GALILEO",
      "clue": "Astronomer whose name echoes through the operatic section",
      "enumeration": "(7)",
      "explanation": "Sung repeatedly in the 1975 single's operatic section."
    }
  ]
}
```

- The `grid` above is truncated to two rows for brevity; a real file has exactly `height` rows of `width` characters.
- `grid` is one string per row: `#` = block, `A`–`Z` = solution letter. Numbers and entry positions are **derived** by the validator; `entries` supplies only clue text, enumeration, and explanation, matched by number + direction, and the importer fails if `answer` disagrees with the grid.
