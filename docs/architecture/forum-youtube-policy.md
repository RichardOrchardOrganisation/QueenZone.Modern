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

Forum-only Razor partials replace only the trusted mapped anchor with a first-party card, retaining its sanitized original markup and a canonical Watch on YouTube link. Quote templates keep `UgcHtml.FormatForDisplay` output. Cards use an inline local SVG; load controls are hidden until JavaScript is available. No remote image, API script, metadata, preconnect or DNS-prefetch is added. Reading cards never starts a player request.

An explicit Load action generates only `https://www.youtube-nocookie.com/embed/{ID}?autoplay=0&playsinline=1`, with optional bounded `start`. CSP adds that exact frame origin only. The parent script/image/connect policies stay unchanged. Referrer policy is explicitly `strict-origin-when-cross-origin` on the iframe so cross-origin requests identify the first-party origin, without post text or credentials. The frame has no parent Authorization headers or first-party account cookies. Its sandbox permits only scripts and same-origin for the official cross-origin player; it does not grant popups, top navigation, forms, downloads or native bridges. The permissions allowlist is only encrypted-media and fullscreen. No autoplay permission, camera, microphone, geolocation or clipboard capability is granted. Controls and branding are not altered or overlaid. Sandbox behavior requires separate real-browser playback proof; deterministic stubs alone cannot establish it.

Only one frame is mounted per topic page. Loading another or pressing Unload removes it. `pagehide` removes the active frame, including browser-history cache navigation. The activating button remains mounted and keeps keyboard focus; status updates are polite. The viewport reserves 16:9 space with a 200px minimum height, and activation refuses an undersized viewport while retaining external fallback. Width is fluid within the post.

A frame load event says only to use player controls; it never claims playback success. Cross-origin video failures cannot be inspected from this plain iframe. A 12-second loading hint, browser error hint and permanent external fallback cover blocked/offline/unavailable videos without hiding prose. YouTube error 153 is an identification/Referer failure, distinct from deleted/private video errors (100) or embedding-disabled errors (101/150). Report actual observed errors separately. Privacy-enhanced mode is not anonymous: after activation YouTube can collect data.
