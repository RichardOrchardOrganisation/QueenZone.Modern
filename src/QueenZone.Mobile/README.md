# QueenZone.Mobile

Expo development-build client for QueenZone. Android and iOS are equal
supported platforms. This project is not part of `QueenZone.sln`.

Decisions: [ADR 0009](../../docs/decisions/0009-react-native-for-mobile-app.md),
[ADR 0011](../../docs/decisions/0011-mobile-project-location-and-build-tooling.md),
[ADR 0012](../../docs/decisions/0012-react-navigation-app-shell.md).
API versioning / store-lag: [ADR 0019](../../docs/decisions/0019-api-versioning-convention.md).
Host toolchain: [mobile development environment](../../docs/mobile-development-environment.md).

## Pinned versions

| Tool | Version |
| --- | --- |
| Expo SDK | **57** (`expo@~57.0.22`) |
| React Native | **0.86.3** |
| Node.js | **24 LTS** (`>=24 <25`) |
| npm | Bundled with Node.js |
| JDK | **17** (Eclipse Temurin) |
| Android `compileSdk` / `targetSdk` | **36** (from Expo SDK 57) |
| Android SDK Build-Tools | **36.0.0** |
| Xcode | **26.4+** (SDK 57 requirement) |
| Application id | `org.queenzone.mobile` |

Expo Go is **not** a supported build or test environment. Use a development
build (`expo-dev-client`) compiled with `npx expo run:android` or
`npx expo run:ios`.

EAS is **not** required. Do not add an Expo account, EAS project, or `eas.json`
for this initial project.

## Prerequisites

Install the shared toolchain from
[`docs/mobile-development-environment.md`](../../docs/mobile-development-environment.md)
before the first native build.

On both platforms:

- Node.js 24 LTS
- Git
- JDK 17, with `JAVA_HOME` pointing at that JDK
- Android Studio with Android SDK Platform 36, Build-Tools 36.0.0, platform-tools, and an API 36 Google APIs emulator image

`ANDROID_HOME` (or `ANDROID_SDK_ROOT`) must point at the SDK:

- Windows: `%LOCALAPPDATA%\Android\Sdk`
- macOS: `$HOME/Library/Android/sdk`

iOS additionally requires macOS, Xcode 26.4 or newer, the iOS Simulator runtime,
and CocoaPods. `npx expo run:ios` installs pods during prebuild.

## Clean checkout

From the repository root:

```powershell
cd src/QueenZone.Mobile
node --version   # v24.x
npm ci
npm run preflight
```

