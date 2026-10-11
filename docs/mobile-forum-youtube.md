# Native forum YouTube players (#1958)

This change is local and not release-ready until the native matrix below is complete. Dependency #1956 is merged (#1962). Current main already has recognition, additive v1 descriptors and website player scheduling. Mobile uses the same optional descriptor contract and existing hooks/cache. The website implementation is not native evidence.

## Projection and privacy

`ForumVideoBody` parses the sanitized response body with htmlparser2 10.1.0, the parser family used by the native HTML renderer. It counts every anchor, including image/inline/quote anchors, in original DOM order; validates provider, anchored ASCII 11-character ID, integer starts 0–86,400, canonical watch URL and safe anchor index; checks the mapped standalone body link's identity; and replaces only that eligible anchor with a native block card. Original link markup remains in the card. Prose/images/other links stay in native rich HTML. Repeated ID/start pairs and descriptors beyond the server's three-per-post cap remain links. Missing/empty/invalid metadata renders existing readable body links. Quotes, signatures, other rich bodies and queued composer posts do not receive descriptors. No arbitrary embed URL is accepted and stored content is never changed.

Near-screen players automatically contact YouTube before Play. Each card discloses IP address and app identification sharing. No remote thumbnails, oEmbed, preconnect or scraping run for placeholders. No anonymous/no-tracking guarantee is made; this uses the official `www.youtube.com` player, not website privacy-enhanced-mode promises. Existing consent controls cover TelemetryDeck analytics only (`AnalyticsConsentPrompt` explicitly names that provider); they remain unchanged. There is currently no consent restriction for third-party embeds. Any future embed consent control must be an additional gate on the pool's enabled state and immediately release players on withdrawal.

## Native isolation and identification

Expo SDK 57's `bundledNativeModules.json` selects maintained `react-native-webview` **13.16.1** (Android OS WebView / iOS WKWebView). `source` contains only a fixed app-generated local HTML document and `baseUrl: https://org.queenzone.mobile/`. The ID matches Android package and iOS bundle ID in `app.json` and ADR 0011. It contains only the validated ID/start and a local generation number: no post text, member identity, API origin, account cookies, access/refresh token, Authorization header or arbitrary script.

YouTube's [required identification](https://developers.google.com/youtube/terms/required-minimum-functionality) documents local HTML loaded with Android loadDataWithBaseURL / iOS loadHTMLString:baseURL: as a Referer mechanism, using the HTTPS registered app ID. The `origin` player variable complements this; it does not substitute for Referer. This approach does not fetch a DNS wrapper or require a hosted first-party site. Both outgoing native Referers still require actual device network evidence.

