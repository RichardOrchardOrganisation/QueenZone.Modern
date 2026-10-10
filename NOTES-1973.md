# ISSUE-1973 implementation notes

Implemented against `LOCK-1973.md`. Agent: Codex. No deployment performed.

## Files changed

- `src/QueenZone.Web/wwwroot/js/forum/youtube-scheduler.js` — new dependency-free scheduling, retention, generation and message-validation functions.
- `src/QueenZone.Web/wwwroot/js/forum/youtube-video.js` — two observers, three-frame pool, playback bridge, lifecycle cleanup, focus handling, fallback and explicit failure/retry.
- `tests/js/youtube-scheduler.test.cjs` — nine pure scheduler tests.
- `tests/js/youtube-video.test.cjs` — ten adapter tests using a minimal DOM/event/timer harness; no dependencies or network.
- `src/QueenZone.Web/Pages/Forum/Topic.cshtml` and `TopicPage.cshtml` — load scheduler before adapter.
- `src/QueenZone.Web/Pages/Forum/_ForumVideoCard.cshtml` — server-rendered configured origin and revised per-card privacy copy.
- `src/QueenZone.Web/Pages/Forum/_ForumPostList.cshtml` — one privacy notice above the posts when eligible videos exist. It remains visible even when the first video belongs to a collapsed blocked-author post.
- `src/QueenZone.Data/SampleData/SampleForumData.cs` — topic 1029 has 30 unique eligible cards across 15 posts on one page; existing 1030 fixture retained.
- `tests/QueenZone.Web.E2E/ForumYoutubeVideoTests.cs` — real-origin nocookie response stub, nearby/far network checks, scroll stress, cap/duplicate checks, bridge spoof/one-playing/hidden checks, stable layout/focus, Retry, fallback and cleanup; no-JS, viewport, quote and pagination coverage retained.
- `tests/QueenZone.Web.Tests/ForumVideoRenderingTests.cs` — exact frame/script/ancestor/object/referrer policy pins and privacy/fixture assertions.
- `.github/workflows/scripts-tests.yml` — syntax checks and both Node test suites.
- `scripts/Run-E2E.ps1` — local test hosts set `Site__PublicBaseUrl` to their actual listening origin so tests exercise automatic loading rather than the origin-mismatch fallback.
- `docs/architecture/forum-youtube-policy.md` — loading policy, constants, bridge, fallback, privacy, absence of consent mechanism and real-player proof requirements.
- `docs/feature-map/web/forum.json` and generated `docs/feature-map/README.md` — scheduler source and 30-card fixture mapping.
- `.cursor/skills/verify-queenzone/features/forum.md` — updated proof recipe.
- `NOTES-1973.md` — this handoff.

The supplied `LOCK-1973.md` and `ISSUE-1973.md` were not edited. `ForumVideoContent`, `UgcHtml`, descriptor/API contracts, exclusions, per-post limits, dedupe, CSP implementation and design tokens were not changed. No mobile changes or legacy writes.

## Test results

PASS:

- `node --check src/QueenZone.Web/wwwroot/js/forum/youtube-scheduler.js`
- `node --check src/QueenZone.Web/wwwroot/js/forum/youtube-video.js`
- `node --test tests/js/youtube-scheduler.test.cjs tests/js/youtube-video.test.cjs` — both suites passed. Also ran Node 24 with `--test-isolation=none` to enumerate all **19 passing tests** (9 scheduler + 10 adapter), including the final scroll-before-retain-callback regression.
- `node scripts/check-feature-map.mjs` — passed; generated index updated with `--write` beforehand.
- `node scripts/check-suppressions.mjs` — passed, baseline unchanged.
- `git diff --check` — passed.

UNAVAILABLE / NOT RUN:

- Attempted all four required commands: `dotnet restore QueenZone.sln`, `dotnet build QueenZone.sln --configuration Release --no-restore`, `dotnet format QueenZone.sln --verify-no-changes`, and `dotnet test QueenZone.sln --configuration Release --no-build`. Each failed immediately with `dotnet: command not found`. Razor/C# compilation, .NET tests and coverage are therefore **not verified**.
- Deterministic Playwright suite was written but not executed: no .NET host/runtime or PowerShell is available. A supplemental temporary Chrome harness could not run either: localhost bind failed with `EPERM`; an alternative Chrome debugging-pipe attempt failed with `ECONNRESET`. These are environment failures, not passing browser tests. Temporary harness files are outside the repository.
- Real legacy database checks: NOT RUN; no database access required by this loading-policy change.
- Real YouTube playback, sandbox state delivery, fullscreen and error 153 on DEV: NOT RUN. No desktop/mobile browser recording, performance profile or deployed proof was produced. The release history in the lock was accepted as supplied, not independently reverified.

## Verification handoff

Feature: `web.forum.youtube`.

Run default .NET verification and `pwsh -File ./scripts/Run-E2E.ps1 -Mode Deterministic` on a capable host. If attaching to an existing test host, ensure its `Site:PublicBaseUrl` equals the browser origin.

Capture command (NOT RUN here):

```powershell
pwsh -File .cursor/skills/verify-queenzone/scripts/control-queenzone.ps1 capture-proof -Feature web.forum.youtube
```

Platform: local Linux sandbox, Node-only checks completed. Browser platforms: not verified. Proof links: none.

Dinesh must attach the real DEV recording specified in the lock, showing actual bridge states through the sandbox, one-playing, fullscreen retention and no error 153. Linus reviews; Pat gates release. Do not treat stub or adapter tests as real-player/production proof.

## Open questions and lock interpretations

- Real YouTube handshake/state behavior through the unchanged sandbox still needs the required DEV proof. Deterministic messages cannot settle that question.
- Closest-card priority can replace a non-playing retained frame when the pool is full. Visible playing/fullscreen frames remain protected, and a frame outside retention holds its slot until its 250ms deadline. This reconciles nearest-card priority with retention hysteresis; the policy documents this interpretation explicitly.
- After pagehide or removal of any card-bearing post, all observers/listeners are torn down as locked. A history-cache restore retains readable links and requires a reload to re-enable players; there is no automatic resume or reinitialization.

## Deviations and environment blockers

- No existing web JS syntax-check step was present in this checkout. Added one to the existing Scripts tests workflow and wired the Node tests there instead.
- Added adapter tests beyond the required pure tests to cover message source/origin validation, stale callbacks, focus, offline/fallback and observer ordering without external dependencies.
- Required .NET/browser/manual verification remains incomplete for the reasons above; implementation is not declared release-ready.
- Branch creation (`git switch -c codex/1973-youtube-near-view`) failed because `.git` is read-only. `git fetch origin main` also failed: `cannot open '.git/FETCH_HEAD': Read-only file system`. Work remains in the provided `draft/1973-youtube-near-view` checkout. Commit, push, PR creation and merge-queue enrollment could not be completed in this sandbox. No attempt was made to bypass that restriction.