`npm run preflight` runs typecheck, lint, the unit tests, and Expo Doctor
(`npm run doctor`). The `expo-doctor` binary is lockfile-pinned; the
SDK package-version check is not — it compares `package.json` against
Expo's current SDK 57 native-module list. A clean `npm ci` that passed
Doctor this morning can fail CI later the same day when Expo publishes a
patch. Fix with `npx expo install <package>` (or `npx expo install --fix`)
and commit the lockfile. Do not add packages to `expo.install.exclude`
just to silence Doctor. Typecheck + `npm test` alone is not a local
substitute for the CI `mobile-js` job. Publish workflows still run
`npm run preflight`. CI `mobile-js` runs the npm advisory gate, typecheck, lint,
`npm run test:coverage`, the coverage gate, and Doctor so the same suites
are measured without running tests twice. High/critical advisories fail closed unless the GHSA
is in [`npm-advisory-allowlist.json`](./npm-advisory-allowlist.json); see
[`npm-advisory-allowlist.md`](./npm-advisory-allowlist.md). Never run
`npm audit fix --force`.
`npm test` discovers every `src/**/*.test.ts` and `src/**/*.test.tsx` file:
Node's test runner executes pure `*.test.ts` files, and Jest + `jest-expo` +
React Native Testing Library execute component/hook `*.test.tsx` files. Do
not add tests to a path list in `package.json`. Device/emulator journeys
stay in Maestro (#872 / #883) and are not part of coverage totals.

### Unit test coverage

Both host-free suites publish line, branch, function, and statement
coverage. Jest `collectCoverageFrom` includes every production
`src/**/*.{ts,tsx}` file. The gate overlays Node hits onto that universe
and enforces the floors in `scripts/mobile-coverage-floors.json` — not
Jest `coverageThreshold`. Contracts (#869) and Maestro stay out.

```powershell
npm run test:coverage
node ../../scripts/Test-MobileCoverageGate.mjs
# or:
npm run coverage
node ../../scripts/Test-MobileCoverageGate.mjs --self-test
```

Do not commit `coverage/`. Policy, measured baseline, and the changed-line
gate: [`docs/architecture/testing-policy.md`](../../docs/architecture/testing-policy.md).

SDK 57 always uses React Native's New Architecture, so `app.json` does not
set `newArchEnabled` (the field is no longer in the config schema). Splash
is configured through the `expo-splash-screen` plugin, not a top-level
`splash` object.

Generate native projects from committed Expo config (Continuous Native
Generation). Do not commit the resulting `android/` or `ios/` directories.

### Android (Windows or macOS)

```powershell
npx expo run:android
```

This generates `android/`, compiles a development-client APK, installs it on a
running emulator or device, and starts the Metro bundler. Rebuild the native
app only after changing native dependencies, `app.json`, or the Expo SDK:

```powershell
npx expo prebuild --clean
npx expo run:android
```

JavaScript-only changes use:

```powershell
npx expo start --dev-client
```

### iOS (macOS only)

```bash
npx expo run:ios
```

This generates `ios/`, installs CocoaPods, compiles a Simulator development
client, and starts Metro. The same `--clean` prebuild rule as Android applies
when native configuration changes.

## Theme

Design tokens live in `src/theme/` (#792). Palette hex values match
`wwwroot/design-system/tokens/colors.css`; type, space, radius, and motion
follow the same CSS foundation plus the mobile handoff at
`design/Queenzone mobile app design/handoff/` (`STYLE_GUIDE.md`, `theme.ts`).

The app is **dark-first** (`#111111` page, Antique Gold `#B89A4A` as
`accentPrimary`). Import via `useTheme()` — do not hard-code colours already
named in the theme.

**Fonts:** Cormorant Garamond, Inter, and Cinzel load at startup through
`useQueenzoneFonts()` (`@expo-google-fonts/*` TTFs, same families as the web
WOFF2s). Family names live in `theme.fonts`. After adding `expo-font` /
`expo-splash-screen`, rebuild the development client:

```powershell
npx expo prebuild --clean
npx expo run:android
```

## API base URL (environments)

Per-environment API origins live in `src/config/` (#793). `app.config.ts`
writes `extra.appEnv` and `extra.apiBaseUrl`; runtime code reads them via
`getAppConfig()` / `apiV1Url()`.

| `EXPO_PUBLIC_APP_ENV` | Default API origin |
| --- | --- |
| `development` (default) | `http://localhost:5146` (local `QueenZone.Web`) |
| `staging` | `https://dev.queenzone.org` |
| `production` | `https://www.queenzone.org` |

Override the origin for any environment without code changes:

```powershell
# Point a local build at the HTTPS launch profile
$env:EXPO_PUBLIC_API_BASE_URL = "https://localhost:7162"
npx expo start --dev-client
```

```powershell
# Staging defaults
$env:EXPO_PUBLIC_APP_ENV = "staging"
npx expo start --dev-client
```

Copy `.env.example` to `.env` (git-ignored) for a sticky local override. Restart
Metro after changing env vars.

Android emulators rewrite `localhost` / `127.0.0.1` to `10.0.2.2` automatically.
Physical devices need your machine's LAN IP in `EXPO_PUBLIC_API_BASE_URL`.
The Profile screen (Home masthead avatar) shows the active `appEnv` and resolved origin for a quick check.

Call sites should use `apiV1Url('/content/news')` (or `getAppConfig().apiBaseUrl`)
rather than hard-coding hosts. Path versioning and how long v1 stays live:
[ADR 0019](../../docs/decisions/0019-api-versioning-convention.md).

## Crash and error monitoring

[Sentry](https://sentry.io) (`@sentry/react-native`) reports JS exceptions,
native crashes, and performance traces. Configured in the `self-0tb` org,
project `queenzone-mobile` (#855). It stays a no-op — `initSentry()` in
`src/config/sentry.ts` returns immediately — until a DSN is available, so
builds and local dev work fine without it (e.g. a fresh clone before copying
`.env.example`). Publish workflows bake `EXPO_PUBLIC_SENTRY_DSN` into Expo
`extra.sentryDsn` at prebuild (same pattern as the API origin) **and** pass
the env var through Gradle / `xcodebuild` so Metro can inline it. Runtime
prefers `extra` so a missing Metro env cannot disable Sentry in a signed
build. Once a DSN is set, `tracesSampleRate` and the
`reactNavigationIntegration` (registered against the root `NavigationContainer`
ref in `App.tsx`) turn on route-change performance traces — a DSN alone only
enables error/crash reporting, not the Performance/Traces views.

Look at **self-0tb / queenzone-mobile** on sentry.io. The website has no Sentry
SDK; only this mobile client reports.

| Variable | Where | Purpose |
| --- | --- | --- |
| `EXPO_PUBLIC_SENTRY_DSN` | `.env` locally; `vars.SENTRY_DSN` repo variable in CI | Enables reporting. DSNs are not secret. Baked into `extra.sentryDsn` at prebuild. |
| `EXPO_PUBLIC_SENTRY_TRACES_SAMPLE_RATE` | `.env` locally; optional | Fraction (0.0-1.0) of sessions traced for performance. Defaults to `1.0`. |
| `SENTRY_ORG` / `SENTRY_PROJECT` | CI repo `vars` | `self-0tb` / `queenzone-mobile` — target org/project for source map and dSYM upload at build time. |
| `SENTRY_AUTH_TOKEN` | Bitwarden `Queenzone Development` project (Android) / `secrets.SENTRY_AUTH_TOKEN` repo secret (iOS) | Lets `sentry-cli` upload symbols during the native build. Never committed — read directly from the build environment, not from Expo config. |
| `SENTRY_DISABLE_AUTO_UPLOAD` | CI unsigned mobile jobs (`ci.yml`); optional locally | Skips source-map / dSYM upload when org/token are unset. Required for simulator/debug CI builds once the Sentry Expo plugin is registered — otherwise Xcode fails with "organization ID or slug is required". |

Unsigned CI Android/iOS jobs set `SENTRY_DISABLE_AUTO_UPLOAD=true`. Publish workflows
(`publish-android-google-play.yml`, `publish-ios-testflight.yml`) set org/project/token
and upload symbols. For a local native build without Sentry credentials, export the
same disable flag (or set org/project/token as in `.env.example`).

To rotate the auth token or point at a different Sentry org/project: create an
org token (Sentry → Settings → Auth Tokens → Create New Organization Token;
the default scopes — Source Map Upload, Release Creation, Code Mappings — are
sufficient), then update the `SENTRY_AUTH_TOKEN` secret in Bitwarden and
GitHub, and the `SENTRY_DSN`/`SENTRY_ORG`/`SENTRY_PROJECT` repo variables if
the org/project changed.

## Anonymous product analytics

[TelemetryDeck](https://telemetrydeck.com) records two low-volume events:
one `app.active` event per installation per UTC day and `section.viewed` when
the user changes among Home, News, Photography, Archive, and Forum. This gives
anonymous active-installation counts and section popularity. TelemetryDeck can
also report country-level origin and the app sends the device's two-letter
region preference; neither requires location permission or precise location.

The analytics installation ID is random, stored separately in AsyncStorage,
and never derived from a QueenZone account, push token, email, or member ID.
Events contain no route parameters, URLs, content IDs, search text, messages,
or other user content. Delivery is best-effort and never blocks the app.

The app asks before creating the analytics identity or sending its first
event. A refusal is remembered. Signed-in and signed-out users can later grant
or withdraw consent under **Profile → Analytics preferences**. Withdrawal
stops future signals and deletes the analytics-only local identifier; earlier
anonymous events cannot be matched back to that installation for deletion.

Set the public `EXPO_PUBLIC_TELEMETRYDECK_APP_ID` at build time to enable the
client. `app.config.ts` bakes it into `extra.telemetryDeckAppId`. It remains
disabled when unset; non-production builds mark configured signals as test
data. Store publish environments use the `TELEMETRYDECK_APP_ID` GitHub Actions
variable. Do not add a payment method or paid subscription to the TelemetryDeck
account without a separate product decision.

## Navigation shell

React Navigation provides the app shell ([ADR 0012](../../docs/decisions/0012-react-navigation-app-shell.md)).
The v2 design handoff lives at
[`design/Queenzone mobile app design/handoff/`](../../design/Queenzone%20mobile%20app%20design/handoff/).

Five tabs, signed-in and signed-out: **Home · News · Photography · Archive · Forum**.
Profile, settings, and private messages sit behind the Home masthead avatar —
never a sixth tab. New archive sections become rows on the Archive hub.

Home, Archive hub, Photography, Forum, Search, and Profile follow the approved
screen contracts in `QUEENZONE_APP_SPEC.md`. News and photography browse live
content from `/api/v1` (photo image URIs are `cdn.queenzone.org`, never App Service).
Signed-in members can submit a photo from the camera or photo library
(`Photography` → Submit a photo). That posts multipart to
`POST /api/v1/member/photo-submissions` — the same `PhotoSubmissionService` /
`ugc-photos` review queue as website `/submit/photo`. Camera and library access
use `expo-image-picker` (already required for member avatars).

Members can also share a public news URL into the app, or open **Suggest news**
from the News header or Profile. That write lives on the Home stack
(`HomeTab` → `SuggestNews`), not the News archive tab. The OS share sheet
targets this app for `text/*` and URL shares only
(`expo-share-intent@8.0.1` CNG plugin, plus `expo-linking`). The bridge copies
the payload into `queenzone.newsShare.v1` before OAuth can background the
process — `resetOnBackground: true` wipes the native share, so a later read
would be empty. https links only; http is never upgraded and never stored.
The client does not fetch the page or its HTML. Signed-out members still see
the review form; Sign in uses `returnTo: { tab: 'HomeTab', screen: 'SuggestNews' }`.
A 201 clears the slot and opens **My submissions** on the same stack.

Signed iOS builds need an App Group `group.org.queenzone.mobile`, assigned to
both the main `org.queenzone.mobile` App ID and the separate
`org.queenzone.mobile.ShareExtension` App ID. There is no EAS project; each
target needs its own regenerated App Store profile. Do not
add a second URL scheme — shares reuse `queenzone`. Do not add
`NavigationContainer.linking` for this flow.
Fan performances list from `/api/v1/content/fan-performances`; streaming uses
`GET /api/v1/content/fan-performances/{id}/audio` with the member Bearer token
(same private `songfiles` blob as the website). Play All / Shuffle Play All on the
list screen walk that same paged GET (`pageSize` 100) for the full catalog, then
pass the ordered or once-shuffled queue to `FanPerformancePlayer.play` — not the
`FlatList` pages already on screen.

### Offline fan-performance downloads

Signed-in members can download a recording from the listing or the detail/player.
Downloads use the same authenticated audio GET (Bearer). The app never stores or
plays a blob or CDN URL. Completed files live under app-private
`Paths.document/fan-performances` with opaque ID filenames. A sibling `.part`
file is promoted only after a successful download; cancel/fail deletes the
partial. One download runs at a time. Repeated taps do not start a second copy.

A versioned, account-scoped manifest (beside `ContentCache`, not inside it)
stores only completed rows: performance id, local URI, title/performer, byte
size, `sourceRevision` (the audio response **ETag** when the size probe got
headers), completed time, and owning `memberId`. Startup reconcile drops missing
or zero-length files and scrubs orphan `.part` files. A `Range: bytes=0-0` probe
is best-effort only (12s timeout, body cancelled immediately) so a proxy that
ignores Range cannot buffer a whole MP3 in JS or fail longer tracks. Missing
ETag is allowed. Partial, empty, or HTTP-error-page files are discarded and
playback falls back to the authenticated stream so a failed download cannot
wedge the shared player. There is no silent eviction of a completed download —
members remove it explicitly. Size and in-situ percent are shown when known.
The manager checks `Paths.availableDiskSpace` with an 8 MB margin before writing.

`FanPerformancePlayer.load` resolves every source through `resolveAudioSource`
(listing, detail, Play All / Shuffle, next/previous). A valid same-member local
file wins; otherwise the player streams with Bearer. Local playback needs the
retained member identity, not a network token refresh. An undownloaded recording
while offline shows an offline-specific message, not a generic failure.

Download UI state (`queued` / `downloading` / `downloaded` / `failed` /
`removing`) lives on `externalStore` under `DOWNLOAD_UI_CACHE_KEY_PREFIX`. The
listing, detail/player, Downloads screen, and Play All all subscribe there.

**Sign-out** stops playback, deletes every completed file and `.part`, then
clears the manifest and invalidates the download UI prefix. Offline or timeout
refresh failures keep the local member identity so same-account downloads stay
playable; a definite `401` / `invalid_grant` still signs the member out and
deletes downloads.

**Cloud backup:** Expo’s high-level FileSystem API does not expose
`NSURLIsExcludedFromBackupKey` (iOS) or Android backup-exclusion attributes.
Until a CNG/native config sets those flags, downloaded audio in `Paths.document`
may be included in device cloud backup. Do not treat that as a privacy
guarantee.

Non-goals: export/share, download-all, cross-device sync, public `songfiles`,
web offline, or putting audio bytes in `ContentCache`.

### Fan-performance background playback

Background playback and lock-screen controls use `expo-audio`
(`shouldPlayInBackground`, `setActiveForLockScreen`). Keep that engine. Do not
switch to `react-native-track-player` for lock-screen next/previous buttons.

Supported on physical Android and iOS (device matrix, 30 Aug 2026). Lock-screen
chrome is not visible in the iOS Simulator.

- The current recording continues when another app is opened or the device is locked.
- The queue auto-advances to the next recording while locked, and stops at the last item (no loop).
- The system now-playing / lock-screen player can play, pause, and seek (skip ahead / behind).
- Seek-only is a library exception: `expo-audio`'s single player exposes seek remote commands, not next/previous-track buttons or JS remote-command events. In-app queue skip stays on the detail screen. Seek is enough; missing next/prev remote commands are not a defect.
- Unplugging headphones or losing Bluetooth does not continue playback on the speaker.
- Signing out stops audio and clears lock-screen / notification controls. Finishing the last queued recording does the same.
- Now-playing artwork is the bundled Q app icon (`assets/icon.png`) via expo-audio `artworkUrl` (a local `file://` URI from `expo-asset`). Not per-track art. Not a network or blob URL. Metadata is title, performer, album title "Fan performances", and that artwork. Tokens and audio URLs stay off the lock screen.

There is no `fanPerformanceBackgroundPlayback` feature flag. Native CNG
background configuration (`UIBackgroundModes: audio`, Android media foreground
service) stays generated by the `expo-audio` plugin. Rebuild the development
client after plugin changes.

iOS `expo-audio` ~57.0.4 `currentStatus()` is patched at prebuild
(`plugins/withExpoAudioIosCurrentOffsetFromLive.cjs`) so `currentOffsetFromLive`
is `nil` and never calls `AVPlayerItem.currentDate()` on the 250ms main-queue
observer (Sentry QUEENZONE-MOBILE-7). Play/pause/seek stay on `currentTime`.
Android is unchanged. Rebuild the iOS client after this plugin changes.

Home → **Member sign in**, Profile → **Sign in**, and signed-out **Sign in to
reply / submit / New thread** all open a root Sign-in modal (Google, Microsoft,
Discord, GitHub, Apple). After OAuth succeeds the app returns to the screen
that asked for login, or to Profile from the Home row. Member-only routes also
render a `MemberGate` so they stay closed while signed out.

### Stored sessions are scoped to the API origin

`src/session/tokenStore.ts` keys the SecureStore grant by the build's
`apiBaseUrl` as one JSON value (`queenzone.mobile.<origin>.grant`). A refresh
token is only valid at the host that issued it, and TestFlight ships staging and
production builds under the same `org.queenzone.mobile` bundle id — before
scoping, updating from a `staging` build to a `production` one presented a
`dev.queenzone.org` grant to `www.queenzone.org`, which answers `invalid_grant`
and silently signed the member out on the next launch. Scoping keeps one session
per origin, so switching builds restores whichever session that build owns. One
key also means a killed process cannot tear the grant into a partial state.

Do not reintroduce an unscoped key, and do not add the app version to the scope —
a store or TestFlight update must not sign members out. A grant written before
this shape — unscoped per-field keys, origin-scoped per-field keys, or an
unscoped `queenzone.mobile.grant` — is adopted into the running build's scoped
grant once, then deleted.

Only a token-endpoint response that actually says the grant is dead
(`invalid_grant` / `invalid_token`, or a 401) ends a session. A 429 from the
per-account limiter, a `temporarily_unavailable`, and any 5xx are transient:
`src/session/refreshFailure.ts` keeps the session so the next launch retries.
Route every refresh through `refreshWithStoredGrant` — the server treats a reused
refresh token as theft and revokes every grant the member holds, so two
concurrent refreshes of the same grant sign them out on every device.

### Testing member sign-in

Automated tests mock `expo-web-browser` and do not need real OAuth credentials.
A live emulator check does: the hop is a system browser against the configured
API host (`EXPO_PUBLIC_APP_ENV` / `EXPO_PUBLIC_API_BASE_URL`), so Google /
Microsoft (or another listed provider) must already be enabled on that host.

1. Start the development client (`npx expo run:android` or `run:ios`).
2. From Home, tap **Member sign in** — the provider list should appear without
   swiping anything away.
3. Pick a provider, complete OAuth in the system browser, and confirm the app
   dismisses Sign in and lands on Profile (or back on compose / photo submit if
   that is what started the hop).
4. If the custom tab stays in front of the app after the redirect, swipe it
   away once; the app should still finish the token exchange from the deep link.

Do not commit credentials. A one-off local login as a real member is enough.

Rebuild the development client after this native dependency set changes
(`react-native-screens`, `react-native-safe-area-context`, `react-native-svg`,
`expo-image`, `expo-image-picker`, `expo-linear-gradient`, `expo-audio`):

```powershell
npx expo prebuild --clean
npx expo run:android
```

## CI build pipeline

`mobile-js`, `mobile-android`, and `mobile-ios` in
[`.github/workflows/ci.yml`](../../.github/workflows/ci.yml) run whenever
`src/QueenZone.Mobile/` changes, or on demand via `workflow_dispatch`.
`mobile-api-contracts` is a **separate** classifier output
(`mobile_api_contracts=true`) so `/api/v1` or mobile client-parser changes
run the Testing-host consumer suite **without** forcing Android/iOS native
builds. Path classification lives in `scripts/classify-pipeline-changes.sh`
(mobile-only PRs skip the .NET suite; API-only PRs skip native compiles;
mixed PRs run both; docs-only PRs run neither unless they are json-api
contract docs).

The unsigned `mobile-ios` job prefers an online, idle self-hosted Apple Silicon
runner carrying the dedicated `ios-build` label. If that runner is offline,
busy, or cannot be queried, the same job immediately targets GitHub-hosted
`macos-26`. TestFlight remains separate and Mac-only on `ios-signing`; neither
job uses the cross-platform `e2e` label.

Local preflight after a clean install:

```powershell
cd src/QueenZone.Mobile
npm ci
npm run preflight
```

Consumer contracts (repo root; no secrets or real database). Run twice from
a clean checkout to prove determinism:

```powershell
bash ./scripts/run-mobile-api-contracts.sh
```

That starts `QueenZone.Web` in `Testing` (`QUEENZONE_MOBILE_CONTRACT_HOST=1`)
and runs `npm run test:api-contracts`. Failures name the endpoint and the
expected field or status. A renamed server JSON field or a tightened zod
assert must fail that way (revert the probe; do not commit it). This is not
part of `npm test` / `npm run preflight` — those stay host-free unit and
component tests.

```text
unit / component (npm test, #833)
  ≠ npm test coverage (#871)
  ≠ consumer contracts (#869)
  ≠ native compile
  ≠ device smoke (Maestro, #872 / #883)
```

`npm test` discovers every `src/**/*.test.ts` and `src/**/*.test.tsx` file
and runs both the Node pure suite and the Jest component suite. Do not add
new tests to a path list in `package.json`. `npm run preflight` is typecheck
+ lint + those tests + `npm run doctor` (see Clean checkout: Doctor's version
check can move when Expo publishes a patch). CI `mobile-js` runs the
#837 npm advisory gate, then collects coverage from both suites and
enforces `scripts/Test-MobileCoverageGate.mjs`.

PR check **names** (job `name:` values; these are the strings to require on
`main`):

| Check name | What it is | What it is not |
| --- | --- | --- |
| `Mobile typecheck and unit tests` | `npm ci` + advisory gate + typecheck + `npm run test:coverage` + coverage gate + Doctor | Native compile, contracts, or device E2E |
| `Mobile Android build` | Unsigned debug APK compile | Play-store signing or device E2E |
| `Mobile iOS build` | Unsigned Simulator compile | TestFlight signing or device E2E |
| `Mobile API consumer contracts` | Testing host + real mobile parsers | Native compile, OpenAPI-only, or device E2E |

Android and iOS are equal: a mobile PR cannot skip either native compile.
Non-mobile PRs get skip-success stubs with those same names so required
checks are not left pending (same idea as `test-docs-ok`). Server-only API
PRs get the Android/iOS stubs and **do** run `Mobile API consumer contracts`.

**Branch protection is repository settings, not YAML.** A human must add
those four names as required status checks on `main` after merge. Live
required contexts on 2026-08-24 did **not** yet include them; see
`docs/architecture/testing-policy.md`.

The unsigned jobs are PR compile checks only. Publishing runs the same
`npm run preflight` against `github.sha` **before** signing or upload:
[`publish-android-google-play.yml`](../../.github/workflows/publish-android-google-play.yml)
and [`publish-ios-testflight.yml`](../../.github/workflows/publish-ios-testflight.yml).
A failed or cancelled preflight skips publication.

`mobile-android` builds an unsigned debug APK on a GitHub-hosted Linux
runner; `mobile-ios` builds an unsigned Simulator `.app` (zipped) on a
GitHub-hosted macOS runner — no Apple account or signing credentials are
used. Both jobs upload their build as a workflow artifact
(`mobile-android-<run-id>` / `mobile-ios-<run-id>`), downloadable from the
run's summary page for one day. Those compile artifacts are **not** the
device-smoke binaries: `EXPO_PUBLIC_API_BASE_URL` is bake-time, so smoke
rebuilds a Release-embedded binary with a loopback Testing origin.

## Device smoke (Maestro)

Device smoke boots a **Release-embedded** APK / Simulator `.app` against the
same Testing contract host as the consumer-contract suite
(`ASPNETCORE_ENVIRONMENT=Testing`, `QUEENZONE_MOBILE_CONTRACT_HOST=1`).
It is **not** a substitute for `npm test` (#833), consumer contracts
(#869), or the unsigned compile jobs. It does not use EAS, Expo Go, the
live site, Azure SQL, real OAuth, Metro in CI, or member passwords.
Local `expo start` + Debug remains for developers; device smoke/journeys
never install `app-debug.apk` or `Debug-iphonesimulator`.
The Release smoke embed starts signed out and keeps its seeded smoke token in
memory; it never restores from or writes to the device keychain. Debug and all
staging/production builds retain the normal SecureStore session lifecycle.

| Smoke is | Smoke is not |
| --- | --- |
| Launch past splash, five tabs, Home → news detail, News → story, Photography → category + viewer, Archive search → result, Forum → board + thread, Profile signed-out + member-only gate, one Testing-token authenticated inbox | Component state permutations, write/upload/audio/permission journeys, production credentials, a required PR check (Phase 1) |

Shared flows live in [`maestro/`](maestro/). Android and iOS use the same YAML; overlays are only for real platform differences.

Local (repo root). Install [Maestro](https://maestro.mobile.dev) first
(`curl -Ls "https://get.maestro.mobile.dev" | bash`). Unset any
`ConnectionStrings__*` env vars. The script starts the Testing host on
port 5098, bakes a Release-embedded binary
(`assembleRelease` / `-configuration Release`, `QUEENZONE_MOBILE_SMOKE_EMBED=1`
to strip expo-dev-client), and runs `maestro/smoke.yaml`. Release embed is
what puts the JS bundle in the binary — Debug + env flags still opened the
dev-client launcher (#1322). `EXPO_PUBLIC_API_BASE_URL` still points at the
Testing host (`10.0.2.2:5098` / `127.0.0.1:5098`), not prod.
`SENTRY_DISABLE_AUTO_UPLOAD=true` stays. `queenzone://smoke-auth` is allowed
on that bake because `appEnv` is development and `smokeEmbed` is set.

```bash
# Android: start an API 36 emulator first
./scripts/run-mobile-device-smoke.sh --platform android

# iOS Simulator (macOS only)
./scripts/run-mobile-device-smoke.sh --platform ios

# Prove a failed assertion uploads diagnostics
./scripts/run-mobile-device-smoke.sh --platform android --prove-failure

# On-demand journeys (#1071); smoke.yaml stays 9 flows
./scripts/run-mobile-device-smoke.sh --platform android --suite journeys

# Manual P0 release acceptance: member messaging, forum writes, news submission
./scripts/run-mobile-device-smoke.sh --platform android --suite release
```

Authenticated smoke injects the contract-host access token through
`queenzone://smoke-auth`. That deep link is handled when `appEnv` is
development and either `__DEV__` is true (local Debug) or `smokeEmbed`
was baked (CI/local Release smoke). It is not compiled into
staging/production store Release behavior.

CI: [`.github/workflows/mobile-device-smoke.yml`](../../.github/workflows/mobile-device-smoke.yml)
runs on **Actions → Mobile device smoke** (`workflow_dispatch`) and
weekdays at 04:00 UTC. Jobs are `Mobile Android device smoke` (self-hosted
Mac, isolated AVD/port) and `Mobile iOS device smoke` (hosted `macos-26`).
Phase 1 is soak only — do **not** add those
names as required checks on `main`. Device smoke is **not** started for
API-only PRs (that stays `mobile-api-contracts`). After pass rate and
duration are known, promote the short set by adding the jobs to `ci.yml`
when `mobile=true` (with skip-success stubs) and then enabling branch
protection; record the date on #872. See
[`docs/architecture/testing-policy.md`](../../docs/architecture/testing-policy.md).
Failures upload `maestro-results/` (screenshots, JUnit, host/app logs).
Maestro app assertions are not retried. The harness retries one Android
device-server or emulator transport loss (`DeviceServerDied`, hierarchy
timeout, or a vanished emulator serial): it recovers ADB when the
emulator is still alive, otherwise the self-hosted Android smoke job
boots a fresh isolated AVD (`QueenZone_CI_Smoke_API_36_retry` on port
5558, not the P0 `QueenZone_CI_API_36` / 5556 path) and reruns once.
Failure logs name transport death versus a selector miss. An iOS driver startup may likewise retry once only when no
JUnit file exists; the harness reboots the Simulator and preserves the
first driver log. The authenticated smoke flow repeats only the
iOS smoke-auth deep link when the profile explicitly remains signed out after
the system Open prompt; it does not repeat failed app assertions.

On-demand journeys (`maestro/journeys.yaml`, #1071–#1074) are a
**separate job pair** in the same workflow: `Mobile Android device
journeys` / `Mobile iOS device journeys`. Dispatch with `suite=journeys`
(or `both`), or wait for the weekday 16:00 UTC slot. Do not add those
names to branch protection until soak. `smoke.yaml` stays exactly the
9 #872 flows.

The manual P0 release suite (`maestro/release.yaml`, #1411) is a third,
independent layer. Dispatch `suite=release` against a fresh Testing host. It
runs on Android and iOS (`platform=both`, the default), or one selected platform
for focused diagnostics. Android P0 acceptance uses the repository's ARM64
self-hosted Mac and a dedicated hardware-rendered emulator on port 5556; repeated
GitHub-hosted Linux emulator-process exits made that environment unsuitable for
this longer release suite. Its three isolated flows authenticate with the
Testing-only token and verify private-message compose/reply/archive/unarchive,
forum topic creation and reply, and a news suggestion appearing under My
submissions. It does not exercise real OAuth, SecureStore restoration, signed
store artifacts, or production data; those are separate release checks (#1412).

The unsigned jobs are PR compile checks only. Separate manual workflows
publish installable builds: [`publish-ios-testflight.yml`](../../.github/workflows/publish-ios-testflight.yml)
archives and signs the iOS app on the dedicated self-hosted Mac runner and
uploads it to TestFlight;
[`publish-android-google-play.yml`](../../.github/workflows/publish-android-google-play.yml)
uploads a signed Android App Bundle to the Play internal track. CI
`mobile-android` still compiles an unsigned debug APK and uploads it as a
one-day workflow artifact; that is a compile gate, not tester
distribution.

## Install the latest iOS TestFlight build

The one-time Apple setup for `org.queenzone.mobile` consists of:

- Apple Developer team `X28Z75P69M`;
- an Apple Distribution certificate;
- Push Notifications and Sign in with Apple enabled on the
  `org.queenzone.mobile` App ID;
- the `QueenZone App Store App Groups` App Store provisioning profile
  (regenerated after those capabilities are enabled, so it includes
  `aps-environment=production`, `com.apple.developer.applesignin=Default`,
  and `group.org.queenzone.mobile`);
- the QueenZone App Store Connect record (Apple ID `6803889011`); and
- a Developer-role App Store Connect API key dedicated to GitHub uploads.

Run **Publish iOS to TestFlight** from the repository's **Actions** tab and
select `main`. Leave **API environment** set to `production` for normal
TestFlight and App Store candidates; choose `staging` only for a build that is
deliberately testing `https://dev.queenzone.org`. The workflow intentionally
rejects other branches and targets the self-hosted Mac runner through
`[self-hosted, macOS, ARM64, ios-signing]`.
The runner service does not load an interactive shell profile, so the workflow
puts Homebrew (`/opt/homebrew/bin` or `/usr/local/bin`) on `PATH`, installs
CocoaPods if `pod` is missing, runs `expo prebuild --no-install` with
`IOS_BUILD_NUMBER` set to the workflow run number (so `CFBundleVersion` is unique
for App Store Connect), records the UTC build time and source revision for the
in-app build stamp, then runs `pod install` before archiving. Expo writes
`ITSAppUsesNonExemptEncryption=false` because the app uses only exempt platform
HTTPS; this prevents each TestFlight build pausing for the same export-compliance
questionnaire. Expo SDK 57 stamps `aps-environment=development` during prebuild;
Xcode changes it to `production` when archiving with the App Store distribution
profile. The workflow verifies both stages and rejects an exported IPA that does
not carry the production entitlement. It also reads the packaged Expo config
back from the exported IPA and rejects an environment or API-origin mismatch
before upload. Expo's own CocoaPods auto-install is skipped
because a missing CLI is only a warning and otherwise continues without an
`.xcworkspace`. It then imports signing material into a temporary Keychain,
produces and verifies a signed `.ipa`, retains that IPA as a seven-day
workflow artifact, uploads it to App Store Connect, and deletes the temporary
Keychain and provisioning profile even when a step fails.

The exported IPA verification checks the build number, exempt-encryption
declaration, timestamp, and source revision before upload. The app shows the
version, native build number, localised build date/time, and short revision at
the bottom of the **Profile** screen (Home masthead avatar), using the same
subdued build-stamp treatment as the website.

Install Apple's TestFlight app on the iPhone and accept the QueenZone internal
tester invitation. After Apple finishes processing an uploaded build, install
or update QueenZone from TestFlight; the phone does not need to connect to this
Mac. Each TestFlight build remains testable for 90 days.

The workflow uses these encrypted GitHub Actions secrets (names only; never
commit their values):

| Secret | Purpose |
| --- | --- |
| `IOS_DISTRIBUTION_CERTIFICATE_BASE64` | Base64-encoded password-protected Apple Distribution `.p12` |
| `IOS_DISTRIBUTION_CERTIFICATE_PASSWORD` | Password for the distribution `.p12` |
| `IOS_PROVISIONING_PROFILE_BASE64` | Base64-encoded `QueenZone App Store App Groups` `.mobileprovision` |
| `IOS_SHARE_EXTENSION_PROVISIONING_PROFILE_BASE64` | Base64-encoded `QueenZone Share Extension App Store` `.mobileprovision` |
| `IOS_WIDGET_EXTENSION_PROVISIONING_PROFILE_BASE64` | Base64-encoded `QueenZone Widget Extension App Store` `.mobileprovision` |
| `APP_STORE_CONNECT_KEY_ID` | App Store Connect API key identifier |
| `APP_STORE_CONNECT_ISSUER_ID` | App Store Connect API issuer identifier |
| `APP_STORE_CONNECT_PRIVATE_KEY` | One-time-downloaded App Store Connect `.p8` private key |

Rotate the distribution certificate/profiles before their shared expiry and
replace the corresponding secrets together. After enabling a new App ID
capability, regenerate the affected profile in Apple Developer **Profiles**
and replace its base64 GitHub secret (value length only in logs and chat).
All three profiles must include `group.org.queenzone.mobile`; only the main app
profile includes Push Notifications / `aps-environment` and Sign in with Apple.
The extension profiles
sign `org.queenzone.mobile.ShareExtension` and
`org.queenzone.mobile.ExpoWidgetsTarget`, respectively. A stale profile fails
before archive with a target-specific entitlement error. Revoke and
replace the API key if its private key is ever exposed. Signing material must
never be copied into the repository, workflow artifacts, logs, or issue/PR
text.

## Install the latest Google Play internal-test build

Google Play's equivalent of TestFlight is the **internal testing track**. Run
**Publish Android to Google Play** from the repository's **Actions** tab and
select `main`. Leave **API environment** set to `production` for normal Play
candidates; choose `staging` only for a deliberate `https://dev.queenzone.org`
test build. The workflow runs mobile preflight, builds a signed Android App
Bundle (`.aab`), verifies its packaged environment and API origin, retains it
as a seven-day artifact, and uploads it to the `internal` track for opted-in
testers.

The one-time Play Console setup for `org.queenzone.mobile` is:

- enrol the app in Play App Signing;
- add internal testers and copy the opt-in link;
- enable the Google Play Android Developer API in the existing
  `queenzone-mobile` Google Cloud project;
- use the dedicated service account
  `queenzone-play-publisher@queenzone-mobile.iam.gserviceaccount.com`, which has
  app-scoped permission to release only to testing tracks; and
- store its JSON credential in Bitwarden as
  `GOOGLE_PLAY_SERVICE_ACCOUNT_JSON`, then add that secret's ID to the existing
  `BITWARDEN_MOBILE_BUILD_SECRETS` deploy-environment mapping.

Google Play requires the first bundle for a new app to be uploaded in Play
Console before API publishing works. For that bootstrap only, run the workflow
with **Upload the bundle to the Google Play internal track** cleared, download
its `.aab` artifact, and upload it under **Internal testing → Create new
release**. After that first release establishes the package and upload key, keep
the option selected for normal automated internal releases.

The workflow reuses the stable Android test key as the Play **upload key**.
Google Play holds the separate app-signing key and signs the APKs delivered to
testers. Keep the upload keystore and passwords in Bitwarden; do not add the
service-account JSON or any private signing material to GitHub secrets or the
repository.

Play App Signing gives the store build a different signing certificate from a
locally signed, leftover sideload, or CI debug APK. Uninstall the other
install before switching to Play.

Android push does not use APNs. The app already uses Firebase Cloud Messaging
(FCM) directly, with the Firebase client configuration in
`google-services.json`. The backend sender credential remains
`PushNotifications__Fcm__ServiceAccountJson` plus
`PushNotifications__Fcm__ProjectId`; it is separate from the Play publishing
service account. APNs remains iOS-only.

Sideload APK publish to the old Blob + Static Web App download site is retired
(#1306). Testers install Android from Play internal testing, iOS from
TestFlight, or grab the unsigned CI `mobile-android` debug artifact from a
workflow run when they only need a compile check. iOS remains an explicit
manual TestFlight release so signing and upload never run merely because a
pull request was opened or merged.
