# Privacy and compliance draft

This is a conservative draft based on the source tree and production configuration. Re-audit it against the exact release build immediately before submission.

**Updated 2026-09-08:** the app is ad-free and includes Sentry plus TelemetryDeck. TelemetryDeck receives an SDK-hashed, random installation identifier, coarse device/app metadata, and allowlisted product interactions. No Advertising or Tracking purpose applies; App Tracking Transparency is not required. Reconfirm this against the final binary.

## App privacy label

Answer **Yes, data is collected**. Do not declare tracking unless the final app or an included SDK combines QueenZone data with third-party data for advertising or measurement across other companies' apps/sites.

Likely collected data types:

| App Store data type | Linked to identity | Tracking | Purpose / source |
| --- | --- | --- | --- |
| Name | Yes | No | Member profile, contact requests |
| Email address | Yes | No | Account, authentication, contact requests |
| User ID | Yes | No | Account/session and member API operations |
| Device ID / push token | Yes | No | Push registration and notification delivery |
| Photos or videos | Yes when submitted | No | Avatar and moderated photo submissions |
| Other user content | Yes | No | Forum posts, private messages, news suggestions, contact messages |
| Product interaction | Yes | No | TelemetryDeck top-level section views; Sentry navigation/performance traces when enabled |
| Crash data | Possibly | No | Sentry crash/error reporting when enabled |
| Performance data | Possibly | No | Sentry performance tracing when enabled |

For each collected type, select only purposes actually used. Expected purposes are **App Functionality** and, for diagnostics, **Analytics**. Do not select Third-Party Advertising, Developer Advertising or Other Purposes unless the final build genuinely uses them.

Items selected by a user from their camera or photo library and uploaded to QueenZone count as collection; merely reading a local image without transmitting it would not.

## Permissions and privacy manifests

- Camera: used only when the member chooses to take a photograph for an avatar or gallery submission.
- Photo library: used only when the member chooses an image for an avatar or gallery submission.
- Notifications: request after contextual explanation; settings remain available in-app.
- No microphone/recording permission is configured.
- Confirm all included SDK privacy manifests and required-reason API declarations in the archived build.
- TelemetryDeck's published disclosure is Device ID and Product Interaction,
  used for Analytics, not linked, and not used for tracking. The app as a whole
  also links its separate push device identifier to a member for App
  Functionality, so reconcile the combined answer in App Store Connect.
- The app presents a first-run TelemetryDeck consent choice before creating an
  analytics identifier or sending signals. Signed-in and signed-out users can
  withdraw under Profile → Analytics preferences. Confirm both paths in the
  archived release build.
- Review **Coarse Location** conservatively in App Store Connect because
  TelemetryDeck derives country from the request origin. If declared, use
  Analytics purpose, not linked, and not used for tracking.

## Age rating

Recommended working assumption: **13+ or the regional equivalent**, subject to Apple's generated result.

Questionnaire considerations:

- User-generated content: Yes — forum posts and submitted material.
- Messaging/chat: Yes — private member messages.
- Profanity or crude humour: Infrequent/possible because historical community content is user-generated.
- Mature or suggestive themes: Infrequent/possible in music journalism and archive material.
- Contests, gambling, loot boxes and simulated gambling: None unless the final feature set changes.
- Unrestricted web access: Normally No; links opening a system browser do not by themselves make the app a general-purpose browser. Verify final behaviour.
- Parental controls and age assurance: None currently identified.

## User-generated content controls

Before submission, confirm the release candidate visibly supports:

- Reporting objectionable forum content.
- Blocking or restricting unwanted private contact where applicable.
- Moderation and enforcement processes.
- Published contact information for users to reach QueenZone.
- Terms that prohibit abusive or unlawful content.

## Accounts and sign-in

- Sign in with Apple must be offered wherever equivalent third-party consumer sign-in is offered, unless a guideline exception clearly applies.
- Account deletion is available in-app and at `https://www.queenzone.org/data-deletion`.
- Verify immediate deletion in the release build. Explain that outstanding blob deletion and Sign in with Apple token revocation are retried by the backend, and that older scheduled requests remain cancellable until their due date.

## Export compliance

The Expo configuration declares only exempt encryption usage. Confirm the final binary uses encryption solely through standard OS/network security and eligible SDK functionality, then answer App Store Connect consistently. If any non-exempt or proprietary cryptography is added, reassess before uploading.

## Content rights and independence

- QueenZone is an independent fan archive and must not imply official affiliation.
- Review rights for photographs, audio, artwork, logos, articles and imported legacy content.
- Ensure the store copy, screenshots and icon do not imply endorsement by Queen or its representatives.
- Fan-performance audio is member-gated; verify every recording offered in the app is authorised for distribution.

## Business and regional declarations

- Complete Digital Services Act trader/non-trader status for EU availability.
- Confirm tax category and the free price tier.
- Review South Korea, China mainland and Vietnam declarations only if distributing there and App Store Connect presents them.

