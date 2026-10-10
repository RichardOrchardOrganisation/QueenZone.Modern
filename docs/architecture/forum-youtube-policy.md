# Forum YouTube display policy

`ForumVideoContent.Project` runs after `UgcHtml.FormatForDisplay`. It performs no network or database operations, changes no stored bodies, and never puts a player in API HTML. Signatures, quote sources, composers and other UGC callers continue to use the original formatter.

## URLs and starts

Exact hosts: `youtube.com`, `www.youtube.com`, `m.youtube.com` accept `/watch?v=ID`, `/shorts/ID`, `/embed/ID`; `youtu.be`, `www.youtu.be` accept `/ID`. HTTP and HTTPS are recognized; generated watch URLs are HTTPS. IDs are exactly 11 ASCII letters, digits, underscores or hyphens. Unexpected ports, userinfo, relative URLs, whitespace/control characters, backslashes, escaped URL spellings (including encoded ID delimiters), dot-segment normalization, lookalike hosts and unsupported paths are rejected. Multiple `v` values are rejected, as is a query `v` combined with a path ID. Query key matching is case insensitive. Fragments are ignored.

Only one `t` or `start` value may supply the start. Accept nonnegative decimal seconds or ordered integer `h`, `m`, `s` components, such as `90`, `90s`, `1m30s`, `1h2m3s`. Maximum is 86,400 seconds (24 hours). Invalid, excessive or multiple time values are ignored while the video remains eligible. Zero equals an omitted start. No posted tracking, playlist, autoplay, origin or other parameters are propagated. A video watch URL with a playlist parameter remains a single-video card; playlist-only URLs are unsupported.

## Eligibility and mapping

A link must be the only meaningful content of a paragraph/div or a formatted plain-text line. Inline formatting wrappers and surrounding whitespace are allowed. Punctuation/prose, multiple links on one line, images, headings, lists, blockquotes, legacy `qz-bbcode-quote` blocks and code/preformatted sources do not expand. Unsupported code/pre tags are already removed with their contents by the existing sanitizer. Separate signature and quote/composer rendering is never projected.

`youtubeVideos` descriptors have `provider: "youtube"`, `videoId`, `watchUrl`, nullable `startSeconds`, and `anchorIndex`. `anchorIndex` is the zero-based position among **all** `a` elements in DOM order after parsing the response's unchanged sanitized `body`, including ineligible links and image anchors. It is scoped to that post body, never the whole page or signature. HTML entities are decoded by the DOM parser; URL text matching and user-provided attributes are unnecessary. Consumers must validate descriptor shape/provider/ID/start and map against the same body. Missing or empty metadata means render existing readable links.

The first eligible occurrence of each video ID/start pair gets a descriptor. Later duplicates remain links. At most three descriptors per post, in source order; overflow remains links. This limit is owned by the server policy. Recognition tests include HTTP, all supported hosts/paths, entities, timestamps, repeated URLs, nested formatting, hostile URL spellings, sanitizer attacks and legacy text.

Part of #1955; shared contract #1956, website #1957, future mobile #1958.

## Website player boundary

Forum-only Razor partials replace only trusted mapped anchors with first-party cards, retaining the original link and a canonical Watch on YouTube link. Quote templates keep `UgcHtml.FormatForDisplay` output. The server recognition, dedupe, three-per-post limit, API descriptors and composer/signature exclusions are unchanged. Mobile #1958 is separate.

### Near-view loading (#1973)

`youtube-scheduler.js` loads before `youtube-video.js`. Two IntersectionObservers observe each reserved card viewport: preload **300px 0px**, retention **600px 0px**. These margins, the **three-frame page pool** and **250ms eviction debounce** are constants in the pure scheduler. Visible playing frames and fullscreen frames keep their slots. Remaining slots go to eligible preload cards closest to the viewport centre; retained frames may yield a slot to a nearer candidate. Leaving retention starts a 250ms deadline; re-entry cancels it, and pending deadlines retain their slots until expiry. Pause precedes frame removal. Failed cards release their slots and require explicit Retry. Offline and viewports below 200 × 200 CSS pixels never mount.