The wrapper's CSP allows scripts only from YouTube/ytimg plus fixed inline app code, frames only from YouTube, no form action or base changes, and uses strict-origin-when-cross-origin. The provider iframe has its own policy and necessary network subresources; wrapper CSP does not claim to sandbox or suppress YouTube's internal requests. The official [IFrame API](https://developers.google.com/youtube/iframe_api_reference) creates one player with autoplay=0, playsinline=1 and untouched controls/branding. There is no play/seek/loadVideo command anywhere in the app document; native injection can only pause. Native `mediaPlaybackRequiresUserAction` is also enabled.

WebView uses incognito/no shared cookies/no third-party cookies/no cache, disables file/universal-file access, mixed content, script-created windows, PiP, AirPlay, scroll/back-forward gestures and debugging. Android content access is already false in the pinned integration. The only bridge reports version/generation, ready, known numeric state/error codes and fullscreen boolean. It schema-checks size/types/keys and ignores stale player generations. It cannot open URLs, call the API or perform privileged native actions. Android WebView may expose its message object to child frames; this is not a general trusted RPC channel and the small state-only schema is intentional.

`originWhitelist=['*']` ensures rejected schemes reach the explicit navigation policy instead of WebView's automatic system opener. Initial local HTML is fixed. Top-level navigation is confined to that document/about:blank. OS-identified child frames can load approved official embed routes. Deliberate HTTPS clicks go through the existing caught `openExternalUrl`; non-user redirects/popups/dangerous schemes are blocked. The permanent accessible Watch on YouTube link works independently of provider navigation and uses existing failure Alerts. No failure launches another app automatically.

## Native integration gaps

[WebView reference](https://github.com/react-native-webview/react-native-webview/blob/master/docs/Reference.md) and pinned native sources were reviewed. `withForumWebViewBoundary.cjs` patches node_modules during CNG (idempotent, template guards throw on upstream drift):

- Android's native DownloadManager listener bypasses onFileDownload; remove that listener completely. iOS onFileDownload does not perform a download and is a no-op.
- Android popup handling normally creates an unmanaged transient WebView even with onOpenWindow. Return false without creating it, forwarding only OS-identified user-gesture hit-test URLs. WK popup callbacks forward only LinkActivated actions. JavaScript permits only deliberate HTTPS external opening. If Android cannot provide a hit-test URL for an iframe link, the permanent Watch link is the reliable alternative.
- The pinned Android navigation overload discards isForMainFrame/hasGesture and its JS timeout normally fails open. Preserve those OS values, allow the fixed document and official child embed directly, and reject other in-WebView navigations even on timeout/interruption. This avoids turning an iframe load into a top-level load through the asynchronous compatibility path. HTTPS user clicks still notify JavaScript for the external opener.
- Android onShowCustomView/onHideCustomView and WK native video presentation callbacks forward only fullscreen state into the fixed document. Fullscreen has no public React Native event in this version. WK notifications can be broader than a single iframe, so fullscreen conservatively reserves the already mounted pool until native dismissal; the cap stays two. This requires real-device verification, especially rotation/back/focus.

Incognito settings do not promise no YouTube tracking, no provider cookies or zero storage across every WebView implementation. The OS/provider control native video presentation, back gestures, screen-reader traversal and some subresource behavior. React Native does not expose a reliable VoiceOver/TalkBack focus event for iframe controls. The external link remains accessible outside the player; keyboard focus recovery is implemented, but actual screen-reader recovery on automatic eviction must be verified and may need a native accessibility change. These are release blockers, not claims of successful device proof.

## Pool and lifecycle

The screen owns one pool. Cards report actual measured window rectangles relative to the measured FlatList viewport, on layout/content changes and scroll (32ms event throttle, animation-frame coalescing). Native row geometry updates are batched into the 100ms pool tick, avoiding remounts against partially updated row positions; callback frame stamps reject older scroll measurements. List render-ahead is not eligibility. Width below 200 stays a placeholder; reserved height is max(200, width*9/16), so the player is at least 200x200 and eviction preserves its rectangle. Original/disclosure/fallback text stays outside the active WebView; no controls are overlaid. The status slot reserves at least 100 logical pixels and expands its fixed reservation for narrow widths/large text; the Retry slot remains reserved while visually/accessibly hidden before a failure.

Preload is **300 logical pixels**, retention **600**, eviction deadline **250ms** checked by a 100ms timer (effective release 250–350ms after leaving). Reentry cancels eviction. Slots prioritize fullscreen/visible playing, then pending eviction and nearest eligible cards. At most **two** WebViews mount. A new state 1 pauses the other mounted player immediately; preload never interrupts a visible playing video. Mount/readiness never marks playing. Returning remounts without resuming.

Focus loss, actual AppState background, network loss and provider/thread teardown pause and release all players. Inactive system/native fullscreen presentation is distinguished from background. Measurements carry owner and lifecycle epoch; messages carry mount generation; recycled rows cannot revive obsolete players. Failed state survives row recycling for the lifetime of the thread and is cleared only by Retry. There is a 12-second readiness timeout, explicit errors for 100/101/150/153 and generic network/renderer failures, no automatic retry loop and no automatic external launch. Unknown/offline network starts with a placeholder.

## Native runtime and rollout

There was no runtimeVersion/OTA delivery policy or expo-updates dependency on current main. This change sets `runtimeVersion: { policy: 'fingerprint' }` using Expo's [native runtime policy](https://docs.expo.dev/eas-update/runtime-versions/). Adding WebView and the native CNG patch changes the native fingerprint. It requires new Android/iOS development/store binaries; existing binaries must never receive this JavaScript as a compatible OTA. No OTA hosting or EAS service was configured. If OTA is later enabled, preserve the fingerprint policy and validate actual native runtime matching. Old installed clients keep reading additive v1 bodies and ignore descriptors. Native ios/android output remains uncommitted. Store publication, updated privacy declarations, signed builds and submissions require separate user authorization.

## Verification matrix

Feature: `mobile.forum.thread`. Host-free descriptor/mapping/navigation/schema/pool tests plus native-patch and rendered-card/provider tests are not real playback evidence.

Run npm ci, npm run preflight, npm run audit:gate, and the TypeScript verify profile with origin/main coverage. Build unsigned Android Release and iOS Simulator Release targets from CNG. Use the maintained mobile verification helper with an isolated Testing host, never a personal forum mutation or production API. `21-forum-youtube.yaml` uses existing sample thread **1030** (official example video M7lc1UVf-VE plus unavailable video, duplicate, quote, inline and signature links). Long fixture **1029** has **30** eligible cards across 15 posts; its synthetic IDs are unavailable and must not be claimed as playable.

Capture-proof commands after a compatible Release fixture build is installed on an available dedicated device:

```
pwsh -File .cursor/skills/verify-queenzone-mobile/scripts/control-queenzone-mobile.ps1 capture-proof -Feature mobile.forum.thread -Platform android
pwsh -File .cursor/skills/verify-queenzone-mobile/scripts/control-queenzone-mobile.ps1 capture-proof -Feature mobile.forum.thread -Platform ios
```

The existing generic thread capture flow proves thread reading only. Additionally run `drive -Flow youtube` and capture recordings/network traces for every requirement below; generic proof or a screenshot of a placeholder is not enough.

| Required evidence | Android | iOS |
| --- | --- | --- |
| Real near-view load before tap, no media/audio until YouTube Play | NOT RUN | NOT RUN |
| Actual outgoing app-ID HTTPS Referer; absence of tokens/post text | NOT RUN | NOT RUN |
| Far-offscreen cards produce no provider/thumbnail/preconnect requests | NOT RUN | NOT RUN |
| Scroll thresholds, two mounted/one playing, return without resume | NOT RUN | NOT RUN |
| Play/fullscreen/rotation/back/return to same scroll position | NOT RUN | NOT RUN |
| Background/navigation cleanup with no hidden audio | NOT RUN | NOT RUN |
| Offline/timeout/private/disabled/error153/Retry/external-open failure | NOT RUN | NOT RUN |
| TalkBack/VoiceOver, large text, minimum dimensions/focus recovery | NOT RUN | NOT RUN |
| 30-card rapid-scroll peak WebViews/mount count/memory/frame comparison | NOT RUN | NOT RUN |

For performance, use the same device/OS/build and Testing fixture 1029 for repeated scroll down/up. Compare against the same fixture with only optional youtubeVideos omitted by a local test host/proxy. Collect actual mounted WebView count and mount/unmount counts, Android dumpsys meminfo/framestats or iOS Instruments allocations/frame timing, sample duration and peak/steady memory, slow frames and returning-card behavior. Retain traces/recording and report numerical differences. The deterministic 30-card test only proves scheduler bounds; no device performance numbers are available yet.

At implementation time adb devices listed no Android target. The only booted iOS target is an existing QueenZone Store iPhone16 ProMax simulator session; it was not replaced or driven. Native manual proof is therefore NOT RUN. A dedicated compatible device or explicit permission to replace that existing simulator's QueenZone install is needed. Workflow dispatch also awaits publication/CI authorization because this branch has not been pushed. This issue is not fully accepted until those checks pass.