The URL is `https://www.youtube-nocookie.com/embed/{ID}?autoplay=0&playsinline=1&enablejsapi=1&origin={origin}`, with an optional bounded `start`. Razor derives `data-qz-player-origin` from `Site:PublicBaseUrl`. JS uses it only when it exactly matches `location.origin`. A mismatch falls back to the previous single-frame click-to-load URL without API/origin parameters. Missing IntersectionObserver also falls back to single-frame click-to-load (with API/origin parameters when the origin matches). Neither fallback auto-mounts. Local Testing/E2E hosts must configure `Site__PublicBaseUrl` to their actual origin; `Run-E2E.ps1` does this for hosts it starts.

No remote thumbnail, metadata request, `iframe_api` script, preconnect or DNS-prefetch is added. Far-offscreen cards generate no YouTube requests. Only near-view eligible cards connect automatically. No posted query parameters are copied.

### Playback bridge and cleanup

The handwritten postMessage bridge sends only the `listening` handshake (`channel: "widget"`) and `pauseVideo`. It never sends play, seek, mute or volume commands. `listening` is repeated every 250ms until the frame reports a valid event or the readiness timeout fires; that is handshake, not remount or auto-retry. Incoming JSON must come from the exact nocookie origin and the `contentWindow` of a currently pooled frame. Only `onReady`, numeric `onStateChange` / `infoDelivery.playerState` states (-1, 0, 1, 2, 3, 5), and known `onError` codes (2, 5, 100, 101, 150, 153) are accepted. Malformed and spoofed messages are ignored.

Only state **1** means playing; iframe load does not. Unknown frames have no playing priority. When one reports playing, all other pooled frames receive pause. Hidden documents pause all; returning never resumes playback. `pagehide` or removal of a post containing a card releases the whole pool, disconnects observers/listeners, clears timers, and increments a generation to invalidate queued callbacks. A restored history-cache page keeps readable external links; reload to re-enable players after teardown.

Each card has idle/mounting/mounted/failed state. A 12-second readiness timeout or bridge error removes the player and shows Retry and Watch on YouTube; no automatic retry occurs. A fallback without a valid API origin can only observe load/error/timeout, not inspect cross-origin playback errors. Error 153 indicates identification/Referer failure; distinguish it from private/deleted (100) and embedding-disabled (101/150) videos.

### Security, focus and privacy

CSP is unchanged: `frame-src 'self' https://www.googletagmanager.com https://www.youtube-nocookie.com`, `frame-ancestors 'none'`, `object-src 'none'`, and the existing nonce-based script policy. The iframe retains `strict-origin-when-cross-origin`, `sandbox="allow-scripts allow-same-origin"`, `allow="encrypted-media; fullscreen"`, and its accessible title. There is no autoplay permission. YouTube controls and branding remain untouched.

The viewport reserves 16:9 space with minimum height 200px. Automatic mount/eviction does not change its dimensions or add status text that shifts the thread. Buttons and links remain in the DOM. If focus is inside a transitioning card, the permanent external link receives focus with `preventScroll`; otherwise focus is untouched. No-JS pages retain all prose and links.

One notice above the first card on each topic page says: “Videos near where you're reading load automatically from YouTube (privacy-enhanced mode) before you press Play. Loading a player connects to YouTube and shares your IP address and this site's address.” Cards say “Loads from YouTube automatically.” Privacy-enhanced mode does not mean anonymous or no tracking. **There is no consent mechanism in this codebase today.** If one is introduced, it must gate automatic loading back to click-to-load when third-party embeds are not permitted.

### Verification

Run `node --test tests/js/youtube-scheduler.test.cjs tests/js/youtube-video.test.cjs`; CI also syntax-checks both scripts in `scripts-tests.yml`. The deterministic Playwright fixture `/forum/topic/1029/archive-sample-thread-1029` contains 30 cards across 15 posts. Nocookie responses are stubbed at their real origin, with scripted player-state messages. The existing 1030 fixture retains duplicate/quote/inline/signature and pagination coverage. CSP values are pinned by `ForumVideoRenderingTests`.

Stubbed tests cannot establish real YouTube sandbox compatibility. Dinesh must attach a DEV screen recording proving bridge state, one-playing, fullscreen retention and no identification error 153. Linus reviews and Pat gates release. Desktop/mobile real playback and production promotion remain separate verification; local stubs are not deployed proof.
